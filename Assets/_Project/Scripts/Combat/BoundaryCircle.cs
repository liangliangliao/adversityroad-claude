using UnityEngine;
using AdversityRoad.Player;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 边界圈（责任转嫁法院安全区）：站进来缓慢恢复边界值、消退过度负责。
    /// 象征"守住自己的范围"——不是逃避战斗，而是给玩家一个稳住边界、重新判断的落点。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class BoundaryCircle : MonoBehaviour
    {
        public float boundaryRegenPerSec = 12f;

        void Awake() => GetComponent<Collider>().isTrigger = true;

        void OnTriggerStay(Collider other)
        {
            var p = other.GetComponentInParent<PlayerController>();
            if (p == null) return;
            p.Stats.RestoreAxis(Personalization.WeaknessAxis.BoundaryConflict,
                boundaryRegenPerSec * Time.deltaTime);
            var debuff = p.GetComponent<OverResponsibilityDebuff>();
            if (debuff != null) Destroy(debuff);
        }
    }
}
