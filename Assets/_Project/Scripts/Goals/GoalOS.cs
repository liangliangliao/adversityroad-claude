using System;
using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.Core;

namespace AdversityRoad.Goals
{
    [Serializable]
    class GoalOSSave
    {
        public List<GoalData> goals = new List<GoalData>();
        public string activeGoalId = "";
        public List<GoalMonument> hall = new List<GoalMonument>();
        /// <summary>玩家自己记录的现实行动进度条目（Reality Progress，方案 17.2）。</summary>
        public List<string> realityLog = new List<string>();
    }

    /// <summary>
    /// Goal OS（方案第 3 章）：目标操作系统——V2.0 的最高层主线。
    ///
    /// V1.0 把七大心理章节当主线，容易被理解成"我要完成一套心理训练"。
    /// V2.0 把结构反转：**目标是主线**，拖延/边界/专注/旧我/低谷只有在
    /// 真正阻挡这个目标时才被当作逆境模块调用。
    ///
    /// 本地优先存档（方案 18.4）：目标、经历、画像默认只存在本机，可一键删除。
    /// </summary>
    public static class GoalOS
    {
        const string SaveKey = "adversity_goalos_v2";

        static GoalOSSave _save;

        /// <summary>目标/旅程发生变化（钉下、推进、重铸、完成）。</summary>
        public static event Action Changed;

        static GoalOSSave S()
        {
            if (_save != null) return _save;
            string json = PlayerPrefs.GetString(SaveKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                try { _save = JsonUtility.FromJson<GoalOSSave>(json); }
                catch { _save = null; }
            }
            if (_save == null) _save = new GoalOSSave();
            return _save;
        }

        static void Persist()
        {
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(S()));
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>外部改完 GoalData 内容后调用（保存 + 广播）。</summary>
        public static void Save() => Persist();

        // ================= 目标 =================

        public static GoalData Active
        {
            get
            {
                var s = S();
                if (string.IsNullOrEmpty(s.activeGoalId)) return null;
                foreach (var g in s.goals) if (g.goalId == s.activeGoalId) return g;
                return null;
            }
        }

        public static bool HasActiveGoal => Active != null && !Active.completed;

        public static List<GoalData> AllGoals => S().goals;

        public static List<GoalMonument> HallOfGoals => S().hall;

        public static List<string> RealityLog => S().realityLog;

        public static GoalData FindGoal(string goalId)
        {
            foreach (var g in S().goals) if (g.goalId == goalId) return g;
            return null;
        }

        /// <summary>创建并激活一条新的目标旅程（不含 AI 章节；AI 章节由 GoalChapterGenerator 补齐）。</summary>
        public static GoalData CreateGoal(string freeText, GoalTemplate template = null)
        {
            var goal = GoalParser.Parse(freeText, template);
            var s = S();
            s.goals.Add(goal);
            s.activeGoalId = goal.goalId;
            Persist();
            GameEvents.RaiseSubtitle("目标已录入：「" + goal.title +
                "」——开放世界正在按它重排道路。");
            return goal;
        }

        public static void SetActive(string goalId)
        {
            var s = S();
            s.activeGoalId = goalId ?? "";
            Persist();
        }

        /// <summary>删除一条目标（数据删除开关的一部分）。</summary>
        public static void DeleteGoal(string goalId)
        {
            var s = S();
            for (int i = s.goals.Count - 1; i >= 0; i--)
                if (s.goals[i].goalId == goalId) s.goals.RemoveAt(i);
            if (s.activeGoalId == goalId) s.activeGoalId = s.goals.Count > 0 ? s.goals[0].goalId : "";
            Persist();
        }

        // ================= 旅程推进 =================

        /// <summary>完成一个关键行动（游戏内由遭遇/交互触发；现实行动由玩家打卡）。</summary>
        public static bool CompleteAction(string actionId)
        {
            var g = Active;
            if (g == null) return false;
            foreach (var a in g.criticalActions)
                if (a.actionId == actionId && !a.done)
                {
                    a.done = true;
                    a.doneAtUtc = DateTime.UtcNow.ToString("o");
                    GameEvents.RaiseSubtitle("关键行动完成：" + a.label);
                    AfterProgress(g);
                    return true;
                }
            return false;
        }

