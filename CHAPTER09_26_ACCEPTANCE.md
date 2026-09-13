# 第 9–26 章 · 90 关内部障碍线 验收对照表（V2.2 增补 PRD）

这一批的定义写在标题里：**90 关的敌人全部是自我内部敌人，没有一个外部敌人。**
外部敌人线（不公、噪声、他人索取、被注视）在 V1/V2.0 的七大主题与 V2.1 的第八章里已经讲完；
第 9–26 章讲的是另一件事——挡在目标前面的是自己那套机制。

数据是策划冻结件，放在 `Assets/_Project/Resources/Chapters/` 下两份 JSON 里；
代码只负责读它、校验它、把它跑起来。

| 交付物 | 数量 | 落在哪里 |
| --- | --- | --- |
| 章节 | 18（第 9–26 章） | `internal_chapters_v22.json` |
| 关卡 | 90（每章 3 普通 + 1 精英 + 1 Boss） | 同上，含灰盒尺度、出生点、主路径、触发点、NavMesh/Camera、复用资产 |
| Boss | 18（Boss 28–45） | 同上 `boss` 段：真命门、假弱点、阶段、Execution Gate、DeathType |
| 内部敌人单位 | 53 组（共 113 个单位，分布在 51 关） | 同上 `internalUnits`，`category` 恒为 `Internal` |
| 三选一内部语言攻击 | 90 条 Canonical 事件、270 个选项 | `mental_attacks_v22.json` |
| 新增 EnemyType | 18（Boss 28–45） | `AI/EnemyCatalog.cs`，全部 `EnemyCategory.Boss` |
| World Kit / 根障碍 / DeathType / Mastery | 14 / 7 / 20（附录 B 15 + Boss 总表 5）/ 6 | `internal_chapters_v22.json` 顶层 |

---

## 一、总原则（PRD 第 3 节）对照

| # | 验收条目 | 落在哪里 | 怎么满足的 |
| --- | --- | --- | --- |
| 1 | Goal OS 优先：只有内部机制真的挡住当前 Goal 时才激活对应章节 | `InternalOS/InternalChapterBridge.SelectFor` | 与 `LegacyChapterCatalog.SelectFor` 同形：按目标里未消除的障碍轴挑章节。没有"解锁第 9 章"这类 API——顺序不由章节号决定 |
| 2 | 三层世界：Reality / Adversity / Inner World，返回后仍在合理物理位置 | `InternalOS/InternalSiteComposer.cs` | 每关的 `worldKitIds` 决定现实底座（WK01–WK14 → 已批准 `siteKind`），心理层只改材质/天气/高低差；WK14 InnerCore 单独映射到可做断裂与错层的场景类型 |
| 3 | 空间记忆：逆境层保持现实地点的柱距、门、出口方向 | 同上 | 布局取自关卡表的"主路径 / 拓扑"，不是取自场景类型——同一处 Base 的不同关卡共用同一套拓扑词 |
| 4 | **不以清怪定义胜利** | `InternalLevelRunner.ExecutionGate`、`InternalChapterBridge.BossDefeated` | 通关走 Execution Gate，不走血条。18 个 Boss 里允许"打倒即结束"的是 0 个：`BossDefeated` 只在 DeathType 全部为 `Defeat` 时才看 `hpCleared` |
| 5 | 失败必须产生可验证信息，不得按次数换胜率 | `ControlChainRecorder`、`RealityVictorySystem` | 只接收"发生了什么行为"：控制链节点、证据条目、恢复时延。没有任何一条接口接受"我失败了 N 次" |
| 6 | 安全边界：可退出、可降低强度，撤退是合法胜利 | `InternalLevelRunner.StrategicWithdraw` | 撤退显示 Strategic Withdrawal 而不是 FAILED；但必须给出重返条件，否则拒绝——没有重返条件的撤退是拖延 |

## 二、Unity 契约（第 4.1 / 10.1 节）对照

