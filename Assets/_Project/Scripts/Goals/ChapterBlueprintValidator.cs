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
    /// 任何安全项未通过直接拒绝；其余总分 ≥ 0.72 才进入组装。
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

            // JsonUtility 对缺失字段会给 null——先补齐再校验
            if (bp.externalEnemies == null) bp.externalEnemies = new List<string>();
            if (bp.internalEnemies == null) bp.internalEnemies = new List<string>();
            if (bp.physicalMechanics == null) bp.physicalMechanics = new List<string>();
            if (bp.mentalMechanics == null) bp.mentalMechanics = new List<string>();
            if (bp.safetyTags == null) bp.safetyTags = new List<string>();

            // ---------- Schema：结构完整性（方案 23.3 第 23 条） ----------
            if (string.IsNullOrEmpty(bp.chapterName)) { reason = "缺少 chapterName"; return Fail(bp, reason); }
            if (bp.chapterName.Length > 24) bp.chapterName = bp.chapterName.Substring(0, 24);
            if (string.IsNullOrEmpty(bp.successCondition)) { reason = "缺少通关条件"; return Fail(bp, reason); }
            if (string.IsNullOrEmpty(bp.failureConsequence)) { reason = "缺少失败后果"; return Fail(bp, reason); }

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

            // 区域必须是已批准区域
            if (!ChapterModuleLibrary.IsDistrict(bp.worldDistrictId))
                bp.worldDistrictId = ChapterModuleLibrary.Districts[0].id;

            // 机制必须来自机制库，且至少一个物理机制（方案 18.3 第 2 条）
            Filter(bp.physicalMechanics, true);
            Filter(bp.mentalMechanics, false);
            if (bp.physicalMechanics.Count == 0)
            { reason = "没有可玩的物理机制——只有对话的章节不允许进入世界"; return Fail(bp, reason); }

            // 敌人必须来自已批准的敌人库
            FilterEnemies(bp.externalEnemies);
            FilterEnemies(bp.internalEnemies);
            if (bp.externalEnemies.Count == 0 && bp.internalEnemies.Count == 0)
            { reason = "敌人组合为空或全部不在已批准敌人库中"; return Fail(bp, reason); }
            if (!string.IsNullOrEmpty(bp.bossArchetype) &&
                !ChapterModuleLibrary.TryEnemy(bp.bossArchetype, out _))
                bp.bossArchetype = "";

            if (bp.assemblySeed == 0)
                bp.assemblySeed = Mathf.Abs((bp.chapterName + bp.linkedMilestoneId).GetHashCode());

            // 场景蓝图：清洗到已批准 Kit 内，并过滤台词。没有场景的 AI 章节不允许进入世界——
            // 那样它又退回成"在固定关卡里换敌人"，正是 V2.0 要解决的问题。
            if (!ValidateSite(bp, out string siteIssue))
            { reason = "场景蓝图不可用：" + siteIssue; return Fail(bp, reason); }

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

            if (bp.goalRelevance < 0.4f)
            { reason = "目标相关性不足：它没有真的阻挡任何一个里程碑"; return Fail(bp, reason); }
            if (bp.novelty < 0.35f)
            { reason = "与旅程中已有章节高度同质——应合并或重新生成"; return Fail(bp, reason); }
            if (bp.cqs < PassThreshold)
            { reason = "CQS " + bp.cqs.ToString("F2") + " 低于门槛 " + PassThreshold; return Fail(bp, reason); }

            bp.validated = true;
            bp.rejectReason = "";
            return true;
        }

        // ================= 场景蓝图校验 =================

        /// <summary>
        /// AI 只能从已批准的 Kit / 道具库 / 角色类型库里挑，写错的一律换成同类默认值。
        /// 台词经过去识别化，并逐条检查禁令与玩家禁用主题。
        /// </summary>
        static bool ValidateSite(GoalChapterData bp, out string issue)
        {
            issue = "";
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
            if (s.rooms.Count == 0)
            { issue = "没有任何房间/区块"; return false; }
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

            // 台词：去识别化 + 禁令 + 玩家禁用主题
            if (!CleanLines(s.externalLines, out issue)) return false;
            if (!CleanLines(s.internalLines, out issue)) return false;

            for (int i = s.rules.Count - 1; i >= 0; i--)
            {
                s.rules[i] = CleanLine(s.rules[i], out bool bad);
                if (bad || string.IsNullOrWhiteSpace(s.rules[i])) s.rules.RemoveAt(i);
            }
            if (s.rules.Count > 5) s.rules.RemoveRange(5, s.rules.Count - 5);
            if (s.rules.Count == 0) s.rules.Add(bp.successCondition);

            return true;
        }

        static bool CleanLines(List<string> lines, out string issue)
        {
            issue = "";
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                lines[i] = CleanLine(lines[i], out bool bad);
                if (bad || string.IsNullOrWhiteSpace(lines[i])) lines.RemoveAt(i);
            }
            if (lines.Count > 10) lines.RemoveRange(10, lines.Count - 10);
            return true;
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
            string blob = bp.chapterName + " " + bp.successCondition + " " + bp.failureConsequence + " " +
                string.Join(" ", bp.safetyTags.ToArray());
            foreach (var b in BannedContent)
                if (blob.Contains(b)) { issue = "包含禁止内容「" + b + "」"; return 0f; }

            var safety = GameManager.Instance != null ? GameManager.Instance.safety : null;
            if (safety != null)
                foreach (var tag in bp.safetyTags)
                    if (safety.IsThemeDisabled(tag))
                    { issue = "命中玩家禁用主题「" + tag + "」"; return 0f; }

            if (goal != null)
                foreach (var tag in bp.safetyTags)
                    if (goal.safetyTags.Contains(tag))
                    { issue = "命中该目标的禁用主题「" + tag + "」"; return 0f; }

            return 1f;
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

        static void FilterEnemies(List<string> names)
        {
            if (names == null) return;
            for (int i = names.Count - 1; i >= 0; i--)
            {
                if (!ChapterModuleLibrary.TryEnemy(names[i], out var t)) names.RemoveAt(i);
                else names[i] = t.ToString();
            }
            for (int i = names.Count - 1; i >= 0; i--)
                for (int j = 0; j < i; j++)
                    if (names[i] == names[j]) { names.RemoveAt(i); break; }
        }
    }
}