        /// <summary>消除一个障碍（通常由对应章节通关触发）。</summary>
        public static bool RemoveObstacle(string obstacleId)
        {
            var g = Active;
            if (g == null) return false;
            var o = g.FindObstacle(obstacleId);
            if (o == null || o.removed) return false;
            o.removed = true;
            o.removedAtUtc = DateTime.UtcNow.ToString("o");
            GameEvents.RaiseSubtitle("障碍已消除：" + o.label + "——它不再统治这条路。");
            AfterProgress(g);
            return true;
        }

        /// <summary>能力成长（训练节点 / 战斗中的高质量行为推进）。</summary>
        public static void AdvanceCapability(string capabilityLabel, float delta)
        {
            var g = Active;
            if (g == null) return;
            foreach (var c in g.capabilities)
                if (c.label == capabilityLabel)
                {
                    c.readiness = Mathf.Clamp01(c.readiness + delta);
                    Persist();
                    return;
                }
        }

        /// <summary>章节通关：消除它挂着的障碍、推进该阶段的游戏内关键行动，并检查里程碑。</summary>
        public static void ChapterCleared(string chapterId)
        {
            var g = Active;
            if (g == null) return;
            var ch = g.FindChapter(chapterId);
            if (ch == null || ch.cleared) return;
            ch.cleared = true;
            ch.clearedAtUtc = DateTime.UtcNow.ToString("o");

            // 打赢一关就是"这个阶段的关键行动做到了一次"——游戏内行动由世界推进。
            // 章节挂的里程碑已经翻页时，退而推进任意一条待办的游戏内行动，
            // 免得某个既没章节也没障碍的阶段把整条旅程卡死。
            if (!CompleteNextAction(g, ch.linkedMilestoneId, false))
                CompleteNextAction(g, "", false);

            if (!string.IsNullOrEmpty(ch.primaryObstacle)) RemoveObstacle(ch.primaryObstacle);
            else AfterProgress(g);
        }

        /// <summary>
        /// 推进某个里程碑名下的下一条未完成关键行动。
        /// realWorld=true 只推进"现实行动"（必须由玩家自己打卡，游戏不替他做）；
        /// false 只推进游戏内行动。找不到就返回 false。
        /// </summary>
        static bool CompleteNextAction(GoalData g, string milestoneId, bool realWorld)
        {
            foreach (var a in g.criticalActions)
            {
                if (a.done || a.realWorld != realWorld) continue;
                if (!string.IsNullOrEmpty(milestoneId) && a.linkedMilestoneId != milestoneId) continue;
                a.done = true;
                a.doneAtUtc = DateTime.UtcNow.ToString("o");
                GameEvents.RaiseSubtitle("关键行动完成：" + a.label);
                return true;
            }
            return false;
        }

        public static void NoteChapterAttempt(string chapterId)
        {
            var g = Active;
            var ch = g != null ? g.FindChapter(chapterId) : null;
            if (ch == null) return;
            ch.attempts++;
            Persist();
        }

        /// <summary>推进后统一处理：里程碑达成判定 → 旅程刷新 → 终局判定。</summary>
        static void AfterProgress(GoalData g)
        {
            var cur = g.CurrentMilestone();
            if (cur != null && MilestoneSatisfied(g, cur))
            {
                cur.done = true;
                cur.doneAtUtc = DateTime.UtcNow.ToString("o");
                GameEvents.RaiseSubtitle("里程碑达成：【" + cur.title + "】——" +
                    GoalJourneyGraph.ProgressLine(g));
            }
            GoalJourneyGraph.Refresh(g);
            Persist();
        }

        /// <summary>里程碑达成条件：它名下的关键行动与障碍都已处理完。</summary>
        static bool MilestoneSatisfied(GoalData g, GoalMilestone ms)
        {
            foreach (var a in g.criticalActions)
                if (a.linkedMilestoneId == ms.milestoneId && !a.done) return false;
            foreach (var o in g.obstacles)
                if (o.linkedMilestoneId == ms.milestoneId && !o.removed) return false;
            return true;
        }

        /// <summary>终局是否可以开启（所有里程碑完成）。</summary>
        public static bool FinalGateOpen()
        {
            var g = Active;
            return g != null && !g.completed && g.CurrentMilestone() == null;
        }

