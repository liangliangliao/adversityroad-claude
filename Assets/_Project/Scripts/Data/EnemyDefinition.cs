using UnityEngine;
using AdversityRoad.AI;

namespace AdversityRoad.Data
{
    /// <summary>敌人模板 ScriptableObject：设计师在编辑器中配置，运行时注入 EnemyController。</summary>
    [CreateAssetMenu(menuName = "AdversityRoad/EnemyDefinition")]
    public class EnemyDefinition : ScriptableObject
    {
        public EnemyProfile profile;
        [TextArea] public string designNotes;
    }
}
