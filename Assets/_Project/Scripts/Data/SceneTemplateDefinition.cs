using UnityEngine;
using AdversityRoad.Personalization;

namespace AdversityRoad.Data
{
    /// <summary>场景模板 ScriptableObject：供 SceneMatcher 匹配。</summary>
    [CreateAssetMenu(menuName = "AdversityRoad/SceneTemplate")]
    public class SceneTemplateDefinition : ScriptableObject
    {
        public SceneTemplate template;
        [TextArea] public string sceneNarrative;
    }
}
