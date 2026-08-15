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
