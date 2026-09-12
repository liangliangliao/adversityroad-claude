using System;
using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.AI;
using AdversityRoad.Goals;
using AdversityRoad.Personalization;

namespace AdversityRoad.InternalOS
{
    /// <summary>
    /// 把第 9-26 章接到 Goal OS 上。
    ///
    /// 【最高原则：Goal OS 始终高于心理训练（第 0 节）】
    /// 这 18 章不是一条要从头打到尾的主线。只有当某种内部机制**真的挡住当前 Goal/Milestone**
    /// 时，对应章节才会被插进旅程。所以这里只有两个入口：
    /// <see cref="SelectFor"/>（按目标里的障碍轴挑）与 <see cref="ToBlueprint"/>（包成统一蓝图）。
    /// 没有"解锁第 9 章"这种 API——因为顺序不该由章节号决定。
    ///
    /// 【和 LegacyChapterCatalog 的分工】
    /// Legacy 那一份回答"外面有什么挡着你"（不公、噪声、他人索取）；
    /// 这一份回答"你自己有什么挡着你"（不敢交付、启动不了、停不下来）。
    /// 两份共用同一个 GoalChapterData 契约，旅程图上看不出来源差别——这是故意的。
    /// </summary>
    public static class InternalChapterBridge
    {
        /// <summary>章节的弱点轴（数据里存的是枚举名）。</summary>
        public static WeaknessAxis AxisOf(InternalChapterInfo ch)
        {
            WeaknessAxis axis;
            return ch != null && Enum.TryParse(ch.axis ?? "", out axis)
                ? axis : WeaknessAxis.Procrastination;
        }

        /// <summary>按目标里的障碍轴挑出最相关的内部章节（与 LegacyChapterCatalog.SelectFor 同形）。</summary>
        public static List<InternalChapterInfo> SelectFor(GoalData goal, int max)
        {
            var picked = new List<InternalChapterInfo>();
            if (goal == null) return picked;

            var chapters = InternalChapterCatalog.Chapters;
            for (int i = 0; i < goal.obstacles.Count; i++)
            {
                if (picked.Count >= max) break;
                var ob = goal.obstacles[i];
                if (ob.removed) continue;

                for (int j = 0; j < chapters.Count; j++)
                {
                    var ch = chapters[j];
                    if (AxisOf(ch) != ob.axis) continue;
                    if (picked.Contains(ch)) continue;
                    picked.Add(ch);
                    break;
                }
            }
            return picked;
        }

        /// <summary>
        /// 按根障碍挑：同一条控制链上的多个 Boss 出现时，Keystone 优先（第 7.3 节）。
        /// 传入 A-G 的根障碍代号。
        /// </summary>
        public static List<InternalChapterInfo> ByRootObstacle(string rootCode)
        {
            var list = new List<InternalChapterInfo>();
            var chapters = InternalChapterCatalog.Chapters;
            for (int i = 0; i < chapters.Count; i++)
                if (chapters[i].rootObstacle == rootCode) list.Add(chapters[i]);
            return list;
        }

        /// <summary>
        /// 包成统一的章节蓝图。
        ///
        /// 注意 externalEnemies 恒为空：这 18 章按定义没有外部敌人。
        /// 校验器（ChapterBlueprintValidator）如果要求"至少一个敌人"，它会从 internalEnemies 里拿到。
        /// </summary>
        public static GoalChapterData ToBlueprint(InternalChapterInfo ch, GoalData goal, GoalObstacle ob)
        {
            if (ch == null) return null;

            var axis = AxisOf(ch);
            string p1, p2, mental;
            ChapterModuleLibrary.SuggestMechanics(axis, out p1, out p2, out mental);

            var boss = ch.boss;
            var bp = new GoalChapterData
            {
                chapterId = "internal_" + ch.chapterId,
                source = ChapterSource.Legacy,   // 策划冻结件，与 Legacy 同级：直接视为已校验
                linkedGoalId = goal != null ? goal.goalId : "",
                linkedMilestoneId = ob != null ? ob.linkedMilestoneId : "",
                chapterName = ch.title,
                worldDistrictId = ChapterModuleLibrary.IsDistrict(ch.districtId)
                    ? ch.districtId : ChapterModuleLibrary.DistrictFor(axis),
                primaryObstacle = ob != null ? ob.obstacleId : "",
                bossArchetype = boss != null ? boss.enemyType : "",
                successCondition = boss != null
                    ? boss.executionGate + "（DeathType=" + boss.deathType + "）"
                    : ch.core,
                failureConsequence = "这条线上的逆境加深，但会留下可复盘的控制链与恢复节点",
                validated = true,
                goalRelevance = 0.9f,
                playability = 1f,
                playerFit = 0.9f,
                novelty = 0.8f,
                narrativeCoherence = 1f,
                safetyCompliance = 1f,
                cqs = 0.9f,
                assemblySeed = Mathf.Abs((ch.chapterId ?? "").GetHashCode()),
            };

            // 内心敌人：Boss + 本章各关出现过的投影单位（去重，取前几个避免蓝图过长）。
            if (boss != null && !string.IsNullOrEmpty(boss.enemyType))
                bp.internalEnemies.Add(boss.enemyType);
            var seen = new HashSet<string>();
            for (int i = 0; i < ch.levels.Count && bp.internalEnemies.Count < 6; i++)
            {
                var units = ch.levels[i].internalUnits;
                for (int j = 0; j < units.Count && bp.internalEnemies.Count < 6; j++)
                {
                    if (!seen.Add(units[j].displayName)) continue;
                    bp.internalEnemies.Add(units[j].displayName);
                }
            }

            bp.physicalMechanics.Add("boss_phase");
            bp.physicalMechanics.Add(p1);
            if (p2 != p1) bp.physicalMechanics.Add(p2);
            bp.mentalMechanics.Add(mental);

            // 章节节点落在 Boss 关那处场景上：旅程图上点进去，走进的就是这一章收束的地方。
            var bossLevel = BossLevelOf(ch);
            bp.site = InternalSiteComposer.Compose(bossLevel, ch);
            bp.enemyPlan = InternalSiteComposer.ComposeEnemies(bossLevel, ch);

            return bp;
        }

