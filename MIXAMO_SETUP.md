# 接入 Mixamo 动捕角色 + 动作（真人级战斗动作）

> ## ⚠️ 最重要的一步：资产必须提交进 Git 仓库！
> 安卓 APK 由 GitHub Actions **从仓库**构建——只放在你本机 Unity 工程里的文件
> **不会**出现在手机上。放好文件后务必执行：
> ```
> git add Assets/_Project/Resources/Characters
> git commit -m "添加角色/动作/武器资产"
> git push
> ```
> 等 CI 构建出新 APK 再装机测试。（本机 Unity 里当然即放即生效，无需提交。）
>
> ## ⚠️ 本机 Unity 报 "An error occurred while resolving packages"
> 这是**网络问题**：工程引用的 glTFast 包（glb/gltf 导入支持）需要从 Unity 官方
> 包服务器下载，代理断线/网络不稳会拉取失败——glb 全部无法导入（武器不出现、
> 角色贰不显示都是它的连锁反应）。恢复网络后：菜单 `Assets → Reimport All`，
> 或删除工程 `Library/PackageCache` 后重开 Unity 即可。CI 构建不受你本机网络影响。
>
> ## ⚠️ 替换了带皮肤的新模型但游戏里还是旧的白模
> 一定要**同名替换 + 提交推送**：把新模型改成与旧文件完全相同的名字
> （`PlayerModel.fbx` / `EnemyModel.fbx` / `PlayerModel2.glb`）覆盖旧文件，
> 然后 `git add/commit/push`。若本机 Unity 仍显示旧模型：对该文件右键 → **Reimport**，
> 或删除工程根目录的 `Library/` 文件夹后重开（清导入缓存）。角色/敌人的皮肤与肤色
> 完全取自模型自带材质——代码不做任何染色改色（受击只是短暂闪红并自动恢复原色）。

代码已全部写好。你**只需要把下载好的文件放进指定文件夹**，Unity 会自动把它们配成
Humanoid、自动给走/跑/待机加循环——**无需在 Inspector 手动设置任何东西**。
运行时代码会自动加载它们；找不到就回退到原来的方块角色（所以放之前也不影响运行）。

---

## 一、放文件（就两步）

在工程里建好这个目录结构（没有就新建）：

```
Assets/_Project/Resources/Characters/
├── PlayerModel.fbx          ← 玩家角色模型
├── EnemyModel.fbx           ← 敌人角色模型（可选，不放就复用 PlayerModel）
└── Anims/                    ← 所有动作 FBX 全部丢这里（不用改名）
    ├── Maria WProp J J Ong@Idle.fbx
    ├── Maria WProp J J Ong@Fighting Idle.fbx
    ├── Maria WProp J J Ong@Walking.fbx
    ├── Maria WProp J J Ong@Running.fbx
    ├── Maria WProp J J Ong@Great Sword Slash.fbx
    ├── Maria WProp J J Ong@Great Sword Slash (1).fbx
    ├── Maria WProp J J Ong@Great Sword High Slash.fbx
    ├── Maria WProp J J Ong@Great Sword Jump Attack.fbx
    ├── Maria WProp J J Ong@Stabbing.fbx
    ├── Maria WProp J J Ong@Lead Jab.fbx
    ├── Maria WProp J J Ong@Cross Punch.fbx
    ├── Maria WProp J J Ong@Kicking.fbx
    ├── Maria WProp J J Ong@Side Kick.fbx
    ├── Maria WProp J J Ong@Spin Flip Kick.fbx
    ├── Maria WProp J J Ong@Flying Kick.fbx
    ├── Maria WProp J J Ong@Hit Reaction.fbx
    ├── Maria WProp J J Ong@Knocked Down.fbx
    ├── Maria WProp J J Ong@Dying.fbx
    └── Maria WProp J J Ong@Spell Casting.fbx
```

