using System;
using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.Personalization;

namespace AdversityRoad.Core
{
    /// <summary>技能树节点：五条成长路线（边界/专注/行动/公平/自尊）× 每线两级。</summary>
    [Serializable]
    public struct GrowthNode
    {
        public string id;
        public string route;   // 路线名（边界/专注/行动/公平/自尊）
        public string name;
        public string desc;
    }

    /// <summary>装备套装：一次只能穿一套，提供一组被动（对应方案五大套装）。</summary>
    [Serializable]
    public struct EquipmentSet
    {
        public string id;
        public string name;
        public string mantra;  // 核心台词
        public string desc;
    }

    /// <summary>一条复盘档案：四栏（事实/感受/边界/行动）+ 章节与时间。旧事不再无限回放，而是入档。</summary>
    [Serializable]
    public class ReflectionEntry
    {
        public string chapterTitle;
        public string fact, feeling, boundary, action;
        public string savedAt;
    }

    [Serializable]
    class ReflectionLogData { public List<ReflectionEntry> entries = new List<ReflectionEntry>(); }

    /// <summary>
    /// 成长系统（安全屋核心数据层）：
    /// - 复盘点：归档战斗复盘获得的成长货币；
    /// - 技能树：五条路线的被动节点，用复盘点解锁；
    /// - 装备套装：五大套装被动，一次穿一套；
    /// - 敌人图鉴：击败计数；
    /// - 旧事档案：复盘历史。
    /// 全部本地持久化（PlayerPrefs / 本地 JSON），可随存档删除。
    /// </summary>
    public static class GrowthSystem
    {
        const string PointsKey = "adversity_growth_points";
        const string NodesKey = "adversity_growth_nodes";
        const string SetKey = "adversity_equipment_set";
        const string KillKeyPrefix = "adversity_kill_";
        const string ReflectionKey = "adversity_reflection_log";

        public static readonly GrowthNode[] Nodes =
        {
            new GrowthNode { id = "boundary1", route = "边界", name = "边界加固",
                desc = "边界上限 +25：索取与责任转嫁更难击穿你。" },
            new GrowthNode { id = "boundary2", route = "边界", name = "明确拒绝",
                desc = "关系消耗积累 -40%：说清楚之后，消耗就少了。" },
            new GrowthNode { id = "focus1", route = "专注", name = "专注扩容",
                desc = "专注上限 +25：更大的注意力容器。" },
            new GrowthNode { id = "focus2", route = "专注", name = "声音过滤",
                desc = "噪声/凝视类心理伤害 -25%：听见了，但不跟随。" },
            new GrowthNode { id = "action1", route = "行动", name = "起步惯性",
                desc = "拖延类行动力流失 -30%：开始过一次，第二次更容易。" },
            new GrowthNode { id = "action2", route = "行动", name = "火种常燃",
                desc = "五分钟火种冷却 -40%：随时可以再点一次。" },
            new GrowthNode { id = "fact1", route = "公平", name = "事实之刃锋利",
                desc = "物理输出 +6%：先说清事实，出手更有力。" },
            new GrowthNode { id = "fact2", route = "公平", name = "成本判断",
                desc = "公平刺痛类心理伤害 -25%：记住事实，不被事实伤口困住。" },
            new GrowthNode { id = "self1", route = "自尊", name = "自尊锚点",
                desc = "自尊上限 +25：被看见不等于被否定。" },
            new GrowthNode { id = "self2", route = "自尊", name = "旧事止响",
                desc = "反刍积累 -25%：刺痛照旧发生，回放明显变短。" },
        };

