using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.AI;
using AdversityRoad.Personalization;
using AdversityRoad.World;

namespace AdversityRoad.Goals
{
    /// <summary>V1.0 经典章节的通用逆境模块描述。</summary>
    public class LegacyArc
    {
        public int zoneIndex;
        public string name;
        public WeaknessAxis axis;
        public string obstacleHint;      // 它擅长处理的障碍
        public EnemyType boss;
        public string districtId;        // 它在开放世界里从哪个区域长出来
        public string riftHint;          // 心理裂隙的现实触发条件
    }

    /// <summary>
    /// Universal Adversity Arcs（方案 1.2 / 13 章）：
    /// V1.0 的七大章节与全部关卡一个都没有删除——它们从固定线性主线
    /// **升级为 Goal OS 可动态插入的通用逆境模块库**。
    /// 拖延沼泽、责任法院、刺激街道之所以重要，是因为它们正在挡住玩家自己选的方向。
    /// </summary>
    public static class LegacyChapterCatalog
    {
        public static readonly LegacyArc[] Arcs =
        {
            // ---- 第一章 公平与承诺线 ----
            new LegacyArc { zoneIndex = 9,  name = "两元赌桌", axis = WeaknessAxis.FairnessSensitivity,
                obstacleHint = "赖账、承诺模糊、为小额不公反复消耗", boss = EnemyType.GambleKing,
                districtId = "residential", riftHint = "居民区棋牌室/社交空间发生失信事件后开启" },
            new LegacyArc { zoneIndex = 10, name = "债务车影", axis = WeaknessAxis.FairnessSensitivity,
                obstacleHint = "未结清的事长期占据生命中心", boss = EnemyType.DebtCarKing,
                districtId = "transit", riftHint = "开放世界停车场直接覆盖逆境层" },
            new LegacyArc { zoneIndex = 6,  name = "小题大做审判庭", axis = WeaknessAxis.Shame,
                obstacleHint = "自我否定、被评价、感受被判为小题大做", boss = EnemyType.SelfDenialGavel,
                districtId = "service", riftHint = "现实争议事件升级后打开法庭裂隙" },

            // ---- 第二章 外界刺激线 ----
            new LegacyArc { zoneIndex = 2,  name = "一声咳嗽的街道", axis = WeaknessAxis.NoiseSensitivity,
                obstacleHint = "注意力被环境刺激持续拿走", boss = EnemyType.CoughAssassin,
                districtId = "transit", riftHint = "城市街道现实层→逆境层的标准演示点" },
            new LegacyArc { zoneIndex = 11, name = "眼神审判走廊", axis = WeaknessAxis.Shame,
                obstacleHint = "被注视即被否定", boss = EnemyType.ThousandEyeJudge,
                districtId = "office", riftHint = "办公楼/商场/地铁走廊动态逆境化" },
            new LegacyArc { zoneIndex = 12, name = "陌生挑衅路口", axis = WeaknessAxis.NoiseSensitivity,
                obstacleHint = "被挑衅拖入与目标无关的战场", boss = EnemyType.TauntMirror,
                districtId = "transit", riftHint = "真实十字路口事件，支持战略撤退胜利" },
            new LegacyArc { zoneIndex = 2,  name = "噪声放大（终战）", axis = WeaknessAxis.NoiseSensitivity,
                obstacleHint = "所有刺激被放大成针对自己的证据", boss = EnemyType.StimulusAmplifier,
                districtId = "commercial", riftHint = "人群压力峰值时在商业区开启" },

            // ---- 第三章 拖延与目标线 ----
            new LegacyArc { zoneIndex = 13, name = "目标遗忘房", axis = WeaknessAxis.Procrastination,
                obstacleHint = "忘记自己进来是要做什么的", boss = EnemyType.GoalForgetter,
                districtId = "residential", riftHint = "Player Home 的动态状态（目标板落灰）" },
            new LegacyArc { zoneIndex = 7,  name = "拖延沼泽", axis = WeaknessAxis.Procrastination,
                obstacleHint = "明天再说、迟迟不开始", boss = EnemyType.TomorrowKing,
                districtId = "residential", riftHint = "住宅/办公地点连续忽略关键行动后裂隙打开" },
            new LegacyArc { zoneIndex = 3,  name = "求职沉默荒原", axis = WeaknessAxis.JobAnxiety,
                obstacleHint = "投出去没有回音、反馈缺失", boss = EnemyType.NoReplyKing,
                districtId = "office", riftHint = "目标与求职/投递相关时由 Goal OS 插入" },
            new LegacyArc { zoneIndex = 4,  name = "城市广场（终战）", axis = WeaknessAxis.Procrastination,
                obstacleHint = "无数个「明天再说」堆成的旧我", boss = EnemyType.ProcrastinationShadow,
                districtId = "commercial", riftHint = "拖延累积到阈值后在广场成形" },

            // ---- 第四章 边界与责任线 ----
            new LegacyArc { zoneIndex = 14, name = "老实人消耗局", axis = WeaknessAxis.BoundaryConflict,
                obstacleHint = "请求膨胀、好人卡、无法拒绝", boss = EnemyType.GoodPersonCage,
                districtId = "office", riftHint = "工作/生活大厅中的社会事件" },
            new LegacyArc { zoneIndex = 5,  name = "责任转嫁法院", axis = WeaknessAxis.BoundaryConflict,
                obstacleHint = "责任被倒扣、什么都要背", boss = EnemyType.TotalResponsibilityJudge,
                districtId = "office", riftHint = "工作争议/关系压力后进入逆境层" },
            new LegacyArc { zoneIndex = 15, name = "无限代付走廊", axis = WeaknessAxis.BoundaryConflict,
                obstacleHint = "默认代付时间/金钱/精力/情绪", boss = EnemyType.InfinitePayer,
                districtId = "residential", riftHint = "从住宅/工作区生成心理裂隙" },

            // ---- 第五章 低谷与生存线 ----
            new LegacyArc { zoneIndex = 16, name = "饥饿荒巷", axis = WeaknessAxis.WillpowerCollapse,
                obstacleHint = "基本生存资源不足", boss = EnemyType.HungerHound,
                districtId = "edge", riftHint = "边缘区小巷高风险遭遇" },
            new LegacyArc { zoneIndex = 17, name = "车库寒夜", axis = WeaknessAxis.WillpowerCollapse,
                obstacleHint = "持续消耗意志的环境压力", boss = EnemyType.ColdWindBlade,
                districtId = "edge", riftHint = "停车场/车库夜间逆境化" },
            new LegacyArc { zoneIndex = 18, name = "病房回廊", axis = WeaknessAxis.WillpowerCollapse,
                obstacleHint = "无力感、内疚重石、求助羞耻", boss = EnemyType.ValleyColossus,
                districtId = "service", riftHint = "医院区域，低动作强度高资源压力" },

            // ---- 第六章 哲学与行动线 ----
            new LegacyArc { zoneIndex = 19, name = "哲学虚无图书馆", axis = WeaknessAxis.SelfDoubt,
                obstacleHint = "只读不做、想明白才肯开始", boss = EnemyType.ConceptMazeMaster,
                districtId = "service", riftHint = "图书馆/书店类生活服务点" },
            new LegacyArc { zoneIndex = 20, name = "无限追问大厅", axis = WeaknessAxis.LowConfidence,
                obstacleHint = "问题无限延伸、行动被推迟", boss = EnemyType.QuestionBeast,
                districtId = "office", riftHint = "会议室/走廊的无限追问" },
            new LegacyArc { zoneIndex = 21, name = "意志断桥", axis = WeaknessAxis.WillpowerCollapse,
                obstacleHint = "意义焦虑与怀疑风暴", boss = EnemyType.InfiniteAsker,
                districtId = "edge", riftHint = "边缘区高空/废弃结构" },

            // ---- 终章 旧我与新我线 ----
            new LegacyArc { zoneIndex = 22, name = "失败展览馆", axis = WeaknessAxis.FailureFear,
                obstacleHint = "失败被当成身份", boss = EnemyType.PastJudge,
                districtId = "commercial", riftHint = "商业区展馆/橱窗前的旧事回放" },
            new LegacyArc { zoneIndex = 23, name = "意志塔", axis = WeaknessAxis.FailureFear,
                obstacleHint = "旧话复读成为自己的声音", boss = EnemyType.OldVoiceRepeater,
                districtId = "commercial", riftHint = "高层建筑的六层能力考验" },
            new LegacyArc { zoneIndex = 8,  name = "旧事回声馆", axis = WeaknessAxis.FailureFear,
                obstacleHint = "旧我模式反复接管当下", boss = EnemyType.OldSelf,
                districtId = "residential", riftHint = "住宅深处的旧物触发" },
        };