**只需两处操作：**
1. 把角色模型 `Maria WProp J J Ong.fbx` 复制进 `Resources/Characters/`，**改名为 `PlayerModel.fbx`**。
2. （可选）把 `Paladin WProp J Nordstrom.fbx` 复制进去，**改名为 `EnemyModel.fbx`**。
3. 其余**所有动作 FBX 原样丢进 `Resources/Characters/Anims/`，不用改名**
   （代码按 Mixamo 的 `@动作名` 自动识别并分配到招式）。

> 动作文件名里的 `@Idle` / `@Side Kick` / `@Great Sword Slash` 这些**后缀**是关键，
> Unity 会用它给动画片段命名，代码据此匹配。别去掉 `@` 后缀就行。

> **没有 `@` 后缀的文件（如 `Sitting.fbx`）必须连 `.fbx.meta` 一起提交。**
> Mixamo 导出的 take 一律叫 `mixamo.com`，Unity 在没有 meta 时就用这个名字命名片段，
> 而代码会把 `mixamo.com` 这个名字直接过滤掉（十几个文件都叫同一个名字，没法匹配）。
> meta 里的 `clipAnimations.name` 才是片段真正的名字。直接从网页上传 FBX 不会带 meta，
> 这时片段等于没导进来——照着同目录里任何一个 `.meta` 复制一份，改 `guid`、
> `clipAnimations.name`（= 文件名）与 `lastFrame`（帧数 = FBX 里的 LocalTime ÷ 30fps）即可。

---

## 一·补、动作库覆盖面补充清单（去 Mixamo 搜索下载，放进 `Anims/` 即自动生效）

以下动作当前用替代片段顶替，下载专用片段后代码**自动优先使用专用片段**（不用改任何配置，
文件名保留 `角色名@动作名.fbx` 的 `@后缀` 即可）：

| 去 Mixamo 搜索的动作名 | 生效位置 | 现在的替代方案 |
| --- | --- | --- |
| **Great Sword Blocking**（或 Blocking） | 格挡架势（挡键） | 格斗架势收紧 |
| **Stand To Roll**（或 Forward Roll / Sprinting Forward Roll / Dive Roll） | 翻滚闪避（闪键） | 程序化前滚翻 |
| **Stunned**（或 Dizzy） | 破绽/踉跄硬直 | 受击动作慢放 |
| **Great Sword Casting**（或 Warming Up / Taunt） | 重键蓄力姿态 | 施法聚气 |
| **Leg Sweep** | 扫堂腿（蹲+腿） | 空翻踢低位 |

## 一·补一之二、方向移动片段（后退 / 横移 / 斜向，已就位）

交战时角色**脸锁在敌人身上、脚往哪走由摇杆决定**，所以移动层需要一圈按方向摆开的
片段，而不是只有"向前走/跑"。这几段已经在 `Anims/` 里：

| 文件名 | 方向 | 档位 |
| --- | --- | --- |
| `Walking` | 前 | 走 |
| `Walking Backwards` | 后 | 走 |
| `Left Strafe Walking` / `Right Strafe Walking` | 左 / 右 | 走 |
| `Running` | 前 | 跑 |
| `Slow Jog Backwards` | 后 | 跑 |
| `Jog Forward Diagonal` | 右前 **+45°**（实测值） | 跑 |
| `Jog Backward Diagonal` | 左后 **−136°**（实测值） | 跑 |

> 上面两条的角度是 CI 实测打出来的——注意这两段**不在同一侧**（一个右前、一个左后）。
> 按名字猜"前斜=+45、后斜=+135"会把后斜的那条摆到反方向去。每次构建的日志里
> 都有这张实测表（搜 `[CIDIAG][移动]`），换素材后照着核对即可。

**什么时候会用到这些方向片段？——只有【玩家主动锁定】的时候。**
按锁定键（Q / 触屏「锁」）之后，角色脸锁在目标上，摇杆负责往哪走：左右横跨、
后撤、绕角。再按一次解除，回到"推杆即转身"。
没锁定时角色永远是朝行进方向转身，用的还是向前走/跑——这是默认手感，不会被改动。
（曾经试过"交战中自动锁面向"，结果是全程横移、推杆不再转身，手感尽失。
锁不锁必须是玩家自己的决定。想要自动的，设置面板里有「自动锁定」开关。）

