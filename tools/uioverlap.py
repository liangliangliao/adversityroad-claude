#!/usr/bin/env python3
"""UI 版面重叠自检。

这条 linter 是被一次真实的失败换来的：我给设置面板加了「模型本色对照」开关，
放在 y=-1396，而已有的「跳过当前子章」在 y=-1388——两个都是 70 高的按钮，
只差 8 像素完全重叠，而且它比我后创建，直接把我这颗盖没了。玩家打开设置
面板，看到的是"你确定加了？"。

顺手一查，同一个文件里还埋着一颗更久的：「调试模式（敌人耐揍）」在 -560，
被 -564 那两颗 545 宽的按钮完全盖住，一直点不到。

这类错误编译器不管、运行时不报、截图上也看不出来（被盖住的东西就是看不见），
只有玩家去点才发现。而它纯粹是算术题：两个矩形有没有交集。

只查【直接挂在 _panel.transform 下】的控件——挂在别的父节点上的本来就
各自独立，不该互相比较。
"""
import re, sys, pathlib

# 锚点写法：UiUtil.MakeButton(_panel.transform, ..., new Vector2(0.5f,1f), new Vector2(x,y), new Vector2(w,h)
V = r"new Vector2\((-?[\d.]+)f?,\s*(-?[\d.]+)f?\)"

def rects_of(path):
    src = path.read_text(encoding="utf-8")
    # 注释里的数字不算
    code = "\n".join("" if l.strip().startswith("//") else l for l in src.split("\n"))
    if "_panel.transform" not in code:
        return None, []
    # 【只查按钮压按钮】按所有控件查会满屏误报：滚动容器、背景板、输入框
    # 本来就该把里面的东西包住，那不是 bug。而一颗按钮压住另一颗按钮，
    # 永远是 bug——被压住的那颗点不到。
    #
    # 锚点要一起记下来并【只比同锚点的】：顶部锚(0.5,1) 与底部锚(0.5,0) 的
    # y 坐标不在同一个参照系里，混着比就是拿两把尺子量同一段距离。
    # 【按所在方法分组】同一个面板的不同"页"是分别在各自的方法里搭的，
    # 页与页之间本来就不会同时出现（CharacterPanel 的主页与换装页各有一颗
    # "关闭"，坐标一样，那不是重叠）。只比同一个方法里搭出来的控件。
    methods = [m.start() for m in re.finditer(
        r"^\s*(?:public\s+|static\s+|private\s+)*(?:void|Button|GameObject|Text|Image)\s+\w+\s*\(",
        code, re.M)]
    btns = [m.start() for m in re.finditer(r"MakeButton\s*\(", code)]
    vs = [(m.start(), float(m.group(1)), float(m.group(2)))
          for m in re.finditer(V, code)]
    out, i = [], 0
    while i < len(vs) - 2:
        pos, ax, ay = vs[i]
        if ax in (0.0, 0.5, 1.0) and ay in (0.0, 0.5, 1.0):
            _, px, py = vs[i + 1]
            _, w, h = vs[i + 2]
            prev = max([b for b in btns if b < pos], default=-1)
            owned = prev >= 0 and ";" not in code[prev:pos]
            if owned and w > 50 and h > 10:
                scope = max([q for q in methods if q < pos], default=-1)
                out.append((px, py, w, h, code[:vs[i + 1][0]].count("\n") + 1,
                            (ax, ay), scope))
                i += 3
                continue
        i += 1
    # MakeToggle(label, y)：宽高写在辅助函数里，单独取一次
    mt = re.search(r"Button MakeToggle.*?" + V + r"\s*,\s*" + V, code, re.S)
    if mt:
        tw, th = float(mt.group(3)), float(mt.group(4))
        for m in re.finditer(r'MakeToggle\("[^"]*",\s*(-?[\d.]+)f?', code):
            scope = max([q for q in methods if q < m.start()], default=-1)
            out.append((0.0, float(m.group(1)), tw, th,
                        code[:m.start()].count("\n") + 1, (0.5, 1.0), scope))
    # 面板自身的高度（越界判定用）
    ph = None
    mp = re.search(r"MakePanel\([^;]*?" + V, code, re.S)
    if mp:
        ph = float(mp.group(2))
    return ph, out

def main():
    bad = 0
    files = sorted(pathlib.Path("Assets/_Project/Scripts/UI").glob("*.cs"))
    for f in files:
        ph, rs = rects_of(f)
        for a in range(len(rs)):
            for b in range(a + 1, len(rs)):
                A, B = rs[a], rs[b]
                # 留 1 像素容差：紧挨着不算压着
                if A[5] == B[5] and A[6] == B[6] and \
                   abs(A[0] - B[0]) * 2 < A[2] + B[2] - 1 and \
                   abs(A[1] - B[1]) * 2 < A[3] + B[3] - 1:
                    print(f"{f}:{A[4]}: 与第 {B[4]} 行的控件重叠 —— "
                          f"(x={A[0]:.0f} y={A[1]:.0f} {A[2]:.0f}x{A[3]:.0f}) 压着 "
                          f"(x={B[0]:.0f} y={B[1]:.0f} {B[2]:.0f}x{B[3]:.0f})，"
                          f"后创建的那个会把先创建的盖住")
                    bad += 1
        top = [r for r in rs if r[5] == (0.5, 1.0)]
        if ph and ph > 200 and top:
            low = min(r[1] - r[3] / 2 for r in top)
            if low < -ph:
                print(f"{f}: 最低边 {low:.0f} 掉出面板（面板高 {ph:.0f}）")
                bad += 1
    print(f"\n检查 {len(files)} 个界面文件，发现 {bad} 处问题")
    return 1 if bad else 0

if __name__ == "__main__":
    sys.exit(main())
