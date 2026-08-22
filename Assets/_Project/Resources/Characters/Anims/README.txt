【动作库主目录】

把 Mixamo 动作 FBX 原样丢进来，不用改名。代码有两条寻址通路，任选其一即可命中：

  ① 文件名带 @ 后缀（如 `角色名@Side Kick.fbx`）——Unity 会把内部片段命名为
     「Side Kick」，代码按片段名匹配；
  ② 文件名就是动作名（如 `Jog Forward.fbx`）——Mixamo 的 take 一律叫 "mixamo.com"，
     没有命名 .meta 时按片段名一个都找不到，所以代码另有一张
     **文件名清单**（PlayableAnimator.LibraryFiles）按【文件路径】加载。
     ⚠️ 走这条路的新文件，必须同时把文件名加进那张清单，否则运行时寻址不到。

硬性前提：至少要有 Idle / Walking / Running 三条，否则整个动捕层判定为无效、
回退程序化骨骼。

—— 加了新动作之后怎么确认它真的被用上了 ——

    python3 tools/animaudit.py    # 有没有"放进来了却没人用"的 FBX
    python3 tools/resolvesim.py   # 复刻运行时解析顺序，推出每个 FBX 落到哪个槽位

真机/CI 侧：构建日志里搜 `[CIDIAG][移动]`（方向片段的实测角度与自然速度）
和 `[CIDIAG][招式]`（每个姿态实际拿到的片段名）。

当前 82 条片段的完整落位表见仓库根目录 MIXAMO_SETUP.md「一·补四」。
