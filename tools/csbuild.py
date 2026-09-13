#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""用真正的 C# 编译器（Roslyn）做一遍**语法**检查。

【为什么需要它：正则门禁连着三次没挡住编译错误】
这个仓库的 13 条 lint 全是正则/括号扫描，不解析 C# 语义。它们连着漏掉了：
  · CS1003  中文文案里用了 ASCII 双引号，字符串提前闭合（3ff9a5c）
  · CS0103  漏了 using，类型名解析不到（3574398）
  · CS0136  参数和局部变量同名（3574398）
每次都是"十几条 lint 全绿、玩家那边编译报错"，这是最坏的一种情况。
再加一条正则也挡不住第四类，所以换成真编译器。

【它能挡什么、不能挡什么——这条必须说准】
这台机器上没有 Unity，UnityEngine.* 一律解析不到。实测下来：

  能挡：**所有语法错误**（CS1xxx）。解析是先于类型绑定的，不依赖引用。
        上面那条 CS1003 回插验证过，一次就抓出来。

  挡不住：**需要类型绑定的错误**（CS0103 漏 using、CS0136 作用域冲突……）。
        因为类里的字段/基类是 Unity 类型，绑定在第一步就失败，
        方法体根本不会被分析，那些诊断压根不会产生。
        这两条已经回插验证过——报 0 处。**不要以为它跑绿了就等于能编译。**

要挡住第二类，只有拿到 Unity 的引用程序集（这台机器上没有），
或者就让 CI 的 Unity 构建去挡——那是它现在唯一的可靠来源。

【没有 Unity 引用造成的噪音怎么滤】
CS0246/CS0103 这两类是"名字解析不到"，绝大多数是 Unity 类型，属噪音。
但如果解析不到的名字**是本仓库自己声明的类型**，那就是漏了 using，算真错误——
这条规则留着，一旦哪天有了 Unity 引用它就能生效（现在轮不到它，见上）。
其余所有错误码一律当真错误。
"""
import glob, os, re, subprocess, sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
SRC = os.path.join(ROOT, "Assets", "_Project")

# 只有这两类可能是"没有 Unity 引用"造成的噪音
UNRESOLVED = {"CS0246", "CS0103"}


def find(pattern, *roots):
    for r in roots:
        hits = glob.glob(os.path.join(r, pattern), recursive=True)
        if hits:
            return sorted(hits)[-1]
    return None


def repo_types():
    """本仓库自己声明的类型名。"""
    names = set()
    decl = re.compile(r"\b(?:class|struct|enum|interface|record)\s+(\w+)")
    for dp, _, fns in os.walk(SRC):
        for fn in fns:
            if not fn.endswith(".cs"):
                continue
            with open(os.path.join(dp, fn), encoding="utf-8", errors="replace") as f:
                for m in decl.finditer(f.read()):
                    names.add(m.group(1))
    return names


def main():
    csc = find("sdk/*/Roslyn/bincore/csc.dll", "/usr/lib/dotnet", "/usr/share/dotnet")
    refdir = find("packs/Microsoft.NETCore.App.Ref/*/ref/net*", "/usr/lib/dotnet", "/usr/share/dotnet")
    if not csc or not refdir:
        print("跳过：这台机器上没有 .NET SDK（装法：apt-get install -y dotnet-sdk-8.0）")
        return 0

    files = []
    for dp, _, fns in os.walk(SRC):
        files += [os.path.join(dp, f) for f in fns if f.endswith(".cs")]
    files.sort()

    cmd = ["dotnet", csc, "-target:library", "-out:/tmp/csbuild.dll",
           "-nologo", "-langversion:latest", "-nostdlib+"]
    cmd += ["-r:" + p for p in sorted(glob.glob(os.path.join(refdir, "*.dll")))]
    cmd += files

    # Roslyn 把诊断写在 **stdout** 上，不是 stderr。
    # 第一版只读了 stderr，于是它一条错误都看不见、永远报"0 处"——
    # 那正是这个文件要消灭的那种"门禁说没事"。两路都收。
    r = subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True, timeout=900)
    out = (r.stdout or "") + "\n" + (r.stderr or "")

    line_re = re.compile(r"^(.*?)\((\d+),(\d+)\): error (CS\d+): (.*)$")
    name_re = re.compile(r"[Tt]he (?:type or namespace )?name '(\w+)'")
    mine = repo_types()

    real, noise = [], 0
    seen = set()
    for ln in out.splitlines():
        m = line_re.match(ln.strip())
        if not m:
            continue
        path, row, col, code, msg = m.groups()
        if code in UNRESOLVED:
            nm = name_re.search(msg)
            # 解析不到的名字若是本仓库声明的类型 → 漏了 using，是真错误
            if not (nm and nm.group(1) in mine):
                noise += 1
                continue
        key = (path, row, code, msg)
        if key in seen:
            continue
        seen.add(key)
        real.append("%s:%s:%s: error %s: %s" % (path, row, col, code, msg))

    for r in real:
        print(r)
    print("\n编译检查 %d 个文件：真错误 %d 处（另跳过 %d 条无 Unity 引用造成的噪音）"
          % (len(files), len(real), noise))
    return 1 if real else 0


if __name__ == "__main__":
    sys.exit(main())
