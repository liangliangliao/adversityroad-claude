using UnityEngine;
using AdversityRoad.Mobile;

namespace AdversityRoad.Player
{
    /// <summary>
    /// 第三人称跟随镜头（防晕 v4）：
    /// - 位置临界阻尼软跟随（followSmoothTime 极短）：跟随点是一个平滑的
    ///   CameraTarget，而不是直接钉在角色身体上——用极短的临界阻尼滤掉角色
    ///   逐帧移动微抖动（这是移动晃屏的主因），同时几乎无滞后、不产生橡皮筋游动；
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
        [Header("战斗/锁定取景：让玩家与敌人同框居中，镜头降低、靠近")]
        [Tooltip("锁定时俯仰压低到该角度（战斗更贴地、更有压迫感与临场感）")]
        public float combatLockPitch = 4f;
        [Tooltip("锁定取景点偏向「玩家↔敌人中点」的比例：0=只看玩家，1=完全取中点")]
        [Range(0f, 0.8f)] public float lockCenterBias = 0.42f;
        [Tooltip("转角平滑时间（秒）：临界阻尼，越小越跟手")]
        public float rotationSmoothTime = 0.11f;
        [Tooltip("水平跟随平滑时间：极短的临界阻尼，滤掉角色移动的逐帧微抖动又几乎无滞后" +
                 "（抖动是逐帧高频信号衰减充分，稳态跟随仅微量拖尾，不产生橡皮筋）")]
        public float followSmoothTime = 0.05f;
        public float fieldOfView = 66f;

        [Header("跟拍者式自动回正（迟滞门限 + 临界阻尼弹簧，参考主流第三人称防晕运镜）：" +
                "只在玩家【大幅转向/掉头】后把镜头平滑转到移动方向的身后；" +
                "日常小幅转向/摇杆微调一律不动镜头——根治「摇杆一动整屏晃」")]
        public bool autoFollow = true;
        [Tooltip("停止手动转镜后隔多久才允许自动回正（避免与玩家转镜打架）")]
        public float autoFollowDelay = 0.3f;
        public float autoFollowSpeed = 50f;   // 锁定时追向敌人的转速
        [Tooltip("回正【启动】阈值：移动方向与镜头偏差超过此角度才开始回正——" +
                 "低于它的小幅转向/摇杆微调永不动镜头（防晕关键）")]
        public float followEngageAngle = 40f;
        [Tooltip("回正【停止】阈值：偏差小于此角度即停（迟滞，避免来回追与抖动）")]
        public float followDisengageAngle = 6f;
        [Tooltip("回正平滑时间（临界阻尼弹簧）：越小归位越快、越大越柔，无过冲无抖动")]
        public float followTurnSmoothTime = 0.34f;
        [Tooltip("回正最大转速（度/秒）：封顶，掉头也不会瞬甩")]
        public float followMaxSpeed = 150f;

        public PlayerController player;
        public LockOnSystem lockOn;

        float _yaw, _pitch = 10f;
        float _curYaw, _curPitch = 10f;
        float _yawVel, _pitchVel;
        float _boomDist, _boomVel;
        float _kick;
        float _yawFollowVel;               // 回正弹簧速度（SmoothDampAngle 用）
        bool _following;                   // 迟滞状态：是否正在回正
        float _lastManualLook;
        Vector3 _lastTargetPos;
        float _pivotY, _pivotYVel;         // 纵向软化：跳跃落地不硬拽镜头
        Vector2 _pivotXZ, _pivotXZVel;     // 水平软跟随：消除刚性同步放大的逐帧抖动
        float _pivotH = 1.55f;
        float _lenFactor = 1f;             // 动态构图：战斗拉近/疾跑拉远
        float _lockBlend;                  // 锁定取景渐入渐出，避免切锁瞬间跳镜
        bool _pivotInit;
        Transform _head;                    // 第一人称时隐藏头部（显露手臂与兵器）

        /// <summary>受击脉冲：小幅纵向颠簸，快速衰减（防晕：不做随机抖动）。</summary>
        public void Kick(float strength) => _kick = Mathf.Min(0.5f, Mathf.Max(_kick, strength * 0.5f));

        // 多视角预设（参考动作游戏惯例：近身看招 / 标准跟随 / 战术远景）
        struct CamPreset
        {
            public string name;
            public Vector3 offset;
            public float pitch;
            public bool fp;   // 第一人称
        }

        static readonly CamPreset[] Presets =
        {
            new CamPreset { name = "近身动作", offset = new Vector3(0.55f, 1.75f, -3.7f), pitch = 7f },
            new CamPreset { name = "标准跟随", offset = new Vector3(0.6f, 2.0f, -4.9f), pitch = 10f },
            new CamPreset { name = "战术远景", offset = new Vector3(0.25f, 3.3f, -7.0f), pitch = 21f },
            new CamPreset { name = "第一人称", offset = new Vector3(0, 0.75f, 0.1f), pitch = 2f, fp = true },
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

            // 只按水平位移判定移动：跳跃/台阶的纵向起伏不应误触发运镜与变焦
            Vector3 frameDelta = target.position - _lastTargetPos;
            frameDelta.y = 0;
            float moveSpeed = frameDelta.magnitude / dt;

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
                _following = false;
                _yawFollowVel = 0f;
            }
            else if (autoFollow)   // 所有视角（含第一人称）统一回正
            {
                // === 迟滞门限 + 临界阻尼弹簧回正 ===
                // 目标 = 玩家「移动方向」的身后。用速度方向（本帧水平位移）而非瞬时朝向，
                // 天然滤抖、更稳。核心是两个阈值构成的「迟滞」：
                //   · 偏差 > engage 才【启动】回正 → 小幅转向/摇杆微调（低于阈值）永不动镜头，
                //     这就是「摇杆一动整屏晃」的根治：日常走位镜头纹丝不动；
                //   · 一旦启动，持续到偏差 < disengage 才【停止】 → 掉头/大转向后一定回正到
                //     新的正前方（解决转身后镜头不归位）；
                //   · 回正用 SmoothDampAngle（临界阻尼，无过冲无抖动、自动缓入缓出、限速封顶）。
                bool moving = moveSpeed > 1.6f;
                bool manualRecently = Time.unscaledTime - _lastManualLook < autoFollowDelay;
                if (moving && !manualRecently && frameDelta.sqrMagnitude > 1e-4f)
                {
                    float wantYaw = Quaternion.LookRotation(frameDelta).eulerAngles.y;
                    float absDiff = Mathf.Abs(Mathf.DeltaAngle(_yaw, wantYaw));
                    if (!_following && absDiff > followEngageAngle) _following = true;
                    else if (_following && absDiff < followDisengageAngle) _following = false;

                    if (_following)
                        _yaw = Mathf.SmoothDampAngle(_yaw, wantYaw, ref _yawFollowVel,
                            followTurnSmoothTime, followMaxSpeed, dt);
                }
                else
                {
                    _following = false;
                    _yawFollowVel = 0f;
                }
            }

