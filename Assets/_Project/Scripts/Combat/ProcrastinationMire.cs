using UnityEngine;
using AdversityRoad.Player;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 拖延泥潭：区域内移速下降 + 行动力缓慢流失。
    /// 浅泥轻微减速、深泥大幅减速并快速消耗行动力。
    /// 玩家使用「五分钟火种」可立即恢复行动力并清除减速。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ProcrastinationMire : MonoBehaviour
    {
        [Range(0.1f, 1f)] public float speedMultiplier = 0.45f;
        public float actionDrainPerSec = 3f;

        void Awake() => GetComponent<Collider>().isTrigger = true;

        void OnTriggerStay(Collider other)
        {
            var p = other.GetComponentInParent<PlayerController>();
            if (p == null) return;
            p.MoveSpeedMultiplier = Mathf.Min(p.MoveSpeedMultiplier, speedMultiplier);
            p.Stats.TakeMentalDamage(Personalization.WeaknessAxis.Procrastination,
                actionDrainPerSec * Time.deltaTime);
        }

        void OnTriggerExit(Collider other)
        {
            var p = other.GetComponentInParent<PlayerController>();
            if (p != null) p.MoveSpeedMultiplier = 1f;
        }

        // 临时深泥（明天之王浇灌的）到时干涸销毁：玩家还站在里面时 OnTriggerExit
        // 不会触发，主动把减速还原
        void OnDestroy()
        {
            var p = FindObjectOfType<PlayerController>();
            if (p != null) p.MoveSpeedMultiplier = 1f;
        }
    }
}
