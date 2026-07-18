using System.Collections;
using UnityEngine;
using AdversityRoad.Combat;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.AI
{
    /// <summary>
    /// 目标遗忘者（目标遗忘房 Boss）：守着落灰的目标板，让你忘记为什么出发。
    /// - 遗忘之雾：大范围预警冲击——命中流失行动力与专注（"我进来是要干什么来着？"）；
    /// - 召唤完美准备者："再准备一下再打它吧"；
    /// - 目标板就在它身后：击败它，钉下今日唯一目标。
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class GoalForgetterBoss : MonoBehaviour
    {
        public float fogInterval = 9f;
        public float fogRadius = 6f;
        public float summonInterval = 15f;

        EnemyController _ec;
        Transform _player;
        PlayerCombatController _playerCombat;
        float _fogCd = 5f, _summonCd = 8f;
        bool _busy;

        void Awake() => _ec = GetComponent<EnemyController>();

        void Start()
        {
            var p = FindObjectOfType<PlayerController>();
            if (p != null) { _player = p.transform; _playerCombat = p.GetComponent<PlayerCombatController>(); }
            GameEvents.RaiseSubtitle("『目标遗忘者』守在目标板前——别忘了你进来是要做什么的。");
        }

        void Update()
        {
            if (_ec == null || _player == null || _busy) return;
            if (_ec.State == EnemyState.Dead || _ec.State == EnemyState.Stagger) return;
            float dist = Vector3.Distance(transform.position, _player.position);
            if (dist > 22f) return;

            float dt = Time.deltaTime;
            _fogCd -= dt; _summonCd -= dt;

            if (_fogCd <= 0 && dist < fogRadius + 3f)
            {
                _fogCd = fogInterval;
                StartCoroutine(ForgetFog());
                return;
            }
            if (_summonCd <= 0)
            {
                _summonCd = summonInterval;
                if (EnemySpawnHook.AliveCount("enemy_perfect_preparer") < 1)
                {
                    Vector3 pos = transform.position + transform.right * 5f;
                    if (EnemySpawnHook.SpawnNear(EnemyType.PerfectPreparer, EnemyTier.Novice, pos) != null &&
                        _ec.dialogue != null)
                        _ec.dialogue.Show("先别急着打我——再准备一下？", 2.4f);
                }
            }
        }

        /// <summary>遗忘之雾：预警后的大范围冲击——流失行动力与专注。</summary>
        IEnumerator ForgetFog()
        {
            _busy = true;
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(ring.GetComponent<Collider>());
            ring.name = "FogWarnRing";
            ring.transform.position = new Vector3(transform.position.x,
                transform.position.y - 0.9f, transform.position.z);
            ring.GetComponent<MeshRenderer>().sharedMaterial =
                CombatFeedback.EnergyMaterial(new Color(0.6f, 0.55f, 0.8f), 0.4f);
            if (_ec.dialogue != null) _ec.dialogue.Show("你进来……是要干什么来着？", 1.8f);
            GameAudio.Play(GameAudio.Sfx.Alert, 0.55f);

            float t = 0f;
            while (t < 1.0f)
            {
                t += Time.deltaTime;
                float k = 0.3f + 0.7f * t;
                ring.transform.localScale = new Vector3(fogRadius * 2f * k, 0.03f, fogRadius * 2f * k);
                yield return null;
            }
            Destroy(ring);

            if (_ec.State != EnemyState.Dead && _player != null)
            {
                CombatFeedback.Shake(0.4f);
                CombatFeedback.RecipeBurst(transform.position, new Color(0.6f, 0.55f, 0.8f));
                GameAudio.Play(GameAudio.Sfx.Hurt, 0.7f);
                var pc = _player.GetComponent<PlayerController>();
                if (Vector3.Distance(_player.position, transform.position) <= fogRadius + 0.5f &&
                    _playerCombat != null && pc != null && !pc.IsInvincible)
                {
                    _playerCombat.TakeHit(new DamageInfo
                    {
                        mentalDamage = _ec.profile.mentalDamage * 0.6f,
                        mentalAxis = Personalization.WeaknessAxis.Procrastination,
                        isMentalOnly = true,
                        attackerId = _ec.profile.enemyId
                    });
                    pc.Stats.TakeMentalDamage(Personalization.WeaknessAxis.NoiseSensitivity,
                        _ec.profile.mentalDamage * 0.3f);
                }
            }
            _busy = false;
        }
    }

    /// <summary>
    /// 好人牢笼（老实人消耗局 Boss）：没有边界的善良，最后变成一座笼子。
    /// - 好人卡投掷：预警落点——命中附着「过度负责」减益并抬升关系消耗（按『还』清除）；
    /// - 困人牢笼：在玩家四周立起好人卡之墙（数秒消散；「责任归还」立即打破）；
    /// - 召唤请求膨胀者："就这一次"永远有下一次。
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class GoodPersonCageBoss : MonoBehaviour
    {
        public float cardInterval = 8f;
        public float cardRadius = 2.6f;
        public float cageInterval = 14f;
        public float summonInterval = 16f;

        EnemyController _ec;
        Transform _player;
        PlayerCombatController _playerCombat;
        float _cardCd = 4f, _cageCd = 9f, _summonCd = 7f;
        bool _busy;

        void Awake() => _ec = GetComponent<EnemyController>();

        void Start()
        {
            var p = FindObjectOfType<PlayerController>();
            if (p != null) { _player = p.transform; _playerCombat = p.GetComponent<PlayerCombatController>(); }
            GameEvents.RaiseSubtitle("『好人牢笼』现身——被好人卡困住时，用「责任归还」（键3/还）打破牢笼！");
        }

        void Update()
        {
            if (_ec == null || _player == null || _busy) return;
            if (_ec.State == EnemyState.Dead || _ec.State == EnemyState.Stagger) return;
            float dist = Vector3.Distance(transform.position, _player.position);
            if (dist > 24f) return;

            float dt = Time.deltaTime;
            _cardCd -= dt; _cageCd -= dt; _summonCd -= dt;

            if (_cageCd <= 0 && dist < 14f)
            {
                _cageCd = cageInterval;
                BuildCage();
                return;
            }
            if (_cardCd <= 0 && dist < 18f)
            {
                _cardCd = cardInterval;
                StartCoroutine(GoodCardThrow());
                return;
            }
            if (_summonCd <= 0)
            {
                _summonCd = summonInterval;
                if (EnemySpawnHook.AliveCount("enemy_request_expander") < 2)
                {
                    Vector3 pos = transform.position +
                        Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward * 5f;
                    if (EnemySpawnHook.SpawnNear(EnemyType.RequestExpander, EnemyTier.Novice, pos) != null &&
                        _ec.dialogue != null)
                        _ec.dialogue.Show("他人这么好，再帮一个忙不过分吧？", 2.4f);
                }
            }
        }

        /// <summary>好人卡投掷：玩家脚下预警——命中附着「过度负责」并抬升关系消耗。</summary>
        IEnumerator GoodCardThrow()
        {
            _busy = true;
            Vector3 spot = _player.position;
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(ring.GetComponent<Collider>());
            ring.name = "CardWarnRing";
            spot.y = transform.position.y - 0.9f;
            ring.transform.position = spot + Vector3.up * 0.05f;
            ring.GetComponent<MeshRenderer>().sharedMaterial =
                CombatFeedback.EnergyMaterial(new Color(0.95f, 0.8f, 0.4f), 0.4f);
            if (_ec.dialogue != null) _ec.dialogue.Show("你人最好了——接着！", 1.5f);
            GameAudio.Play(GameAudio.Sfx.Alert, 0.5f);

            float t = 0f;
            while (t < 0.85f)
            {
                t += Time.deltaTime;
                float pulse = 0.6f + Mathf.PingPong(t * 3f, 0.6f);
                ring.transform.localScale = new Vector3(cardRadius * 2f * pulse, 0.03f, cardRadius * 2f * pulse);
                yield return null;
            }
            Destroy(ring);

            if (_ec.State != EnemyState.Dead && _player != null)
            {
                CombatFeedback.RecipeBurst(spot, new Color(0.95f, 0.8f, 0.4f));
                GameAudio.Play(GameAudio.Sfx.Hit, 0.7f);
                var pc = _player.GetComponent<PlayerController>();
                if (Vector3.Distance(_player.position, spot) <= cardRadius + 0.5f &&
                    pc != null && !pc.IsInvincible)
                {
                    OverResponsibilityDebuff.Apply(pc, 6f, 0.6f);
                    pc.Stats.AddRelationshipDrain(14f);
                    pc.Stats.TakeMentalDamage(Personalization.WeaknessAxis.BoundaryConflict,
                        _ec.profile.mentalDamage * 0.5f);
                    GameEvents.RaiseSubtitle("被好人卡糊了一脸——「过度负责」附身，按「责任归还」（键3/还）清除！");
                }
            }
            _busy = false;
        }

        /// <summary>困人牢笼：在玩家四周立起好人卡之墙——责任归还立即打破，否则数秒后消散。</summary>
        void BuildCage()
        {
            if (_ec.dialogue != null) _ec.dialogue.Show("这么好的人，就该一直待在\"好人\"里。", 2.6f);
            GameEvents.RaiseSubtitle("好人牢笼升起——按「责任归还」（键3/还）打破，或等它自行消散。");
            GameAudio.Play(GameAudio.Sfx.HeavyHit, 0.7f);
            Vector3 c = _player.position;
            for (int i = 0; i < 4; i++)
            {
                float ang = i * Mathf.PI / 2f;
                Vector3 pos = c + new Vector3(Mathf.Cos(ang) * 2.4f, 1.1f, Mathf.Sin(ang) * 2.4f);
                CageWall.Spawn(pos, Quaternion.Euler(0, ang * Mathf.Rad2Deg + 90f, 0));
            }
        }
    }

    /// <summary>
    /// 无限代付者（无限代付走廊 Boss）：把你的时间、金钱、精力当成无限资源。
    /// - 索取冲击（核心机制）：预警后的大范围索取——**举盾格挡住它 = 明确拒绝成功，
    ///   Boss 当场大破绽**；没挡住则边界与关系被大量掏空；
    /// - 消耗账单：边界轴心念弹；
    /// - 代付之门：在玩家脚下开一扇吸取之门（站内持续流失意志与边界，快走开）。
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class InfinitePayerBoss : MonoBehaviour
    {
        public float demandInterval = 11f;
        public float demandRadius = 5.5f;
        public float billInterval = 6.5f;
        public float gateInterval = 13f;

        EnemyController _ec;
        Transform _player;
        PlayerCombatController _playerCombat;
        float _demandCd = 7f, _billCd = 3f, _gateCd = 10f;
        bool _busy;

        void Awake() => _ec = GetComponent<EnemyController>();

        void Start()
        {
            var p = FindObjectOfType<PlayerController>();
            if (p != null) { _player = p.transform; _playerCombat = p.GetComponent<PlayerCombatController>(); }
            GameEvents.RaiseSubtitle("『无限代付者』现身——它发动【索取冲击】时举盾格挡，明确拒绝 = 它当场大破绽！");
        }

        void Update()
        {
            if (_ec == null || _player == null || _busy) return;
            if (_ec.State == EnemyState.Dead || _ec.State == EnemyState.Stagger) return;
            float dist = Vector3.Distance(transform.position, _player.position);
            if (dist > 24f) return;

            float dt = Time.deltaTime;
            _demandCd -= dt; _billCd -= dt; _gateCd -= dt;

            if (_demandCd <= 0 && dist < demandRadius + 3f)
            {
                _demandCd = demandInterval;
                StartCoroutine(DemandBlast());
                return;
            }
            if (_gateCd <= 0)
            {
                _gateCd = gateInterval;
                StartCoroutine(OpenPayGate());
                return;
            }
            if (_billCd <= 0 && dist < 18f)
            {
                _billCd = billInterval;
                StartCoroutine(BillVolley());
            }
        }

        /// <summary>索取冲击：格挡住 = 明确拒绝成功 → Boss 大破绽；没挡住 = 边界被掏空。</summary>
        IEnumerator DemandBlast()
        {
            _busy = true;
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(ring.GetComponent<Collider>());
            ring.name = "DemandWarnRing";
            ring.transform.position = new Vector3(transform.position.x,
                transform.position.y - 0.9f, transform.position.z);
            ring.GetComponent<MeshRenderer>().sharedMaterial =
                CombatFeedback.EnergyMaterial(new Color(0.4f, 0.8f, 0.55f), 0.4f);
            if (_ec.dialogue != null) _ec.dialogue.Show("这次也先替我垫上——你不会拒绝的，对吧？", 2f);
            GameEvents.RaiseSubtitle("【索取冲击】来了——举盾（格挡键/触屏『挡』）明确拒绝！");
            GameAudio.Play(GameAudio.Sfx.Alert, 0.65f);

            float t = 0f;
            while (t < 1.1f)
            {
                t += Time.deltaTime;
                float k = 0.3f + 0.7f * (t / 1.1f);
                ring.transform.localScale = new Vector3(demandRadius * 2f * k, 0.03f, demandRadius * 2f * k);
                yield return null;
            }
            Destroy(ring);

            if (_ec.State != EnemyState.Dead && _player != null &&
                Vector3.Distance(_player.position, transform.position) <= demandRadius + 0.5f)
            {
                var pc = _player.GetComponent<PlayerController>();
                if (_playerCombat != null && _playerCombat.IsGuarding)
                {
                    // 明确拒绝成功：索取被边界盾整个挡回——Boss 大破绽
                    _ec.ForceBreak(3f);
                    var stats = pc != null ? pc.Stats : null;
                    if (stats != null)
                    {
                        stats.RestoreAxis(Personalization.WeaknessAxis.BoundaryConflict, 20f);
                        stats.ReduceRelationshipDrain(15f);
                    }
                    CombatFeedback.RecipeBurst(_player.position, new Color(0.4f, 0.85f, 0.6f));
                    GameAudio.Play(GameAudio.Sfx.Parry, 0.9f);
                    GameEvents.RaiseSubtitle("明确拒绝成功——「这次费用需要你自己承担。」无限代付者被顶了回去！【大破绽】");
                }
                else if (pc != null && !pc.IsInvincible && _playerCombat != null)
                {
                    _playerCombat.TakeHit(new DamageInfo
                    {
                        physicalDamage = _ec.profile.physicalDamage * 0.7f,
                        mentalDamage = _ec.profile.mentalDamage * 0.8f,
                        mentalAxis = Personalization.WeaknessAxis.BoundaryConflict,
                        knockback = 3f,
                        sourcePosition = transform.position,
                        attackerId = _ec.profile.enemyId
                    });
                    pc.Stats.AddRelationshipDrain(16f);
                    GameEvents.RaiseSubtitle("又默认代付了一次——下次它发动索取冲击时，举盾明确拒绝！");
                }
            }
            _busy = false;
        }

        IEnumerator BillVolley()
        {
            _busy = true;
            if (_ec.dialogue != null)
                _ec.dialogue.Show(Random.value < 0.5f ? "就这一次。" : "你以前都帮过别人的。", 2.2f);
            for (int i = 0; i < 2 && _ec.State != EnemyState.Dead; i++)
            {
                if (_player == null) break;
                Vector3 origin = transform.position + Vector3.up * 1.5f + transform.forward * 0.5f;
                Vector3 target = _player.position + Vector3.up * 1.0f;
                Projectile.Launch(transform, origin, target - origin, new DamageInfo
                {
                    physicalDamage = _ec.profile.physicalDamage * 0.35f,
                    mentalDamage = _ec.profile.mentalDamage * 0.5f,
                    mentalAxis = Personalization.WeaknessAxis.BoundaryConflict,
                    knockback = 0.6f,
                    attackerId = _ec.profile.enemyId
                }, 13f, new Color(0.5f, 0.7f, 0.55f), _ec.baseMaterial);
                GameAudio.Play(GameAudio.Sfx.Swing, 0.4f);
                yield return new WaitForSeconds(0.3f);
            }
            _busy = false;
        }

        /// <summary>代付之门：在玩家脚下开一扇吸取之门（站内持续流失，走开即止）。</summary>
        IEnumerator OpenPayGate()
        {
            Vector3 spot = _player.position;
            spot.y = transform.position.y - 0.95f;
            if (_ec.dialogue != null) _ec.dialogue.Show("门开了——你的资源，从来都是大家的。", 2f);
            GameAudio.Play(GameAudio.Sfx.Alert, 0.45f);
            yield return new WaitForSeconds(0.6f);
            if (_ec.State == EnemyState.Dead) yield break;
            PayDrainZone.Spawn(spot, 7f);
            GameEvents.RaiseSubtitle("脚下开了一扇「代付之门」——站在里面会被持续吸取，快离开！");
        }
    }
}