            // 锁定取景渐入渐出（切锁不跳镜）
            _lockBlend = Mathf.MoveTowards(_lockBlend, lockTarget != null ? 1f : 0f, dt / 0.5f);

            // 俯仰：锁定时压低到战斗视角（更贴地、更有临场感）；未锁定时闲置回中
            if (!Presets[PresetIndex].fp && Time.unscaledTime - _lastManualLook > 0.4f)
            {
                if (lockTarget != null)
                    _pitch = Mathf.MoveTowards(_pitch, combatLockPitch, 14f * dt);
                else if (Time.unscaledTime - _lastManualLook > pitchRecenterDelay && moveSpeed > 1.2f)
                    _pitch = Mathf.MoveTowards(_pitch, defaultPitch, 10f * dt);
            }

            _lastTargetPos = target.position;

            // ---- 临界阻尼转角（无过冲），位置刚性跟随（零滞后） ----
            _curYaw = Mathf.SmoothDampAngle(_curYaw, _yaw, ref _yawVel, rotationSmoothTime,
                Mathf.Infinity, dt);
            _curPitch = Mathf.SmoothDamp(_curPitch, _pitch, ref _pitchVel, rotationSmoothTime,
                Mathf.Infinity, dt);

            Quaternion rot = Quaternion.Euler(_curPitch, _curYaw, 0);

            // ---- 第一人称：镜头在眼位，隐藏头部，手臂与兵器动作直观可见 ----
            SetHeadVisible(!Presets[PresetIndex].fp);
            if (Presets[PresetIndex].fp)
            {
                Vector3 eye = target.position + Vector3.up * 0.75f + rot * new Vector3(0, 0, 0.12f);
                if (_kick > 0.001f)
                {
                    eye.y += Mathf.Sin(Time.unscaledTime * 34f) * _kick * 0.03f;
                    _kick = Mathf.MoveTowards(_kick, 0, dt * 2.2f);
                }
                transform.position = eye;
                transform.rotation = rot;
                return;
            }

            // 物理感取景：跟随点做临界阻尼软跟随（GDC 稳定镜头原则——不复制角色
            // 每个逐帧小动作）。水平用极短时间几乎无滞后但滤掉抖动，纵向更软，
            // 跳跃/落地/台阶时镜头柔和跟进。
            float wantH = player != null && player.IsCrouched ? 1.15f : 1.55f;
            wantH -= 0.22f * _lockBlend;   // 锁定时略降取景高度：镜头压低看清拳脚交锋
            _pivotH = Mathf.Lerp(_pivotH, wantH, 6f * dt);
            float targetPivotY = target.position.y + _pivotH;
            // 锁定时取景点偏向玩家↔敌人中点，让两人同时居中（近身仍以玩家为主，不贴边）
            Vector2 focusXZ = new Vector2(target.position.x, target.position.z);
            if (lockTarget != null)
            {
                Vector2 enemyXZ = new Vector2(lockTarget.position.x, lockTarget.position.z);
                focusXZ = Vector2.Lerp(focusXZ, (focusXZ + enemyXZ) * 0.5f, lockCenterBias * _lockBlend);
            }
            if (!_pivotInit) { _pivotY = targetPivotY; _pivotXZ = focusXZ; _pivotInit = true; }
            _pivotY = Mathf.SmoothDamp(_pivotY, targetPivotY, ref _pivotYVel, 0.13f,
                Mathf.Infinity, dt);
            _pivotXZ = Vector2.SmoothDamp(_pivotXZ, focusXZ, ref _pivotXZVel,
                followSmoothTime, Mathf.Infinity, dt);
            Vector3 pivot = new Vector3(_pivotXZ.x, _pivotY, _pivotXZ.y);

            // 电影感构图：锁定时按敌我距离取景（双人同框），疾跑微拉远
            float wantFactor;
            if (lockTarget != null)
            {
                // 贴近取景：近身缠斗时镜头压近看清拳脚细节，拉开时同框
                float enemyDist = Vector3.Distance(target.position, lockTarget.position);
                wantFactor = Mathf.Clamp(0.6f + enemyDist * 0.06f, 0.72f, 1.3f);
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

        void SetHeadVisible(bool visible)
        {
            if (_head == null && player != null)
            {
                var app = player.GetComponent<PlayerAppearance>();
                if (app != null && app.Rig != null) _head = app.Rig.head;
            }
            if (_head != null && _head.gameObject.activeSelf != visible)
                _head.gameObject.SetActive(visible);
        }
    }
}
