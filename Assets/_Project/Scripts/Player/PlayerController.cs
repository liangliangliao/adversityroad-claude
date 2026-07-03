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
        [Header("移动")]
        public float walkSpeed = 3.5f;
        public float runSpeed = 6.5f;
        public float rotateSpeed = 14f;
        public float quickTurnMultiplier = 2.1f;   // 大角度转身加速倍率
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
        float _vy;
        float _dodgeTimer, _iframeTimer;
        Vector3 _dodgeDir;
        Vector3 _lastPos;

        /// <summary>拖延泥潭等减速效果的外部倍率（1 = 正常）。</summary>
        public float MoveSpeedMultiplier { get; set; } = 1f;

        public bool IsInvincible => _iframeTimer > 0;
        public bool IsDodging => _dodgeTimer > 0;
        public bool IsCrouched { get; private set; }

        /// <summary>倒地起身等外部授予的无敌帧。</summary>
        public void SetInvincible(float duration) => _iframeTimer = Mathf.Max(_iframeTimer, duration);

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
                _cc.Move(_dodgeDir * dodgeSpeed * dt + Vector3.up * _vy * dt);
                if (_dodgeTimer <= 0 && _combat != null) _combat.RequestState(CombatState.Locomotion);
                return;
            }

            if (_combat != null && _combat.IsActionLocked) { ApplyGravityOnly(dt); return; }

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
                _dodgeTimer = dodgeDuration;
                _iframeTimer = dodgeIFrames;
                if (_combat != null) _combat.RequestState(CombatState.Dodge);
                return;
            }

            _cc.Move(moveDir * speed * dt + Vector3.up * _vy * dt);

            // 快速灵活转身：目标夹角越大转得越快，掉头不拖泥带水
            if (moveDir.sqrMagnitude > 0.01f)
            {
                Quaternion target = Quaternion.LookRotation(moveDir);
                float ang = Quaternion.Angle(transform.rotation, target);
                float rs = rotateSpeed * (ang > 80f ? quickTurnMultiplier : 1f);
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
            float speed01 = Mathf.Clamp01(planar.magnitude / dt / Mathf.Max(0.1f, runSpeed));
            _anim.SetLocomotion(speed01, IsCrouched, _cc.isGrounded);
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
            _vy = _cc.isGrounded ? -1f : _vy + gravity * dt;
            _cc.Move(Vector3.up * _vy * dt);
        }
    }
}
