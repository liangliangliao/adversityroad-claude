#!/usr/bin/env python3
"""复刻 PlayableAnimator.Build 的解析顺序，静态推出每个 FBX 最终落到哪个槽位。

只靠"代码里出现过这个名字"是不够的：候选链有先后、connected 去重会让后来者
拿不到已被占用的片段、方向环还会按 (档位, 角度) 丢掉等价片段。
这个脚本按运行时的同一套规则跑一遍，把"名字写了但永远轮不到"的情况揪出来。
"""
import os, re, glob, sys

ROOT = "Assets/_Project/Resources/Characters"
CS   = "Assets/_Project/Scripts/Combat/PlayableAnimator.cs"
src  = open(CS, encoding="utf-8").read()

# ---------- byName：Unity 能给出的片段名 ----------
def meta_clip_name(fbx):
    m = fbx + ".meta"
    if not os.path.exists(m): return None
    t = open(m, encoding="utf-8", errors="ignore").read()
    g = re.search(r"clipAnimations:\s*\n\s*-\s*serializedVersion:.*?\n\s*name:\s*(.+)", t)
    return g.group(1).strip() if g else None

files = {}          # 文件相对名 -> 绝对路径
for d in ("Anims", "Anims2"):
    for p in sorted(glob.glob(os.path.join(ROOT, d, "*.fbx"))):
        files[os.path.join(d, os.path.basename(p))] = p

byname = {}         # key(小写) -> 文件相对名
order  = []
for rel, p in files.items():
    n = meta_clip_name(p)
    key = (n or "mixamo.com").lower()
    if key == "mixamo.com": continue          # Build 里被显式跳过
    if key not in byname: byname[key] = rel; order.append(key)

# LibraryFiles：按文件名补齐
lib = re.search(r"static readonly string\[\] LibraryFiles\s*=\s*\{(.*?)\n        \};", src, re.S).group(1)
libfiles = re.findall(r'"([^"]+)"', lib)
missing_lib = []
for f in libfiles:
    k = f.lower()
    if k in byname: continue
    hit = next((rel for rel in files if os.path.splitext(os.path.basename(rel))[0].lower() == k), None)
    if hit is None: missing_lib.append(f)
    else: byname[k] = hit

def pick(keys, taken=None):
    for k in keys:
        k = k.lower()
        if k in byname and (taken is None or byname[k] not in taken): return byname[k]
    for k in keys:
        k = k.lower()
        for kk, v in byname.items():
            if k in kk and (taken is None or v not in taken): return v
    return None

slots, taken = {}, set()
def claim(slot, rel):
    if rel is None: return
    slots.setdefault(rel, []).append(slot); taken.add(rel)

# ---------- 移动层的四个固定片段 ----------
for slot, keys in (("待机 idle", ["idle", "breathing idle", "standing idle"]),
                   ("走 walk", ["walking", "great sword walk", "walk"]),
                   ("跑 run", ["running", "great sword run", "run"]),
                   ("临战架势(持剑)", ["great sword idle", "fighting idle", "combat idle", "sword and shield idle"]),
                   ("临战架势(空手)", ["fighting idle", "combat idle"])):
    claim(slot, pick(keys))

# ---------- ActionMap（按源码顺序，含 connected 去重） ----------
body = src[src.index("static readonly ActionDef[] ActionMap"):]
body = body[:body.index("\n        };")]
# 把折行的候选串接回同一行（源码里长的 keys 会换行写）
body = re.sub(r',\s*\n\s+"', ', "', body)
for line in body.splitlines():
    m = re.match(r'\s*(A|AP)\(PoseState\.(\w+),[^,]+,[^,]+,[^,]+,(?:\s*(?:true|false),)?\s*(.+?)\),?\s*$', line)
    if not m: continue
    kind, pose, rest = m.group(1), m.group(2), m.group(3)
    keys = re.findall(r'"([^"]+)"', rest)
    if not keys: continue
    if kind == "AP":
        for k in keys:
            c = pick([k], taken)
            if c: claim("招式池 " + pose, c)
    else:
        claim("招式 " + pose, pick(keys, taken))

# ---------- UnarmedMap ----------
um = src[src.index("static readonly ActionDef[] UnarmedMap"):]
um = um[:um.index("\n        };")]
for line in um.splitlines():
    m = re.match(r'\s*A\(PoseState\.(\w+),.*?(".*")\),?\s*$', line)
    if not m: continue
    claim("空手替身 " + m.group(1), pick(re.findall(r'"([^"]+)"', m.group(2)), taken))

# ---------- 方向环（含 (档位,角度) 去重） ----------
cd = src[src.index("List<DirDef> CollectDirectional"):]
cd = cd[:cd.index("\n            return list;")]
ring, dropped = [], []
def add_dir(rel, angle, tier, label):
    if rel is None: return
    if any(r == rel for r, _, _ in ring): return
    for r, a, t in ring:
        if t == tier and abs((a - angle + 180) % 360 - 180) < 20:
            dropped.append((rel, label, r)); return
    ring.append((rel, angle, tier)); claim("方向环 " + label, rel)

TIER = {"0": "走", "1": "慢跑", "2": "跑", "3": "冲刺", "CrouchTier": "蹲伏"}
add_dir(pick(["walking", "great sword walk", "walk"]), 0.0, 0, "走 0°")
add_dir(pick(["running", "great sword run", "run"]), 0.0, 2, "跑 0°")   # Add(run,...) 在档2
for m in re.finditer(r'Add\(PickFile\(byName, "([^"]+)", "([^"]+)"\), (-?[\d.]+)f, (\w+),', cd):
    key, fname, angle, tier = m.group(1), m.group(2), float(m.group(3)), m.group(4)
    rel = byname.get(key.lower()) or byname.get(fname.lower())
    add_dir(rel, angle, {"0":0,"1":1,"2":2,"3":3,"CrouchTier":4}[tier],
            "%s %.0f°" % (TIER[tier], angle))

# ---------- 报告 ----------
print("=== 槽位分配 ===")
for rel in sorted(files):
    print("  %-52s %s" % (rel, " / ".join(slots.get(rel, [])) or "（仅按名字可播：休息/拔刀/预览）"))
print()
if missing_lib:
    print("!! LibraryFiles 里写了但目录里没有：", missing_lib)
if dropped:
    print("=== 方向环里被判为重复而未接入（等价片段）===")
    for rel, label, other in dropped: print("  · %-38s（%s 已由 %s 占据）" % (rel, label, other))
print()
# 只靠内部名/文件名都进不了 byName 的文件 = 运行时根本找不到
lost = [rel for rel, p in files.items()
        if (meta_clip_name(p) or "mixamo.com").lower() == "mixamo.com"
        and os.path.splitext(os.path.basename(rel))[0].lower() not in [f.lower() for f in libfiles]]
print("=== 运行时寻址不到的文件（没有命名 meta，且不在 LibraryFiles 里）===")
for rel in sorted(lost): print("  ✗ " + rel)
if not lost: print("  （无）")
sys.exit(1 if lost or missing_lib else 0)
