#!/usr/bin/env python3
"""C# 字符串字面量的两类错误。

一、CS1010「Newline in constant」：字符串/字符字面量里混进了真换行。
    cslint（结构）和 csshadow（作用域）都查不出来，而它极容易出现在
    「用脚本改源码」的场合——脚本里写的 \n 被当成真换行写进了 C# 字面量。
    逐行扫描，跳过注释与逐字字符串（@"..."），行尾若仍停在字面量里即报错。

二、CS1003：中文文案里用了 **ASCII 双引号**，把字符串提前闭合又重开。
        RaiseSubtitle("〔最后检查者〕"你确定这就能交了吗"——箱子被推回了检查区。");
    这一行的引号数是**偶数**，行尾是平衡的，所以第一条检查看不见它；
    但编译器看到的是 字符串 + 裸标识符 + 字符串，报 CS1003。
    这是一次真实的红 CI（3ff9a5c）。写中文引号时必须用「」『』，不能用 "。
    判据很紧：**非逐字字符串的闭合引号后面紧跟一个中日韩/全角字符**——
    合法 C# 里不可能出现这种写法（拼接一定有 + 或 ) 或 ; 隔开）。
"""
import os, sys

def is_cjk(ch):
    o = ord(ch)
    return (0x2E80 <= o <= 0x9FFF          # 部首、汉字
            or 0x3000 <= o <= 0x303F        # 、。〔〕「」等全角标点
            or 0xFF00 <= o <= 0xFF60        # 全角字母数字与标点
            or 0xFE30 <= o <= 0xFE4F)       # 兼容形式（——、……的一部分）


def scan(path):
    bad = []
    early = []
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
                if c == '"':
                    state = None
                    # 闭合引号后面紧跟中文/全角字符 = 这个引号本该是「」
                    if i + 1 < n and is_cjk(line[i + 1]):
                        early.append((ln, line.strip()[:90]))
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
    return bad, early

root = sys.argv[1] if len(sys.argv) > 1 else "Assets"
files = problems = 0
for dp, _, fns in os.walk(root):
    for fn in fns:
        if not fn.endswith(".cs"): continue
        files += 1
        bad, early = scan(os.path.join(dp, fn))
        for ln, kind, txt in bad:
            print("%s:%d: %s字面量在行尾未闭合 —— CS1010 Newline in constant\n    %s"
                  % (os.path.join(dp, fn), ln, kind, txt))
            problems += 1
        for ln, txt in early:
            print("%s:%d: 字符串被 ASCII 双引号提前闭合（后面紧跟中文）—— 疑似 CS1003，"
                  "中文引号请用「」\n    %s" % (os.path.join(dp, fn), ln, txt))
            problems += 1
print("检查 %d 个文件，发现 %d 处问题" % (files, problems))
sys.exit(1 if problems else 0)
