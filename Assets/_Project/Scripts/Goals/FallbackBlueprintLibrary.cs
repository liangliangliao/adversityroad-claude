using UnityEngine;
using AdversityRoad.Personalization;

namespace AdversityRoad.Goals
{
    /// <summary>
    /// Fallback Blueprint Library（方案 18.3 最后一条 / 23.3 第 25 条）：
    /// **AI 不可用时游戏仍必须能正常运行**——没有网络、没有 API Key、生成失败或被校验拒绝时，
    /// 这里用本地规则从同一个模块库里组装出一个合法的 AIRequired 章节。
    ///
    /// 它不是"占位符"：产出的蓝图同样绑定里程碑、同样有物理机制、
    /// 同样声明成功条件与失败后果，同样要过 Validator。
    /// </summary>
    public static class FallbackBlueprintLibrary
    {
        /// <summary>每条弱点轴一套本地专属章节骨架（名字/机制/成败条件都不同，保证差异性）。</summary>
        struct Skeleton
        {
            public string name;
            public string[] physical;
            public string success;
            public string failure;
        }

        static Skeleton SkeletonFor(WeaknessAxis axis)
        {
            switch (axis)
            {
                case WeaknessAxis.LowConfidence:
                    return new Skeleton
                    {
                        name = "需求迷宫",
                        physical = new[] { "explore_branching", "timed_actions" },
                        success = "在分叉的需求街区里选出必须做的那条路，并把暂缓项明确挂起",
                        failure = "时间债务增加，下一阶段的可用窗口变窄，但会出现一次重新排期的机会"
                    };
                case WeaknessAxis.FailureFear:
                    return new Skeleton
                    {
                        name = "错误之城",
                        physical = new[] { "source_hunt", "protect_zone" },
                        success = "找到连锁故障的真正源头，并保住已经跑通的稳定区",
                        failure = "连锁故障扩散一格，路线关闭，但解锁一条恢复与复盘路线"
                    };
                case WeaknessAxis.Procrastination:
                    return new Skeleton
                    {
                        name = "发布前夜",
                        physical = new[] { "timed_actions", "beacon_ignite" },
                        success = "在有限时间内完成关键动作，而不是把时间花在低价值打磨上",
                        failure = "机会窗口滑走，时间推进，但训练与补给节点开放"
                    };
                case WeaknessAxis.BoundaryConflict:
                    return new Skeleton
                    {
                        name = "代价清算所",
                        physical = new[] { "route_cost", "protect_zone" },
                        success = "对每一次索取给出明确回应，并守住自己的资源区",
                        failure = "资源被持续扣款，关系状态变化，但会出现一次重新谈判的机会"
                    };
                case WeaknessAxis.NoiseSensitivity:
                    return new Skeleton
                    {
                        name = "信号噪声区",
                        physical = new[] { "stealth_observe", "route_cost" },
                        success = "在持续干扰下把注意力收回到真正的目标上并完成推进",
                        failure = "专注被拿走，误锁定低价值目标，但注意力回收点在附近生成"
                    };
                case WeaknessAxis.FairnessSensitivity:
                    return new Skeleton
                    {
                        name = "事实对质台",
                        physical = new[] { "evidence_collect", "boss_phase" },
                        success = "集齐事实碎片并当场对质，把模糊叙事钉在事实上",
                        failure = "争议悬置，公平刺痛升高，但会开放一条成本判断路线"
                    };
                case WeaknessAxis.Shame:
                    return new Skeleton
                    {
                        name = "评价长廊",
                        physical = new[] { "terrain_climb", "escort_protect" },
                        success = "在被注视的全程里完成动作，不为躲避目光而改变路线",
                        failure = "自尊受损，动作反馈变沉，但镜前恢复点开启"
                    };
                case WeaknessAxis.JobAnxiety:
                    return new Skeleton
                    {
                        name = "回音收发室",
                        physical = new[] { "resource_scavenge", "timed_actions" },
                        success = "在沉默反馈中完成约定次数的投递动作，并至少拿到一次真实反馈",
                        failure = "沉默继续，但会解锁新的情报渠道与训练机会"
                    };
                case WeaknessAxis.WillpowerCollapse:
                    return new Skeleton
                    {
                        name = "断电街区",
                        physical = new[] { "resource_scavenge", "route_cost" },
                        success = "从一个取暖点规划到下一个，找到资源与支持并抵达出口",
                        failure = "意志见底被送回最近安全点，恢复变慢但支持节点常驻开放"
                    };
                default:
                    return new Skeleton
                    {
                        name = "起步空场",
                        physical = new[] { "beacon_ignite", "explore_branching" },
                        success = "点燃分散的行动灯台，把「还没准备好」变成「已经开始」",
                        failure = "行动力流失，路线变长，但五分钟火种台常驻"
                    };
            }
        }

        /// <summary>
        /// 为一个障碍生成本地专属章节蓝图。
        /// 章节名会带上目标关键词，让同一条轴在不同目标下也长得不一样（差异性要求）。
        /// </summary>
        public static GoalChapterData Build(GoalData goal, GoalObstacle ob, int seedSalt)
        {
            if (goal == null || ob == null) return null;
            var sk = SkeletonFor(ob.axis);
            ChapterModuleLibrary.SuggestEnemies(ob.axis, out var ext, out var inn, out var boss);

            int seed = Mathf.Abs((goal.goalId + ob.obstacleId + seedSalt).GetHashCode());
            var rng = new System.Random(seed);

            string keyword = GoalKeyword(goal.title);
            var bp = new GoalChapterData
            {
                chapterId = "ai_" + goal.goalId + "_" + ob.obstacleId,
                source = ChapterSource.AIRequired,
                linkedGoalId = goal.goalId,
                linkedMilestoneId = ob.linkedMilestoneId,
                chapterName = string.IsNullOrEmpty(keyword) ? sk.name : keyword + "·" + sk.name,
                worldDistrictId = ChapterModuleLibrary.DistrictFor(ob.axis),
                primaryObstacle = ob.obstacleId,
                secondaryObstacle = SecondObstacle(goal, ob),
                bossArchetype = boss.ToString(),
                successCondition = sk.success,
                failureConsequence = sk.failure,
                assemblySeed = seed
            };
            foreach (var m in sk.physical) bp.physicalMechanics.Add(m);
            // 第三个物理机制按种子轮换，保证同一条轴在不同目标下机制组合不同
            var pool = ChapterModuleLibrary.AllMechanicIds(true);
            string extra = pool[rng.Next(pool.Count)];
            if (!bp.physicalMechanics.Contains(extra)) bp.physicalMechanics.Add(extra);

            bp.mentalMechanics.Add(ChapterModuleLibrary.MentalMechanicFor(ob.axis));
            bp.externalEnemies.Add(ext.ToString());
            bp.internalEnemies.Add(inn.ToString());
            return bp;
        }

        /// <summary>从目标标题里抽一个短关键词做章节名前缀（不含真实姓名/地址等隐私内容）。</summary>
        static string GoalKeyword(string title)
        {
            if (string.IsNullOrEmpty(title)) return "";
            string t = title.Trim();
            foreach (var w in new[] { "我要", "我想", "希望", "打算", "准备" })
                if (t.StartsWith(w)) t = t.Substring(w.Length);
            if (t.Length > 6) t = t.Substring(0, 6);
            return t;
        }

        static string SecondObstacle(GoalData goal, GoalObstacle primary)
        {
            foreach (var o in goal.obstacles)
                if (o != primary && !o.removed) return o.obstacleId;
            return "";
        }
    }
}
