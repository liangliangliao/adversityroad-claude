using UnityEngine;

namespace AdversityRoad.Combat
{
    /// <summary>物理伤害结算：防御减免、韧性削减、击退。</summary>
    public static class DamageResolver
    {
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
