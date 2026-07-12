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
        public float acceleration = 18f;           // 起步加速度（防晕：速度不突变）
        public float deceleration = 26f;           // 停步减速度
        public float rotateSpeed = 11f;
        public float quickTurnMultiplier = 1.7f;   // 大角度转身加速倍率
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
        float _vy;
        float _dodgeTimer, _iframeTimer;
        float _dodgeSpd = 10f;   // 本次翻滚的实际速度（时长匹配片段时反比缩放）
        Vector3 _dodgeDir;
        Vector3 _lastPos;
        Vector3 _hVel;   // 平滑后的水平速度

        /// <summary>拖延泥潭等减速效果的外部倍率（1 = 正常）。</summary>
        public float MoveSpeedMultiplier { get; set; } = 1f;

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

            if (_dodgeTimer > 0)
            {
                _dodgeTimer -= dt;
                _cc.Move(_dodgeDir * _dodgeSpd * dt + Vector3.up * _vy * dt);
                if (_dodgeTimer <= 0 && _combat != null) _combat.RequestState(CombatState.Locomotion);
                return;
            }

            // 硬锁定（重击/倒地/硬直等）才禁止移动；轻击连段可以边移动边出招
            if (_combat != null && _combat.IsHardLocked) { ApplyGravityOnly(dt); return; }
            bool attacking = _combat != null && _combat.Current == CombatState.LightAttack;

            // 蹲伏切换（潜行/低姿态）
            if (Input.GetKeyDown(KeyCode.C) || MobileInput.GetDown("Crouch")) ToggleCrouch();

            Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            input += MobileInput.Move;                       // 合并虚拟摇杆
            input = Vector2.ClampMagnitude(input, 1f);
            float inputMag = input.magnitude;
            Vector3 moveDir = CameraRelative(input);

            // 模拟量速度：摇杆半推=走路，全推=奔跑；桌面按住 Alt 慢走
            float speed = runSpeed * MoveSpeedMultiplier * inputMag;
            if (!Application.isMobilePlatform && Input.GetKey(KeyCode.LeftAlt))
                speed = Mathf.Min(speed, walkSpeed * MoveSpeedMultiplier);
            if (IsCrouched) speed *= crouchSpeedMult;
            // 出招定步：攻击动画占据全身（含腿部），此时若照常位移就是"脚不动
            // 人在滑"的漂移。出招期间移动近乎锁定（保留极小微调），突进位移
            // 由招式自身的 GlideMove 负责——动作与位移始终匹配。
            if (attacking) speed *= 0.1f;

            if (_cc.isGrounded)
            {
                _vy = -1f;
                if (Input.GetKeyDown(KeyCode.Space) || MobileInput.GetDown("Jump"))
                {
                    if (IsCrouched) ToggleCrouch();
                    _vy = jumpForce;
                }
            }
            else _vy += gravity * dt;

            // 翻滚闪避（Shift / 闪）
            if ((Input.GetKeyDown(KeyCode.LeftShift) || MobileInput.GetDown("Dodge")) && Stats.SpendStamina(dodgeStaminaCost))
            {
                if (IsCrouched) ToggleCrouch();
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

            // 加减速平滑：世界移动无速度突变（防晕关键之一）
            Vector3 targetVel = moveDir * speed;
            float a = targetVel.sqrMagnitude > _hVel.sqrMagnitude ? acceleration : deceleration;
            _hVel = Vector3.MoveTowards(_hVel, targetVel, a * dt);
            _cc.Move(_hVel * dt + Vector3.up * _vy * dt);

            // 快速灵活转身：目标夹角越大转得越快；出招中保持攻击朝向仅缓慢修正
            if (moveDir.sqrMagnitude > 0.01f)
            {
                Quaternion target = Quaternion.LookRotation(moveDir);
                float ang = Quaternion.Angle(transform.rotation, target);
                float rs = rotateSpeed * (ang > 80f ? quickTurnMultiplier : 1f);
                // 出招中朝向基本锁定（配合锥形辅助瞄准）：摇杆推着连招不甩离目标
                if (attacking) rs = 2.2f;
                transform.rotation = Quaternion.Slerp(transform.rotation, target, rs * dt);
            }
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
