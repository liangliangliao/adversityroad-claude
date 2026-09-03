#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
CS1061 探测器：在【本仓库自己声明的】struct / class 上访问了不存在的成员。

为什么单独做一条：这次 CI 红在
    _dirs[a1].clip.name        // LocoDir 没有 clip 字段，只有 cp / angle / name
而现有五条 linter 一条都看不见它——cslint 只看括号与语句结构，csident 只看
"下划线写错"，都不理解类型。而这类错在写调试代码时最容易犯：你记得那个结构
"里面有个片段"，就顺手写了 .clip，其实存的是 AnimationClipPlayable。

覆盖范围刻意只留**一种能百分百确定类型**的受体：声明为 `List<T> _x` /
`T[] _x` 的字段，写成 `_x[任意表达式].成员`。T 必须是本仓库里唯一声明的
struct/class，且没有基类（有基类就可能继承成员，判不准，直接跳过）。

初版还判了局部变量（`T v = …` 之后的所有 `v.`）。那一条在没有作用域分析时
必然误报——同一个文件里另一处的 `var m = 某材质` 也叫 m，于是 188 个类型
里凡是有人写过 `ZoneMood m = …` 的，全文件的 `m.SetFloat` 都被判成错。
53 条里 52 条是这么来的。一个会喊狼来了的 linter 比没有 linter 更糟，删掉。
"""
import io, os, re, sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
SRC = os.path.join(ROOT, "Assets", "_Project")

def read(p): return io.open(p, encoding="utf-8", errors="replace").read()

def strip_comments(src):
    out, i, n = [], 0, len(src)
    while i < n:
        c = src[i]
        if c == '"' or c == "'":
            q = c; out.append(c); i += 1
            while i < n:
                if src[i] == '\\': out.append("  "); i += 2; continue
                out.append(src[i])
                if src[i] == q: i += 1; break
                i += 1
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

files = []
for d, _, fs in os.walk(SRC):
    for f in fs:
        if f.endswith(".cs"): files.append(os.path.join(d, f))
files.sort()

# ---------- 第一遍：收集类型声明与成员 ----------
types = {}          # 名字 -> set(成员)  （只收无基类的）
seen  = {}          # 名字 -> 声明次数（重名就放弃，判不准）
DECL = re.compile(r"\b(?:public|internal|private|protected)?\s*"
                  r"(?:static\s+|readonly\s+|sealed\s+|partial\s+)*"
                  r"\b(struct|class)\s+(\w+)\s*(:[^{\n]*)?\{")
MEMBER = re.compile(r"\b(?:public|internal|private|protected)\s+"
                    r"(?:static\s+|readonly\s+|const\s+|volatile\s+)*"
                    r"[\w<>\[\],\.\?]+\s+(\w+)\s*(?:[;=({])")

def body_of(src, open_idx):
    depth, i, n = 0, open_idx, len(src)
    while i < n:
        if src[i] == "{": depth += 1
        elif src[i] == "}":
            depth -= 1
            if depth == 0: return src[open_idx + 1:i]
        i += 1
    return ""

srcs = {}
for f in files:
    s = strip_comments(read(f))
    srcs[f] = s
    for m in DECL.finditer(s):
        kind, name, base = m.group(1), m.group(2), m.group(3)
        seen[name] = seen.get(name, 0) + 1
        if base:                      # 有基类/接口，可能继承成员——不判
            types.pop(name, None); types[name] = None; continue
        body = body_of(s, s.index("{", m.end() - 1))
        mem = set(MEMBER.findall(body))
        # 自动属性 / 方法 / 嵌套类型也算成员
        mem |= set(re.findall(r"\b(?:public|internal)\s+[\w<>\[\],\.\?]+\s+(\w+)\s*=>", body))
        mem |= set(re.findall(r"\b(?:struct|class|enum)\s+(\w+)", body))
        if name in types and types[name] is None: continue
        types[name] = mem

usable = {k: v for k, v in types.items()
          if v is not None and seen.get(k, 0) == 1 and v}

# ---------- 第二遍：找受体并核对成员 ----------
bad = 0
for f in files:
    s = srcs[f]
    rel = os.path.relpath(f, ROOT)
    # 容器字段：List<T> _x / T[] _x
    cont = {}
    for m in re.finditer(r"\b(?:List|IList|IReadOnlyList)<(\w+)>\s+(\w+)\s*[;=]", s):
        if m.group(1) in usable: cont[m.group(2)] = m.group(1)
    for m in re.finditer(r"\b(\w+)\[\]\s+(\w+)\s*[;=]", s):
        if m.group(1) in usable: cont[m.group(2)] = m.group(1)

    def check(recv_re, tname, label):
        global bad
        for m in re.finditer(recv_re + r"\s*\.\s*(\w+)", s):
            mem = m.group(m.lastindex)
            if mem in usable[tname]: continue
            line = s.count("\n", 0, m.start()) + 1
            print(u"%s:%d: %s 的 '%s' 没有成员 '%s' —— 疑似 CS1061（有 %s）"
                  % (rel, line, label, tname, mem,
                     "、".join(sorted(usable[tname])[:6])))
            bad += 1

    for name, t in cont.items():
        check(re.escape(name) + r"\s*\[[^\]\[]*\]", t, u"容器 " + name)

print(u"\n检查 %d 个文件，%d 个可判定类型，发现 %d 处问题" % (len(files), len(usable), bad))
sys.exit(1 if bad else 0)
