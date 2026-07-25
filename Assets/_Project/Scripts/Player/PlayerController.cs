using UnityEngine;
using AdversityRoad.Combat;
using AdversityRoad.Mobile;

namespace AdversityRoad.Player
{
    /// <summary>
    /// 第三人称移动控制器：走/跑（摇杆模拟量）/慢走/蹲伏潜行/跳跃/
    /// 翻滚闪避（无敌帧）/快速灵活转身。
    /// 每帧把运动状态喂给 HumanoidAnimator 驱动类人步态动画。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("移动（速度按真实体感收敛，防晕）")]
        public float walkSpeed = 2.6f;
        public float runSpeed = 5.2f;
        // 起步/刹车响应（指数逼近速率，1/秒）：对齐 Unity 官方 ThirdPersonController
        // 的 SpeedChangeRate=10 思路，但按动作游戏上调——
        // 起步 k=20：0.05s 到 63%、0.15s 到 95%；刹车 k=26 更利落。
        // 防晕由镜头侧负责（软跟随 + 焦点死区 + 渐进回正），而不是靠拖慢角色本体。
        public float accelRate = 20f;              // 起步逼近速率
        public float decelRate = 26f;              // 停步逼近速率
        public float rotateSpeed = 14f;            // 转身更跟手（大作级的方向响应）
        public float quickTurnMultiplier = 2.1f;   // 大角度转身加速倍率：掉头近乎即时
        public float jumpForce = 7f;
        public float gravity = -20f;

        [Header("蹲伏")]
        public float crouchSpeedMult = 0.45f;

        [Header("闪避（翻跟头）")]
        public float dodgeSpeed = 10f;
        public float dodgeDuration = 0.35f;
        public float dodgeIFrames = 0.25f;
        public float dodgeStaminaCost = 20f;

        public PlayerStats Stats = new PlayerStats();
        public Transform cameraTransform;

        CharacterController _cc;
        CombatStateMachine _combat;
        HumanoidAnimator _anim;
        LockOnSystem _lockOn;
        PlayerAppearance _appearance;
        float _vy;
        float _dodgeTimer, _iframeTimer;
        float _dodgeSpd = 10f;   // 本次翻滚的实际速度（时长匹配片段时反比缩放）
        Vector3 _dodgeDir;
        Vector3 _lastPos;
        Vector3 _hVel;   // 平滑后的水平速度

        /// <summary>拖延泥潭等减速效果的外部倍率（1 = 正常）。</summary>
        public float MoveSpeedMultiplier { get; set; } = 1f;

        /// <summary>本帧摇杆的世界方向（相机相对，零向量=未推杆）。
        /// 技能连招据此判断玩家是否正在主动引导方向，从而让出自动吸附。</summary>
        public Vector3 StickWorldDir { get; private set; }

        // ---- 意图匹配：输入缓冲 + 土狼时间（大作通用）----
        const float DodgeBufferWindow = 0.3f;   // 闪避缓冲窗（硬直中按下也能在恢复帧兑现）
        const float JumpBufferWindow = 0.2f;    // 跳跃缓冲窗（落地前按下，落地即跳）
        const float CoyoteTime = 0.12f;         // 土狼时间（离开边缘后仍可起跳）
        readonly InputBuffer _inputBuf = new InputBuffer();

        /// <summary>供其它战斗组件共用的输入缓冲（技能在动作锁期间的排队兑现）。</summary>
        public InputBuffer Buffer => _inputBuf;
        float _coyoteT;
        float _attackSpeedFactor = 1f;   // 出招定步倍率（平滑过渡，防连打时速度震荡）

        public bool IsInvincible => _iframeTimer > 0;
        public bool IsDodging => _dodgeTimer > 0;
        public bool IsCrouched { get; private set; }

        /// <summary>倒地起身等外部授予的无敌帧。</summary>
        public void SetInvincible(float duration) => _iframeTimer = Mathf.Max(_iframeTimer, duration);

