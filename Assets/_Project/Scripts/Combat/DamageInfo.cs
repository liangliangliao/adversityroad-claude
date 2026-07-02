using AdversityRoad.Personalization;

namespace AdversityRoad.Combat
{
    /// <summary>一次伤害的完整描述：物理 + 心理 + 硬直 + 击退。</summary>
    public struct DamageInfo
    {
        public float physicalDamage;
        public float mentalDamage;
        public WeaknessAxis mentalAxis;   // 心理伤害落到哪条弱点轴
        public float postureDamage;       // 削韧
        public float knockback;
        public UnityEngine.Vector3 sourcePosition;
        public string attackerId;
        public bool isMentalOnly;         // 纯心理攻击（凝视、低语）不触发受击动画
    }
}
