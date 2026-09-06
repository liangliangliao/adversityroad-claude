#!/usr/bin/env python3
"""
csns —— 命名空间限定被同名成员遮蔽（Unity 里编不过，本地却看不出来）。

起因：PottedPlant 里有个 `static readonly Color Core`（花芯的颜色），
而我在同一个类里写了 `Core.ActorRegistry.Player`。C# 的名字解析里
**成员优先于命名空间**，于是 `Core` 被解析成那个 Color 字段，
报 CS1061「'Color' does not contain a definition for 'ActorRegistry'」。

这类错误的特点是：读代码完全看不出来（两处相隔一百多行），
本地又没有 C# 编译器，只能等 CI 跑二十分钟才发现。规则本身却很确定：
**类里声明了一个和本工程命名空间同名的成员，同一个文件又把那个名字当命名空间用**，
就一定编不过。

判定按文件而不是按类：本工程基本一个文件一个类，按文件足够，
且宁可粗一点也不能漏——真出现时它是必错，不是可疑。
"""
import re, sys, glob, os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cslint import blank_out

SRC = os.path.join(ROOT, "Assets", "_Project")

# 本工程的命名空间末段（AdversityRoad.World → World）
NS = set()
for f in glob.glob(os.path.join(SRC, "**", "*.cs"), recursive=True):
    for m in re.finditer(r'\bnamespace\s+([\w\.]+)', open(f, encoding="utf-8").read()):
        for seg in m.group(1).split('.'):
            if seg != "AdversityRoad":
                NS.add(seg)

# 成员声明：修饰符 + 类型 + 名字，且名字后面是 = 或 ; （字段/常量/属性）
MEMBER = re.compile(
    r'^\s*(?:public|private|protected|internal|static|readonly|const|new|override|virtual|\s)+'
    r'[\w<>,\.\[\]\?]+\s+([A-Z]\w*)\s*(?:=|;|\{)', re.M)

bad = []
files = sorted(glob.glob(os.path.join(SRC, "**", "*.cs"), recursive=True))
for f in files:
    src = open(f, encoding="utf-8").read()
    s = blank_out(src)          # 去掉注释与字符串，避免注释里的示例被当真
    members = {m.group(1) for m in MEMBER.finditer(s)} & NS
    if not members:
        continue
    for name in sorted(members):
        # 只看**表达式位置**。类型位置（泛型实参、new、变量声明）里成员根本不参与
        # 名字解析，`FindFirstObjectByType<Player.PlayerController>()` 一直是对的。
        # 表达式位置的特征：后面还要接着取成员或调用，即至少三段
        # （Core.ActorRegistry.Player）或直接调用（Core.Foo(...)）。
        # 这样既抓住真问题，又不会把满地的泛型实参报成错。
        for m in re.finditer(r'(?<![\w\.<])' + name + r'\.([A-Z]\w*)\s*(?=[\.\(])', s):
            seg = s.rfind('\n', 0, m.start()) + 1
            head = s[seg:m.start()]
            # 同一行里还没闭合的 '<' 说明我们在泛型实参里
            if head.count('<') > head.count('>'):
                continue
            if re.search(r'\b(new|typeof|is|as)\s*$', head):
                continue
            line = s[:m.start()].count('\n') + 1
            bad.append("%s:%d: '%s' 既是本文件的成员名，又被当作命名空间用"
                       "（%s.%s）—— 成员优先于命名空间，这里编不过；"
                       "改用全限定名 AdversityRoad.%s.%s"
                       % (os.path.relpath(f, ROOT), line, name, name,
                          m.group(1), name, m.group(1)))

for b in bad:
    print(b)
print("\n检查 %d 个文件，发现 %d 处问题" % (len(files), len(bad)))
sys.exit(1 if bad else 0)
