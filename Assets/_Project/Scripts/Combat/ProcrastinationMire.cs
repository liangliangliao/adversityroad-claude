using UnityEngine;
using AdversityRoad.Player;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 拖延泥潭（独居小屋机制）：区域内移速下降 + 决断值缓慢流失。
    /// 玩家使用"五分钟起步"类技能（mentalRestore）可抵消。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ProcrastinationMire : MonoBehaviour
    {
        [Range(0.1f, 1f)] public float speedMultiplier = 0.45f;
        public float resolveDrainPerSec = 3f;

        void Awake() => GetComponent<Collider>().isTrigger = true;

        void OnTriggerStay(Collider other)
        {
            var p = other.GetComponentInParent<PlayerController>();
            if (p == null) return;
            p.MoveSpeedMultiplier = speedMultiplier;
            p.Stats.TakeMentalDamage(Personalization.WeaknessAxis.Procrastination,
                resolveDrainPerSec * Time.deltaTime);
        }

        void OnTriggerExit(Collider other)
        {
            var p = other.GetComponentInParent<PlayerController>();
            if (p != null) p.MoveSpeedMultiplier = 1f;
        }
    }
}
