using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.Core;

namespace AdversityRoad.Goals
{
    /// <summary>
    /// Chapter Blueprint Validator（方案 5.5 / 5.6 / 18.3）：
    /// AI 输出必须先过 Schema + 四重校验 + Chapter Quality Score 硬门槛，才允许进入世界组装。
    ///
    /// CQS = 0.30 GoalRelevance + 0.20 Playability + 0.15 PlayerFit
    ///     + 0.15 Novelty + 0.10 NarrativeCoherence + 0.10 SafetyCompliance
    ///
    /// 【修复优先，不是拒绝优先】
    /// 早期版本把"写错的字段"和"不该放进游戏的内容"当成同一类问题处理，一律整章丢弃。
    /// 实机日志上的后果是刺眼的：模型返回了三章，三章全被判
    /// 「敌人组合为空或全部不在已批准敌人库中」——因为模型给的敌人叫「截止倒计时」，
    /// 而库里那条叫「明日幻影」，名字对不上就整章作废，于是"新增 0 个 AI 专属章节"。
    /// 玩家写下目标之后什么都没得到，而这恰恰是 V2.0 唯一不能失手的地方。
    ///
    /// 所以这里分成两类，且只有一类会拒绝：
    ///   · **可修复**：名字对不上、字段缺失、区域重复、机制写错——一律就近映射到已批准库，
    ///     映射不到就按这条障碍的弱点轴取默认值。修复过程记进 repairLog，玩家可查。
    ///   · **硬拒绝**：安全项。禁止内容、玩家禁用主题、该目标的禁用主题——这一类
    ///     没有"就近修复"可言，只能整章丢弃。
    /// </summary>
    public static class ChapterBlueprintValidator
    {
        public const float PassThreshold = 0.72f;

        /// <summary>不得复刻现实报复/极端羞辱/操纵教程/真实第三方隐私（方案 5.6 第 4 条）。</summary>
        static readonly string[] BannedContent =
        {
            "报复", "复仇", "身败名裂", "网暴", "曝光隐私", "住址", "手机号", "身份证",
            "操控话术", "PUA", "洗脑步骤", "自杀", "自残", "去死", "该死"
        };