锁定期间移速会降到**步法档**（正前 ≤3.77、正侧 ≤2.60、后撤 ≤2.08 m/s），
既因为横穿画面的观感本来就快，也因为这几条封顶正是照着片段自然速度算的——
保证任何方向都不打滑。想全速跑，解除锁定即可。

**接入方式与招式片段不同，有三点值得知道：**

1. **按文件路径取片段，不按片段名。** Mixamo 导出的 FBX 里 take 一律叫 `mixamo.com`，
   没有 `.meta` 改名的话按名字一个都找不到。所以这几段走 `Resources.Load` 按路径取，
   **文件名就是契约**——重命名文件会让它失效，改片段内部的名字则无所谓。
2. **斜向片段的左右由运行时实测。** 各家素材的"Forward Diagonal"是左前还是右前并不统一，
   猜错比不用更糟。代码会采样片段首尾两帧、量髋骨走了哪个方向，**测到才用**；
   测不到就交给相邻的正方向片段混合出来。所以你再丢别的斜向片段进来也能自动就位。
3. **还缺跑动版的横移**（Mixamo 的 `Left Strafe` / `Right Strafe`）。
   现在跑着横移用的是走的横移片段提速播，移速被**按片段撑得住的速度封了顶**：
   横移封到 `walkSpeed×1.0`、后退封到 `walkSpeed×0.8`——这两个数是照着实测的
   自然速度（横移 1.70m/s、后退 1.12m/s）与播放速率上限 2.0 倒推的，不是拍的。
   补上跑动版横移后，把 `PlayerController` 里那两个系数放开即可。

## 一·补三、移动动作集的完整度审计（对照成熟动作游戏，缺什么一览）

### 现在有什么（实测数据来自构建日志 `[CIDIAG][移动]`）

| 档 | 方向 | 片段 | 自然速度 |
| --- | --- | --- | --- |
| 走 | 前 / 后 / 左 / 右 | Walking / Walking Backwards / Left·Right Strafe Walking | 1.75 / 1.12 / 1.70 |
| 跑 | 前 / 后 / 右前 / 左后 | Running / Slow Jog Backwards / Jog Forward Diagonal / Jog Backward Diagonal | 4.65 / 1.68 / 2.83 / 2.51 |

也就是：**走档是完整的四方向，跑档是残缺且不对称的四条**（有右前 +45° 与左后 −136°，
却没有左前、右后，也没有跑动的正左/正右）。

### 缺什么 —— 按"现在就在露馅"的顺序

**P0-1　慢跑档（最大的洞）**
走 1.75、跑 4.65，中间 2.6–4.6 m/s 这一整段**没有对应片段**，只能靠走与跑交叉淡入——
两条步态叠在一起，读起来"既不是走也不是跑"。而摇杆推到六七成正好落在这一段，
是最常用的速度。
> 需要：`Jog Forward`、`Jog Backward`、`Jog Strafe Left`、`Jog Strafe Right`
> （与你已下载的 Jog Forward/Backward Diagonal 同属一个 Mixamo 移动包，一起下齐）

**P0-2　跑动横移（补齐跑档的八方向）**
现在跑着横移是拿"走的横移"提速播，因此移速被**按片段能力封了顶**
（正侧 2.60 m/s）。补齐后这个封顶才能放开。
> 需要：`Left Strafe`、`Right Strafe`（跑速横移）+ 左前/右后两条斜向补对称

**P0-3　原地转身**
完全没有。站着不动改朝向时脚是钉住的、身体硬转——这是"像在推一个模型"而不是
"在操纵一个人"的重要来源。
> 需要：`Left Turn 90`、`Right Turn 90`、`Left Turn 180`、`Right Turn 180`

