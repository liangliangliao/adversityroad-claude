using System;
using System.Collections.Generic;

namespace AdversityRoad.Personalization
{
    [Serializable]
    public class WeaknessScore
    {
        public WeaknessAxis axis;
        public float score;              // 0-1
        public string evidenceSummary;   // 匿名化摘要，不含原文
    }

    /// <summary>
    /// 玩家画像：只保存匿名化摘要 + 弱点标签 + 匹配参数。
    /// 数据最小化原则：绝不保存真实姓名、地点、联系方式。
    /// </summary>
    [Serializable]
    public class PlayerProfile
    {
        public string playerId = "local_player";
        public List<WeaknessScore> weaknessScores = new List<WeaknessScore>();
        public List<string> lifeThemes = new List<string>();
        public List<string> avoidedTopics = new List<string>();
        public List<string> preferredGrowthThemes = new List<string>();
        public float challengeTolerance = 0.6f;
        public float psychologicalIntensityLimit = 0.7f;
        public List<string> unlockedSceneIds = new List<string>();
        public List<string> recentFailedSceneIds = new List<string>();
        public List<string> currentGoalKeywords = new List<string>();

        public float GetWeaknessScore(WeaknessAxis axis)
        {
            foreach (var w in weaknessScores)
                if (w.axis == axis) return w.score;
            return 0f;
        }

        public void SetWeaknessScore(WeaknessAxis axis, float score, string evidence = "")
        {
            foreach (var w in weaknessScores)
                if (w.axis == axis)
                {
                    w.score = UnityEngine.Mathf.Clamp01(score);
                    if (!string.IsNullOrEmpty(evidence)) w.evidenceSummary = evidence;
                    return;
                }
            weaknessScores.Add(new WeaknessScore
            {
                axis = axis,
                score = UnityEngine.Mathf.Clamp01(score),
                evidenceSummary = evidence
            });
        }
    }
}
