#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
谁有资格读【用】键。

【这条 linter 是被一次卡关换来的】
MobileInput.GetDown 是**消费式**的：

    if (_pressFrame.TryGetValue(btn, out int f) && Time.frameCount - f <= 2)
    {
        _pressFrame.Remove(btn);      // ← 读到就删掉
        return true;
    }

所以同一帧里谁先读到，谁就独吞这一次按下，后面的人一律读到 false。
而 Unity 的 Update 顺序是不保证的。

我给"走近看说明"的牌子（Examinable）也加了一句读【用】，想让玩家点一下收起卡片。
后果是：地面那五条分段线每条都挂着一个只读牌，而【起手位】那条线和工作台
**在同一个 t（0.32）上**——分段的 Update 先跑就把按键吃掉，工作台永远收不到，
第一版做不出来，这一关直接过不去。玩家原话：
"靠近带文字木牌，并按下用/R，会触发在左侧触发一个弹框，
这直接导致玩家无法完成触发通关条件！"

教训：**动作键属于你要操作的那件东西。只能读的牌子一律不许碰它。**

这类错误编译器不管、lint 不管、截图也看不出来（它只是"按了没反应"），
只有玩家走到那个位置去按才发现。而它是可以用名单挡住的：
读这个键的文件就那么十几个，每一个都该是"玩家在它跟前按下去会发生事"的东西。
名单之外的新增，一律先红——逼一次思考，而不是等玩家来报。
"""
import io, os, sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
SRC = os.path.join(ROOT, "Assets", "_Project")
NEEDLE = 'GetDown("Interact")'

# 允许读【用】键的文件：每一个都是"按下去真的会发生事"的东西。
# 加新的进来之前先问一句：它和别的可交互物会不会同时落在玩家身边？
# 会的话，两者只能有一个读这个键。
ALLOW = {
    "Assets/_Project/Scripts/Shame/ShameProps.cs",             # 第八章的羞耻物件
    "Assets/_Project/Scripts/InternalOS/InternalProps.cs",     # 第 9-26 章的关键物
    "Assets/_Project/Scripts/InternalOS/Level0901BlankPage.cs",# 9-1 的修改卡
    "Assets/_Project/Scripts/InternalOS/Level0902ProofRoom.cs",# 9-2 的修改项
    "Assets/_Project/Scripts/OpenWorld/HomeComforts.cs",       # 家里的床/椅/浴室
    "Assets/_Project/Scripts/OpenWorld/HomeFixture.cs",        # 家里的固定家具
    "Assets/_Project/Scripts/OpenWorld/SiteGate.cs",           # 进关卡的门
    "Assets/_Project/Scripts/OpenWorld/UserImageLibrary.cs",   # 画框
    "Assets/_Project/Scripts/OpenWorld/WaterOutlet.cs",        # 水龙头
    "Assets/_Project/Scripts/OpenWorld/WeaponRack.cs",         # 兵器架
    "Assets/_Project/Scripts/OpenWorld/PetCat.cs",             # 猫
    "Assets/_Project/Scripts/OpenWorld/WorldLayerController.cs",  # 世界层切换点
}


def strip_comments(src):
    """去掉注释与字符串字面量；字符串里出现的 GetDown("Interact") 不算调用。"""
    out, i, n = [], 0, len(src)
    while i < n:
        c = src[i]
        if c == '"':
            # 字面量整体留下来：我们要判的是"这里有没有真的调用"，
            # 而调用本身写作 GetDown("Interact")，引号是语法的一部分。
            out.append(c); i += 1
            while i < n:
                if src[i] == '\\': out.append(src[i:i + 2]); i += 2; continue
                out.append(src[i])
                if src[i] == '"': i += 1; break
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


def main():
    files = []
    for d, _, fs in os.walk(SRC):
        for f in fs:
            if f.endswith(".cs"):
                files.append(os.path.join(d, f))
    files.sort()

    found, bad = set(), 0
    for path in files:
        code = strip_comments(io.open(path, encoding="utf-8", errors="replace").read())
        if NEEDLE not in code:
            continue
        rel = os.path.relpath(path, ROOT).replace(os.sep, "/")
        found.add(rel)
        if rel in ALLOW:
            continue
        line = code[:code.index(NEEDLE)].count("\n") + 1
        print("%s:%d: 这个文件在读【用】键，但不在名单里。" % (rel, line))
        print("    MobileInput.GetDown 是消费式的：谁先读谁独吞，别人一律读到 false。")
        print("    如果它只是一块「走近能读」的牌子，就不该碰这个键；")
        print("    如果它确实是按下去会发生事的东西，把它加进 tools/interactkey.py 的 ALLOW，")
        print("    并确认它不会和别的可交互物同时落在玩家身边。")
        bad += 1

    stale = sorted(ALLOW - found)
    for rel in stale:
        print("%s: 在名单里但已经不读这个键了——把它从 ALLOW 删掉，名单要跟着代码走。" % rel)
        bad += 1

    print("\n检查 %d 个文件，读【用】键的 %d 个，名单外/名单过期 %d 处"
          % (len(files), len(found), bad))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
