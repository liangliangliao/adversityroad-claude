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
        public UnityEngine.Vector3 contactPoint;   // 兵器/肢体实际接触受击者身体的世界点（命中特效定位）
        public bool hasContact;                     // contactPoint 是否有效（否则退回估算位置）
        public string attackerId;
        public bool isMentalOnly;         // 纯心理攻击（凝视、低语）不触发受击动画
        public bool unblockable;          // 必中：无法被格挡/闪避/对攻化解（蓄力技）
        public BodyPart bodyPart;         // 打中了哪个部位（由受击框落款，决定伤害/削韧倍率）

        // ---- 命中质量（见 Hitbox.ScanInner 的推导）：同一招打在同一个部位上，
        //      "扫到边"和"正中"、"用剑柄怼"和"用剑刃中段划过"不该是同一个伤害。----
        /// <summary>接触体积占比 0~1：受击部位有多少体积真的在这一刀扫过的空间里（≈深度/面积）。</summary>
        public float hitArea01;
        /// <summary>刃位 0~1：沿出手方向的相对位置。0≈贴身（剑柄/肘部），1≈够到最远（剑尖）。</summary>
        public float hitReach01;
        /// <summary>综合命中质量倍率（0.55~1.25）。0 表示这一击没有走判定框（投射物/心理攻击），按 1 处理。</summary>
        public float hitQuality;
    }
}
