using System.Collections;
using UnityEngine;
using AdversityRoad.Player;
using AdversityRoad.Core;
using AdversityRoad.Mobile;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 玩家战斗控制（黑神话悟空框架 × KOF 拳脚体系）：
    /// - 拳/腿双系普攻（KOF 拳脚分立）：攻=兵器/拳系，腿=脚技系，两系可互相
    ///   衔接组成连段路线（拳拳腿、腿腿拳、拳腿拳……），带输入缓冲与取消窗口；
    ///   拳系伤害高，腿系削韧强；
    /// - 指令技（KOF 方向+按键）：前+重=突进肘击，后+重=吹飞踢（大击退，KOF CD 吹飞）；
    /// - 「意势」资源（0-3）：命中/完美闪避/蓄力积攒；
    /// - 重击按住蓄力，松开释放：势=3 触发超必杀「觉醒·乱舞」（KOF 超杀式连续技），
    ///   势=2 旋风终结；连段中轻点重=切手技；
    /// - 完美闪避（时缓+返势+必暴击）、空中下劈、蹲伏扫堂腿；
    /// - 受身（KOF 受身）：被击倒瞬间按闪避快速翻身起立带无敌帧。
    /// </summary>
    [RequireComponent(typeof(CombatStateMachine))]
    public class PlayerCombatController : MonoBehaviour
    {
        [Header("连段")]
        public Hitbox weaponHitbox;
        public float baseDamage = 16f;
        public float staminaPerHit = 8f;
        public float comboResetTime = 1.1f;
        public float autoAimRange = 5f;

        [Header("重击 / 蓄力 / 指令技")]
        public float heavyDamage = 34f;
        public float maxChargeTime = 1.2f;
        public float chargeStaminaPerSec = 9f;
        public float tapThreshold = 0.18f;   // 轻点/长按分界

        [Header("格挡 / 精准格挡")]
        public float guardMentalReduction = 0.7f;
        public float parryWindow = 0.2f;
        public float parryFocusRestore = 25f;

        [Header("倒地")]
        public float knockdownThreshold = 20f;

        [Header("状态可视化（运行时注入）")]
        public GameObject guardShield;
        public GameObject innerAura;

        struct ComboStage
        {
            public PoseState pose;
            public float dmg, posture, lunge, windup, open, length, cancelAt;
        }

        // 拳键=剑式套路（持械时耍剑）：横斩→上撩→弓步突刺→腾空跃劈，伤害高
        static readonly ComboStage[] PunchChain =
        {
            new ComboStage { pose = PoseState.Attack,      dmg = 1.0f,  posture = 8,  lunge = 0.5f,  windup = 0.09f, open = 0.20f, length = 0.36f, cancelAt = 0.22f },
            new ComboStage { pose = PoseState.AttackUp,    dmg = 1.1f,  posture = 10, lunge = 0.4f,  windup = 0.09f, open = 0.20f, length = 0.36f, cancelAt = 0.22f },
            new ComboStage { pose = PoseState.SwordThrust, dmg = 1.3f,  posture = 12, lunge = 0.9f,  windup = 0.08f, open = 0.22f, length = 0.4f,  cancelAt = 0.26f },
            new ComboStage { pose = PoseState.AttackLeap,  dmg = 1.85f, posture = 24, lunge = 0.9f,  windup = 0.16f, open = 0.30f, length = 0.62f, cancelAt = 0.6f },
        };

        // 腿系（脚技）：削韧强
        static readonly ComboStage[] KickChain =
        {
            new ComboStage { pose = PoseState.AttackKick, dmg = 0.9f,  posture = 18, lunge = 0.7f, windup = 0.12f, open = 0.24f, length = 0.46f, cancelAt = 0.30f },
            new ComboStage { pose = PoseState.SideKick,   dmg = 1.0f,  posture = 24, lunge = 0.5f, windup = 0.12f, open = 0.24f, length = 0.46f, cancelAt = 0.30f },
            new ComboStage { pose = PoseState.SpinKick,   dmg = 1.2f,  posture = 30, lunge = 0.4f, windup = 0.12f, open = 0.32f, length = 0.54f, cancelAt = 0.38f },
            new ComboStage { pose = PoseState.SpinKick,   dmg = 1.5f,  posture = 40, lunge = 0.6f, windup = 0.14f, open = 0.32f, length = 0.58f, cancelAt = 0.56f },
        };

        enum AttackBtn { None, Punch, Kick }

        // 组合技配方（e：武术技能不是一键生成，而是玩家打出来的组合）
        struct Recipe
        {
            public string seq;    // P=拳 K=腿
            public string name;
            public float mult;
            public int cost;      // 释放绝招所需意势（越复杂越强，需能量积累）
        }

        // 复杂度越高伤害越强、消耗意势越多——大绝招不可无限使用
        static readonly Recipe[] Recipes =
        {
            new Recipe { seq = "PPKK", name = "双龙出海", mult = 2.6f, cost = 2 },
            new Recipe { seq = "PKPK", name = "拳腿相济", mult = 2.5f, cost = 2 },
            new Recipe { seq = "KKPP", name = "踏山贯拳", mult = 2.5f, cost = 2 },
            new Recipe { seq = "PPP",  name = "三连崩拳", mult = 1.6f, cost = 0 },
            new Recipe { seq = "KKK",  name = "连环三腿", mult = 1.65f, cost = 0 },
            new Recipe { seq = "PPK",  name = "崩拳扫腿", mult = 1.5f, cost = 0 },
            new Recipe { seq = "KKP",  name = "连腿贯拳", mult = 1.5f, cost = 0 },
        };

        PlayerController _player;
        CombatStateMachine _fsm;
        CharacterController _cc;
        HumanoidAnimator _anim;

        int _depth = -1;              // 连段深度（-1 空闲）
        float _stageT;
        ComboStage _cur;
        string _seq = "";             // 本次连段的拳腿序列（组合技识别）
        AttackBtn _buffered = AttackBtn.None;
        float _lastAttackEnd;
        Coroutine _hitboxRoutine;
        Coroutine _ranwuRoutine;

        int _momentum;
        bool _critNext;
        float _lastPerfect;

        bool _charging;
        float _chargeT;
        float _chargeGained;
        float _chargeFxAt;
        float _heavyDirFwd, _heavyDirSide;   // 按下重键时的八向意图
        float _specialCd;                     // 指令技共享冷却（大招不能无限使用）

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
            if (_specialCd > 0) _specialCd -= dt;

            // 受身：被击倒瞬间按闪避快速翻身（KOF 受身）
            if (_fsm.Current == CombatState.Knockdown &&
                (Input.GetKeyDown(KeyCode.LeftShift) || MobileInput.GetDown("Dodge")))
            {
                _fsm.RequestState(CombatState.Locomotion);
                _player.SetInvincible(0.6f);
                GameEvents.RaiseSubtitle("受身！");
                return;
            }

            // 连段推进
            if (_depth >= 0)
            {
                _stageT += dt;
                if (_buffered != AttackBtn.None && _stageT >= _cur.cancelAt)
                    NextStage(_buffered);
                else if (_stageT >= _cur.length) EndCombo();
            }

            // 格挡
            IsGuarding = Input.GetKey(KeyCode.LeftControl) || MobileInput.GetHeld("Guard");
            if (Input.GetKeyDown(KeyCode.LeftControl) || MobileInput.GetDown("Guard")) _parryTimer = parryWindow;
            if (guardShield != null && guardShield.activeSelf != (IsGuarding && !_fsm.IsActionLocked))
                guardShield.SetActive(IsGuarding && !_fsm.IsActionLocked);
            if (innerAura != null && innerAura.activeSelf != (_momentum >= 3))
                innerAura.SetActive(_momentum >= 3);

            // ---- 输入 ----
            bool desktop = !Application.isMobilePlatform;
            bool mouseOverUI = UnityEngine.EventSystems.EventSystem.current != null
                && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
            bool punchDown = (desktop && Input.GetMouseButtonDown(0) && !mouseOverUI) || MobileInput.GetDown("Light");
            bool kickDown = (desktop && Input.GetKeyDown(KeyCode.E)) || MobileInput.GetDown("Kick");
            bool heavyDown = (desktop && Input.GetMouseButtonDown(1)) || MobileInput.GetDown("Heavy");
            bool heavyHeld = (desktop && Input.GetMouseButton(1)) || MobileInput.GetHeld("Heavy");

            // ---- 蓄力 / 指令技 ----
            if (_charging)
            {
                _chargeT += dt;
                if (!_player.Stats.SpendStamina(chargeStaminaPerSec * dt) || _chargeT >= maxChargeTime + 0.4f)
                {
                    ReleaseHeavy();
                }
                else
                {
                    if (_chargeT - _chargeGained > 0.55f && _momentum < 3)
                    {
                        _chargeGained = _chargeT;
                        AddMomentum(1);
                    }
                    // 蓄力可见化：脚下周期性金色能量火花
                    if (_chargeT > 0.25f && Time.time > _chargeFxAt)
                    {
                        _chargeFxAt = Time.time + 0.28f;
                        CombatFeedback.HitSpark(transform.position - Vector3.up * 0.6f,
                            new Color(1f, 0.85f, 0.35f), 4);
                    }
                    if (!heavyHeld) ReleaseHeavy();
                }
                return;
            }

            if (heavyDown)
            {
                if (_depth >= 1 && _stageT >= 0.12f) { QiShou(); return; }
                MoveIntent(out _heavyDirFwd, out _heavyDirSide);
                StartCharge();
                return;
            }

            AttackBtn pressed = punchDown ? AttackBtn.Punch : kickDown ? AttackBtn.Kick : AttackBtn.None;
            if (pressed != AttackBtn.None)
            {
                if (_depth >= 0) { _buffered = pressed; return; }
                if (_fsm.IsActionLocked) return;
                if (!_cc.isGrounded)
                {
                    // 跳跃派生：跳+拳=下劈坠击，跳+腿=飞踢
                    if (pressed == AttackBtn.Kick) JumpKickAttack(); else JumpAttack();
                    return;
                }
                if (_player.IsCrouched) { SweepAttack(); return; }
                _depth = -1;
                _seq = "";
                NextStage(pressed);
            }
        }

        /// <summary>移动输入相对角色朝向的前后/左右分量（八向指令技判定）。</summary>
        void MoveIntent(out float fwd, out float side)
        {
            fwd = 0; side = 0;
            Vector2 mv = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"))
                         + MobileInput.Move;
            if (mv.sqrMagnitude < 0.09f) return;
            Transform cam = Camera.main != null ? Camera.main.transform : null;
            Vector3 dir;
            if (cam != null)
            {
                Vector3 f = cam.forward; f.y = 0; f.Normalize();
                Vector3 r = cam.right; r.y = 0; r.Normalize();
                dir = (f * mv.y + r * mv.x).normalized;
            }
            else dir = new Vector3(mv.x, 0, mv.y).normalized;
            fwd = Vector3.Dot(dir, transform.forward);
            side = Vector3.Dot(dir, transform.right);
        }

        // ================= 拳/腿连段 =================

        void NextStage(AttackBtn btn)
        {
            _buffered = AttackBtn.None;
            int nextDepth = _depth + 1;
            if (nextDepth > 3) nextDepth = 0;
            var chain = btn == AttackBtn.Kick ? KickChain : PunchChain;
            var s = chain[nextDepth];
            if (!_player.Stats.SpendStamina(staminaPerHit)) { EndCombo(); return; }

            _depth = nextDepth;
            _cur = s;
            _stageT = 0;
            _seq += btn == AttackBtn.Kick ? "K" : "P";
            RaiseSeq();
            _fsm.RequestState(CombatState.LightAttack, s.length);
            _fsm.InCombat = true;
            PlayPose(s.pose);
            FaceAndLunge(s.lunge);

            float dmg = baseDamage * s.dmg * CritMult();

            // 组合技识别：打出配方即触发「招式」——冲击波+时缓+大增伤+击飞
            // 高级绝招（cost>0）需消耗意势能量；能量不足则退化为普通连段
            bool recipeHit = false;
            foreach (var r in Recipes)
            {
                if (_seq.EndsWith(r.seq))
                {
                    if (r.cost > 0 && !TrySpendMomentum(r.cost))
                    {
                        GameEvents.RaiseSubtitle("意势不足，「" + r.name + "」未能成招（需 " + r.cost + " 势）");
                        break;
                    }
                    dmg *= r.mult;
                    recipeHit = true;
                    GameEvents.RaiseSkillBanner("绝招「" + r.name + "」");
                    CombatFeedback.RecipeBurst(transform.position, new Color(1f, 0.85f, 0.3f));
                    if (r.cost > 0) CombatFeedback.SlowMo(0.4f, 0.18f);
                    break;
                }
            }

            CombatFeedback.SwingArc(transform, nextDepth >= 2 || recipeHit,
                recipeHit ? new Color(1f, 0.85f, 0.3f)
                : btn == AttackBtn.Kick ? new Color(1f, 0.65f, 0.4f) : new Color(0.45f, 0.75f, 1f));
            OpenHitboxTimed(s.windup, s.open, dmg, s.posture,
                (nextDepth >= 2 ? 2f : 1f) + (recipeHit ? 5f : 0f), true);
        }

        void RaiseSeq()
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in _seq)
            {
                if (sb.Length > 0) sb.Append('·');
                sb.Append(c == 'K' ? '腿' : '拳');
            }
            GameEvents.RaiseComboSeq(sb.ToString());
        }

        void EndCombo()
        {
            if (_depth >= 0) _lastAttackEnd = Time.time;
            _depth = -1;
            _seq = "";
            _buffered = AttackBtn.None;
            GameEvents.RaiseComboSeq("");
        }

        // ================= 重击 / 蓄力 / 指令技 / 超必杀 =================

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

            // 轻点=八向指令技（斜向就近归并）：前=疾影突 后=吹飞踢 左/右=侧旋斩
            // 指令技共享 3.5s 冷却：大招不能无限制连发
            if (_chargeT < tapThreshold && _specialCd <= 0)
            {
                float af = Mathf.Abs(_heavyDirFwd), asd = Mathf.Abs(_heavyDirSide);
                if (Mathf.Max(af, asd) > 0.35f)
                {
                    _specialCd = 3.5f;
                    if (af >= asd)
                    {
                        if (_heavyDirFwd > 0) { DashStrike(); return; }
                        BlowbackKick(); return;
                    }
                    SideSpinStrike(_heavyDirSide > 0); return;
                }
            }

            float charge01 = Mathf.Clamp01(_chargeT / maxChargeTime);
            int spent = _momentum;
            SetMomentum(0);

            if (spent >= 3) { StartRanWu(charge01); return; }   // 超必杀

            bool finisher = spent >= 2;
            _fsm.RequestState(CombatState.HeavyAttack, finisher ? 0.55f : 0.65f);
            PlayPose(finisher ? PoseState.AttackSpin : PoseState.HeavyAttack);
            FaceAndLunge(finisher ? 0.3f : 0.8f);

            float dmg = heavyDamage * (1f + 0.6f * charge01 + 0.45f * spent) * CritMult();
            CombatFeedback.SwingArc(transform, true,
                finisher ? new Color(1f, 0.8f, 0.3f) : new Color(1f, 0.6f, 0.3f));
            CombatFeedback.Shake(finisher ? 0.8f : 0.5f);
            // 满蓄力落地带地裂冲击环
            if (charge01 > 0.7f || finisher)
                CombatFeedback.RecipeBurst(transform.position,
                    finisher ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 0.55f, 0.2f));
            OpenHitboxTimed(0.12f, finisher ? 0.4f : 0.3f, dmg, 24f + 10f * spent, 3.5f, false);
            if (finisher) GameEvents.RaiseSkillBanner("「旋风终结」");
            else if (charge01 > 0.7f) GameEvents.RaiseSkillBanner("「蓄力重劈」");
        }

        /// <summary>前+重：突进刺（疾影突）——高速突进直刺，双重剑气。</summary>
        void DashStrike()
        {
            _fsm.RequestState(CombatState.HeavyAttack, 0.5f);
            PlayPose(PoseState.SwordThrust);
            FaceAndLunge(2.6f);
            float dmg = heavyDamage * 0.85f * CritMult();
            CombatFeedback.SwingArc(transform, true, new Color(0.9f, 0.95f, 0.6f));
            CombatFeedback.SwingArc(transform, false, new Color(0.7f, 0.85f, 1f));
            CombatFeedback.HitSpark(transform.position + transform.forward * 1.2f,
                new Color(0.9f, 0.95f, 0.6f), 5);
            CombatFeedback.Shake(0.4f);
            OpenHitboxTimed(0.08f, 0.3f, dmg, 16f, 2f, false);
            GameEvents.RaiseSkillBanner("「疾影突」");
        }

        /// <summary>后+重：吹飞踢（KOF CD 吹飞攻击，大击退拉开身位）。</summary>
        void BlowbackKick()
        {
            _fsm.RequestState(CombatState.HeavyAttack, 0.55f);
            PlayPose(PoseState.SideKick);
            FaceAndLunge(0.4f);
            float dmg = heavyDamage * 0.7f * CritMult();
            CombatFeedback.RecipeBurst(transform.position, new Color(1f, 0.5f, 0.25f));
            OpenHitboxTimed(0.1f, 0.3f, dmg, 34f, 9f, false);
            GameEvents.RaiseSkillBanner("「吹飞踢」");
        }

        /// <summary>左/右+重：侧旋斩——侧步位移接旋身横扫（八向指令技）。</summary>
        void SideSpinStrike(bool right)
        {
            _fsm.RequestState(CombatState.HeavyAttack, 0.55f);
            PlayPose(PoseState.AttackSpin);
            Vector3 lateral = (right ? transform.right : -transform.right) * 1.7f
                              + transform.forward * 0.4f;
            _cc.Move(lateral);
            var target = AutoAimTarget();
            if (target != null)
            {
                Vector3 dir = target.position - transform.position;
                dir.y = 0;
                if (dir.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(dir);
            }
            float dmg = heavyDamage * 0.75f * CritMult();
            CombatFeedback.SwingArc(transform, true, new Color(0.7f, 1f, 0.7f));
            CombatFeedback.Shake(0.4f);
            OpenHitboxTimed(0.1f, 0.34f, dmg, 20f, 3f, false);
            GameEvents.RaiseSkillBanner(right ? "「右旋斩」" : "「左旋斩」");
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
            GameEvents.RaiseSkillBanner("「切手技」");
        }

        /// <summary>超必杀「觉醒·乱舞」：满 3 势释放的连续技（KOF 超杀）。</summary>
        void StartRanWu(float charge01)
        {
            if (_ranwuRoutine != null) StopCoroutine(_ranwuRoutine);
            _ranwuRoutine = StartCoroutine(RanWu(charge01));
        }

        IEnumerator RanWu(float charge01)
        {
            GameEvents.RaiseSkillBanner("超必杀「觉醒·乱舞」");
            _fsm.RequestState(CombatState.Finisher, 1.5f);
            _player.SetInvincible(1.6f);
            PoseState[] seq =
            {
                PoseState.PunchJab, PoseState.AttackKick, PoseState.PunchCross,
                PoseState.SideKick, PoseState.AttackSpin, PoseState.AttackLeap
            };
            float per = heavyDamage * (0.45f + 0.15f * charge01);
            for (int i = 0; i < seq.Length; i++)
            {
                PlayPose(seq[i]);
                FaceAndLunge(0.25f);
                OpenHitboxTimed(0.04f, 0.12f, per * (i == seq.Length - 1 ? 2f : 1f),
                    i == seq.Length - 1 ? 40f : 10f, i == seq.Length - 1 ? 6f : 0.5f, false);
                yield return new WaitForSeconds(i == seq.Length - 1 ? 0.3f : 0.18f);
            }
            CombatFeedback.SlowMo(0.25f, 0.4f);
            CombatFeedback.Shake(1f);
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

        /// <summary>跳+腿：飞踢（KOF 跳踢），带前冲与击退。</summary>
        void JumpKickAttack()
        {
            _fsm.RequestState(CombatState.LightAttack, 0.5f);
            _fsm.InCombat = true;
            PlayPose(PoseState.JumpKick);
            _cc.Move(transform.forward * 1.2f);
            _player.ForceFall(-9f);
            float dmg = baseDamage * 1.4f * CritMult();
            CombatFeedback.SwingArc(transform, true, new Color(1f, 0.7f, 0.4f));
            OpenHitboxTimed(0.1f, 0.38f, dmg, 26f, 4f, true);
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
                if (buildMomentum) AddMomentum(1);
                // 打击感：命中顿帧随伤害加重
                CombatFeedback.HitStop(dmg >= heavyDamage ? 0.08f : 0.05f);
                CombatFeedback.Shake(0.3f);
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

        /// <summary>技能消耗意势（能量门槛）：足够则扣除返回 true。</summary>
        public bool TrySpendMomentum(int cost)
        {
            if (_momentum < cost) return false;
            SetMomentum(_momentum - cost);
            return true;
        }

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

        // ================= 调试接口（「测试」面板：逐个验证招式实际生效） =================

        public void Debug_DashStrike() { if (!_fsm.IsHardLocked) DashStrike(); }
        public void Debug_Blowback() { if (!_fsm.IsHardLocked) BlowbackKick(); }
        public void Debug_LeftSpin() { if (!_fsm.IsHardLocked) SideSpinStrike(false); }
        public void Debug_RightSpin() { if (!_fsm.IsHardLocked) SideSpinStrike(true); }

        public void Debug_EnergyBlade()
        {
            var exec = GetComponent<SkillExecutor>();
            if (exec == null) return;
            SetMomentum(3);
            foreach (var s in exec.equippedSkills)
                if (s != null && s.isRanged) { exec.TryCast(s); return; }
        }
        public void Debug_QiShou() { if (!_fsm.IsHardLocked) QiShou(); }
        public void Debug_JumpAttack() { if (!_fsm.IsHardLocked) JumpAttack(); }
        public void Debug_JumpKick() { if (!_fsm.IsHardLocked) JumpKickAttack(); }
        public void Debug_Sweep() { if (!_fsm.IsHardLocked) SweepAttack(); }

        public void Debug_HeavyCharged()
        {
            if (_fsm.IsHardLocked) return;
            SetMomentum(0);
            _chargeT = maxChargeTime;
            _charging = true;
            ReleaseHeavy();
        }

        public void Debug_Finisher()
        {
            if (_fsm.IsHardLocked) return;
            SetMomentum(2);
            _chargeT = maxChargeTime;
            _charging = true;
            ReleaseHeavy();
        }

        public void Debug_RanWu()
        {
            if (_fsm.IsHardLocked) return;
            SetMomentum(3);
            _chargeT = maxChargeTime;
            _charging = true;
            ReleaseHeavy();
        }

        public void Debug_FillMomentum() => SetMomentum(3);

        public void Debug_RestoreAll()
        {
            var s = _player.Stats;
            s.hp = s.maxHp;
            s.stamina = s.maxStamina;
            s.RestoreMental(999f);
            GameEvents.RaisePlayerHpChanged(s.hp, s.maxHp);
        }

        // ================= 受击 =================

        public void TakeHit(DamageInfo dmg)
        {
            if (_player.IsInvincible)
            {
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
