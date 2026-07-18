using System.Collections;
using UnityEngine;
using AdversityRoad.Combat;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.AI
{
    /// <summary>
    /// 万眼审判者（眼神审判走廊 Boss）：
    /// - 凝视光束：三连发评价之光（羞耻轴）；
    /// - 虚假凝视点：制造"是不是在看我"的幻影（打幻影亏专注涨反刍，
    ///   不读心盾显形、注意力回收清场）；
    /// - 万目扫射：以自身为中心的大范围凝视冲击（预警后爆发）。
    /// 被看见，不等于被否定。
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class ThousandEyeJudgeBoss : MonoBehaviour
    {
        public float beamInterval = 6f;
        public float decoyInterval = 12f;
        public float sweepInterval = 11f;
        public float sweepRadius = 6.5f;
        public int maxDecoys = 3;

        EnemyController _ec;
        Transform _player;
        PlayerCombatController _playerCombat;
        float _beamCd = 3f, _decoyCd = 6f, _sweepCd = 9f;
        bool _busy;

        void Awake() => _ec = GetComponent<EnemyController>();

        void Start()
        {
            var p = FindObjectOfType<PlayerController>();
            if (p != null) { _player = p.transform; _playerCombat = p.GetComponent<PlayerCombatController>(); }
            GameEvents.RaiseSubtitle("『万眼审判者』睁开千目——虚假凝视点是幻影，用「不读心盾」「注意力回收」破解。");
        }

        void Update()
        {
            if (_ec == null || _player == null || _busy) return;
            if (_ec.State == EnemyState.Dead || _ec.State == EnemyState.Stagger) return;
            float dist = Vector3.Distance(transform.position, _player.position);
            if (dist > 24f) return;

            float rate = Mathf.Lerp(0.65f, 1f, _ec.HpRatio);
            float dt = Time.deltaTime;
            _beamCd -= dt; _decoyCd -= dt; _sweepCd -= dt;

            if (_sweepCd <= 0 && dist < sweepRadius + 3f)
            {
                _sweepCd = sweepInterval * rate;
                StartCoroutine(GazeSweep());
                return;
            }
            if (_decoyCd <= 0)
            {
                _decoyCd = decoyInterval * rate;
                SpawnGazeDecoys();
                return;
            }
            if (_beamCd <= 0 && dist < 20f)
            {
                _beamCd = beamInterval * rate;
                StartCoroutine(GazeBeams());
            }
        }

        IEnumerator GazeBeams()
        {
            _busy = true;
            if (_ec.dialogue != null)
                _ec.dialogue.Show(Random.value < 0.5f ? "所有人都在看你。" : "他们记得你出丑的样子。", 2.2f);
            for (int i = 0; i < 3 && _ec.State != EnemyState.Dead; i++)
            {
                if (_player == null) break;
                Vector3 origin = transform.position + Vector3.up * 1.7f + transform.forward * 0.5f;
                Vector3 target = _player.position + Vector3.up * 1.0f;
                Projectile.Launch(transform, origin, target - origin, new DamageInfo
                {
                    physicalDamage = _ec.profile.physicalDamage * 0.3f,
                    mentalDamage = _ec.profile.mentalDamage * 0.55f,
                    mentalAxis = Personalization.WeaknessAxis.Shame,
                    knockback = 0.5f,
                    attackerId = _ec.profile.enemyId
                }, 13f, new Color(0.7f, 0.6f, 1f), _ec.baseMaterial);
                GameAudio.Play(GameAudio.Sfx.Cast, 0.4f);
                yield return new WaitForSeconds(0.26f);
            }
            _busy = false;
        }

        void SpawnGazeDecoys()
        {
            int alive = FindObjectsOfType<PhantomDecoy>().Length;
            int want = Mathf.Min(maxDecoys - alive, 2);
            if (want <= 0) return;
            for (int i = 0; i < want; i++)
            {
                Vector3 pos = _player.position +
                    Quaternion.Euler(0, Random.Range(60f, 300f), 0) *
                    (_player.forward * Random.Range(4.5f, 7.5f));
                pos.y = transform.position.y;
                PhantomDecoy.Spawn(pos);
            }
            if (_ec.dialogue != null) _ec.dialogue.Show("那边也有眼睛——在看你。", 2.2f);
            GameEvents.RaiseSubtitle("虚假凝视点出现——无法确认的注视不必回应，清场后打真身。");
            GameAudio.Play(GameAudio.Sfx.Alert, 0.4f);
        }

        /// <summary>万目扫射：预警后以 Boss 为中心的凝视冲击（羞耻轴）。</summary>
        IEnumerator GazeSweep()
        {
            _busy = true;
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(ring.GetComponent<Collider>());
            ring.name = "GazeWarnRing";
            ring.transform.position = new Vector3(transform.position.x,
                transform.position.y - 0.9f, transform.position.z);
            ring.GetComponent<MeshRenderer>().sharedMaterial =
                CombatFeedback.EnergyMaterial(new Color(0.7f, 0.6f, 1f), 0.4f);
            if (_ec.dialogue != null) _ec.dialogue.Show("千目——审视！", 1.5f);
            GameAudio.Play(GameAudio.Sfx.Alert, 0.6f);

            float t = 0f;
            while (t < 1.0f)
            {
                t += Time.deltaTime;
                float k = 0.3f + 0.7f * t;
                ring.transform.localScale = new Vector3(sweepRadius * 2f * k, 0.03f, sweepRadius * 2f * k);
                yield return null;
            }
            Destroy(ring);

            if (_ec.State != EnemyState.Dead && _player != null)
            {
                CombatFeedback.Shake(0.55f);
                CombatFeedback.RecipeBurst(transform.position, new Color(0.7f, 0.6f, 1f));
                GameAudio.Play(GameAudio.Sfx.HeavyHit, 0.85f);
                var pc = _player.GetComponent<PlayerController>();
                if (Vector3.Distance(_player.position, transform.position) <= sweepRadius + 0.5f &&
                    _playerCombat != null && pc != null && !pc.IsInvincible)
                    _playerCombat.TakeHit(new DamageInfo
                    {
                        physicalDamage = _ec.profile.physicalDamage * 0.9f,
                        mentalDamage = _ec.profile.mentalDamage * 0.6f,
                        mentalAxis = Personalization.WeaknessAxis.Shame,
                        knockback = 4f,
                        sourcePosition = transform.position,
                        attackerId = _ec.profile.enemyId
                    });
            }
            _busy = false;
        }

        void OnDestroy()
        {
            foreach (var d in FindObjectsOfType<PhantomDecoy>()) d.Dissolve();
        }
    }

    /// <summary>
    /// 挑衅镜像（陌生挑衅路口 Boss）——本关核心是"不被拖入战场"：
    /// 周期性进入【挑衅窗口】（头顶亮起挑衅标记、双手一摊）：
    /// - 窗口内打它 = 挑衅得逞：它吸血变强（怒意层数，物理伤害递增，最多 3 层）；
    /// - 窗口内忍住不打 = 挑衅落空：它自曝大破绽（此时猛攻）。
    /// 不是所有挑衅都值得回应——等它先露出破绽。
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class TauntMirrorBoss : MonoBehaviour
    {
        public float tauntInterval = 12f;
        public float tauntWindow = 4f;
        public int maxRage = 3;

        EnemyController _ec;
        Transform _player;
        float _tauntCd = 7f;
        int _rage;
        bool _taunting;
        TextMesh _mark;

        void Awake() => _ec = GetComponent<EnemyController>();

        void Start()
        {
            var p = FindObjectOfType<PlayerController>();
            if (p != null) _player = p.transform;
            BuildMark();
            GameEvents.RaiseSubtitle("『挑衅镜像』现身——它亮出【挑衅】标记时别出手：打它它变强，不理它它自己露破绽。");
        }

        void BuildMark()
        {
            var go = new GameObject("TauntMark");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0, 3.9f, 0);
            _mark = go.AddComponent<TextMesh>();
            _mark.text = "";
            _mark.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _mark.fontSize = 72;
            _mark.characterSize = 0.06f;
            _mark.anchor = TextAnchor.MiddleCenter;
            _mark.color = new Color(1f, 0.6f, 0.2f);
            var mr = go.GetComponent<MeshRenderer>();
            if (_mark.font != null) mr.material = _mark.font.material;
            go.AddComponent<World.FaceCamera>();
        }

        void Update()
        {
            if (_ec == null || _player == null || _taunting) return;
            if (_ec.State == EnemyState.Dead || _ec.State == EnemyState.Stagger) return;
            float dist = Vector3.Distance(transform.position, _player.position);
            if (dist > 22f) return;

            _tauntCd -= Time.deltaTime;
            if (_tauntCd <= 0)
            {
                _tauntCd = tauntInterval;
                StartCoroutine(TauntPhase());
            }
        }

        /// <summary>挑衅窗口：亮标记双手一摊——按窗口内是否被打决定变强还是破绽。</summary>
        IEnumerator TauntPhase()
        {
            _taunting = true;
            if (_mark != null) _mark.text = "【挑衅】";
            if (_ec.dialogue != null)
                _ec.dialogue.Show(Random.value < 0.5f ? "来啊，就这？" : "不敢动手了？我看你就是怂。", 3f);
            GameEvents.RaiseSubtitle("挑衅窗口——忍住！此刻打它=它吸血变强；不理它=它自己露出大破绽。");
            GameAudio.Play(GameAudio.Sfx.Alert, 0.5f);

            float hpBefore = _ec.HpRatio;
            float t = 0f;
            while (t < tauntWindow && _ec.State != EnemyState.Dead)
            {
                t += Time.deltaTime;
                if (_mark != null)
                    _mark.characterSize = 0.06f * (1f + 0.2f * Mathf.Sin(t * 10f));
                yield return null;
            }
            if (_mark != null) { _mark.text = ""; _mark.characterSize = 0.06f; }
            if (_ec.State == EnemyState.Dead) { _taunting = false; yield break; }

            bool wasHit = _ec.HpRatio < hpBefore - 0.001f;
            if (wasHit)
            {
                // 挑衅得逞：吸血变强（怒意层数递增物理伤害）
                if (_rage < maxRage)
                {
                    _rage++;
                    _ec.profile.physicalDamage *= 1.15f;
                }
                _ec.HealFraction(0.05f);
                CombatFeedback.RecipeBurst(transform.position, new Color(1f, 0.45f, 0.2f));
                if (_ec.dialogue != null) _ec.dialogue.Show("上钩了——你的火气喂饱了我！", 2.6f);
                GameEvents.RaiseSubtitle("挑衅得逞（怒意 " + _rage + "/" + maxRage +
                    "）——它吸血变强了。下次挑衅窗口，忍住别打。");
            }
            else
            {
                // 挑衅落空：自曝大破绽
                _ec.ForceBreak(2.8f);
                if (_ec.dialogue != null) _ec.dialogue.Show("你怎么……不接招？！", 2.6f);
                GameEvents.RaiseSubtitle("挑衅落空——它愣住了！大破绽，全力猛攻！");
            }
            _taunting = false;
        }
    }
}