**P0-4　起步 / 急停**
没有加减速片段。现在起步是走循环直接淡入、停下是直接淡出，缺少"蹬地起步"
与"刹住重心"的那两拍，这是分量感的主要来源。
> 需要：`Walk Start`、`Jog To Stop`、`Run To Stop`（急停带侧身刹车最好）

**P1-5　跳跃 / 下落 / 落地（功能已有、动画完全没有）**
游戏里跳跃键是通的（含土狼时间与输入缓冲），但**动捕模式下没有任何跳跃动画**——
人在空中还在播走跑循环。
> 需要：`Jumping Up`、`Falling Idle`、`Falling To Landing`、`Hard Landing`
> （另有 `Jump Forward` 更好，跑动中起跳用）

**P1-6　蹲伏移动（同上：功能已有、动画完全没有）**
C 键蹲伏改了碰撞体与移速，但动捕模式下播的还是站立的走——蹲伏姿态只存在于
程序化方块骨骼那条兜底路径里。
> 需要：`Crouch Idle`、`Crouched Walking`、`Crouched Walking Backwards`、
> `Crouched Sneaking Left`/`Right`

**P1-7　方向闪避**
只有一条前滚翻（Stand To Roll），四个方向的闪避全都播它，靠转身体凑。
锁定交战时后撤步/侧滚是基本盘。
> 需要：`Back Step`（后撤步）、`Dodging Left`、`Dodging Right`、
> 以及跑动中的 `Sprinting Forward Roll`

**P2-8　冲刺档与战斗移动**
> `Fast Run` / `Sprint` + `Sprint To Stop`；举械前进后退的 `Combat Walk Forward/Backward`
> （现在临战前进用的还是普通走）

**P2-9　待机与受伤变体**
> `Breathing Idle` 变体、`Injured Walking`（低血量时切换，情绪表达用）

### 落地时要注意的两件事

1. **命名就是契约。** 移动片段走的是"按文件路径取"（见上一节），
   代码里的候选名单在 `PlayableAnimator.CollectDirectional`。上面 P0-1/P0-2 的名字
   已经预留在名单里（`jog forward`、`left strafe`… 等），**下载放进去即自动生效**；
   其余各项（转身/起步急停/跳跃/蹲伏/方向闪避）**需要配套写代码**，不是丢文件就行。
2. **方向与自然速度不用手填。** 接入时会实测每条片段的行进方向与位移速度，
   构建日志里能直接核对（搜 `[CIDIAG][移动]`）。所以斜向片段是左是右无所谓，
   放进去它自己会找到位置。

## 一·补三之二、移动之外的动作缺口（战斗 / 受击 / 演出 / NPC）

上一节只盘了移动。这一节盘其余全部动画消费方。**最值得注意的一类是
"逻辑已经写好、动画没跟上"** —— 机制在跑，玩家却看不出来。

### A. 招式片段被反复复用（剑技尤其严重）

剑技只有 5 条（Slash、Slash (1)、High Spin、Jump Attack、Stabbing），
却要撑起 8 个剑类招式 + 五大连招 + 超必杀。实际复用情况：

| 片段 | 被几个招式共用 |
| --- | --- |
| `Great Sword Jump Attack` | **3**（重击 HeavyAttack / 跃劈 AttackLeap / 空中下劈 JumpAttack） |
| `Great Sword High Spin Attack` | **3**（重击候补 / 上挑候补 / 旋风斩） |
| `Spin Flip Kick` | 2（后旋踢 / 扫堂腿候补） |

也就是说玩家眼里的"重击""跃劈""空中下劈"**是同一个动作**。
> 需要：`Great Sword Slash 2/3`（横斩变体，连段第二三段用）、
> `Great Sword Overhead Strike`（真正的上段重劈，把 HeavyAttack 从跳劈里解放出来）、
> `Great Sword Impact`（打空/被格挡的收招）、`Sword And Shield Slash` 等

### B. 受击表现只有一条

