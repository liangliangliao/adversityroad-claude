# 第八章「羞耻与污名线」验收对照表（V2.1 增补包 §8.14，第 36–47 条）

这份表把设计方案里的 12 条验收标准逐条落到代码上：**每一条都指到具体文件与具体做法**，
方便逐项核对，也方便以后改动时知道哪一条会被碰到。

> 说明：第 36–47 条是 V2.1 增补包在原 §23 验收标准（1–35 条）之后的续编号。

| # | 验收条目 | 落在哪里 | 怎么满足的 |
| --- | --- | --- | --- |
| 36 | 第八章可被 Goal OS 按目标相关性动态插入，且能说明它正在阻挡哪一个里程碑 | `Goals/LegacyChapterCatalog.cs` | 新增两条 `LegacyArc`（zoneIndex 25 / 26，axis = Shame），带 `obstacleHint` 与 `riftHint`；`SelectFor(goal, max)` 按目标里的障碍轴挑出本章，`obstacleHint` 就是"它在挡什么"的那句话 |
| 37 | 8-1 的广播室门全程保持开启，系统不提供强制引导把玩家推进门内 | `World/ZoneBuilder.BuildDebtCorridor`、`Shame/ShameProps.BroadcastDoor` | 门只有门框、没有门板，也没有开关状态；关卡里不摆任何箭头/任务标记/引导光带。`BroadcastDoor` 只是一个进门触发器，玩家自己走进去才开陈述面板 |
| 38 | 8-1 中每一次使用隐瞒类交互物，长廊必须发生玩家可见的延长 | `Shame/CorridorGrowthSystem.cs` | `NoteConcealment` 真的往世界里加几何体（地板 + 两侧墙 + 一扇每周门 + 一盏灯），并把整间广播室往后挪一整段；分段挂在自己的根节点下并重烘这一块导航面 |
| 39 | 8-2 的三个目标交互物全部位于视线锥内，且存在至少一条可行但被注视的完成路径 | `World/ZoneBuilder.BuildEchoClassroom`、`Core/GameBootstrap.SpawnShameLineMinions` | 三名侧目者的位置与**朝向**逐个对位：主锥罩住讲台前的「归还」，另两只在座位区交叉、「完成本职」落在交叉处，「步行离场」要穿过全场。锥内稳态（8 秒不回避）是被注视状态下的可行解 |
| 40 | 8-2 的低语链被打断后 8 秒内从另一处重建，且不存在使其永久停止的解法 | `Shame/WhisperChainSystem.cs` | `RebuildDelay = 8f`；重建时按"离玩家最远"的一端起链，所以一定是从别处接上。没有任何接口能让它永久停止——`EnableForLevel(false)` 只在离开关卡时调用 |
| 41 | 认领不终审对 truthTag = true 有效，对 truthTag = false 无效并产生硬直 | `Shame/OwnNotFinalSystem.ResolveOwn` | 分流写在同一个方法里：`_claim.truthTag` 为假时不清钉、不减暴露，直接 `RequestState(HitReaction)` 并照常吃这次指认；为真时才清钉、Exposure -25、废除复用权 |
| 42 | 身份钉只能被认领不终审清除；恢复点、道具与时间流逝均无法清除 | `Shame/IdentityNailSystem.cs` | 对外只有三个清除入口：`ClearAll`（认领不终审调用）、`KeepOneForNextLevel`（8-1 普通结算）、`ResetForExit`（离开本章）。没有任何按时间或按道具触发的路径 |
| 43 | Exposure 仅在本章、且仅在被有效注视时显示，不进入常驻 HUD | `Shame/ShameHudOverlay.cs`、`Shame/ExposureSystem.cs` | HUD 组只在 `ShameLine.InChapter` 时挂出，且要 `RecentlyGazed \|\| Revealed \|\| Value > 0.5` 才显示那一组；底层数值照常运行并写进 `ShameLineData` |
| 44 | 本章任意公开场景均可随时退出，并可用文字复盘替代而不阻断主线 | `Shame/StatementSystem.cs`、`Shame/ShameProps.WeeklyInquiry` | 陈述面板永远有第三个按钮「先出去」，以及「以文字复盘替代」（照常推进主线，只记普通结算）；每周追问面板永远有「退出」 |
| 45 | 本章失败结算中不出现围观特写、嘲笑音效或任何形式的羞辱演出 | `Shame/ShameLineController.OnSelfWorthZero` / `OnCaseTimerExpired`、`AI/PendingJudgeBoss.DemandPublic` | 失败只改变世界状态与路线：回最近恢复点（不回退进度）或长廊闭环回起点。「要求当众」这一招在**取消**的那一刻就结束，全程没有一个围观镜头——当众处罚只作为威胁存在，不作为已执行的演出 |
| 46 | 本章至少产出 1 个可升格为 T7 的宿敌，其学习内容仅来自行为标签 | `AI/BackRowWhispererBoss.EvaluateNemesis` | 否认 ≥5 次或触发搜查回响即登记为宿敌候选，`displayName` 升格为「未播出的广播」。写入 `learnedPlayerPatterns` 的只有行为标签（"否认优先于认领"、"搜查回响已执行"），不含任何玩家输入原文 |
| 47 | 本章复盘必须以绑定 Goal Graph 节点的「行动」栏收尾，否则不允许提交 | `UI/ReflectionPanel.ShameActionGateBlocked` | 在第八章内，行动栏为空或过短直接拒绝归档；有在途目标时还必须用「绑定目标节点」选一个未完成节点。没有在途目标时只要求行动栏非空——不因为玩家还没建目标就卡死主线 |

