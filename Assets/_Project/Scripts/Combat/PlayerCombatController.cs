using System.Collections;
using UnityEngine;
using AdversityRoad.Player;
using AdversityRoad.Core;
using AdversityRoad.Mobile;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 玩家战斗控制（借鉴黑神话悟空的战斗框架）：
    /// - 轻击五段连招（横斩→上挑→蹬踢→旋身扫→跃劈），带输入缓冲与前摇取消窗口；
    /// - 「意势」资源（0-3 点）：命中轻击、完美闪避、蓄力均可积攒，重击释放时消耗增伤，
    ///   满势时脚下亮起意志光环；
    /// - 重击按住蓄力（沉腰举械），松开释放：势≥2 触发旋风终结，否则蓄力重劈；
    ///   连段中轻点重击派生「切手技」快速上挑；
    /// - 完美闪避：在敌人攻击命中瞬间翻滚成功 → 短暂时缓 + 势+1 + 下一击必暴击；
    /// - 空中攻击=下劈坠击；蹲伏攻击=扫堂腿（高削韧）；
    /// - 格挡（边界盾）/ 精准格挡（定心格挡）化解心理攻击。
    /// </summary>
    [RequireComponent(typeof(CombatStateMachine))]
    public class PlayerCombatController : MonoBehaviour
    {
        [Header("连段")]
        public Hitbox weaponHitbox;
        public float baseDamage = 13f;
        public float staminaPerHit = 8f;
        public float comboResetTime = 1.1f;
        public float autoAimRange = 5f;

        [Header("重击 / 蓄力")]
        public float heavyDamage = 30f;
        public float heavyStamina = 22f;
        public float maxChargeTime = 1.2f;
        public float chargeStaminaPerSec = 9f;

        [Header("格挡 / 精准格挡")]
        public float guardMentalReduction = 0.7f;
        public float parryWindow = 0.2f;
        public float parryFocusRestore = 25f;

        [Header("倒地")]
        public float knockdownThreshold = 20f;

        [Header("状态可视化（运行时注入）")]
        public GameObject guardShield;
        public GameObject innerAura;   // 满势光环

        // 连段表：姿态 / 伤害倍率 / 削韧 / 突进 / 前摇 / 开框时长 / 段时长 / 可取消点
        struct ComboStage
        {
            public PoseState pose;
            public float dmg, posture, lunge, windup, open, length, cancelAt;
        }

        static readonly ComboStage[] Combo =
        {
            new ComboStage { pose = PoseState.Attack,     dmg = 1.0f, posture = 8,  lunge = 0.5f, windup = 0.10f, open = 0.22f, length = 0.42f, cancelAt = 0.28f },
            new ComboStage { pose = PoseState.AttackUp,   dmg = 1.1f, posture = 10, lunge = 0.4f, windup = 0.10f, open = 0.22f, length = 0.40f, cancelAt = 0.26f },
            new ComboStage { pose = PoseState.AttackKick, dmg = 1.2f, posture = 18, lunge = 0.7f, windup = 0.12f, open = 0.24f, length = 0.46f, cancelAt = 0.30f },
            new ComboStage { pose = PoseState.AttackSpin, dmg = 1.35f, posture = 14, lunge = 0.3f, windup = 0.10f, open = 0.34f, length = 0.52f, cancelAt = 0.36f },
            new ComboStage { pose = PoseState.AttackLeap, dmg = 1.8f, posture = 26, lunge = 0.9f, windup = 0.16f, open = 0.30f, length = 0.62f, cancelAt = 0.6f },
        };

        PlayerController _player;
        CombatStateMachine _fsm;
        CharacterController _cc;
        HumanoidAnimator _anim;

        int _stage = -1;               // 当前连段下标（-1 空闲）
        float _stageT;                 // 当前段计时
        bool _buffered;                // 输入缓冲
        float _lastAttackEnd;
        Coroutine _hitboxRoutine;

        int _momentum;                 // 意势 0-3
        bool _critNext;                // 完美闪避奖励
        float _lastPerfect;

        bool _charging;
        float _chargeT;
        float _chargeGained;

        float _parryTimer;

        public bool IsGuarding { get; private set; }
        public int Momentum => _momentum;

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _fsm = GetComponent<CombatStateMachine>();
            _cc = GetComponent<CharacterController>();
        }

        void Update()
        {
            if (_anim == null) _anim = GetComponent<HumanoidAnimator>();
            if (_player.Stats.IsDead) return;
            float dt = Time.deltaTime;
            if (_parryTimer > 0) _parryTimer -= dt;

            // 连段计时与收招
            if (_stage >= 0)
            {
                _stageT += dt;
                var s = Combo[_stage];
                if (_buffered && _stageT >= s.cancelAt) AdvanceCombo();
                else if (_stageT >= s.length) EndCombo();
            }
            else if (Time.time - _lastAttackEnd > comboResetTime)
            {
                _buffered = false;
            }

            // 格挡
            IsGuarding = Input.GetKey(KeyCode.LeftControl) || MobileInput.GetHeld("Guard");
            if (Input.GetKeyDown(KeyCode.LeftControl) || MobileInput.GetDown("Guard")) _parryTimer = parryWindow;
            if (guardShield != null && guardShield.activeSelf != (IsGuarding && !_fsm.IsActionLocked))
                guardShield.SetActive(IsGuarding && !_fsm.IsActionLocked);

            // 满势光环
            if (innerAura != null && innerAura.activeSelf != (_momentum >= 3))
                innerAura.SetActive(_momentum >= 3);

            // ---- 输入 ----
            bool desktop = !Application.isMobilePlatform;
            bool mouseOverUI = UnityEngine.EventSystems.EventSystem.current != null
                && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
            bool lightDown = (desktop && Input.GetMouseButtonDown(0) && !mouseOverUI) || MobileInput.GetDown("Light");
            bool heavyDown = (desktop && Input.GetMouseButtonDown(1)) || MobileInput.GetDown("Heavy");
            bool heavyHeld = (desktop && Input.GetMouseButton(1)) || MobileInput.GetHeld("Heavy");

            // ---- 蓄力重击 ----
            if (_charging)
            {
                _chargeT += dt;
                if (_player.Stats.SpendStamina(chargeStaminaPerSec * dt) == false || _chargeT >= maxChargeTime + 0.4f)
                {
                    ReleaseHeavy();
                }
                else
                {
                    // 蓄力积势：每 0.55 秒 +1
                    if (_chargeT - _chargeGained > 0.55f && _momentum < 3)
                    {
                        _chargeGained = _chargeT;
                        AddMomentum(1);
                    }
                    if (!heavyHeld) ReleaseHeavy();
                }
                return;
            }

            if (heavyDown)
            {
                // 连段中轻点重击 → 切手技（快速反手上挑）
                if (_stage >= 1 && _stageT >= 0.12f) { QiShou(); return; }
                if (!_fsm.IsActionLocked || _stage >= 0) StartCharge();
                return;
            }

            if (lightDown)
            {
                if (_stage >= 0) { _buffered = true; return; }
                if (_fsm.IsActionLocked) return;
                if (!_cc.isGrounded) { JumpAttack(); return; }
                if (_player.IsCrouched) { SweepAttack(); return; }
                StartCombo();
            }
        }

        // ================= 连段 =================

        void StartCombo()
        {
            _stage = -1;
            AdvanceCombo();
        }

        void AdvanceCombo()
        {
            _buffered = false;
            int next = Mathf.Min(_stage + 1, Combo.Length - 1);
            if (_stage == Combo.Length - 1) next = 0; // 终结后回首段
            var s = Combo[next];
            if (!_player.Stats.SpendStamina(staminaPerHit)) { EndCombo(); return; }

            _stage = next;
            _stageT = 0;
            _fsm.RequestState(CombatState.LightAttack, s.length);
            _fsm.InCombat = true;
            PlayPose(s.pose);
            FaceAndLunge(s.lunge);

            float dmg = baseDamage * s.dmg * CritMult();
            CombatFeedback.SwingArc(transform, next >= 3, new Color(0.45f, 0.75f, 1f));
            OpenHitboxTimed(s.windup, s.open, dmg, s.posture, next >= 2 ? 2f : 1f, true);
        }

        void EndCombo()
        {
            if (_stage >= 0) _lastAttackEnd = Time.time;
            _stage = -1;
            _buffered = false;
        }

        // ================= 重击 / 蓄力 / 切手 =================

        void StartCharge()
        {
            if (!_player.Stats.SpendStamina(6f)) return;
            EndCombo();
            _charging = true;
            _chargeT = 0;
            _chargeGained = 0;
            _fsm.RequestState(CombatState.HeavyAttack, maxChargeTime + 1.2f);
            _fsm.InCombat = true;
            PlayPose(PoseState.Charge);
        }

        void ReleaseHeavy()
        {
            _charging = false;
            float charge01 = Mathf.Clamp01(_chargeT / maxChargeTime);
            int spent = _momentum;
            SetMomentum(0);

            bool finisher = spent >= 2;
            var pose = finisher ? PoseState.AttackSpin : PoseState.HeavyAttack;
            float dur = finisher ? 0.55f : 0.65f;
            _fsm.RequestState(CombatState.HeavyAttack, dur);
            PlayPose(pose);
            FaceAndLunge(finisher ? 0.3f : 0.8f);

            float dmg = heavyDamage * (1f + 0.6f * charge01 + 0.45f * spent) * CritMult();
            CombatFeedback.SwingArc(transform, true,
                finisher ? new Color(1f, 0.8f, 0.3f) : new Color(1f, 0.6f, 0.3f));
            CombatFeedback.Shake(finisher ? 0.8f : 0.5f);
            OpenHitboxTimed(0.12f, finisher ? 0.4f : 0.3f, dmg, 24f + 10f * spent, 3.5f, false);
            if (finisher) GameEvents.RaiseSubtitle("旋风终结！消耗 " + spent + " 点意势");
        }

        /// <summary>切手技：连段中轻点重击派生的快速反击。</summary>
        void QiShou()
        {
            EndCombo();
            _fsm.RequestState(CombatState.HeavyAttack, 0.42f);
            PlayPose(PoseState.AttackUp);
            FaceAndLunge(0.5f);
            float dmg = heavyDamage * 0.85f * CritMult();
            CombatFeedback.SwingArc(transform, true, new Color(0.7f, 0.9f, 1f));
            OpenHitboxTimed(0.08f, 0.24f, dmg, 20f, 2.5f, false);
        }

        // ================= 空中 / 蹲伏攻击 =================

        void JumpAttack()
        {
            _fsm.RequestState(CombatState.LightAttack, 0.55f);
            _fsm.InCombat = true;
            PlayPose(PoseState.JumpAttack);
            _player.ForceFall(-13f);
            float dmg = baseDamage * 1.5f * CritMult();
            CombatFeedback.SwingArc(transform, true, new Color(0.6f, 0.8f, 1f));
            OpenHitboxTimed(0.14f, 0.42f, dmg, 22f, 2.5f, true);
        }

        void SweepAttack()
        {
            _fsm.RequestState(CombatState.LightAttack, 0.55f);
            _fsm.InCombat = true;
            PlayPose(PoseState.Sweep);
            float dmg = baseDamage * 0.9f * CritMult();
            OpenHitboxTimed(0.16f, 0.32f, dmg, 30f, 1.5f, true);
        }

        // ================= 公共机制 =================

        float CritMult()
        {
            if (!_critNext) return 1f;
            _critNext = false;
            return 1.7f;
        }

        void PlayPose(PoseState p)
        {
            if (_anim == null) _anim = GetComponent<HumanoidAnimator>();
            if (_anim != null) _anim.PlayAttackPose(p);
        }

        void FaceAndLunge(float lunge)
        {
            var target = AutoAimTarget();
            if (target != null)
            {
                Vector3 dir = target.position - transform.position;
                dir.y = 0;
                if (dir.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(dir);
            }
            if (target == null || Vector3.Distance(transform.position, target.position) > 1.3f)
                _cc.Move(transform.forward * lunge);
        }

        void OpenHitboxTimed(float windup, float open, float dmg, float posture, float knockback, bool buildMomentum)
        {
            if (weaponHitbox == null) return;
            if (_hitboxRoutine != null) StopCoroutine(_hitboxRoutine);
            _hitboxRoutine = StartCoroutine(HitboxWindow(windup, open, dmg, posture, knockback, buildMomentum));
        }

        IEnumerator HitboxWindow(float windup, float open, float dmg, float posture, float knockback, bool buildMomentum)
        {
            yield return new WaitForSeconds(windup);
            weaponHitbox.onHit = h =>
            {
                if (buildMomentum) AddMomentum(1);   // 命中积势（悟空棍势逻辑）
                CombatFeedback.HitStop(0.03f);
            };
            weaponHitbox.EnableHitbox(new DamageInfo
            {
                physicalDamage = dmg,
                postureDamage = posture,
                knockback = knockback,
                attackerId = "player"
            });
            yield return new WaitForSeconds(open);
            weaponHitbox.DisableHitbox();
            weaponHitbox.onHit = null;
        }

        void AddMomentum(int n) => SetMomentum(_momentum + n);

        void SetMomentum(int v)
        {
            v = Mathf.Clamp(v, 0, 3);
            if (v == _momentum) return;
            _momentum = v;
            GameEvents.RaiseMomentumChanged(_momentum);
        }

        /// <summary>最近的存活敌人（普攻转向与远程技能瞄准共用）。</summary>
        public Transform AutoAimTarget()
        {
            var enemies = FindObjectsOfType<AI.EnemyController>();
            Transform best = null;
            float bestDist = Mathf.Max(autoAimRange, 14f);
            foreach (var e in enemies)
            {
                if (e.State == AI.EnemyState.Dead) continue;
                float d = Vector3.Distance(transform.position, e.transform.position);
                if (d < bestDist) { bestDist = d; best = e.transform; }
            }
            return best;
        }

        // ================= 受击 =================

        public void TakeHit(DamageInfo dmg)
        {
            if (_player.IsInvincible)
            {
                // 完美闪避：在攻击命中的瞬间翻滚成功
                if (_player.IsDodging && dmg.physicalDamage > 0 && Time.time - _lastPerfect > 1f)
                {
                    _lastPerfect = Time.time;
                    _critNext = true;
                    AddMomentum(1);
                    CombatFeedback.SlowMo(0.3f, 0.35f);
                    GameEvents.RaiseSubtitle("完美闪避！意势+1，下一击必暴击");
                }
                return;
            }

            // 心理伤害
            if (dmg.mentalDamage > 0)
            {
                float mult = GameManager.Instance != null && GameManager.Instance.safety != null
                    ? GameManager.Instance.safety.MentalDamageMultiplier() : 1f;
                float mental = dmg.mentalDamage * mult;

                if (_parryTimer > 0)
                {
                    _player.Stats.focus = Mathf.Min(_player.Stats.maxFocus,
                        _player.Stats.focus + parryFocusRestore);
                    GameEvents.RaiseMentalStatChanged("focus", _player.Stats.focus, _player.Stats.maxFocus);
                    GameEvents.RaiseSubtitle("定心格挡！心理攻击被化解，专注恢复。");
                }
                else
                {
                    if (IsGuarding) mental *= (1f - guardMentalReduction);
                    bool staggered = _player.Stats.TakeMentalDamage(dmg.mentalAxis, mental);
                    if (staggered) _fsm.TriggerMentalStagger();
                }
            }

            // 物理伤害
            if (dmg.physicalDamage > 0)
            {
                float phys = dmg.physicalDamage;
                if (IsGuarding && _player.Stats.SpendStamina(phys * 0.5f)) phys *= 0.2f;
                _player.Stats.TakePhysicalDamage(phys);

                CombatFeedback.HitFlash(gameObject);
                CombatFeedback.DamageNumber(transform.position, Mathf.RoundToInt(phys).ToString(),
                    new Color(1f, 0.35f, 0.3f));
                CombatFeedback.Shake(phys >= knockdownThreshold ? 1.0f : 0.4f);

                _charging = false;
                EndCombo();

                if (_player.Stats.IsDead)
                {
                    _fsm.RequestState(CombatState.Death);
                }
                else if (!dmg.isMentalOnly)
                {
                    if (phys >= knockdownThreshold)
                    {
                        _fsm.RequestState(CombatState.Knockdown, 1.4f);
                        _player.SetInvincible(1.8f);
                        CombatFeedback.HitStop(0.06f);
                    }
                    else
                    {
                        _fsm.RequestState(CombatState.HitReaction, 0.4f);
                    }
                }
            }
        }
    }
}
