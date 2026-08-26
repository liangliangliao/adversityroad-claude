#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
逐帧调试日志分析器：把 movelog_*.csv 变成一份排好序的问题清单。

用法：
    python3 tools/movelog.py movelog_20260827_021530.csv
    python3 tools/movelog.py 日志.csv --at 7.2      # 打印某个时刻前后 1 秒的全部列

【为什么要有它】
这一路我都是靠录屏截帧读 HUD：一段 20 秒只能抠出十来个采样点，而"起步滑一下"
"转向那一瞬漂了"只存在于零点几秒里，十来个点大概率整个错过；而且从中文拼接
字符串里抠数字抠错过一次，让我误判成"脚滑 15%"。日志每帧一行、每列都是数，
这个脚本把判据写死成阈值，谁也别再凭印象说话。

每一条判据旁边都注明【为什么这个阈值】——阈值不是拍的，是从这个项目的实测
参数推出来的。
"""
import csv
import io
import sys


# ---- 阈值：全部有出处，不是拍脑袋 ----
SLIP_LO, SLIP_HI = 0.88, 1.12   # 步幅比。1.00=脚踩实；实测正常段落在 0.92~1.00，
                                # 起步滑动那一帧读到 0.46。留 ±12% 的容差。
LAT_G_MAX = 1.05                # 横向加速度。真人慢跑转弯 0.4~0.6g，冲刺极限 ≈1g。
                                # 超过 1.05g 就是"物理上不可能"，读作陀螺。
STEP_MAX = 0.40                 # 单帧位移。胶囊半径 0.4m，超过它一帧就能穿薄墙。
                                # 正常 60fps 跑动是 0.09m。
STUCK_MIN = 0.30               # 一段"看不见自己"持续多久才值得报（秒）。
FOOTFIX_MAX = 0.26             # 锁脚修正量。封顶是 0.28，常年顶格说明步幅同步有问题。
MIN_SPAN = 0.15                # 短于它的异常不报，避免单帧噪声刷屏（秒）。


def load(path):
    with io.open(path, encoding="utf-8-sig", errors="replace") as f:
        rows = list(csv.DictReader(f))
    st = [r for r in rows if r.get("kind") == "S"]
    ev = [r for r in rows if r.get("kind") == "E"]
    return st, ev


def num(r, k, d=0.0):
    try:
        return float(r.get(k) or 0.0)
    except ValueError:
        return d


def spans(state, pred, minlen=MIN_SPAN):
    """把满足 pred 的连续帧合并成区间 [(t0, t1, 峰值行), ...]。"""
    out, cur = [], None
    for r in state:
        t = num(r, "t")
        if pred(r):
            if cur is None:
                cur = [t, t, r]
            else:
                cur[1] = t
        elif cur is not None:
            if cur[1] - cur[0] >= minlen:
                out.append(tuple(cur))
            cur = None
    if cur is not None and cur[1] - cur[0] >= minlen:
        out.append(tuple(cur))
    return out


def report(title, items, fmt):
    print("\n" + title)
    if not items:
        print("  （无）")
        return
    for it in items[:12]:
        print("  " + fmt(it))
    if len(items) > 12:
        print("  …… 另有 %d 段" % (len(items) - 12))


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("-")]
    if not args:
        print(__doc__)
        return 2
    path = args[0]
    at = None
    if "--at" in sys.argv:
        at = float(sys.argv[sys.argv.index("--at") + 1])

    st, ev = load(path)
    if not st:
        print("没有状态行——日志是空的，或者列名对不上。")
        return 1

    dur = num(st[-1], "t") - num(st[0], "t")
    fps = [num(r, "fps") for r in st]
    print("=" * 72)
    print("%s：%d 帧 / %.1f 秒　平均 %.0f fps（最低 %.0f）" %
          (path, len(st), dur, sum(fps) / len(fps), min(fps)))
    print("=" * 72)

    # ---- ① 看不见自己：这是所有"撞墙/进不去门/盲区"的共同上游 ----
    blind = spans(st, lambda r: r.get("seeSelf") == "0", STUCK_MIN)
    blind_t = sum(b[1] - b[0] for b in blind)
    report("【① 镜头看不见角色】占比 %.0f%%　—— 它为真的时候，玩家在盲操，"
           "之后所有操作失误都只是后果" % (100.0 * blind_t / max(dur, 1e-6)),
           blind,
           lambda b: "%.2f→%.2f 秒（%.2fs）  吊杆%.2f/%.2f 抬%.2f 俯%.0f° %s"
                     % (b[0], b[1], b[1] - b[0], num(b[2], "camBoom"),
                        num(b[2], "camBoomWant"), num(b[2], "camLift"),
                        num(b[2], "camPitch"),
                        "[嵌墙]" if b[2].get("camStuck") == "1" else ""))

    # ---- ② 脚打滑：步幅比偏离 1.00 ----
    def slipping(r):
        v = num(r, "strideRatio")
        return num(r, "actual") > 0.5 and v > 0.01 and not (SLIP_LO <= v <= SLIP_HI)
    slip = spans(st, slipping)
    report("【② 脚打滑】步幅比 = 实际每周期距离 ÷ 片段自带步幅；1.00 才是踩实。"
           "\n     >1 脚被往前拖（大步漂移），<1 腿转得比地面快（太空步）",
           slip,
           lambda b: "%.2f→%.2f 秒　比值 %.2f　速度%.1f　片段 %s/%s　%s"
                     % (b[0], b[1], num(b[2], "strideRatio"), num(b[2], "actual"),
                        b[2].get("dir1") or "-", b[2].get("dir2") or "-",
                        ("⚠速率夹持 %s 需%.2f×得%.2f×" %
                         (b[2].get("slipClip"), num(b[2], "slipWant"), num(b[2], "slipGot")))
                        if b[2].get("slipClip") else ""))

    # ---- ③ 转向超出人类极限 ----
    spin = spans(st, lambda r: num(r, "lateralG") > LAT_G_MAX)
    worstg = max(st, key=lambda r: num(r, "lateralG"))
    report("【③ 转向像陀螺】横向加速度峰值 %.2fg（真人慢跑转弯 0.4~0.6g，冲刺极限≈1g）"
           % num(worstg, "lateralG"), spin,
           lambda b: "%.2f→%.2f 秒　%.2fg　半径%.1fm　角速度%.0f°/s　速度%.1f"
                     % (b[0], b[1], num(b[2], "lateralG"), num(b[2], "turnRadius"),
                        num(b[2], "bodyYawRate"), num(b[2], "actual")))

    # ---- ④ 位置突变 ----
    jump = [r for r in st if num(r, "stepLen") > STEP_MAX]
    report("【④ 单帧位移过大】胶囊半径 0.4m，超过它一帧就能穿薄墙"
           "（正常 60fps 跑动 0.09m）",
           jump[:12],
           lambda r: "%.2f 秒　位移 %.2fm　速度%.1f　fps%.0f" %
                     (num(r, "t"), num(r, "stepLen"), num(r, "actual"), num(r, "fps")))

    # ---- ⑤ 推着杆却走不动：卡墙 ----
    stuckmv = spans(st, lambda r: num(r, "stickMag") > 0.6 and num(r, "actual") < 0.6
                    and r.get("hitSides") == "1")
    report("【⑤ 推满杆却走不动】撞在墙上。若它紧跟在①之后，就是"
           "\"看不见→撞墙\"那条因果链", stuckmv,
           lambda b: "%.2f→%.2f 秒（%.2fs）　杆%.2f　目标%.1f→实际%.1f"
                     % (b[0], b[1], b[1] - b[0], num(b[2], "stickMag"),
                        num(b[2], "finalSpeed"), num(b[2], "actual")))

    # ---- ⑥ 动作层盖住移动层 ----
    over = spans(st, lambda r: num(r, "actionW") > 0.5 and num(r, "actual") > 1.5)
    report("【⑥ 位移中动作层接管】动作层是**替换**移动层的：这段时间里腿在演招式，"
           "人却还在跑——这就是\"多个动画在打架\"", over,
           lambda b: "%.2f→%.2f 秒　动作 %s 权重%.2f　速度%.1f"
                     % (b[0], b[1], b[2].get("actionClip") or "-",
                        num(b[2], "actionW"), num(b[2], "actual")))

    # ---- ⑦ 锁脚顶格 ----
    fix = spans(st, lambda r: num(r, "footFix") > FOOTFIX_MAX)
    report("【⑦ 锁脚修正顶格】封顶 0.28m。常年顶格说明步幅同步本身有问题，"
           "锁脚只是在硬拽", fix,
           lambda b: "%.2f→%.2f 秒　修正 %.2fm　速度%.1f" %
                     (b[0], b[1], num(b[2], "footFix"), num(b[2], "actual")))

    # ---- ⑧ 方向片段有没有真的用上 ----
    used = {}
    for r in st:
        for k, wk in (("dir1", "dir1W"), ("dir2", "dir2W")):
            n = r.get(k)
            if n and num(r, wk) > 0.05:
                used[n] = used.get(n, 0) + 1
    print("\n【⑧ 方向片段实际出场次数】没出现的那些就是接了却永远播不到")
    if used:
        for n, c in sorted(used.items(), key=lambda kv: -kv[1]):
            print("  %-38s %5d 帧（%.0f%%）" % (n, c, 100.0 * c / len(st)))
    else:
        print("  （一条都没有——移动层完全没工作）")

    # ---- 事件时间轴 ----
    print("\n【事件时间轴】")
    if ev:
        for r in ev[:80]:
            print("  %7.2f  %s" % (num(r, "t"), r.get("event") or ""))
        if len(ev) > 80:
            print("  …… 另有 %d 条" % (len(ev) - 80))
    else:
        print("  （无）")

    # ---- 定点查看 ----
    if at is not None:
        print("\n【%.2f 秒前后 1 秒的逐帧全量】" % at)
        cols = ["t", "stickMag", "finalSpeed", "actual", "hVel", "moveAngle", "turnNeed",
                "dirTrust", "lateralG", "turnRadius", "bodyYawRate", "phaseRate",
                "strideRatio", "footFix", "pose", "actionClip", "actionW",
                "dir1", "dir1W", "dir2", "dir2W", "camBoom", "seeSelf", "hitSides"]
        print("  " + " ".join("%-10s" % c for c in cols))
        for r in st:
            if abs(num(r, "t") - at) <= 1.0:
                print("  " + " ".join("%-10s" % (r.get(c) or "")[:10] for c in cols))
    return 0


if __name__ == "__main__":
    sys.exit(main())