`Hit Reaction` 一条撑全部——**不分方向、不分轻重、不分部位**。
而代码里部位系统（头/躯干/四肢）和轻重击判定**都已经做好了**，
动画却给不出区别，玩家因此感受不到"打中了哪儿"。
死亡也只有 `Dying` 一条，所有敌人所有死法都一样。
> 需要：`Hit Reaction`（左/右/后向各一条）、`Head Hit`、`Stomach Hit`、
> `Big Hit To Head`（重击）、`Death From Front/Back`、`Falling Back Death`

### C. 防御链缺三个关键动作

| 机制 | 代码状态 | 动画状态 |
| --- | --- | --- |
| 精准格挡 / 招架（0.2s 窗口、免伤、对方露破绽） | **完整实现** | ❌ 无专用动作，仍是格挡保持姿态 |
| 格挡被击中的反震 | 有减伤与体力消耗 | ❌ 无 |
| 破防 / 被破韧 | 有 | ⚠️ 借用 `Stunned` |

玩家把时机做对了却看不到任何区别，这是"格挡没用"这类反馈的一个来源。
> 需要：`Sword And Shield Block Idle`→`Block Impact`、`Parry`/`Deflect`、`Guard Break`

### D. 处决 / 终结技：全套机制在跑，没有动作

破韧后重击命中 = 2.8 倍伤害 + 横幅「处决」+ 强顿帧 + 慢镜 + 推近特写——
唯独**播的是普通重击**。这是全局最该有仪式感的一击。
> 需要：`Sword Finisher`、`Execution`、`Stealth Kill`

### E. 心理硬直——这是本作的主题所在

「短暂失守」（跪一下、掉锁定）走的是 `Stunned`（普通眩晕）。
而这款游戏整体讲的就是心理对抗，崩溃/抱头/踉跄后退这类动作**是叙事的一部分**，
用一个通用眩晕顶掉很可惜。
> 需要：`Kneeling Down`、`Defeated`、`Sad Idle`、`Head Hit`（抱头）、
> `Stumble Backwards`

### F. 敌人：九种武术类型共用一套动作

`MartialArchetype` 设计了拳/腿/擒拿/刀/棍/重武器/刺客/防反/协同九类，
但动作只有一套——不同心魔的动作语言完全一样，只靠颜色与台词区分。
> 需要（按性价比排）：`Boxing` 系（拳）、`Martial Arts Kick` 系（腿）、
> `Staff/Spear` 系（棍），三套就能把主要类型区分开

### G. NPC 与生活场景

行人与城市 NPC 只有走/跑/待机；住处只用了坐/躺/起身三条。
> 需要：`Standing Idle` 变体、`Talking`、`Looking Around`、`Waiting`、
> `Sitting Idle` 变体；住处设施（书桌/瑜伽垫/厨房）的专属动作

### H. 设计里有、实现完全没有

`terrain_climb 地形攀越`（跳跃、攀爬、破障逐阶推进）是 `ChapterModuleLibrary`
里的**正式机制**，AI 生成的章节会把它排进关卡——但攀爬/翻越既没有动画也没有实现。
> 需要：`Climbing`、`Climb To Top`、`Hanging Idle`、`Braced Hang`、`Vault Over Obstacle`
> （这一项动画只是其中一半，另一半是攀爬状态机）

### I. 交互动作：全无

开门、拾取、按按钮、推拉——现在都是走过去即触发，没有任何肢体动作。
> 需要：`Opening Door`、`Picking Up Object`、`Pressing Button`、`Pushing`

### 优先级建议

| 级别 | 项 | 理由 |
| --- | --- | --- |
| **P0** | C 招架 / D 处决 | 机制已完整，只差动画就能被玩家感知——性价比最高 |
| **P0** | B 受击方向与轻重 | 部位系统已做好，没有动画等于白做 |
| **P1** | A 剑技变体 | 消除"三招一个样"，连段观感立刻不同 |
| **P1** | E 心理硬直专用动作 | 主题表达，别的游戏没有、本作必须有 |
| **P2** | F 敌人武术类型 / G NPC / I 交互 | 丰富度，不影响核心手感 |
| **P2** | H 攀爬 | 要连状态机一起做，工作量最大 |

