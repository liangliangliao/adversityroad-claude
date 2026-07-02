using System.Collections.Generic;
using UnityEngine;

namespace AdversityRoad.Personalization
{
    /// <summary>
    /// 弱点标签器（MVP 规则版）：中文关键词 → 弱点轴打分。
    /// 正式版可替换为 Unity Inference Engine 本地分类模型或云端 LLM，
    /// 但接口保持不变：string → PlayerProfile。
    /// </summary>
    public static class WeaknessTagger
    {
        static readonly Dictionary<WeaknessAxis, string[]> Keywords = new Dictionary<WeaknessAxis, string[]>
        {
            { WeaknessAxis.Procrastination, new[]{ "拖延", "不想动", "明天再", "迟迟", "开始不了", "懒", "起不来" } },
            { WeaknessAxis.LowConfidence, new[]{ "没信心", "做不到", "不相信自己", "怀疑能力", "不敢尝试" } },
            { WeaknessAxis.NoiseSensitivity, new[]{ "噪声", "吵", "被打扰", "眼神", "咳嗽", "分心", "注意力" } },
            { WeaknessAxis.Shame, new[]{ "羞耻", "丢脸", "被看不起", "被嘲笑", "自卑", "尴尬" } },
            { WeaknessAxis.JobAnxiety, new[]{ "失业", "找工作", "投简历", "面试", "被拒", "没回应", "offer" } },
            { WeaknessAxis.BoundaryConflict, new[]{ "欺负", "霸凌", "借钱", "不还", "被阻挠", "不敢拒绝", "被利用" } },
            { WeaknessAxis.FairnessSensitivity, new[]{ "不公平", "赖账", "承诺", "责任", "认输", "说话不算" } },
            { WeaknessAxis.SelfDoubt, new[]{ "自我怀疑", "是不是我", "我不行", "否定自己" } },
            { WeaknessAxis.FailureFear, new[]{ "失败", "低谷", "流浪", "堕落", "崩塌", "挫败", "放弃过" } },
            { WeaknessAxis.WillpowerCollapse, new[]{ "坚持不住", "自律", "半途而废", "松懈", "意志" } },
        };

        /// <summary>分析匿名化后的文本，输出弱点分。分值 = min(1, 命中次数 × 0.25)。</summary>
        public static PlayerProfile Analyze(string anonymizedText, PlayerProfile baseProfile = null)
        {
            var profile = baseProfile ?? new PlayerProfile();
            if (string.IsNullOrEmpty(anonymizedText)) return profile;

            foreach (var kv in Keywords)
            {
                int hits = 0;
                foreach (var kw in kv.Value)
                {
                    int idx = 0;
                    while ((idx = anonymizedText.IndexOf(kw, idx)) >= 0) { hits++; idx += kw.Length; }
                }
                if (hits > 0)
                {
                    float newScore = Mathf.Min(1f, hits * 0.25f);
                    float old = profile.GetWeaknessScore(kv.Key);
                    profile.SetWeaknessScore(kv.Key, Mathf.Max(old, newScore),
                        "文本分析命中 " + hits + " 次相关表达");
                }
            }
            return profile;
        }
    }
}
