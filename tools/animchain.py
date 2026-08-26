#!/usr/bin/env python3
"""动画引用链审计：文件 → 加载 → 映射 → 触发，四层逐条对账。

【为什么需要它】
既有的 animaudit.py 只查"目录里的 FBX 有没有被提到"，resolvesim.py 只查
"片段名能不能解析到槽位"。两者都止步于**映射**层。而实际踩到的坑是：
Left Turn 90 / Quick 180 Turn 文件在、加载了、映射了 PoseState，
却因为触发条件写成"站定 0.2 秒以上"而在跑动中永远播不到——
四层里前三层全绿，第四层是死的，没有任何工具能报出来。

【四层】
  L1 文件    Resources/Characters/Anims{,2} 下的 FBX
  L2 加载    进了 LibraryFiles，或被 PickFile(byName,key,File) 按路径取
  L3 映射    命中 ActionMap/UnarmedMap 的候选键，或方向环的某个槽位
  L4 触发    玩法代码里出现过对应的 PoseState（SetPose / 变量赋值皆算）

用法：python3 tools/animchain.py [-v]
"""
import re, sys, glob, io, os, pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
SRC  = ROOT / "Assets/_Project/Scripts"
PA   = SRC / "Combat/PlayableAnimator.cs"
DIRS = [ROOT / "Assets/_Project/Resources/Characters/Anims",
        ROOT / "Assets/_Project/Resources/Characters/Anims2"]

def norm(s): return (s or "").strip().lower()

def read(p): return io.open(p, encoding="utf-8").read()

def strip_comments(src):
    """去掉 // 行注释（保留字符串里的 //，这里够用：表里没有含 // 的字面量）"""
    out = []
    for line in src.split("\n"):
        i = line.find("//")
        out.append(line if i < 0 else line[:i])
    return "\n".join(out)

# ---------- L1：目录里的 FBX ----------
files = {}                      # norm(文件名) -> 相对路径
for d in DIRS:
    for f in sorted(glob.glob(str(d / "*.fbx"))):
        stem = os.path.splitext(os.path.basename(f))[0]
        files.setdefault(norm(stem), os.path.relpath(f, ROOT))

pa = read(PA)
pac = strip_comments(pa)

# ---------- L2：LibraryFiles + PickFile 的按路径取 ----------
m = re.search(r"LibraryFiles\s*=\s*\{(.*?)\};", pac, re.S)
library = set(norm(x) for x in re.findall(r'"([^"]+)"', m.group(1))) if m else set()
# PickFile(byName, "key", "File")：第三参是文件名，同样构成一条加载通路
pickfile = {}                   # norm(file) -> norm(key)
for key, fil in re.findall(r'PickFile\(\s*byName\s*,\s*"([^"]+)"\s*,\s*"([^"]+)"\s*\)', pac):
    pickfile[norm(fil)] = norm(key)
# walk / run / idle 这类由 Pick(byName, ...) 直接取的基础片段
# 【必须按调用分组保序】Pick 是"先把所有候选各试一次精确，再试包含"，
# 所以候选的**顺序**决定了结果。拍平成集合会丢掉这一点，让已经被精确候选
# 钉死的链条里的模糊候选也被当成歧义报出来（我第一版就报了 5 条这样的假警）。
basecalls = []                  # [[key...], ...] 每个 Pick 调用一组，保序
for grp in re.findall(r'Pick\(\s*byName\s*,\s*((?:"[^"]+"\s*,?\s*)+)\)', pac):
    basecalls.append([norm(k) for k in re.findall(r'"([^"]+)"', grp)])
basekeys = set(k for g in basecalls for k in g)

# 【L2 的正确模型】Build() 用的是 Resources.LoadAll(folder)，把目录里**所有**片段
# 都加载进 byName，键是 Norm(clip.name)；而 MixamoImportPostprocessor 在导入时
# 就把 Resources/Characters/ 下每个 FBX 的片段按【文件名】重命名了
#（clips[i].name ← Path.GetFileNameWithoutExtension）。
# 所以目录里的文件**全部**是加载得到的，LibraryFiles 只是给"片段名还留着
# mixamo.com"（没跑过导入器）的文件兜底的第二条通路，不是必经之路。
#
# 初版我把"没进 LibraryFiles"判成"加载不到"，报出 37 条假阳性——
# 而这个错我在 resolvesim.py 里已经犯过并改正过一次，这次又犯了。
loaded = set(files)
unreachable = sorted(f for f in files if f not in library and f not in pickfile)

