using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.Combat
{
    /// <summary>拖延沼泽的全局小状态：已点燃的火种台数（3 座齐燃 → 明天之王泥壳崩裂）。</summary>
    public static class SwampState
    {
        public const int SparksNeeded = 3;
        public static int SparksLit;

        public static void Reset() { SparksLit = 0; }
    }

    /// <summary>
    /// 五分钟火种台：走近即点燃（行动本身就是点火）。点燃恢复行动力；
    /// 三座齐燃时明天之王的泥壳崩裂进入破防。象征"先做五分钟"堆出的行动惯性。
    /// </summary>
    public class FireSparkAltar : MonoBehaviour
    {
        public float interactRange = 3f;

        bool _lit;
        GameObject _flame;
        float _flicker;

        void Update()
        {
            if (_lit)
            {
                // 火焰摇曳
                if (_flame != null)
                {
                    _flicker += Time.deltaTime * 7f;
                    float s = 1f + Mathf.Sin(_flicker) * 0.15f;
                    _flame.transform.localScale = new Vector3(0.8f * s, 1.2f + Mathf.Sin(_flicker * 1.3f) * 0.2f, 0.8f * s);
                }
                return;
            }
            var p = FindObjectOfType<PlayerController>();
            if (p == null) return;
            if (Vector3.Distance(transform.position, p.transform.position) > interactRange) return;
            Ignite(p);
        }

        void Ignite(PlayerController p)
        {
            _lit = true;
            SwampState.SparksLit++;

            _flame = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Object.DestroyImmediate(_flame.GetComponent<Collider>());
            _flame.name = "SparkFlame";
            _flame.transform.SetParent(transform, false);
            _flame.transform.localPosition = new Vector3(0, 1.4f, 0);
            _flame.transform.localScale = new Vector3(0.8f, 1.2f, 0.8f);
            _flame.GetComponent<MeshRenderer>().sharedMaterial =
                CombatFeedback.EnergyMaterial(new Color(1f, 0.55f, 0.15f), 0.75f);

            var lightGo = new GameObject("SparkLight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = new Vector3(0, 2f, 0);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 12;
            l.intensity = 1.6f;
            l.color = new Color(1f, 0.65f, 0.3f);

            p.Stats.RestoreAxis(Personalization.WeaknessAxis.Procrastination, 30f);
            p.MoveSpeedMultiplier = 1f;
            CombatFeedback.RecipeBurst(transform.position + Vector3.up, new Color(1f, 0.6f, 0.2f));
            GameAudio.Play(GameAudio.Sfx.Parry, 0.8f);

            GameEvents.RaiseSubtitle(SwampState.SparksLit >= SwampState.SparksNeeded
                ? "第三座火种台点燃——火种齐燃！明天之王的泥壳正在崩裂！"
                : "火种台点燃（" + SwampState.SparksLit + "/" + SwampState.SparksNeeded +
                  "）——行动力回来了。再找下一座。");
        }
    }

    /// <summary>目标石板：站上去持续恢复行动力（明确的目标是干地）。</summary>
    [RequireComponent(typeof(Collider))]
    public class GoalStoneZone : MonoBehaviour
    {
        public float actionRegenPerSec = 14f;

        void Awake() => GetComponent<Collider>().isTrigger = true;

        void OnTriggerStay(Collider other)
        {
            var p = other.GetComponentInParent<PlayerController>();
            if (p == null) return;
            p.Stats.RestoreAxis(Personalization.WeaknessAxis.Procrastination,
                actionRegenPerSec * Time.deltaTime);
        }
    }

    /// <summary>
    /// 手机光点区：诱人偏离主路线的光——站入其中专注与行动力持续流失。
    /// "再看一眼就好"是拖延沼泽里最深的泥。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class PhoneLightZone : MonoBehaviour
    {
        public float focusDrainPerSec = 3f;
        public float actionDrainPerSec = 4f;

        float _warnedAt = -99f;

        void Awake() => GetComponent<Collider>().isTrigger = true;

        void OnTriggerStay(Collider other)
        {
            var p = other.GetComponentInParent<PlayerController>();
            if (p == null) return;
            p.Stats.TakeMentalDamage(Personalization.WeaknessAxis.NoiseSensitivity,
                focusDrainPerSec * Time.deltaTime);
            p.Stats.TakeMentalDamage(Personalization.WeaknessAxis.Procrastination,
                actionDrainPerSec * Time.deltaTime);
            if (Time.time - _warnedAt > 12f)
            {
                _warnedAt = Time.time;
                GameEvents.RaiseSubtitle("手机光点：\"再看一眼就好\"——注意力正在被吸走，离开光区。");
            }
        }
    }
}