        public static readonly EquipmentSet[] Sets =
        {
            new EquipmentSet { id = "set_boundary", name = "边界守卫套",
                mantra = "我不是你的钱包，也不是你的替身人生。",
                desc = "索取/责任转嫁类心理伤害 -30%；关系消耗积累 -50%。" },
            new EquipmentSet { id = "set_focus", name = "专注夺回套",
                mantra = "我听见了，但我不跟随。",
                desc = "咳嗽/眼神/低语类心理伤害 -30%；反刍积累 -15%。" },
            new EquipmentSet { id = "set_fact", name = "公平复盘套",
                mantra = "我记住事实，但不被事实伤口困住。",
                desc = "公平刺痛类心理伤害 -30%；物理输出 +5%。" },
            new EquipmentSet { id = "set_action", name = "行动起步套",
                mantra = "不等动力，先开始。",
                desc = "拖延类行动力流失 -40%；五分钟火种冷却 -30%。" },
            new EquipmentSet { id = "set_oldself", name = "旧我整合套",
                mantra = "你曾经保护过我，但现在我要继续向前。",
                desc = "旧事回声类心理伤害 -30%；反刍积累 -30%。" },
        };

        static HashSet<string> _unlocked;
        static ReflectionLogData _log;

        // ================= 复盘点 =================

        public static int Points
        {
            get => PlayerPrefs.GetInt(PointsKey, 0);
            private set { PlayerPrefs.SetInt(PointsKey, value); PlayerPrefs.Save(); }
        }

        public static void AddPoints(int n)
        {
            Points = Points + n;
            GameEvents.RaiseSubtitle("获得复盘点 +" + n + "（安全屋「成长」面板可解锁技能树节点）");
        }

        // ================= 技能树 =================