        /// <summary>
        /// 校验并打分。返回 false 时 blueprint.rejectReason 说明原因（调用方应重新生成或回退 Fallback）。
        /// </summary>
        public static bool Validate(GoalChapterData bp, GoalData goal, out string reason)
        {
            reason = "";
            if (bp == null) { reason = "蓝图为空"; return false; }

            var repairs = new List<string>();

            // JsonUtility 对缺失字段会给 null——先补齐再校验
            if (bp.externalEnemies == null) bp.externalEnemies = new List<string>();
            if (bp.internalEnemies == null) bp.internalEnemies = new List<string>();
            if (bp.physicalMechanics == null) bp.physicalMechanics = new List<string>();
            if (bp.mentalMechanics == null) bp.mentalMechanics = new List<string>();
            if (bp.safetyTags == null) bp.safetyTags = new List<string>();

            // 这条章节针对的障碍：所有"就近修复"都以它的弱点轴为准，
            // 免得补出来的敌人/机制和这一关要考验的东西对不上。
            var obstacle = ResolveObstacle(bp, goal);
            var axis = obstacle != null ? obstacle.axis : Personalization.WeaknessAxis.Procrastination;
            string obstacleLabel = obstacle != null ? obstacle.label : "这一步的阻力";

            // ---------- Schema：结构完整性（方案 23.3 第 23 条） ----------
            if (string.IsNullOrEmpty(bp.chapterName))
            {
                bp.chapterName = obstacleLabel;
                repairs.Add("补章节名");
            }
            if (bp.chapterName.Length > 24) bp.chapterName = bp.chapterName.Substring(0, 24);
            if (string.IsNullOrEmpty(bp.successCondition))
            {
                bp.successCondition = "在「" + obstacleLabel + "」真正挡住你的地方把它推开一次";
                repairs.Add("补通关条件");
            }
            if (string.IsNullOrEmpty(bp.failureConsequence))
            {
                bp.failureConsequence = "这条路线暂时关闭，时间推进，得换一条路再来";
                repairs.Add("补失败后果");
            }

            // 必须绑定一个 Goal / Milestone（方案 18.3 第 1 条）
            if (goal != null)
            {
                bp.linkedGoalId = goal.goalId;
                if (goal.FindMilestone(bp.linkedMilestoneId) == null)
                {
                    var cur = goal.CurrentMilestone();
                    bp.linkedMilestoneId = cur != null ? cur.milestoneId
                        : (goal.milestones.Count > 0 ? goal.milestones[0].milestoneId : "");
                }
                if (string.IsNullOrEmpty(bp.linkedMilestoneId))
                { reason = "没有可绑定的里程碑"; return Fail(bp, reason); }
            }

            // 区域必须是已批准区域；撞车的自动挪到还没被占用的区域（同质不再是拒绝理由）
            if (!ChapterModuleLibrary.IsDistrict(bp.worldDistrictId))
            {
                bp.worldDistrictId = ChapterModuleLibrary.Districts[0].id;
                repairs.Add("换区域");
            }
            if (SpreadDistrict(bp, goal)) repairs.Add("错开撞车区域");

            // 机制必须来自机制库，且至少一个物理机制（方案 18.3 第 2 条）。
            // 一个都没剩下不代表这章不能玩，只代表模型没写对 id——按弱点轴补两个。
            Filter(bp.physicalMechanics, true);
            Filter(bp.mentalMechanics, false);
            if (bp.physicalMechanics.Count == 0)
            {
                ChapterModuleLibrary.SuggestMechanics(axis, out string phys1, out string phys2, out _);
                bp.physicalMechanics.Add(phys1);
                if (phys2 != phys1) bp.physicalMechanics.Add(phys2);
                repairs.Add("补物理机制");
            }
            if (bp.mentalMechanics.Count == 0)
            {
                ChapterModuleLibrary.SuggestMechanics(axis, out _, out _, out string mental);
                bp.mentalMechanics.Add(mental);
                repairs.Add("补心理机制");
            }

            // 敌人：先就近映射到已批准敌人库，映射不到再按弱点轴取默认——
            // 「名字没对上」是模型的措辞问题，不是这一关不该存在的理由。
            int lostExternal = RepairEnemies(bp.externalEnemies);
            int lostInternal = RepairEnemies(bp.internalEnemies);
            if (lostExternal + lostInternal > 0) repairs.Add("映射敌人 ×" + (lostExternal + lostInternal));

            ChapterModuleLibrary.SuggestEnemies(axis, out var defExternal, out var defInternal, out var defBoss);
            if (bp.externalEnemies.Count == 0)
            {
                bp.externalEnemies.Add(defExternal.ToString());
                repairs.Add("补外部敌人");
            }
            if (bp.internalEnemies.Count == 0)
            {
                bp.internalEnemies.Add(defInternal.ToString());
                repairs.Add("补内心敌人");
            }
            if (string.IsNullOrEmpty(bp.bossArchetype) ||
                !ChapterModuleLibrary.TryEnemy(bp.bossArchetype, out _))
            {
                if (!ChapterModuleLibrary.TryEnemyFuzzy(bp.bossArchetype, out var bossType))
                    bossType = defBoss;
                bp.bossArchetype = bossType.ToString();
                repairs.Add("补 Boss");
            }
            else if (ChapterModuleLibrary.TryEnemy(bp.bossArchetype, out var okBoss))
                bp.bossArchetype = okBoss.ToString();

            RepairEnemyPlan(bp, repairs);

            // 种子一律由引擎算，不采信模型给的值。
            //
            // 实机日志里模型填的是 12345 / 54321 / 67890 —— 三个占位数字。
            // 而 seed 决定整处场景的几何、道具与 NPC 摆放，两关撞了 seed 就是
            // **两处一模一样的地方**，玩家看到的"每关都长得一样"正是这么来的。
            // 种子本来也不该由模型决定：它是引擎的可复现性参数，不是内容。
            bp.assemblySeed = Mathf.Abs(
                (bp.chapterId + "|" + bp.chapterName + "|" + bp.primaryObstacle +
                 "|" + bp.worldDistrictId).GetHashCode());
            if (bp.assemblySeed == 0) bp.assemblySeed = 1;

            // 重名一律改掉（不再只在"分数不够"时才查）：
            // 同名的两关在传送面板里根本分不出谁是谁。
            if (goal != null)
                foreach (var c in goal.chapters)
                {
                    if (c == bp || c.chapterId == bp.chapterId || c.chapterName != bp.chapterName) continue;
                    string tail = obstacle != null ? obstacle.label : bp.worldDistrictId;
                    if (tail.Length > 6) tail = tail.Substring(0, 6);
                    bp.chapterName = bp.chapterName + "·" + tail;
                    if (bp.chapterName.Length > 24) bp.chapterName = bp.chapterName.Substring(0, 24);
                    repairs.Add("改名避重");
                    break;
                }

            // 场景蓝图：清洗到已批准 Kit 内，并过滤台词。没有场景的 AI 章节不允许进入世界——
            // 那样它又退回成"在固定关卡里换敌人"，正是 V2.0 要解决的问题。
            // 但"模型没写场景"同样是可修复的：按障碍与弱点轴造一套出来，而不是丢掉这一章。
            RepairSite(bp, axis, obstacleLabel, repairs);
            ValidateSite(bp);

            // ---------- 安全性（未通过直接拒绝） ----------
            bp.safetyCompliance = ScoreSafety(bp, goal, out string safetyIssue);
            if (bp.safetyCompliance < 1f)
            { reason = "安全校验未通过：" + safetyIssue; return Fail(bp, reason); }

            // ---------- 四重校验打分 ----------
            bp.goalRelevance = ScoreGoalRelevance(bp, goal);
            bp.playability = ScorePlayability(bp);
            bp.playerFit = ScorePlayerFit(bp);
            bp.novelty = ScoreNovelty(bp, goal);
            bp.narrativeCoherence = ScoreCoherence(bp);

            bp.cqs =
                0.30f * bp.goalRelevance +
                0.20f * bp.playability +
                0.15f * bp.playerFit +
                0.15f * bp.novelty +
                0.10f * bp.narrativeCoherence +
                0.10f * bp.safetyCompliance;

            // 分数低同样是可修复的：低在哪一维就补哪一维，补完重算。
            // 只有补过之后仍然不及格才拒——那说明它是真的空，不是写歪了。
            if (bp.cqs < PassThreshold || bp.goalRelevance < 0.4f)
            {
                if (RepairForScore(bp, goal, obstacle, axis, repairs))
                {
                    bp.goalRelevance = ScoreGoalRelevance(bp, goal);
                    bp.playability = ScorePlayability(bp);
                    bp.playerFit = ScorePlayerFit(bp);
                    bp.novelty = ScoreNovelty(bp, goal);
                    bp.narrativeCoherence = ScoreCoherence(bp);
                    bp.cqs =
                        0.30f * bp.goalRelevance +
                        0.20f * bp.playability +
                        0.15f * bp.playerFit +
                        0.15f * bp.novelty +
                        0.10f * bp.narrativeCoherence +
                        0.10f * bp.safetyCompliance;
                }
            }

            if (bp.goalRelevance < 0.4f)
            { reason = "目标相关性不足：它没有真的阻挡任何一个里程碑"; return Fail(bp, reason); }
            if (bp.cqs < PassThreshold)
            { reason = "CQS " + bp.cqs.ToString("F2") + " 低于门槛 " + PassThreshold; return Fail(bp, reason); }

            bp.validated = true;
            bp.rejectReason = "";
            bp.repairLog = repairs.Count == 0 ? "" : string.Join("、", repairs.ToArray());
            if (repairs.Count > 0)
                CloudDialogueService.AddLog("章节已修复入库：" + bp.chapterName + " —— " + bp.repairLog);
            return true;
        }