        /// <summary>按目标里的障碍轴挑出最相关的 Legacy 章节（按目标相关性动态插入，方案 5.1）。</summary>
        public static List<LegacyArc> SelectFor(GoalData goal, int max)
        {
            var picked = new List<LegacyArc>();
            if (goal == null) return picked;

            foreach (var ob in goal.obstacles)
            {
                if (picked.Count >= max) break;
                LegacyArc best = null;
                foreach (var arc in Arcs)
                {
                    if (arc.axis != ob.axis) continue;
                    if (picked.Contains(arc)) continue;
                    best = arc;
                    break;
                }
                if (best != null) picked.Add(best);
            }
            return picked;
        }

        /// <summary>把 Legacy 章节包装成统一的章节蓝图（Legacy 来源，直接视为已校验）。</summary>
        public static GoalChapterData ToBlueprint(LegacyArc arc, GoalData goal, GoalObstacle ob)
        {
            ChapterModuleLibrary.SuggestEnemies(arc.axis, out var ext, out var inn, out _);
            var bp = new GoalChapterData
            {
                chapterId = "legacy_" + arc.zoneIndex + "_" + arc.name.GetHashCode().ToString("x"),
                source = ChapterSource.Legacy,
                linkedGoalId = goal != null ? goal.goalId : "",
                linkedMilestoneId = ob != null ? ob.linkedMilestoneId : "",
                chapterName = arc.name,
                worldDistrictId = arc.districtId,
                primaryObstacle = ob != null ? ob.obstacleId : "",
                bossArchetype = arc.boss.ToString(),
                successCondition = "击败【" + EnemyCatalog.TypeLabel(arc.boss) + "】并解除它对这条路的封锁",
                failureConsequence = "该区域逆境层加深，路线暂时关闭，但会出现训练与恢复节点",
                legacyZoneIndex = arc.zoneIndex,
                validated = true,
                goalRelevance = 0.9f,
                playability = 1f,
                playerFit = 0.9f,
                novelty = 0.7f,
                narrativeCoherence = 1f,
                safetyCompliance = 1f,
                cqs = 0.9f,
                assemblySeed = Mathf.Abs(arc.name.GetHashCode())
            };
            bp.externalEnemies.Add(ext.ToString());
            bp.internalEnemies.Add(inn.ToString());
            bp.physicalMechanics.Add("boss_phase");
            bp.physicalMechanics.Add("explore_branching");
            bp.mentalMechanics.Add(ChapterModuleLibrary.MentalMechanicFor(arc.axis));
            return bp;
        }

        public static LegacyArc ByZone(int zoneIndex)
        {
            foreach (var a in Arcs) if (a.zoneIndex == zoneIndex) return a;
            return null;
        }

        public static string ZoneName(int zoneIndex) => ZoneBuilder.ZoneNameOf(zoneIndex);
    }
}
