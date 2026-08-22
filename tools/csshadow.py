#!/usr/bin/env python3
"""查 CS0136：同一个方法里，内层作用域重新声明了外层已有的局部名。
注意 C# 的这条规则**不看先后**——外层声明写在后面照样报错，
所以不能只比较"先声明的"，必须按作用域的包含关系判断。"""
import re, sys, glob
sys.path.insert(0, '/tmp/claude-0/-home-user-adversityroad-claude/32d97a98-14db-5ec7-af0c-5a2a21afba47/scratchpad')
from cslint import blank_out

TYPES = r'(?:var|float|int|bool|string|double|long|byte|char|uint|short|object)'
DECL = re.compile(
    r'(?:^|[;{}()\n]|\bfor\s*\(|\bforeach\s*\()\s*'
    r'(?:' + TYPES + r'|[A-Z][\w<>,\.\[\]]*)\s+'
    r'([a-z_]\w*)\s*(?:=[^=]|;|\)|\bin\b)')

def scan(path, code):
    s = blank_out(code)
    depth = 0
    path_stack = []        # 每层一个块 id
    counter = 0
    decls = {}             # name -> [ (block_path_tuple, char_index) ]
    i = 0
    # 先记录每个位置的块路径
    positions = []
    for i, ch in enumerate(s):
        if ch == '{':
            counter += 1
            path_stack.append(counter)
            depth += 1
        elif ch == '}':
            if path_stack: path_stack.pop()
            depth -= 1
        positions.append((tuple(path_stack), depth))

    for m in DECL.finditer(s):
        name = m.group(1)
        idx = m.start(1)
        blk, d = positions[idx]
        if d < 3:            # 类字段/属性层，不是局部变量
            continue
        decls.setdefault(name, []).append((blk, idx))

    problems = []
    for name, lst in decls.items():
        for a in range(len(lst)):
            for b in range(a + 1, len(lst)):
                p1, i1 = lst[a]; p2, i2 = lst[b]
                if p1 == p2:
                    continue                      # 同一块内重复声明，编译器另有报错
                # 一个是另一个的祖先作用域 → CS0136
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
