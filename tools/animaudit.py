#!/usr/bin/env python3
"""动作库使用审计：Anims / Anims2 里每一个 FBX，代码里到底有没有按名字用到。

查找依据与运行时一致——PlayableAnimator 全部按【片段名/文件名】做字符串匹配，
所以"代码里是否出现这个名字的字符串字面量"就是"这个文件是否被用上"的准确判据。
"""
import os, re, glob, sys

ROOT = "Assets/_Project/Resources/Characters"
SRC  = "Assets/_Project/Scripts"

lits = set()
for dp, _, fns in os.walk(SRC):
    for fn in fns:
        if not fn.endswith(".cs"): continue
        t = open(os.path.join(dp, fn), encoding="utf-8", errors="ignore").read()
        for m in re.findall(r'"([^"\\\n]{2,80})"', t):
            lits.add(m.strip().lower())

def clip_names(fbx):
    """这个文件在 Unity 里可能以哪些名字被找到。"""
    base = os.path.splitext(os.path.basename(fbx))[0]
    names = {base.lower()}
    if "@" in base: names.add(base.split("@", 1)[1].lower())
    return names

used, unused = [], []
for d in ("Anims", "Anims2"):
    for p in sorted(glob.glob(os.path.join(ROOT, d, "*.fbx"))):
        names = clip_names(p)
        hit = sorted(n for n in names if n in lits)
        (used if hit else unused).append((os.path.join(d, os.path.basename(p)), hit))

print("=== 已接入 %d 条 ===" % len(used))
for f, h in used: print("  ✓ %-52s <- \"%s\"" % (f, h[0]))
print()
print("=== 未接入 %d 条 ===" % len(unused))
for f, _ in unused: print("  ✗ " + f)
sys.exit(1 if unused else 0)
