using System;
using System.Collections.Generic;
using UnityEngine;

namespace AdversityRoad.Goals
{
    [Serializable]
    public class GoalJourneyNode
    {
        public string nodeId;
        public GoalNodeType type = GoalNodeType.Action;
        public string title;
        public string detail = "";
        public GoalNodeState state = GoalNodeState.Locked;

        public string linkedMilestoneId = "";
        public string linkedChapterId = "";
        public string linkedObstacleId = "";
        public string linkedActionId = "";
        public string linkedCapabilityId = "";

        /// <summary>旅程图布局坐标（列 = 阶段，行 = 并行路线）。</summary>
        public int column;
        public int row;

        /// <summary>该节点是否为目标主干（用于 HUD 目标相关性提示与机会成本）。</summary>
        public bool mainLine = true;

        public string TypeLabel()
        {
            switch (type)
            {
                case GoalNodeType.Start: return "起点";
                case GoalNodeType.Milestone: return "里程碑";
                case GoalNodeType.Action: return "关键行动";
                case GoalNodeType.Obstacle: return "障碍";
                case GoalNodeType.Encounter: return "遭遇";
                case GoalNodeType.Resource: return "资源";
                case GoalNodeType.Training: return "训练";
                case GoalNodeType.Decision: return "取舍";
                case GoalNodeType.Recovery: return "恢复";
                default: return "终局";
            }
        }
    }

    [Serializable]
    public class GoalJourneyEdge
    {
        public string fromId;
        public string toId;
        public GoalEdgeKind kind = GoalEdgeKind.Direct;
    }

    /// <summary>四维进度（方案 4.3）：拒绝"完成了 63%"的单一百分比幻觉。</summary>
    public struct JourneyProgress
    {
        public int milestonesDone, milestonesTotal;
        public int actionsDone, actionsTotal;
        public int obstaclesRemoved, obstaclesTotal;
        public float capabilityReadiness;      // 0-1

        public float MilestoneRatio => milestonesTotal > 0 ? (float)milestonesDone / milestonesTotal : 0f;
        public float ActionRatio => actionsTotal > 0 ? (float)actionsDone / actionsTotal : 0f;
        public float ObstacleRatio => obstaclesTotal > 0 ? (float)obstaclesRemoved / obstaclesTotal : 0f;
    }

