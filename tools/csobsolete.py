#!/usr/bin/env python3
"""CS0619：用了本 Unity 版本里【obsolete-as-error】的 API。

这类调用在编辑器里看着完全正常、本地也没有任何提示，直到 CI 编译才报错。
而且它不是"写错了"——是引擎版本把某个一直能用的 API 升级成了错误。

已经因此红过的：
  · GetInstanceID()  Unity 6000.5 起标记为 obsolete-as-error（改用自发序号即可，
    多数用法要的只是"一个唯一编号"，并不需要引擎的实例 id）

用法：python3 tools/csobsolete.py
"""
import re, sys, pathlib

# API 名 -> 该怎么办
BANNED = {
    "GetInstanceID": "Unity 6000.5 起 obsolete-as-error；要唯一编号请用自增静态序号",
}

ROOT = pathlib.Path(__file__).resolve().parent.parent / "Assets" / "_Project" / "Scripts"

def strip_comment(line):
    """去掉 // 之后的部分（不处理块注释里的误报，够用）。"""
    i = line.find("//")
    return line if i < 0 else line[:i]

def main():
    files = sorted(ROOT.rglob("*.cs"))
    hits = 0
    for f in files:
        for n, raw in enumerate(f.read_text(encoding="utf-8").splitlines(), 1):
            code = strip_comment(raw)
            for api, why in BANNED.items():
                if re.search(r"\b" + re.escape(api) + r"\s*\(", code):
                    print(f"{f}:{n}: 调用了 {api}() —— {why}")
                    hits += 1
    print(f"检查 {len(files)} 个文件，发现 {hits} 处问题")
    return 1 if hits else 0

if __name__ == "__main__":
    sys.exit(main())