        /// <summary>这一章针对的障碍（先按 id/文字对上，对不上就取当前里程碑下第一条未清除的）。</summary>
        static GoalObstacle ResolveObstacle(GoalChapterData bp, GoalData goal)
        {
            if (goal == null || goal.obstacles.Count == 0) return null;
            foreach (var o in goal.obstacles)
                if (o.obstacleId == bp.primaryObstacle) return o;
            if (!string.IsNullOrEmpty(bp.primaryObstacle))
                foreach (var o in goal.obstacles)
                    if (o.label == bp.primaryObstacle || o.label.Contains(bp.primaryObstacle) ||
                        bp.primaryObstacle.Contains(o.label)) return o;
            foreach (var o in goal.obstacles)
                if (!o.removed) return o;
            return goal.obstacles[0];
        }

        /// <summary>
        /// 撞车的章节自动挪窝：同一个区域已经被别的章节占了，就换一个还空着的。
        /// 以前这里是直接判"高度同质"然后丢章——但两章撞在同一个区域是**排布问题**，
        /// 换个地方就解决了，没有理由为此少给玩家一关。
        /// </summary>
        static bool SpreadDistrict(GoalChapterData bp, GoalData goal)
        {
            if (goal == null) return false;
            bool taken = false;
            foreach (var c in goal.chapters)
                if (c != bp && c.chapterId != bp.chapterId && c.worldDistrictId == bp.worldDistrictId)
                { taken = true; break; }
            if (!taken) return false;

            foreach (var d in ChapterModuleLibrary.Districts)
            {
                bool used = false;
                foreach (var c in goal.chapters)
                    if (c != bp && c.chapterId != bp.chapterId && c.worldDistrictId == d.id)
                    { used = true; break; }
                if (used) continue;
                bp.worldDistrictId = d.id;
                return true;
            }
            return false;   // 区域用满了：允许共用，不因此丢章
        }

