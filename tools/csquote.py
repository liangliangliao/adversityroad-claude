#!/usr/bin/env python3
"""CS1010「Newline in constant」检查：字符串/字符字面量里混进了真换行。

这一类错误 cslint（结构）和 csshadow（作用域）都查不出来，而它极容易出现在
「用脚本改源码」的场合——脚本里写的 \n 被当成真换行写进了 C# 字面量。
逐行扫描，跳过注释与逐字字符串（@"..."），行尾若仍停在字面量里即报错。
"""
import os, sys

def scan(path):
    bad = []
    in_block_comment = False
    in_verbatim = False
    for ln, line in enumerate(open(path, encoding="utf-8", errors="ignore"), 1):
        line = line.rstrip("\n").rstrip("\r")
        i, n = 0, len(line)
        state = "verbatim" if in_verbatim else None   # None / str / char
        while i < n:
            c = line[i]
            if in_block_comment:
                if line.startswith("*/", i): in_block_comment = False; i += 2
                else: i += 1
                continue
            if state == "verbatim":
                if c == '"':
                    if i + 1 < n and line[i+1] == '"': i += 2; continue
                    state = None
                i += 1
                continue
            if state == "str":
                if c == "\\": i += 2; continue
                if c == '"': state = None
                i += 1
                continue
            if state == "char":
                if c == "\\": i += 2; continue
                if c == "'": state = None
                i += 1
                continue
            # 普通代码
            if line.startswith("//", i): break
            if line.startswith("/*", i): in_block_comment = True; i += 2; continue
            if line.startswith('@"', i): state = "verbatim"; i += 2; continue
            if line.startswith('$@"', i) or line.startswith('@$"', i): state = "verbatim"; i += 3; continue
            if c == '"': state = "str"; i += 1; continue
            if c == "'": state = "char"; i += 1; continue
            i += 1
        if state in ("str", "char"):
            bad.append((ln, "字符串" if state == "str" else "字符", line.strip()[:90]))
            state = None
        in_verbatim = (state == "verbatim")
    return bad

root = sys.argv[1] if len(sys.argv) > 1 else "Assets"
files = problems = 0
for dp, _, fns in os.walk(root):
    for fn in fns:
        if not fn.endswith(".cs"): continue
        files += 1
        for ln, kind, txt in scan(os.path.join(dp, fn)):
            print("%s:%d: %s字面量在行尾未闭合 —— CS1010 Newline in constant\n    %s"
                  % (os.path.join(dp, fn), ln, kind, txt))
            problems += 1
print("检查 %d 个文件，发现 %d 处问题" % (files, problems))
sys.exit(1 if problems else 0)
