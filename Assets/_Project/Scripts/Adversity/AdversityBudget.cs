using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Goals;
using AdversityRoad.Player;

namespace AdversityRoad.Adversity
{
    /// <summary>
    /// 逆境预算（方案 7.2）：每个遭遇有一个总预算 B，拆成五类分配——
    /// 导演**先定 B_target，再在五维之间分配**，而不是同时把所有压力拉满。
    /// </summary>
    public struct AdversityBudget
    {
        public float total;
        public float physical;        // 敌人数、攻击力、组合、Boss 阶段
        public float mental;          // 言语攻击频率、心理护甲、干扰
        public float resource;        // 补给、体力、装备消耗
        public float environmental;   // 视野、噪声、地形、封路
        public float time;            // 限时、机会窗口、交通延迟

        public string Describe() =>
            "B=" + total.ToString("F2") +
            " 物理" + physical.ToString("F2") +
            " 心理" + mental.ToString("F2") +
            " 资源" + resource.ToString("F2") +
            " 环境" + environmental.ToString("F2") +
            " 时间" + time.ToString("F2");
    }

    /// <summary>
    /// Adversity Budget Calculator（方案 7.3）：
    /// B_target = BaseStage × SkillFactor × GoalImportance × IntensitySetting × RecoveryModifier
    ///
    /// RecoveryModifier 是这套系统的良心所在：连续失败时**逐渐降低预算并增加训练/补给机会**；
    /// 连续轻松取胜时增加的是组合复杂度，而不是简单堆血。
    /// </summary>
    public static class AdversityBudgetCalculator
    {
        public static AdversityBudget Compute(GoalData goal, PlayerStats stats,
            int consecutiveDefeats, int consecutiveEasyWins)
        {
            // BaseStage：旅程越往后阶段压力越高（但只到 1.35，不无限膨胀）
            float baseStage = 1f;
            if (goal != null && goal.milestones.Count > 0)
            {
                int done = 0;
                foreach (var m in goal.milestones) if (m.done) done++;
                baseStage = 1f + 0.35f * ((float)done / goal.milestones.Count);
            }

            // SkillFactor：由玩家当前状态推出的可承受度（不是"读心"，只是看条）
            float skill = 1f;
            if (stats != null)
            {
                float vit = Mathf.Clamp01(stats.hp / Mathf.Max(1f, stats.maxHp));
                float will = Mathf.Clamp01(stats.will / Mathf.Max(1f, stats.maxWill));
                skill = Mathf.Lerp(0.7f, 1.25f, (vit * 0.6f + will * 0.4f));
            }

            float goalImportance = goal != null && !goal.completed ? 1.1f : 0.85f;

            float intensity = 1f;
            var safety = GameManager.Instance != null ? GameManager.Instance.safety : null;
            if (safety != null)
            {
                switch (safety.intensity)
                {
                    case MentalIntensity.Light: intensity = 0.6f; break;
                    case MentalIntensity.HighPressure: intensity = 1.3f; break;
                }
                if (safety.recoveryMode) intensity = 0.35f;
            }

            // 连续失败保护 + Challenge Ceiling（方案 C.2 V2.0 新增项）
            float recovery = 1f;
            if (consecutiveDefeats > 0) recovery = Mathf.Clamp(1f - consecutiveDefeats * 0.18f, 0.45f, 1f);
            else if (consecutiveEasyWins >= 2) recovery = 1.08f;   // 只加复杂度，不加血量

            float b = baseStage * skill * goalImportance * intensity * recovery;
            b = Mathf.Clamp(b, 0.3f, 1.6f);

            // ---- 五维分配：不同弱点/优势下重心不同，但总和恒等于 B ----
            float wPhysical = 0.34f, wMental = 0.22f, wResource = 0.16f, wEnv = 0.16f, wTime = 0.12f;

            // 第八章「羞耻与污名线」的重心与别处显著不同（方案 8.7）：
            // Physical 被**主动压低**到 15%，Mental 与 Environmental 承担主要压力。
            // 这是主题决定的，不是难度取巧——这一章的压力来自注视、指认与低语，
            // 而不是敌人更多、血更厚；本章明令禁止靠堆 Boss 血量制造难度。
            if (Shame.ShameLine.InChapter)
            {
                wPhysical = 0.15f; wMental = 0.40f;
                wEnv = 0.25f; wResource = 0.10f; wTime = 0.10f;
            }

            // 高压设置下把重心从"堆数值"移向"组合与环境"，符合"提升策略复杂度而非堆血"
            if (safety != null && safety.intensity == MentalIntensity.HighPressure)
            {
                wPhysical -= 0.06f; wEnv += 0.04f; wMental += 0.02f;
            }
            // 心理数值吃紧时主动降低心理维度：压力要可学，不是要压垮
            if (stats != null && stats.will < stats.maxWill * 0.35f)
            {
                float shift = 0.08f;
                wMental = Mathf.Max(0.05f, wMental - shift);
                wResource += shift;
            }
            // 目标里带时间限制才使用时间维度（方案 7.2：目标相关时才用，不滥用）
            if (goal == null || goal.deadlineDays <= 0)
            {
                wPhysical += wTime * 0.5f;
                wEnv += wTime * 0.5f;
                wTime = 0f;
            }

            return new AdversityBudget
            {
                total = b,
                physical = b * wPhysical,
                mental = b * wMental,
                resource = b * wResource,
                environmental = b * wEnv,
                time = b * wTime
            };
        }

        /// <summary>物理预算 → 这次遭遇最多放几个敌人（永远有上限，永远不在视野外贴脸刷）。</summary>
        public static int EnemyCountFor(AdversityBudget b) =>
            Mathf.Clamp(Mathf.RoundToInt(b.physical * 6f), 1, 4);

        /// <summary>心理预算 → 心理攻击最短间隔秒数（预算越低间隔越长）。</summary>
        public static float MentalCooldownFor(AdversityBudget b) =>
            Mathf.Lerp(16f, 6f, Mathf.Clamp01(b.mental / 0.4f));
    }
}
