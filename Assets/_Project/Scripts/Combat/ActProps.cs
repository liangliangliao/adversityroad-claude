using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 账本对质（两元赌桌核心机制）：桌上的账本记着事实——
    /// 走近即"对质"：两元赖账王当场语塞进入长破绽。可重复使用（有冷却）。
    /// 象征"用白纸黑字的事实回应耍赖，而不是陷入争吵"。
    /// </summary>
    public class LedgerProp : MonoBehaviour
    {
        public float interactRange = 2.8f;
        public float cooldown = 22f;

        float _readyAt;
        GameObject _glow;

        void Start() => BuildGlow();

        void BuildGlow()
        {
            _glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(_glow.GetComponent<Collider>());
            _glow.name = "LedgerGlow";
            _glow.transform.SetParent(transform, false);
            _glow.transform.localPosition = new Vector3(0, 0.6f, 0);
            _glow.transform.localScale = Vector3.one * 0.4f;
            _glow.GetComponent<MeshRenderer>().sharedMaterial =
                CombatFeedback.EnergyMaterial(new Color(0.95f, 0.9f, 0.6f), 0.8f);
        }

        void Update()
        {
            bool ready = Time.time >= _readyAt;
            if (_glow != null) _glow.SetActive(ready);
            if (!ready) return;

            var p = FindObjectOfType<PlayerController>();
            if (p == null) return;
            if (Vector3.Distance(transform.position, p.transform.position) > interactRange) return;

            // 找到场上的两元赖账王，对质破绽
            AI.EnemyController king = null;
            foreach (var e in FindObjectsOfType<AI.EnemyController>())
                if (e.State != AI.EnemyState.Dead && e.profile != null &&
                    e.profile.enemyId != null && e.profile.enemyId.StartsWith("boss_gamble_king"))
                { king = e; break; }
            if (king == null) return;

            _readyAt = Time.time + cooldown;
            king.ForceBreak(2.8f);
            p.Stats.RestoreAxis(Personalization.WeaknessAxis.FairnessSensitivity, 15f);
            CombatFeedback.HitSpark(transform.position + Vector3.up, new Color(0.95f, 0.9f, 0.6f), 6);
            GameAudio.Play(GameAudio.Sfx.Parry, 0.8f);
            GameEvents.RaiseSubtitle("账本对质——白纸黑字，赖账王当场语塞！【大破绽】（账本冷却中，稍后可再次对质）");
        }
    }

    /// <summary>债务车影的全局小状态：已收集欠条残片数（3 张集齐 → 新车债王护体崩碎）。</summary>
    public static class DebtState
    {
        public const int NotesNeeded = 3;
        public static int NotesCollected;

        public static void Reset() { NotesCollected = 0; }
    }

    /// <summary>欠条残片：停车场里发光的事实凭据。走近拾取——集齐三张，债王护体崩碎。</summary>
    public class DebtNoteProp : MonoBehaviour
    {
        float _spin;

        public static DebtNoteProp Spawn(Vector3 pos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = "DebtNote";
            go.transform.position = pos;
            go.transform.localScale = new Vector3(0.55f, 0.72f, 0.05f);
            go.GetComponent<MeshRenderer>().sharedMaterial =
                CombatFeedback.EnergyMaterial(new Color(0.95f, 0.9f, 0.55f), 0.85f);

            var lightGo = new GameObject("NoteGlow");
            lightGo.transform.SetParent(go.transform, false);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 6;
            l.intensity = 1.2f;
            l.color = new Color(1f, 0.9f, 0.5f);

            return go.AddComponent<DebtNoteProp>();
        }

        void Update()
        {
            _spin += Time.deltaTime * 90f;
            transform.rotation = Quaternion.Euler(0, _spin, 0);
            var p = FindObjectOfType<PlayerController>();
            if (p == null) return;
            if (Vector3.Distance(transform.position, p.transform.position) > 1.6f) return;

            DebtState.NotesCollected++;
            p.Stats.RestoreAxis(Personalization.WeaknessAxis.FairnessSensitivity, 10f);
            p.Stats.ReduceRumination(6f);
            CombatFeedback.HitSpark(transform.position, new Color(0.95f, 0.9f, 0.55f), 5);
            GameAudio.Play(GameAudio.Sfx.Parry, 0.6f);
            GameEvents.RaiseSubtitle("拾取欠条残片（" + DebtState.NotesCollected + "/" +
                DebtState.NotesNeeded + "）——证据在手，焦虑就少一分。");
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 车流幻影区（陌生挑衅路口）：马路上呼啸的幻影车流——
    /// 站在其中持续掉血。挑衅镜像会引诱你追进来："不被拖入战场"的空间化。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class TrafficPhantomZone : MonoBehaviour
    {
        public float damagePerSec = 6f;

        float _warnedAt = -99f;

        void Awake() => GetComponent<Collider>().isTrigger = true;

        void OnTriggerStay(Collider other)
        {
            var p = other.GetComponentInParent<PlayerController>();
            if (p == null || p.IsInvincible) return;
            p.Stats.TakePhysicalDamage(damagePerSec * Time.deltaTime);
            if (Time.time - _warnedAt > 6f)
            {
                _warnedAt = Time.time;
                CombatFeedback.HitFlash(p.gameObject);
                GameEvents.RaiseSubtitle("〔车流幻影〕危险区持续掉血——别被挑衅拖进马路，退回路口！");
                GameAudio.Play(GameAudio.Sfx.Hurt, 0.5f);
            }
        }
    }
}
