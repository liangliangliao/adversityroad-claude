using UnityEngine;

namespace AdversityRoad.Combat
{
    /// <summary>物理伤害结算：防御减免、韧性削减、击退。</summary>
    public static class DamageResolver
    {
        // ---- 「重击」判定阈值（受击反应/顿帧/击飞/处决共用同一条线）----
        // 此前这两个数在 EnemyController 里被复制了四遍，一旦调整基础伤害就会
        // 悄悄让普通连段升格成"每下都击倒"。集中到一处，攻击力调值不再牵一发动全身。
        public const float HeavyPosture = 22f;
        public const float HeavyPhysical = 34f;

        /// <summary>这一击是否算重击（决定击倒/强顿帧/处决增伤）。</summary>
        public static bool IsHeavy(DamageInfo d) =>
            d.postureDamage >= HeavyPosture || d.physicalDamage >= HeavyPhysical;

        /// <summary>打击力度 0-1（供特效/顿帧/受击冲量按伤害连续分级，
        /// 而不是只有"轻/重"两档——轻拳和终结技的反馈差别必须看得出来）。</summary>
        public static float Power01(DamageInfo d) =>
            Mathf.Clamp01(Mathf.Max(d.physicalDamage / 60f, d.postureDamage / 40f));

        public static float ResolvePhysical(float rawDamage, float defense)
        {
            return Mathf.Max(1f, rawDamage * (100f / (100f + defense)));
        }

        public static Vector3 KnockbackDir(Vector3 sourcePos, Vector3 targetPos)
        {
            Vector3 dir = targetPos - sourcePos; dir.y = 0;
            return dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.back;
        }
    }
}
