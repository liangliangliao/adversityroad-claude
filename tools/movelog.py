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
    # 切后台那一帧的 dt 是"离开到回来"的整段时间（实测见过 15 秒），
    # 把它算进帧率统计会把"最低 fps"直接压成 0，掩盖掉真正的卡顿。
    play = [r for r in st if num(r, "dt") < 1.0]
    fps = [num(r, "fps") for r in play] or [0.0]
    hitch = [r for r in play if num(r, "dt") > 0.033]
    print("=" * 72)
    print("%s：%d 帧 / %.1f 秒　平均 %.0f fps（在场帧最低 %.0f）" %
          (path, len(st), dur, sum(fps) / len(fps), min(fps)))
    print("卡顿：%d 帧超过 33ms（%.1f%%），最长 %.0fms；切后台 %d 次" %
          (len(hitch), 100.0 * len(hitch) / max(len(play), 1),
           max(num(r, "dt") for r in play) * 1000, len(st) - len(play)))
    print("=" * 72)

    # ---- ① 看不见自己：这是所有"撞墙/进不去门/盲区"的共同上游 ----
    # 不设最短时长：0.1 秒的一闪同样是"画面丢了一下"，而且往往成串出现，
    # 用最短时长过滤会把整串一起藏掉（上一版 0.3 秒的门槛就藏掉了十段）。
    # 【近第一人称不算盲】吊杆被墙压扁时会主动切成"沿吊杆方向看出去"，
    # 那时看不见自己是**设计如此**——玩家看得见前方，正是它存在的目的。
    # 把这一段算进盲区会得出完全相反的结论：上一份日志里 19.34 秒的"盲区"
    # camTight 中位数是 1.00，其实全程是近第一人称。缺列（老日志）按 0 处理。
    blind = spans(st, lambda r: r.get("seeSelf") == "0" and num(r, "camTight") < 0.55, 0.0)
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

    # ---- ②b 播放倍率：片段被快放/慢放了多少 ----
    #
    # 【这一条才是"滑动"的直接量度，比步幅比更早暴露问题】
    # 步幅同步会为了让脚不打滑而调播放速率，所以步幅比可以是漂亮的 0.96，
    # 而腿的摆动频率比动捕快 40%——数值不打滑，看起来照样在冰上滑。
    # 业界经验：0.85~1.15 之间看不出来，超过 1.25 就是肉眼可见的快放。
    # 自然速度 = 实测地面速度 ÷ 播放倍率，两列日志里都有，可以直接反推。
    mv = [r for r in st if num(r, "actual") > 1.0 and num(r, "phaseRate") > 0.05
          and num(r, "dtSim") > 0.005]
    if mv:
        per = {}
        for r in mv:
            per.setdefault(r.get("dir1") or "-", []).append(r)
        rows = []
        for clip, v in sorted(per.items(), key=lambda kv: -len(kv[1])):
            v.sort(key=lambda r: num(r, "phaseRate"))
            rate = num(v[len(v) // 2], "phaseRate")
            spd = num(v[len(v) // 2], "actual")
            if rate > 1.25 or rate < 0.8:
                rows.append((clip, len(v), spd, rate, spd / rate if rate > 0 else 0))
        report("【②b 片段被快放】播放倍率 >1.25 就是肉眼可见的快放，腿摆得比动捕快那么多。"
               "\n     只列超标的；自然速度是反推出来的（实测速度 ÷ 倍率）", rows,
               lambda x: "%-24s %5d帧　实测 %.2f m/s　倍率 %.2f×　片段自然速度 %.2f"
                         % (x[0], x[1], x[2], x[3], x[4]))

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
    #
    # 【判据要排掉两类"本来就该整个身体一起演"的】否则这一条会一直有几十段，
    # 而里面绝大多数是正常的，真正的问题反倒被淹掉：
    #   · 位移型动作：起跳/下落/落地/翻滚/突进斩/跃劈/飞踢——这些招的主体就是
    #     "人在空中或在冲"，腿当然归招式管；
    #   · 受击/倒地/死亡：被打就该整个人失控，那是打击感，不是打架。
    # 剩下的才是真正的"腿在演招式、人却还在跑"。
    # 另外读 upperOnly 列：为 1 表示上半身遮罩已经生效，那一段腿是归移动层的，
    # 不算冲突——这一列是这一版新加的，老日志里没有，缺列按 0 处理。
    WHOLE_BODY_OK = (
        "Jumping Up", "Falling Idle", "Falling To Landing", "Hard Landing",
        "Stand To Roll", "Standing Dodge Left", "Dodging Right",
        "Great Sword Slide Attack", "Great Sword Jump Attack", "Flying Kick",
        "Great Sword Slash 3",
        "Great Sword Impact", "Great Sword Impact 2", "Great Sword Impact 4",
        "Great Sword Block Hit", "Knocked Down", "Stunned",
    )

    def real_overlay(r):
        if num(r, "actionW") <= 0.5 or num(r, "actual") <= 1.5:
            return False
        if num(r, "upperOnly") > 0.5:
            return False
        clip = (r.get("actionClip") or "").strip()
        for ok in WHOLE_BODY_OK:
            if ok in clip:
                return False
        return True

    over = spans(st, real_overlay)
    report("【⑥ 位移中动作层接管】动作层是**替换**移动层的：这段时间里腿在演招式，"
           "人却还在跑——这就是\"多个动画在打架\"。"
           "\n     已排除位移型动作（跳/落/滚/突进）与受击倒地，也排除已开上半身遮罩的段落",
           over,
           lambda b: "%.2f→%.2f 秒　动作 %s 权重%.2f　速度%.1f"
                     % (b[0], b[1], b[2].get("actionClip") or "-",
                        num(b[2], "actionW"), num(b[2], "actual")))

    # ---- ⑥b 谁在推角色 ----
    #
    # 玩家的原话是"更像是被动画控制了"。这一条把它变成数字：本帧除了玩家的输入，
    # 还有谁在挪人、挪了多少、当量速度是多少。extMove/extSrc 是这一版新加的列，
    # 老日志没有——缺列时整条跳过，不要拿 0 去冒充"没有外力"。
    if "extMove" in (st[0] if st else {}):
        ext = [r for r in st if num(r, "extMove") > 0.001]
        by = {}
        for r in ext:
            src = (r.get("extSrc") or "?").strip('"')
            d = num(r, "extMove")
            sim = max(num(r, "dtSim"), 1e-4)
            cur = by.setdefault(src, [0, 0.0, 0.0])
            cur[0] += 1
            cur[1] += d
            cur[2] = max(cur[2], d / sim)
        rows = sorted(by.items(), key=lambda kv: -kv[1][1])
        report("【⑥b 谁在推角色】外部位移＝不是玩家输入产生的那部分。"
               "\n     当量速度远超冲刺速度(5.2)就是玩家说的\"魔法般改变位置\"",
               rows,
               lambda kv: "%-10s %4d 帧　累计 %6.2fm　峰值当量速度 %6.1f m/s"
                          % (kv[0], kv[1][0], kv[1][1], kv[1][2]))

        snap = [r for r in st if num(r, "faceSnap") > 0.5]
        big = [r for r in snap if num(r, "faceSnap") > 30]
        report("【⑥c 出招强制转向】自动瞄准每按一次攻击就掰一次朝向。"
               "\n     只列 >30° 的：那已经不是\"修正一点偏差\"，是替玩家决定朝哪打",
               big[:12],
               lambda r: "%.2f 秒　掰了 %.0f°　姿态 %s" %
                         (num(r, "t"), num(r, "faceSnap"), r.get("pose") or "-"))
        if snap:
            print("     （出招转向共 %d 次，其中 >30° 的 %d 次，占 %.0f%%）"
                  % (len(snap), len(big), 100.0 * len(big) / len(snap)))

    # ---- ⑥d 接地抖动 ----
    flips = sum(1 for i in range(1, len(st)) if st[i].get("grounded") != st[i - 1].get("grounded"))
    dur = max(num(st[-1], "t") - num(st[0], "t"), 1e-3) if st else 1.0
    air = sum(1 for r in st if r.get("grounded") == "0")
    print("\n【⑥d 接地抖动】grounded 每秒翻转 %.1f 次，离地帧占 %.1f%%" %
          (flips / dur, 100.0 * air / max(1, len(st))))
    print("     纯水平的 Move 会让 CharacterController 判成离地。翻转频繁＝有人绕过"
          "\n     统一位移通道单独挪了人，落地/起跳姿态与踩地校准都会跟着乱。")

    # ---- ⑥e 顿帧 / 时间缩放 ----
    #
    # 命中顿帧把 Time.timeScale 打到 0.07，于是 Time.deltaTime 只剩 1 毫秒。
    # 凡是"位移 ÷ dt"的换算在这一刻都会炸——喂给动画层的地面速度尤其致命，
    # 它决定走/跑/冲刺档的混合与每条片段的播放速率。分母一小，腿就狂蹬。
    if st and "dtSim" in st[0]:
        sc = [(num(r, "dtSim") / max(num(r, "dt"), 1e-6), r) for r in st if num(r, "dt") > 1e-4]
        low = [x for x in sc if x[0] < 0.9]
        if sc:
            print("\n【⑥e 顿帧/时间缩放】timeScale < 0.9 的帧占 %.1f%%，最低 %.3f" %
                  (100.0 * len(low) / len(sc), min(x[0] for x in sc)))
            fast = [r for s2, r in low if num(r, "actual") > 12]
            if fast:
                print("     其中 actual > 12 m/s 的有 %d 帧（正常冲刺 5.2），峰值 %.0f m/s"
                      % (len(fast), max(num(r, "actual") for r in fast)))
                print("     —— 这就是顿帧里腿狂蹬的直接证据：分母是那个 1 毫秒的 dt。")
            else:
                print("     顿帧期间 actual 没有异常放大（除数地板生效）。")

    # ---- ⑦ 锁脚顶格 ----
    fix = spans(st, lambda r: num(r, "footFix") > FOOTFIX_MAX)
    report("【⑦ 锁脚修正顶格】封顶 0.28m。常年顶格说明步幅同步本身有问题，"
           "锁脚只是在硬拽", fix,
           lambda b: "%.2f→%.2f 秒　修正 %.2fm　速度%.1f" %
                     (b[0], b[1], num(b[2], "footFix"), num(b[2], "actual")))

    # ---- ⑦b 镜头姿态突跳 ----
    # 【这一条是补上来的：上一轮我漏掉的正是它】
    # 实机日志里 camLift 在 0.000 与 0.550 之间逐帧横跳，俯角跟着一帧摆 80~95 度，
    # 而我当时只看"见自己"的占比，没看**镜头姿态本身稳不稳**，于是完全没发现。
    # 任何镜头量逐帧大幅跳变都是不能接受的，不管角色在不在画面里。
    jumps = []
    for i in range(1, len(st)):
        if num(st[i], "dt") > 0.1:      # 切后台/长卡顿那一帧不算突跳
            continue
        dp = abs(num(st[i], "camPitch") - num(st[i - 1], "camPitch"))
        dl = abs(num(st[i], "camLift") - num(st[i - 1], "camLift"))
        if dp > 25.0 or dl > 0.25:
            jumps.append((st[i], dp, dl))
    report("【⑦b 镜头姿态突跳】俯角一帧变 >25° 或抬高一帧变 >0.25m。"
           "\n     镜头量逐帧大幅跳变一定会被看见，与角色在不在画面里无关",
           jumps,
           lambda j: "%.2f 秒　俯角跳 %.0f°（→%.0f°）　抬高跳 %.2f（→%.2f）　吊杆%.2f"
                     % (num(j[0], "t"), j[1], num(j[0], "camPitch"), j[2],
                        num(j[0], "camLift"), num(j[0], "camBoom")))

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
