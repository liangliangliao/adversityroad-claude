using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 资源拾取（低谷与生存线）：水瓶/食物包——低谷里先解决生存，再谈别的。
    /// 走近拾取：恢复生命与意志。
    /// </summary>
    public class SupplyPickup : MonoBehaviour
    {
        public string supplyName = "食物包";
        public float hpRestore = 20f;
        public float willRestore = 15f;

        float _bob;
        Vector3 _basePos;

        public static SupplyPickup Spawn(Vector3 pos, string name, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = "Supply_" + name;
            go.transform.position = pos;
            go.transform.localScale = new Vector3(0.5f, 0.6f, 0.4f);
            go.GetComponent<MeshRenderer>().sharedMaterial =
                CombatFeedback.EnergyMaterial(color, 0.85f);
            var p = go.AddComponent<SupplyPickup>();
            p.supplyName = name;
            return p;
        }

        void Start() => _basePos = transform.position;

        void Update()
        {
            _bob += Time.deltaTime;
            transform.position = _basePos + Vector3.up * Mathf.Sin(_bob * 2f) * 0.15f;
            transform.Rotate(0, 70f * Time.deltaTime, 0);

            var p = FindObjectOfType<PlayerController>();
            if (p == null) return;
            if (Vector3.Distance(transform.position, p.transform.position) > 1.6f) return;

            p.Stats.hp = Mathf.Min(p.Stats.maxHp, p.Stats.hp + hpRestore);
            GameEvents.RaisePlayerHpChanged(p.Stats.hp, p.Stats.maxHp);
            p.Stats.RestoreAxis(Personalization.WeaknessAxis.WillpowerCollapse, willRestore);
            CombatFeedback.HitSpark(transform.position, new Color(0.6f, 0.9f, 0.6f), 5);
            GameAudio.Play(GameAudio.Sfx.Parry, 0.5f);
            GameEvents.RaiseSubtitle("拾取「" + supplyName + "」——低谷里，先照顾好基本生存。");
            Destroy(gameObject);
        }
    }

    /// <summary>寒冷区（车库寒夜）：意志持续流失。取暖点附近（WarmBuff）豁免。</summary>
    [RequireComponent(typeof(Collider))]
    public class ColdZone : MonoBehaviour
    {
        public float willDrainPerSec = 3f;

        float _warnedAt = -99f;

        void Awake() => GetComponent<Collider>().isTrigger = true;

        void OnTriggerStay(Collider other)
        {
            var p = other.GetComponentInParent<PlayerController>();
            if (p == null) return;
            if (p.GetComponent<WarmBuff>() != null) return;   // 带着暖意就不怕冷
            p.Stats.TakeMentalDamage(Personalization.WeaknessAxis.WillpowerCollapse,
                willDrainPerSec * Time.deltaTime);
            if (Time.time - _warnedAt > 14f)
            {
                _warnedAt = Time.time;
                GameEvents.RaiseSubtitle("〔寒夜〕意志在被冷风一点点吹散——找橙色的取暖点烤一会儿火。");
            }
        }
    }

    /// <summary>取暖点：站入恢复意志并获得「暖意」（离开后短时间内寒冷区豁免）。</summary>
    [RequireComponent(typeof(Collider))]
    public class WarmSpot : MonoBehaviour
    {
        public float willRegenPerSec = 10f;
        public float warmDuration = 12f;

        void Awake() => GetComponent<Collider>().isTrigger = true;

        void OnTriggerStay(Collider other)
        {
            var p = other.GetComponentInParent<PlayerController>();
            if (p == null) return;
            p.Stats.RestoreAxis(Personalization.WeaknessAxis.WillpowerCollapse,
                willRegenPerSec * Time.deltaTime);
            var buff = p.GetComponent<WarmBuff>();
            if (buff == null) buff = p.gameObject.AddComponent<WarmBuff>();
            buff.Refresh(warmDuration);
        }
    }

    /// <summary>暖意：离开取暖点后的一段时间内不受寒冷区侵蚀。</summary>
    public class WarmBuff : MonoBehaviour
    {
        float _until;

        public void Refresh(float duration) => _until = Time.time + duration;

        void Update()
        {
            if (Time.time >= _until) Destroy(this);
        }
    }

    /// <summary>
    /// 求助电话亭（低谷与生存线核心交互）：承认需要帮助，不等于失去尊严。
    /// 走近使用：大幅恢复生命与心理属性；若低谷巨像在场，它会被"外部支援"震出大破绽。
    /// 带冷却可重复使用。
    /// </summary>
    public class HelpPhoneBooth : MonoBehaviour
    {
        public float interactRange = 2.8f;
        public float cooldown = 30f;

        float _readyAt;
        GameObject _glow;

        void Start()
        {
            _glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(_glow.GetComponent<Collider>());
            _glow.name = "PhoneGlow";
            _glow.transform.SetParent(transform, false);
            _glow.transform.localPosition = new Vector3(0, 2.6f, 0);
            _glow.transform.localScale = Vector3.one * 0.45f;
            _glow.GetComponent<MeshRenderer>().sharedMaterial =
                CombatFeedback.EnergyMaterial(new Color(0.4f, 0.9f, 0.6f), 0.85f);
        }

        void Update()
        {
            bool ready = Time.time >= _readyAt;
            if (_glow != null) _glow.SetActive(ready);
            if (!ready) return;

            var p = FindObjectOfType<PlayerController>();
            if (p == null) return;
            if (Vector3.Distance(transform.position, p.transform.position) > interactRange) return;

            _readyAt = Time.time + cooldown;
            p.Stats.hp = Mathf.Min(p.Stats.maxHp, p.Stats.hp + 30f);
            GameEvents.RaisePlayerHpChanged(p.Stats.hp, p.Stats.maxHp);
            p.Stats.RestoreMental(30f);
            p.Stats.ReduceRumination(15f);
            CombatFeedback.RecipeBurst(transform.position + Vector3.up, new Color(0.4f, 0.9f, 0.6f));
            GameAudio.Play(GameAudio.Sfx.Parry, 0.8f);

            // 低谷巨像在场：求助的声音让"无力感"松动——Boss 大破绽
            AI.EnemyController colossus = null;
            foreach (var e in FindObjectsOfType<AI.EnemyController>())
                if (e.State != AI.EnemyState.Dead && e.profile != null &&
                    e.profile.enemyId != null && e.profile.enemyId.StartsWith("boss_valley_colossus"))
                { colossus = e; break; }
            if (colossus != null &&
                Vector3.Distance(colossus.transform.position, transform.position) < 30f)
            {
                colossus.ForceBreak(3f);
                GameEvents.RaiseSubtitle("你拨通了求助电话——低谷巨像的「无力感」松动了！【大破绽】" +
                    "（承认需要帮助，正是它最怕的事）");
            }
            else
            {
                GameEvents.RaiseSubtitle("求助电话：说出「我需要帮」并不丢人——恢复了生命与心神。");
            }
        }
    }
}