| 条目 | 怎么满足的 |
| --- | --- |
| Trigger 命名 `TRG_Chapter_Level_Function` | 90 关共 275 个触发点，全部以 `TRG_` 开头；`InternalChapterCatalog.Validate` 逐个检查，`InternalLevelRunner.FireTrigger` 对未声明的触发点发警告而不是静默吞掉 |
| Spawn Socket 命名 `SPN_Chapter_Level_Group_Index` | 每个内部单位自带 `SPN_CH09_L1_Internal_01` 形式的槽位 id，由数据生成时写死，校验器检查前缀 |
| `LevelGreyboxData` 字段 | `InternalLevelData`：levelId / chapterId / worldKitIds / bounds(greyboxScale) / spawnPoint / goalRoute(mainPath) / triggerIds / navMeshCamera / realityVictory / reusedAssets |
| `BossDNA` 字段 | `InternalBossDNA`：bossType / attackGrammar / phaseGraph / fakeWeaknesses / coreMechanism / executionGate / deathType |
| 同一关每次组装必须一致 | `ToLevelBlueprint` 的 `assemblySeed = levelId.GetHashCode()`——同一关的种子恒定，Bug 才可复现 |
| 不为心理覆盖重载整个现实场景 | 走既有 `SiteBuilder` 管线：内部章节与 AI 章节用**同一个建造器**，没有"内部章节专用建造器" |

## 三、三选一反制系统（增补章第 12 条）对照

| # | 验收条目 | 怎么满足的 |
| --- | --- | --- |
| 1 | 90 关全部存在 Profile，不允许空关 | `MentalAttackValidator.ValidateAll` 逐关检查；CI 每次构建跑一遍（`DiagInternalChapters`） |
| 2 | 每关至少 1 个 Canonical 事件、3 个选项、明确 Follow-up | 90/90 覆盖，每条恰好 3 项；缺 Follow-up 直接判错 |
| 3 | 恰好 1 个 best，运行时顺序随机 | `MentalAttackSystem.Shuffle` 每次呈现都 Fisher-Yates 洗牌，实际顺序写进历史（`optionOrder`）可核对 |
| 4 | 两个干扰项必须属于不同错误家族 | 180 个干扰项逐个归入 13 个家族（Avoidance / Overcontrol / Overcorrection / WaitDependency / Rumination / Recklessness / Overpersistence / ProofFight / SelfPunishment / Denial / Conformity / IdentityFreeze / Catastrophizing）；同家族判错 |
| 5 | **答对不等于胜利** | `Choose` 选中 best 只开 Follow-up 窗口，不加 Courage、不判胜；成长证据在 `CompleteFollowUp` 才记 |
| 6 | 高速战斗不被频繁硬暂停 | 三档呈现：探索 `timeScale 0`、Boss 阶段 `0.2` 且 3 秒淡出、高速战 `0.6` 且面板压到屏幕下方 |
| 7 | 结构 / 安全 / 机制三道 Validator | `ValidateStructure` / `ValidateSafety` / `ValidateMechanism`；AI 生成物任一报错即回退 Canonical |
| 8 | AI 不可改变真弱点、命门、DeathType、Reality Victory | `ValidateMechanism` 比对 attackIntent、followUpAction、best 的 immediateEffect，任一被改写即判错 |
| 9 | 所有语言攻击都映射到实际 State Effect | 270 个选项全部带 `immediateEffect`，缺一个判错 |
| 10 | 可回溯"攻击语句 → 选择 → 后续行为 → 结果"因果链 | `MentalAttackHistoryEntry` 存了这四段；`LinkLaterOutcome` 把后续成败挂回去 |
| 11 | Mastery 升高后弹框压缩 | `SuppressedByMastery`：M0–M2 全弹、M3 六成、M4 三成、M5 不弹 |
| 12 | 无响应不视为答错 | `Dismiss` 只让攻击原效果继续生效，不扣分、不出失败音、不记错误 |

## 四、这 90 关一个外部敌人都没有——怎么保证的

三道，缺一不可：