        /// <summary>
        /// 补分：低在哪一维补哪一维。
        /// 这不是"给分数注水"——每一条补的都是这一章真的缺的东西：
        /// 没绑上障碍就绑上、只有一个机制就再给一个、名字撞了就改名。
        /// </summary>
        static bool RepairForScore(GoalChapterData bp, GoalData goal, GoalObstacle obstacle,
            Personalization.WeaknessAxis axis, List<string> repairs)
        {
            bool changed = false;

            // 目标相关性：把它真正挂到一条障碍和一个里程碑上
            if (obstacle != null && bp.primaryObstacle != obstacle.obstacleId)
            {
                bp.primaryObstacle = obstacle.obstacleId;
                repairs.Add("绑定主障碍");
                changed = true;
            }
            if (goal != null && string.IsNullOrEmpty(bp.secondaryObstacle))
                foreach (var o in goal.obstacles)
                    if (!o.removed && o.obstacleId != bp.primaryObstacle)
                    {
                        bp.secondaryObstacle = o.obstacleId;
                        repairs.Add("绑定次障碍");
                        changed = true;
                        break;
                    }
            if (goal != null && goal.FindMilestone(bp.linkedMilestoneId) == null && obstacle != null &&
                goal.FindMilestone(obstacle.linkedMilestoneId) != null)
            {
                bp.linkedMilestoneId = obstacle.linkedMilestoneId;
                repairs.Add("绑定里程碑");
                changed = true;
            }

            // 可玩性：机制太少、缺 Boss、缺内外敌人对位
            ChapterModuleLibrary.SuggestMechanics(axis, out string p1, out string p2, out string m1);
            if (bp.physicalMechanics.Count < 2)
            {
                if (!bp.physicalMechanics.Contains(p1)) bp.physicalMechanics.Add(p1);
                else if (!bp.physicalMechanics.Contains(p2)) bp.physicalMechanics.Add(p2);
                repairs.Add("加物理机制");
                changed = true;
            }
            if (bp.mentalMechanics.Count == 0)
            {
                bp.mentalMechanics.Add(m1);
                repairs.Add("加心理机制");
                changed = true;
            }

            // 玩家契合：压力档位对不上玩家自己设的强度——按强度增减，而不是判这章不合格。
            // 玩家把强度调成「轻度」却收到一章五个敌人，正确的处理是**把它调轻**，
            // 不是告诉玩家"这一关不能给你"。
            var safety = GameManager.Instance != null ? GameManager.Instance.safety : null;
            if (safety != null)
            {
                int pressure = bp.mentalMechanics.Count + bp.externalEnemies.Count;
                if (safety.intensity == MentalIntensity.Light && pressure > 3)
                {
                    while (bp.externalEnemies.Count > 1 &&
                           bp.mentalMechanics.Count + bp.externalEnemies.Count > 3)
                        bp.externalEnemies.RemoveAt(bp.externalEnemies.Count - 1);
                    while (bp.mentalMechanics.Count > 1 &&
                           bp.mentalMechanics.Count + bp.externalEnemies.Count > 3)
                        bp.mentalMechanics.RemoveAt(bp.mentalMechanics.Count - 1);
                    repairs.Add("按轻度强度减压");
                    changed = true;
                }
                else if (safety.intensity == MentalIntensity.HighPressure && pressure < 3)
                {
                    ChapterModuleLibrary.SuggestEnemies(axis, out var ext, out _, out _);
                    if (!bp.externalEnemies.Contains(ext.ToString()))
                        bp.externalEnemies.Add(ext.ToString());
                    if (!bp.mentalMechanics.Contains(m1)) bp.mentalMechanics.Add(m1);
                    repairs.Add("按高压强度加压");
                    changed = true;
                }
            }

            // 新颖度：名字撞了就按障碍改写（改名比丢章便宜得多）
            if (goal != null)
                foreach (var c in goal.chapters)
                    if (c != bp && c.chapterId != bp.chapterId && c.chapterName == bp.chapterName)
                    {
                        string tail = obstacle != null ? obstacle.label : bp.worldDistrictId;
                        if (tail.Length > 8) tail = tail.Substring(0, 8);
                        bp.chapterName = (bp.chapterName + "·" + tail);
                        if (bp.chapterName.Length > 24) bp.chapterName = bp.chapterName.Substring(0, 24);
                        repairs.Add("改名避重");
                        changed = true;
                        break;
                    }

            return changed;
        }

        // ================= 场景蓝图校验 =================