    /// <summary>
    /// Goal Journey Graph（方案第 4 章）：目标旅程用有向图而不是单一路线表达。
    /// 「自由」不是没有方向，而是同一目标允许多条道路——直接挑战、先训练、
    /// 找资源、绕开低价值敌人、失败后恢复、或者改变阶段方案。
    /// </summary>
    public static class GoalJourneyGraph
    {
        /// <summary>
        /// 从 Goal Model 生成旅程图：
        /// 起点 → [每个里程碑：关键行动 → 障碍/遭遇 → 里程碑]（并行挂训练/资源/恢复/取舍支线）→ 终局。
        /// </summary>
        public static void Build(GoalData goal)
        {
            if (goal == null) return;
            goal.nodes.Clear();
            goal.edges.Clear();

            var start = Add(goal, GoalNodeType.Start, "start", "出发：" + goal.title, 0, 0);
            start.state = GoalNodeState.Done;
            start.detail = string.IsNullOrEmpty(goal.currentState) ? "从现在的位置开始。" : goal.currentState;

            string prevId = start.nodeId;
            int col = 1;

            for (int i = 0; i < goal.milestones.Count; i++)
            {
                var ms = goal.milestones[i];

                // —— 主干：该阶段的关键行动 ——
                var actions = ActionsOf(goal, ms.milestoneId);
                string stageEntry = prevId;
                if (actions.Count > 0)
                {
                    for (int a = 0; a < actions.Count; a++)
                    {
                        var an = Add(goal, GoalNodeType.Action, "act_" + actions[a].actionId,
                            actions[a].label, col, a);
                        an.linkedMilestoneId = ms.milestoneId;
                        an.linkedActionId = actions[a].actionId;
                        an.detail = actions[a].realWorld
                            ? "现实行动：完成后回到目标板打卡。"
                            : "游戏内关键行动：由旅程中的遭遇或交互推进。";
                        Link(goal, stageEntry, an.nodeId, GoalEdgeKind.Direct);
                        if (a == 0) prevId = an.nodeId;
                        else Link(goal, an.nodeId, prevId, GoalEdgeKind.Alternative);
                    }
                    col++;
                }

                // —— 障碍（含其对应章节）：阻挡这个里程碑的东西 ——
                var obstacles = ObstaclesOf(goal, ms.milestoneId);
                for (int b = 0; b < obstacles.Count; b++)
                {
                    var ob = obstacles[b];
                    var on = Add(goal, GoalNodeType.Obstacle, "obs_" + ob.obstacleId, ob.label, col, b);
                    on.linkedMilestoneId = ms.milestoneId;
                    on.linkedObstacleId = ob.obstacleId;
                    on.linkedChapterId = ChapterOfObstacle(goal, ob.obstacleId);
                    on.detail = "阻挡【" + ms.title + "】：" + SourceLabel(ob.source);
                    Link(goal, prevId, on.nodeId, b == 0 ? GoalEdgeKind.Direct : GoalEdgeKind.Alternative);

                    // 每个障碍都配一条训练替代路线与一条失败恢复路线（方案 4.1 边类型）
                    var tr = Add(goal, GoalNodeType.Training, "trn_" + ob.obstacleId,
                        "训练：" + CapabilityHintFor(ob), col, b + 10);
                    tr.mainLine = false;
                    tr.linkedMilestoneId = ms.milestoneId;
                    tr.detail = "先练再打：把这条障碍需要的能力提上来，再回主干。";
                    Link(goal, prevId, tr.nodeId, GoalEdgeKind.Alternative);
                    Link(goal, tr.nodeId, on.nodeId, GoalEdgeKind.Alternative);

                    var rc = Add(goal, GoalNodeType.Recovery, "rec_" + ob.obstacleId,
                        "恢复：" + ob.label + " 之后", col, b + 20);
                    rc.mainLine = false;
                    rc.detail = "失败不是终点——补给、复盘、换策略，再来一次。";
                    Link(goal, on.nodeId, rc.nodeId, GoalEdgeKind.RecoveryPath);
                    Link(goal, rc.nodeId, on.nodeId, GoalEdgeKind.RecoveryPath);

                    if (b == 0) prevId = on.nodeId;
                }
                if (obstacles.Count > 0) col++;

                // —— 取舍节点：这一阶段值不值得现在打 ——
                var dec = Add(goal, GoalNodeType.Decision, "dec_" + ms.milestoneId,
                    "取舍：" + ms.title, col, 5);
                dec.mainLine = false;
                dec.linkedMilestoneId = ms.milestoneId;
                dec.detail = "低价值战斗也给经验，但会花掉时间、体力与机会。";
                Link(goal, prevId, dec.nodeId, GoalEdgeKind.Alternative);

                // —— 资源支线 ——
                var res = Add(goal, GoalNodeType.Resource, "res_" + ms.milestoneId,
                    "资源：" + ms.title, col, 6);
                res.mainLine = false;
                res.detail = "时间、金钱、情报、盟友、装备——绕一下路能让主干好走。";
                Link(goal, dec.nodeId, res.nodeId, GoalEdgeKind.HiddenOpportunity);

                // —— 里程碑本体 ——
                var mn = Add(goal, GoalNodeType.Milestone, "ms_" + ms.milestoneId, ms.title, col, 0);
                mn.linkedMilestoneId = ms.milestoneId;
                mn.detail = string.IsNullOrEmpty(ms.successHint) ? "阶段性到达。" : ms.successHint;
                Link(goal, prevId, mn.nodeId, GoalEdgeKind.Direct);
                Link(goal, res.nodeId, mn.nodeId, GoalEdgeKind.Alternative);
                prevId = mn.nodeId;
                col++;
            }

            // —— 终局 ——
            var fg = Add(goal, GoalNodeType.FinalGate, "final",
                string.IsNullOrEmpty(goal.successCondition) ? "目标终局" : goal.successCondition, col, 0);
            fg.detail = "最终胜利必须与你自己写下的完成条件绑定，而不是统一只打旧我。";
            Link(goal, prevId, fg.nodeId, GoalEdgeKind.Direct);
            goal.finalGateId = fg.nodeId;

            Refresh(goal);
        }