1. **数据层**：`InternalUnitSpec.category` 恒为 `"Internal"`，不是注释而是字段；
2. **校验层**：`InternalChapterCatalog.Validate` 把任何非 Internal 的单位判为错误，CI 另有一条独立统计；
3. **组装层**：`InternalSiteComposer.ComposeEnemies` 只从 `ChapterModuleLibrary.SuggestEnemies(axis)` 取**该轴的内心敌人**，
   `SiteBlueprint.externalLines` 保持为空——没有外部敌人，也就没有外部台词。

关卡表里那些叫法（白纸哨兵、链断者、反例书记员、必须怪、模糊词雾……）是这一关的**称呼**，
不是敌人库里的类型；类型一律走已批准枚举，所以"只引用已批准 id"与"全部内部敌人"同时成立。

## 五、场景里真的有东西吗——一次被问出来的返工

玩家的原话是"目前一个场景都没有？"。查下来，问题不在场景文件（这个工程只有 2 个 .unity，
V1 的 24 个区域和第八章全是运行时用代码搭的，90 关没有场景文件是**架构如此**），
而在两个我漏掉的地方：

| 漏的 | 后果 |
| --- | --- |
| `InternalSiteComposer` 里 `rooms[].props` 一件没填 | 建出来是只有地板和墙的空屋子，房间名写着"草稿工位"，走进去什么都没有 |
| 关键物写进了 `site.interactables` | 这个字段**全工程没有任何代码读它**——9-1 的通关条件是"按 Submit"，而场上没有提交台 |

补上的是 `InternalOS/InternalProps.cs`：按 PRD 第 10.2 节的 Prefab 契约，
把 90 关点名的 54 个 PF_ 归成 10 类**行为**（提交台 / 完成标准锁 / 工作台 / 检查点 /
脱离门 / 证据台 / 判据板 / 触发物 / 归档柜 / 诱饵），程序化建出来并接到
`InternalLevelRunner` 上——走近给一行字，走到跟前真的推进或判定这一关。
落位不随机：Gate 站在出口那一侧，其余沿主路径左右交替铺开，
这样"同一关每次进来东西都在同一个地方"才成立。

合计 142 件，平均每关 1.6 件，**90 关全部有 Execution Gate**。

### 这一轮里 CI 门禁自己抓到的一个设计错误

新门禁第一次跑就报了 8 关没有 Gate。根因不是漏写，是我把**名字当成了行为的来源**：
兜底只补一个名字，类别再回头从名字猜。18-4 的通关动作是"重新进入"，兜底给出
`PF_RestartStation`，而分类器把带 `Restart` 的一律读成检查点——补出来的 Gate 不是 Gate。
改法不是加关键词打补丁，而是让兜底**直接把类别钉死**，不再经过猜名字那一步。

## 六、CI 门禁

`Assets/_Project/Editor/CIDiagnostics.cs` 新增 `DiagInternalChapters`，每次构建打印：

- 章节 / 关卡 / Boss / 内部单位计数，以及非 Internal 单位数（>0 报红）；
- 18 个 Boss 的命门、失效条件、DeathType 一览，和"允许靠打倒结束的 Boss"计数；
- 三选一的结构 / 安全错误（报红）与可读性提醒（不报红，但逐条列出）；
- **每一关的关键物件数、有没有 Execution Gate、房间里有没有道具**——缺一个报红。

最后一条是被"一个场景都没有"那次换来的。在那之前，报告绿得很好看，
而它量的全是数据：关卡数、Boss 命门、三选一结构。没有一条在量
"这一关建出来之后，场上到底有没有东西"。

另：18 个新 Boss 全部在 `MartialArchetypeCatalog.For` 里显式归了流派。
这不是可选项——CI 有一条"每个敌人都必须有显式流派"的门禁，
漏掉的表现是"一个审判官打起来像拳击手"，而代码里看不出任何问题。

---

## 那 25 关的 best 明显更长——已做一次只压长度的文案 pass

`MentalAttackValidator.ValidateReadability` 原本报了 29 条提醒，25 条是同一种：
**best 比最长的干扰项明显更长**。只要 best 恒定是最长的那条，玩家不读内容也能连对 90 关，
三选一就从"行为反制窗口"退回成"找最长的那一行"。选项顺序随机解决不了它：长度本身就是标记。

