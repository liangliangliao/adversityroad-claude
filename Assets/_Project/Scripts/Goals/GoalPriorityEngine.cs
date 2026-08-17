using UnityEngine;

namespace AdversityRoad.Goals
{
    /// <summary>一次待评估的机会/威胁/事件。</summary>
    public struct PriorityInput
    {
        public string label;
        public float goalRelevance;     // 0-1：它是否直接推进当前里程碑
        public float urgency;           // 0-1
        public float opportunityValue;  // 0-1：资源/情报/盟友
        public float riskOfDelay;       // 0-1：不做的代价
        public float playerCommitment;  // 0-1：玩家已经在这件事上投入多少
        public float cost;              // 0-1：时间/体力/机会成本
    }

    public struct PriorityResult
    {
        public float score;
        public string verdict;          // 一句成本信息（不替玩家决定）
        public bool worthIt;
    }

    /// <summary>
    /// Goal Priority Engine（方案 4.4 / 16.3）：
    /// Priority = 0.35 GoalRelevance + 0.20 Urgency + 0.15 OpportunityValue
    ///          + 0.15 RiskOfDelay + 0.15 PlayerCommitment - CostPenalty
    ///
    /// 它只用于排序提示，绝不自动替玩家行动——主动性是本作的设计支柱之一。
    /// 「不是每个敌人都值得打」是玩家自己学会的，不是系统替他跳过的。
    /// </summary>
    public static class GoalPriorityEngine
    {
        public const float CostWeight = 0.25f;

        public static PriorityResult Evaluate(PriorityInput i)
        {
            float score =
                0.35f * Mathf.Clamp01(i.goalRelevance) +
                0.20f * Mathf.Clamp01(i.urgency) +
                0.15f * Mathf.Clamp01(i.opportunityValue) +
                0.15f * Mathf.Clamp01(i.riskOfDelay) +
                0.15f * Mathf.Clamp01(i.playerCommitment) -
                CostWeight * Mathf.Clamp01(i.cost);

            score = Mathf.Clamp(score, -0.25f, 1f);

            string verdict;
            if (score >= 0.62f) verdict = "直接推进目标——现在做，收益最高。";
            else if (score >= 0.40f) verdict = "有价值但不是主线：会花掉时间与体力，你可以先放着。";
            else if (score >= 0.18f) verdict = "低目标相关性：打赢了也只是经验，成本你自己判断。";
            else verdict = "与当前目标无关：解除锁定离开也是一种胜利（边界胜利）。";

            return new PriorityResult { score = score, verdict = verdict, worthIt = score >= 0.40f };
        }

        /// <summary>为一个遭遇打分：按它是否挂在当前里程碑的旅程节点上判断目标相关性。</summary>
        public static PriorityResult EvaluateEncounter(GoalData goal, string chapterId,
            float urgency, float cost)
        {
            float relevance = 0.1f;
            float commitment = 0.2f;
            if (goal != null && !string.IsNullOrEmpty(chapterId))
            {
                var ch = goal.FindChapter(chapterId);
                if (ch != null)
                {
                    var cur = goal.CurrentMilestone();
                    relevance = cur != null && ch.linkedMilestoneId == cur.milestoneId ? 0.95f : 0.55f;
                    commitment = Mathf.Clamp01(0.2f + ch.attempts * 0.2f);
                }
            }
            return Evaluate(new PriorityInput
            {
                goalRelevance = relevance,
                urgency = urgency,
                opportunityValue = 0.3f,
                riskOfDelay = relevance * 0.8f,
                playerCommitment = commitment,
                cost = cost
            });
        }

        /// <summary>战略撤退判定（方案 4.5）：与当前目标无关的挑衅，离开就是胜利。</summary>
        public static bool IsStrategicRetreatWin(float priorityScore) => priorityScore < 0.18f;
    }
}