**接入成本提示**：A 类里若只是**替换**现有招式的片段，改一下 `PlayableAnimator.ActionMap`
的候选名即可（小改）；B/C/D/E 都需要**新增 PoseState + 在触发点接线**；
H 需要完整实现。都不是"丢文件即生效"。

## 一·补二、居家休息动作（已就位，住处的坐/躺全靠它们）

这几段不是招式，走的是 `HumanoidAnimator.PlayRestClip` 这条通路（完整播完、停在末帧、
可倒放），由 `SitController` 串成一条链：

| 片段（`Anims/` 下的文件名） | 生效位置 |
| --- | --- |
| **Sitting** | 站→坐（椅子/凳子/沙发/床沿）；**倒放**即从椅子上站起来 |
| **Lying Down** | 坐→躺（床、沙发、躺椅、卧推凳） |
| **Sleeping Idle** | 躺着的持续姿态（带呼吸起伏，循环播放） |
| **Getting Up** | 从躺姿起身；**倒放**即"就地躺下"（健身房瑜伽垫） |
| Stand Up / Standing Up | 备用的起身片段（现用 Getting Up，它最利落） |
| **Putting Down** | 在兵器架上放下 / 取回兵器（弯身那一刻交换手里与架上的剑） |

拔剑 / 收剑（两个角色各有一套，缺的自动用另一套）：

| 片段 | 目录 | 用在 |
| --- | --- | --- |
| Draw A Great Sword 2 / Sheath A Great Sword 1 | `Anims/` | 角色·壹优先 |
| Withdrawing Sword / Sheathing Sword (1) | `Anims2/` | 角色·贰优先 |

> `Anims2/` 现在是**公共补充库**而不是"角色·贰专属库"：它缺 idle/walk/run，
> 当主库是不成立的；但里面的通用片段（拔剑/收剑）会被两个角色一起装进动作库
> （见 `PlayableAnimator.ExtraFolder`）。

> 这些片段自带**动作骨架自己的座面高度**（那把椅子约 45cm、那张床约 67cm）。
> 家里每件家具高度都不同，所以休息期间 `HumanoidAnimator` 改用**骨盆锚定**：
> 量家具包围盒顶面，把骨盆对到座面而不是把脚底对到地面。换家具不用调参数。

可选增强（有则更好，无不影响）：

| 动作名 | 生效位置 |
| --- | --- |
| Sitting Idle | 坐稳后的待机（现用 Sitting 末段循环） |
| Great Sword Idle | 持剑临战待机（现用 Fighting Idle） |
| Great Sword Walk / Great Sword Run | 持剑走/跑（现用 Walking/Running） |
| Great Sword Impact | 持剑受击（现用 Hit Reaction） |
| Great Sword Death | 持剑死亡（现用 Dying） |

> 下载设置：Format=FBX for Unity，Skin=With Skin 或 Without Skin 均可，帧率 30，不勾 In Place。

---

## 一·续、第二角色 + 武器库（角色与武器资产分离）

游戏内右上「角色」按钮打开**角色·武器库面板**：先选角色，再从武器库选武器拿在手中
（默认持剑，重选即替换）。工程已内置 **glTFast** 包——`.glb` / `.gltf` 与 `.fbx`
一样放进去就能导入使用。目录契约（`Anims2/` 与 `Weapons/` 目录已在工程中建好）：