# ---------- L3：ActionMap / UnarmedMap 的候选键 ----------
def parse_map(name):
    mm = re.search(name + r"\s*=\s*\{(.*?)\n        \};", pac, re.S)
    if not mm: return []
    body, out = mm.group(1), []
    for call in re.finditer(r"\b(A|AP)\(\s*PoseState\.(\w+)\s*,(.*?)\)\s*,\s*(?=\n|$)", body, re.S):
        keys = re.findall(r'"([^"]+)"', call.group(3))
        out.append((call.group(2), [norm(k) for k in keys]))
    return out

def parse_map_full(name):
    """同 parse_map，但把 speed/start/end/hold/pool 也读出来（供 --table）。"""
    mm = re.search(name + r"\s*=\s*\{(.*?)\n        \};", pac, re.S)
    if not mm: return []
    body, out = mm.group(1), []
    for call in re.finditer(r"\b(A|AP)\(\s*PoseState\.(\w+)\s*,(.*?)\)\s*,\s*(?=\n|$)",
                            body, re.S):
        kind, pose, rest = call.group(1), call.group(2), call.group(3)
        nums = re.findall(r"(-?\d+(?:\.\d+)?)f", rest)
        hold = bool(re.search(r"\btrue\b", rest.split('"')[0]))
        keys = [norm(k) for k in re.findall(r'"([^"]+)"', rest)]
        sp, st, en = (nums + ["?", "?", "?"])[:3]
        out.append((pose, keys, sp, st, en, hold, kind == "AP"))
    return out

armed_rows   = parse_map_full("ActionMap")
unarmed_rows = parse_map_full("UnarmedMap")
action_rows  = armed_rows + unarmed_rows

# 方向环行：Add(PickFile(byName,"key","File"), 角度, 档, 斜向) / Add(walk|run, ...)
# `var walk = Pick(byName, "…", "…")` —— 环里的 Add(walk, …) 传的是**已解析好的
# 变量**，不是搜索键。初版直接拿变量名去做包含匹配，"walk" 命中了
# Crouch Walk Back，报出一条根本不存在的错。变量名必须映射回它自己的候选链。
varkeys = {}
for m in re.finditer(r'\bvar\s+(\w+)\s*=\s*Pick\(\s*byName\s*,\s*((?:"[^"]+"\s*,?\s*)+)\)', pac):
    varkeys[m.group(1)] = [norm(k) for k in re.findall(r'"([^"]+)"', m.group(2))]

ring_rows = []
for m in re.finditer(r'Add\(\s*(?:PickFile\(\s*byName\s*,\s*"([^"]+)"\s*,\s*"([^"]+)"\s*\)'
                     r'|(\w+))\s*,\s*(-?[\d.]+)f\s*,\s*(\w+)', pac):
    key = m.group(1) or m.group(3)
    disp = m.group(2) or m.group(3)
    ring_rows.append((m.group(4), m.group(5), key, disp))

action_map  = parse_map("ActionMap")
unarmed_map = parse_map("UnarmedMap")

# 方向环槽位（Add(PickFile(...)) 与 Add(walk/run,...)）
dir_keys = set(pickfile.values()) | basekeys

def resolve(keys):
    """复刻 Pick 的解析：先精确，再包含。返回命中的文件名（norm）。"""
    for k in keys:
        if k in files: return k
    for k in keys:
        for f in files:
            if k in f: return f
    return None

mapped = {}                     # norm(file) -> [用途...]
pose_clip = {}                  # PoseState -> norm(file) 或 None
for pose, keys in action_map:
    hit = resolve(keys)
    pose_clip[pose] = hit
    if hit: mapped.setdefault(hit, []).append("招式 " + pose)
for pose, keys in unarmed_map:
    hit = resolve(keys)
    if hit: mapped.setdefault(hit, []).append("空手 " + pose)
    # 变体池 AP：池里每一条都要接
for pose, keys in action_map + unarmed_map:
    for k in keys:
        if k in files: mapped.setdefault(k, []).append("候选 " + pose)
