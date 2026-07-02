using System.Collections.Generic;

namespace AdversityRoad.Personalization
{
    /// <summary>
    /// 个性化流水线：文本 → 去识别化 → 弱点分析 → 场景推荐。
    /// 对应方案 10.2 节流程图。原文只在内存中流转，分析后不保存。
    /// </summary>
    public static class PersonalizationPipeline
    {
        public static List<SceneMatchResult> Process(
            string rawPlayerText,
            List<SceneTemplate> allScenes,
            Core.SafetySettings safety,
            out PlayerProfile profile)
        {
            string anonymized = SafetyFilter.Anonymize(rawPlayerText);
            profile = WeaknessTagger.Analyze(anonymized,
                DefaultProfileFactory.CreateDefaultLifeTemplate());
            return SceneMatcher.Rank(allScenes, profile, safety);
        }
    }
}
