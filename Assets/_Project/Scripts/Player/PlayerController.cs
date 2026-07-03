using UnityEngine;
using AdversityRoad.Combat;
using AdversityRoad.Mobile;

namespace AdversityRoad.Player
{
    /// <summary>
    /// 第三人称移动控制器：移动/奔跑/跳跃/闪避（含无敌帧）。
    /// MVP 使用 CharacterController + 旧输入系统保证零依赖可编译；
    /// 正式版建议切换 Input System + Cinemachine ThirdPersonFollow。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("移动")]
        public float walkSpeed = 3.5f;
        public float runSpeed = 6.5f;
        public float rotateSpeed = 12f;
        public float jumpForce = 7f;
        public float gravity = -20f;

        [Header("闪避（流云步）")]
        public float dodgeSpeed = 10f;
        public float dodgeDuration = 0.35f;
        public float dodgeIFrames = 0.25f;
        public float dodgeStaminaCost = 20f;

        public PlayerStats Stats = new PlayerStats();
        public Transform cameraTransform;

        CharacterController _cc;
        CombatStateMachine _combat;
        float _vy;
        float _dodgeTimer, _iframeTimer;
        Vector3 _dodgeDir;

        /// <summary>拖延泥潭等减速效果的外部倍率（1 = 正常）。</summary>
        public float MoveSpeedMultiplier { get; set; } = 1f;

        public bool IsInvincible => _iframeTimer > 0;
        public bool IsDodging => _dodgeTimer > 0;

        /// <summary>倒地起身等外部授予的无敌帧。</summary>
        public void SetInvincible(float duration) => _iframeTimer = Mathf.Max(_iframeTimer, duration);

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _combat = GetComponent<CombatStateMachine>();
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
        }

        void Update()
        {
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

            Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            input += MobileInput.Move;                       // 合并虚拟摇杆
            input = Vector2.ClampMagnitude(input, 1f);
            Vector3 moveDir = CameraRelative(input);
            float speed = runSpeed * MoveSpeedMultiplier;

            if (_cc.isGrounded)
            {
                _vy = -1f;
                if (Input.GetKeyDown(KeyCode.Space) || MobileInput.GetDown("Jump")) _vy = jumpForce;
            }
            else _vy += gravity * dt;

            // 闪避（Shift）
            if ((Input.GetKeyDown(KeyCode.LeftShift) || MobileInput.GetDown("Dodge")) && Stats.SpendStamina(dodgeStaminaCost))
            {
                _dodgeDir = moveDir.sqrMagnitude > 0.01f ? moveDir : transform.forward;
                _dodgeTimer = dodgeDuration;
                _iframeTimer = dodgeIFrames;
                if (_combat != null) _combat.RequestState(CombatState.Dodge);
                return;
            }

            _cc.Move(moveDir * speed * dt + Vector3.up * _vy * dt);

            if (moveDir.sqrMagnitude > 0.01f)
            {
                Quaternion target = Quaternion.LookRotation(moveDir);
                transform.rotation = Quaternion.Slerp(transform.rotation, target, rotateSpeed * dt);
            }
        }

        Vector3 CameraRelative(Vector2 input)
        {
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