## 章节专属禁止项（§8.7.1）对照

| 禁止项 | 落在哪里 |
| --- | --- |
| 禁止在 SelfWorth 低于 25% 时生成新的指认招式 | `OwnNotFinalSystem.Accuse` 开头的 `NoAccuseSelfWorthRatio` 检查 |
| 禁止连续两次挂钉而不给出至少一个认领窗口 | 挂钉只有一条路径：`OwnNotFinalSystem` 判定失败后调用 `Mount`；而 `Accuse` 必开判定窗，所以天然满足 |
| 禁止使用玩家在游戏外输入的经历文本原文作为敌人台词 | `Shame/ClaimRegistry.cs` 的指控素材是写死的行为标签数组，与玩家输入完全不通 |
| 禁止把视线锥做成不可见或不可预测 | `GazeCone.Update` 里可见度被 `Mathf.Clamp(visibility, 0.35f, 1f)` 兜住下限，锥体是真的画出来的地面扇形 |
| 禁止在失败结算中使用围观特写、嘲笑音效或慢镜头羞辱 | 见上表第 45 条 |

## 逆境预算（§8.7）对照

`Adversity/AdversityBudget.cs` 在五维分配处为本章单独设一组权重：

| 维度 | 方案建议 | 代码 |
| --- | --- | --- |
| Physical | 15% | `wPhysical = 0.15f`（全局默认是 0.34） |
| Mental | 40% | `wMental = 0.40f` |
| Environmental | 25% | `wEnv = 0.25f` |
| Resource | 10% | `wResource = 0.10f` |
| Time | 10% | `wTime = 0.10f`，且沿用既有规则——目标不带截止日期时这一维归零，不滥用 |

Physical 被主动压低是**主题决定的，不是难度取巧**：本章的压力来自注视、指认与低语。
单次遭遇的指认次数上限 5 次落在 `AI/ShameLineEnemies.NailAccuser.maxAccusations`。

## 优势发挥窗口（§8.7.2）对照

| 窗口 | 触发条件 | 落在哪里 |
| --- | --- | --- |
| 连续认领窗口 | 连续 2 次认领成功 | `OwnNotFinalSystem.WindowLength()`：`_ownStreak >= 2` 时第 3 次用 `SlowWindow`（16 帧） |
| 锥内稳态 | 在视线锥内连续行动 8 秒未回避 | `ExposureSystem.EnterSteady / TickSteadyReward`：增速减半 + 每秒回补自尊 |
| 提前陈述奖励 | 计时器高位主动进入陈述 | `ShameLineController.OnStatementCompleted`：剩余 ≥40% 记最佳结算，授予「自述之证」并降低后续关卡初始 Exposure（`ExposureGainMult`） |

