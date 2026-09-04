#!/usr/bin/env python3
"""同类内方法缺失自检（CS0103 的另一半）。

这条 linter 是被一次真实的红 CI 换来的：我改 ThirdPersonCamera 时用了
"从字段 A 切到方法 B、整段替换"的做法，结果把夹在中间的 BoomBlocked /
FreeBoomDistance / SightDistance 三个方法一起删掉了。它们在同一个文件里
被调用了十几处，Unity 直接编译失败——而已有的六条 linter 一条都没抓到：
csident 只认"写错下划线"，csmember 只查得到类型的成员访问，
没有一条负责"这个不带点的调用，它的方法还在不在"。

做法：把形如 `Foo(` 的**裸调用**（前面不是 `.`、不是 `new`）收集起来，
要求 Foo 在同一个文件里有声明。不带点的调用只可能是本类/基类的成员，
所以"同文件有声明"对本项目这种一类一文件的写法成立。
基类（MonoBehaviour 等）与语言结构的名字走白名单。

误报代价是加一个白名单条目；漏报代价是一次二十分钟的构建红。
"""
import re, sys, pathlib

# 语言结构与基类/常用静态方法：这些裸调用不需要在本文件里声明
ALLOW = {
    # 语言/流程
    "if", "for", "foreach", "while", "switch", "catch", "lock", "using", "return",
    "sizeof", "typeof", "nameof", "default", "checked", "unchecked", "fixed",
    "new", "base", "this", "get", "set", "add", "remove", "value", "yield",
    # MonoBehaviour / Object
    "GetComponent", "GetComponentInChildren", "GetComponentInParent",
    "GetComponents", "GetComponentsInChildren", "GetComponentsInParent",
    "TryGetComponent", "AddComponent", "Instantiate", "Destroy", "DestroyImmediate",
    "StartCoroutine", "StopCoroutine", "StopAllCoroutines", "Invoke", "InvokeRepeating",
    "CancelInvoke", "IsInvoking", "print", "FindObjectOfType", "FindObjectsOfType",
    "DontDestroyOnLoad", "SendMessage", "BroadcastMessage", "Equals", "ToString",
    "GetHashCode", "GetType", "MemberwiseClone", "ReferenceEquals",
    # 常见泛型/委托调用形态
    "Invoke", "BeginInvoke", "EndInvoke",
}

CALL = re.compile(r"(?<![\w.])(?<!new )([A-Za-z_]\w*)\s*\(")


def declared_names(code):
    """收集本文件里声明的方法名。

    【不看返回类型】原来按 `返回类型 名字(` 匹配，元组返回值
    `(string label, int count) MostCommon()` 与嵌套泛型
    `IEnumerable<KeyValuePair<A, B>> All(...)` 全都漏掉，于是把真实存在的
    方法报成缺失。改成只认**声明的形状**：名字 + 括号闭合之后紧跟 `{` 或 `=>`。
    调用永远不是这个形状（`Foo(x) {` 不是合法 C#），所以既与返回类型无关，
    也不会把调用误当成声明。
    """
    names = set()
    for m in re.finditer(r"\b(\w+)\s*\(", code):
        i = m.end() - 1
        depth = 0
        while i < len(code):
            if code[i] == "(":
                depth += 1
            elif code[i] == ")":
                depth -= 1
                if depth == 0:
                    break
            i += 1
        else:
            continue
        j = i + 1
        # 跳过泛型约束 where T : X
        tail = code[j:j + 120]
        tail = re.sub(r"^\s*where\s+[^{;=]*", "", tail)
        tail = tail.lstrip()
        if tail.startswith("{") or tail.startswith("=>"):
            names.add(m.group(1))
    return names


def strip_noise(code):
    code = re.sub(r"//[^\n]*", "", code)
    # 特性不是调用：[Tooltip("…")] [Range(0,1)] [RequireComponent(typeof(X))]
    code = re.sub(r"\[\s*[A-Za-z_][\w.]*\s*\((?:[^\[\]]|\[[^\]]*\])*\)\s*\]", "", code)
    code = re.sub(r"\[\s*[A-Za-z_][\w.]*\s*\]", "", code)
    code = re.sub(r"/\*.*?\*/", "", code, flags=re.S)
    code = re.sub(r'"(?:[^"\\\n]|\\.)*"', '""', code)
    code = re.sub(r"'(?:[^'\\\n]|\\.)*'", "''", code)
    return code

def main():
    files = sorted(pathlib.Path("Assets/_Project").rglob("*.cs"))
    bad = 0
    for f in files:
        raw = f.read_text(encoding="utf-8")
        code = strip_noise(raw)
        declared = declared_names(code)
        # 本文件里声明的类型名也算（构造函数调用 Foo(...) 形如类型名）
        declared |= set(re.findall(r"\b(?:class|struct|enum|interface)\s+(\w+)", code))
        # 局部函数与委托字段
        declared |= set(re.findall(r"\b(\w+)\s*=>", code))
        declared |= set(re.findall(r"\b(?:Action|Func|UnityAction|Predicate)[<\s][^;=]*?\b(\w+)\s*[;=]", code))
        for m in CALL.finditer(code):
            name = m.group(1)
            if name in ALLOW or name in declared:
                continue
            # 大写开头才当作方法名候选：小写的多半是局部委托/变量
            if not name[0].isupper():
                continue
            line = code[:m.start()].count("\n") + 1
            print(f"{f}:{line}: 裸调用 '{name}(' 在本文件里找不到声明 —— 疑似 CS0103"
                  f"（方法被误删 / 拼错 / 该加白名单）")
            bad += 1
    print(f"\n检查 {len(files)} 个文件，发现 {bad} 处可疑")
    return 1 if bad else 0

if __name__ == "__main__":
    sys.exit(main())