for k in dir_keys:
    hit = resolve([k])
    if hit: mapped.setdefault(hit, []).append("移动环")

# ---------- L3 的【第二条通路】：按名字直接播，完全绕过 ActionMap ----------
# HumanoidAnimator / PlayableAnimator 暴露了一组 by-name 接口：
#   HasClip / PlayRestClip / PlayNamed / PlayClip / PlayClipContaining / PlayFirstClip
# 坐、躺、起身、拔刀、收刀、武器架都走这条路。初版审计只建模了 ActionMap 一条，
# 于是把这十来个片段全判成"没有用途"——**又是一批假阳性**。
# 【口径：保守】这条通路的间接形式太多——直接字面量、const、
# static readonly string[] XxxKeys、变量传参、LoopClip 包装……
# 逐个建模的结果是我连报了两轮假阳性（先把 37 个判成"加载不到"，
# 又把 16 个判成"没用途"，其实都在用）。
# 所以改成保守口径：**全库任意字符串字面量**只要能解析到某个片段，
# 就算它被引用。宁可漏报一两个真闲置，也不再冤枉正在用的片段。
# 片段名足够独特（"Crouch Walk Back" 这类），误判风险很低。
direct = set()
for f in glob.glob(str(SRC / "**/*.cs"), recursive=True):
    src_raw = read(f)
    if f.endswith("PlayableAnimator.cs"):
        # 它自己也有 by-name 直播（PlayNamed("standing up") 等），必须扫；
        # 但要先剜掉 LibraryFiles——那张表只代表"加载得到"，不代表"被用到"，
        # 混进来会把闲置片段一律洗白（初版把 Standing Up 误报成闲置就是因为反过来整个跳过了它）。
        src_raw = re.sub(r"LibraryFiles\s*=\s*\{.*?\};", "", src_raw, flags=re.S)
    # 【必须限制在单行内】用 [^"]{3,} 会跨行匹配：文件里任何一处落单的引号
    # （转义引号、字符字面量 '"'）都会让配对错位，把整个文件吞成一个"字面量"，
    # 于是真正的片段名一个都提不出来——我第一版就是这么把 6 个在用的片段
    # 误报成"没有用途"的。加 \n 到排除集即可。
    for lit in re.findall(r'"([^"\n]{3,})"', strip_comments(src_raw)):
        direct.add(norm(lit))
for k in direct:
    hit = resolve([k])
    if hit: mapped.setdefault(hit, []).append("按名引用")

# ---------- L4：玩法代码里出现过的 PoseState ----------
enum_src = ""
for f in glob.glob(str(SRC / "**/*.cs"), recursive=True):
    s = read(f)
    if "enum PoseState" in s: enum_src = s
all_poses = []
em = re.search(r"enum PoseState\s*\{(.*?)\}", enum_src, re.S)
if em:
    for line in em.group(1).split("\n"):
        line = line.split("//")[0]
        for w in re.findall(r"\b([A-Z]\w+)\b", line): all_poses.append(w)

triggered = {}                  # PoseState -> [文件:行]
for f in sorted(glob.glob(str(SRC / "**/*.cs"), recursive=True)):
    rel = os.path.relpath(f, ROOT)
    if rel.endswith("PlayableAnimator.cs") or "SimpleAnimator.cs" in rel: continue
    for i, line in enumerate(read(f).split("\n"), 1):
        code = line.split("//")[0]
        for p in re.findall(r"PoseState\.(\w+)", code):
            triggered.setdefault(p, []).append(f"{rel}:{i}")

