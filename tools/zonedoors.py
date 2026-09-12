#!/usr/bin/env python3
"""
zonedoors —— 经典关卡的两扇门是不是**各在一端**，而且 BOSS 在这条路上。

起因：有几关的进出两扇门开在同一侧。小题大做审判庭（±14, -35）和旧事回声馆
（±14, -42）两扇门都贴在南墙上，相距 28 米；陌生挑衅路口两扇门都在南沿，
而挑衅镜像站在路口正中 30 米之外；欠条长廊两扇门并排立在小商店里，相距 12 米。
于是"从一扇门进来、朝出口方向走、从另一扇门出去"这件事在那几关根本不成立——
玩家站在入口一转身就是出口，关底 BOSS 连面都不用碰。

外部心魔的关卡把"走到出口"当成通关条件（见 Core/LevelRules.cs）之后，这不再是
观感问题：出口贴着入口 = 这一关可以两步走完。所以把它做成门禁。

判据（四条，全部只看 ZoneBuilder.cs 里的坐标，不需要跑起来）：
  ① 两扇门相对区域原点的夹角 ≥ MIN_ANGLE：小于它就是"同一侧"；
  ② 两扇门之间的直线距离 ≥ MIN_GAP；
  ③ 关底 BOSS（enemySpawns）到"入口→出口"这条线段的垂距 ≤ MAX_BOSS_OFF：
     BOSS 必须在这条路附近，不能缩在角落让人绕过去；
  ④ 玩家落点（playerSpawns）到出口 ≥ MIN_SPAWN_TO_EXIT（= LevelTraverse.MinDistance），
     且不能压在回头门的触发体里（3×2.2，取一半再留余量）。

「落点落在持续掉血的危险区里」（陌生挑衅路口原来就压在车流幻影臂中）**不在这里查**：
危险区的坐标常常来自局部数组或循环变量（BuildCrossroad 的四条臂就是这么摆的），
静态扫源码只会写出一条永远命中不了的正则——那比没有检查更糟。
这一条改由运行时兜：ZoneBuilder.EnsureSpawnsClearOfHazards 在建完世界之后
拿真实碰撞体量一遍，与 EnsureSpawnPads 同一个思路。

只检查"两扇门都由 MakePortal 建出来"的静态区。豁免见 EXEMPT。
"""
import re, sys, os, math

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "Assets", "_Project", "Scripts", "World", "ZoneBuilder.cs")

MIN_ANGLE = 75.0          # 度：两扇门相对区域中心的方位夹角
MIN_GAP = 20.0            # 米：两扇门之间的直线距离
MAX_BOSS_OFF = 25.0       # 米：BOSS 到入口→出口线段的垂距
MIN_SPAWN_TO_EXIT = 12.0  # 米：与 World/LevelTraverse.MinDistance 同一个数
TRIGGER_HALF_X = 1.5      # 门触发体 3(x) × 2.2(z)，落点不能落在里面
TRIGGER_HALF_Z = 1.1
TRIGGER_MARGIN = 0.6      # 再留一点余量：贴着边缘站也不该被门吃掉

# 豁免：**必须写清楚为什么**，否则就是把门禁关掉而不是解决问题。
EXEMPT = {
    26: "二十元回声教室（8-2）：两扇门都在南门厅是**设计要求**，不是疏漏。"
        "本关的终局动作就是「步行离场」——做完归还（讲台前）与完成本职（座位区）之后，"
        "在全场注视下原路走出那扇门，验收第 39 条明写「步行离场要穿过全场」。"
        "把出口挪到教室北端，离场就变成从归还台走五米，那一条就废了。"
        "穿过全场并从 BOSS 脚下路过这件事在这一关由三个目标动作强制，不由门的位置强制。",
}


def blank_comments(code):
    """把 // 注释换成等长空格：注释里的坐标不算数（下标保持不变）。"""
    out = list(code)
    i, n = 0, len(code)
    while i < n:
        if code[i] == '/' and i + 1 < n and code[i + 1] == '/':
            while i < n and code[i] != '\n':
                out[i] = ' '
                i += 1
        else:
            i += 1
    return ''.join(out)


def parse_table(src, name):
    """读 ctx.<name> = new[] { ... } 里按区号排列的 zoneOrigins 偏移表。"""
    m = re.search(r'ctx\.' + name + r'\s*=\s*new\[\]\s*\{(.*?)\n            \};', src, re.S)
    if not m:
        return {}
    out = {}
    for mm in re.finditer(
            r'ctx\.zoneOrigins\[(\d+)\]\s*\+\s*new Vector3\(\s*([-\d.]+)f?\s*,\s*[-\d.]+f?\s*,\s*([-\d.]+)f?\s*\)',
            m.group(1)):
        z = int(mm.group(1))
        if z not in out:                      # 同一区只取第一条（就是那一区的那一项）
            out[z] = (float(mm.group(2)), float(mm.group(3)))
    return out


