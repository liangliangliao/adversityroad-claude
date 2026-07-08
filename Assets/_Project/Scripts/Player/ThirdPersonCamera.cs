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
        public Vector3 offset = new Vector3(0.55f, 1.75f, -3.6f);
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
        // 悟空式取景：镜头近、视野窄（长焦感），角色占画面 1/3~1/2 高，人物有分量
        public float fieldOfView = 54f;

        [Header("镜头运镜规则（探索/战斗/大招三模式，参考主流第三人称防晕运镜）：" +
                "角色转向快、镜头位置中速跟随、镜头旋转慢——只有玩家【持续朝某方向移动一段" +
                "时间】镜头才慢慢转到身后；小幅左右调整绝不转镜头。遇敌自动切战斗镜头。")]
        public bool autoFollow = true;
        [Tooltip("停止手动转镜后隔多久才允许自动回正（避免与玩家转镜打架）")]
        public float autoFollowDelay = 0.3f;
        public float autoFollowSpeed = 50f;   // 战斗镜头追向敌人的转速

        [Header("探索镜头：玩家一转向，镜头立刻开始平稳缓慢地转到其背后（面朝方向）")]
        [Tooltip("镜头与角色朝向偏差小于此角度就不必回正（避免细抖）")]
        public float exploreReorientAngle = 6f;
        [Tooltip("回正平滑时间（临界阻尼弹簧）：偏大=更缓更稳。控制「缓慢跟随」的慢")]
        public float exploreTurnSmoothTime = 0.55f;
        [Tooltip("回正最大转速（度/秒）：封顶让掉头也平稳不猛甩")]
        public float exploreMaxSpeed = 85f;

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
            new CamPreset { name = "近身动作", offset = new Vector3(0.5f, 1.55f, -2.8f), pitch = 5f },
            new CamPreset { name = "标准跟随", offset = new Vector3(0.55f, 1.75f, -3.6f), pitch = 8f },
            new CamPreset { name = "战术远景", offset = new Vector3(0.3f, 2.7f, -5.4f), pitch = 17f },
            new CamPreset { name = "第一人称", offset = new Vector3(0, 0.75f, 0.1f), pitch = -8f, fp = true },
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
            if (cam != null)
            {
                cam.fieldOfView = fieldOfView;
                cam.nearClipPlane = 0.04f;   // 第一人称能看清眼前的手/兵器（否则被近裁剪面切掉）
            }
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
            // 第一人称放开俯仰范围：低头能看见自己的手/脚/剑，抬头能看见天空
            bool fpNow = Presets[PresetIndex].fp;
            _pitch = Mathf.Clamp(_pitch - lookY, fpNow ? -72f : minPitch, fpNow ? 80f : maxPitch);

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
                _yawFollowVel = 0f;
            }
            else if (autoFollow)
            {
                // 探索镜头：玩家一改变朝向，镜头【立刻开始】平稳缓慢地转到其背后
                // （面朝方向），无需先"持续朝一个方向走一段时间"。
                //   · 目标 = 角色朝向（PlayerController 已让角色即时朝移动方向），转身即跟；
                //   · 用大平滑时间的临界阻尼弹簧 SmoothDampAngle：小抖动只带来极轻微慢移
                //     不晃屏，大转向/掉头则平稳缓慢地归位到身后，绝不猛甩。
                bool moving = moveSpeed > 1.4f;
                bool manualRecently = Time.unscaledTime - _lastManualLook < autoFollowDelay;
                if (moving && !manualRecently)
                {
                    float heading = target.eulerAngles.y;   // 角色（=移动）正前方
                    if (Mathf.Abs(Mathf.DeltaAngle(_yaw, heading)) > exploreReorientAngle)
                        _yaw = Mathf.SmoothDampAngle(_yaw, heading, ref _yawFollowVel,
                            exploreTurnSmoothTime, exploreMaxSpeed, dt);
                }
                else
                {
                    _yawFollowVel = 0f;
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

            // ---- 第一人称：真实的「眼睛」视角 ----
            //   · 镜头在头部眼睛高度、脸的正前方（不在体内，平视看到前方而非自己身体）；
            //   · 俯仰自由：低头看见自己的手/脚/剑，抬头看见天空；
            //   · 只隐藏头部，躯干/手臂/腿/兵器都在——挥剑、踢腿时低头即可看见其在空中运动。
            SetHeadVisible(!Presets[PresetIndex].fp);
            if (Presets[PresetIndex].fp)
            {
                Quaternion fpRot = Quaternion.Euler(_curPitch, _curYaw, 0);
                // 眼位就在头部眼睛高度（不前移到身体前方，否则身体在镜头后方就看不见了）。
                // 身体位于镜头正下方 → 低头即见自己的躯干/手/腿/剑，抬头见天空。
                Vector3 eye = target.position + Vector3.up * 0.86f;
                if (_kick > 0.001f)
                {
                    eye.y += Mathf.Sin(Time.unscaledTime * 34f) * _kick * 0.03f;
                    _kick = Mathf.MoveTowards(_kick, 0, dt * 2.2f);
                }
                transform.position = eye;
                transform.rotation = fpRot;
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
                wantFactor = Mathf.Clamp(0.52f + enemyDist * 0.05f, 0.62f, 1.12f);
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