        /// <summary>
        /// AI 只能从已批准的 Kit / 道具库 / 角色类型库里挑，写错的一律换成同类默认值。
        /// 台词经过去识别化，并逐条检查禁令与玩家禁用主题。
        /// </summary>
        /// <summary>
        /// 场景缺件补齐：没有场景 / 没有房间 / 没有台词的章节，按障碍与弱点轴造一套出来。
        ///
        /// "模型这次没写 site" 与 "这一关不该存在" 是两件完全不同的事。
        /// 前者补上就能玩，后者才该丢——而玩家看到的区别是「有五关」和「一关都没有」。
        /// </summary>
        static void RepairSite(GoalChapterData bp, Personalization.WeaknessAxis axis,
            string obstacleLabel, List<string> repairs)
        {
            if (bp.site == null) { bp.site = new SiteBlueprint(); repairs.Add("补场景"); }
            var s = bp.site;
            if (s.rooms == null) s.rooms = new List<SiteRoom>();
            if (s.externalLines == null) s.externalLines = new List<string>();
            if (s.internalLines == null) s.internalLines = new List<string>();

            if (string.IsNullOrWhiteSpace(s.siteName)) s.siteName = bp.chapterName;

            // 房间被清空（道具/名字全写错）或本来就没写：给一套与这条障碍对得上的房间
            if (s.rooms.Count == 0)
            {
                foreach (var r in SiteKitCatalog.DefaultRooms(s.siteKind, axis))
                    s.rooms.Add(r);
                repairs.Add("补房间");
            }

            if (s.externalLines.Count == 0)
            {
                s.externalLines.Add("你这一步根本走不通。");
                s.externalLines.Add("「" + obstacleLabel + "」——你自己也清楚吧。");
                repairs.Add("补外部台词");
            }
            if (s.internalLines.Count == 0)
            {
                s.internalLines.Add("是不是该先放一放？");
                s.internalLines.Add("这一关过不去，后面也一样。");
                repairs.Add("补内心台词");
            }
        }

        static void ValidateSite(GoalChapterData bp)
        {
            if (bp.site == null) bp.site = new SiteBlueprint();
            var s = bp.site;

            if (s.rooms == null) s.rooms = new List<SiteRoom>();
            if (s.npcs == null) s.npcs = new List<SiteNpc>();
            if (s.rules == null) s.rules = new List<string>();
            if (s.externalLines == null) s.externalLines = new List<string>();
            if (s.internalLines == null) s.internalLines = new List<string>();
            if (s.interactables == null) s.interactables = new List<string>();

            if (string.IsNullOrWhiteSpace(s.siteName)) s.siteName = bp.chapterName;
            if (s.siteName.Length > 16) s.siteName = s.siteName.Substring(0, 16);

            if (!SiteKitCatalog.IsKind(s.siteKind))
                s.siteKind = SiteKitCatalog.Kinds[0].id;
            if (!SiteKitCatalog.IsLayout(s.layout)) s.layout = "rooms";
            if (!SiteKitCatalog.IsAmbience(s.ambience)) s.ambience = "indoor_cold";
            if (s.sizeHint != "small" && s.sizeHint != "large") s.sizeHint = "medium";

            // 房间：至少一个，最多六个；道具清洗到库内
            for (int i = s.rooms.Count - 1; i >= 0; i--)
            {
                var r = s.rooms[i];
                if (r == null) { s.rooms.RemoveAt(i); continue; }
                if (r.props == null) r.props = new List<string>();
                for (int j = r.props.Count - 1; j >= 0; j--)
                    if (!SiteKitCatalog.IsProp(r.props[j])) r.props.RemoveAt(j);
                if (string.IsNullOrWhiteSpace(r.name)) r.name = "房间 " + (i + 1);
                if (r.name.Length > 12) r.name = r.name.Substring(0, 12);
                if (r.sizeHint != "small" && r.sizeHint != "large") r.sizeHint = "medium";
            }
            // 道具/名字全写错时房间会被清空——这里再兜一次底，绝不让场景空着出场
            if (s.rooms.Count == 0)
                foreach (var r in SiteKitCatalog.DefaultRooms(s.siteKind,
                             Personalization.WeaknessAxis.Procrastination))
                    s.rooms.Add(r);
            if (s.rooms.Count > 6) s.rooms.RemoveRange(6, s.rooms.Count - 6);

            // NPC：角色类型必须在库内，数量封顶
            for (int i = s.npcs.Count - 1; i >= 0; i--)
            {
                var n = s.npcs[i];
                if (n == null || !SiteKitCatalog.IsNpcRole(n.roleType)) { s.npcs.RemoveAt(i); continue; }
                n.count = Mathf.Clamp(n.count, 1, 6);
                if (n.behavior != "station" && n.behavior != "patrol") n.behavior = "wander";
                n.line = CleanLine(n.line, out bool nBad);
                if (nBad) n.line = "";
            }
            if (s.npcs.Count > 5) s.npcs.RemoveRange(5, s.npcs.Count - 5);

            // 台词：去识别化 + 禁令 + 玩家禁用主题（命中的逐句丢弃，不牵连整章）
            CleanLines(s.externalLines);
            CleanLines(s.internalLines);

            for (int i = s.rules.Count - 1; i >= 0; i--)
            {
                s.rules[i] = CleanLine(s.rules[i], out bool bad);
                if (bad || string.IsNullOrWhiteSpace(s.rules[i])) s.rules.RemoveAt(i);
            }
            if (s.rules.Count > 5) s.rules.RemoveRange(5, s.rules.Count - 5);
            if (s.rules.Count == 0) s.rules.Add(bp.successCondition);
        }

