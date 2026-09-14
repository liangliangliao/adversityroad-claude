#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
CS0841 探测器：在声明之前用了同一个块里的局部变量。

【为什么值得单独做一条】
这一轮我连着两次让"局部变量作用域"类的错误跑到 CI：
  · CS0103 —— 重写落点逻辑时删掉了 center，后面的代码还在用它；
  · CS0841 —— 把 replay 的判据换个地方之后，声明留在了第二处，
             而第一处（MarkDone）在它前面。
两次都是 push → 等二十分钟 → 看日志。而本机的 csbuild 挡不住：
没有 Unity DLL，方法体根本不会被绑定，只报得出 CS1xxx 那一类语法错。

CS0103 需要完整的符号表才判得准（我试过，161 处误报，弃了）。
但 CS0841 **不需要**：只要一个名字在这个块里被声明为局部变量，
那么同一个块（或它的嵌套块）里、位置在声明之前的任何一次同名使用，
都是编译错误——不管外层有没有同名字段，C# 的简单名解析规则都会先撞上这个局部。
所以这一条不必知道类型，只靠文本位置就能判，误报面天然很窄。

【刻意跳过的情况】（宁可漏，不可喊狼来了）
· 类体里的声明（那是**字段**，不受顺序约束——方法可以用后面才写的字段）；
· 成员访问 `x.name` 里的 name（`\b` 会被点号当成词边界，不去掉就是一片误报）；
· 对象初始化器里的键 `new T { name = ... }`（那是属性名，不是这个局部）；
· 具名实参 `name: value`；
· 括号里的声明（for / foreach / using / catch 的头部）：那是各自的作用域；
· 早出现的那一次若落在某个**嵌套块**里，而那个嵌套块自己也声明了同名局部——
  那是另一码事（CS0136 或合法的同名遮蔽），不在这一条的射程内；
· nameof(x) 里的名字不算使用。
"""
import io, os, re, sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
SRC = os.path.join(ROOT, "Assets", "_Project")


def strip(src):
    """去掉注释与字符串字面量，位置用空格占住，行号与偏移都不变。"""
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


# 语句开头的局部声明并带初始化：`var x = ...` / `bool x = ...` / `List<int> x = ...`
DECL = re.compile(
    r"(?:^|[;{}])\s*"
    r"(var|[A-Za-z_][\w.]*(?:<[\w\s,<>\[\]\.]*>)?(?:\[\])?)"
    r"\s+([a-z_]\w*)\s*=(?!=)")

KEYWORDS = {
    "return", "else", "case", "new", "await", "throw", "yield", "in", "is", "as",
    "out", "ref", "default", "base", "this", "if", "while", "for", "foreach",
    "switch", "do", "using", "lock", "checked", "unchecked", "goto", "break",
    "continue", "when", "and", "or", "not",
}


def paren_depth_at(code, pos):
    """这个位置处在几层圆括号里（用来跳过 for/foreach/using 头部的声明）。"""
    d = 0
    for ch in code[:pos]:
        if ch == '(': d += 1
        elif ch == ')': d -= 1
    return d


def block_start(code, pos):
    """包住 pos 的那个 `{` 的位置；找不到返回 -1。"""
    depth = 0
    i = pos - 1
    while i >= 0:
        c = code[i]
        if c == '}': depth += 1
        elif c == '{':
            if depth == 0: return i
            depth -= 1
        i -= 1
    return -1


TYPE_HEAD = re.compile(
    r"\b(?:class|struct|interface|record|enum|namespace)\b[^{};()]*$")


def is_type_body(code, brace_pos):
    """这个 `{` 是类型体（类/结构/接口/枚举/命名空间）的开括号吗。

    是的话，里面那一层的声明是**字段**：字段不受书写顺序约束，
    方法里用一个写在后面的字段完全合法。不排掉这一类，报出来的全是误报。
    """
    head = code[max(0, brace_pos - 400):brace_pos]
    return TYPE_HEAD.search(head) is not None


def nested_redeclares(code, seg, name):
    """这一段里有没有某个嵌套块自己又声明了同名局部（那就不归这一条管）。"""
    for m in DECL.finditer(seg):
        if m.group(2) == name:
            return True
    return False


def scan(path):
    raw = io.open(path, encoding="utf-8", errors="replace").read()
    code = strip(raw)
    hits = []

    for m in DECL.finditer(code):
        typ, name = m.group(1), m.group(2)
        if typ in KEYWORDS or name in KEYWORDS:
            continue
        decl_at = m.start(2)
        # for / foreach / using / catch 的头部：各自的作用域，不在射程内
        if paren_depth_at(code, decl_at) > 0:
            continue
        b = block_start(code, decl_at)
        if b < 0 or is_type_body(code, b):
            continue

        seg = code[b + 1:m.start()]          # 本块里、声明之前的那一段
        for u in re.finditer(r"\b" + re.escape(name) + r"\b", seg):
            before = seg[max(0, u.start() - 12):u.start()]
            after = seg[u.end():u.end() + 3]
            # 成员访问 x.name：`\b` 把点号当词边界，不排掉就是一片误报
            if before.rstrip().endswith(".") or before.rstrip().endswith("?."):
                continue
            # nameof(x) 不算使用
            if before.rstrip().endswith("nameof("):
                continue
            # 具名实参 name: value
            if after.lstrip().startswith(":") and not after.lstrip().startswith("::"):
                continue
            # 对象初始化器里的键：`new T { name = ... }` / `, name = ...`
            bs = before.rstrip()
            if (bs.endswith("{") or bs.endswith(",")) and after.lstrip().startswith("="):
                continue
            # 早出现的这一次若落在某个嵌套块里，而那个嵌套块自己声明了同名局部，
            # 那是遮蔽/CS0136，不归这一条管——保守跳过
            nb = block_start(seg, u.start())
            if nb >= 0 and nested_redeclares(code, seg[nb + 1:u.start()], name):
                continue
            line = code.count("\n", 0, b + 1 + u.start()) + 1
            dline = code.count("\n", 0, decl_at) + 1
            hits.append((line, dline, name))
            break

    return hits


def main():
    files = []
    for d, _, fs in os.walk(SRC):
        for f in fs:
            if f.endswith(".cs"):
                files.append(os.path.join(d, f))
    files.sort()

    bad = 0
    for path in files:
        for line, dline, name in scan(path):
            rel = os.path.relpath(path, ROOT)
            print("%s:%d: 局部变量 '%s' 在声明之前就被用了（声明在第 %d 行）"
                  " —— CS0841" % (rel, line, name, dline))
            bad += 1

    print("\n检查 %d 个文件，发现 %d 处可疑" % (len(files), bad))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
