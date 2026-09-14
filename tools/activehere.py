#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
关卡系统之外，不许读 InternalLevelRunner.Active。

【这条 linter 是被一次"功能被悄悄关掉"换来的】
Active 是个生命周期标志，它会漏：有三条路只清"人在场景里"的状态、不收 runner
——阵亡、从关卡选择面板直接跳走、重载新局。走过任何一条之后 Active 仍然非空。

我拿它当成"玩家现在在第 9-26 章的关卡里"来用，写了一道
"这批关卡不弹言语攻防"的闸。漏了之后那道闸就**永远关着**：
经典关卡的言语攻防再也不弹，敌人还会开口说第 9 章的台词。
玩家原话："经典关卡中原有的言语攻击弹框被关闭了？请恢复！"

这类错误最难查的地方在于它**不报错、不崩溃、也不是一直错**——
只有走过那三条路之一，游戏的另一半才悄悄少掉一块。

所以规矩定死：
  · InternalOS 内部可以用 Active（那里的机关各自按 levelId 对得上才动手，
    而且随场景一起销毁，不存在"人已经走了它还在"的问题）；
  · 其余任何地方一律用 ActiveHere —— 它问的是**玩家此刻站在哪儿**，
    而 SiteGate 的进出状态在每一条离开路径上都会被清，不会残留。
"""
import io, os, re, sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
SRC = os.path.join(ROOT, "Assets", "_Project")
# 允许直接用 Active 的地方：关卡系统自己
INSIDE = "Assets/_Project/Scripts/InternalOS/"
PAT = re.compile(r"InternalLevelRunner\s*\.\s*Active\b(?!Here)")


def strip(src):
    """去掉注释与字符串，位置用空格占住，行号不变。"""
    out, i, n = [], 0, len(src)
    while i < n:
        c = src[i]
        if c == '"':
            out.append(' '); i += 1
            while i < n:
                if src[i] == '\\': out.append("  "); i += 2; continue
                if src[i] == '"': out.append(' '); i += 1; break
                out.append("\n" if src[i] == "\n" else " "); i += 1
            continue
        if src.startswith("//", i):
            while i < n and src[i] != "\n": out.append(" "); i += 1
            continue
        if src.startswith("/*", i):
            while i < n and not src.startswith("*/", i):
                out.append("\n" if src[i] == "\n" else " "); i += 1
            out.append("  "); i += 2
            continue
        out.append(c); i += 1
    return "".join(out)


def main():
    files = []
    for d, _, fs in os.walk(SRC):
        for f in fs:
            if f.endswith(".cs"):
                files.append(os.path.join(d, f))
    files.sort()

    bad = 0
    for path in files:
        rel = os.path.relpath(path, ROOT).replace(os.sep, "/")
        if rel.startswith(INSIDE):
            continue
        code = strip(io.open(path, encoding="utf-8", errors="replace").read())
        for m in PAT.finditer(code):
            line = code.count("\n", 0, m.start()) + 1
            print("%s:%d: 关卡系统之外读了 InternalLevelRunner.Active。" % (rel, line))
            print("    它是生命周期标志，会漏（阵亡 / 关卡选择跳走 / 重载之后仍然非空），")
            print("    拿它当「玩家在第 9-26 章关卡里」用，会把功能悄悄关在外面。")
            print("    改用 ActiveHere：它问的是玩家此刻站在哪儿。")
            bad += 1

    print("\n检查 %d 个文件，关卡系统之外误用 Active %d 处" % (len(files), bad))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