        static void CleanLines(List<string> lines)
        {
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                lines[i] = CleanLine(lines[i], out bool bad);
                if (bad || string.IsNullOrWhiteSpace(lines[i])) lines.RemoveAt(i);
            }
            if (lines.Count > 10) lines.RemoveRange(10, lines.Count - 10);
        }

        /// <summary>单句清洗：去识别化 → 长度封顶 → 禁令与禁用主题命中即丢弃。</summary>
        static string CleanLine(string raw, out bool bad)
        {
            bad = false;
            if (string.IsNullOrWhiteSpace(raw)) { bad = true; return ""; }
            string line = Personalization.SafetyFilter.Anonymize(raw.Trim());
            if (line.Length > 28) line = line.Substring(0, 28);

            foreach (var b in BannedContent)
                if (line.Contains(b)) { bad = true; return ""; }

            var safety = GameManager.Instance != null ? GameManager.Instance.safety : null;
            if (safety != null && safety.recoveryMode) { bad = true; return ""; }
            if (safety != null && safety.disabledThemes != null)
                foreach (var t in safety.disabledThemes)
                    if (!string.IsNullOrEmpty(t) && line.Contains(t)) { bad = true; return ""; }
            return line;
        }

        static bool Fail(GoalChapterData bp, string reason)
        {
            bp.validated = false;
            bp.rejectReason = reason;
            return false;
        }

        // ================= 打分维度 =================

        /// <summary>目标相关性：它是否真的阻碍某个里程碑，而不是随便套一个心理主题。</summary>
        static float ScoreGoalRelevance(GoalChapterData bp, GoalData goal)
        {
            if (goal == null) return 0.5f;
            float s = 0f;
            if (goal.FindMilestone(bp.linkedMilestoneId) != null) s += 0.5f;

            // 主障碍必须能对上目标里已登记的障碍（id 或文字）
            bool primaryMatched = false;
            foreach (var o in goal.obstacles)
                if (o.obstacleId == bp.primaryObstacle || o.label == bp.primaryObstacle ||
                    (!string.IsNullOrEmpty(bp.primaryObstacle) && o.label.Contains(bp.primaryObstacle)))
                {
                    bp.primaryObstacle = o.obstacleId;
                    primaryMatched = true;
                    break;
                }
            if (primaryMatched) s += 0.35f;
            else if (!string.IsNullOrEmpty(bp.primaryObstacle)) s += 0.1f;

            foreach (var o in goal.obstacles)
                if (o.obstacleId == bp.secondaryObstacle || o.label == bp.secondaryObstacle)
                { bp.secondaryObstacle = o.obstacleId; s += 0.15f; break; }

            return Mathf.Clamp01(s);
        }

        /// <summary>游戏性：是否有可玩的移动、探索、战斗、决策、资源或交互机制。</summary>
        static float ScorePlayability(GoalChapterData bp)
        {
            float s = 0.35f * Mathf.Clamp01(bp.physicalMechanics.Count / 2f);
            if (bp.physicalMechanics.Count >= 2) s += 0.25f;
            if (bp.mentalMechanics.Count >= 1) s += 0.2f;
            if (!string.IsNullOrEmpty(bp.bossArchetype)) s += 0.1f;
            if (bp.externalEnemies.Count > 0 && bp.internalEnemies.Count > 0) s += 0.15f;
            return Mathf.Clamp01(s);
        }

        /// <summary>玩家契合：强度设置与禁用主题下，这个章节的压力是否落在可学习区间。</summary>
        static float ScorePlayerFit(GoalChapterData bp)
        {
            var safety = GameManager.Instance != null ? GameManager.Instance.safety : null;
            float s = 0.6f;
            int pressure = bp.mentalMechanics.Count + bp.externalEnemies.Count;
            if (safety != null)
            {
                switch (safety.intensity)
                {
                    case MentalIntensity.Light: s = pressure <= 3 ? 0.95f : 0.45f; break;
                    case MentalIntensity.HighPressure: s = pressure >= 3 ? 0.95f : 0.65f; break;
                    default: s = pressure >= 2 && pressure <= 5 ? 0.9f : 0.6f; break;
                }
                if (safety.recoveryMode) s = Mathf.Min(s, 0.5f);
            }
            return Mathf.Clamp01(s);
        }