## Stress State Machine 映射（§8.10.3）与 Resolve Window（§8.10.4）对照

`Shame/ShameStressMapping.cs` 订阅全局压力机器的阶段变化，只做**本章的表现落位**，
不改任何数值曲线：

| 阶段 | 本章表现 | 实现 |
| --- | --- | --- |
| Strained | 环境音出现方向性低语，视线锥边缘微亮 | 锥体可见度 → 0.5 + 一句字幕 |
| Destabilized | 视线锥可见度提升，Exposure 首次显示 | 可见度 → 0.75，推一点点暴露度让 HUD 那一组真的出现 |
| Overloaded | 多条视线锥交叉，低语链完整成形 | 可见度 → 1.0，8-2 里强制启用低语链 |
| Near Collapse | 画面边缘收缩、脚步声放大，Resolve Window 可触发 | 全局机器已 `OpenWindow()`，这里补一句写明本章的三条触发条件 |
| Breakdown | 短暂低头 / 解除锁定，不超过 12 秒，禁止围观特写 | 播低头片段 + 解除锁定；全局机器把 Breakdown 压在 3 秒内，**不加任何围观镜头** |

Resolve Window 的三条本章触发条件（`NoteQualityAction` 只在窗口开着时才算数）：
锥内完成一次完整目标交互而未回避（`ExposureSystem.EnterSteady` / `CompleteObjective`）、
满身份钉状态下完成一次认领不终审（`OwnNotFinalSystem.ResolveOwn`）、
低语链完整活跃时正常步行通过全场（`ShameLineController.NoteWalkOut`）。

## 逆袭判定（§8.11）对照

`ShameLineController.EvaluateComeback()` 逐条检查，过三项即成立。**不看伤害，也不看胜负**：

1. 指控复用失效 —— `ClaimRegistry.SpentCount() >= ownCount`
2. 陈述提前量提升 —— `statementHistory` 末次 `timingRatio` 高于首次
3. 锥内行动稳定 —— 三个目标动作全部完成
4. 否认频率下降 —— `ownCount > denialCount`
5. 宿敌降级 —— 玩家在场时低语链无法完整成形

## 新增连招（§8.8.4）对照

`Shame/ShameComboTracker.cs` 按语义步骤匹配，每步之间 14 秒窗口：

| 连招 | 步骤 | 用途 |
| --- | --- | --- |
| 自述三段 | 认领不终审 → 事实之刃 → 自行陈述 | Boss 终局：先拔钉、再陈述事实、最后主动公开 |
| 破钉式 | 精准格挡 → 认领不终审 → 真实一击 | 身份钉兵的连续指认链 |
| 聚光穿越 | 聚光灯校准 → 稳定站位 → 目标动作 | 在视线锥内完成长按交互 |
| 不上庭反制 | 不上庭 → 边界盾 → 解除锁定 | 拒绝被拖入低价值的「判词」交互 |

`ShameComboTracker.Push` 在章外直接返回，所以这四组不会在前七章里冒出来。

## 复盘时发现并补上的缺口（第二轮）

第一轮实现之后按方案逐条回查，有五处对不上，已补：

| 方案条目 | 原来的状态 | 现在 |
| --- | --- | --- |
| 8.3 上升来源「被指认招式命中」 | 只有否认才涨暴露度，被指认打中不涨 | `OwnNotFinalSystem.ApplyAccusationDamage` 命中 +10 |
| 8.3 上升来源「使用隐瞒类交互物成功」 | 只抬了上限，当下的值没动 | `CorridorGrowthSystem.NoteConcealment` 抬上限 +10、当下 +6 |
| 8.3 上升来源「讨好度上升」 | 完全没接 | `AppeasementSystem.Appease` 按讨好增量的 40% 上涨 |
| 8.3 下降来源「完成目标相关行动」 | 完全没接 | `ShameLineController.CompleteObjective` -15 |
| 8.11.1 记录项要能被看到 | `HistorySummary()` 写了但没人调用 | 接进「逆境史」面板；没进过本章时不占版面 |