        /// <summary>空中下劈等强制下坠。</summary>
        public void ForceFall(float verticalVelocity) => _vy = Mathf.Min(_vy, verticalVelocity);

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _combat = GetComponent<CombatStateMachine>();
            _anim = GetComponent<HumanoidAnimator>();
            _lastPos = transform.position;
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
        }

        void Update()
        {
            // 运行时组装顺序下 Awake 期间兄弟组件可能尚未挂载，惰性补齐
            if (_combat == null) _combat = GetComponent<CombatStateMachine>();

            float dt = Time.deltaTime;
            Stats.TickRegen(dt, _combat != null && _combat.InCombat);
            if (Stats.IsDead) return;

            if (_iframeTimer > 0) _iframeTimer -= dt;

            // 输入采集 → 缓冲（意图匹配）：必须在任何 early-return 之前，
            // 否则翻滚中/硬直中按下的键会被整帧跳过而丢失（连续翻滚就是这样失效的）。
            // 消费式输入（MobileInput.GetDown）每帧只在这里读一次，其余系统一律走缓冲。
            if (Input.GetKeyDown(KeyCode.LeftShift) || MobileInput.GetDown("Dodge"))
                _inputBuf.Press("Dodge");
            if (Input.GetKeyDown(KeyCode.Space) || MobileInput.GetDown("Jump"))
                _inputBuf.Press("Jump");

            if (_dodgeTimer > 0)
            {
                _dodgeTimer -= dt;
                _cc.Move(_dodgeDir * _dodgeSpd * dt + Vector3.up * _vy * dt);
                if (_dodgeTimer <= 0 && _combat != null) _combat.RequestState(CombatState.Locomotion);
                return;   // 翻滚中按下的闪避已入缓冲，滚完立刻接下一次（连续翻滚）
            }

            bool dodgePressed = _inputBuf.Has("Dodge", DodgeBufferWindow);

            // 摇杆世界方向提前求出：硬锁动作（技能/绝招/重击）期间也要读它做「出招转向」，
            // 所以不能等到下面移动逻辑才算
            Vector2 stickInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            stickInput += MobileInput.Move;
            stickInput = Vector2.ClampMagnitude(stickInput, 1f);
            Vector3 stickDir = CameraRelative(stickInput);
            StickWorldDir = stickDir;   // 供技能连招判断"玩家是否正在主动引导方向"

            // 硬锁定（重击/倒地/硬直等）才禁止移动；轻击连段可以边移动边出招。
            // 例外——收招闪避取消（大作手感）：技能/绝招打完主要段进入恢复相位后，
            // 按闪避可立刻打断收招，不必干等动作播完。
            if (_combat != null && _combat.IsHardLocked)
            {
                if (!(dodgePressed && _combat.CanDodgeCancel && Stats.stamina >= dodgeStaminaCost))
                {
                    // 出招转向影响（大作 attack steering / directional influence）：
                    // 技能、绝招、重击期间【仍可用摇杆缓慢调整朝向】——此前这里直接 return，
                    // 摇杆完全无响应、动作结束才突然弹到新朝向，正是"出招+转向不连贯"的主因。
                    // 受击/倒地/硬直不给转向（挨打就该失控）。
                    SteerDuringAction(stickDir, dt);
                    ApplyGravityOnly(dt);
                    return;
                }
                _combat.RequestState(CombatState.Locomotion);   // 解除收招锁，落入下方翻滚
            }
            bool attacking = _combat != null && _combat.Current == CombatState.LightAttack;

            // 蹲伏切换（潜行/低姿态）
            if (Input.GetKeyDown(KeyCode.C) || MobileInput.GetDown("Crouch")) ToggleCrouch();

            // 拔刀/收刀（带剑鞘武器，手动按钮触发）
            if (Input.GetKeyDown(KeyCode.T) || MobileInput.GetDown("Sheathe"))
            {
                if (_appearance == null) _appearance = GetComponent<PlayerAppearance>();
                if (_appearance != null) _appearance.ToggleWeaponDrawn();
            }

            float inputMag = stickInput.magnitude;            // 摇杆已在本帧开头统一读取
            Vector3 moveDir = stickDir;

            // 模拟量速度：摇杆半推=走路，全推=奔跑；桌面按住 Alt 慢走
            // 行动力过低时脚步沉重（拖延的具象体感）：35 以下开始线性减速，最低 ×0.65
            float apMult = Mathf.Lerp(0.65f, 1f, Mathf.Clamp01(Stats.actionPower / 35f));
            float speed = runSpeed * MoveSpeedMultiplier * apMult * inputMag;
            if (!Application.isMobilePlatform && Input.GetKey(KeyCode.LeftAlt))
                speed = Mathf.Min(speed, walkSpeed * MoveSpeedMultiplier);
            if (IsCrouched) speed *= crouchSpeedMult;
            // 出招定步（平滑化）：攻击动画占据全身，照常位移会读作"脚不动人在滑"。
            // 但此前用【硬性 ×0.1】会造成速度震荡——推着摇杆连打时，每一段出招速度
            // 从全速骤降到 10%、收招again骤升回全速，配合已提速的加减速就是一顿一顿的
            // 抽搐感。改为对倍率本身做时间常数 ≈0.07s 的平滑过渡，并把下限抬到 0.3：
            // 既保留定步的分量感，又让"边推杆边连打"是一条连续的速度曲线。
            _attackSpeedFactor = Mathf.MoveTowards(_attackSpeedFactor,
                attacking ? 0.3f : 1f, dt / 0.07f);
            speed *= _attackSpeedFactor;

            // 土狼时间（coyote time）：离开地面边缘后仍有一小段时间可以起跳——
            // 玩家的意图是"我在跳台边缘按了跳"，而不是"我晚了两帧所以活该掉下去"
            if (_cc.isGrounded) { _vy = -1f; _coyoteT = CoyoteTime; }
            else { _vy += gravity * dt; _coyoteT -= dt; }

            // 跳跃：缓冲 + 土狼时间——落地前一瞬按下也能在落地帧兑现
            if (_coyoteT > 0f && _inputBuf.TryConsume("Jump", JumpBufferWindow))
            {
                if (IsCrouched) ToggleCrouch();
                _vy = jumpForce;
                _coyoteT = 0f;
            }

            // 翻滚闪避（Shift / 闪）——走缓冲：硬直/翻滚中按下的闪避会在此兑现
            if (dodgePressed && Stats.SpendStamina(dodgeStaminaCost))
            {
                _inputBuf.Consume("Dodge");
                if (IsCrouched) ToggleCrouch();
                // 闪避取消：切断进行中的轻连段（清序列/收判定框），翻滚落地即可全新起手
                var pcc = GetComponent<Combat.PlayerCombatController>();
                if (pcc != null) pcc.CancelComboForDodge();
                _dodgeDir = moveDir.sqrMagnitude > 0.01f ? moveDir : transform.forward;
                // 翻滚方向即刻转身
                transform.rotation = Quaternion.LookRotation(_dodgeDir);
                Core.GameAudio.Play(Core.GameAudio.Sfx.Dodge, 0.7f);
                // 有专用翻滚片段时：闪避时长匹配片段（完整呈现整个滚翻动作），
                // 总位移保持恒定（速度反比时长），无片段沿用默认参数
                float dur = dodgeDuration;
                _dodgeSpd = dodgeSpeed;
                if (_anim != null)
                {
                    float clipLen = _anim.ActionClipLength(PoseState.Dodge);
                    if (clipLen > 0.1f)
                    {
                        dur = Mathf.Clamp(clipLen * 0.85f, 0.42f, 0.7f);
                        _dodgeSpd = dodgeSpeed * dodgeDuration / dur;   // 位移总量不变
                    }
                }
                _dodgeTimer = dur;
                _iframeTimer = dodgeIFrames;
                if (_combat != null) _combat.RequestState(CombatState.Dodge);
                return;
            }

            // 加减速曲线：改用【指数逼近】而非线性匀加速——对齐 Unity 官方
            // ThirdPersonController 的做法（其注释原文：curved result rather than a
            // linear one giving a more organic speed change）。
            // 起步瞬间给足冲量（前几帧就到大半速度＝跟手），末段自然收敛（不突兀）。
            // 用 1-e^(-k·dt) 而非 Lerp(a,b,k·dt)：帧率无关，高低帧手感一致。
            Vector3 targetVel = moveDir * speed;
            float k = targetVel.sqrMagnitude > _hVel.sqrMagnitude ? accelRate : decelRate;
            _hVel = Vector3.Lerp(_hVel, targetVel, 1f - Mathf.Exp(-k * dt));
            _cc.Move(_hVel * dt + Vector3.up * _vy * dt);

            // 快速灵活转身：目标夹角越大转得越快
            if (moveDir.sqrMagnitude > 0.01f)
            {
                Quaternion target = Quaternion.LookRotation(moveDir);
                if (attacking)
                {
                    // 轻击连段中的转向影响：用明确的角速度上限（度/秒）而非低倍率 Slerp——
                    // 旧值 2.2 的 Slerp 每帧只挪动 3.7%，推杆几乎转不动，读作"出招就僵住"。
                    // 150°/s 既能顺着摇杆把连招引向新方向，又不会一帧甩离当前目标。
                    transform.rotation = Quaternion.RotateTowards(transform.rotation,
                        target, AttackSteerDegPerSec * dt);
                }
                else
                {
                    float ang = Quaternion.Angle(transform.rotation, target);
                    float rs = rotateSpeed * (ang > 80f ? quickTurnMultiplier : 1f);
                    transform.rotation = Quaternion.Slerp(transform.rotation, target, rs * dt);
                }
            }
        }

        // ---- 出招转向影响（大作 attack steering）速率表（度/秒）----
        const float AttackSteerDegPerSec = 150f;   // 轻击连段：顺杆引导连招方向
        const float SkillSteerDegPerSec = 110f;    // 技能/绝招：可调整但保持"已出招"的承诺感
        const float HeavySteerDegPerSec = 85f;     // 重击/蓄力：最重，转向最慢

        /// <summary>
        /// 硬锁动作期间的摇杆转向（技能/绝招/重击）：按状态给不同角速度上限，
        /// 让「出招 + 转向」是一个连贯动作，而不是无响应后突然弹向新朝向。
        /// 受击/倒地/心理硬直/死亡不给转向——挨打就该失控，这是打击反馈的一部分。
        /// </summary>
        void SteerDuringAction(Vector3 dir, float dt)
        {
            if (_combat == null || dir.sqrMagnitude < 0.01f) return;
            float rate;
            switch (_combat.Current)
            {
                case CombatState.Finisher:
                case CombatState.InnerPowerCast:
                    rate = SkillSteerDegPerSec; break;
                case CombatState.HeavyAttack:
                    rate = HeavySteerDegPerSec; break;
                default:
                    return;   // HitReaction / Knockdown / MentalStagger / Death：不可转向
            }
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(dir), rate * dt);
        }

        void LateUpdate()
        {
            // 把实际位移换算成步态参数喂给人形动画
            if (_anim == null) _anim = GetComponent<HumanoidAnimator>();
            if (_anim == null) return;
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 planar = transform.position - _lastPos;
            planar.y = 0;
            float actual = planar.magnitude / dt;
            float speed01 = Mathf.Clamp01(actual / Mathf.Max(0.1f, runSpeed));
            _anim.SetLocomotion(speed01, IsCrouched, _cc.isGrounded, actual);
            // 临战架势：只有敌人【逼近到近身范围(≈6m)】或正在交战时才摆格斗预备架势；
            // 敌人在远处/无敌人时用普通待机（不再一有敌人在场就一直端着架势）
            if (_lockOn == null) _lockOn = GetComponent<LockOnSystem>();
            bool enemyClose = _lockOn != null && _lockOn.CurrentTarget != null &&
                Vector3.Distance(transform.position, _lockOn.CurrentTarget.position) < 6f;
            bool ready = enemyClose || (_combat != null && _combat.InCombat);
            _anim.SetCombatReady(ready);
            // 拔刀/收刀改为手动按钮触发（见 PlayerAppearance.ToggleWeaponDrawn），此处不再自动驱动
            _lastPos = transform.position;
        }

        void ToggleCrouch()
        {
            IsCrouched = !IsCrouched;
            // 碰撞体随姿态变化，底部保持贴地
            if (IsCrouched)
            {
                _cc.height = 1.3f;
                _cc.center = new Vector3(0, -0.35f, 0);
            }
            else
            {
                _cc.height = 2f;
                _cc.center = Vector3.zero;
            }
        }

        Vector3 CameraRelative(Vector2 input)
        {
            if (input.sqrMagnitude < 0.0001f) return Vector3.zero;
            if (cameraTransform == null) return new Vector3(input.x, 0, input.y).normalized;
            Vector3 fwd = cameraTransform.forward; fwd.y = 0; fwd.Normalize();
            Vector3 right = cameraTransform.right; right.y = 0; right.Normalize();
            return (fwd * input.y + right * input.x).normalized;
        }

        void ApplyGravityOnly(float dt)
        {
            // 硬锁状态(重击蓄力/聚气、施法、倒地等)：只落重力、水平零位移，且清空残余
            // 水平速度——聚气时玩家原地扎稳，不带着惯性前滑（"漂移"），锁定解除也不会
            // 突然窜出一段。
            _hVel = Vector3.zero;
            _vy = _cc.isGrounded ? -1f : _vy + gravity * dt;
            _cc.Move(Vector3.up * _vy * dt);
        }
    }
}
