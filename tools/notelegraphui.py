#!/usr/bin/env python3
"""任何读敌人前摇状态来画屏幕提示的代码，必须受 GameDebug.TelegraphOverlays 管。

【为什么需要这条检查】产品要求是硬的：对敌人攻击的判断与预测只能来自
**敌人身体的动作**与**固定的节拍规律**，屏幕上不给任何文字或符号提示。
我按这条改过一次，只关了敌人头顶那一枚记号，却漏了 UI/ThreatIndicator——
它专门给画面外正在起手的敌人画箭头加「危」，于是玩家看到的是"「危」不断持续出现"。
两处属于完全不同的系统，靠人去记"还有没有第三处"是不可靠的。

判据：凡是引用 PerilousTelegraph / .Telegraphing 的源文件，
同一个文件里必须出现 TelegraphOverlays（即它确实过了那道开关）。
"""
import os, re, sys

ROOT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                    "Assets", "_Project", "Scripts")
READS = re.compile(r"PerilousTelegraph|\.Telegraphing\b")
GATE = "TelegraphOverlays"

bad = []
for dirpath, _, files in os.walk(ROOT):
    for fn in files:
        if not fn.endswith(".cs"):
            continue
        path = os.path.join(dirpath, fn)
        with open(path, encoding="utf-8") as fh:
            src = fh.read()
        hits = [i + 1 for i, ln in enumerate(src.splitlines()) if READS.search(ln)]
        if hits and GATE not in src:
            rel = os.path.relpath(path, ROOT)
            bad.append((rel, hits))

if bad:
    print("前摇提示层检查未通过——下列文件读了敌人的前摇状态，却没有经过 "
          + GATE + " 这道开关：")
    for rel, hits in bad:
        print("  %s  行 %s" % (rel, ", ".join(str(h) for h in hits)))
    print("屏幕上的文字/符号预警必须可以一次性关掉；漏掉一处，玩家看到的就是"
          "「危」不断出现。")
    sys.exit(1)
print("前摇提示层检查通过：读前摇状态的文件都受 " + GATE + " 管。")
