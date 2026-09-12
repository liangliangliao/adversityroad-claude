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

## 五、CI 门禁

`Assets/_Project/Editor/CIDiagnostics.cs` 新增 `DiagInternalChapters`，每次构建打印：

- 章节 / 关卡 / Boss / 内部单位计数，以及非 Internal 单位数（>0 报红）；
- 18 个 Boss 的命门、失效条件、DeathType 一览，和"允许靠打倒结束的 Boss"计数；
- 三选一的结构 / 安全错误（报红）与可读性提醒（不报红，但逐条列出）。

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