```
Assets/_Project/Resources/Characters/
├── PlayerModel.fbx        ← 角色·壹（已就位）
├── PlayerModel2.glb       ← 角色·贰模型：glb / gltf / fbx 均可，文件名必须是 PlayerModel2
├── Anims/                  ← 角色·壹动作库（已就位）——角色·贰【沿用】这套动作库
├── Anims2/                 ← （可选）角色·贰专属动作库：留空=沿用 Anims/；
│                              放入片段则优先使用（清单与 Anims/ 相同，保留 @后缀）
├── Weapons/                ← 武器库：每个模型文件 = 一件武器，文件名即游戏内武器名
│   ├── 长剑.glb            **强烈建议用 .glb（自包含）或 .fbx**；.gltf 是多文件格式
│   ├── 巨剑.fbx            （.gltf + .bin + 一堆贴图），任一文件缺失/路径错就【整件武器
│   │                         导入失败、面板里不出现】——若某武器没显示，多半是它的
│   │                         .gltf 导入失败(看 Console 报错)，改用 .glb 最稳
│   └── 武器合集.zip        也可直接丢 zip：编辑器自动解压出其中的模型/贴图并删除压缩包
└── Masks/                  ← 面具库：每个模型文件 = 一个面具，文件名即游戏内面具名
    ├── 狐狸面具.fbx        支持 .fbx / .glb / .gltf / .zip；戴上后自动贴合脸部
    └── 能面.glb            自动定尺（面具宽≈头宽）、法向对齐面部、随头骨转动
```

规则与自动处理：

- **角色·贰（glb）**：`Resources.Load` 按名加载与扩展名无关，`PlayerModel2.glb` 放入即被识别；
  模型没有 Animator 时运行时自动补挂。**动作库沿用角色·壹**（`Anims/`）。
  **异源骨架自动对齐**：glb 角色骨名常无 mixamorig 前缀（Hips/Spine/LeftArm…，
  ReadyPlayerMe 等标准人形均如此）或根链路不同——运行时自动把骨骼改名并把链路对齐到
  参考骨架，让默认动作库直接绑定生效；仅当骨架与人形标准差异过大（匹配骨数过少）
  才放弃对齐（此时模型会保持静止，换回角色·壹即恢复）。`PlayerModel2` 缺失自动回退角色·壹。
- **武器库（glb/fbx/gltf/zip）**：放入即出现在面板中。
  - **zip**：每个 zip 自动解压到【以 zip 名命名的子目录】且保留包内相对路径——
    多个 zip 里同名的 scene.gltf/贴图互不覆盖；包内唯一的模型文件会被重命名为 zip 名
    （游戏内武器名 = zip 文件名）；解压完成后 zip 自动删除。
  - **装备**：自动隐藏角色模型自带兵器、把新武器挂到右手，并做**定尺 + 握持对齐 +
    手指握拳**——复刻角色·壹自带巨剑的握姿（世界空间+身高比例，跨骨架单位安全），
    柄端放进掌心、五指绕柄轴卷曲攥住剑柄；最长轴视为刃轴、离模型原点近的一端视为柄端
    （可在武器预制体里放名为 `Grip` 的子节点显式指定柄位）。
  - **默认佩剑**：优先用模型自带兵器；模型没有自带兵器（glb 角色常见）时自动生成
    一把程序化长剑兜底——任何角色选「默认佩剑」手里都有剑。
- **面具库（fbx/glb/gltf/zip）**：放进 `Masks/` 即出现在面板「面具库」区，选择后自动戴到
  脸部——自动定尺（面具宽≈头宽）、贴到**眼睛高度**的脸面、法向对齐面部朝向并随头骨转动；
  选「不戴面具」摘下。朝向的正反/上下由面具挂到头骨时各轴与前方/上方的贴合度自动判定；
  若个别面具仍**戴反或上下颠倒**：在模型正前方加一个名为 `Front` 的空子节点、
  或在头顶方向加名为 `Top` 的空子节点显式指定（也可直接把模型在 DCC 里摆正后导出）。
  支持多个面具，随时切换。
- 选择本地持久化，重启保留。

---

## 二、然后……没有然后了

- 放进去后 Unity 会自动导入：**FBX 自动设为 Humanoid，走/跑/待机自动加 Loop**
  （由 `Assets/_Project/Editor/MixamoImportPostprocessor.cs` 完成）。
- 如果你是**先放的文件、后拉的这次代码**，对着
  `Assets/_Project/Resources/Characters` 文件夹右键 → **Reimport** 一次即可让自动设置生效。
