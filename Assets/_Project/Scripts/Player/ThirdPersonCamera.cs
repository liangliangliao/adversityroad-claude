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
        [Tooltip("电影感肩后构图：横向偏移让角色居于画面三分位（悟空式）。" +
                 "高度贴近肩线（不架高）：配合近水平俯仰，地平线/天空始终在画面上部，" +
                 "画面有纵深不压抑——黑猴战斗镜头的核心是【低机位+平视】而非俯拍")]
        public Vector3 offset = new Vector3(0.45f, 1.15f, -4.6f);
        public float mouseSensitivity = 3f;
        [Tooltip("触屏灵敏度：整屏高度拖动对应的旋转角度")]
        public float touchSensitivity = 190f;
        [Tooltip("俯仰限制收紧+闲时回中，避免卡在俯视角变成上帝视角")]
        public float minPitch = -18f, maxPitch = 38f;
        public float defaultPitch = 4f;   // 近水平视角：地平线在画面上三分位，纵深开阔
        public float pitchRecenterDelay = 2.5f;
        [Header("战斗/锁定取景：让玩家与敌人同框居中，人物大而全身可见")]
        [Tooltip("锁定时俯仰回到该角度：近水平（黑猴式）——双方全身、地面与远景同框，" +
                 "绝不俯拍成'满屏地板'的压抑构图")]
        public float combatLockPitch = 5f;
        [Tooltip("锁定取景点偏向「玩家↔敌人中点」的比例：0=只看玩家，1=完全取中点")]
        [Range(0f, 0.8f)] public float lockCenterBias = 0.34f;
        [Tooltip("转角平滑时间（秒）：临界阻尼，越小越跟手")]
        public float rotationSmoothTime = 0.11f;
        [Tooltip("水平位置跟随平滑时间（中速）：临界阻尼软跟随——不太快(否则复制抖动)、" +
                 "不太慢(否则玩家跑出画面)，滤掉逐帧微抖又稳稳跟住位置")]
        public float followSmoothTime = 0.09f;
        // 悟空式取景：长焦感让人物有分量（2.3m 角色约占屏高一半），
        // 距离保持中景不贴脸——全身+周边环境始终可见，画面不压低不狭窄
        public float fieldOfView = 56f;

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
        [Tooltip("大招时的取景距离系数（<1 拉近；幅度克制，不掉转/不猛切镜头）")]
        public float ultimateZoom = 0.82f;

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
        Vector2 _focusAnchor;              // 焦点死区锚：小位移不推镜（电影三脚架感）
        Vector3 _planarVel;                // 玩家水平速度（移动构图的引导留白用）
        Combat.CombatStateMachine _playerFsm;   // 临战判定（未锁定的战斗回正用）
        bool _combatReorient;              // 战斗回正进行中（迟滞开关防小幅摆镜）
        float _pivotH = 0.42f;
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
            new CamPreset { name = "近身动作", offset = new Vector3(0.45f, 1.0f, -3.8f), pitch = 3f },
            new CamPreset { name = "标准跟随", offset = new Vector3(0.45f, 1.15f, -4.6f), pitch = 4f },
            new CamPreset { name = "战术远景", offset = new Vector3(0.3f, 1.7f, -5.9f), pitch = 9f },
            new CamPreset { name = "第一人称", offset = new Vector3(0, 0.75f, 0.1f), pitch = -8f, fp = true },
        };

        public int PresetIndex { get; private set; } = 1;

        /// <summary>当前是否第一人称（近镜角色淡出要跳过玩家本体）。</summary>
        public bool FirstPerson => Presets[PresetIndex].fp;

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
            //（首帧尚未初始化跟踪点时视为静止，避免出生瞬间的伪速度触发运镜）
            Vector3 frameDelta = _pivotInit ? target.position - _lastTargetPos : Vector3.zero;
            frameDelta.y = 0;
            float moveSpeed = frameDelta.magnitude / dt;
            // 平滑后的水平速度向量（供移动构图的"引导留白"使用，滤掉逐帧抖动）
            _planarVel = Vector3.Lerp(_planarVel, frameDelta / dt, 5f * dt);

            // ---- 模式判定：大招 > 战斗（有敌可锁）> 探索 ----
            if (_ultimateTimer > 0f) _ultimateTimer -= dt;
            Transform lockTarget = lockOn != null ? lockOn.CurrentTarget : null;
            bool combat = lockTarget != null;
            bool ultimate = _ultimateTimer > 0f;

            if (combat)
            {
                // 战斗镜头（过肩对峙位，参考 Souls/悟空）：镜头朝「玩家→敌人」方向对齐。
                // 电影稳定原则：小角度偏差不纠偏（死区防微振），大偏差按比例加速追——
                // 敌人绕背才快速转过去，近身缠斗的小幅换位绝不来回摆镜。
                Vector3 toEnemy = lockTarget.position - target.position;
                toEnemy.y = 0;
                if (toEnemy.sqrMagnitude > 0.1f)
                {
                    float wantYaw = Quaternion.LookRotation(toEnemy).eulerAngles.y;
                    float err = Mathf.DeltaAngle(_yaw, wantYaw);
                    if (Mathf.Abs(err) > 4f)
                    {
                        float spd = Mathf.Min(autoFollowSpeed * 1.6f, Mathf.Abs(err) * 2.2f);
                        _yaw = Mathf.MoveTowardsAngle(_yaw, wantYaw, spd * dt);
                    }
                }
                _yawFollowVel = 0f;
            }
            else if (autoFollow)
            {
                bool moving = moveSpeed > 1.4f;
                bool manualRecently = Time.unscaledTime - _lastManualLook < autoFollowDelay;

                // 战斗回正（未锁定）：手动锁定模式下没有锁定目标时，玩家原地换向
                // ——比如从打前方敌人瞬间转身打背后的敌人——镜头也要跟着转到其
                // 身后，把新交战方向的敌人框进画面。带迟滞开关：偏差 >40° 才开始
                // 回正、追到 <10° 停——近身缠斗的小幅换位绝不来回摆镜。
                if (_playerFsm == null && player != null)
                    _playerFsm = player.GetComponent<Combat.CombatStateMachine>();
                bool fighting = _playerFsm != null && _playerFsm.InCombat;
                if (fighting && !manualRecently)
                {
                    float heading = target.eulerAngles.y;   // 角色出招朝向（磁吸已面向交战敌人）
                    float err = Mathf.Abs(Mathf.DeltaAngle(_yaw, heading));
                    if (err > 40f) _combatReorient = true;
                    else if (err < 10f) _combatReorient = false;
                    if (_combatReorient)
                    {
                        // 比探索回正更快（0.22s 阻尼、封顶 260°/s）：转身打背后的敌人
                        // 约半秒内完成取景，能立刻看清并确认新目标
                        _yaw = Mathf.SmoothDampAngle(_yaw, heading, ref _yawFollowVel,
                            0.22f, 260f, dt);
                    }
                }
                // 探索镜头：玩家一改变朝向，镜头【立刻开始】平稳缓慢地转到其背后
                // （面朝方向），无需先"持续朝一个方向走一段时间"。
                //   · 目标 = 角色朝向（PlayerController 已让角色即时朝移动方向），转身即跟；
                //   · 用大平滑时间的临界阻尼弹簧 SmoothDampAngle：小抖动只带来极轻微慢移
                //     不晃屏，大转向/掉头则平稳缓慢地归位到身后，绝不猛甩。
                else if (moving && !manualRecently)
                {
                    _combatReorient = false;
                    float heading = target.eulerAngles.y;   // 角色（=移动）正前方
                    if (Mathf.Abs(Mathf.DeltaAngle(_yaw, heading)) > exploreReorientAngle)
                        _yaw = Mathf.SmoothDampAngle(_yaw, heading, ref _yawFollowVel,
                            exploreTurnSmoothTime, exploreMaxSpeed, dt);
                }
                else
                {
                    _combatReorient = false;
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
                Vector3 eye = target.position + Vector3.up * 1.0f;
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
            // 取景点=胸口：target.position 是胶囊【中心】（已在脚底上方约 1m），
            // 只需再加小量到胸口。旧值 +1.55 把取景点抬到头顶之上，导致画面被
            // 压低、角色挤在屏幕下缘只见上半身——这是"镜头压太低"的根因。
            float wantH = player != null && player.IsCrouched ? 0.05f : 0.42f;
            _pivotH = Mathf.Lerp(_pivotH, wantH, 6f * dt);
            float targetPivotY = target.position.y + _pivotH;
            // 锁定时取景点偏向玩家↔敌人中点，让两人同时居中（近身仍以玩家为主，不贴边）
            Vector2 focusXZ = new Vector2(target.position.x, target.position.z);
            if (lockTarget != null)
            {
                Vector2 enemyXZ = new Vector2(lockTarget.position.x, lockTarget.position.z);
                focusXZ = Vector2.Lerp(focusXZ, (focusXZ + enemyXZ) * 0.5f, lockCenterBias * _lockBlend);
            }
            else if (moveSpeed > 1.5f)
            {
                // 移动构图·引导留白（电影 lead room）：奔跑时焦点向移动方向前移，
                // 角色让出前方画面空间——观众能看见"要去哪"，构图更有方向感。
                float lead = Mathf.Clamp01(moveSpeed / 5.2f) * 0.45f;
                Vector2 vdir = new Vector2(_planarVel.x, _planarVel.z);
                if (vdir.sqrMagnitude > 0.04f) focusXZ += vdir.normalized * lead;
            }
            if (!_pivotInit) { _pivotY = targetPivotY; _pivotXZ = focusXZ; _focusAnchor = focusXZ; _pivotInit = true; }

            // 电影三脚架感·焦点死区：小于死区的焦点位移完全不推镜——近身互殴时
            // 拳脚带来的细碎换位（突进/击退/侧闪的残余）不再传导成镜头晃动；
            // 只有真正的走位才移镜。战斗死区大（稳如三脚架），探索死区小（跟手）。
            float dead = Mathf.Lerp(0.03f, 0.15f, _lockBlend);
            Vector2 drift = focusXZ - _focusAnchor;
            if (drift.magnitude > dead) _focusAnchor = focusXZ - drift.normalized * dead;

            _pivotY = Mathf.SmoothDamp(_pivotY, targetPivotY, ref _pivotYVel, 0.13f,
                Mathf.Infinity, dt);
            // 战斗中位置阻尼加重（斯坦尼康式慢移），探索保持跟手
            float fst = Mathf.Lerp(followSmoothTime, 0.24f, _lockBlend);
            _pivotXZ = Vector2.SmoothDamp(_pivotXZ, _focusAnchor, ref _pivotXZVel,
                fst, Mathf.Infinity, dt);
            Vector3 pivot = new Vector3(_pivotXZ.x, _pivotY, _pivotXZ.y);

            // 电影感构图：锁定时按敌我距离取景（双人同框），疾跑微拉远
            float wantFactor;
            if (lockTarget != null)
            {
                // 战斗取景：近身不再压到贴脸（旧下限 0.62 是"视野狭窄/看不到全身"
                // 的根因之一），保持中景看清双方全身与拳脚，拉开时略拉远同框
                float enemyDist = Vector3.Distance(target.position, lockTarget.position);
                wantFactor = Mathf.Clamp(0.8f + enemyDist * 0.05f, 0.92f, 1.28f);
            }
            else wantFactor = 1f;   // 疾跑不再微调焦距：任何"呼吸式"变焦都读作不稳
            // 大招镜头：短暂拉近（覆盖当前构图，结束自动回稳）
            wantFactor = Mathf.Lerp(wantFactor, ultimateZoom, _ultimateBlend);
            // 变焦极慢（电影推轨是分镜级动作，不是逐帧伺服）：缠斗中距离忽近忽远
            // 不再造成镜头前后泵动
            _lenFactor = Mathf.Lerp(_lenFactor, wantFactor, 1.1f * dt);

            Vector3 boomDir = (rot * offset).normalized;
            float maxDist = offset.magnitude * _lenFactor;

            // ---- 碰撞：回缩快、伸出慢，避免弹跳 ----
            // 只对【环境】做遮挡回缩：忽略触发器（受击/攻击判定盒）、玩家与敌人的
            // 身体胶囊、飞散的物理碎屑——此前近身缠斗时敌人身体反复穿过吊杆，
            // 镜头被迫急缩急伸，这是"互击时镜头严重晃动"的最大来源。
            float wantDist = maxDist;
            var occluders = Physics.SphereCastAll(pivot, 0.25f, boomDir, maxDist,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (var hit in occluders)
            {
                if (hit.distance <= 0.001f) continue;                       // 起点内嵌，忽略
                var col = hit.collider;
                if (col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic)
                    continue;                                               // 飞散碎屑
                if (col.GetComponentInParent<PlayerController>() != null) continue;
                if (col.GetComponentInParent<AI.EnemyController>() != null) continue;
                // 回缩下限抬高：贴墙也绝不缩进角色身体里（缩得再近由
                // CharacterCloseFade 把角色淡透，不出现"整屏白模糊脸"）
                wantDist = Mathf.Min(wantDist, Mathf.Max(1.35f, hit.distance - 0.1f));
            }
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
            // 视线目标略高于取景点（锁定时再抬一点）：角色落于画面下半部，
            // 上半部留给天空/远景——开阔的黑猴式构图，而非满屏地板
            float lookUp = 0.38f + 0.12f * _lockBlend;
            transform.rotation = Quaternion.LookRotation(pivot + Vector3.up * lookUp - pos);
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
