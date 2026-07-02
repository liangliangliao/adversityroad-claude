using System.Collections.Generic;
using UnityEngine;

namespace AdversityRoad.Personalization
{
    [System.Serializable]
    public class SceneTemplate
    {
        public string sceneId;
        public string displayName;
        public WeaknessAxis primaryAxis;
        public string themeTag;          // 用于禁用主题检查
        public float baseIntensity;      // 0-1 心理压迫强度
        public string bossId;
        public List<string> enemyIds = new List<string>();
    }

    [System.Serializable]
    public class SceneMatchResult
    {
        public SceneTemplate scene;
        public float score;
    }

    /// <summary>
    /// 场景匹配器：实现方案第 10.4 节公式。
    /// SceneScore = 弱点匹配×0.35 + 目标相关×0.25 + 最近失败×0.15 + 主动偏好×0.15 + 安全适配×0.10
    /// </summary>
    public static class SceneMatcher
    {
        public static List<SceneMatchResult> Rank(
            List<SceneTemplate> scenes, PlayerProfile profile, Core.SafetySettings safety)
        {
            var results = new List<SceneMatchResult>();
            foreach (var s in scenes)
            {
                // 禁用主题直接排除
                if (!SafetyFilter.IsTopicAllowed(s.themeTag, profile, safety)) continue;

                float weakness = profile.GetWeaknessScore(s.primaryAxis);

                float goal = 0f;
                foreach (var g in profile.currentGoalKeywords)
                    if (!string.IsNullOrEmpty(g) && (s.displayName.Contains(g) || s.themeTag.Contains(g)))
                        { goal = 1f; break; }

                float recentFail = profile.recentFailedSceneIds.Contains(s.sceneId) ? 1f : 0f;

                float preference = 0f;
                foreach (var t in profile.preferredGrowthThemes)
                    if (!string.IsNullOrEmpty(t) && s.themeTag.Contains(t)) { preference = 1f; break; }

                // 安全适配：场景强度越接近玩家承受上限内的舒适区，得分越高
                float safetyFit = 1f - Mathf.Abs(s.baseIntensity - profile.psychologicalIntensityLimit * 0.8f);
                safetyFit = Mathf.Clamp01(safetyFit);
                if (s.baseIntensity > profile.psychologicalIntensityLimit) safetyFit *= 0.3f;

                float score = weakness * 0.35f + goal * 0.25f + recentFail * 0.15f
                            + preference * 0.15f + safetyFit * 0.10f;

                results.Add(new SceneMatchResult { scene = s, score = score });
            }
            results.Sort((a, b) => b.score.CompareTo(a.score));
            return results;
        }
    }
}