        /// <summary>按里程碑/障碍/行动的完成情况刷新节点状态（可用/进行中/已完成）。</summary>
        public static void Refresh(GoalData goal)
        {
            if (goal == null) return;
            var current = goal.CurrentMilestone();
            string curId = current != null ? current.milestoneId : "";

            foreach (var n in goal.nodes)
            {
                switch (n.type)
                {
                    case GoalNodeType.Start:
                        n.state = GoalNodeState.Done;
                        break;
                    case GoalNodeType.Milestone:
                    {
                        var m = goal.FindMilestone(n.linkedMilestoneId);
                        n.state = m == null ? GoalNodeState.Locked
                            : m.done ? GoalNodeState.Done
                            : m.milestoneId == curId ? GoalNodeState.Active
                            : GoalNodeState.Locked;
                        break;
                    }
                    case GoalNodeType.Obstacle:
                    {
                        var o = goal.FindObstacle(n.linkedObstacleId);
                        n.state = o == null ? GoalNodeState.Locked
                            : o.removed ? GoalNodeState.Done
                            : n.linkedMilestoneId == curId ? GoalNodeState.Available
                            : GoalNodeState.Locked;
                        break;
                    }
                    case GoalNodeType.Action:
                    {
                        var a = FindAction(goal, n.linkedActionId);
                        n.state = a == null ? GoalNodeState.Locked
                            : a.done ? GoalNodeState.Done
                            : n.linkedMilestoneId == curId ? GoalNodeState.Available
                            : GoalNodeState.Locked;
                        break;
                    }
                    case GoalNodeType.FinalGate:
                        n.state = goal.completed ? GoalNodeState.Done
                            : current == null ? GoalNodeState.Available
                            : GoalNodeState.Locked;
                        break;
                    default:
                        // 支线（训练/资源/取舍/恢复）在当前阶段永远可走——这就是开放世界的自由
                        n.state = string.IsNullOrEmpty(n.linkedMilestoneId) || n.linkedMilestoneId == curId
                            ? GoalNodeState.Available : GoalNodeState.Locked;
                        break;
                }
            }
        }

        public static JourneyProgress Progress(GoalData goal)
        {
            var p = new JourneyProgress();
            if (goal == null) return p;

            p.milestonesTotal = goal.milestones.Count;
            foreach (var m in goal.milestones) if (m.done) p.milestonesDone++;

            p.actionsTotal = goal.criticalActions.Count;
            foreach (var a in goal.criticalActions) if (a.done) p.actionsDone++;

            p.obstaclesTotal = goal.obstacles.Count;
            foreach (var o in goal.obstacles) if (o.removed) p.obstaclesRemoved++;

            float sum = 0f;
            foreach (var c in goal.capabilities) sum += Mathf.Clamp01(c.readiness);
            p.capabilityReadiness = goal.capabilities.Count > 0 ? sum / goal.capabilities.Count : 0f;
            return p;
        }

        /// <summary>四维进度文案（方案 4.3：只有目标天然量化时才额外显示百分比）。</summary>
        public static string ProgressLine(GoalData goal)
        {
            var p = Progress(goal);
            return "里程碑 " + p.milestonesDone + "/" + p.milestonesTotal +
                " · 关键行动 " + p.actionsDone + "/" + p.actionsTotal +
                " · 已消除障碍 " + p.obstaclesRemoved + "/" + p.obstaclesTotal +
                " · 能力准备 " + Mathf.RoundToInt(p.capabilityReadiness * 100f) + "%";
        }

        /// <summary>当前应该去的主干节点（Goal Beacon 指向它）。</summary>
        public static GoalJourneyNode CurrentTarget(GoalData goal)
        {
            if (goal == null) return null;
            foreach (var n in goal.nodes)
                if (n.mainLine && n.type == GoalNodeType.Obstacle && n.state == GoalNodeState.Available)
                    return n;
            foreach (var n in goal.nodes)
                if (n.mainLine && n.type == GoalNodeType.Action && n.state == GoalNodeState.Available)
                    return n;
            foreach (var n in goal.nodes)
                if (n.type == GoalNodeType.Milestone && n.state == GoalNodeState.Active)
                    return n;
            foreach (var n in goal.nodes)
                if (n.type == GoalNodeType.FinalGate && n.state == GoalNodeState.Available)
                    return n;
            return null;
        }

