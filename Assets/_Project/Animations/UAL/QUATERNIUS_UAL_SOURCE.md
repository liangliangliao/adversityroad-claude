# Quaternius Universal Animation Library 1 (UAL1)

License: CC0 1.0 Universal. See `QUATERNIUS_UAL_CC0_LICENSE.txt` in this directory.

## UAL1 — Universal Animation Library

Official source: https://quaternius.com/packs/universalanimationlibrary.html

The publisher describes UAL1 as a 120+ humanoid-animation library compatible with Unity and common humanoid rigs. It covers locomotion, combat, gun actions, sitting, emotes and other general actions.

Assets retained here:
- `UAL1_Standard.fbx` — in-place / standard variant (already present)
- `UAL1_Standard_RM.fbx` — root-motion variant

Original UAL1 standard import used the pinned mirror commit `8456155dbae7eb861f553a2341871ccae633c857` from `IAFahim/quaternius.universalAnimationLibrary.standard`.

Pinned source Git blob / size record:
- `UAL1_Standard_RM.fbx`: blob `37feb1b854c16231fccd938116c4c5652cc617da`, 23,767,852 bytes
  (copied from the public mirror `IBimsHedebe/The-last-World`, pinned at commit
  `40ff2658496c7f69e0c50482b2a8f8153cc7d261`)

UAL2 lives in `Assets/_Project/Animations/UAL2/`; see the SOURCE.md there.

This directory is deliberately **outside** `Resources/`. `UAL1_Standard.fbx` is
baked offline by `Editor/UalRetargetBaker.cs` into `Resources/Characters/AnimsUAL`,
and only those baked clips are loaded at runtime. See `README.txt`.
