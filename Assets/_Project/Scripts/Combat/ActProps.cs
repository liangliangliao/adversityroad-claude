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
    /// 好人卡之墙（好人牢笼 Boss 机制）：围住玩家的金色卡墙。
    /// 「责任归还」技能立即打破；否则数秒后自行消散。
    /// </summary>
    public class CageWall : MonoBehaviour
    {
        public float lifetime = 4.5f;

        float _dieAt;

        public static CageWall Spawn(Vector3 pos, Quaternion rot)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "GoodCardWall";
            go.transform.position = pos;
            go.transform.rotation = rot;
            go.transform.localScale = new Vector3(3.2f, 2.4f, 0.25f);
            go.GetComponent<MeshRenderer>().sharedMaterial =
                CombatFeedback.EnergyMaterial(new Color(0.95f, 0.8f, 0.4f), 0.55f);
            return go.AddComponent<CageWall>();
        }

        void Start() => _dieAt = Time.time + lifetime;

        void Update()
        {
            if (Time.time >= _dieAt) Break(false);
        }

        public void Break(bool byPlayer = true)
        {
            CombatFeedback.Debris(transform.position, new Color(0.95f, 0.8f, 0.4f), 5);
            if (byPlayer) GameAudio.Play(GameAudio.Sfx.HeavyHit, 0.6f);
            Destroy(gameObject);
        }

        /// <summary>打破场上全部好人卡墙（责任归还技能调用），返回打破数量。</summary>
        public static int BreakAll()
        {
            int n = 0;
            foreach (var w in FindObjectsOfType<CageWall>()) { w.Break(); n++; }
            return n;
        }
    }

    /// <summary>
    /// 代付之门（无限代付者 Boss 机制）：地上开出的吸取之门——
    /// 站在其中意志与边界被持续吸走。限时消散，走开即止。
    /// </summary>
    public class PayDrainZone : MonoBehaviour
    {
        public float willDrainPerSec = 4f;
        public float boundaryDrainPerSec = 4f;

        public static PayDrainZone Spawn(Vector3 groundPos, float lifetime)
        {
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.name = "PayGateVisual";
            visual.transform.position = groundPos + Vector3.up * 0.04f;
            visual.transform.localScale = new Vector3(5f, 0.02f, 5f);
            visual.GetComponent<MeshRenderer>().sharedMaterial =
                CombatFeedback.EnergyMaterial(new Color(0.35f, 0.6f, 0.45f), 0.5f);
            Object.Destroy(visual, lifetime);

            var zone = new GameObject("PayDrainZone");
            zone.transform.position = groundPos + Vector3.up * 1f;
            var col = zone.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(5f, 2f, 5f);
            Object.Destroy(zone, lifetime);
            return zone.AddComponent<PayDrainZone>();
        }

        void OnTriggerStay(Collider other)
        {
            var p = other.GetComponentInParent<PlayerController>();
            if (p == null) return;
            p.Stats.TakeMentalDamage(Personalization.WeaknessAxis.WillpowerCollapse,
                willDrainPerSec * Time.deltaTime);
            p.Stats.TakeMentalDamage(Personalization.WeaknessAxis.BoundaryConflict,
                boundaryDrainPerSec * Time.deltaTime);
        }
    }

    /// <summary>
    /// 代付请求区（无限代付走廊）：走廊上一道道"请求"——
    /// **举着盾通过 = 明确拒绝**（回补边界、消退关系消耗）；
    /// 空手走过 = 默认代付（边界受损、关系消耗上升）。
    /// 方案"默认同意走廊变长，明确边界走廊缩短"的可玩化。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class PayRequestZone : MonoBehaviour
    {
        public float rearmCooldown = 20f;

        float _readyAt;

        void Awake() => GetComponent<Collider>().isTrigger = true;

        void OnTriggerEnter(Collider other)
        {
            if (Time.time < _readyAt) return;
            var p = other.GetComponentInParent<PlayerController>();
            if (p == null) return;
            _readyAt = Time.time + rearmCooldown;

            var combat = p.GetComponent<PlayerCombatController>();
            if (combat != null && combat.IsGuarding)
            {
                p.Stats.RestoreAxis(Personalization.WeaknessAxis.BoundaryConflict, 8f);
                p.Stats.ReduceRelationshipDrain(8f);
                CombatFeedback.HitSpark(p.transform.position + Vector3.up, new Color(0.4f, 0.85f, 0.6f), 4);
                GameAudio.Play(GameAudio.Sfx.Parry, 0.5f);
                GameEvents.RaiseSubtitle("举盾通过——明确拒绝：「这次费用需要你自己承担。」边界回稳。");
            }
            else
            {
                p.Stats.TakeMentalDamage(Personalization.WeaknessAxis.BoundaryConflict, 8f);
                p.Stats.AddRelationshipDrain(10f);
                GameAudio.Play(GameAudio.Sfx.Hurt, 0.45f);
                GameEvents.RaiseSubtitle("空手走过请求区=默认代付——下一道请求，试试举着盾（格挡）通过。");
            }
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
