using System.Collections;
using UnityEngine;
using AdversityRoad.Player;
using AdversityRoad.Core;
using AdversityRoad.Mobile;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 玩家战斗控制（大型动作游戏标准布局：轻/重双连段 + 闪避 + 格挡 + 蓄力，
    /// 全部招式绑定动作库真实动作，全量覆盖动作库）：
    /// - 【拳】轻连段·拳脚：前手直拳→交叉重拳→正踢→侧踹（出手最快、削韧、积势快）；
    /// - 【剑】重连段·巨剑：横斩→撩斩→突刺→旋风斩（伤害高、击退大）；两系可自由混接；
    /// - 组合招式：按顺序连点拳/剑自动成招（三段无消耗；四段需 2 势），终结动作取
    ///   动作库大招（旋风斩/裂地跳劈/飞踢/旋身空翻踢）；
    /// - 指令技（轻点重+方向）：前=疾影突刺，后=旋身空翻踢，左右=旋风斩；跳中按重=空袭跳劈；
    /// - 派生：跳+拳=飞踢、跳+剑=空袭跳劈、蹲+拳=扫堂腿、蹲+剑=低位突刺；
    /// - 蓄力气场（按住重）：强风场持续外推敌人无法近身 + 防御姿态减伤 75% 且轻击不打断，
    ///   消耗少量生命能量；松开释放的蓄力斩【无法格挡/闪避】必中；
    /// - 出招位移（大作惯例）：有目标磁吸贴身（差多远冲多远）；无目标只小步前移
    ///   （≤0.35m），原地连打不会一路平移；
    /// - 「意势」资源（0-3）：命中/完美闪避/蓄力积攒；势=2 旋风终结，势=3 超必杀「觉醒·乱舞」；
    /// - 完美闪避（时缓+返势+必暴击）；受身：被击倒瞬间按闪快速起身带无敌帧。
    /// </summary>
    [RequireComponent(typeof(CombatStateMachine))]
    public class PlayerCombatController : MonoBehaviour
    {
        [Header("连段")]
        public Hitbox weaponHitbox;
        public float baseDamage = 16f;
        public float staminaPerHit = 8f;
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

        // ============ 基本键 ↔ 动作库绑定（大作标准：轻连段拳脚 / 重连段武器）============
        // 【拳】轻连段（快、削韧、积势）：前手直拳→交叉重拳→正踢→侧踹
        //       （Lead Jab / Cross Punch / Kicking / Side Kick）
        // 【剑】重连段（伤害、击退）：巨剑横斩→巨剑撩斩→突刺→巨剑旋风斩
        //       （Great Sword Slash / Slash(1) / Stabbing / High Spin Attack）
        // 【重】按住=蓄力气场，松开=巨剑跳劈；轻点+方向=指令技；跳中按重=空袭跳劈
        // 派生：跳+拳=飞踢  跳+剑=空袭跳劈  蹲+拳=扫堂腿  蹲+剑=低位突刺
        // 帧数：动画从发力相位起播 → windup 0.1s 上下按键当拍出手；
        // cancelAt 紧跟命中相位，连点无缝衔接。拳系帧数更短（轻攻击手感）。
        static readonly ComboStage[] PunchChain =
        {
            new ComboStage { pose = PoseState.PunchJab,   dmg = 0.7f,  posture = 14, lunge = 0.4f, windup = 0.07f, open = 0.14f, length = 0.4f,  cancelAt = 0.2f },
            new ComboStage { pose = PoseState.PunchCross, dmg = 0.8f,  posture = 16, lunge = 0.4f, windup = 0.08f, open = 0.14f, length = 0.4f,  cancelAt = 0.21f },
            new ComboStage { pose = PoseState.AttackKick, dmg = 0.9f,  posture = 20, lunge = 0.5f, windup = 0.09f, open = 0.16f, length = 0.44f, cancelAt = 0.24f },
            new ComboStage { pose = PoseState.SideKick,   dmg = 1.05f, posture = 24, lunge = 0.6f, windup = 0.09f, open = 0.16f, length = 0.44f, cancelAt = 0.24f },
        };

        // 剑系（重连段）：伤害高、击退大
        static readonly ComboStage[] SwordChain =
        {
            new ComboStage { pose = PoseState.Attack,      dmg = 1.1f,  posture = 10, lunge = 0.6f, windup = 0.10f, open = 0.16f, length = 0.46f, cancelAt = 0.26f },
            new ComboStage { pose = PoseState.AttackUp,    dmg = 1.25f, posture = 12, lunge = 0.6f, windup = 0.10f, open = 0.16f, length = 0.46f, cancelAt = 0.26f },
            new ComboStage { pose = PoseState.SwordThrust, dmg = 1.45f, posture = 14, lunge = 1.0f, windup = 0.09f, open = 0.16f, length = 0.44f, cancelAt = 0.25f },
            new ComboStage { pose = PoseState.AttackSpin,  dmg = 2.0f,  posture = 28, lunge = 0.6f, windup = 0.14f, open = 0.24f, length = 0.6f,  cancelAt = 0.42f },
        };

        enum AttackBtn { None, Punch, Kick }

        // 组合技配方（e：武术技能不是一键生成，而是玩家打出来的组合）
        struct Recipe
        {
            public string seq;    // P=拳 K=腿
            public string name;
            public float mult;
            public int cost;      // 释放绝招所需意势（越复杂越强，需能量积累）
            public PoseState pose;   // 成招的专属终结动作（从动作库中重新定位）
        }

        // 复杂度越高伤害越强、消耗意势越多——大绝招不可无限使用。
        // 全部只用界面上真实存在的键（拳/剑）按顺序连点打出（P=拳 K=剑）；
        // 终结动作取自动作库最具杀伤力的片段：旋风斩/裂地跳劈/飞踢/旋身空翻踢。
        static readonly Recipe[] Recipes =
        {
            // 顶级绝招（需 2 势）
            new Recipe { seq = "PPKK", name = "龙卷·旋风绝斩", mult = 2.8f, cost = 2, pose = PoseState.AttackSpin },
            new Recipe { seq = "KKPP", name = "踏空·裂地跳劈", mult = 2.7f, cost = 2, pose = PoseState.AttackLeap },
            new Recipe { seq = "PKPK", name = "拳剑·惊鸿飞踢", mult = 2.6f, cost = 2, pose = PoseState.JumpKick },
            // 基础连招（无消耗）：三段收一个动作库大招
            new Recipe { seq = "PPP",  name = "连环拳脚·空翻踢", mult = 1.6f, cost = 0, pose = PoseState.SpinKick },
            new Recipe { seq = "KKK",  name = "三连斩·大回旋", mult = 1.7f, cost = 0, pose = PoseState.AttackSpin },
            new Recipe { seq = "PPK",  name = "拳影·裂地跳劈", mult = 1.5f, cost = 0, pose = PoseState.AttackLeap },
            new Recipe { seq = "KKP",  name = "双斩·惊鸿飞踢", mult = 1.5f, cost = 0, pose = PoseState.JumpKick },
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
        float _bufferedAt;            // 输入缓冲时间戳（过期作废，防陈旧输入迟到触发）
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

        StanceSystem _stance;

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _fsm = GetComponent<CombatStateMachine>();
            _cc = GetComponent<CharacterController>();
            _stance = GetComponent<StanceSystem>();
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

            // 格挡（含格挡架势动作：抬臂护身，收招后放下）
            bool wasGuarding = IsGuarding;
            IsGuarding = Input.GetKey(KeyCode.LeftControl) || MobileInput.GetHeld("Guard");
            if (Input.GetKeyDown(KeyCode.LeftControl) || MobileInput.GetDown("Guard")) _parryTimer = parryWindow;
            if (IsGuarding != wasGuarding && !_fsm.IsActionLocked && _anim != null)
                _anim.SetPose(IsGuarding ? PoseState.Guard : PoseState.Idle);
            // 兜底：格挡是保持型姿态，若松开瞬间恰逢动作锁而错过收招，空闲时补收，
            // 避免站立时卡在举械架势上（看起来像"待机动作不对"）
            if (!IsGuarding && _anim != null && _anim.CurrentPose == PoseState.Guard &&
                !_fsm.IsActionLocked)
                _anim.SetPose(PoseState.Idle);
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
                // 蓄力气场（防御姿态）：强风场持续外推周围敌人——无法近身攻击；
                // 代价是持续消耗少量生命能量（远小于该技能对敌伤害）
                RepelEnemies(dt);
                _player.Stats.hp = Mathf.Max(1f, _player.Stats.hp - 3f * dt);
                GameEvents.RaisePlayerHpChanged(_player.Stats.hp, _player.Stats.maxHp);
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
                    // 蓄力可见化：环身狂风气流 + 地面冲击环 + 金色能量火花
                    if (Time.time > _chargeFxAt)
                    {
                        _chargeFxAt = Time.time + 0.18f;
                        CombatFeedback.ChargeGale(transform.position, Mathf.Clamp01(_chargeT / maxChargeTime));
                        CombatFeedback.HitSpark(transform.position - Vector3.up * 0.6f,
                            new Color(1f, 0.85f, 0.35f), 3);
                    }
                    if (!heavyHeld) ReleaseHeavy();
                }
                // 蓄力中按下拳/腿：进缓冲排队（重击一出手立刻接连段，输入不丢）
                AttackBtn queued = punchDown ? AttackBtn.Punch
                    : kickDown ? AttackBtn.Kick : AttackBtn.None;
                if (queued != AttackBtn.None) { _buffered = queued; _bufferedAt = Time.time; }
                return;
            }

            if (heavyDown)
            {
                if (!_cc.isGrounded) { AirLeapAttack(); return; }   // 跳+重=空袭跳劈
                if (_depth >= 1 && _stageT >= 0.1f) { QiShou(); return; }
                MoveIntent(out _heavyDirFwd, out _heavyDirSide);
                StartCharge();
                return;
            }

            AttackBtn pressed = punchDown ? AttackBtn.Punch : kickDown ? AttackBtn.Kick : AttackBtn.None;
            if (pressed != AttackBtn.None)
            {
                if (_depth >= 0) { _buffered = pressed; _bufferedAt = Time.time; return; }
                // 动作锁期间（受击硬直/重击收招等）不再吞掉输入：进缓冲排队，
                // 锁一解除立即出招——连点第二下绝不丢（"连续性差/有延迟"的根因）
                if (_fsm.IsActionLocked) { _buffered = pressed; _bufferedAt = Time.time; return; }
                StartAttack(pressed);
                return;
            }

            // 缓冲兑现：锁解除后立刻打出排队的那一下（0.6s 内有效，过期作废）
            if (_depth < 0 && _buffered != AttackBtn.None && !_fsm.IsActionLocked)
            {
                if (Time.time - _bufferedAt > 0.6f) { _buffered = AttackBtn.None; return; }
                var b = _buffered;
                _buffered = AttackBtn.None;
                StartAttack(b);
            }
        }

        void StartAttack(AttackBtn pressed)
        {
            if (!_cc.isGrounded)
            {
                // 跳跃派生：跳+拳=飞踢，跳+剑=空袭跳劈
                if (pressed == AttackBtn.Kick) JumpAttack(); else JumpKickAttack();
                return;
            }
            // 蹲伏派生：蹲+拳=扫堂腿（贴地环扫），蹲+剑=低位突刺（下段直线戳击）
            if (_player.IsCrouched)
            {
                if (pressed == AttackBtn.Kick) CrouchThrust(); else SweepAttack();
                return;
            }
            _depth = -1;
            _seq = "";
            NextStage(pressed);
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
            var chain = btn == AttackBtn.Kick ? SwordChain : PunchChain;
            var s = chain[nextDepth];
            if (!_player.Stats.SpendStamina(staminaPerHit)) { EndCombo(); return; }

            _depth = nextDepth;
            _cur = s;
            _stageT = 0;
            _seq += btn == AttackBtn.Kick ? "K" : "P";
            RaiseSeq();
            _fsm.InCombat = true;

            float dmg = baseDamage * s.dmg * CritMult();

            // 组合技识别：打出配方即触发「招式」——冲击波+时缓+大增伤+击飞
            // 高级绝招（cost>0）需消耗意势能量；能量不足则退化为普通连段
            bool recipeHit = false;
            PoseState playPose = s.pose;
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
                    playPose = r.pose;   // 成招：改播该绝招的专属终结动作（动作库大招）
                    // 终结动作给足施展窗口（比普通段稍长），且成招后序列清零重新起手
                    _cur.length = 0.68f;
                    _cur.cancelAt = 0.5f;
                    _cur.open = Mathf.Max(_cur.open, 0.26f);
                    _seq = "";
                    GameEvents.RaiseSkillBanner("绝招「" + r.name + "」");
                    CombatFeedback.RecipeBurst(transform.position, new Color(1f, 0.85f, 0.3f));
                    if (r.cost > 0) CombatFeedback.SlowMo(0.4f, 0.18f);
                    break;
                }
            }

            _fsm.RequestState(CombatState.LightAttack, _cur.length);
            PlayPose(playPose);
            FaceAndLunge(s.lunge);

            CombatFeedback.SwingArc(transform, nextDepth >= 2 || recipeHit,
                recipeHit ? new Color(1f, 0.6f, 0.2f)
                : btn == AttackBtn.Kick ? new Color(1f, 0.65f, 0.4f) : new Color(0.45f, 0.75f, 1f));
            // 招式分工：剑系主司「击退」（重兵器大幅推开、打断敌人突进），
            // 拳系主司「快攻」（低击退但出手快、可高频衔接，帧数更短、削韧更高）。
            float knock = (nextDepth >= 2 ? 2f : 1f) + (recipeHit ? 5f : 0f);
            if (btn == AttackBtn.Kick) knock += 3.5f;
            OpenHitboxTimed(_cur.windup, _cur.open, dmg, _cur.posture, knock, true,
                playPose, recipeHit ? 1.3f : 1f);
        }

        void RaiseSeq()
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in _seq)
            {
                if (sb.Length > 0) sb.Append('·');
                sb.Append(c == 'K' ? '剑' : '拳');
            }
            GameEvents.RaiseComboSeq(sb.ToString());
        }

        void EndCombo()
        {
            _depth = -1;
            _seq = "";
            GameEvents.RaiseComboSeq("");
            // 连段收尾立即解除动作锁：下一次点击零等待（残留锁是"连点延迟"根因）
            if (_fsm.Current == CombatState.LightAttack)
                _fsm.RequestState(CombatState.Locomotion);
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

            // 轻点=八向指令技（斜向就近归并）：前=疾影突刺 后=旋身空翻踢 左/右=旋风斩
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

            // 蓄力释放=连贯二连击（跳劈→旋风斩，均必中）：敌方至少吃两次伤害
            if (_heavyComboRoutine != null) StopCoroutine(_heavyComboRoutine);
            _heavyComboRoutine = StartCoroutine(HeavyCombo(charge01, spent >= 2));
        }

        Coroutine _heavyComboRoutine;

        /// <summary>蓄力释放二连击：巨剑跳劈 → 紧接巨剑旋风斩（快速无缝衔接）。
        /// 两段均【必中】（无法格挡/闪避/对攻化解）；攻击范围随蓄力大幅增大
        /// （长/宽/高与距离一起放大），2 势终结版两段更大更痛。</summary>
        IEnumerator HeavyCombo(float charge01, bool finisher)
        {
            _fsm.RequestState(CombatState.HeavyAttack, finisher ? 1.3f : 1.15f);
            _fsm.InCombat = true;

            // ---- 段1：蓄力·巨剑跳劈 ----
            PlayPose(PoseState.HeavyAttack);
            FaceAndLunge(0.9f);
            float dmg1 = heavyDamage * (1f + 0.6f * charge01 + (finisher ? 0.5f : 0f)) * CritMult();
            CombatFeedback.SwingArc(transform, true,
                finisher ? new Color(1f, 0.8f, 0.3f) : new Color(1f, 0.6f, 0.3f));
            if (charge01 > 0.5f || finisher)
                CombatFeedback.RecipeBurst(transform.position,
                    finisher ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 0.55f, 0.2f));
            // 范围随蓄力放大：满蓄约 2.3 倍（长宽高与打击距离同步大幅增大，攻得更远更广）
            OpenHitboxTimed(0.18f, 0.34f, dmg1, 26f + (finisher ? 12f : 0f), 3.5f, false,
                PoseState.HeavyAttack, 1.4f + 0.9f * charge01, true);
            GameEvents.RaiseSkillBanner(finisher ? "「旋风终结·二连」"
                : charge01 > 0.7f ? "「蓄力·跳劈连斩」" : "「巨剑跳劈」");
            CombatFeedback.ShockRing(transform.position + transform.forward * 1.8f,
                new Color(1f, 0.7f, 0.3f), 4.5f + 3f * charge01);

            yield return new WaitForSeconds(0.5f);
            if (_fsm.Current != CombatState.HeavyAttack) yield break;   // 被击倒等打断

            // ---- 段2：紧接巨剑旋风斩（环身大范围second hit）----
            PlayPose(PoseState.AttackSpin);
            FaceAndLunge(0.4f);
            float dmg2 = dmg1 * (finisher ? 0.9f : 0.7f);
            CombatFeedback.SwingArc(transform, true, new Color(1f, 0.85f, 0.4f));
            OpenHitboxTimed(0.12f, 0.32f, dmg2, 20f, 5.5f, false,
                PoseState.AttackSpin, finisher ? 1.9f : 1.5f + 0.5f * charge01, true);
            if (finisher)
            {
                CombatFeedback.EnergyBurst(transform.position + transform.forward * 1.2f,
                    new Color(1f, 0.8f, 0.3f), 1.2f);
                CombatFeedback.SlowMo(0.45f, 0.2f);
            }
        }

        /// <summary>前+重：疾影突刺（动作库 Stabbing）——高速突进直刺，双重剑气。</summary>
        void DashStrike()
        {
            _fsm.RequestState(CombatState.HeavyAttack, 0.46f);
            PlayPose(PoseState.SwordThrust);
            FaceAndLunge(2.6f);
            float dmg = heavyDamage * 0.85f * CritMult();
            CombatFeedback.SwingArc(transform, true, new Color(0.9f, 0.95f, 0.6f));
            CombatFeedback.SwingArc(transform, false, new Color(0.7f, 0.85f, 1f));
            CombatFeedback.HitSpark(transform.position + transform.forward * 1.2f,
                new Color(0.9f, 0.95f, 0.6f), 5);
            CombatFeedback.Shake(0.4f);
            OpenHitboxTimed(0.12f, 0.28f, dmg, 16f, 2f, false, PoseState.SwordThrust, 1.25f);
            GameEvents.RaiseSkillBanner("「疾影突刺」");
        }

        /// <summary>后+重：旋身空翻踢（动作库 Spin Flip Kick）——大击退吹飞拉开身位。</summary>
        void BlowbackKick()
        {
            _fsm.RequestState(CombatState.HeavyAttack, 0.5f);
            PlayPose(PoseState.SpinKick);
            FaceAndLunge(0.4f);
            float dmg = heavyDamage * 0.7f * CritMult();
            CombatFeedback.RecipeBurst(transform.position, new Color(1f, 0.5f, 0.25f));
            OpenHitboxTimed(0.14f, 0.28f, dmg, 34f, 9f, false, PoseState.SpinKick, 1.1f);
            GameEvents.RaiseSkillBanner("「旋身空翻踢」");
        }

        /// <summary>左/右+重：旋风斩（动作库 High Spin Attack）——侧步位移接整身旋斩。</summary>
        void SideSpinStrike(bool right)
        {
            _fsm.RequestState(CombatState.HeavyAttack, 0.5f);
            PlayPose(PoseState.AttackSpin);
            Vector3 lateral = (right ? transform.right : -transform.right) * 1.7f
                              + transform.forward * 0.4f;
            GlideMove(lateral, 0.14f);
            ApplyAttackFacing();
            float dmg = heavyDamage * 0.75f * CritMult();
            CombatFeedback.SwingArc(transform, true, new Color(0.7f, 1f, 0.7f));
            CombatFeedback.Shake(0.4f);
            OpenHitboxTimed(0.16f, 0.3f, dmg, 20f, 3f, false, PoseState.AttackSpin);
            GameEvents.RaiseSkillBanner(right ? "「右旋风斩」" : "「左旋风斩」");
        }

        /// <summary>跳+重：空袭跳劈（动作库 Great Sword Jump Attack）——凌空砸地。</summary>
        void AirLeapAttack()
        {
            _fsm.RequestState(CombatState.HeavyAttack, 0.55f);
            _fsm.InCombat = true;
            PlayPose(PoseState.AttackLeap);
            ApplyAttackFacing();
            _player.ForceFall(-14f);
            float dmg = heavyDamage * 1.1f * CritMult();
            CombatFeedback.SwingArc(transform, true, new Color(1f, 0.72f, 0.3f));
            OpenHitboxTimed(0.14f, 0.34f, dmg, 30f, 3f, false, PoseState.AttackLeap, 1.1f);
            CombatFeedback.ShockRing(transform.position + transform.forward * 0.9f,
                new Color(1f, 0.72f, 0.3f), 3f);
            GameEvents.RaiseSkillBanner("「空袭·裂地跳劈」");
        }

        /// <summary>切手技：连段中轻点重击派生的快速反击（撩斩上挑）。</summary>
        void QiShou()
        {
            EndCombo();
            _fsm.RequestState(CombatState.HeavyAttack, 0.4f);
            PlayPose(PoseState.AttackUp);
            FaceAndLunge(0.5f);
            float dmg = heavyDamage * 0.85f * CritMult();
            CombatFeedback.SwingArc(transform, true, new Color(0.7f, 0.9f, 1f));
            OpenHitboxTimed(0.1f, 0.22f, dmg, 20f, 2.5f, false, PoseState.AttackUp);
            GameEvents.RaiseSkillBanner("「切手·撩斩」");
        }

        /// <summary>蓄力气场：强风场把半径内的敌人持续推出（蓄力期间无法被近身）。</summary>
        void RepelEnemies(float dt)
        {
            foreach (var e in FindObjectsOfType<AI.EnemyController>())
                e.Repel(transform.position, 3.8f, 6.5f, dt);
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
            _fsm.RequestState(CombatState.Finisher, 2.2f);
            _player.SetInvincible(2.3f);
            CombatFeedback.UltimateShot(2.2f);   // 镜头拉近看清连段
            // 大幅剑技串成连段：节奏留给动作本体，每击只配一道收敛的刀光，
            // 收招才一次中等能量爆发 + 短时缓——特效点到为止，不糊住招式。
            var seq = new (PoseState pose, float dmg, float posture, float knock, float wait, Color arc)[]
            {
                (PoseState.AttackUp,    1.0f, 16f,  8f, 0.4f,  new Color(0.6f, 0.85f, 1f)),
                (PoseState.AttackSpin,  1.2f, 18f, 10f, 0.46f, new Color(0.7f, 0.9f, 1f)),
                (PoseState.SwordThrust, 1.3f, 16f,  6f, 0.38f, new Color(0.8f, 0.92f, 1f)),
                (PoseState.AttackLeap,  2.6f, 42f, 12f, 0.56f, new Color(0.55f, 0.8f, 1f)),
            };
            float baseDmg = heavyDamage * (0.7f + 0.25f * charge01);
            for (int i = 0; i < seq.Length; i++)
            {
                var s = seq[i];
                PlayPose(s.pose);
                FaceAndLunge(0.3f);
                CombatFeedback.SwingArc(transform, true, s.arc);
                OpenHitboxTimed(0.16f, 0.2f, baseDmg * s.dmg, s.posture, s.knock, false,
                    s.pose, i == seq.Length - 1 ? 1.3f : 1.05f);
                // 每击落点一道小型地面冲击环（震地感），终结一击放大招级爆发
                CombatFeedback.ShockRing(transform.position + transform.forward * 1.1f,
                    s.arc, i == seq.Length - 1 ? 5f : 2.2f);
                if (i == seq.Length - 1)
                {
                    CombatFeedback.EnergyBurst(transform.position + transform.forward * 1.3f,
                        new Color(0.55f, 0.8f, 1f), 1.4f);
                    CombatFeedback.Debris(transform.position + transform.forward * 1.3f,
                        new Color(0.5f, 0.65f, 0.9f), 8);
                    CombatFeedback.SlowMo(0.35f, 0.3f);
                }
                yield return new WaitForSeconds(s.wait);
            }
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
            OpenHitboxTimed(0.18f, 0.4f, dmg, 22f, 2.5f, true, PoseState.JumpAttack);
        }

        /// <summary>跳+腿：飞踢（KOF 跳踢），带前冲与击退。</summary>
        void JumpKickAttack()
        {
            _fsm.RequestState(CombatState.LightAttack, 0.5f);
            _fsm.InCombat = true;
            PlayPose(PoseState.JumpKick);
            GlideMove(transform.forward * 1.2f, 0.16f);
            _player.ForceFall(-9f);
            float dmg = baseDamage * 1.4f * CritMult();
            CombatFeedback.SwingArc(transform, true, new Color(1f, 0.7f, 0.4f));
            OpenHitboxTimed(0.15f, 0.36f, dmg, 26f, 4f, true, PoseState.JumpKick);
        }

        /// <summary>蹲+腿：扫堂腿（贴地 360° 环扫，高削韧）。</summary>
        void SweepAttack()
        {
            _fsm.RequestState(CombatState.LightAttack, 0.55f);
            _fsm.InCombat = true;
            PlayPose(PoseState.Sweep);
            float dmg = baseDamage * 0.9f * CritMult();
            OpenHitboxTimed(0.16f, 0.3f, dmg, 30f, 1.5f, true, PoseState.Sweep);
        }

        /// <summary>蹲+拳：低位突刺——蹲姿下段直线戳击（判定框贴近地面）。</summary>
        void CrouchThrust()
        {
            _fsm.RequestState(CombatState.LightAttack, 0.46f);
            _fsm.InCombat = true;
            PlayPose(PoseState.SwordThrust);
            ApplyAttackFacing();
            float dmg = baseDamage * 1.2f * CritMult();
            CombatFeedback.SwingArc(transform, false, new Color(0.7f, 0.85f, 1f));
            // 复用突刺的长窄直线形状，但整体压低到下段（打腿/打倒地目标）
            if (weaponHitbox == null) return;
            weaponHitbox.SetShape(new Vector3(0.9f, 0.8f, 2.5f), new Vector3(0, -0.45f, 1.4f));
            if (_hitboxRoutine != null) StopCoroutine(_hitboxRoutine);
            _hitboxRoutine = StartCoroutine(HitboxWindow(0.1f, 0.18f, dmg, 16f, 1.5f, true));
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

        /// <summary>出招转向 + 磁吸突进：有目标时按「够到目标」计算突进量——
        /// 差多远冲多远（带上限），一步贴身，连点每一击都实打实落在敌人身上；
        /// 无目标才按招式固有前冲量直线突进。</summary>
        void FaceAndLunge(float lunge)
        {
            ApplyAttackFacing();
            var target = _aimTarget != null ? _aimTarget : AutoAimTarget();
            if (target != null)
            {
                Vector3 to = target.position - transform.position; to.y = 0;
                float need = to.magnitude - 1.05f;   // 停在贴身出招距离
                float move = Mathf.Min(Mathf.Max(need, 0f), Mathf.Max(lunge, 0.4f) + 0.9f);
                if (move > 0.04f && to.sqrMagnitude > 0.01f)
                    GlideMove(to.normalized * move, 0.09f);
                return;
            }
            // 无目标（大作惯例）：只做小步身位前移，不做全额突进——
            // 原地连打只轻微向前挪步，绝不一路平移；突进类指令技保留较大位移
            float cap = lunge >= 2f ? 0.9f : 0.35f;
            GlideMove(transform.forward * Mathf.Min(lunge * 0.35f, cap), 0.1f);
        }

        Transform _aimTarget;   // 本次出招锁定的目标（FaceAndLunge 磁吸共用）

        /// <summary>出招朝向决策（摇杆磁吸锁敌，根治"朝着摇杆方向打空"）：
        /// ① 攻击范围内有敌人 → 直接面向敌人出招（摇杆此时只用来在多个敌人间
        ///    选择目标：吸向摇杆所指方向的那一个），连点期间稳稳咬住同一目标；
        /// ② 摇杆明确指向没有敌人的方向（偏差>100°）→ 尊重玩家意图朝摇杆方向打；
        /// ③ 范围内无敌人 → 朝摇杆方向；连摇杆也没推 → 保持当前朝向。</summary>
        void ApplyAttackFacing()
        {
            Vector3 stick = WorldMoveDir();
            _aimTarget = PickTarget(stick);
            if (_aimTarget != null)
            {
                Vector3 dir = _aimTarget.position - transform.position; dir.y = 0;
                if (dir.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.LookRotation(dir.normalized);
                return;
            }
            if (stick.sqrMagnitude > 0.02f)
                transform.rotation = Quaternion.LookRotation(stick);
        }

        /// <summary>摇杆磁吸选target：范围内的敌人按「距离+与摇杆方向的偏角」打分取最优；
        /// 摇杆指向明显偏离某敌人（>100°）时不吸它——玩家想脱离目标打别处时不抢方向。</summary>
        Transform PickTarget(Vector3 preferDir)
        {
            var enemies = FindObjectsOfType<AI.EnemyController>();
            Transform best = null;
            float bestScore = float.MaxValue;
            bool hasDir = preferDir.sqrMagnitude > 0.02f;
            float range = Mathf.Max(autoAimRange, 6f);
            foreach (var e in enemies)
            {
                if (e.State == AI.EnemyState.Dead) continue;
                Vector3 to = e.transform.position - transform.position; to.y = 0;
                float d = to.magnitude;
                if (d > range || d < 0.01f) continue;
                float ang = hasDir ? Vector3.Angle(preferDir, to)
                                   : Vector3.Angle(transform.forward, to) * 0.5f;
                if (hasDir && ang > 100f) continue;   // 摇杆明确指向别处：不吸这个敌人
                float score = d + ang * 0.045f;
                if (score < bestScore) { bestScore = score; best = e.transform; }
            }
            return best;
        }

        /// <summary>摇杆/键盘的世界移动方向（相机相对），无输入返回零向量。</summary>
        Vector3 WorldMoveDir()
        {
            Vector2 mv = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"))
                         + MobileInput.Move;
            if (mv.sqrMagnitude < 0.04f) return Vector3.zero;
            Transform cam = Camera.main != null ? Camera.main.transform : null;
            if (cam != null)
            {
                Vector3 f = cam.forward; f.y = 0; f.Normalize();
                Vector3 r = cam.right; r.y = 0; r.Normalize();
                return (f * mv.y + r * mv.x).normalized;
            }
            return new Vector3(mv.x, 0, mv.y).normalized;
        }

        Coroutine _glideRoutine;

        /// <summary>短促滑步位移：突进不再一帧瞬移（瞬移会被跟随镜头复制成"一记一顿"
        /// 的画面抖动），改为 0.1 秒左右的高速滑行——镜头软跟随即可保持稳定。</summary>
        void GlideMove(Vector3 offset, float duration)
        {
            if (_glideRoutine != null) StopCoroutine(_glideRoutine);
            _glideRoutine = StartCoroutine(Glide(offset, duration));
        }

        IEnumerator Glide(Vector3 offset, float duration)
        {
            float t = 0;
            while (t < duration)
            {
                float dt = Time.deltaTime;
                t += dt;
                _cc.Move(offset * Mathf.Min(dt / duration, 1f));
                yield return null;
            }
        }

        /// <summary>每招独立的攻击判定范围：size=(宽X, 高Y, 长Z纵深)、center=相对角色根的偏移。
        /// 设计原则：招式越强范围越大——蓄力/绝招终结 > 连段末段 > 起手轻击；
        /// 形状对应轨迹——突刺长而窄（直线）、横斩横宽（横扫弧）、撩斩纵高（下→上弧）、
        /// 旋风斩/扫堂腿环身 360°、跳劈罩住落点、扫堂贴地。</summary>
        static void PoseHitShape(PoseState p, out Vector3 size, out Vector3 center)
        {
            switch (p)
            {
                // ---- 剑系 ----
                case PoseState.Attack:      size = new Vector3(2.2f, 1.2f, 1.7f); center = new Vector3(0, 0.1f, 1.0f); break;   // 横斩：横向宽弧
                case PoseState.AttackUp:    size = new Vector3(1.3f, 2.3f, 1.7f); center = new Vector3(0, 0.4f, 1.0f); break;   // 撩斩：纵向高弧
                case PoseState.SwordThrust: size = new Vector3(0.9f, 0.9f, 2.7f); center = new Vector3(0, 0.15f, 1.5f); break;  // 突刺：长而窄的直线
                case PoseState.AttackSpin:  size = new Vector3(3.6f, 1.4f, 3.6f); center = new Vector3(0, 0.15f, 0.2f); break;  // 旋风斩：环身 360°
                case PoseState.HeavyAttack: size = new Vector3(4.2f, 3.6f, 4.8f); center = new Vector3(0, 0.4f, 2.2f); break;   // 蓄力跳劈：超大范围·打得极远
                case PoseState.AttackLeap:  size = new Vector3(2.8f, 2.6f, 2.8f); center = new Vector3(0, -0.1f, 1.1f); break;  // 裂地跳劈：罩住砸点
                case PoseState.JumpAttack:  size = new Vector3(2.1f, 2.5f, 2.3f); center = new Vector3(0, -0.3f, 1.1f); break;  // 空袭下劈：偏下罩落点
                // ---- 拳系 ----
                case PoseState.PunchJab:    size = new Vector3(0.9f, 1.0f, 1.5f); center = new Vector3(0, 0.25f, 0.9f); break;  // 直拳：短直线
                case PoseState.PunchCross:  size = new Vector3(1.0f, 1.0f, 1.6f); center = new Vector3(0, 0.25f, 1.0f); break;
                // ---- 腿系 ----
                case PoseState.AttackKick:  size = new Vector3(1.0f, 1.3f, 1.8f); center = new Vector3(0, 0.0f, 1.1f); break;   // 正踢：中距直线
                case PoseState.SideKick:    size = new Vector3(1.1f, 1.1f, 2.0f); center = new Vector3(0, 0.1f, 1.2f); break;   // 侧踹：更长的直线
                case PoseState.SpinKick:    size = new Vector3(2.8f, 1.7f, 2.4f); center = new Vector3(0, 0.2f, 0.6f); break;   // 旋身空翻踢：大扇面
                case PoseState.JumpKick:    size = new Vector3(1.3f, 1.7f, 2.4f); center = new Vector3(0, 0.2f, 1.3f); break;   // 飞踢：最远的腿击
                case PoseState.Sweep:       size = new Vector3(3.3f, 0.8f, 3.3f); center = new Vector3(0, -0.55f, 0.15f); break;// 扫堂腿：贴地环扫
                default:                    size = new Vector3(1.4f, 1.4f, 1.8f); center = new Vector3(0, 0.1f, 1.1f); break;
            }
        }

        void OpenHitboxTimed(float windup, float open, float dmg, float posture, float knockback,
            bool buildMomentum, PoseState shapePose, float shapeScale = 1f, bool unblockable = false)
        {
            if (weaponHitbox == null) return;
            // 判定框按招式定形：蓄力越满/技能越高，shapeScale 越大（范围随强度增长）
            PoseHitShape(shapePose, out Vector3 size, out Vector3 center);
            if (!Mathf.Approximately(shapeScale, 1f))
            {
                size *= shapeScale;
                center.z *= shapeScale;
            }
            weaponHitbox.SetShape(size, center);
            if (_hitboxRoutine != null) StopCoroutine(_hitboxRoutine);
            _hitboxRoutine = StartCoroutine(HitboxWindow(windup, open, dmg, posture, knockback,
                buildMomentum, unblockable));
        }

        IEnumerator HitboxWindow(float windup, float open, float dmg, float posture, float knockback,
            bool buildMomentum, bool unblockable = false)
        {
            yield return new WaitForSeconds(windup);
            weaponHitbox.onHit = h =>
            {
                if (buildMomentum) AddMomentum(1);
                // 打击感：命中顿帧（不晕）随伤害加重 + 打击音效；
                // 只有重击/大伤害才震屏——普通连段不频繁震屏（防晕）。
                bool heavy = dmg >= heavyDamage;
                CombatFeedback.HitStop(heavy ? 0.08f : 0.05f);
                if (heavy) CombatFeedback.Shake(0.3f);
                Core.GameAudio.Play(heavy ? Core.GameAudio.Sfx.HeavyHit : Core.GameAudio.Sfx.Hit,
                    heavy ? 1f : 0.8f);
            };
            float outMult = (_stance != null ? _stance.OutgoingPhysicalMult() : 1f)
                * Core.GrowthSystem.PhysicalOutMult();   // 技能树/套装被动增伤
            weaponHitbox.EnableHitbox(new DamageInfo
            {
                physicalDamage = dmg * outMult,
                postureDamage = posture,
                knockback = knockback,
                unblockable = unblockable,
                attackerId = "player"
            });
            yield return new WaitForSeconds(open);
            weaponHitbox.DisableHitbox();
            weaponHitbox.onHit = null;
        }

        public void AddMomentum(int n) => SetMomentum(_momentum + n);

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
        public void Debug_AirLeap() { if (!_fsm.IsHardLocked) AirLeapAttack(); }
        public void Debug_CrouchThrust() { if (!_fsm.IsHardLocked) CrouchThrust(); }
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

        /// <summary>重击击飞：与倒地动画同步的短促飞退（二次强减速，落地即停）——
        /// 位移在身体倒下的过程中完成，不"漂移一段才倒下"。</summary>
        IEnumerator KnockFly(Vector3 dir)
        {
            // 极短极快：位移在身体后仰的头几帧内完成（≈0.35m），倒下过程零滑动
            float t = 0, dur = 0.15f;
            while (t < dur && _fsm.Current == CombatState.Knockdown)
            {
                t += Time.deltaTime;
                float k = 1f - t / dur;
                _cc.Move(dir * (5f * k * k) * Time.deltaTime);
                yield return null;
            }
        }

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

            // 死亡归因：记录最近对玩家造成伤害的心魔（供失败诊断）
            if (!string.IsNullOrEmpty(dmg.attackerId)) Core.FailureLog.NoteHit(dmg.attackerId);

            if (dmg.mentalDamage > 0)
            {
                float mult = GameManager.Instance != null && GameManager.Instance.safety != null
                    ? GameManager.Instance.safety.MentalDamageMultiplier() : 1f;
                float mental = dmg.mentalDamage * mult;
                // 姿态减伤：把姿态切到与来袭弱点轴匹配的一档，可大幅削减这次心理伤害
                if (_stance != null) mental *= _stance.IncomingMentalMult(dmg.mentalAxis);

                var mindShield = GetComponent<MindShieldBuff>();
                if (_parryTimer > 0)
                {
                    _player.Stats.focus = Mathf.Min(_player.Stats.maxFocus,
                        _player.Stats.focus + parryFocusRestore);
                    GameEvents.RaiseMentalStatChanged("focus", _player.Stats.focus, _player.Stats.maxFocus);
                    GameEvents.RaiseSubtitle("定心格挡！心理攻击被化解，专注恢复。");
                    Core.GameAudio.Play(Core.GameAudio.Sfx.Parry);
                }
                else if (mindShield != null && mindShield.TryConsume())
                {
                    // 不读心盾：这次心理攻击被整个挡下——猜测没能变成事实
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
                // 敌方偷袭：从背后被打 = 趁其不备，1.4 倍伤害且格挡无效（格挡只护正面）
                Vector3 fromSrc = transform.position - dmg.sourcePosition; fromSrc.y = 0;
                bool backstab = fromSrc.sqrMagnitude > 0.01f &&
                    Vector3.Dot(transform.forward, fromSrc.normalized) > 0.35f;
                if (backstab)
                {
                    phys *= 1.4f;
                    CombatFeedback.DamageNumber(transform.position, "被偷袭！",
                        new Color(1f, 0.45f, 0.2f), 1.25f);
                }
                bool blocked = IsGuarding && !backstab && _player.Stats.SpendStamina(phys * 0.5f);
                if (blocked) phys *= 0.2f;
                // 蓄力气场=防御姿态：受物理伤害大减（敌人也几乎无法近身）
                bool chargeGuard = _charging;
                if (chargeGuard) phys *= 0.25f;
                _player.Stats.TakePhysicalDamage(phys);

                Core.GameAudio.Play(blocked ? Core.GameAudio.Sfx.Block
                    : phys >= knockdownThreshold ? Core.GameAudio.Sfx.HeavyHit
                    : Core.GameAudio.Sfx.Hurt);
                CombatFeedback.HitFlash(gameObject);
                CombatFeedback.DamageNumber(transform.position, Mathf.RoundToInt(phys).ToString(),
                    new Color(1f, 0.35f, 0.3f));
                Vector3 toSrc = dmg.sourcePosition - transform.position; toSrc.y = 0;
                Vector3 dirS = toSrc.sqrMagnitude > 0.01f ? toSrc.normalized : transform.forward;
                // 优先用判定框算出的真实接触身体点，退回估算
                Vector3 contact = dmg.hasContact ? dmg.contactPoint
                    : transform.position + dirS * 0.5f + Vector3.up * 1.25f;
                if (blocked)
                {
                    // 敌人的兵器砍在玩家举起的兵器/护体上：接触点撞击火花
                    CombatFeedback.WeaponClash(contact);
                }
                else
                {
                    // 实打实挨了一下：接触点冲击 + 顺着打击方向的血花（红系，不计连击）
                    // + 部位受击反应（头被打甩头/腿被扫屈膝，打几下动几下）
                    CombatFeedback.HitImpact(contact,
                        new Color(1f, 0.4f, 0.3f), phys >= knockdownThreshold, false);
                    CombatFeedback.BloodSpray(contact, -dirS);
                    HitReactionOverlay.Trigger(transform, contact, -dirS,
                        phys >= knockdownThreshold);
                }

                // 蓄力霸体：轻击不打断蓄力（重击/击倒仍会打断）
                if (!chargeGuard || phys >= knockdownThreshold)
                {
                    _charging = false;
                    EndCombo();
                }

                if (_player.Stats.IsDead)
                {
                    _fsm.RequestState(CombatState.Death);
                }
                else if (!dmg.isMentalOnly)
                {
                    if (phys >= knockdownThreshold)
                    {
                        // 重击=被撞飞一段距离重重倒地，起身带无敌帧立刻回到战斗
                        _fsm.RequestState(CombatState.Knockdown, 1.4f);
                        _player.SetInvincible(1.8f);
                        CombatFeedback.HitStop(0.06f);
                        Vector3 fly = transform.position - dmg.sourcePosition; fly.y = 0;
                        if (fly.sqrMagnitude > 0.01f) StartCoroutine(KnockFly(fly.normalized));
                    }
                    else if (!chargeGuard)
                    {
                        _fsm.RequestState(CombatState.HitReaction, 0.4f);
                    }
                }
            }
        }
    }
}
