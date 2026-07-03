using UnityEngine;
using AdversityRoad.Personalization;

namespace AdversityRoad.Data
{
    /// <summary>
    /// 技能定义（原创命名，规避既有武侠 IP 名称）：
    /// 影步连踢 / 逆伤崩拳 / 破念指 / 流云步 / 定心护体 / 意志燃烧 / 壁走突袭 / 翻身反击 / 斩念气刃 / 起步斩
    /// </summary>
    [CreateAssetMenu(menuName = "AdversityRoad/SkillDefinition")]
    public class SkillDefinition : ScriptableObject
    {
        public string skillId;
        public string displayName;
        [TextArea] public string description;

        [Header("消耗")]
        public float staminaCost = 15f;
        public float willCost = 0f;

        [Header("逆伤类技能：自损代价（如逆伤崩拳消耗自尊/意志）")]
        public WeaknessAxis selfCostAxis = WeaknessAxis.WillpowerCollapse;
        public float selfCostAxisDamage = 0f;

        [Header("效果")]
        public float physicalDamage = 0f;
        public float postureDamage = 0f;
        public float knockback = 0f;
        public float mentalRestore = 0f;   // 定心护体/自我确认类恢复技能

        [Header("远程（斩念气刃等投射类技能）")]
        public bool isRanged = false;
        public float projectileSpeed = 16f;
        public float projectileScale = 1f;

        [Header("能量消耗：大招需消耗意势（0=不需要）")]
        public int momentumCost = 0;

        [Header("时序")]
        public float cooldown = 5f;
        public float castLockTime = 0.6f;
        public float hitboxOpenTime = 0.35f;

        public string animatorTrigger;
    }
}