# ---------- 对齐表（--table）：哪个动作由哪条动画合成、参数是什么 ----------
# 用意很直接：这张表必须是**从代码里读出来的**，不是手写的。
# 手写的表第二天就和代码不一致，而"动画接错了"恰恰是不一致造成的。
def emit_table():
    print("# 动作 ↔ 动画 对齐表（由 tools/animchain.py 从源码生成）")
    print()
    print("## 一、移动层：方向环")
    print()
    print("移动层是**按行进夹角在一圈片段之间混合**，不是"
          "\"一个状态播一条\"。同一时刻通常有 2 条片段各占权重，")
    print("并且共享同一个归一化步态相位；每条片段的播放速率 = 实际速度 / 该片段实测自然速度。")
    print()
    print("| 档 | 角度 | 片段 | 在库 |")
    print("|---|---|---|---|")
    tiername = {"0": "0 走", "1": "1 慢跑", "2": "2 跑", "3": "3 冲刺", "CrouchTier": "蹲伏"}
    for ang, tier, key, disp in ring_rows:
        hit = resolve(varkeys.get(key, [norm(key)]))
        print("| %s | %s° | %s | %s |" % (tiername.get(tier, tier), ang,
              files[hit] if hit else "（缺）**" + disp + "**", "✔" if hit else "✘"))
    print()
    print("## 二、动作层：招式 / 过渡 / 状态")
    print()
    print("动作层**替换**移动层的输出（不是叠加）。所以只有"
          "\"整段身体都归它\"的动作才该走这一层；")
    print("移动过渡（起步/急停/转身）不属于这里——盖上去就会和位移打架。")
    print()
    def dump(rows):
        print("| 姿态 | 片段 | 倍速下限 | 起手裁切 | 收招裁切 | 可保持 | 变体池 | 玩法触发 |")
        print("|---|---|---|---|---|---|---|---|")
        for pose, keys, sp, st, en, hold, pool in rows:
            hit = resolve(keys)
            t = triggered.get(pose, [])
            print("| %s | %s | %s | %s | %s | %s | %s | %d 处 |" % (
                pose, os.path.basename(files[hit])[:-4] if hit else "**（缺）**",
                sp, st, en, "是" if hold else "", "是" if pool else "", len(t)))
        print()
    dump(armed_rows)
    print("### 空手（收刀后用这一套，避免\"凭空攥着一把剑\"）")
    print()
    dump(unarmed_rows)

    # ---- 方向环的缺口：哪些档缺哪些方向 ----
    print("## 三、方向环的缺口（补片段即自动生效，无需改代码）")
    print()
    want = [-135, -90, -45, 0, 45, 90, 135, 180]
    have = {}
    for ang, tier, key, disp in ring_rows:
        if resolve(varkeys.get(key, [norm(key)])):
            have.setdefault(tier, set()).add(int(float(ang)))
    print("| 档 | 已有方向 | 缺的方向 |")
    print("|---|---|---|")
    for tier in ["0", "1", "2", "3", "CrouchTier"]:
        h = sorted(have.get(tier, set()))
        gap = [a for a in want if a not in h]
        print("| %s | %s | %s |" % (tiername.get(tier, tier),
              " ".join("%d°" % a for a in h) or "—",
              " ".join("%d°" % a for a in gap) or "（齐）"))
    print()
    print("缺的方向不会留空——`BlendDirectional` 会在相邻两条之间插值、"
          "或退到 `NearestCoveringTier` 借上下档的片段。")
    print("所以缺口表现为\"这个方向的步子不太对\"，而不是\"这个方向不动\"。")
    print()
    print("两点必须说清楚，免得照着这张表下判断时判错：")
    print()
    print("1. **斜向那两条的角度是运行时实测的，不是表里写的。**"
          " `Jog Forward/Backward Diagonal` 在 `Add(..., measured: true)` 下")
    print("   会用 `clip.SampleAnimation` 量髋部位移反推真实角度——"
          "Mixamo 的斜向片段是左前还是右前各家素材不一样。")
    print("   所以 45°/135° 这两行**实际可能落在 −45°/−135°**。"
          "屏幕上第四行 HUD 打的是真正在播的片段名，以它为准。")
    print("2. **左右不对称是真的。** 慢跑档只有一侧有专用斜向片段，"
          "另一侧靠 0° 与 ±90° 插值补出来——")
    print("   于是同样是斜着跑，一边有专用步法、一边是混出来的，"
          "读作\"往一边跑更自然\"。")
    print("   补法是去 Mixamo 把那条斜向片段勾上 Mirror 再下一份，"
          "丢进 `Anims/` 即可，代码不用动。")
    print()
    print("## 四、参数含义")
    print()
    print("- **倍速下限**：真正的播放速度由招式帧数据反推，这里只是下限（见 PlayIndex）。")
    print("- **起手裁切 / 收招裁切**：片段的 [start, end] 归一化区间。"
          "Mixamo 原始片段前 12~22% 是摆架势、尾部 20~32% 是走回站姿，都要裁掉。")
    print("- **可保持**：播到 end 处停住不回落（格挡、蓄力、倒地、死亡）。")
    print("- **变体池**：同一姿态挂多条，每次轮换（受击、斩击、掉头）。")

