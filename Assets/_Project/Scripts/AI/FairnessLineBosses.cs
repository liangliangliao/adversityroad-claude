using System.Collections;
using UnityEngine;
using AdversityRoad.Combat;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.AI
{
    /// <summary>
    /// 两元赖账王（两元赌桌 Boss）：
    /// - 硬币弹幕：把"才这点钱"扔到你脸上（公平刺痛轴心念弹）；
    /// - 耍赖回血：拉开距离就开始搓牌回血——贴身施压别让它喘；
    /// - 账本对质（核心机制）：走近桌上的账本即"对质事实"——它当场语塞进入长破绽。
    /// 核心不是金额，而是承诺。
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class GambleKingBoss : MonoBehaviour
    {
        public float coinInterval = 6f;
        public float cheatHealInterval = 9f;
        public float summonInterval = 18f;

        EnemyController _ec;
        Transform _player;
        float _coinCd = 3f, _healCd = 6f, _summonCd = 10f;
        bool _busy;

        void Awake() => _ec = GetComponent<EnemyController>();

        void Start()
        {
            var p = FindObjectOfType<PlayerController>();
            if (p != null) _player = p.transform;
            GameEvents.RaiseSubtitle("『两元赖账王』上桌——桌上的账本记着事实，走近「账本对质」可令它语塞破绽。");
        }

        void Update()
        {
            if (_ec == null || _player == null || _busy) return;
            if (_ec.State == EnemyState.Dead || _ec.State == EnemyState.Stagger) return;
            float dist = Vector3.Distance(transform.position, _player.position);
            if (dist > 24f) return;

            float dt = Time.deltaTime;
            _coinCd -= dt; _healCd -= dt; _summonCd -= dt;

            // 耍赖回血：玩家离得远（>7m）时搓牌回血——逼你贴身施压
            if (_healCd <= 0 && dist > 7f)
            {
                _healCd = cheatHealInterval;
                StartCoroutine(CheatHeal());
                return;
            }
            if (_summonCd <= 0)
            {
                _summonCd = summonInterval;
                SummonOnlooker();
            }
            if (_coinCd <= 0 && dist < 18f)
            {
                _coinCd = coinInterval;
                StartCoroutine(CoinVolley());
            }
        }

        IEnumerator CoinVolley()
        {
            _busy = true;
            if (_ec.dialogue != null)
                _ec.dialogue.Show(Random.value < 0.5f ? "才这点钱，你也计较？" : "我又不是不还！", 2.2f);
            for (int i = 0; i < 3 && _ec.State != EnemyState.Dead; i++)
            {
                if (_player == null) break;
                Vector3 origin = transform.position + Vector3.up * 1.4f + transform.forward * 0.5f;
                Vector3 target = _player.position + Vector3.up * 1.0f;
                Projectile.Launch(transform, origin, target - origin, new DamageInfo
                {
                    physicalDamage = _ec.profile.physicalDamage * 0.35f,
                    mentalDamage = _ec.profile.mentalDamage * 0.5f,
                    mentalAxis = Personalization.WeaknessAxis.FairnessSensitivity,
                    knockback = 0.6f,
                    attackerId = _ec.profile.enemyId
                }, 14f, new Color(0.9f, 0.75f, 0.3f), _ec.baseMaterial);
                GameAudio.Play(GameAudio.Sfx.Swing, 0.4f);
                yield return new WaitForSeconds(0.22f);
            }
            _busy = false;
        }

        /// <summary>耍赖回血：搓牌重开一局——回 6% 血。贴身打断（任何受击都会停）。</summary>
        IEnumerator CheatHeal()
        {
            _busy = true;
            if (_ec.dialogue != null) _ec.dialogue.Show("重新洗牌，这局不算——", 2f);
            GameEvents.RaiseSubtitle("『两元赖账王』在耍赖回血——贴身施压，或走近账本对质打断它！");
            GameAudio.Play(GameAudio.Sfx.Alert, 0.4f);
            float hpBefore = _ec.HpRatio;
            yield return new WaitForSeconds(1.4f);
            // 期间被打（血量下降）则耍赖失败
            if (_ec.State != EnemyState.Dead && _ec.State != EnemyState.Stagger &&
                _ec.HpRatio >= hpBefore - 0.001f)
            {
                _ec.HealFraction(0.06f);
                CombatFeedback.HitSpark(transform.position + Vector3.up * 1.4f,
                    new Color(0.9f, 0.8f, 0.4f), 5);
            }
            _busy = false;
        }

        void SummonOnlooker()
        {
            if (EnemySpawnHook.AliveCount("enemy_mocking_bystander") >= 1) return;
            Vector3 pos = transform.position + transform.right * (Random.value < 0.5f ? 5f : -5f);
            if (EnemySpawnHook.SpawnNear(EnemyType.MockingBystander, EnemyTier.Novice, pos) == null) return;
            if (_ec.dialogue != null) _ec.dialogue.Show("大家评评理，是不是他小气！", 2.4f);
            GameEvents.RaiseSubtitle("旁观者围了上来——嘲笑不是事实，账本才是。");
        }
    }

    /// <summary>
    /// 新车债王（债务车影 Boss）：
    /// - 欠条护体：被"未结清的故事"保护着，常规伤害大减——收集场中三张欠条残片即碎；
    /// - 车灯眩光：周期性大范围强光冲击（预警后爆发，翻滚/跑出可躲，专注伤害）；
    /// - 召唤欠款残影：从车后拉出未结清之事的具象（场上限 2）。
    /// 事实是，这件事还没有结清——但它不该占据你生命的中心。
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class DebtCarKingBoss : MonoBehaviour
    {
        public float glareInterval = 10f;
        public float glareRadius = 7f;
        public float summonInterval = 13f;
        [Range(0f, 1f)] public float shieldedDamageMult = 0.25f;

        EnemyController _ec;
        Transform _player;
        PlayerCombatController _playerCombat;
        float _glareCd = 6f, _summonCd = 7f, _hintAt = -99f;
        bool _busy, _shieldBroken;

        void Awake() => _ec = GetComponent<EnemyController>();

        void Start()
        {
            var p = FindObjectOfType<PlayerController>();
            if (p != null) { _player = p.transform; _playerCombat = p.GetComponent<PlayerCombatController>(); }
            _ec.externalDamageMult = shieldedDamageMult;
            GameEvents.RaiseSubtitle("『新车债王』被「未结清的故事」护体——收集场中三张发光的欠条残片，护体即碎！");
        }

        void Update()
        {
            if (_ec == null || _ec.State == EnemyState.Dead) return;

            // 欠条集齐 → 护体崩碎 + 长破绽
            if (!_shieldBroken && DebtState.NotesCollected >= DebtState.NotesNeeded)
            {
                _shieldBroken = true;
                _ec.externalDamageMult = 1f;
                _ec.ForceBreak(3.2f);
                CombatFeedback.Debris(transform.position + Vector3.up, new Color(0.85f, 0.8f, 0.6f), 9);
                GameAudio.Play(GameAudio.Sfx.HeavyHit, 1f);
                if (_ec.dialogue != null) _ec.dialogue.Show("欠条……怎么会都在你手里！", 3f);
                GameEvents.RaiseSubtitle("三张欠条对上了账——「未结清的故事」护体崩碎，全力猛攻！");
            }

            if (_player == null || _busy || _ec.State == EnemyState.Stagger) return;
            float dist = Vector3.Distance(transform.position, _player.position);
            if (dist > 26f) return;

            if (!_shieldBroken && Time.time - _hintAt > 20f && dist < 18f)
            {
                _hintAt = Time.time;
                GameEvents.RaiseSubtitle("护体中（欠条 " + DebtState.NotesCollected + "/" +
                    DebtState.NotesNeeded + "）——先去捡场中发光的欠条残片。");
            }

            float dt = Time.deltaTime;
            _glareCd -= dt; _summonCd -= dt;

            if (_glareCd <= 0 && dist < glareRadius + 4f)
            {
                _glareCd = glareInterval;
                StartCoroutine(HeadlightGlare());
                return;
            }
            if (_summonCd <= 0)
            {
                _summonCd = summonInterval;
                SummonDebtShadow();
            }
        }

        /// <summary>车灯眩光：以 Boss 为中心的大范围强光冲击（预警圈内翻滚/跑出可躲）。</summary>
        IEnumerator HeadlightGlare()
        {
            _busy = true;
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(ring.GetComponent<Collider>());
            ring.name = "GlareWarnRing";
            ring.transform.position = new Vector3(transform.position.x,
                transform.position.y - 0.9f, transform.position.z);
            ring.GetComponent<MeshRenderer>().sharedMaterial =
                CombatFeedback.EnergyMaterial(new Color(1f, 0.95f, 0.6f), 0.4f);
            if (_ec.dialogue != null) _ec.dialogue.Show("看清楚——这可是新车的灯！", 1.6f);
            GameAudio.Play(GameAudio.Sfx.Alert, 0.6f);

            float t = 0f;
            while (t < 1.0f)
            {
                t += Time.deltaTime;
                float k = 0.3f + 0.7f * t;
                ring.transform.localScale = new Vector3(glareRadius * 2f * k, 0.03f, glareRadius * 2f * k);
                yield return null;
            }
            Destroy(ring);

            if (_ec.State != EnemyState.Dead && _player != null)
            {
                CombatFeedback.Shake(0.5f);
                CombatFeedback.RecipeBurst(transform.position, new Color(1f, 0.95f, 0.7f));
                GameAudio.Play(GameAudio.Sfx.HeavyHit, 0.8f);
                var pc = _player.GetComponent<PlayerController>();
                if (Vector3.Distance(_player.position, transform.position) <= glareRadius + 0.5f &&
                    _playerCombat != null && pc != null && !pc.IsInvincible)
                    _playerCombat.TakeHit(new DamageInfo
                    {
                        physicalDamage = _ec.profile.physicalDamage * 0.8f,
                        mentalDamage = _ec.profile.mentalDamage * 0.6f,
                        mentalAxis = Personalization.WeaknessAxis.NoiseSensitivity,
                        knockback = 3.5f,
                        sourcePosition = transform.position,
                        attackerId = _ec.profile.enemyId
                    });
            }
            _busy = false;
        }

        void SummonDebtShadow()
        {
            if (EnemySpawnHook.AliveCount("enemy_debt_shadow") >= 2) return;
            Vector3 pos = transform.position +
                Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward * 5f;
            if (EnemySpawnHook.SpawnNear(EnemyType.DebtShadow, EnemyTier.Novice, pos) == null) return;
            CombatFeedback.Debris(pos, new Color(0.35f, 0.32f, 0.4f), 5);
            if (_ec.dialogue != null) _ec.dialogue.Show("那些没结清的事，都还记得你。", 2.4f);
            GameAudio.Play(GameAudio.Sfx.Alert, 0.45f);
        }
    }
}