两件事一起做掉了它：

**① 量错了一半。** 原来按 `string.Length` 数，而 PRD 说的是"汉字"数。
这批文案里混着 `Goal Action` / `SuccessCondition` / `Cue-Routine-Reward` 这类机制术语，
半角字符按一个汉字算，会把屏幕上只占半格的东西判成超长。
改成 `MentalAttackValidator.DisplayWidth`（汉字 1、半角 0.5）后，25 条里有 9 条本来就不成立。

**② 其余 16 条压了长度**（实际动了 19 条——10-4 / 20-4 / 26-3 按宽度本来不超标，顺手也压短了）**。** 只动措辞，机制、术语与它指向的动作一个字没变：
`然后提交` → `再提交`、`准确分类当前能力` → `先分类当前能力`、
`如果仍在安全拉伸区，就再完成一个小动作` → `还在拉伸区就多做一步`。
PRD 原文全部留在数据里的 `canonicalText` 字段，随时可以核对改的是什么——
「只增补，不删除、不替换」在数据层仍然成立。

压得最狠的是 18-1（31 字 → 20 字）与 19-5（30 字 → 23 字），
它们的两个干扰项本来就特别短（14–18 字），best 又必须带一个双分句条件。
这两条值得写这批台词的人再看一眼措辞。

结果：**BEST_TOO_LONG 0 条、OPT_LEN 0 条、LINE_LEN 0 条**，
三选一的可读性提醒清零，结构与安全错误仍是 0。

---

## 玩家试玩之后的返工：从"数据全绿"到"这一关能玩"

玩家的原话是三条：**不知道怎么玩、游戏变复杂了、场景拥挤狭小封闭像地下室**。
逐条核对 PRD 之后，三条全部成立，而且每一条都能指到我具体做错的地方。

### 一、我做反了 PRD 的第一句话

第 3 节开篇：**"本增补不是'给玩家再加 18 套心理课程'"**。
而我交付的玩家入口，是一张 18 章 × 5 关的目录表——那就是一份课程表。
现在只放 `InternalChapterCatalog.VerifiedChapters`（当前只有第 9 章），
旅程插入也走同一道闸。其余 17 章数据一行没删：问题在实现不在数据。

### 二、场景尺度被我压成了三档

| PRD | 我交下去的 |
| --- | --- |
| 每关明写尺度（42×18、65×38、55×45、120m 路线…） | 压成 `sizeHint` 三档，建造器只有 81×62 / 60×46 / 45×34 三种盒子 |
| 4.1：战斗区≥15×20m、精英 25×30m、Boss **35-70m** | 一个数都没传下去 |
| 街道/交通/商业/公园占 World Kit 一大半 | 69/90 关落到室内类型（WK07 商业区→室内中庭） |
| 空间语言是街区、广场、路线 | 58/90 关落到 `rooms`——分隔最密的那一种 |

改法：`SiteBlueprint` 新增 `siteWidth/siteDepth`（米），`SiteBuilder` 有值就按米建；
`InternalSiteComposer.MetersOf` 从关卡表解析真实尺度，Boss 关取**最大**的那一组
（9-5 的第一组是 Reality 35×25，真正的战场是法庭 55×45），路线型关卡给狭长占地。
`rooms` 不再是兜底布局，认不出一律 `openblock`。

第 9 章五关从"五个一样的 81×62 盒子"变成：

| 关 | PRD 尺度 | 实际建造 | 布局 |
| --- | --- | --- | --- |
| 9-1 | 42m×18m×4m | 42×18 | corridor |
| 9-2 | 主厅32×24m | 32×24 | maze |
| 9-3 | 65×38m，高12m | 65×38 | maze（货架迷宫） |
| 9-4 | 主路线120m；平台宽8-15m | 80×21.6（狭长） | corridor |
| 9-5 | 法庭55×45m | 55×45 | hall |

夹在 20-80m / 15-64m：这个工程吃过"开阔到 130 米、Boss 在 91 米外、走过去什么都遇不到"的亏，
**开阔不等于空旷到找不到人**。关卡表里"约 400m 有效路线"说的是穿城路线，一个盒子表达不了它。