        static HashSet<string> Unlocked()
        {
            if (_unlocked == null)
                _unlocked = new HashSet<string>(
                    PlayerPrefs.GetString(NodesKey, "").Split(
                        new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            return _unlocked;
        }

        public static bool IsUnlocked(string nodeId) => Unlocked().Contains(nodeId);

        public static bool TryUnlock(string nodeId)
        {
            if (IsUnlocked(nodeId) || Points < 1) return false;
            Points = Points - 1;
            Unlocked().Add(nodeId);
            PlayerPrefs.SetString(NodesKey, string.Join(",", Unlocked()));
            PlayerPrefs.Save();
            return true;
        }

        // ================= 装备套装 =================

        public static string EquippedSet
        {
            get => PlayerPrefs.GetString(SetKey, "");
            set { PlayerPrefs.SetString(SetKey, value ?? ""); PlayerPrefs.Save(); }
        }

        // ================= 被动效果查询（战斗系统调用） =================

        /// <summary>某弱点轴心理伤害的承受倍率（技能树 + 套装叠乘）。</summary>
        public static float MentalTakenMult(WeaknessAxis axis)
        {
            float m = 1f;
            bool noiseAxis = axis == WeaknessAxis.NoiseSensitivity;
            bool fairAxis = axis == WeaknessAxis.FairnessSensitivity;
            bool boundAxis = axis == WeaknessAxis.BoundaryConflict;
            bool procAxis = axis == WeaknessAxis.Procrastination;
            bool oldAxis = axis == WeaknessAxis.FailureFear;
            if (noiseAxis && IsUnlocked("focus2")) m *= 0.75f;
            if (fairAxis && IsUnlocked("fact2")) m *= 0.75f;
            if (procAxis && IsUnlocked("action1")) m *= 0.7f;
            switch (EquippedSet)
            {
                case "set_boundary": if (boundAxis || fairAxis) m *= 0.7f; break;
                case "set_focus": if (noiseAxis) m *= 0.7f; break;
                case "set_fact": if (fairAxis) m *= 0.7f; break;
                case "set_action": if (procAxis) m *= 0.6f; break;
                case "set_oldself": if (oldAxis) m *= 0.7f; break;
            }
            return m;
        }

        public static float RuminationGainMult()
        {
            float m = 1f;
            if (IsUnlocked("self2")) m *= 0.75f;
            if (EquippedSet == "set_oldself") m *= 0.7f;
            else if (EquippedSet == "set_focus") m *= 0.85f;
            return m;
        }

        public static float DrainGainMult()
        {
            float m = 1f;
            if (IsUnlocked("boundary2")) m *= 0.6f;
            if (EquippedSet == "set_boundary") m *= 0.5f;
            return m;
        }

        public static float PhysicalOutMult()
        {
            float m = 1f;
            if (IsUnlocked("fact1")) m *= 1.06f;
            if (EquippedSet == "set_fact") m *= 1.05f;
            return m;
        }

        /// <summary>技能冷却倍率（火种类冷却缩减节点/套装）。</summary>
        public static float CooldownMult(Data.SkillDefinition skill)
        {
            float m = 1f;
            if (skill != null && skill.isFiveMinuteSpark)
            {
                if (IsUnlocked("action2")) m *= 0.6f;
                if (EquippedSet == "set_action") m *= 0.7f;
            }
            return m;
        }

        /// <summary>把技能树的上限加成落到属性（玩家生成时与节点解锁时调用）。</summary>
        public static void ApplyMaxBonuses(Player.PlayerStats stats)
        {
            if (stats == null) return;
            float b = 100f + (IsUnlocked("boundary1") ? 25f : 0f);
            float f = 100f + (IsUnlocked("focus1") ? 25f : 0f);
            float s = 100f + (IsUnlocked("self1") ? 25f : 0f);
            // 上限提高时按比例带高当前值（升级立刻有获得感）
            stats.boundary = stats.boundary / stats.maxBoundary * b;
            stats.maxBoundary = b;
            stats.focus = stats.focus / stats.maxFocus * f;
            stats.maxFocus = f;
            stats.selfWorth = stats.selfWorth / stats.maxSelfWorth * s;
            stats.maxSelfWorth = s;
            GameEvents.RaiseMentalStatChanged("boundary", stats.boundary, stats.maxBoundary);
            GameEvents.RaiseMentalStatChanged("focus", stats.focus, stats.maxFocus);
            GameEvents.RaiseMentalStatChanged("selfWorth", stats.selfWorth, stats.maxSelfWorth);
        }

        // ================= 敌人图鉴（击败计数） =================

        static bool _killHooked;

        /// <summary>挂上击败计数钩子（静态方法+防重入，场景重载不重复订阅）。</summary>
        public static void EnsureKillHook()
        {
            if (_killHooked) return;
            _killHooked = true;
            GameEvents.OnEnemyKilled += RecordKill;
        }

        /// <summary>记录一次击败：玩家自由添加的敌人 id 带 "_extra_N" 后缀，归并到基础 id。</summary>
        public static void RecordKill(string enemyId)
        {
            if (string.IsNullOrEmpty(enemyId)) return;
            int idx = enemyId.IndexOf("_extra_", StringComparison.Ordinal);
            string baseId = idx > 0 ? enemyId.Substring(0, idx) : enemyId;
            string key = KillKeyPrefix + baseId;
            PlayerPrefs.SetInt(key, PlayerPrefs.GetInt(key, 0) + 1);
            PlayerPrefs.Save();
        }

        public static int KillCount(string baseId) =>
            PlayerPrefs.GetInt(KillKeyPrefix + baseId, 0);

        // ================= 旧事档案（复盘历史） =================

        static ReflectionLogData Log()
        {
            if (_log == null)
            {
                string json = PlayerPrefs.GetString(ReflectionKey, "");
                _log = string.IsNullOrEmpty(json) ? new ReflectionLogData()
                    : JsonUtility.FromJson<ReflectionLogData>(json) ?? new ReflectionLogData();
            }
            return _log;
        }

        public static void SaveReflection(ReflectionEntry entry)
        {
            entry.savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            var log = Log();
            log.entries.Add(entry);
            // 档案上限：留最近 40 条，太久远的旧事让它过去
            while (log.entries.Count > 40) log.entries.RemoveAt(0);
            PlayerPrefs.SetString(ReflectionKey, JsonUtility.ToJson(log));
            PlayerPrefs.Save();
        }

        public static IReadOnlyList<ReflectionEntry> Reflections() => Log().entries;

        /// <summary>数据删除安全开关：清空全部成长与档案数据。</summary>
        public static void DeleteAll()
        {
            PlayerPrefs.DeleteKey(PointsKey);
            PlayerPrefs.DeleteKey(NodesKey);
            PlayerPrefs.DeleteKey(SetKey);
            PlayerPrefs.DeleteKey(ReflectionKey);
            _unlocked = null;
            _log = null;
            GoalSystem.DeleteAll();
            ActionSystem.DeleteAll();
            FailureLog.DeleteAll();
        }
    }

    /// <summary>
    /// 目标板系统（方案：玩家每次进入游戏都要完成一个明确的小目标 + 现实行动映射）。
    /// 钉下「今日唯一目标」→ HUD 常驻显示 → 完成打卡获得复盘点（每日一次）。
    /// 目标可以是游戏内挑战，也可以是现实行动小任务——由玩家自己写。
    /// </summary>
    public static class GoalSystem
    {
        const string GoalKey = "adversity_today_goal";
        const string DateKey = "adversity_today_goal_date";
        const string DoneKey = "adversity_today_goal_done";

        static string Today => System.DateTime.Now.ToString("yyyy-MM-dd");

        public static string CurrentGoal => PlayerPrefs.GetString(GoalKey, "");

        /// <summary>钉的是不是今天的目标（跨天自动过期，鼓励每天重新定向）。</summary>
        public static bool PinnedToday =>
            PlayerPrefs.GetString(DateKey, "") == Today && CurrentGoal.Length > 0;

        public static bool DoneToday =>
            PinnedToday && PlayerPrefs.GetInt(DoneKey, 0) == 1;

        /// <summary>钉下今日唯一目标（目标钉：一天只钉一个，钉新的覆盖旧的）。</summary>
        public static void Pin(string goal)
        {
            goal = (goal ?? "").Trim();
            if (goal.Length == 0) return;
            if (goal.Length > 60) goal = goal.Substring(0, 60);
            PlayerPrefs.SetString(GoalKey, goal);
            PlayerPrefs.SetString(DateKey, Today);
            PlayerPrefs.SetInt(DoneKey, 0);
            PlayerPrefs.Save();
            GameEvents.RaiseGoalChanged();
            GameEvents.RaiseSubtitle("目标钉下：「" + goal + "」——今天只做这一件事，做五分钟也算开始。");
        }

        /// <summary>完成打卡：复盘点 +1（每日一次），恢复行动力。返回是否成功。</summary>
        public static bool CompleteToday(Player.PlayerStats stats)
        {
            if (!PinnedToday || DoneToday) return false;
            PlayerPrefs.SetInt(DoneKey, 1);
            PlayerPrefs.Save();
            GrowthSystem.AddPoints(1);
            if (stats != null)
            {
                stats.RestoreAxis(Personalization.WeaknessAxis.Procrastination, 40f);
                stats.ReduceRumination(15f);
            }
            GameEvents.RaiseGoalChanged();
            GameEvents.RaiseSubtitle("今日目标完成——行动先于完美状态，你已经赢下今天。");
            return true;
        }

        /// <summary>HUD 常驻行文案（空字符串 = 不显示）。</summary>
        public static string HudLine()
        {
            if (!PinnedToday) return "";
            return (DoneToday ? "✓ 已完成 · " : "◎ 今日目标 · ") + CurrentGoal;
        }

        public static void DeleteAll()
        {
            PlayerPrefs.DeleteKey(GoalKey);
            PlayerPrefs.DeleteKey(DateKey);
            PlayerPrefs.DeleteKey(DoneKey);
        }
    }
}
