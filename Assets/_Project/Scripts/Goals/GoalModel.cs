using System;
using System.Collections.Generic;
using AdversityRoad.Core;

namespace AdversityRoad.Goals
{
    /// <summary>旅程图节点类型（方案 4.2）。</summary>
    public enum GoalNodeType
    {
        Start, Milestone, Action, Obstacle, Encounter,
        Resource, Training, Decision, Recovery, FinalGate
    }

    public enum GoalNodeState { Locked, Available, Active, Done, Skipped }

    /// <summary>边的类型：直接路线 / 替代路线 / 失败恢复路线 / 隐藏机会（方案 4.1）。</summary>
    public enum GoalEdgeKind { Direct, Alternative, RecoveryPath, HiddenOpportunity }

    /// <summary>章节来源三分类（方案 5.1）。AIRequired 为强制存在项。</summary>
    public enum ChapterSource { Legacy, UserPreset, AIRequired }

    /// <summary>障碍来源：玩家已知 / AI 预测 / 玩家预设关卡（方案 3.3、17.2 障碍图）。</summary>
    public enum ObstacleSource { PlayerKnown, AIPredicted, UserPreset }

    [Serializable]
    public class GoalMilestone
    {
        public string milestoneId;
        public string title;              // 「需求冻结」「核心开发」……
        public string successHint;        // 可验证的"更接近"状态描述
        public bool done;
        public string doneAtUtc = "";
    }

    [Serializable]
    public class GoalObstacle
    {
        public string obstacleId;
        public string label;                          // 「功能膨胀」「反复重做」
        public ObstacleSource source = ObstacleSource.PlayerKnown;
        public Personalization.WeaknessAxis axis = Personalization.WeaknessAxis.Procrastination;
        public string linkedMilestoneId = "";
        public bool removed;                          // 已消除（四维进度之一）
        public string removedAtUtc = "";
    }

    /// <summary>关键行动：必须由玩家完成的行为（Critical Actions 维度）。</summary>
    [Serializable]
    public class GoalCriticalAction
    {
        public string actionId;
        public string label;
        public string linkedMilestoneId = "";
        public bool done;
        /// <summary>是否为现实行动（玩家自己在现实里做，回来打卡）。</summary>
        public bool realWorld;
        public string doneAtUtc = "";
    }

    /// <summary>需要成长的能力（Capability Readiness 维度）。</summary>
    [Serializable]
    public class GoalCapability
    {
        public string capabilityId;
        public string label;              // 「范围管理」「持续行动」「错误恢复」
        public float readiness;           // 0-1，由训练节点与战斗表现推进
    }

    /// <summary>目标重铸历史（方案 3.6）：保留原目标、改变原因、新目标与中间证据。</summary>
    [Serializable]
    public class GoalEvolutionEntry
    {
        public string atUtc;
        public string oldTitle;
        public string newTitle;
        public string reason;
        public string evidence;           // 触发重铸时的旅程证据摘要
    }

    /// <summary>
    /// Goal Model（方案 3.3 / 20.1 GoalData）：目标操作系统的最高层数据契约。
    /// 一切世界内容——章节、敌人、里程碑、Boss、失败与逆袭——都必须能追溯回这里的某个字段。
    /// </summary>
    [Serializable]
    public class GoalData
    {
        public string goalId;
        public string title;                       // 最终目标
        public string why = "";                    // 意义/动机
        public string successCondition = "";       // 可验证完成条件
        public string currentState = "";
        public string targetState = "";

        public List<GoalMilestone> milestones = new List<GoalMilestone>();
        public List<GoalObstacle> obstacles = new List<GoalObstacle>();
        public List<GoalCriticalAction> criticalActions = new List<GoalCriticalAction>();
        public List<GoalCapability> capabilities = new List<GoalCapability>();
        public List<string> riskFactors = new List<string>();

        /// <summary>三类章节 id（方案 5.1）：Legacy 复用 V1 七大章节，AIRequired 强制至少 1 个。</summary>
        public List<string> legacyChapterIds = new List<string>();
        public List<string> userChapterIds = new List<string>();
        public List<string> aiGeneratedChapterIds = new List<string>();

        /// <summary>本目标专属的章节蓝图（自包含存档，便于导出/删除）。</summary>
        public List<GoalChapterData> chapters = new List<GoalChapterData>();

        public List<GoalJourneyNode> nodes = new List<GoalJourneyNode>();
        public List<GoalJourneyEdge> edges = new List<GoalJourneyEdge>();

        public string finalGateId = "";

        // 量化参数（方案 3.2）
        public int deadlineDays;                   // 0 = 不设截止
        public int weeklyHours = 5;
        public int plannedMilestones = 4;

        public RealityCloseness realism = RealityCloseness.SemiClose;   // 现实贴近度
        public MentalIntensity intensity = MentalIntensity.Standard;    // 困难强度 ↔ 逆境预算上限
        public List<string> safetyTags = new List<string>();            // 玩家禁用主题快照

        public List<GoalEvolutionEntry> evolution = new List<GoalEvolutionEntry>();

        public string createdAtUtc = "";
        public bool completed;
        public string completedAtUtc = "";
        /// <summary>正式生成完成：必须存在至少一个通过校验的 AIRequired 章节（验收标准 11/20）。</summary>
        public bool journeyReady;

        public GoalMilestone FindMilestone(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var m in milestones) if (m.milestoneId == id) return m;
            return null;
        }

        public GoalChapterData FindChapter(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var c in chapters) if (c.chapterId == id) return c;
            return null;
        }

        public GoalObstacle FindObstacle(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var o in obstacles) if (o.obstacleId == id) return o;
            return null;
        }

        /// <summary>当前里程碑（第一个未完成的）。全部完成时返回 null（进入终局）。</summary>
        public GoalMilestone CurrentMilestone()
        {
            foreach (var m in milestones) if (!m.done) return m;
            return null;
        }

        public bool HasAiChapter()
        {
            foreach (var c in chapters)
                if (c.source == ChapterSource.AIRequired && c.validated) return true;
            return false;
        }
    }

    /// <summary>完成目标后留在 Hall of Goals 的纪念碑（方案 26.2）。</summary>
    [Serializable]
    public class GoalMonument
    {
        public string goalId;
        public string title;
        public string why;
        public string startedAtUtc;
        public string finishedAtUtc;
        public int milestoneCount;
        public int failureCount;
        public int nemesisCount;
        public string biggestComeback = "";       // 最大逆袭一句话
        public string keyStrategyShift = "";      // 关键策略变化
        public string finalVictoryNote = "";
        public int evolutionCount;
    }
}
