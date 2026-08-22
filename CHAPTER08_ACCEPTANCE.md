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

## 优势发挥窗口（§8.7.2）对照

| 窗口 | 触发条件 | 落在哪里 |
| --- | --- | --- |
| 连续认领窗口 | 连续 2 次认领成功 | `OwnNotFinalSystem.WindowLength()`：`_ownStreak >= 2` 时第 3 次用 `SlowWindow`（16 帧） |
| 锥内稳态 | 在视线锥内连续行动 8 秒未回避 | `ExposureSystem.EnterSteady / TickSteadyReward`：增速减半 + 每秒回补自尊 |
| 提前陈述奖励 | 计时器高位主动进入陈述 | `ShameLineController.OnStatementCompleted`：剩余 ≥40% 记最佳结算，授予「自述之证」并降低后续关卡初始 Exposure（`ExposureGainMult`） |

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

## 本章不提供的东西

没有「洗清」结算。没有 NPC 宣布玩家清白，没有道歉，没有误会解除。
`BackRowWhispererBoss.Silence()` 里只有一句字幕：他停下来了，玩家继续走出去。

玩家在本章获得的不是清白，是**行动能力**。
