using UnityEngine;
using AdversityRoad.Mobile;

namespace AdversityRoad.Player
{
    /// <summary>
    /// 第三人称跟随镜头（防晕 v3）：
    /// - 位置刚性跟随（零滞后）：只平滑"转角"，不平滑"位置"，
    ///   消除橡皮筋式游动感——这是眩晕的主因之一；
    /// - 转角用临界阻尼 SmoothDamp（无过冲、无回弹）；
    /// - FOV 固定 62°，永不动态变化；
    /// - 碰撞回缩快、伸出慢（避免镜头突然弹跳）；
    /// - 震屏改为幅度极小的纵向脉冲（禁用随机抖动）；
    /// - 专注低不再摇镜头（改由 HUD 暗角表达）；
    /// - 真机只读触屏转镜头区，灵敏度按屏高归一化并限幅。
    /// </summary>
    public class ThirdPersonCamera : MonoBehaviour
    {
        public Transform target;
        [Tooltip("电影感肩后构图：横向偏移让角色居于画面三分位（悟空式）")]
        public Vector3 offset = new Vector3(0.6f, 2.0f, -4.9f);
        public float mouseSensitivity = 3f;
        [Tooltip("触屏灵敏度：整屏高度拖动对应的旋转角度")]
        public float touchSensitivity = 190f;
        [Tooltip("俯仰限制收紧+闲时回中，避免卡在俯视角变成上帝视角")]
        public float minPitch = -18f, maxPitch = 38f;
        public float defaultPitch = 10f;   // 低平视角：地平线可见，画面有纵深电影感
        public float pitchRecenterDelay = 2.5f;
        [Tooltip("转角平滑时间（秒）：临界阻尼，越小越跟手")]
        public float rotationSmoothTime = 0.11f;
        public float fieldOfView = 66f;

        [Header("跟拍者式自动跟随：只在玩家背离镜头跑远时缓慢跟上；" +
                "左右转向/横移绝不转动镜头（符合真实摄影师行为）")]
        public bool autoFollow = true;
        public float autoFollowDelay = 1.2f;
        public float autoFollowSpeed = 50f;

        public PlayerController player;
        public LockOnSystem lockOn;

        float _yaw, _pitch = 10f;
        float _curYaw, _curPitch = 10f;
        float _yawVel, _pitchVel;
        float _boomDist, _boomVel;
        float _kick;
        float _followBlend;                // 自动跟随渐入渐出
        float _lastManualLook;
        Vector3 _lastTargetPos;
        float _pivotY, _pivotYVel;         // 纵向软化：跳跃落地不硬拽镜头
        float _pivotH = 1.55f;
        float _lenFactor = 1f;             // 动态构图：战斗拉近/疾跑拉远
        bool _pivotInit;

        /// <summary>受击脉冲：小幅纵向颠簸，快速衰减（防晕：不做随机抖动）。</summary>
        public void Kick(float strength) => _kick = Mathf.Min(0.5f, Mathf.Max(_kick, strength * 0.5f));

        // 多视角预设（参考动作游戏惯例：近身看招 / 标准跟随 / 战术远景）
        struct CamPreset
        {
            public string name;
            public Vector3 offset;
            public float pitch;
        }

        static readonly CamPreset[] Presets =
        {
            new CamPreset { name = "近身动作", offset = new Vector3(0.55f, 1.75f, -3.7f), pitch = 7f },
            new CamPreset { name = "标准跟随", offset = new Vector3(0.6f, 2.0f, -4.9f), pitch = 10f },
            new CamPreset { name = "战术远景", offset = new Vector3(0.25f, 3.3f, -7.0f), pitch = 21f },
        };

        public int PresetIndex { get; private set; } = 1;

        /// <summary>循环切换视角预设（「视角」按钮）。</summary>
        public void CyclePreset()
        {
            ApplyPreset((PresetIndex + 1) % Presets.Length, true);
        }

        void ApplyPreset(int idx, bool announce)
        {
            PresetIndex = Mathf.Clamp(idx, 0, Presets.Length - 1);
            var p = Presets[PresetIndex];
            offset = p.offset;
            defaultPitch = p.pitch;
            _pitch = p.pitch;
            _boomDist = offset.magnitude;
            PlayerPrefs.SetInt("cam_preset", PresetIndex);
            if (announce)
                Core.GameEvents.RaiseSubtitle("镜头视角：" + p.name);
        }

        void Awake()
        {
            var cam = GetComponent<Camera>();
            if (cam != null) cam.fieldOfView = fieldOfView;
            ApplyPreset(PlayerPrefs.GetInt("cam_preset", 1), false);
            _boomDist = offset.magnitude;
        }

        void LateUpdate()
        {
            if (target == null) return;
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0) return;

            // ---- 输入 ----
            Vector2 touch = MobileInput.ConsumeLook();
            float norm = touchSensitivity / Mathf.Max(1, Screen.height);
            float lookX = touch.x * norm;
            float lookY = touch.y * norm;
            if (!Application.isMobilePlatform)
            {
                lookX += Input.GetAxis("Mouse X") * mouseSensitivity;
                lookY += Input.GetAxis("Mouse Y") * mouseSensitivity;
            }
            lookX = Mathf.Clamp(lookX, -9f, 9f);
            lookY = Mathf.Clamp(lookY, -7f, 7f);

