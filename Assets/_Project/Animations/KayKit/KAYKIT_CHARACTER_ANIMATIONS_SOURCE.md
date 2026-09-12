# KayKit Character Animations — daily-behaviour subset

Creator: Kay Lousberg / KayKit

Official source: https://kaylousberg.itch.io/kaykit-character-animations

Official version used: **Free 1.1**

License: **CC0 1.0 Universal**

The official page describes the current free pack as 161 humanoid animations in FBX/GLTF, compatible with Unity and other engines. Its non-combat categories include general interactions, basic/advanced movement, simulation/emotes (including sitting and lying down), and tool/work actions. Version 1.1 added 28 Rig_Medium tool animations including chopping, digging, fishing, hammering, lockpicking, sawing, pickaxing and generic working actions.

## Imported subset

Only the non-combat Rig_Medium animation-set FBX files were imported into `Assets/_Project/Resources/Characters/Anims2`:

- `KayKit_Rig_Medium_General.fbx`
- `KayKit_Rig_Medium_MovementBasic.fbx`
- `KayKit_Rig_Medium_MovementAdvanced.fbx`
- `KayKit_Rig_Medium_Simulation.fbx`
- `KayKit_Rig_Medium_Tools.fbx`

The KayKit CombatMelee, CombatRanged and Special FBX sets were intentionally excluded because this import is a daily-behaviour supplement and the repository already has dedicated combat libraries.

## Reproducible binary transport

The official itch.io page is the licensing/source-of-truth page. Because the free itch download is served through an interactive download flow, the exact Free 1.1 FBX binaries were retrieved from a public GitHub mirror of the same package, pinned to commit:

- Mirror: `ArtjomSchwenk/Koy`
- Commit: `8742b69b6d965f369e7b8a87cee570a81184c403`
- Source directory: `Assets/Character/KayKit_Character_Animations_1.1/KayKit_Character_Animations_1.1/Animations/fbx/Rig_Medium`

Source Git blob SHAs verified before import:

- General: `2abcd06d6df7eda781a726e97fd89db715281c30`
- MovementBasic: `afbc67426f531cdda49b7f665f91c2cc7e744fbf`
- MovementAdvanced: `ff188316654191f267475903a8d8e5d9289b1d25`
- Simulation: `cc6641d380bfb906a85a204cbcd20157bf3ec5dd`
- Tools: `72df46850d70c9a95c62395e1a832bf0c04775c0`

SHA-256 hashes of the imported binaries are recorded in `KAYKIT_CHARACTER_ANIMATIONS_SHA256.txt`.

Exact animation names are enumerated from the matching GLB files in `KAYKIT_DAILY_CLIPS.txt`; the GLB files are used only for inspection and are not committed.

## Scope note

This commit adds animation assets only. It does **not** modify runtime C#, Animator state machines, `PlayableAnimator`, animation mappings or gameplay code.

The official page currently lists eating/drinking as planned future simulation animations, so this package should not be treated as verified coverage for eating/drinking until those clips are actually released and inspected.
