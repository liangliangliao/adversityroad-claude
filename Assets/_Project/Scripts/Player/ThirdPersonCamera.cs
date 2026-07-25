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
        [Tooltip("停止手动转镜后隔多久才允许自动回正（避免与玩家转镜打架）。" +
                 "0.3s 过短——玩家刚手动摆好角度，镜头立刻又自己转回去，读作'和我抢镜头'；" +
                 "1.2s 让玩家的手动取景有足够的停留时间")]
        public float autoFollowDelay = 1.2f;
        public float autoFollowSpeed = 50f;   // 战斗镜头追向敌人的转速

        [Header("探索镜头：玩家一转向，镜头立刻开始平稳缓慢地转到其背后（面朝方向）")]
        [Tooltip("镜头与角色朝向偏差小于此角度就不必回正（死区放大：细碎修正完全不动镜头）")]
        public float exploreReorientAngle = 10f;
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
        bool _combatReorient;              // 大幅换向追击中（迟滞开关防小幅摆镜）
        float _yawErr;                     // 本帧镜头与角色朝向的偏差（大幅转向时拉远视野用）
        float _towardCamT;                 // 持续朝镜头行进的时长（区分"转身看一眼"与"真要往那走"）
        // ---- 镜头导演：景别选择与平滑过渡 ----
        ShotProfile _shot;                 // 当前生效的景别（插值后的实时值）
        float _nextShotScan;               // 敌情扫描节流
        int _nearbyEnemies;
        float _roomAround = 99f;           // 镜头四周可用空间（识别狭窄场地）
        bool _shotInit;
        /// <summary>朝镜头持续行进多久才认定"真要往那个方向去"，才允许绕镜。
        /// 低于此值一律视为玩家在看正脸/调整站位，镜头保持不动。
        /// 0.9→0.6：有了威胁感知兜底，这里不必再压那么久。</summary>
        const float TowardCameraHold = 0.6f;
        readonly System.Collections.Generic.List<float> _threatDirs =
            new System.Collections.Generic.List<float>();   // 附近敌人的方位角（0.3s 刷新）

        /// <summary>该方位角附近是否有敌人（威胁感知：决定镜头该不该赶紧转过去）。</summary>
        bool ThreatNear(float yawDeg, float coneDeg)
        {
            for (int i = 0; i < _threatDirs.Count; i++)
                if (Mathf.Abs(Mathf.DeltaAngle(_threatDirs[i], yawDeg)) < coneDeg) return true;
            return false;
        }
        // 朝向防抖（魂系/战神跟随镜头的通行做法）：镜头永远追【低通滤波后的朝向】，
        // 并且只有玩家朝一个方向【持续稳定一小段时间】才开始回正——摇杆快速搓动、
        // 出招磁吸的瞬间换向都被滤在镜头之外，不再逐帧牵动镜头来回摆。
        float _headingAvg;                 // 平滑朝向（低通滤波，镜头的实际追踪目标）
        float _headingHoldT;               // 当前朝向的稳定时长（大幅瞬转会清零重计）
        float _lastHeading;
        bool _headingInit;
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

            // ---- 朝向防抖滤波（所有自动回正共用）----
            float rawHeading = target.eulerAngles.y;
            if (!_headingInit)
            {
                _headingInit = true;
                _headingAvg = rawHeading;
                _lastHeading = rawHeading;
            }
            // 角速度检测：单帧瞬转（出招磁吸换向/摇杆猛搓）视为"方向还没定下来"，
            // 稳定计时清零；只有连续低角速度才累计稳定时长
            float headingRate = Mathf.Abs(Mathf.DeltaAngle(_lastHeading, rawHeading)) / dt;
            if (headingRate > 220f) _headingHoldT = 0f;
            else _headingHoldT += dt;
            _lastHeading = rawHeading;
            // 自适应低通滤波：朝向还在乱动时收敛慢（滤掉摇杆抖动），
            // 一旦朝向稳定下来就快速收敛——避免"转完身还要等滤波追上"的盲区延迟
            float lpRate = Mathf.Lerp(5f, 18f, Mathf.Clamp01(_headingHoldT / 0.25f));
            _headingAvg = Mathf.LerpAngle(_headingAvg, rawHeading, 1f - Mathf.Exp(-lpRate * dt));

            // ---- 模式判定：大招 > 战斗（有敌可锁）> 探索 ----
            if (_ultimateTimer > 0f) _ultimateTimer -= dt;

            // ===== 镜头导演：选景别 + 平滑推轨过渡 =====
            // 敌情与场地扫描节流（0.3s 一次，避免每帧全场遍历）
            if (Time.unscaledTime >= _nextShotScan)
            {
                _nextShotScan = Time.unscaledTime + 0.3f;
                _nearbyEnemies = 0;
                _threatDirs.Clear();
                foreach (var e in Object.FindObjectsOfType<AI.EnemyController>())
                {
                    if (e.State == AI.EnemyState.Dead) continue;
                    Vector3 to = e.transform.position - target.position; to.y = 0;
                    float d2 = to.sqrMagnitude;
                    if (d2 < 81f) _nearbyEnemies++;
                    // 威胁方位缓存（14m 内）：供每帧判断"我正在转向的方向上有没有敌人"
                    if (d2 < 196f && d2 > 0.01f)
                        _threatDirs.Add(Quaternion.LookRotation(to.normalized).eulerAngles.y);
                }
                // 场地宽敞度：向镜头四周投射，取最短可用距离——识别走廊/贴墙等狭窄场地
                _roomAround = ProbeRoom(target.position + Vector3.up * _pivotH);
            }
            var wantShot = CameraDirector.Pick(
                _ultimateTimer > 0f, _nearbyEnemies,
                lockOn != null && lockOn.CurrentTarget != null, _roomAround);
            if (!_shotInit) { _shot = wantShot; _shotInit = true; }
            else
            {
                // 参数级插值＝推轨，而不是切镜：这是"平稳、不适感为零"的关键
                float rate = CameraDirector.BlendRate(_shot, wantShot);
                _shot = ShotProfile.Lerp(_shot, wantShot, 1f - Mathf.Exp(-rate * dt));
            }
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
                bool moving = moveSpeed > 0.6f;
                bool manualRecently = Time.unscaledTime - _lastManualLook < autoFollowDelay;
                if (_playerFsm == null && player != null)
                    _playerFsm = player.GetComponent<Combat.CombatStateMachine>();
                bool fighting = _playerFsm != null && _playerFsm.InCombat;
                // 「正在推杆」也算主动意图：出招/技能期间角色只转不移动（移动被定步锁住），
                // 若只看 moveSpeed 会误判成静止而完全不回正——这正是"出招中转向后看不见前方"的缺口
                bool steering = player != null && player.StickWorldDir.sqrMagnitude > 0.04f;
                bool active = moving || steering;

                // ===== 统一渐进回正（转向尺度 → 镜头同步幅度，连续映射，无分档断层）=====
                // 此前战斗/探索是互斥的两条分支：战斗中只有 >55° 才回正，
                // 12°—55° 的中等转向【完全没有跟随】——战斗中长期存在的部分盲区。
                // 现在合并为一条：
                //   · 中小幅转向（>10°）+ 有移动或推杆 → 温和跟随，越大越快；
                //   · 大幅换向（战斗 >55° / 探索 >45°）→ 迟滞开关锁定追击，直到 <12° 才松开，
                //     保证掉头/转身打背后敌人时迅速把新方向框进画面；
                //   · 手动转镜期间一律让位给玩家。
                float err = Mathf.Abs(Mathf.DeltaAngle(_yaw, _headingAvg));

                // ===== 意图识别 ①：「朝镜头走来」≠「要把镜头甩到背后」=====
                // err≈180° 意味着角色正朝镜头方向来。此前一律读作"要换方向"，于是玩家
                // 一转身想看正脸、或贴墙后退调整站位，镜头立刻绕到背后——完全违背意图。
                // 大作的判据是【是否持续行进】而非【是否转了身】：
                //   · 只是转身看一眼／短暂后退 → 不满足持续时间 → 镜头岿然不动，看得到正脸；
                //   · 真的一直朝镜头跑（要往那个方向去） → 累积够时间后镜头才缓缓绕过去，
                //     避免角色跑出画面。
                // 战斗中豁免（背后有敌人时必须迅速看到），仍走快速追击。
                // 威胁优先：正在转向的方向上有敌人时，"看清那边"压倒"看正脸"。
                // 这是上一版遗留的关键漏洞——为了让面壁看正脸不被绕镜，会把
                // 「向前突然改为向后逃跑/迎击追兵」也一并抑制 0.9 秒，导致后方
                // 跟随的敌人进入盲区。两者输入相同、意图相反，唯一可靠的区分
                // 就是【那个方向上有没有威胁】。
                bool threatAhead = ThreatNear(_headingAvg, 70f);
                bool towardCamera = err > 130f;
                if (towardCamera && moving && !fighting) _towardCamT += dt;
                else _towardCamT = 0f;
                bool backingIntent = towardCamera && !fighting && !threatAhead
                                     && _towardCamT < TowardCameraHold;

                float bigAngle = fighting ? 55f : 45f;
                float holdNeed = fighting ? 0.24f : 0.15f;
                // 那个方向有敌人：几乎立刻开始转过去（把"发现追兵"的延迟压到最低）
                if (threatAhead) { holdNeed = 0.06f; bigAngle = 35f; }
                if (err > bigAngle && _headingHoldT > holdNeed && !backingIntent) _combatReorient = true;
                else if (err < 12f || _headingHoldT < 0.1f || backingIntent) _combatReorient = false;

                bool gentle = active && _headingHoldT > 0.15f && err > exploreReorientAngle
                              && !backingIntent;

                if (!manualRecently && (_combatReorient || gentle))
                {
                    // 0°→180° 连续映射：平滑时间 0.5s→0.15s、转速上限 70→340°/s。
                    // 小幅修正依旧慢而稳（防抖），大角度掉头迅速跟上（防盲区）。
                    float t = Mathf.Clamp01((err - exploreReorientAngle) / 130f);
                    float smoothT = Mathf.Lerp(exploreTurnSmoothTime, 0.15f, t);
                    float maxSpd = Mathf.Lerp(70f, 340f, t);

                    // ===== 意图识别 ②：不要把镜头甩进墙里 =====
                    // 玩家面壁转身时，"绕到角色背后"恰好是墙的方向——硬绕过去只会让镜头
                    // 顶在墙上被迫贴脸，视野瞬间塌掉。大幅回正前先探一下目标方位是否够宽敞：
                    // 越憋屈就转得越慢（最低降到 25%），把镜头留在看得见的地方。
                    if (err > 45f)
                    {
                        Vector3 probePivot = target.position + Vector3.up * _pivotH;
                        float freeAtTarget = FreeBoomDistance(probePivot, _headingAvg,
                                                              offset.magnitude * _lenFactor);
                        float room = Mathf.InverseLerp(1.8f, 3.2f, freeAtTarget);   // 0=贴墙 1=开阔
                        float damp = Mathf.Lerp(0.25f, 1f, room);
                        maxSpd *= damp;
                        smoothT /= Mathf.Max(0.25f, damp);
                    }

                    _yaw = Mathf.SmoothDampAngle(_yaw, _headingAvg, ref _yawFollowVel,
                        smoothT, maxSpd, dt);
                }
                else _yawFollowVel = 0f;
                _yawErr = err;   // 供下方「大幅转向时轻微拉远视野」使用
            }
            _ultimateBlend = Mathf.MoveTowards(_ultimateBlend, ultimate ? 1f : 0f, dt / 0.25f);

            // 锁定取景渐入渐出（切锁不跳镜）
            _lockBlend = Mathf.MoveTowards(_lockBlend, lockTarget != null ? 1f : 0f, dt / 0.5f);

            // 俯仰：锁定时压低到战斗视角（更贴地、更有临场感）；未锁定时闲置回中
            if (!Presets[PresetIndex].fp && Time.unscaledTime - _lastManualLook > 0.4f)
            {
                // 目标俯仰＝基准 + 当前景别的俯仰偏置（群战略俯看局势、特写略压低）
                if (lockTarget != null)
                    _pitch = Mathf.MoveTowards(_pitch, combatLockPitch + _shot.pitchBias, 14f * dt);
                else if (Time.unscaledTime - _lastManualLook > pitchRecenterDelay && moveSpeed > 1.2f)
                    _pitch = Mathf.MoveTowards(_pitch, defaultPitch + _shot.pitchBias, 10f * dt);
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
            float wantH = (player != null && player.IsCrouched ? 0.05f : 0.42f) + _shot.heightBias;
            _pivotH = Mathf.Lerp(_pivotH, wantH, 6f * dt);
            float targetPivotY = target.position.y + _pivotH;
            // 锁定时取景点偏向玩家↔敌人中点，让两人同时居中（近身仍以玩家为主，不贴边）
            Vector2 focusXZ = new Vector2(target.position.x, target.position.z);
            if (lockTarget != null)
            {
                Vector2 enemyXZ = new Vector2(lockTarget.position.x, lockTarget.position.z);
                focusXZ = Vector2.Lerp(focusXZ, (focusXZ + enemyXZ) * 0.5f,
                    Mathf.Max(lockCenterBias, _shot.centerBias) * _lockBlend);
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
            float fst = Mathf.Lerp(followSmoothTime, 0.24f, _lockBlend) * _shot.damping;
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
            else
            {
                // 视野开阔化（未锁定时）：疾跑与大幅转向各给一点点拉远，让"要去的方向"
                // 有更多可见余量——转身/掉头的瞬间正是最需要看清周围的时刻。
                // 幅度克制（合计 ≤ +12%）且变焦本身极慢（下方 1.1/s 插值），
                // 不会形成"呼吸式"变焦那种不稳感。
                float runOut = Mathf.Clamp01(moveSpeed / 5.2f) * 0.06f;
                float turnOut = Mathf.Clamp01(_yawErr / 120f) * 0.06f;
                wantFactor = 1f + runOut + turnOut;
            }
            // 大招镜头：短暂拉近（覆盖当前构图，结束自动回稳）
            wantFactor = Mathf.Lerp(wantFactor, ultimateZoom, _ultimateBlend);
            wantFactor *= _shot.distanceMult;   // 景别决定景深：群战拉远、狭窄收紧、决胜推近
            // 变焦极慢（电影推轨是分镜级动作，不是逐帧伺服）：缠斗中距离忽近忽远
            // 不再造成镜头前后泵动
            _lenFactor = Mathf.Lerp(_lenFactor, wantFactor, 1.1f * dt);

            Vector3 boomDir = (rot * offset).normalized;
            float maxDist = offset.magnitude * _lenFactor;

            // ---- 碰撞：回缩快、伸出慢，避免弹跳 ----
            // 只对【环境】做遮挡回缩：忽略触发器（受击/攻击判定盒）、玩家与敌人的
            // 身体胶囊、飞散的物理碎屑——此前近身缠斗时敌人身体反复穿过吊杆，
            // 镜头被迫急缩急伸，这是"互击时镜头严重晃动"的最大来源。
            // 探测球略缩小（0.25→0.18）：转身时吊杆扫过墙角/柱子不再因"擦边"就大幅回缩，
            // 减少「一转身视野突然变窄」的误触发
            float wantDist = maxDist;
            var occluders = Physics.SphereCastAll(pivot, 0.18f, boomDir, maxDist,
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
                wantDist = Mathf.Min(wantDist, Mathf.Max(1.6f, hit.distance - 0.1f));
            }
            // 回缩仍然快（避免穿墙），但【伸出恢复】明显加快（0.3→0.14s）：
            // 转身扫过障碍后视野立刻回到正常景别，不再长时间贴脸发窄
            float smooth = wantDist < _boomDist ? 0.03f : 0.14f;
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

            // 景别的焦段：群战广角看局势、决胜长焦压缩更有分量。
            // 变焦本身极慢（跟随 _shot 的插值），不会形成"呼吸式"变焦的不稳感。
            var camc = GetComponent<Camera>();
            if (camc != null && !Presets[PresetIndex].fp)
                camc.fieldOfView = Mathf.MoveTowards(camc.fieldOfView,
                    fieldOfView + _shot.fovBias, 12f * dt);
        }

        /// <summary>
        /// 场地宽敞度：向四周（含斜后方）投射，取最短可用距离。
        /// 用于识别走廊/贴墙等狭窄场地，让导演切到「收紧贴身」景别，
        /// 而不是傻乎乎地拉远把镜头顶进墙里。
        /// </summary>
        float ProbeRoom(Vector3 pivot)
        {
            float shortest = 99f;
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f;
                Vector3 dir = Quaternion.Euler(0, _yaw + 150f + a, 0) * Vector3.forward;
                if (Physics.SphereCast(pivot, 0.2f, dir, out RaycastHit hit, 6f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    var col = hit.collider;
                    if (col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic) continue;
                    if (col.GetComponentInParent<PlayerController>() != null) continue;
                    if (col.GetComponentInParent<AI.EnemyController>() != null) continue;
                    shortest = Mathf.Min(shortest, hit.distance);
                }
            }
            return shortest;
        }

        /// <summary>
        /// 探测「若把镜头转到该偏航角，吊杆能退到多远」——用于避免把镜头甩进墙里。
        /// 与主碰撞回缩同一套过滤规则（忽略触发器、玩家/敌人身体、飞散碎屑）。
        /// </summary>
        float FreeBoomDistance(Vector3 pivot, float yawDeg, float maxDist)
        {
            Vector3 dir = (Quaternion.Euler(_curPitch, yawDeg, 0) * offset).normalized;
            float best = maxDist;
            var hits = Physics.SphereCastAll(pivot, 0.18f, dir, maxDist,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (hit.distance <= 0.001f) continue;
                var col = hit.collider;
                if (col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic) continue;
                if (col.GetComponentInParent<PlayerController>() != null) continue;
                if (col.GetComponentInParent<AI.EnemyController>() != null) continue;
                best = Mathf.Min(best, Mathf.Max(1.6f, hit.distance - 0.1f));
            }
            return best;
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
