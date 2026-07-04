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
        [Tooltip("水平位置跟随平滑时间（中速）：临界阻尼软跟随——不太快(否则复制抖动)、" +
                 "不太慢(否则玩家跑出画面)，滤掉逐帧微抖又稳稳跟住位置")]
        public float followSmoothTime = 0.09f;
        public float fieldOfView = 66f;

        [Header("镜头运镜规则（探索/战斗/大招三模式，参考主流第三人称防晕运镜）：" +
                "角色转向快、镜头位置中速跟随、镜头旋转慢——只有玩家【持续朝某方向移动一段" +
                "时间】镜头才慢慢转到身后；小幅左右调整绝不转镜头。遇敌自动切战斗镜头。")]
        public bool autoFollow = true;
        [Tooltip("停止手动转镜后隔多久才允许自动回正（避免与玩家转镜打架）")]
        public float autoFollowDelay = 0.3f;
        public float autoFollowSpeed = 50f;   // 战斗镜头追向敌人的转速

        [Header("探索镜头：角度基本固定，持续同向移动才慢速回正到身后")]
        [Tooltip("需持续朝同一方向移动多久，镜头才开始慢慢回正（秒）")]
        public float exploreSustainDelay = 0.9f;
        [Tooltip("移动方向抖动在此角度内视为「同一方向」；超过即重置计时（掉头/大调整会延迟回正）")]
        public float exploreHeadingBand = 30f;
        [Tooltip("镜头与移动方向偏差小于此角度就不必回正（避免细抖）")]
        public float exploreReorientAngle = 12f;
        [Tooltip("探索回正平滑时间：偏大=慢=稳（镜头旋转要慢）")]
        public float exploreTurnSmoothTime = 0.6f;
        [Tooltip("探索回正最大转速（度/秒）：很低，慢慢转过去不晃眼")]
        public float exploreMaxSpeed = 65f;

        [Header("大招镜头：短暂拉近，结束回稳（普通移动/普攻不触发）")]
        [Tooltip("大招时的取景距离系数（<1 拉近）")]
        public float ultimateZoom = 0.66f;

        public PlayerController player;
        public LockOnSystem lockOn;

        float _yaw, _pitch = 10f;
        float _curYaw, _curPitch = 10f;
        float _yawVel, _pitchVel;
        float _boomDist, _boomVel;
        float _kick;
        float _yawFollowVel;               // 回正弹簧速度（SmoothDampAngle 用）
        float _sustainT;                   // 已持续同向移动时长（探索回正的时间门限）
        float _smoothHeading, _headingVel; // 低通后的移动方向（判定"同一方向"用）
        bool _headingInit;
        float _ultimateTimer, _ultimateBlend;   // 大招镜头计时与渐入渐出
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

        /// <summary>大招镜头：短暂拉近取景（配合技能自身的轻微慢动作/命中小震），到点回稳。</summary>
        public void UltimateShot(float duration) => _ultimateTimer = Mathf.Max(_ultimateTimer, duration);

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

            // ---- 模式判定：大招 > 战斗（有敌可锁）> 探索 ----
            if (_ultimateTimer > 0f) _ultimateTimer -= dt;
            Transform lockTarget = lockOn != null ? lockOn.CurrentTarget : null;
            bool combat = lockTarget != null;
            bool ultimate = _ultimateTimer > 0f;

            if (combat)
            {
                // 战斗镜头：优先看敌人——镜头朝「玩家→敌人」方向对齐（中等速度、平滑不乱晃）。
                Vector3 toEnemy = lockTarget.position - target.position;
                toEnemy.y = 0;
                if (toEnemy.sqrMagnitude > 0.1f)
                {
                    float wantYaw = Quaternion.LookRotation(toEnemy).eulerAngles.y;
                    _yaw = Mathf.MoveTowardsAngle(_yaw, wantYaw, autoFollowSpeed * 1.4f * dt);
                }
                _sustainT = 0f; _headingInit = false; _yawFollowVel = 0f;
            }
            else if (autoFollow)
            {
                // 探索镜头：角度基本固定；只有玩家【持续朝同一方向移动超过 exploreSustainDelay】，
                // 镜头才【慢速】转到移动方向的身后。规则要点：
                //   · 移动方向抖动超过 exploreHeadingBand 即重置计时 → 左右小调整/摇杆微抖不转镜头；
                //   · 掉头时方向突变 → 计时清零 → 角色可立刻回头，但镜头延迟一会儿才慢慢跟；
                //   · 回正用大平滑时间 + 低转速上限的 SmoothDampAngle，转得很慢、不晃眼。
                bool moving = moveSpeed > 1.6f;
                bool manualRecently = Time.unscaledTime - _lastManualLook < autoFollowDelay;
                if (moving && !manualRecently && frameDelta.sqrMagnitude > 1e-4f)
                {
                    float heading = Quaternion.LookRotation(frameDelta).eulerAngles.y;
                    if (!_headingInit) { _smoothHeading = heading; _headingInit = true; }
                    float headingJitter = Mathf.Abs(Mathf.DeltaAngle(heading, _smoothHeading));
                    _smoothHeading = Mathf.SmoothDampAngle(_smoothHeading, heading,
                        ref _headingVel, 0.18f, Mathf.Infinity, dt);
                    // 方向稳定→累积时长；方向大变（转向/掉头）→清零，延迟镜头跟随
                    _sustainT = headingJitter < exploreHeadingBand ? _sustainT + dt : 0f;

                    float yawOffset = Mathf.Abs(Mathf.DeltaAngle(_yaw, _smoothHeading));
                    if (_sustainT >= exploreSustainDelay && yawOffset > exploreReorientAngle)
                        _yaw = Mathf.SmoothDampAngle(_yaw, _smoothHeading, ref _yawFollowVel,
                            exploreTurnSmoothTime, exploreMaxSpeed, dt);
                }
                else
                {
                    _sustainT = 0f; _headingInit = false; _yawFollowVel = 0f;
                }
            }
            _ultimateBlend = Mathf.MoveTowards(_ultimateBlend, ultimate ? 1f : 0f, dt / 0.25f);

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
            // 大招镜头：短暂拉近（覆盖当前构图，结束自动回稳）
            wantFactor = Mathf.Lerp(wantFactor, ultimateZoom, _ultimateBlend);
            _lenFactor = Mathf.Lerp(_lenFactor, wantFactor, 2.2f * dt);

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