        public static InternalLevelData BossLevelOf(InternalChapterInfo ch)
        {
            if (ch == null) return null;
            for (int i = 0; i < ch.levels.Count; i++)
                if (ch.levels[i].isBossLevel) return ch.levels[i];
            return ch.levels.Count > 0 ? ch.levels[ch.levels.Count - 1] : null;
        }

        /// <summary>
        /// 单关 → 可建造的蓝图。SiteBuilder.BuildRoutine 吃的就是这个类型，
        /// 所以 90 关和 AI 章节走的是同一条建造管线，不存在"内部章节专用建造器"。
        /// </summary>
        public static GoalChapterData ToLevelBlueprint(InternalLevelData lv, GoalData goal = null)
        {
            if (lv == null) return null;
            var ch = InternalChapterCatalog.Chapter(lv.chapterId);
            var axis = AxisOf(ch);

            string p1, p2, mental;
            ChapterModuleLibrary.SuggestMechanics(axis, out p1, out p2, out mental);

            var bp = new GoalChapterData
            {
                chapterId = "internal_level_" + lv.levelId.Replace('-', '_'),
                source = ChapterSource.Legacy,
                linkedGoalId = goal != null ? goal.goalId : "",
                chapterName = lv.levelId + "《" + lv.name + "》",
                worldDistrictId = ChapterModuleLibrary.IsDistrict(lv.districtId)
                    ? lv.districtId : ChapterModuleLibrary.DistrictFor(axis),
                successCondition = lv.realityVictory,
                failureConsequence = "保留 Checkpoint 与控制链；失败本身要留下可验证的信息（第 3.5 节）",
                validated = true,
                goalRelevance = 0.9f,
                playability = 1f,
                playerFit = 0.9f,
                novelty = 0.8f,
                narrativeCoherence = 1f,
                safetyCompliance = 1f,
                cqs = 0.9f,
                // 同一关每次组装必须一模一样：Bug 才可复现，验收才有意义。
                assemblySeed = Mathf.Abs((lv.levelId ?? "").GetHashCode()),
            };

            for (int i = 0; i < lv.internalUnits.Count && bp.internalEnemies.Count < 6; i++)
                bp.internalEnemies.Add(lv.internalUnits[i].displayName);
            if (lv.isBossLevel && ch != null && ch.boss != null)
                bp.bossArchetype = ch.boss.enemyType;

            bp.physicalMechanics.Add(p1);
            if (p2 != p1) bp.physicalMechanics.Add(p2);
            if (lv.isBossLevel) bp.physicalMechanics.Add("boss_phase");
            bp.mentalMechanics.Add(mental);

            bp.site = InternalSiteComposer.Compose(lv, ch);
            bp.enemyPlan = InternalSiteComposer.ComposeEnemies(lv, ch);
            return bp;
        }

        /// <summary>Boss 的 EnemyType（数据里存的是枚举名）。认不出来时返回 false，不做就近映射。</summary>
        public static bool TryBossEnemyType(InternalBossDNA boss, out EnemyType type)
        {
            type = EnemyType.TomorrowPhantom;
            if (boss == null || string.IsNullOrEmpty(boss.enemyType)) return false;
            return Enum.TryParse(boss.enemyType, out type);
        }

        /// <summary>
        /// 这一关的 Boss 是否已经可以判定失效。
        ///
        /// **不看血条**：18 个 Boss 里只有 DeathType=Defeat 的那种才允许靠打倒结束，
        /// 其余一律要求对应的行为条件发生（提交、出门、归档、撤销裁判权……）。
        /// </summary>
        public static bool BossDefeated(string levelId, bool executionGatePassed, bool hpCleared)
        {
            var boss = InternalChapterCatalog.BossOfLevel(levelId);
            if (boss == null) return hpCleared;
            if (InternalChapterCatalog.IsKillBoss(boss))
                return hpCleared && executionGatePassed;
            return executionGatePassed;
        }
    }
}