# ---------- 报告 ----------
verbose = "-v" in sys.argv
if "--table" in sys.argv:
    emit_table()
    sys.exit(0)
bad = 0
print(f"L1 目录 FBX {len(files)} 个 | L2 已加载 {len(loaded)} | L3 已映射 {len(mapped)}")
print()

print("【L1→L2】加载通路：目录内全部经 Resources.LoadAll 按文件名加载（见脚本注释）")
print(f"  仅靠 LoadAll 的 {len(unreachable)} 个 / 另有 LibraryFiles 兜底的 {len(files)-len(unreachable)} 个")
print("  （两条通路都通，此层无问题）")
print()

print("【L2→L3】加载了，却没有任何用途（白占内存与 Playable 槽位）")
miss_map = sorted(f for f in loaded if f not in mapped)
for f in miss_map: print(f"  ✗ {files[f]}")
if not miss_map: print("  （无）")
bad += len(miss_map)
print()

print("【L3→L4】有片段、但玩法代码从不触发这个姿态（永远播不到）")
dead = sorted(p for p, c in pose_clip.items() if c and p not in triggered)
for p in dead: print(f"  ✗ PoseState.{p}  片段 {files[pose_clip[p]]}")
if not dead: print("  （无）")
bad += len(dead)
print()

print("【L4→L3】玩法代码触发了，却没有任何片段（静默空转）")
noclip = sorted(p for p in triggered if p in [x for x, _ in action_map] and not pose_clip.get(p))
extra  = sorted(p for p in triggered if p in all_poses and p not in [x for x, _ in action_map]
                and p not in ("Idle", "Locomotion"))
for p in noclip: print(f"  ✗ PoseState.{p}  候选键一个都没命中")
for p in extra:  print(f"  ⚠ PoseState.{p}  不在 ActionMap 里（{triggered[p][0]}）")
if not noclip and not extra: print("  （无）")
bad += len(noclip)
print()

if verbose:
    print("【明细】每个姿态的落位与触发点")
    for p, c in sorted(pose_clip.items()):
        t = triggered.get(p, [])
        print(f"  {p:<16} 片段={files[c] if c else '—':<58} 触发={len(t)} 处"
              + (f" 例:{t[0]}" if t else ""))
    print()

# ---------- L3b：模糊匹配歧义（本轮真正的大发现） ----------
# Pick 的兜底是"包含匹配"，而它在 Dictionary 上遍历取第一个命中——
# .NET 不保证遍历顺序。一个键若有多个包含候选，选中哪条是不确定的。
# 本轮就是这样发现 idle / walk / run / great sword slash 四条最基础的片段
# 全靠这条兜底命中的；walk/run 还会被直接当成方向环的 0°（正前）槽位，
# 万一命中 Walking Backwards，真正的后退片段会被判重复丢弃，走档就没有正前方了。
print("【L3b】候选键没有精确文件、且模糊匹配有多个候选（选中哪条不确定）")
amb = 0
for pose, keys in action_map + unarmed_map:
    for k in keys:
        if k in files: break                     # 有精确命中，这条链就定了
        cands = sorted(f for f in files if k in f)
        if len(cands) > 1:
            print(f"  ⚠ {pose:<14} 键 \"{k}\" 有 {len(cands)} 个候选: " + ", ".join(cands[:4]))
            amb += 1
        if cands: break
for grp in basecalls + [[k] for k in sorted(pickfile.values())]:
    for k in grp:
        if k in files: break                     # 精确命中，这条链定了
        cands = sorted(f for f in files if k in f)
        if len(cands) > 1:
            print(f"  ⚠ {'移动/基础':<14} 键 \"{k}\" 有 {len(cands)} 个候选: " + ", ".join(cands[:4]))
            amb += 1
        if cands: break
if amb == 0: print("  （无）")
bad += amb
print()

print(f"合计问题 {bad} 处")
sys.exit(1 if bad else 0)
