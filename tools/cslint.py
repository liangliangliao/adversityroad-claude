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

def _consume_body(stmt, i):
    """从 i 开始吃掉一条语句体（块或单句），返回其后的下标；解析不了返回 None。"""
    while i < len(stmt) and stmt[i] in WS:
        i += 1
    if i >= len(stmt):
        return len(stmt)
    if stmt[i] == '{':
        d = 0
        for t in range(i, len(stmt)):
            if stmt[t] == '{':
                d += 1
            elif stmt[t] == '}':
                d -= 1
                if d == 0:
                    return t + 1
        return None
    d = 0
    for t in range(i, len(stmt)):
        c = stmt[t]
        if c in '([{':
            d += 1
        elif c in ')]}':
            d -= 1
        elif c == ';' and d == 0:
            return t + 1
    return len(stmt)


def _consume_paren(stmt, i):
    """i 处（跳空白后）应是 '('，返回配对 ')' 之后的下标；否则 None。"""
    while i < len(stmt) and stmt[i] in WS:
        i += 1
    if i >= len(stmt) or stmt[i] != '(':
        return None
    d = 0
    for t in range(i, len(stmt)):
        if stmt[t] == '(':
            d += 1
        elif stmt[t] == ')':
            d -= 1
            if d == 0:
                return t + 1
    return None


def else_binds_to_if(stmt):
    """stmt 是 else 之前那一段代码（不含 else 本身）。
    返回 True/False 表示这个 else 能不能接到一个 if 上；解析不了返回 None。

    【判据为什么是"最后一条"而不是"第一条"】
    从上一个语句边界切出来的区域里可能有好几条语句：
        if (a) { ... }
        if (b) boss = e;        ← else 接的是这一条
    只检查"区域以 if 开头"会漏掉把语句插进 if 与 else 之间的错误（CS8641），
    只检查"第一条 if 是否结束在末尾"又会把上面这种合法写法误报。
    正确的判据只有一个：**逐条吃完，最后一条必须是恰好结束在末尾的 if 语句。**"""
    i = 0
    last_is_if_at_end = False
    guard = 0
    while True:
        guard += 1
        if guard > 4096:
            return None
        while i < len(stmt) and stmt[i] in WS:
            i += 1
        if i >= len(stmt):
            return last_is_if_at_end
        # 区域里自带的 else（属于更内层、已经配好对的链）：跳过关键字本身，
        # 后面那条 if/体照常按语句吃。
        if re.match(r'^else\b', stmt[i:]):
            i += 4
            continue
        if re.match(r'^if\b', stmt[i:]):
            k = _consume_paren(stmt, i + 2)
            if k is None:
                return None
            k = _consume_body(stmt, k)
            if k is None or k <= i:
                return None
            last_is_if_at_end = (stmt[k:].strip() == '')
            i = k
            continue
        k = _consume_body(stmt, i)
        if k is None or k <= i:
            return None
        last_is_if_at_end = False
        i = k


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
                if c == '}' and depth == 0:
                    # 【回扫也要在 } 处停】倒着走时在 0 层遇到 }，说明前面是一条
                    # **以块收尾的完整语句**（if(){}、foreach(){}、else{} …），
                    # 它就是边界。只在 ; 处停的话会把那整条语句一起吃进来，
                    # 于是切出来的区段从半个关键字开始（"e if (b) …"），
                    # 后面无论怎么判都是错的。
                    break
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
            # 【只看"以 if 开头"是不够的——这正是漏掉 PlayableAnimator 那次的原因】
            # 坏例子：
            #     if (measured) { angle = mA; }
            #     float gaitPhase = ...;          ← 插在中间
            #     else if (...) return;
            # 从上一个语句边界切出来的 stmt 是
            #     "if (measured) { angle = mA; } float gaitPhase = ..."
            # 它**确实以 if 开头**，于是旧判据放行，而编译器报 CS8641。
            # 正确的判据是：这条 if 语句必须**正好结束在 else 之前**，
            # 后面不能再挂任何东西。
            if not re.match(r'^(if|else)\b', stmt):
                ok = False
                why = f'else 前面的语句是 "{stmt[:60]}"，它不是 if 语句'
            else:
                bound = else_binds_to_if(stmt)
                if bound is None:
                    ok = True          # 解析不了就不下判断，宁可漏报也不误报
                else:
                    ok = bound
                    why = ('else 前面最后一条语句不是 if（if 语句已经结束了，'
                           '中间夹了别的东西）—— else 接不到它，编译器报 CS8641。'
                           f'区段："{stmt[-70:].strip()}"')
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