- 直接 **Play**：玩家/敌人就换成 Mixamo 动捕动作了。控制台若报缺 Idle/Walk/Run，
  说明这三个基础片段没放对（其余动作缺了会自动跳过、不报错）。

---

## 三、动作 → 招式 对应表（代码里已配好，供你核对）

| 招式 | 采用的 Mixamo 动作 |
| --- | --- |
| 待机 / 临战待机 | Idle / Fighting Idle |
| 走 / 跑 | Walking / Running |
| 拳键一段·巨剑横斩 | Great Sword Slash |
| 拳键二段·巨剑撩斩 | Great Sword Slash (1) |
| 拳键三段·突刺（含前+重疾影突刺） | Stabbing |
| 拳键四段·巨剑旋风斩（含左右+重、旋风终结） | Great Sword High Spin Attack |
| 蓄力重击·巨剑跳劈 / 空袭跳劈（跳+拳、跳+重） | Great Sword Jump Attack |
| 腿键连段·正踢→侧踹→旋身空翻踢→飞踢 | Kicking / Side Kick / Spin Flip Kick / Flying Kick |
| 后+重·旋身空翻踢 / 蹲+攻·扫堂腿 | Spin Flip Kick |
| 直拳 / 交叉重拳（敌人拳系） | Lead Jab / Cross Punch |
| 受击 / 倒地 / 死亡 | Hit Reaction / Knocked Down / Dying |
| 施法 / 蓄力聚气 | Spell Casting |

播放层带**起手偏移 + 提速**：从片段的发力相位起播、按招式各自的倍速播放，
按键当拍出手、命中后立即可取消接招——连点即无缝连段。

绝招「觉醒·乱舞」会自动把上面几招串成连段演出（配酷炫但不遮挡动作的特效）。
缺哪个动作就自动跳过该招（回到 locomotion），不影响运行。

---

## 四、兵器（大剑）

已改为**自动**，不再挂那把程序化方块剑：

1. 若你下载的角色模型**本身就握着大剑**（Mixamo「Great Sword Pack」的角色多为持械导出），
   直接用模型自带的剑——代码会在骨骼层级里按名字（sword/blade/greatsword…）找到它并绑定刀光，
   **无需任何操作**。
2. 若模型不带剑、你想额外挂一把素材大剑：把大剑做成一个预制体命名为 `Weapon`，
   放到 `Resources/Characters/Weapon.prefab`，代码会实例化并挂到右手骨骼。
3. 两者都没有 → 右手不挂武器（徒手，正常，不会再出现悬空的方块剑）。

---

## 五、提交打包

Unity 里能正常 Play 后，把 `Assets/_Project/Resources/Characters/`（模型、动作 FBX 及其
`.meta`）整个提交推送到 `claude/**` 或 `main`，CI 会打出带动捕动作的 APK 到真机测试。

---

## 六、常见问题（本次已修）

| 现象 | 说明 |
| --- | --- |
| **角色腾空/陷地** | 已自动修：装配时量测模型包围盒，把脚底对齐到角色胶囊体底部。 |
| **角色太小** | 已自动修：自动缩放到标准身高 ~1.85m（大体型敌人在此基础上按体型放大）。 |
| **手里多一把方块剑** | 已自动修：动捕模式不再挂程序化剑，见上面「四、兵器」。 |
| **敌人太容易被打死** | 已加**调试模式**：默认开启「敌人耐揍」（设置面板可关）。正式发布把 `GameDebug.TankyEnemies` 设 false。 |
| **走路腿反向/像「鞋穿反了」** | 已自动修：动作 FBX 不带蒙皮、自建 Avatar 的 T-Pose 常校准失败导致重定向后腿部扭曲。现在 Anims/ 下的动画统一**复用 PlayerModel 的 Avatar**。操作：对 `Resources/Characters` 文件夹右键 → **Reimport**（先有 PlayerModel 再导动画）。 |

---

有任何一招时机/朝向不对，或想调整绝招连段顺序，把片段名发我，我改映射与串招。