def seg_distance(px, pz, ax, az, bx, bz):
    """点到线段的距离。"""
    vx, vz = bx - ax, bz - az
    L2 = vx * vx + vz * vz
    if L2 < 1e-6:
        return math.hypot(px - ax, pz - az)
    t = max(0.0, min(1.0, ((px - ax) * vx + (pz - az) * vz) / L2))
    return math.hypot(px - (ax + t * vx), pz - (az + t * vz))


def angle_between(ax, az, bx, bz):
    """两扇门相对区域原点的方位夹角（度）。"""
    a = math.atan2(az, ax)
    b = math.atan2(bz, bx)
    return abs(math.degrees(math.atan2(math.sin(a - b), math.cos(a - b))))


def main():
    raw = open(SRC, encoding="utf-8").read()
    src = blank_comments(raw)
    lines = src.split('\n')
    spawns = parse_table(src, "playerSpawns")
    enemies = parse_table(src, "enemySpawns")

    # 每个 Build*(WorldContext ctx) 函数 → 它操作的区号 + 它建的门
    funcs = [(i, m.group(1)) for i, l in enumerate(lines)
             for m in [re.search(r'static void (Build\w+)\(WorldContext ctx\)', l)] if m]
    funcs.append((len(lines), "END"))

    problems = []
    checked = 0
    for k in range(len(funcs) - 1):
        start, name = funcs[k]
        body = '\n'.join(lines[start:funcs[k + 1][0]])
        zm = re.search(r'ctx\.zoneOrigins\[(\d+)\]', body)
        if not zm:
            continue
        zone = int(zm.group(1))
        doors = []
        for m in re.finditer(
                r'MakePortal\(ctx,\s*o \+ new Vector3\(\s*([-\d.]+)f?\s*,\s*0\s*,\s*([-\d.]+)f?\s*\)\s*,\s*'
                r'(?:PortalRole\.(\w+)|(\d+)\s*,)', body):
            doors.append((m.group(3) or ("→" + m.group(4)), float(m.group(1)), float(m.group(2))))
        if len(doors) != 2:
            continue
        if zone in EXEMPT:
            print("  [%d] %-24s 豁免：%s" % (zone, name, EXEMPT[zone]))
            continue
        checked += 1

        # 方向：有 Back 的就是进来那扇门；两扇都是显式目标时按出现顺序
        back = [d for d in doors if d[0] == "Back"]
        fwd = [d for d in doors if d[0] == "Forward"]
        entry = back[0] if back else doors[0]
        exit_ = fwd[0] if fwd else doors[1]
        if back and fwd:
            pass
        elif not back and len(fwd) == 1:
            entry = [d for d in doors if d is not fwd[0]][0]
            exit_ = fwd[0]

        _, ax, az = entry
        _, bx, bz = exit_
        gap = math.hypot(bx - ax, bz - az)
        ang = angle_between(ax, az, bx, bz)
        tag = "[%d] %s" % (zone, name)

        if ang < MIN_ANGLE:
            problems.append("%s 两扇门在同一侧（相对区域中心夹角 %.0f° < %.0f°）：(%g,%g) 与 (%g,%g)"
                            % (tag, ang, MIN_ANGLE, ax, az, bx, bz))
        if gap < MIN_GAP:
            problems.append("%s 两扇门相距只有 %.1f 米（< %.0f）：出口贴着入口，这一关两步就走完了"
                            % (tag, gap, MIN_GAP))

        if zone in enemies:
            ex, ez = enemies[zone]
            off = seg_distance(ex, ez, ax, az, bx, bz)
            if off > MAX_BOSS_OFF:
                problems.append("%s 关底 BOSS 离「入口→出口」这条路 %.1f 米（> %.0f）：可以整关绕开它"
                                % (tag, off, MAX_BOSS_OFF))

        if zone in spawns:
            sx, sz = spawns[zone]
            d_exit = math.hypot(bx - sx, bz - sz)
            if d_exit < MIN_SPAWN_TO_EXIT:
                problems.append("%s 落点离出口只有 %.1f 米（< %.0f，即 LevelTraverse.MinDistance）："
                                "玩家一进关就已经站在出口上" % (tag, d_exit, MIN_SPAWN_TO_EXIT))
            if (abs(sx - ax) < TRIGGER_HALF_X + TRIGGER_MARGIN and
                    abs(sz - az) < TRIGGER_HALF_Z + TRIGGER_MARGIN):
                problems.append("%s 落点压在进来那扇门的触发体里（Δ=%.1f,%.1f）："
                                "从「传送」面板直达时会当场被送回上一关"
                                % (tag, abs(sx - ax), abs(sz - az)))

    for p in problems:
        print(p)
    print("\n检查 %d 个关卡的进出门布局，发现 %d 处问题" % (checked, len(problems)))
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
