#!/usr/bin/env python3
"""查 CS0136：同一个方法里，内层作用域重新声明了外层已有的局部名。

两条容易踩空的 C# 规则：
  ① **不看先后**。外层的声明写在后面，前面内层那个照样报错——编译器比的是
     作用域的包含关系，不是文本顺序。ThirdPersonCamera 就栽在这上面：
     LateUpdate 第 2017 行新加了个 `aim`，把第 854 行块里那个合法的 `aim`
     顶成了 CS0136，而错误行号指的是**第 854 行那个没动过的旧代码**。
  ② **for / foreach / using 头部声明的变量，作用域是那条语句本身**，
     不是它所在的块。所以
         for (int x = ...) Foo(x);
         for (int i = ...) { float x = ...; }
     完全合法。这个版本之前把头部变量记在外层块上，于是全库刷出 46 条误报，
     只能被踢出 CI——一个会喊狼来了的检查等于没有检查。

作用域模型：一个位置的作用域路径 = 从方法体往里、每进一层块或一条带头部
声明的语句就压一个唯一 id。头部声明本身记在它压出来的那层上，于是它跟
语句体内的声明是祖先关系（真会报 CS0136），跟兄弟语句里的则互不相干。
"""
import re, sys, glob, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cslint import blank_out

TYPES = r'(?:var|float|int|bool|string|double|long|byte|char|uint|short|object)'
DECL = re.compile(
    r'(?:^|[;{}()\n]|\bfor\s*\(|\bforeach\s*\()\s*'
    r'(?:' + TYPES + r'|[A-Z][\w<>,\.\[\]]*)\s+'
    r'([a-z_]\w*)\s*(?:=[^=]|;|\)|\bin\b)')

HEADER = re.compile(r'\b(for|foreach|using|fixed)\s*\(')


def match_paren(s, i):
    """i 指向 '('，返回配对 ')' 的下标；找不到返回 -1。"""
    d = 0
    while i < len(s):
        if s[i] == '(': d += 1
        elif s[i] == ')':
            d -= 1
            if d == 0: return i
        i += 1
    return -1


def statement_end(s, i):
    """i 指向语句体的第一个非空白字符，返回这条语句结束后的下标（不含）。"""
    if i >= len(s): return len(s)
    if s[i] == '{':                      # 带花括号：找配对的 '}'
        d = 0
        while i < len(s):
            if s[i] == '{': d += 1
            elif s[i] == '}':
                d -= 1
                if d == 0: return i + 1
            i += 1
        return len(s)
    # 无花括号：语句体可能是 `Foo();`，也可能是另一条复合语句
    # （`for (...) { ... }` / `if (...) { ... } else { ... }`）。
    # 前者到分号为止；后者到它自己那对花括号闭合为止——早期版本只找分号，
    # 于是一路吃掉了后面并列的兄弟语句，把兄弟误判成了嵌套。
    pd = bd = 0
    while i < len(s):
        c = s[i]
        if c == '(': pd += 1
        elif c == ')': pd -= 1
        elif c == '{': bd += 1
        elif c == '}':
            bd -= 1
            if bd < 0: return i          # 闭的是外层块，语句到此为止
            if bd == 0:
                j = i + 1
                while j < len(s) and s[j] in ' \t\r\n': j += 1
                if s[j:j + 4] == 'else' and not (s[j + 4:j + 5] or ' ').isalnum():
                    i = j + 4            # else 分支仍属同一条语句
                    continue
                return i + 1
        elif c == ';' and pd == 0 and bd == 0:
            return i + 1
        i += 1
    return len(s)


def scope_paths(s):
    """返回与 s 等长的列表：每个位置的 (作用域路径, 块深度)。

    带头部声明的语句（for/foreach/using/fixed）会额外压一层，
    这层从头部的 '(' 开始，到语句体结束为止——正好是 C# 给它的作用域。
    """
    opens = {}          # 下标 -> 要压的层数
    closes = {}         # 下标 -> 要弹的层数
    for m in HEADER.finditer(s):
        lp = m.end() - 1
        rp = match_paren(s, lp)
        if rp < 0: continue
        j = rp + 1
        while j < len(s) and s[j] in ' \t\r\n': j += 1
        end = statement_end(s, j)
        opens[m.start()] = opens.get(m.start(), 0) + 1
        closes[end] = closes.get(end, 0) + 1

    positions = []
    stack = []
    counter = 0
    depth = 0
    for i, ch in enumerate(s):
        for _ in range(closes.get(i, 0)):
            if stack: stack.pop()
        for _ in range(opens.get(i, 0)):
            counter += 1
            stack.append(counter)
        if ch == '{':
            counter += 1
            stack.append(counter)
            depth += 1
        elif ch == '}':
            if stack: stack.pop()
            depth -= 1
        positions.append((tuple(stack), depth))
    return positions


def scan(path, code):
    s = blank_out(code)
    positions = scope_paths(s)

    decls = {}
    for m in DECL.finditer(s):
        idx = m.start(1)
        blk, d = positions[idx]
        if d < 3:            # 类字段/属性层，不是局部变量
            continue
        decls.setdefault(m.group(1), []).append((blk, idx))

    problems = []
    for name, lst in decls.items():
        for a in range(len(lst)):
            for b in range(a + 1, len(lst)):
                p1, i1 = lst[a]; p2, i2 = lst[b]
                if p1 == p2:
                    continue                      # 同一块内重复声明，编译器另有报错
                if p1[:len(p2)] == p2 or p2[:len(p1)] == p1:
                    line = code.count('\n', 0, max(i1, i2)) + 1
                    other = code.count('\n', 0, min(i1, i2)) + 1
                    problems.append((line, name, other))
    return problems


files = sys.argv[1:] or glob.glob('Assets/**/*.cs', recursive=True)
bad = 0
for f in files:
    code = open(f, encoding='utf-8').read()
    for line, name, other in sorted(set(scan(f, code))):
        print(f"{f}:{line}: 局部名 '{name}' 与外层作用域（第 {other} 行）重名 —— CS0136")
        bad += 1
print(f"\n检查 {len(files)} 个文件，发现 {bad} 处问题")
sys.exit(1 if bad else 0)
