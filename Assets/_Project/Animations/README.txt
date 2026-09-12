【为什么这些动作包不在 Resources 下】

Unity 的 Resources.LoadAll 会把目录里**所有**片段无条件加载进内存；
PlayableAnimator.Build() 就是这么建 byName 表的。所以只要一个 FBX 躺在
Resources/Characters/Anims*/ 下，它就一定被加载、一定占内存与 Playable 槽位，
哪怕玩法代码一次都没用到它。

UAL1/UAL2/KayKit 这三套是**素材储备**，目前没有任何玩法代码引用。
放进 Resources 会白占内存，而且它们的文件名很容易被 Pick() 的"包含匹配"
误命中，把已经调好的招式片段悄悄换掉。所以统一放在这里（非 Resources）。

想启用其中某一套时，走这条路，不要直接搬回 Resources：
  1. 像 Editor/UalRetargetBaker.cs 那样离线烘焙成本工程骨架上的 Generic 片段，
     产物进 Resources/Characters/AnimsUAL；
  2. 在 PlayableAnimator 的 ActionMap / UalMap 里为它登记候选键。

门禁：tools/animchain.py 会把"加载了却没有任何用途"的片段判红。
这就是这几个目录存在的直接原因——那两次 CI 红（run 528 / 530）报的正是这一条。

目录：
  UAL/     Quaternius 通用动作库 1（UAL1_Standard.fbx 已由 UalRetargetBaker 烘焙使用）
  UAL2/    Quaternius 通用动作库 2（未接入）
  KayKit/  KayKit 日常行为动作包（未接入）
