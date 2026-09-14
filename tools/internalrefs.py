#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
说明里点名的东西，场上必须真的有 —— CI 那道门禁的本地镜像。

【为什么要有它】
CIDiagnostics 里有一条「目标行/玩法说明指向核对」：第 9-26 章每一关的
playerObjective 与 howToPlay 里出现的每一个【X】，场上都必须真的有一块
写着 X 的牌子；否则玩家会满场去找一个不存在的名字。这条门禁是对的。

问题在于它住在 Assets/_Project/Editor 里，只有 CI 的 Unity 才跑得起来。
于是它每挡下一次，代价就是一整轮 push → 等二十分钟 → 看日志。
我自己就这么撞了一次：新的区域牌（起手位 / 资料带 / 工作带）确实在场上，
但那道门禁只认关键物的牌面，不认区域牌，判成了指向不存在的东西。

所以把同一条规则在本地镜像一遍。**规则本身不抄死**：
KindOf / GateNameFor 的关键词表是从 InternalProps.cs 里现读的，
区域名是从 InternalLayout.cs 里现读的——C# 改了，这里跟着变，
不会变成一份慢慢过时的副本。

这份镜像是照着 CI 的真实输出校准的：把区域名摘掉之后，它报出的三条
与那次 CI 日志逐字一致（含"场上只有：工作台/完成标准锁/提交台/修改项"）。
"""
import io, json, os, re, sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
PROPS = os.path.join(ROOT, "Assets/_Project/Scripts/InternalOS/InternalProps.cs")
LAYOUT = os.path.join(ROOT, "Assets/_Project/Scripts/InternalOS/InternalLayout.cs")
DATA = os.path.join(ROOT, "Assets/_Project/Resources/Chapters/internal_chapters_v22.json")

# Style() 的牌面：kind → 玩家看到的字
KIND_LABEL = {
    "GateConsole": "提交台", "DoneLock": "完成标准锁", "Checkpoint": "检查点",
    "DisengageGate": "脱离门", "Evidence": "证据台", "DecisionBoard": "判据板",
    "TriggerObject": "触发物", "ArchiveVault": "归档柜", "Decoy": "诱饵",
    "Cargo": "目标箱", "WorkStation": "工作台",
}
# GateLabel()：Gate 的 PF 名 → 牌面
GATE_LABEL = [
    ("LoadDock", "装车月台"), ("Release", "发布台"), ("Publish", "发布台"),
    ("Board", "上车口"), ("Deliver", "交付台"), ("Acceptance", "交付台"),
    ("Threshold", "门槛"), ("ExitGate", "终点门"), ("ExitTrigger", "终点门"),
]
# Cargo 那条规则里被排除掉的 "*Box"
BOX_EXCLUDE = ["Mailbox", "Inbox", "Outbox", "Toolbox", "Checkbox"]
# 【】里也可能是按键名，不是场上的东西（与 CIDiagnostics.ButtonNames 一致）
BUTTONS = ["用", "跳", "蹲", "闪", "挡", "锁", "术", "拔刀"]


def body(src, head):
    """截出 `head` 开头那个方法的方法体（到同缩进的收尾花括号为止）。"""
    at = src.index(head)
    rest = src[at:]
    return rest[: rest.index("\n        }")]


def load_rules():
    src = io.open(PROPS, encoding="utf-8").read()
    kind_src = body(src, "static InternalPropKind KindOf(")
    rules = []
    for m in re.finditer(r"if \((.*?)\)\s*\n\s*return InternalPropKind\.(\w+);", kind_src, re.S):
        rules.append((re.findall(r'"([^"]+)"', m.group(1)), m.group(2),
                      "Mailbox" in m.group(1)))
    fallback = re.findall(r"return InternalPropKind\.(\w+);", kind_src)[-1]

    gate_src = body(src, "static string GateNameFor(")
    gnames = []
    for m in re.finditer(r'if \((.*?)\)\s*\n?\s*return "([^"]+)";', gate_src, re.S):
        gnames.append((re.findall(r'Contains\("([^"]+)"\)', m.group(1)), m.group(2)))
    gdefault = re.findall(r'return "([^"]+)";', gate_src)[-1]
    return rules, fallback, gnames, gdefault


def zone_names():
    """区域牌的名字：从 InternalLayout 的 Zone* 常量现读。"""
    src = io.open(LAYOUT, encoding="utf-8").read()
    return re.findall(r'public const string Zone\w+\s*=\s*"([^"]+)"', src)


def kind_of(pf, rules, fallback):
    for words, kind, is_cargo in rules:
        if is_cargo:
            if any(w in pf for w in words if w not in BOX_EXCLUDE and w != "Box"):
                return kind
            if "Box" in pf and not any(e in pf for e in BOX_EXCLUDE):
                return kind
            continue
        if any(w in pf for w in words):
            return kind
    return fallback


def gate_label(pf):
    for key, label in GATE_LABEL:
        if key in pf:
            return label
    return "提交台"


def levels_of(doc):
    found = []

    def walk(o):
        if isinstance(o, dict):
            if o.get("levelId") and "reusedAssets" in o:
                found.append(o)
            for v in o.values():
                walk(v)
        elif isinstance(o, list):
            for v in o:
                walk(v)

    walk(doc)
    return found


def main():
    rules, fallback, gnames, gdefault = load_rules()
    zones = zone_names()
    levels = levels_of(json.load(io.open(DATA, encoding="utf-8")))

    bad = 0
    for lv in levels:
        pfs = []
        for m in re.finditer(r"PF_[A-Za-z0-9_]*", lv.get("reusedAssets") or ""):
            if m.group(0) not in pfs:
                pfs.append(m.group(0))

        labels, has_gate = [], False
        for pf in pfs:
            kind = kind_of(pf, rules, fallback)
            if kind == "GateConsole":
                labels.append(gate_label(pf))
                has_gate = True
            else:
                labels.append(KIND_LABEL.get(kind, kind))
        # PlanFor 的两处特例：9-3 的三个旧箱、缺 Gate 时的兜底
        if lv["levelId"] == "9-3":
            labels += ["旧箱"] * 3
        if not has_gate:
            v = (lv.get("realityVictory") or "") + (lv.get("coreMechanic") or "")
            pf = gdefault
            for words, name in gnames:
                if any(w in v for w in words):
                    pf = name
                    break
            labels.append(gate_label(pf))
        # 关卡循环自己摆的牌子（LoopLabelsFor）
        if lv["levelId"] in ("9-1", "9-2"):
            labels.append("修改项")
        # 区域牌（BuildInternalZoneSigns）
        labels += zones

        for text in [lv.get("playerObjective") or ""] + (lv.get("howToPlay") or []):
            for want in re.findall(r"【([^】]*)】", text):
                if want in labels or want in BUTTONS:
                    continue
                print("%s 的说明让玩家去找【%s】，但这一关摆出来的牌子只有：%s"
                      % (lv["levelId"], want, "/".join(labels)))
                bad += 1

    print("\n核对 %d 关的目标行与玩法说明，指向对不上的 %d 处" % (len(levels), bad))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