### 三、最重要的一条：9-1 的玩法我压根没做

关卡表把 9-1 的循环写得很清楚：

> Reality→Adversity：**停留越久，白墙按预制阶段向外扩展；粗稿生成后道路才收敛。**
> 核心物理机制：创建第一版；**识别 2 个 Critical 与 4 个 Cosmetic 修改；不要求全部优化。**

也就是：**你不动，房间就变大，提交台就离你越远**；去修那四个无关紧要的，时间在走、墙在扩；
只修两个真正阻断交付的，路才收敛，才能提交。空间本身就是完美主义的代价。

而我做出来的是一个盒子 + 三个方块 + 一条字幕——扩张没做、修改项没做、
Critical/Cosmetic 的判断没做。"不知道怎么玩"不是入口问题，是这一关的核心循环不存在。

`InternalOS/Level0901BlankPage.cs` 把它做出来了：

- 每 9 秒白墙外推一阶（最多 5 阶），提交台跟着退远；第一阶引那句"先别写。第一版这么粗糙……"
- 草稿桌 = 第一版出现 → 扩张停止，提交台匀速收回原位
- 六张修改卡长得一模一样，**卡面只写问题不写标签**（标签印在脸上就没有判断可做了）：
  两张真的挡交付（提交入口打不开／核心结论缺一段），四张只是可优化（字号、配色、语序、客套话）
- 打磨可优化项不惩罚，只如实说代价：目标控制权掉一点，粗稿没出时扩张还会提前
- 提交台按不按得动，由"有没有粗稿 + 两个阻断项处理完没有"决定

## 第二轮试玩返工：空间、规则、文字各归各位

玩家给了三条，逐条查到根因再改，不是调参数。

### 一、"空间还是狭窄封闭，一点儿不方便移动"

根因不在尺寸，在**布局是被一个词决定的**。`LayoutOf` 拿整条 `mainPath` 去匹配关键词，
9-1 的主路径是"空白工作区→草稿工位→**修改走廊**→提交区"——里面一个"走廊"，
整关就被判成 corridor。一段路的名字决定了整个场地的形状，玩家自然被夹在两排家具之间。

改法是把判断顺序摆正：关卡名/尺度里明写的形状最可信 → 路线型 → 路径里**过半**是通道才算通道关 →
其余室内给 `hall`、户外给 `openblock`。**兜底绝不再是 `rooms`**（分隔最密的那一种）。
同时把杂物调下来：clutter 2→1（Boss 关 0）、scatterProps 3→1、房间数按面积给（2/3/4）、每房道具 4→2。

离线核对 90 关的布局归属（`SiteBuilder` 会把边长夹在 20–80 × 15–64 m）：

| 关卡 | 布局 | 实建尺寸 | 面积 |
|---|---|---|---|
| 9-1 白纸之门 | hall | 42 × 18 m | 756 m² |
| 9-2 永久校稿室 | hall | 32 × 24 m | 768 m² |
| 9-3 未提交仓库 | hall | 65 × 38 m | 2470 m² |
| 9-4 全或无断崖 | openblock | 80 × 21.6 m | 1728 m² |
| 9-5 完美审判庭 | hall | 55 × 45 m | 2475 m² |

全 90 关重算一遍：hall 55 / openblock 25 / corridor 9 / maze 1，**没有一关再落到 `rooms`**。

### 二、"游戏规则不清楚，通关需要做什么？"

根因是 HUD 的目标行在**教错误的通关方式**。`SiteGate` 按场上敌人存活数写这一行，
清完就打"这里清空了，从来路走出去"——而 PRD 3.4 写的是"不以清怪定义胜利"。

改法两层：

1. 目标行改由关卡自己给（`InternalLevelRunner.ObjectiveLine()`），只说**下一步**，随进度变。
2. 新增数据栏 `playerObjective`——玩家看的那句人话，和验收条件 `realityVictory` 分开。
   原来目标行直接显示验收条件原文："把当前目标箱装上货车，车门关闭并生成 Reality Evidence。"
   玩家得先解析"Reality Evidence"才知道推哪个箱子。两者都留着，各说各的话。