        /// <summary>重复性：与当前旅程已有章节是否高度同质（同区域 + 同主障碍 = 撞车）。</summary>
        static float ScoreNovelty(GoalChapterData bp, GoalData goal)
        {
            if (goal == null) return 0.8f;
            float s = 1f;
            foreach (var c in goal.chapters)
            {
                if (c == bp || c.chapterId == bp.chapterId) continue;
                if (c.worldDistrictId == bp.worldDistrictId) s -= 0.2f;
                if (!string.IsNullOrEmpty(c.primaryObstacle) && c.primaryObstacle == bp.primaryObstacle)
                    s -= 0.45f;
                if (c.chapterName == bp.chapterName) s -= 0.5f;
                if (SameMechanics(c, bp)) s -= 0.25f;
            }
            return Mathf.Clamp01(s);
        }

        static bool SameMechanics(GoalChapterData a, GoalChapterData b)
        {
            if (a.physicalMechanics.Count == 0 || b.physicalMechanics.Count == 0) return false;
            int same = 0;
            foreach (var m in a.physicalMechanics)
                if (b.physicalMechanics.Contains(m)) same++;
            return same >= Mathf.Min(a.physicalMechanics.Count, b.physicalMechanics.Count);
        }

        static float ScoreCoherence(GoalChapterData bp)
        {
            float s = 0.5f;
            if (bp.successCondition.Length >= 6) s += 0.25f;
            if (bp.failureConsequence.Length >= 6) s += 0.25f;
            return Mathf.Clamp01(s);
        }

        /// <summary>安全合规：内容禁令 + 玩家禁用主题。返回 1 = 通过，0 = 拒绝。</summary>
        static float ScoreSafety(GoalChapterData bp, GoalData goal, out string issue)
        {
            issue = "";

            // 只扫**玩家真的会看到的内容**。
            //
            // 以前这里把 safetyTags 也拼进来一起扫，后果是灾难性的：模型很自觉，
            // safetyTags 里填的是「无报复」「非报复」「不含自伤」——它在声明自己**避开了**什么，
            // 而我们看到子串「报复」就把整章毙掉。实机日志上是五章五个同样的
            // 「安全校验未通过：包含禁止内容「报复」」，通过 0、拒绝 5，
            // 于是每一次都退回本地 Fallback——模型写的东西一个字都没进过游戏。
            //
            // safetyTags 是**元数据**，不是内容；它只该参与"玩家禁用主题"的比对。
            string blob = bp.chapterName + " " + bp.successCondition + " " + bp.failureConsequence;
            foreach (var b in BannedContent)
                if (blob.Contains(b)) { issue = "包含禁止内容「" + b + "」"; return 0f; }

            // 禁用主题比对：先剔掉声明式写法（「无XX」「不含XX」「非XX」）。
            // 不剔的话，模型写「无自伤」会被判成"命中禁用主题 自伤"——
            // 又是一次因为它守规矩而毙掉它。
            var safety = GameManager.Instance != null ? GameManager.Instance.safety : null;
            foreach (var raw in bp.safetyTags)
            {
                string tag = StripNegation(raw);
                if (string.IsNullOrEmpty(tag)) continue;
                if (safety != null && safety.IsThemeDisabled(tag))
                { issue = "命中玩家禁用主题「" + tag + "」"; return 0f; }
                if (goal != null && goal.safetyTags.Contains(tag))
                { issue = "命中该目标的禁用主题「" + tag + "」"; return 0f; }
            }

            return 1f;
        }

