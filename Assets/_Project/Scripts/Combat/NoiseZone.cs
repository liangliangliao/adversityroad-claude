using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 噪声区（街道环境机制）：广告牌下/公交站旁的持续环境噪声——
    /// 站在其中专注缓慢流失、偶发心理低语字幕。
    /// 「定心姿态」大幅减免；不读心盾生效期间完全免疫（猜测不当事实）。
    /// 影响刻意做得很轻：方案要求"不能过强，避免玩家感到眩晕"。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class NoiseZone : MonoBehaviour
    {
        public float focusDrainPerSec = 2f;

        static readonly string[] Whispers =
        {
            "……听见了吗？", "他们是不是在议论……", "那声咳嗽，好像是冲这边……",
        };

        float _whisperAt = -99f;

        void Awake() => GetComponent<Collider>().isTrigger = true;

        void OnTriggerStay(Collider other)
        {
            var p = other.GetComponentInParent<PlayerController>();
            if (p == null) return;
            if (MindShieldBuff.IsActive) return;   // 不读心盾：噪声进不来

            float drain = focusDrainPerSec;
            var stance = p.GetComponent<StanceSystem>();
            if (stance != null)
                drain *= stance.IncomingMentalMult(Personalization.WeaknessAxis.NoiseSensitivity);
            p.Stats.TakeMentalDamage(Personalization.WeaknessAxis.NoiseSensitivity,
                drain * Time.deltaTime);

            if (Time.time - _whisperAt > 15f)
            {
                _whisperAt = Time.time;
                GameEvents.RaiseSubtitle("〔噪声区〕" + Whispers[Random.Range(0, Whispers.Length)] +
                    "（定心姿态可减免，离开即止）");
            }
        }
    }
}
