using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Personalization;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 心理攻击系统：把敌人的心理攻击做成纯游戏机制（黑雾/低语/凝视/泥潭），
    /// 绝不输出可复制到现实的操控话术。攻击强度受 SafetySettings 全局控制。
    /// 弱点针对：敌人的攻击轴命中玩家画像高分弱点时伤害放大。
    /// </summary>
    public static class MentalDamageSystem
    {
        /// <summary>计算敌人对玩家的心理伤害（含弱点针对加成与安全倍率）。</summary>
        public static float Resolve(float baseDamage, WeaknessAxis axis, PlayerProfile profile, SafetySettings safety)
        {
            float mult = safety != null ? safety.MentalDamageMultiplier() : 1f;
            if (mult <= 0f) return 0f;

            float weaknessBonus = 1f;
            if (profile != null)
            {
                float score = profile.GetWeaknessScore(axis);
                weaknessBonus = 1f + score * 0.5f; // 弱点分 0.9 → 伤害 ×1.45
            }
            return baseDamage * mult * weaknessBonus;
        }
    }
}