另外把杂兵等级按附录对位（讨好回声 T3、身份钉兵 T3、心虚投影 T4 改为精英；
后排低语组 T2、伪装同学 T2 改为标准），此前一律按见习/标准生成。

**一处按现状保留并说明**：8.11.1 最后一行「最终结算方式 → 生成 Hall of Goals 纪念条目」。
Hall of Goals 在这套代码里是**按目标**立碑的（`GoalOS.CompleteGoal`），
为一个章节单独造一块碑会污染它的语义。本章的结算改为写进 Adversity History，
等玩家真的走完一个目标时，它作为那块碑的材料出现。

## 两个实机 bug 的根因（第二轮）

| 现象 | 根因 | 修法 |
| --- | --- | --- |
| 两关敌人打不死 | `pacified` 身兼两职：既是"不主动攻击"，也是"免疫伤害"。用它表达"不动手"，于是每周追问者 / 旁观耳语者 / 三名侧目者 / 未敌化的伪装同学全部零反应 | 新增 `EnemyController.passive`：不出手不追击，但照常吃伤害、照常倒下，被打也不还手 |
| 打不死的那几个（法官 / 低语者 / 讨好回声 / 心虚投影）读不出是设计 | 各自留着一根几乎不动的血条 | 新增 `EnemyController.emotionOverride`，把"没有血条 / 血由否认维持 / 只能靠降讨好度削"常驻写在头顶 |
| 打倒侧目者后视线锥永久消失，绕开就能通关 | 锥的持有者被清光后没有补位 | `GazeConeSystem.ScheduleRelay`：20 秒后由另一个人从别处补上（与低语链 8 秒重建同一条道理） |
| 反复被拽回关卡起点 | ① 全关只有入口一个恢复点，"最近恢复点"永远等于起点；② 重新武装门槛（自尊 >8）低于回补量（+26），而本章暴露度高时自尊伤害翻倍，几秒就再次归零；③ 搬人没有冷却 | 恢复点自登记、取最近；两关各摆 4 / 3 处；回补到三分之一上限并同时降 20 点暴露度；门槛提到四分之一上限；两次回落至少隔 60 秒，冷却内只进羞耻状态不搬人 |

## 第三轮：把"不可击杀"收回到方案真正要的那一条

实机反馈是"两个关卡敌人打不死"。回头看，方案说的是**打赢不等于通关**
（终结条件在广播室的门与三个目标动作上），而我把它实现成了**刀砍不动**——
两个 Boss 各自叠着"伤害压到 10~15%"＋"血线卡死"两道闸门，再加上讨好回声、
心虚投影两个常驻血线，两个小关里有四类单位对攻击几乎零反馈。
那不是主题，那是把战斗系统关掉了。

| 单位 | 原来 | 现在 |
| --- | --- | --- |
| 悬案法官 | 伤害 ×0.15 且血线 35% | 伤害照常结算；打到血线他**坐下不再还手**，战斗有结论，案子照样挂着 |
| 后排低语者 | 伤害 ×0.1 且血线 5% | 伤害照常；血由否认次数回补仍是机制——**停止否认就能把他磨到说不出话**，正好是方案要的"他停止发声" |
| 讨好回声 | 常驻血线 15% | 讨好度归零即卸下血线，**打得倒** |
| 心虚投影 | 常驻血线 20% | 认领成功后的 12 秒透明期即卸下血线，**打得倒** |
| 侧目者补位 | 倒下 20 秒后必定有人补上 | 延到 45 秒，且**只在场上注视少于两道时**才补——清场必须换来真实喘息 |

留下的只有一条：**打赢 Boss 不会让关卡结束**。这条是方案的核心命题，保留。

## 本章不提供的东西

没有「洗清」结算。没有 NPC 宣布玩家清白，没有道歉，没有误会解除。
`BackRowWhispererBoss.Silence()` 里只有一句字幕：他停下来了，玩家继续走出去。

玩家在本章获得的不是清白，是**行动能力**。
