# Quaternius Universal Animation Libraries — Anims2

License: CC0 1.0 Universal. See `QUATERNIUS_UAL_CC0_LICENSE.txt` in this directory.

## UAL1 — Universal Animation Library

Official source: https://quaternius.com/packs/universalanimationlibrary.html

The publisher describes UAL1 as a 120+ humanoid-animation library compatible with Unity and common humanoid rigs. It covers locomotion, combat, gun actions, sitting, emotes and other general actions.

Assets retained here:
- `UAL1_Standard.fbx` — in-place / standard variant (already present)
- `UAL1_Standard_RM.fbx` — root-motion variant

Original UAL1 standard import used the pinned mirror commit `8456155dbae7eb861f553a2341871ccae633c857` from `IAFahim/quaternius.universalAnimationLibrary.standard`.

## UAL2 — Universal Animation Library 2

Official source: https://quaternius.com/packs/universalanimationlibrary2.html

The publisher describes UAL2 as a 130+ animation library that complements UAL1 with additional melee and armed combos, 3/4-hit combo pieces and recoveries, parkour, zombie locomotion and other actions.

Assets retained here:
- `UAL2_Standard.fbx` — in-place / standard variant
- `UAL2_Standard_RM.fbx` — root-motion variant

For reproducible binary import, the UAL1 RM and UAL2 FBX files were copied from the public mirror `IBimsHedebe/The-last-World` pinned at commit `40ff2658496c7f69e0c50482b2a8f8153cc7d261`.

Pinned source Git blob / size records:
- `UAL1_Standard_RM.fbx`: blob `37feb1b854c16231fccd938116c4c5652cc617da`, 23,767,852 bytes
- `UAL2_Standard.fbx`: blob `4848c23dc7183a21ad9c05912f9385c50a07486e`, 24,778,332 bytes
- `UAL2_Standard_RM.fbx`: blob `db7b010f4917c9a49103df35e04de2e5d7c2fef3`, 24,805,740 bytes

Only asset files are being added here; no runtime combat/state-machine integration is implied by their presence in `Anims2`.
