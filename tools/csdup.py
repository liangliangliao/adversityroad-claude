#!/usr/bin/env python3
"""
同作用域局部变量重复声明检查（CS0128）。

【为什么需要它】
这个仓库里没有 C# 编译器，全靠 tools/ 下这一组检查在推送前兜底。
csshadow.py 查的是"局部变量遮蔽字段"，查不到**同一个作用域里声明了两次同名局部变量**——
而后者是 error CS0128，会直接把编译打红。

这个检查就是为一次真实的失败补的：往一个方法中间插了一段 `var b = Measure(go);`，
而这个方法下面本来就有一个 `var b = Measure(go);`。lint 全绿、推上去、CI 红。

【判定方式】
按花括号维护作用域栈：进 { 压栈、出 } 弹栈。声明落在当前栈的任意一层里已经存在，
就是重复。兄弟作用域各自压弹，所以两个并列的 if 块里各声明一个同名变量不会误报。
for/foreach/using 的头部声明跳过——它们属于紧随其后的块，单独处理容易误报，
而它们也不是这类错误的常见来源。
"""
import re, sys, pathlib

DECL = re.compile(
    r'^\s*(?:var|int|uint|long|float|double|bool|byte|char|string|decimal|object'
    r'|[A-Z]\w*(?:<[^>();]*>)?(?:\[\])?)\s+([a-z_]\w*)\s*=(?!=)')
HEADER = re.compile(r'^\s*(?:for|foreach|using|while|if|switch|catch|fixed|lock)\s*\(')
METHOD = re.compile(r'^\s{4,}(?:\[[^\]]*\]\s*)?(?:public|private|protected|internal|static|'
                    r'override|virtual|sealed|async|partial|new|extern|unsafe|\s)*[\w<>,\[\]\.\?]+\s+'
                    r'\w+\s*\([^;]*\)\s*$')

def check(path):
    src = pathlib.Path(path).read_text(encoding='utf-8', errors='replace').splitlines()
    bad = []
    scopes = []          # 作用域栈，每层是 {name: line}
    depth = 0
    in_method = False
    for i, raw in enumerate(src, 1):
        line = re.sub(r'//.*$', '', raw)
        line = re.sub(r'"(?:[^"\\]|\\.)*"', '""', line)

        if not in_method and (METHOD.match(line) or re.match(r'^\s+\w.*\)\s*$', line)) \
           and '(' in line and ';' not in line:
            pass  # 方法签名行，真正入栈等下一行的 {

        opens, closes = line.count('{'), line.count('}')

        if in_method and not HEADER.match(line):
            m = DECL.match(line)
            if m:
                name = m.group(1)
                for s in scopes:
                    if name in s:
                        bad.append((i, name, s[name]))
                        break
                if scopes:
                    scopes[-1][name] = i

        for _ in range(opens):
            depth += 1
            if depth >= 3:            # namespace{ class{ method{ …
                in_method = True
                scopes.append({})
        for _ in range(closes):
            if scopes: scopes.pop()
            depth -= 1
            if depth < 3:
                in_method = False
                scopes = []
    return bad

def main():
    args = sys.argv[1:] or ["Assets/_Project/Scripts"]
    files = []
    for a in args:
        p = pathlib.Path(a)
        files += [p] if p.is_file() else sorted(p.rglob("*.cs"))
    n = 0
    for f in files:
        for line, name, first in check(f):
            print(f"{f}:{line}: 局部变量 '{name}' 与第 {first} 行的声明同作用域重复 —— CS0128")
            n += 1
    print(f"\n检查 {len(files)} 个文件，发现 {n} 处问题")
    return 1 if n else 0

if __name__ == "__main__":
    sys.exit(main())
