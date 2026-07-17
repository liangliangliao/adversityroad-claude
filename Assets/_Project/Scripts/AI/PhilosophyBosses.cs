using System.Collections;
using UnityEngine;
using AdversityRoad.Combat;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.AI
{
    /// <summary>
    /// 概念迷宫师（哲学虚无图书馆 Boss）：把你困在"再读一段就懂了"的迷宫里。
    /// - 引文护体：被层层引文包裹，常规伤害大减——点亮馆内三座「行动灯台」即碎；
    /// - 引文弹幕："某某说过"的书页飞刃（自我怀疑轴）；
    /// - 概念迷环：以自身为中心的环形冲击（预警后爆发）。
    /// 有些问题不能只靠想清楚解决，而要靠行动回答。
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class ConceptMazeMasterBoss : MonoBehaviour
    {
        public float quoteInterval = 6f;
        public float mazeInterval = 11f;
        public float mazeRadius = 6f;
        [Range(0f, 1f)] public float shieldedDamageMult = 0.25f;

        EnemyController _ec;
        Transform _player;
        PlayerCombatController _playerCombat;
        float _quoteCd = 3f, _mazeCd = 8f, _hintAt = -99f;
        bool _busy, _shieldBroken;

        void Awake() => _ec = GetComponent<EnemyController>();

        void Start()
        {
            var p = FindObjectOfType<PlayerController>();
            if (p != null) { _player = p.transform; _playerCombat = p.GetComponent<PlayerCombatController>(); }
            _ec.externalDamageMult = shieldedDamageMult;
            GameEvents.RaiseSubtitle("『概念迷宫师』被层层引文护体——点亮馆内三座「行动灯台」，让行动烧穿概念！");
        }

        void Update()
        {
            if (_ec == null || _ec.State == EnemyState.Dead) return;

            if (!_shieldBroken && PhilState.LampsLit >= PhilState.LampsNeeded)
            {
                _shieldBroken = true;
                _ec.externalDamageMult = 1f;
                _ec.ForceBreak(3.2f);
                CombatFeedback.Debris(transform.position + Vector3.up, new Color(0.85f, 0.8f, 0.65f), 9);
                GameAudio.Play(GameAudio.Sfx.HeavyHit, 1f);
                if (_ec.dialogue != null) _ec.dialogue.Show("你怎么不接着读了？！", 3f);
                GameEvents.RaiseSubtitle("引文护体崩碎——概念挡不住已经开始的行动。全力猛攻！");
            }

            if (_player == null || _busy || _ec.State == EnemyState.Stagger) return;
            float dist = Vector3.Distance(transform.position, _player.position);
            if (dist > 24f) return;

            if (!_shieldBroken && Time.time - _hintAt > 20f && dist < 18f)
            {
                _hintAt = Time.time;
                GameEvents.RaiseSubtitle("护体中（行动灯台 " + PhilState.LampsLit + "/" +
                    PhilState.LampsNeeded + "）——先去点亮馆内的灯台。");
            }

            float dt = Time.deltaTime;
            _quoteCd -= dt; _mazeCd -= dt;

            if (_mazeCd <= 0 && dist < mazeRadius + 3f)
            {
                _mazeCd = mazeInterval;
                StartCoroutine(ConceptMazeRing());
                return;
            }
            if (_quoteCd <= 0 && dist < 20f)
            {
                _quoteCd = quoteInterval;
                StartCoroutine(QuoteVolley());
            }
        }

        IEnumerator QuoteVolley()
        {
            _busy = true;
            if (_ec.dialogue != null)
                _ec.dialogue.Show(Random.value < 0.5f ? "书上说，你还没准备好。" : "再读一段，就懂了……", 2.2f);
            for (int i = 0; i < 3 && _ec.State != EnemyState.Dead; i++)
            {
                if (_player == null) break;
                Vector3 origin = transform.position + Vector3.up * 1.6f + transform.forward * 0.5f;
                Vector3 target = _player.position + Vector3.up * 1.0f;
                Projectile.Launch(transform, origin, target - origin, new DamageInfo
                {
                    physicalDamage = _ec.profile.physicalDamage * 0.32f,
                    mentalDamage = _ec.profile.mentalDamage * 0.5f,
                    mentalAxis = Personalization.WeaknessAxis.SelfDoubt,
                    knockback = 0.5f,
                    attackerId = _ec.profile.enemyId
                }, 13f, new Color(0.8f, 0.78f, 0.6f), _ec.baseMaterial);
                GameAudio.Play(GameAudio.Sfx.Swing, 0.4f);
                yield return new WaitForSeconds(0.24f);
            }
            _busy = false;
        }

        IEnumerator ConceptMazeRing()
        {
            _busy = true;
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(ring.GetComponent<Collider>());
            ring.name = "MazeWarnRing";
            ring.transform.position = new Vector3(transform.position.x,
                transform.position.y - 0.9f, transform.position.z);
            ring.GetComponent<MeshRenderer>().sharedMaterial =
                CombatFeedback.EnergyMaterial(new Color(0.55f, 0.5f, 0.8f), 0.4f);
            if (_ec.dialogue != null) _ec.dialogue.Show("概念，环环相扣——", 1.6f);
            GameAudio.Play(GameAudio.Sfx.Alert, 0.55f);

            float t = 0f;
            while (t < 1.0f)
            {
                t += Time.deltaTime;
                float k = 0.3f + 0.7f * t;
                ring.transform.localScale = new Vector3(mazeRadius * 2f * k, 0.03f, mazeRadius * 2f * k);
                yield return null;
            }
            Destroy(ring);

            if (_ec.State != EnemyState.Dead && _player != null)
            {
                CombatFeedback.Shake(0.5f);
                CombatFeedback.RecipeBurst(transform.position, new Color(0.55f, 0.5f, 0.8f));
                GameAudio.Play(GameAudio.Sfx.HeavyHit, 0.85f);
                var pc = _player.GetComponent<PlayerController>();
                if (Vector3.Distance(_player.position, transform.position) <= mazeRadius + 0.5f &&
                    _playerCombat != null && pc != null && !pc.IsInvincible)
                    _playerCombat.TakeHit(new DamageInfo
                    {
                        physicalDamage = _ec.profile.physicalDamage * 0.9f,
                        mentalDamage = _ec.profile.mentalDamage * 0.5f,
                        mentalAxis = Personalization.WeaknessAxis.SelfDoubt,
                        knockback = 3.5f,
                        sourcePosition = transform.position,
                        attackerId = _ec.profile.enemyId
                    });
            }
            _busy = false;
        }
    }

    /// <summary>
    /// 无限问题兽（无限追问大厅 Boss）：每回答一个问题，它就再生两个。
    /// - 问题弹幕：连环"为什么？"（命中大量累积反刍）；
    /// - 召唤引文幽灵助阵；
    /// - 它怕的不是答案，是你不再接题：言语攻防选对回应即大幅削韧。
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class QuestionBeastBoss : MonoBehaviour
    {
        public float volleyInterval = 6.5f;
        public float summonInterval = 15f;

        EnemyController _ec;
        Transform _player;
        float _volleyCd = 3f, _summonCd = 9f;
        bool _busy;

        static readonly string[] Questions =
        {
            "为什么是你？", "如果又失败了呢？", "这么做有什么意义？", "你确定你准备好了？",
        };

        void Awake() => _ec = GetComponent<EnemyController>();

        void Start()
        {
            var p = FindObjectOfType<PlayerController>();
            if (p != null) _player = p.transform;
            GameEvents.RaiseSubtitle("『无限问题兽』现身——它的问题没有尽头，别一题一题接；反刍高了记得复盘归档。");
        }

        void Update()
        {
            if (_ec == null || _player == null || _busy) return;
            if (_ec.State == EnemyState.Dead || _ec.State == EnemyState.Stagger) return;
            float dist = Vector3.Distance(transform.position, _player.position);
            if (dist > 24f) return;

            float dt = Time.deltaTime;
            _volleyCd -= dt; _summonCd -= dt;

            if (_summonCd <= 0)
            {
                _summonCd = summonInterval;
                if (EnemySpawnHook.AliveCount("enemy_quote_ghost") < 2)
                {
                    Vector3 pos = transform.position +
                        Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward * 5f;
                    if (EnemySpawnHook.SpawnNear(EnemyType.QuoteGhost, EnemyTier.Novice, pos) != null &&
                        _ec.dialogue != null)
                        _ec.dialogue.Show("这个问题，引出了下一个问题。", 2.4f);
                }
                return;
            }
            if (_volleyCd <= 0 && dist < 20f)
            {
                _volleyCd = volleyInterval;
                StartCoroutine(QuestionVolley());
            }
        }

        IEnumerator QuestionVolley()
        {
            _busy = true;
            if (_ec.dialogue != null)
                _ec.dialogue.Show(Questions[Random.Range(0, Questions.Length)], 2.2f);
            for (int i = 0; i < 4 && _ec.State != EnemyState.Dead; i++)
            {
                if (_player == null) break;
                Vector3 origin = transform.position + Vector3.up * 1.5f + transform.forward * 0.5f;
                Vector3 target = _player.position + Vector3.up * 1.0f;
                Projectile.Launch(transform, origin, target - origin, new DamageInfo
                {
                    physicalDamage = _ec.profile.physicalDamage * 0.25f,
                    mentalDamage = _ec.profile.mentalDamage * 0.4f,
                    mentalAxis = Personalization.WeaknessAxis.SelfDoubt,
                    knockback = 0.4f,
                    attackerId = _ec.profile.enemyId
                }, 14f, new Color(0.7f, 0.45f, 0.65f), _ec.baseMaterial);
                GameAudio.Play(GameAudio.Sfx.Swing, 0.35f);
                yield return new WaitForSeconds(0.2f);
            }
            _busy = false;
        }
    }

    /// <summary>
    /// 无限追问者（意志断桥 Boss·哲学线终战）：站在断桥尽头，不断提出让桥面崩塌的新问题。
    /// - 追问弹幕（意志轴）；
    /// - 意义崩桥：在玩家脚下引发崩塌冲击（预警红圈）；
    /// - 破防机制：桥面两侧的「行动答台」——用一次行动回答，它当场语塞大破绽。
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class InfiniteAskerBoss : MonoBehaviour
    {
        public float askInterval = 6f;
        public float collapseInterval = 9f;
        public float collapseRadius = 3.2f;

        EnemyController _ec;
        Transform _player;
        PlayerCombatController _playerCombat;
        float _askCd = 3f, _collapseCd = 6f, _hintAt = -99f;
        bool _busy;

        static readonly string[] Asks =
        {
            "然后呢？然后呢？然后呢？", "万一都白做了呢？", "有没有绝对正确的路？",
        };

        void Awake() => _ec = GetComponent<EnemyController>();

        void Start()
        {
            var p = FindObjectOfType<PlayerController>();
            if (p != null) { _player = p.transform; _playerCombat = p.GetComponent<PlayerCombatController>(); }
            GameEvents.RaiseSubtitle("『无限追问者』立于断桥尽头——桥边发亮的「行动答台」能让它当场语塞。");
        }

        void Update()
        {
            if (_ec == null || _player == null || _busy) return;
            if (_ec.State == EnemyState.Dead || _ec.State == EnemyState.Stagger) return;
            float dist = Vector3.Distance(transform.position, _player.position);
            if (dist > 26f) return;

            if (Time.time - _hintAt > 25f && dist < 18f)
            {
                _hintAt = Time.time;
                GameEvents.RaiseSubtitle("追问接不完——走向桥边的行动答台，用「做」来回答它。");
            }

            float dt = Time.deltaTime;
            _askCd -= dt; _collapseCd -= dt;

            if (_collapseCd <= 0 && dist < 16f)
            {
                _collapseCd = collapseInterval;
                StartCoroutine(MeaningCollapse());
                return;
            }
            if (_askCd <= 0 && dist < 22f)
            {
                _askCd = askInterval;
                StartCoroutine(AskVolley());
            }
        }

        IEnumerator AskVolley()
        {
            _busy = true;
            if (_ec.dialogue != null)
                _ec.dialogue.Show(Asks[Random.Range(0, Asks.Length)], 2.2f);
            for (int i = 0; i < 3 && _ec.State != EnemyState.Dead; i++)
            {
                if (_player == null) break;
                Vector3 origin = transform.position + Vector3.up * 1.6f + transform.forward * 0.5f;
                Vector3 target = _player.position + Vector3.up * 1.0f;
                Projectile.Launch(transform, origin, target - origin, new DamageInfo
                {
                    physicalDamage = _ec.profile.physicalDamage * 0.32f,
                    mentalDamage = _ec.profile.mentalDamage * 0.5f,
                    mentalAxis = Personalization.WeaknessAxis.WillpowerCollapse,
                    knockback = 0.6f,
                    attackerId = _ec.profile.enemyId
                }, 13f, new Color(0.55f, 0.6f, 0.95f), _ec.baseMaterial);
                GameAudio.Play(GameAudio.Sfx.Cast, 0.4f);
                yield return new WaitForSeconds(0.26f);
            }
            _busy = false;
        }

        /// <summary>意义崩桥：玩家脚下预警 → 崩塌冲击（物理+意志）。</summary>
        IEnumerator MeaningCollapse()
        {
            _busy = true;
            Vector3 spot = _player.position;
            spot.y = transform.position.y - 0.9f;
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(ring.GetComponent<Collider>());
            ring.name = "CollapseWarnRing";
            ring.transform.position = spot + Vector3.up * 0.05f;
            ring.GetComponent<MeshRenderer>().sharedMaterial =
                CombatFeedback.EnergyMaterial(new Color(0.9f, 0.35f, 0.3f), 0.45f);
            if (_ec.dialogue != null) _ec.dialogue.Show("这块桥面的意义，也撑不住了——", 1.5f);
            GameAudio.Play(GameAudio.Sfx.Alert, 0.5f);

            float t = 0f;
            while (t < 0.9f)
            {
                t += Time.deltaTime;
                float pulse = 0.6f + Mathf.PingPong(t * 3f, 0.6f);
                ring.transform.localScale = new Vector3(collapseRadius * 2f * pulse, 0.03f, collapseRadius * 2f * pulse);
                yield return null;
            }
            Destroy(ring);

            if (_ec.State != EnemyState.Dead && _player != null)
            {
                CombatFeedback.Shake(0.6f);
                CombatFeedback.Debris(spot + Vector3.up * 0.4f, new Color(0.5f, 0.5f, 0.6f), 7);
                GameAudio.Play(GameAudio.Sfx.HeavyHit, 0.9f);
                var pc = _player.GetComponent<PlayerController>();
                if (Vector3.Distance(_player.position, spot) <= collapseRadius + 0.5f &&
                    _playerCombat != null && pc != null && !pc.IsInvincible)
                    _playerCombat.TakeHit(new DamageInfo
                    {
                        physicalDamage = _ec.profile.physicalDamage * 1.2f,
                        mentalDamage = _ec.profile.mentalDamage * 0.5f,
                        mentalAxis = Personalization.WeaknessAxis.WillpowerCollapse,
                        knockback = 4f,
                        sourcePosition = spot,
                        attackerId = _ec.profile.enemyId
                    });
            }
            _busy = false;
        }
    }
}
