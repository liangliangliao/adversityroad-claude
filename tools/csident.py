#!/usr/bin/env python3
"""CS0103「The name 'x' does not exist」的近似检查：**受体标识符**没在本文件里声明过。

本地没有 C# 工具链（更没有 UnityEngine.dll），做不了真正的名字解析。
但下面这个极窄的启发式能抓住"用脚本改源码"最常犯的一类错：
把 `_anim.Foo()` 抄成了 `anim.Foo()`——两个文件字段名不同，锚点却一样。

判据故意收得很紧，只看形如 `ident.` 的**小写开头**受体（本项目里类型一律
PascalCase，所以小写开头基本就是字段/局部/参数），并且只在该标识符
**整个文件里从未以任何声明形式出现过**时才报。
噪声仍然会有（跨文件的 using static、部分类），当作改动文件的提醒用。
"""
import os, re, sys

KEYWORDS = {
    "if","else","for","foreach","while","do","switch","case","return","new","this","base",
    "true","false","null","var","using","namespace","class","struct","enum","interface",
    "public","private","protected","internal","static","readonly","const","void","in","out",
    "ref","is","as","try","catch","finally","throw","yield","break","continue","default",
    "get","set","value","string","int","float","bool","double","long","byte","char","object",
    "params","lock","await","async","when","where","sizeof","typeof","nameof","checked",
}

DECL = [
    # 字段 / 属性 / 局部：类型 名字 = 或 ;
    r"\b[A-Za-z_][\w<>\[\],\.\?]*\s+([a-z_]\w*)\s*(?:=|;|,|\)|\{)",
    r"\bvar\s+([a-z_]\w*)\s*=",
    r"\bforeach\s*\(\s*[\w<>\[\],\.\?]+\s+([a-z_]\w*)\s+in\b",
    r"\bout\s+(?:var\s+)?[\w<>\[\],\.\?]*\s*([a-z_]\w*)\s*[\),]",
    r"\(\s*[\w<>\[\],\.\?]+\s+([a-z_]\w*)\s*[,\)]",          # 参数
    r"\b([a-z_]\w*)\s*=>",                                    # lambda 形参
]

def declared(text):
    names = set()
    for pat in DECL:
        for m in re.finditer(pat, text):
            names.add(m.group(1))
    return names

def check(path):
    text = open(path, encoding="utf-8", errors="ignore").read()
    # 去掉注释与字符串，避免误取
    stripped = re.sub(r"//[^\n]*", "", text)
    stripped = re.sub(r"/\*.*?\*/", "", stripped, flags=re.S)
    stripped = re.sub(r'@"(?:[^"]|"")*"', '""', stripped)
    stripped = re.sub(r'"(?:\\.|[^"\\])*"', '""', stripped)
    names = declared(stripped)
    bad = []
    for m in re.finditer(r"(?<![\w\.])([a-z_]\w*)\s*\.\s*[A-Za-z_]", stripped):
        ident = m.group(1)
        if ident in KEYWORDS or ident in names: continue
        # 只报**差一点点就对**的：本文件里存在 _x / x 这样的近邻声明。
        # 不加这道门槛全库有 500+ 误报（transform、gameObject、assetPath 这类
        # 继承来的成员本文件里当然没有声明），那样的清单没人会看。
        near = [n for n in names if n == "_" + ident or "_" + n == ident]
        if not near: continue
        line = stripped[:m.start()].count("\n") + 1
        bad.append((line, ident, near[0]))
    return bad

root = sys.argv[1] if len(sys.argv) > 1 else "Assets"
files = problems = 0
for dp, _, fns in os.walk(root):
    for fn in sorted(fns):
        if not fn.endswith(".cs"): continue
        p = os.path.join(dp, fn); files += 1
        for line, ident, near in check(p):
            print("%s:%d: 受体 '%s' 未声明，但本文件里有 '%s' —— 疑似 CS0103（写错了下划线）"
                  % (p, line, ident, near))
            problems += 1
print("检查 %d 个文件，发现 %d 处可疑" % (files, problems))