        /// <summary>目标达成：写入 Hall of Goals 纪念碑（方案 26.2）。</summary>
        public static void CompleteGoal(string finalVictoryNote, int failureCount,
            int nemesisCount, string biggestComeback, string keyStrategyShift)
        {
            var g = Active;
            if (g == null || g.completed) return;
            g.completed = true;
            g.completedAtUtc = DateTime.UtcNow.ToString("o");
            GoalJourneyGraph.Refresh(g);

            S().hall.Add(new GoalMonument
            {
                goalId = g.goalId,
                title = g.title,
                why = g.why,
                startedAtUtc = g.createdAtUtc,
                finishedAtUtc = g.completedAtUtc,
                milestoneCount = g.milestones.Count,
                failureCount = failureCount,
                nemesisCount = nemesisCount,
                biggestComeback = biggestComeback,
                keyStrategyShift = keyStrategyShift,
                finalVictoryNote = finalVictoryNote,
                evolutionCount = g.evolution.Count
            });
            Persist();
            GameEvents.RaiseSubtitle("【目标达成】" + g.title + " —— 它已经立在你的 Hall of Goals 里。");
        }

        // ================= 目标重铸（方案 3.6） =================

        /// <summary>
        /// Reforge Goal：意志不等于盲目坚持。
        /// 保留原目标、改变原因、新目标与中间证据；重铸不判失败——
        /// 如果改变来自更好的判断，它本身就是一种高级成长。
        /// </summary>
        public static void ReforgeGoal(string newTitle, string reason)
        {
            var g = Active;
            if (g == null) return;
            newTitle = (newTitle ?? "").Trim();
            if (newTitle.Length == 0) return;
            if (GoalParser.IsUnsafe(newTitle, out string bad))
            {
                GameEvents.RaiseSubtitle("这个新方向包含「" + bad + "」，不能作为旅程目标——换一个方向再来。");
                return;
            }

            g.evolution.Add(new GoalEvolutionEntry
            {
                atUtc = DateTime.UtcNow.ToString("o"),
                oldTitle = g.title,
                newTitle = newTitle,
                reason = string.IsNullOrEmpty(reason) ? "获得了新的事实" : reason,
                evidence = GoalJourneyGraph.ProgressLine(g)
            });
            g.title = newTitle;
            GoalJourneyGraph.Refresh(g);
            Persist();
            GameEvents.RaiseSubtitle("目标已重铸：「" + newTitle +
                "」——改变方向不是失败，前面走过的路全部记在演化史里。");
        }

        // ================= 现实行动进度（方案 17.2 Reality Progress） =================

        public static void LogRealityProgress(string line)
        {
            line = (line ?? "").Trim();
            if (line.Length == 0) return;
            if (line.Length > 80) line = line.Substring(0, 80);
            var s = S();
            s.realityLog.Insert(0, DateTime.Now.ToString("MM-dd HH:mm") + "  " + line);
            if (s.realityLog.Count > 60) s.realityLog.RemoveRange(60, s.realityLog.Count - 60);

            // 现实行动只能由玩家自己打卡推进——游戏内的进度不能替代它，但会为它让路：
            // 打了卡，这个阶段的现实行动就算做到了，里程碑才可能真正翻页。
            var g = Active;
            if (g != null && !g.completed)
            {
                var cur = g.CurrentMilestone();
                if (CompleteNextAction(g, cur != null ? cur.milestoneId : "", true) ||
                    CompleteNextAction(g, "", true))
                {
                    AfterProgress(g);
                    return;
                }
            }
            Persist();
        }

        // ================= HUD / 提示 =================

        /// <summary>HUD 常驻目标行（目标前台化：任何时候都知道当前主线指向哪里）。</summary>
        public static string HudLine()
        {
            var g = Active;
            if (g == null) return "";
            if (g.completed) return "✦ 目标已达成 · " + g.title;
            var node = GoalJourneyGraph.CurrentTarget(g);
            var ms = g.CurrentMilestone();
            string where = node != null ? node.title : (ms != null ? ms.title : "终局");
            return "◈ " + g.title + " · 当前：" + where;
        }

        public static void DeleteAll()
        {
            PlayerPrefs.DeleteKey(SaveKey);
            _save = null;
            Changed?.Invoke();
        }
    }
}