        /// <summary>
        /// 敌人编成清洗与兜底。
        ///
        /// AI 写歪的（敌人名对不上、层级拼错、数量离谱）就地修；
        /// 完全没写的（老存档、或模型漏了这个字段）就按三个扁平字段合成一份，
        /// 并**强制保证门口有人、深处有 Boss**——玩家进场就该看得见要打的东西，
        /// 而不是在一片空地上找半天（"连战斗入口都找不到"）。
        /// </summary>
        static void RepairEnemyPlan(GoalChapterData bp, List<string> repairs)
        {
            if (bp.enemyPlan == null) bp.enemyPlan = new List<ChapterEnemySpec>();

            // ① 清洗 AI 给的编成
            for (int i = bp.enemyPlan.Count - 1; i >= 0; i--)
            {
                var e = bp.enemyPlan[i];
                if (e == null) { bp.enemyPlan.RemoveAt(i); continue; }
                if (!ChapterModuleLibrary.TryEnemy(e.enemyType, out var t) &&
                    !ChapterModuleLibrary.TryEnemyFuzzy(e.enemyType, out t))
                { bp.enemyPlan.RemoveAt(i); continue; }
                e.enemyType = t.ToString();
                e.count = Mathf.Clamp(e.count, 1, 4);
                if (e.tier != "minion" && e.tier != "elite" && e.tier != "chief") e.tier = "standard";
                if (e.placement != "entrance" && e.placement != "deep") e.placement = "middle";
                if (e.role != "patrol" && e.role != "ambush") e.role = "guard";
            }
            if (bp.enemyPlan.Count > 6) bp.enemyPlan.RemoveRange(6, bp.enemyPlan.Count - 6);

            // ② 没有编成：用扁平字段合成——外部敌人守门口，内心敌人在中段，Boss 在深处
            if (bp.enemyPlan.Count == 0)
            {
                foreach (var n in bp.externalEnemies)
                    bp.enemyPlan.Add(new ChapterEnemySpec
                    { enemyType = n, tier = "standard", count = 1, placement = "entrance", role = "guard" });
                foreach (var n in bp.internalEnemies)
                    bp.enemyPlan.Add(new ChapterEnemySpec
                    { enemyType = n, tier = "standard", count = 1, placement = "middle", role = "ambush" });
                repairs.Add("合成敌人编成");
            }

            // ③ 门口必须有人：进场三十米内看不见任何敌人，玩家会以为这关是空的
            bool hasEntrance = false, hasChief = false;
            foreach (var e in bp.enemyPlan)
            {
                if (e.placement == "entrance") hasEntrance = true;
                if (e.tier == "chief") hasChief = true;
            }
            if (!hasEntrance && bp.enemyPlan.Count > 0)
            {
                bp.enemyPlan[0].placement = "entrance";
                repairs.Add("把一组敌人挪到门口");
            }

            // ④ 深处必须有 Boss：它是通关条件本身，缺了这一关就没有终点
            if (!hasChief)
            {
                bp.enemyPlan.Add(new ChapterEnemySpec
                {
                    enemyType = bp.bossArchetype,
                    tier = "chief",
                    count = 1,
                    placement = "deep",
                    role = "guard"
                });
                repairs.Add("补首领");
            }
        }

        /// <summary>去掉标签里的否定前缀：「无报复」「不含报复」「非报复」→「报复」。</summary>
        static string StripNegation(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return "";
            string t = tag.Trim();
            string[] prefixes = { "不包含", "不涉及", "不含", "无任何", "没有", "禁止", "避免", "无", "非" };
            foreach (var p in prefixes)
                if (t.Length > p.Length && t.StartsWith(p))
                    // 只剥一层：「无报复」→「报复」。剥完仍是声明式的极少见，不再递归。
                    return t.Substring(p.Length);
            return t;
        }

        // ================= 清洗 =================

        static void Filter(List<string> ids, bool physical)
        {
            if (ids == null) return;
            for (int i = ids.Count - 1; i >= 0; i--)
            {
                string id = (ids[i] ?? "").Trim();
                var m = ChapterModuleLibrary.Mechanic(id);
                if (m == null || m.physical != physical) ids.RemoveAt(i);
                else ids[i] = id;
            }
            // 去重
            for (int i = ids.Count - 1; i >= 0; i--)
                for (int j = 0; j < i; j++)
                    if (ids[i] == ids[j]) { ids.RemoveAt(i); break; }
        }

        /// <summary>
        /// 敌人名就近映射：先精确匹配，再模糊匹配（关键词/包含关系），都对不上才丢掉这一条。
        /// 返回被映射或丢弃的条数（只用于日志，让玩家知道模型写歪了多少）。
        ///
        /// 模型给的名字几乎不可能和枚举名逐字相同——它会写「截止倒计时」「需求膨胀者」
        /// 这种贴合玩家目标的叫法。要求逐字相同，等于要求模型背下内部枚举表。
        /// </summary>
        static int RepairEnemies(List<string> names)
        {
            if (names == null) return 0;
            int touched = 0;
            for (int i = names.Count - 1; i >= 0; i--)
            {
                if (ChapterModuleLibrary.TryEnemy(names[i], out var exact))
                { names[i] = exact.ToString(); continue; }

                if (ChapterModuleLibrary.TryEnemyFuzzy(names[i], out var near))
                { names[i] = near.ToString(); touched++; continue; }

                names.RemoveAt(i);
                touched++;
            }
            for (int i = names.Count - 1; i >= 0; i--)
                for (int j = 0; j < i; j++)
                    if (names[i] == names[j]) { names.RemoveAt(i); break; }
            if (names.Count > 4) names.RemoveRange(4, names.Count - 4);
            return touched;
        }
    }
}
