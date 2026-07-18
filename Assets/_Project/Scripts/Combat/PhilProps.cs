using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.Combat
{
    /// <summary>哲学与行动线的全局小状态：已点亮的行动灯台数、已走过的行动之门数。</summary>
    public static class PhilState
    {
        public const int LampsNeeded = 3;
        public static int LampsLit;
        public static int ActionDoorsPassed;

        public static void Reset() { LampsLit = 0; ActionDoorsPassed = 0; }
    }

    /// <summary>
    /// 行动灯台（哲学虚无图书馆）：走近即点亮——"有些问题不是靠继续读解决的"。
    /// 三座齐亮时概念迷宫师的引文护体崩碎。点亮回补自尊与行动力。
    /// </summary>
    public class ActionLampAltar : MonoBehaviour
    {
        public float interactRange = 3f;

        bool _lit;
        float _flicker;
        GameObject _flame;

        void Update()
        {
            if (_lit)
            {
                if (_flame != null)
                {
                    _flicker += Time.deltaTime * 6f;
                    float k = 1f + Mathf.Sin(_flicker) * 0.12f;
                    _flame.transform.localScale = new Vector3(0.5f * k, 0.7f + Mathf.Sin(_flicker * 1.4f) * 0.1f, 0.5f * k);
                }
                return;
            }
            var p = FindObjectOfType<PlayerController>();
            if (p == null) return;
            if (Vector3.Distance(transform.position, p.transform.position) > interactRange) return;

            _lit = true;
            PhilState.LampsLit++;

            _flame = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Object.DestroyImmediate(_flame.GetComponent<Collider>());
            _flame.name = "LampFlame";
            _flame.transform.SetParent(transform, false);
            _flame.transform.localPosition = new Vector3(0, 1.5f, 0);
            _flame.transform.localScale = new Vector3(0.5f, 0.7f, 0.5f);
            _flame.GetComponent<MeshRenderer>().sharedMaterial =
                CombatFeedback.EnergyMaterial(new Color(1f, 0.85f, 0.4f), 0.8f);
            var lg = new GameObject("LampLight");
            lg.transform.SetParent(transform, false);
            lg.transform.localPosition = new Vector3(0, 2.2f, 0);
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 11;
            l.intensity = 1.4f;
            l.color = new Color(1f, 0.85f, 0.5f);

            p.Stats.RestoreAxis(Personalization.WeaknessAxis.SelfDoubt, 12f);
            p.Stats.RestoreAxis(Personalization.WeaknessAxis.Procrastination, 15f);
            CombatFeedback.RecipeBurst(transform.position + Vector3.up, new Color(1f, 0.85f, 0.4f));
            GameAudio.Play(GameAudio.Sfx.Parry, 0.7f);
            GameEvents.RaiseSubtitle(PhilState.LampsLit >= PhilState.LampsNeeded
                ? "第三座行动灯台点亮——引文的迷雾散了！概念迷宫师的护体崩碎！"
                : "行动灯台点亮（" + PhilState.LampsLit + "/" + PhilState.LampsNeeded +
                  "）——比起再读一段引文，一个小行动更能回答问题。");
        }
    }

    /// <summary>
    /// 问题之门（无限追问大厅）：写着"为什么？如果失败怎么办？"的门——
    /// 走近它 = 又钻进一个问题：反刍上升。它是诱饵：问题门可以路过，不必推开。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class QuestionDoorZone : MonoBehaviour
    {
        public string question = "为什么？";
        public float rearmCooldown = 18f;

        float _readyAt;

        void Awake() => GetComponent<Collider>().isTrigger = true;

        void OnTriggerEnter(Collider other)
        {
            if (Time.time < _readyAt) return;
            var p = other.GetComponentInParent<PlayerController>();
            if (p == null) return;
            _readyAt = Time.time + rearmCooldown;
            p.Stats.AddRumination(9f);
            p.Stats.TakeMentalDamage(Personalization.WeaknessAxis.SelfDoubt, 5f);
            GameAudio.Play(GameAudio.Sfx.Hurt, 0.4f);
            GameEvents.RaiseSubtitle("你凑近了问题之门「" + question +
                "」——问题后面还是问题。反刍上升；去找发亮的「行动之门」。");
        }
    }

    /// <summary>
    /// 行动之门（无限追问大厅）：不给答案、只给下一步——
    /// 穿过它恢复行动力、降低反刍。走过三扇，通往 Boss 的大门开启。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ActionDoorZone : MonoBehaviour
    {
        bool _passed;

        void Awake() => GetComponent<Collider>().isTrigger = true;

        void OnTriggerEnter(Collider other)
        {
            if (_passed) return;
            var p = other.GetComponentInParent<PlayerController>();
            if (p == null) return;
            _passed = true;
            PhilState.ActionDoorsPassed++;
            p.Stats.RestoreAxis(Personalization.WeaknessAxis.Procrastination, 18f);
            p.Stats.ReduceRumination(10f);
            CombatFeedback.HitSpark(p.transform.position + Vector3.up, new Color(1f, 0.8f, 0.35f), 5);
            GameAudio.Play(GameAudio.Sfx.Parry, 0.6f);
            GameEvents.RaiseSubtitle("穿过行动之门（" + PhilState.ActionDoorsPassed +
                "/3）——不用先想清一切，走出这一步本身就是回答。");
        }
    }

    /// <summary>
    /// 深渊回送区（意志断桥）：从断桥失足坠入虚无——不惩罚死亡，
    /// 传送回桥头，掉一点血与意志，再走一次。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class VoidFallZone : MonoBehaviour
    {
        public Vector3 respawnPoint;

        void Awake() => GetComponent<Collider>().isTrigger = true;

        void OnTriggerEnter(Collider other)
        {
            var p = other.GetComponentInParent<PlayerController>();
            if (p == null) return;
            var cc = p.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            p.transform.position = respawnPoint;
            if (cc != null) cc.enabled = true;
            p.Stats.TakePhysicalDamage(8f);
            p.Stats.TakeMentalDamage(Personalization.WeaknessAxis.WillpowerCollapse, 6f);
            GameAudio.Play(GameAudio.Sfx.Hurt, 0.6f);
            GameEvents.RaiseSubtitle("桥下是虚无——摔下去不是终点，回到桥头再走一次。");
        }
    }

    /// <summary>
    /// 行动答台（意志断桥 Boss 场地）：用一次"行动回答"打断无限追问——
    /// 走近使用：无限追问者当场语塞大破绽。带冷却可重复使用。
    /// </summary>
    public class ActionAnswerAltar : MonoBehaviour
    {
        public float interactRange = 2.8f;
        public float cooldown = 24f;

        float _readyAt;
        GameObject _glow;

        void Start()
        {
            _glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(_glow.GetComponent<Collider>());
            _glow.name = "AnswerGlow";
            _glow.transform.SetParent(transform, false);
            _glow.transform.localPosition = new Vector3(0, 1.6f, 0);
            _glow.transform.localScale = Vector3.one * 0.45f;
            _glow.GetComponent<MeshRenderer>().sharedMaterial =
                CombatFeedback.EnergyMaterial(new Color(1f, 0.8f, 0.35f), 0.85f);
        }

        void Update()
        {
            bool ready = Time.time >= _readyAt;
            if (_glow != null) _glow.SetActive(ready);
            if (!ready) return;

            var p = FindObjectOfType<PlayerController>();
            if (p == null) return;
            if (Vector3.Distance(transform.position, p.transform.position) > interactRange) return;

            AI.EnemyController asker = null;
            foreach (var e in FindObjectsOfType<AI.EnemyController>())
                if (e.State != AI.EnemyState.Dead && e.profile != null &&
                    e.profile.enemyId != null && e.profile.enemyId.StartsWith("boss_infinite_asker"))
                { asker = e; break; }
            if (asker == null) return;

            _readyAt = Time.time + cooldown;
            asker.ForceBreak(2.8f);
            p.Stats.RestoreAxis(Personalization.WeaknessAxis.WillpowerCollapse, 15f);
            p.Stats.ReduceRumination(12f);
            CombatFeedback.RecipeBurst(transform.position + Vector3.up, new Color(1f, 0.8f, 0.35f));
            GameAudio.Play(GameAudio.Sfx.Parry, 0.85f);
            GameEvents.RaiseSubtitle("行动答台——「这个问题，我用做来回答。」无限追问者语塞！【大破绽】");
        }
    }
}
