#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
CS0117 探测器：把成员访问写到了**同名的另一个类型**上。

为什么单独做一条：这次编译红在
    InternalProps.ResetSession()      // 它其实声明在 InternalProp 上
而全套 lint 一条都没看见——InternalProps（静态工具类）和 InternalProp
（MonoBehaviour）只差一个字母，还住在同一个文件里。csmember 只认
`List<T> _x` / `T[] _x` 这一种能百分百确定类型的受体，看不到静态受体。

这一条覆盖的是另一种同样百分百确定的受体：**受体本身就是类型名**。
`Foo.Bar` 里的 Foo 如果是本仓库里唯一声明的类型，那 Bar 必须在 Foo 上，
不需要任何作用域分析——这正是它误报率低的原因。

刻意跳过的情况（宁可漏，不可喊狼来了——见 csmember 抬头那一段）：
· 有基类/接口的类型：成员可能继承来的，判不准。
· 重名类型：判不准。
· 和局部变量/命名空间段重名的类型：判不准。
· partial 类：多处声明的成员并集起来算。
"""
import io, os, re, sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
SRC = os.path.join(ROOT, "Assets", "_Project")


def strip(src):
    """去掉注释与字符串字面量，位置用空格占住，行号不变。"""
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
        if c == "'":
            out.append(' '); i += 1
            while i < n:
                if src[i] == '\\': out.append("  "); i += 2; continue
                if src[i] == "'": out.append(' '); i += 1; break
                out.append(' '); i += 1
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

DECL = re.compile(
    r"\b(?:public|internal|private|protected)\s+"
    r"(?:(?:static|readonly|abstract|sealed|partial|unsafe|new)\s+)*"
    r"(class|struct|enum|interface)\s+(\w+)\s*(?:<[^>{]*>)?\s*(:|\{)")

KEYWORDS = set("""public internal private protected static readonly const virtual override
abstract sealed new async extern unsafe partial event implicit explicit volatile
return if else for foreach while do switch case break continue throw using lock
yield in out ref params this base true false null typeof nameof""".split())


def member_names(body):
    """把类体里深度 1 的成员名取出来。

    不用一条正则：成员的**类型**可以带空格和括号
    （`Action<float, float>`、`(string label, int count)`、`System.Func<A, B, C>`），
    初版正则的字符类不含空格，于是把 GameEvents 的六个事件、FailureLog 的三个
    元组方法全判成了"不存在的成员"——16 处全是误报。
    csmember 抬头那句"会喊狼来了的 linter 比没有更糟"说的就是这个，
    所以这里按括号深度扫，而不是靠正则猜形状。
    """
    names = set()
    i, n = 0, len(body)
    brace = 0
    while i < n:
        c = body[i]
        if c == '{': brace += 1; i += 1; continue
        if c == '}': brace -= 1; i += 1; continue
        if not (c.isalpha() or c == '_'): i += 1; continue

        j = i
        while j < n and (body[j].isalnum() or body[j] == '_'): j += 1
        word = body[i:j]
        # 只认类体本层（深度 1）上的成员声明
        if brace != 1 or word not in ("public", "internal", "protected"):
            i = j; continue

        # 从修饰符往后扫到终结符，终结符必须在括号/尖括号深度 0 上
        k, depth = j, 0
        name = ""
        while k < n:
            ch = body[k]
            if ch in "(<[":
                # 深度 0 上的 '('：前面紧挨着标识符才算"方法名后的左括号"
                if ch == '(' and depth == 0:
                    before = body[:k].rstrip()
                    m = re.search(r"(\w+)$", before)
                    if m and m.group(1) not in KEYWORDS:
                        name = m.group(1); break
                depth += 1; k += 1; continue
            if ch in ")>]":
                depth -= 1; k += 1; continue
            if depth == 0 and ch in ";={":
                before = body[:k].rstrip()
                m = re.search(r"(\w+)$", before)
                if m and m.group(1) not in KEYWORDS: name = m.group(1)
                break
            if ch == '}':
                break
            k += 1
        if name: names.add(name)
        i = j
    return names


kinds, members, count, based = {}, {}, {}, set()

def body_of(src, brace_at):
    depth = 0
    for j in range(brace_at, len(src)):
        if src[j] == '{': depth += 1
        elif src[j] == '}':
            depth -= 1
            if depth == 0: return src[brace_at:j]
    return src[brace_at:]

for f in files:
    src = strip(io.open(f, encoding="utf-8", errors="replace").read())
    for m in DECL.finditer(src):
        kind, name = m.group(1), m.group(2)
        count[name] = count.get(name, 0) + 1
        kinds[name] = kind
        if m.group(3) == ':':
            based.add(name)                      # 有基类/接口：可能继承成员
            brace = src.find('{', m.end())
        else:
            brace = m.end() - 1
        if brace < 0: continue
        body = body_of(src, brace)
        got = members.setdefault(name, set())
        if kind == "enum":
            for mm in re.finditer(r"(\w+)\s*(?:=[^,}]*)?\s*(?:,|$)", body, re.M):
                got.add(mm.group(1))
        else:
            got.update(member_names(body))
            got.add(name)                        # 构造函数 / 嵌套同名

# 和命名空间段、局部变量重名的类型名一概不判
NS = set()
LOCAL = set()
for f in files:
    src = strip(io.open(f, encoding="utf-8", errors="replace").read())
    for mm in re.finditer(r"\bnamespace\s+([\w\.]+)", src): NS.update(mm.group(1).split('.'))
    for mm in re.finditer(r"\busing\s+(?:static\s+)?([\w\.]+)\s*;", src): NS.update(mm.group(1).split('.'))
    for mm in re.finditer(r"\b(?:var|foreach\s*\(\s*var)\s+([A-Z]\w*)\s*[=)]", src): LOCAL.add(mm.group(1))

bad = 0
REF = re.compile(r"(?<![\w\.])([A-Z]\w+)\s*\.\s*([A-Za-z_]\w*)")
for f in files:
    raw = io.open(f, encoding="utf-8", errors="replace").read()
    src = strip(raw)
    for m in REF.finditer(src):
        t, mem = m.group(1), m.group(2)
        if t not in members: continue
        if count.get(t, 0) != 1: continue          # 重名，判不准
        if t in based: continue                    # 可能继承，判不准
        if t in NS or t in LOCAL: continue         # 和命名空间段/局部变量重名
        if mem in members[t]: continue
        line = src[:m.start()].count("\n") + 1
        print("%s:%d: %s.%s —— %s 上没有 '%s'，疑似 CS0117（写到同名的另一个类型上了）"
              % (os.path.relpath(f, ROOT), line, t, mem, t, mem))
        bad += 1

print("\n检查 %d 个文件，发现 %d 处可疑" % (len(files), bad))
sys.exit(1 if bad else 0)