        public static List<GoalJourneyNode> Outgoing(GoalData goal, string nodeId)
        {
            var list = new List<GoalJourneyNode>();
            if (goal == null) return list;
            foreach (var e in goal.edges)
                if (e.fromId == nodeId)
                    foreach (var n in goal.nodes)
                        if (n.nodeId == e.toId) { list.Add(n); break; }
            return list;
        }

        // ================= 内部 =================

        static GoalJourneyNode Add(GoalData goal, GoalNodeType type, string id,
            string title, int column, int row)
        {
            var n = new GoalJourneyNode
            {
                nodeId = id,
                type = type,
                title = title,
                column = column,
                row = row,
                mainLine = row == 0 || type == GoalNodeType.Obstacle || type == GoalNodeType.Action
            };
            goal.nodes.Add(n);
            return n;
        }

        static void Link(GoalData goal, string from, string to, GoalEdgeKind kind)
        {
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || from == to) return;
            goal.edges.Add(new GoalJourneyEdge { fromId = from, toId = to, kind = kind });
        }

        static List<GoalCriticalAction> ActionsOf(GoalData goal, string milestoneId)
        {
            var list = new List<GoalCriticalAction>();
            foreach (var a in goal.criticalActions)
                if (a.linkedMilestoneId == milestoneId) list.Add(a);
            return list;
        }

        static List<GoalObstacle> ObstaclesOf(GoalData goal, string milestoneId)
        {
            var list = new List<GoalObstacle>();
            foreach (var o in goal.obstacles)
                if (o.linkedMilestoneId == milestoneId) list.Add(o);
            return list;
        }

        static GoalCriticalAction FindAction(GoalData goal, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var a in goal.criticalActions) if (a.actionId == id) return a;
            return null;
        }

        /// <summary>
        /// 这条障碍该去打哪一关。
        ///
        /// AI 专属章节优先——它是**为这条障碍现造的**，场景、规则、台词都对着它写。
        /// 经典 24 关只有在没有专属章节时才顶上，属于附加可选项：
        /// 拿一个通用关卡去代表玩家自己写的障碍，正是 V2.0 要摆脱的"心理标签推荐"。
        /// </summary>
        static string ChapterOfObstacle(GoalData goal, string obstacleId)
        {
            foreach (var c in goal.chapters)
                if (c.source == ChapterSource.AIRequired &&
                    (c.primaryObstacle == obstacleId || c.secondaryObstacle == obstacleId))
                    return c.chapterId;
            foreach (var c in goal.chapters)
                if (c.source == ChapterSource.UserPreset &&
                    (c.primaryObstacle == obstacleId || c.secondaryObstacle == obstacleId))
                    return c.chapterId;
            foreach (var c in goal.chapters)
                if (c.primaryObstacle == obstacleId || c.secondaryObstacle == obstacleId)
                    return c.chapterId;
            return "";
        }

        static string SourceLabel(ObstacleSource s)
        {
            switch (s)
            {
                case ObstacleSource.AIPredicted: return "AI 预测的障碍";
                case ObstacleSource.UserPreset: return "你自己预设的障碍";
                default: return "你已经知道的障碍";
            }
        }

        static string CapabilityHintFor(GoalObstacle ob)
        {
            switch (ob.axis)
            {
                case Personalization.WeaknessAxis.BoundaryConflict: return "边界与拒绝";
                case Personalization.WeaknessAxis.NoiseSensitivity: return "注意力回收";
                case Personalization.WeaknessAxis.FairnessSensitivity: return "事实与成本判断";
                case Personalization.WeaknessAxis.Shame: return "自尊锚点";
                case Personalization.WeaknessAxis.FailureFear: return "错误恢复";
                case Personalization.WeaknessAxis.SelfDoubt: return "持续行动";
                case Personalization.WeaknessAxis.LowConfidence: return "范围管理";
                case Personalization.WeaknessAxis.JobAnxiety: return "反馈获取";
                default: return "起步与持续行动";
            }
        }
    }
}