第 9 章五条目标行（其余 85 关留空，回落到验收条件，不会空行）：

| 关卡 | 目标行 |
|---|---|
| 9-1 | 先去【工作台】做出第一版，再挑出真正挡住交付的问题，最后去【提交台】 |
| 9-2 | 把改动分成"挡交付的"和"只是想改的"，处理完前者就去【提交台】停手 |
| 9-3 | 只把当前这一箱推到【装车月台】装车——旧箱子不用管 |
| 9-4 | 沿平台走到【终点门】；掉下去不算输，从最近的检查点接着走 |
| 9-5 | 走完四道修改门、锁定完成标准，然后在窗口期按下【提交台】——不必打空它的血条 |

目标行里点名的东西，场上必须真有那块牌子，所以补了两样：
`InternalProps.GateLabel()` 按 PF 名给牌面（过去所有 Gate 一律写"提交台"，
9-3 的装车月台、9-4 的终点门顶上挂的都是"提交台"），
以及一条 CI 门禁——逐关把目标行里每个【X】和 `PlanFor` 真会摆出的牌面对一遍。

### 三、"哪些文字起到什么作用？"

三套文字都在 48–52 号上一起喊：墙上的房间名、每个道具头顶的跟随牌、字幕。
新增 `OpenWorldBuilder.SmallSign`（22 号、可读距离 16 m）给"走到跟前才需要读"的东西，
关键物和修改卡改用它；六张修改卡的牌面只留"·"——它们的内容本来就该走近了才读到，
六个一模一样的大字牌既是噪点，又把真正该读的内容挡在后面。

### 顺带查出来的三个真 bug

核对目标行指向时发现的，都不是文案问题：

1. **9-3 的目标箱本身被判成了 Execution Gate。** `KindOf` 用子串匹配，
   而 `PF_UnsubmittedBox` 里的 "Unsubmitted" 含 "Submit"。后果是玩家走到箱子两米内**直接通关**——
   而这一关的玩法恰恰是"把正确的那一箱推到月台"。箱子成了终点，这一关就没有了。
   同一类误判还有 14-1 的 `PF_DecisionCriteriaBoard`（"Board" 本指上车口）。
   改法是把更具体的类别排到 `GateConsole` 前面——Gate 是兜底语义，不是优先语义；
   并新增 `Cargo` 类别（`Mailbox/Inbox/Toolbox` 里的 "Box" 单独挡掉，别把 11-3 的邮箱也当货物）。
2. **目标箱过去没有任何行为。** 现在带上它会跟着玩家走，月台空手按不动
   （`GateReady` 增加"箱子在不在手上"）——核心机制那句"推、搬、绕、攀"得看得见。
3. **`InternalProps.ResetSession()` 一个调用点都没有。** `_doneLocked` 按 levelId 记在静态表里，
   于是**重进同一关时上一趟的进度还在**，第二次走到 Gate 直接过。现在每次进关清零。

离线复核（不跑游戏，只算数据与分类）：90 关**每关恰好一个 Execution Gate**，
目标行指向对不上的 0 处，带目标箱的关卡只有 9-3。

### 还没做的，说清楚

- **9-2 / 9-4 / 9-5 还只有关键物，没有各自的循环**，和 9-1 返工前是一个状态。
  （9-3 这一轮因为修 Gate 误判，顺带把"搬箱子到月台"这条循环做出来了。）
  下一步按同样方式逐关做：9-2 的边际收益下降、9-4 的失误只影响这一段、9-5 的四门与 Submit 窗口。
- **关卡仍然建在 x=20000 之外的独立坐标区**（复用既有 `SiteBuilder` 管线）。
  这违反 PRD 3.2/3.3 的"Adversity 在原现实坐标上叠加……而非被传送到另一个游戏"，
  也是"不符合开放世界特性"的根因。它要改既有管线、波及 AI 章节，单独排期。
- 我仍然没有跑过游戏。以上都是代码与数据层面的事实，手感要你来验。
