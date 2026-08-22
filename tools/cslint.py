#!/usr/bin/env python3
"""C# 结构体检：专抓"编辑时把语句插进 if 和它的 else 之间"这类错误。
括号平衡查不出它（两边都平衡），只有顺着语法结构走才能发现。"""
import re, sys, glob

def blank_out(code):
    """把注释与字符串换成等长空格，保持所有下标不变。"""
    out = list(code); i = 0; n = len(code)
    while i < n:
        c = code[i]
        if c == '/' and i+1 < n and code[i+1] == '/':
            while i < n and code[i] != '\n': out[i] = ' '; i += 1
        elif c == '/' and i+1 < n and code[i+1] == '*':
            while i < n and not (code[i] == '*' and i+1 < n and code[i+1] == '/'):
                if code[i] != '\n': out[i] = ' '
                i += 1
            for k in range(i, min(i+2, n)): out[k] = ' '
            i += 2
        elif c == '@' and i+1 < n and code[i+1] == '"':
            out[i] = ' '; out[i+1] = ' '; i += 2
            while i < n:
                if code[i] == '"':
                    if i+1 < n and code[i+1] == '"': out[i] = out[i+1] = ' '; i += 2; continue
                    out[i] = ' '; i += 1; break
                if code[i] != '\n': out[i] = ' '
                i += 1
        elif c == '"' or c == "'":
            q = c; out[i] = ' '; i += 1
            while i < n:
                if code[i] == '\\': out[i] = ' '; out[min(i+1,n-1)] = ' '; i += 2; continue
                if code[i] == q: out[i] = ' '; i += 1; break
                if code[i] != '\n': out[i] = ' '
                i += 1
        else:
            i += 1
    return ''.join(out)

WS = set(' \t\r\n')

def skip_ws_back(s, i):
    while i >= 0 and s[i] in WS: i -= 1
    return i

def match_back(s, i, open_c, close_c):
    """s[i] 是右括号，返回配对左括号的下标。"""
    depth = 0
    while i >= 0:
        if s[i] == close_c: depth += 1
        elif s[i] == open_c:
            depth -= 1
            if depth == 0: return i
        i -= 1
    return -1

def word_before(s, i):
    """取 i 处（含）往前的标识符。"""
    j = i
    while j >= 0 and (s[j].isalnum() or s[j] == '_'): j -= 1
    return s[j+1:i+1]

def check_else(path, code):
    s = blank_out(code)
    problems = []
    for m in re.finditer(r'\belse\b', s):
        i = m.start()
        j = skip_ws_back(s, i-1)
        if j < 0: continue
        if s[j] == '#': continue   # 预处理指令 #else，不是语句
        ok = False; why = ''
        if s[j] == '}':
            k = match_back(s, j, '{', '}')
            if k < 0: why = '找不到配对的 {'
            else:
                p = skip_ws_back(s, k-1)
                if p >= 0 and s[p] == ')':
                    q = match_back(s, p, '(', ')')
                    w = word_before(s, skip_ws_back(s, q-1))
                    ok = (w == 'if')
                    why = f'块前的关键字是 "{w}"，不是 if'
                elif p >= 0 and word_before(s, p) == 'else':
                    ok = True   # else { } else if  —— 由外层的链条负责
                else:
                    why = '块前不是 if(...)'
        elif s[j] == ';':
            # 简单语句结尾：往前找同层的语句边界
            k = j-1; depth = 0
            while k >= 0:
                c = s[k]
                if c in ')]}': depth += 1
                elif c in '([{':
                    if depth == 0: break
                    depth -= 1
                elif c == ';' and depth == 0: break
                k -= 1
            stmt = s[k+1:j].strip()
            # 剥掉外层的循环/using/lock 头：`foreach (...) if (...) x; else` 里
            # 这个 else 属于内层的 if，是合法的
            while True:
                m2 = re.match(r'^(for|foreach|while|using|lock|fixed)\s*\(', stmt)
                if not m2: break
                q2 = stmt.index('(', m2.end()-1)
                d2 = 0
                for t in range(q2, len(stmt)):
                    if stmt[t] == '(': d2 += 1
                    elif stmt[t] == ')':
                        d2 -= 1
                        if d2 == 0: break
                stmt = stmt[t+1:].strip()
            ok = bool(re.match(r'^(if|else)\b', stmt))
            why = f'else 前面的语句是 "{stmt[:60]}"，它不是 if 语句'
        else:
            why = f'else 前面是 "{s[j]}"'
        if not ok:
            line = code.count('\n', 0, i) + 1
            problems.append((line, why))
    return problems

if __name__ == "__main__":
    files = sys.argv[1:] or glob.glob('Assets/**/*.cs', recursive=True)
    bad = 0
    for f in files:
        code = open(f, encoding='utf-8').read()
        for line, why in check_else(f, code):
            print(f"{f}:{line}: 悬空 else —— {why}")
            bad += 1
    print(f"\n检查 {len(files)} 个文件，发现 {bad} 处问题")
    sys.exit(1 if bad else 0)
