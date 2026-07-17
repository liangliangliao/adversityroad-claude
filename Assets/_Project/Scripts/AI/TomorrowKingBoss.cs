using System.Collections;
using UnityEngine;
using AdversityRoad.Combat;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.AI
{
    /// <summary>
    /// 明天之王（拖延沼泽 Boss）：
    /// - 泥壳护体：常态伤害大幅削减——"明天再说"是打不动的，必须点燃场边三座五分钟火种台；
    /// - 火种齐燃：泥壳崩裂 → 长破绽 + 护体解除，此后可正常击伤；
    /// - 召唤明日泥怪：周期性从泥里拉出援军（场上限 2 只）；
    /// - 深泥浇灌：在玩家脚下制造临时深泥（大幅减速 + 行动力流失，五分钟火种可解）。
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class TomorrowKingBoss : MonoBehaviour
    {
        public float summonInterval = 14f;
        public float mudInterval = 9f;
        [Range(0f, 1f)] public float shieldedDamageMult = 0.18f;

        EnemyController _ec;
        Transform _player;
        float _summonCd = 8f, _mudCd = 5f;
        bool _shellBroken;

        void Awake() => _ec = GetComponent<EnemyController>();

        void Start()
        {
            var p = FindObjectOfType<PlayerController>();
            if (p != null) _player = p.transform;
            _ec.externalDamageMult = shieldedDamageMult;
            GameEvents.RaiseSubtitle("『明天之王』裹着打不动的泥壳——点燃场边三座「五分钟火种台」，让行动烧穿拖延！");
        }

        void Update()
        {
            if (_ec == null || _ec.State == EnemyState.Dead) return;

            // 火种齐燃 → 泥壳崩裂：护体解除 + 长破绽（战斗核心转折）
            if (!_shellBroken && SwampState.SparksLit >= SwampState.SparksNeeded)
            {
                _shellBroken = true;
                _ec.externalDamageMult = 1f;
                _ec.ForceBreak(3.5f);
                CombatFeedback.Debris(transform.position + Vector3.up, new Color(0.35f, 0.28f, 0.16f), 10);
                GameAudio.Play(GameAudio.Sfx.HeavyHit, 1f);
                if (_ec.dialogue != null) _ec.dialogue.Show("不……今天……怎么可以是今天！", 3f);
                GameEvents.RaiseSubtitle("泥壳崩裂——「明天」挡不住已经开始的行动。全力猛攻！");
            }

            if (_player == null || _ec.State == EnemyState.Stagger) return;
            float dist = Vector3.Distance(transform.position, _player.position);
            if (dist > 26f) return;

            float dt = Time.deltaTime;
            _summonCd -= dt; _mudCd -= dt;

            if (_summonCd <= 0)
            {
                _summonCd = summonInterval;
                SummonMud();
            }
            if (_mudCd <= 0 && dist < 16f)
            {
                _mudCd = mudInterval;
                StartCoroutine(CastDeepMud());
            }
        }

        /// <summary>从泥里拉出明日泥怪援军（场上限 2 只，未破壳时它靠人海拖住你）。</summary>
        void SummonMud()
        {
            if (EnemySpawnHook.AliveCount("enemy_tomorrow_mud") >= 2) return;
            Vector3 pos = transform.position +
                Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward * 5f;
            var minion = EnemySpawnHook.SpawnNear(EnemyType.TomorrowMud, EnemyTier.Novice, pos);
            if (minion == null) return;
            CombatFeedback.Debris(pos, new Color(0.35f, 0.28f, 0.16f), 6);
            if (_ec.dialogue != null) _ec.dialogue.Show("泥里的明天，都来拖住这个人！", 2.4f);
            GameAudio.Play(GameAudio.Sfx.Alert, 0.5f);
        }

        /// <summary>深泥浇灌：预警后在玩家脚下生成一片临时深泥（8 秒后干涸）。</summary>
        IEnumerator CastDeepMud()
        {
            Vector3 spot = _player.position;
            spot.y = transform.position.y - 0.95f;
            if (_ec.dialogue != null) _ec.dialogue.Show("陷进去吧——明天再走。", 1.6f);
            GameAudio.Play(GameAudio.Sfx.Alert, 0.4f);
            yield return new WaitForSeconds(0.7f);
            if (_ec.State == EnemyState.Dead) yield break;

            var visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.name = "TempDeepMud";
            visual.transform.position = spot + Vector3.up * 0.03f;
            visual.transform.localScale = new Vector3(6f, 0.03f, 6f);
            var mat = _ec.baseMaterial != null ? new Material(_ec.baseMaterial)
                : new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            var c = new Color(0.1f, 0.07f, 0.14f);
            mat.color = c;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            visual.GetComponent<MeshRenderer>().sharedMaterial = mat;

            var zone = new GameObject("TempDeepMudZone");
            zone.transform.position = spot + Vector3.up * 1f;
            var col = zone.AddComponent<BoxCollider>();
            col.size = new Vector3(6f, 2f, 6f);
            var mire = zone.AddComponent<ProcrastinationMire>();
            mire.speedMultiplier = 0.4f;
            mire.actionDrainPerSec = 6f;

            CombatFeedback.RecipeBurst(spot, new Color(0.3f, 0.2f, 0.4f));
            Destroy(visual, 8f);
            Destroy(zone, 8f);
        }
    }
}