            if (Mathf.Abs(lookX) > 0.02f || Mathf.Abs(lookY) > 0.02f)
                _lastManualLook = Time.unscaledTime;

            _yaw += lookX;
            _pitch = Mathf.Clamp(_pitch - lookY, minPitch, maxPitch);

            float moveSpeed = (target.position - _lastTargetPos).magnitude / dt;

            // ---- 锁定运镜 / 自动跟随（渐入渐出，避免突然接管） ----
            Transform lockTarget = lockOn != null ? lockOn.CurrentTarget : null;
            if (lockTarget != null)
            {
                Vector3 toEnemy = lockTarget.position - target.position;
                toEnemy.y = 0;
                if (toEnemy.sqrMagnitude > 0.1f)
                {
                    float wantYaw = Quaternion.LookRotation(toEnemy).eulerAngles.y;
                    _yaw = Mathf.MoveTowardsAngle(_yaw, wantYaw, autoFollowSpeed * 1.4f * dt);
                }
                _followBlend = 0;
            }
            else if (autoFollow)
            {
                // 移动即回正：玩家一旦朝新方向移动，镜头迅速（但平滑）切到其身后
                // 展示新的前方远景；偏差越大追得越快；原地转身不动镜头
                Vector3 vel = (target.position - _lastTargetPos) / dt;
                vel.y = 0;
                bool moving = moveSpeed > 1.6f;
                bool wantFollow = Time.unscaledTime - _lastManualLook > autoFollowDelay && moving;
                _followBlend = Mathf.MoveTowards(_followBlend, wantFollow ? 1f : 0f, dt / 0.3f);
                if (_followBlend > 0.01f && vel.sqrMagnitude > 0.1f)
                {
                    float wantYaw = Quaternion.LookRotation(vel.normalized).eulerAngles.y;
                    float diff = Mathf.Abs(Mathf.DeltaAngle(_yaw, wantYaw));
                    float speed = Mathf.Lerp(45f, 160f, Mathf.InverseLerp(15f, 150f, diff));
                    _yaw = Mathf.MoveTowardsAngle(_yaw, wantYaw, speed * _followBlend * dt);
                }
            }

            // 俯仰角闲时缓慢回中：避免视角卡在高空俯视
            if (Time.unscaledTime - _lastManualLook > pitchRecenterDelay && moveSpeed > 1.2f)
                _pitch = Mathf.MoveTowards(_pitch, defaultPitch, 10f * dt);

            _lastTargetPos = target.position;

            // ---- 临界阻尼转角（无过冲），位置刚性跟随（零滞后） ----
            _curYaw = Mathf.SmoothDampAngle(_curYaw, _yaw, ref _yawVel, rotationSmoothTime,
                Mathf.Infinity, dt);
            _curPitch = Mathf.SmoothDamp(_curPitch, _pitch, ref _pitchVel, rotationSmoothTime,
                Mathf.Infinity, dt);

            Quaternion rot = Quaternion.Euler(_curPitch, _curYaw, 0);

            // 物理感取景：水平刚性跟随，纵向软化（GDC 稳定镜头原则——
            // 不复制角色每个纵向小动作，跳跃/落地时镜头柔和跟进）
            float wantH = player != null && player.IsCrouched ? 1.15f : 1.55f;
            _pivotH = Mathf.Lerp(_pivotH, wantH, 6f * dt);
            float targetPivotY = target.position.y + _pivotH;
            if (!_pivotInit) { _pivotY = targetPivotY; _pivotInit = true; }
            _pivotY = Mathf.SmoothDamp(_pivotY, targetPivotY, ref _pivotYVel, 0.13f,
                Mathf.Infinity, dt);
            Vector3 pivot = new Vector3(target.position.x, _pivotY, target.position.z);

            // 电影感构图：锁定时按敌我距离取景（双人同框），疾跑微拉远
            float wantFactor;
            if (lockTarget != null)
            {
                // 贴近取景：近身缠斗时镜头压近看清拳脚细节，拉开时同框
                float enemyDist = Vector3.Distance(target.position, lockTarget.position);
                wantFactor = Mathf.Clamp(0.68f + enemyDist * 0.07f, 0.8f, 1.35f);
            }
            else wantFactor = moveSpeed > 4.2f ? 1.05f : 1f;
            _lenFactor = Mathf.Lerp(_lenFactor, wantFactor, 1.8f * dt);

            Vector3 boomDir = (rot * offset).normalized;
            float maxDist = offset.magnitude * _lenFactor;

            // ---- 碰撞：回缩快、伸出慢，避免弹跳 ----
            float wantDist = maxDist;
            if (Physics.SphereCast(pivot, 0.25f, boomDir, out RaycastHit hit, maxDist))
                wantDist = Mathf.Max(0.5f, hit.distance - 0.1f);
            float smooth = wantDist < _boomDist ? 0.03f : 0.3f;
            _boomDist = Mathf.SmoothDamp(_boomDist, wantDist, ref _boomVel, smooth,
                Mathf.Infinity, dt);

            Vector3 pos = pivot + boomDir * _boomDist;

            // ---- 受击纵向脉冲（幅度小、衰减快） ----
            if (_kick > 0.001f)
            {
                pos.y += Mathf.Sin(Time.unscaledTime * 34f) * _kick * 0.06f;
                _kick = Mathf.MoveTowards(_kick, 0, dt * 2.2f);
            }

            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(pivot + Vector3.up * 0.25f - pos);
        }
    }
}
