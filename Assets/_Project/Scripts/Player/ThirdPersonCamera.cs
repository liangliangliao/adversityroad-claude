using UnityEngine;
using AdversityRoad.Mobile;

namespace AdversityRoad.Player
{
    /// <summary>
    /// 第三人称跟随镜头。
    ///
    /// 全部自动运镜共用同一套稳定设施——**探索与战斗不再是两套逻辑**：
    ///   ① 目标方位低通滤波（探索追角色朝向 _headingAvg，战斗追敌我方位 _aimAvg）；
    ///   ② **渐进软区**（驱动量＝偏差×(|偏差|/软区)²）：小偏差时增益≈0 不追抖动，
    ///      但始终保有微弱回正力，因此不会像硬死区那样永久停偏——
    ///      硬死区的稳态残差恰好等于死区大小（13~20°），那就是"转动很松"；
    ///   ③ 抗抖靠**按频率筛选**的低通（AimLowPass 3.2），不靠按幅度筛选的死区：
    ///      死区加大会把真实运动一并杀掉（实测硬死区版 0.25Hz 跟随只剩 0.057）；
    ///   ④ 偏差→速度连续映射（小幅慢而稳、掉头快而不甩）；
    ///   ⑤ 墙壁减速（不把镜头甩进墙里）；⑥ 手动转镜期间一律让位给玩家；
    ///   ⑦ **角加速度限幅 1100°/s²，只限提速不限减速**——阻止镜头停下来只会制造拖尾。
    ///
    /// 三版实测对照（0.25Hz 真实绕行跟随 / 1Hz 抖动传递 / 稳态残差 / 峰值角加速度）：
    ///   原始裸伺服      0.731 / 0.326 / 4.0° / 4800°/s²   ← 又抖又颠
    ///   硬死区13°版     0.057 / 0.000 / 13.0° / 290°/s²   ← 不抖了但也不跟了（松）
    ///   现版(软区8°)    0.659 / 0.097 / 1.0° / 234°/s²    ← 跟得住、不抖、不松
    ///
    /// 机位与景别：
    /// - 战斗取 3/4 侧位（不站正后方那个最平庸的机位），偏哪一侧由两侧可用空间
    ///   决定，需领先 0.6m 才换边、切换走 1.4s 慢插值；
    /// - 景别由 CameraDirector 选（行进/对峙/群战/狭窄/决胜），参数级插值＝推轨非切镜；
    /// - 特写有节流（2.6s）与群战抑制：知道何时特写的另一半是知道何时不特写；
    /// - **遮挡换角**：被柱子/墙体挡住超过 0.55s 时绕到看得见的角度（从小到大试偏移，
    ///   取第一个够通透的），2 秒慢速走位、换角后最短驻留 4 秒。
    ///   此前遇遮挡只会缩短吊杆一路推到贴脸，视野塌掉却仍对着那根柱子。
    ///
    /// 与角色的代数环（两个推论，都是几何决定的，不是参数没调好）：
    /// 摇杆是镜头相对的 ⇒ 角色朝向 H = 镜头偏航 C + 摇杆角 θ ⇒ **H - C ≡ θ 恒成立**。
    ///  ① 镜头要绕到角色背后需 H-C=0，故【摇杆持续推在离轴 θ 时，镜头在几何上
    ///     不可能转到角色行进方向背后】。实测持续推左 90°，绕行上限由 30°/s 提到
    ///     340°/s，偏离角只由 87.9° 缩到 75.2°，自转却从 29°/s 涨到 207°/s——
    ///     提速换不来视野，只换来转圈。横向/后向奔跑的盲区必须靠**取景**解决
    ///     （见引导留白：横跑的"看前时间"由 1.06s 提到 1.52s）。
    ///  ② 镜头一转 moveDir 就跟着转，角色必沿弧线行进，**弧的角速度＝绕行速率**。
    ///     曾试过锁存移动参考系来断环，但那会让摇杆与画面对不上（手指按左、画面向前），
    ///     代价不可接受。现保住"摇杆↔画面一致"，把持续绕行压到 SustainedOrbitCap
    ///     (30°/s，半径 9.9m)——缺陷从来不是"会画弧"，而是弧太紧。
    ///
    /// 防晕基线：位置与转角均为临界阻尼（无过冲无回弹）；碰撞回缩快、伸出慢；
    /// 震屏只做幅度极小的纵向脉冲（无随机抖动）；真机触屏灵敏度按屏高归一化并限幅。
    ///
    /// ===== 战斗中的稳定优先（本轮）=====
    /// 「摇杆稍微一动，镜头就频繁抖动」的机理是确定的，不是参数没调好：
    /// 跟随的偏差恒等于摇杆离轴角 θ（H = C + θ ⇒ H − C ≡ θ），而伺服的转速 = 增益 × 偏差，
    /// **于是 θ 的抖动被一比一地翻译成镜头角速度**。虚拟摇杆上拇指本就在 ±10~20° 内游走，
    /// 交战时手指最忙，这段噪声就成了持续的角速度噪声。
    /// 加平滑治不了根（平滑只改相位，不改幅频比）；正解是让那段噪声整个落进死区：
    ///   · 交战程度 _combatBlend（锁定/出招中/身边有敌人在打，不只看"是否锁定"）
    ///     驱动死区 10°→30°、大幅换向门槛 45°→62°、转速 ×0.55；
    ///   · 位置侧同步加重：焦点死区 0.03→0.15m、跟随时间常数 0.09→0.24s、
    ///     引导留白上限 2.2→0.5m（近身走位是短促往复，留白只会把焦点前后甩）；
    ///   · 唯一的例外是**出画兜底**：8m 内那个正在打你的敌人真的掉出取景窗并持续
    ///     0.28s，才恢复全速把它推回【窗沿】（不是画面中央）。稳定压倒一切，
    ///     但"看不见敌人"不是稳定，是失职。
    ///
    /// ===== 自动回正（正式并入自动运镜，一键回正保持不变）=====
    /// 连续伺服受代数环所限，永远追不平一个"推着离轴摇杆"的角色；而**松开摇杆的
    /// 那一刻环就消失了**，那才是把镜头一次性摆正的正确时机，也正好是玩家刚跑完
    /// 一段、在换气的节拍。四重闸门：松杆 0.12s + 需求持续 0.2s + 幅度 >25°
    /// （交战 >55°）+ 只在"移动刚结束"的 1.2s 窗口内（站着不动的玩家绝不打扰——
    /// 特意让镜头对着角色的脸站着，那是他摆的机位）。
    /// 时长由**角加速度预算**反解而不是写死：SmoothStep 走完 Δ 度用时 T，峰值角加速度
    /// = 6Δ/T²；原先固定 0.35s 在 180° 时高达 8816°/s²（舒适上限约 600），
    /// 这正是"回正转得太快、让人头晕"的全部原因。
    ///
    /// ===== 时钟 =====
    /// 整套跑在 Time.deltaTime（缩放时间）上，与世界同一把尺子。此前用无缩放时间，
    /// 而每次命中都会触发顿帧（HitStop：timeScale=0.08，0.035~0.12s），于是顿帧期间
    /// 世界冻住、镜头照常全速跑，且 moveSpeed = 位移(8%)/dt(100%) 每拳塌一次——
    /// 那是与出招同频的一路抖动。
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
        // 取景开阔度（实测重调）：原 vFOV 56° + 吊杆 4.76m 的组合下，
        // 角色占屏高 45%、水平视野仅 86.8°——主流第三人称动作游戏是
        // 占屏 28~36%、水平 95~105°。"人物有分量"被做过头成了"人物填满画面"，
        // 这正是「压抑、不开阔」的直接成因。
        // 调参历程与最终取舍：
        //   56°/吊杆4.76m → 水平 86.8°、角色占屏 45.4%（实测「压抑、不开阔」）
        //   65°/吊杆5.25m → 水平 97.1°、占屏 34.4%（实测「偏晕」且「角色变小」）
        //   62°/吊杆4.76m → 水平 93.8°、占屏 40.2%  ← 现值
        // 只保留 FOV 加宽、撤回吊杆加长：两者都会缩小角色，但只有 FOV 能真正扩大
        // 周边视野（吊杆加长只是等比缩小画面里的一切）。于是角色尺寸拿回大半，
        // 开阔度仍比原来高一档。角色占屏与视野开阔是直接对立的两个量，
        // 40% 是这条曲线上的折中点，不是"两者都要"的解。
        public float fieldOfView = 62f;

        [Header("镜头运镜规则（探索/战斗/大招三模式，参考主流第三人称防晕运镜）：" +
                "角色转向快、镜头位置中速跟随、镜头旋转慢——只有玩家【持续朝某方向移动一段" +
                "时间】镜头才慢慢转到身后；小幅左右调整绝不转镜头。遇敌自动切战斗镜头。")]
        public bool autoFollow = true;
        [Tooltip("停止手动转镜后隔多久才允许自动回正（避免与玩家转镜打架）。" +
                 "0.3s 过短——玩家刚手动摆好角度，镜头立刻又自己转回去，读作'和我抢镜头'；" +
                 "1.2s 让玩家的手动取景有足够的停留时间")]
        public float autoFollowDelay = 1.2f;
        [Tooltip("战斗追击的基准转速（度/秒）：小偏差用它，大幅掉头按偏差放大到 6.4 倍")]
        public float autoFollowSpeed = 50f;

        [Header("探索镜头：玩家一转向，镜头立刻开始平稳缓慢地转到其背后（面朝方向）")]
        [Tooltip("镜头与角色朝向偏差小于此角度就不必回正（死区放大：细碎修正完全不动镜头）")]
        public float exploreReorientAngle = 10f;
        [Tooltip("回正平滑时间（临界阻尼弹簧）：偏大=更缓更稳。控制「缓慢跟随」的慢")]
        public float exploreTurnSmoothTime = 0.55f;
        [Tooltip("探索回正的基准转速（度/秒）：小幅修正用它，大角度掉头按偏差放大到 4 倍")]
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
        /// <summary>离轴奔跑强度（0~1，平滑）：横向/后向跑时拉远取景，进一步扩大可见前方。</summary>
        float _offAxisRun;
        float _towardCamT;                 // 持续朝镜头行进的时长（区分"转身看一眼"与"真要往那走"）
        // ---- 镜头导演：景别选择与平滑过渡 ----
        ShotProfile _shot;                 // 当前生效的景别（插值后的实时值）
        float _nextShotScan;               // 敌情扫描节流
        int _nearbyEnemies;
        ShotProfile _shotTarget;            // 已定下的目标景别（受最短驻留保护）
        float _shotSwitchT = -99f;          // 上次换景别的时刻
        bool _crowdLatch;                   // 群战迟滞（3 进 / 1 出）
        float _fallT;                       // 持续下坠时长（区分起跳上升与真的在掉）
        float _roomAround = 99f;           // 镜头四周可用空间（识别狭窄场地）
        bool _shotInit;
        /// <summary>朝镜头持续行进多久才认定"真要往那个方向去"，才允许绕镜。
        /// 低于此值一律视为玩家在看正脸/调整站位，镜头保持不动。
        /// 0.9→0.6：有了威胁感知兜底，这里不必再压那么久。</summary>
        const float TowardCameraHold = 0.6f;

        /// <summary>
        /// 【持续推非正前方向时】镜头绕行速率的上限（度/秒）。
        ///
        /// 摇杆是镜头相对的，于是 moveDir = 镜头偏航 + 摇杆角，而镜头又要绕到角色背后——
        /// 这是一个代数环，解 H=C+θ 且 C=H 只有 θ=0（正前）存在不动点。
        /// 摇杆持续推向左/右/后时方程无解，角色必然沿弧线行进，**弧的角速度就等于
        /// 镜头的绕行速率**。三条性质里最多只能同时满足两条：
        ///   (a) 摇杆↔画面一致  (b) 镜头绕到背后  (c) 角色不画弧
        /// 主流第三人称动作游戏一律取 (a)+(b)、放弃 (c)。缺陷从来不是"会画弧"，
        /// 而是弧的松紧：本作曾以 207~322°/s 绕行，半径只有 0.9~1.4m，读作原地转圈；
        /// 压到 30°/s 后半径 9.9m、绕一圈 12 秒，就是大作里那种几乎察觉不到的缓弧。
        ///
        /// 只在【推着非正前方向】时生效：摇杆回正或松开后，环即消失，
        /// 镜头恢复全速追平（掉头后想尽快看清前方，松一下杆就行）。
        /// </summary>
        /// 30→26°/s：这一个数同时决定弧线半径(v/ω)、玩家补杆速率(ω)、对齐耗时(θ/ω)。
        /// 26°/s ⇒ 半径 11.5m、补杆 26°/s、90° 转向 3.5 秒对齐——
        /// 慢到补杆是无意识的，于是实际路径几乎看不出弯。
        const float SustainedOrbitCap = 26f;

        // ---- 一键回正（业界通行的"逃生口"）----
        // 摇杆是镜头相对的 ⇒ H = C + θ，而"镜头对着角色正前方"要求 C = H ⇒ θ = 0。
        // 即：只要摇杆推在非正前方向，镜头在几何上就不可能对着角色正前方——
        // 与转速、聚焦点都无关。大作对此的解法不是让镜头去追一个无解的目标，
        // 而是给玩家一个显式动作，一次性把镜头拉到行进方向背后。
        // 回正期间【锁存移动参考系】：否则镜头快速绕行会把角色一起拖着转，
        // 0.35s 内可拖出 90°，回正反而把人转晕。
        // ===== 回正时长必须由【角加速度预算】反解，不能写死 =====
        // 晕动的驱动量是角【加速度】，不是角速度；公认的舒适上限约 600°/s²。
        // SmoothStep 走完 Δ 度用时 T，峰值角加速度 = 6Δ/T²。
        // 原先固定 0.35s：Δ=180° 时峰值高达 8816°/s²，是舒适上限的 14 倍——
        // 这正是"回正转得太快、让人头晕"的全部原因，和平滑曲线选得好不好无关。
        // 反解：T = sqrt(6Δ/预算)，再取一个下限保证小幅回正也不显得急促。
        static float RecenterDuration(float amount, float accelBudget, float minTime)
            => Mathf.Max(minTime, Mathf.Sqrt(6f * Mathf.Abs(amount) / Mathf.Max(1f, accelBudget)));
        /// <summary>一键回正是玩家自己按的，自发运动耐受度高，可以利落一点；
        /// 但 1400°/s² 仍只有原先 8816 的六分之一。180° 用 0.88s、90° 用 0.62s。</summary>
        const float ManualRecenterAccelBudget = 1400f, ManualRecenterMinTime = 0.3f;
        // ===== 为什么不做「掉头时无限期钉住移动参考系」 =====
        // 代数上它成立：镜头绕行 180° 期间把参考系钉住，角色朝向就恒定、走直线，
        // 而镜头照样绕到背后。但实机截图给出了否决性证据——
        // **摇杆推在左下、画面里角色却在往画面深处跑**，偏差最大 180°。
        // 上游此前也因同一现象撤回过（cd4bc4a）。两次独立验证，结论一致：
        // 「摇杆方向 ↔ 画面方向」的一致性不可交易，它比"轨迹是直线"更基本——
        // 玩家是照着画面推杆的，一旦对不上，每一次输入都在赌。
        //
        // 于是 H = C + θ 全时成立，推论只有一个：
        // **玩家按着摇杆时，镜头每转 1°，角色就被带转 1°。**
        // 上游那次的问题不在"钉住"本身，而在**无限期、无封顶**：偏差能一路涨到
        // 180°。本轮的对齐额度封顶 30°、松手即零、按着不放也会自动还清，
        // 是同一个手段的有界版本。持续绕行（稳态）则仍然完全关闭——
        // 那一路没有封顶，正是它把偏差累积到不可接受的量级。
        float _recenterT, _recenterDur;
        float _recenterFrom, _recenterTo;
        /// <summary>回正进行中——PlayerController 据此冻结移动参考系。</summary>
        public bool RecenterActive => _recenterT > 0f;

        /// <summary>回正的目标偏航：有锁定目标时看向敌人，否则看向角色朝向。</summary>
        float RecenterTargetYaw(Transform lockTarget)
        {
            if (lockTarget != null)
            {
                Vector3 toE = lockTarget.position - target.position; toE.y = 0f;
                if (toE.sqrMagnitude > 0.09f)
                    return Quaternion.LookRotation(toE.normalized).eulerAngles.y;
            }
            return target.eulerAngles.y;
        }

        void StartRecenter(float toYaw, float accelBudget, float minTime)
        {
            _recenterFrom = _yaw;
            _recenterTo = toYaw;
            _recenterDur = RecenterDuration(Mathf.DeltaAngle(_yaw, toYaw), accelBudget, minTime);
            _recenterT = _recenterDur;
            // 参考系的冻结由 PlayerController 自己按 RecenterActive 处理——
            // 只在这个【玩家显式触发、有始有终】的动作期间冻结，动作一结束立即恢复。
        }
        readonly System.Collections.Generic.List<float> _threatDirs =
            new System.Collections.Generic.List<float>();   // 附近敌人的方位角（0.3s 刷新）

        // ===== 晕动的真正成因：角加速度与方向反复，不是角速度本身 =====
        // 此前用【二值迟滞开关】驱动一个【连续伺服】：每次关断都把 _yawFollowVel 硬置零，
        // 速度不连续＝无限 jerk。实测敌人在 2m 处 ±18° 摆动 8 秒，开关通断 19 次，
        // 峰值角加速度 1509°/s²（舒适上限约 600）——这才是"抽动、发晕"的来源，
        // 而峰值速度其实只有 38°/s。问题从来不是快。
        //
        // 改为【渐进软区】：驱动量 = 偏差 × (|偏差|/软区)²，封顶 1。
        //
        // 上一版是"死区内驱动为零"，虽然消除了通断，却引入了死区伺服的经典缺陷——
        // **稳态残差恰好等于死区大小**：镜头永久停在偏离目标 13~20° 的地方不再修正，
        // 这就是"转动很松"。更糟的是它按【幅度】筛选，把真实运动一并杀掉：
        // 实测 0.25Hz 的真实绕行跟随率只剩 0.057，镜头基本不跟敌人了。
        //
        // 现在死区内仍有微弱回正力（小偏差时增益≈0，接近软区边缘迅速趋近 1）：
        // 稳态残差 13° → 1.0°，0.25Hz 跟随 0.057 → 0.659，而 1Hz 抖动仍只有 0.097
        // （原始裸伺服是 0.32）。抗抖靠的是【按频率筛选】的低通，不是按幅度筛选的死区。
        static float SoftTarget(float cur, float want, float softZone)
        {
            float e = Mathf.DeltaAngle(cur, want);
            float g = Mathf.Min(1f, Mathf.Pow(Mathf.Abs(e) / Mathf.Max(softZone, 0.001f), 2f));
            return cur + e * g;
        }

        /// <summary>自动运镜的角加速度上限（度/秒²）。只约束镜头【自己】的运动——
        /// 玩家手动转镜不受限，自发运动不引起晕动，限它只会读作迟钝。
        /// 800→1100：实测掉头仍偏慢，盲区 1.00→0.86s；
        /// 因为只限提速不限减速，且渐进软区已把稳态抖动压到 234°/s²，
        /// 抬高上限只影响"大幅掉头"这一种情形，日常运镜的实际加速度远低于它。</summary>
        const float MaxAutoYawAccel = 1100f;
        float _autoYawRate;                // 上一帧自动运镜的角速度（限幅用）

        /// <summary>
        /// 室内舒适模式：在住处这类封闭空间里生效（由 IndoorZone 触发盒开关）。
        ///
        /// 屋里墙多、门多、拐弯多，镜头的三件事都会变成晕动源：吊杆被墙反复推缩、
        /// 景别在小房间里不停切、跟随运镜绕着人转。室内一律关掉它们——
        /// 换来的是"镜头老老实实待在你身后"，这在室内正是玩家想要的。
        /// </summary>
        public static bool IndoorMode;

        /// <summary>室内吊杆长度：短到墙基本碰不到它。</summary>
        const float IndoorBoom = 2.6f;

        /// <summary>
        /// 碰撞回缩的下限（镜头最近能贴到取景点多近）。
        ///
        /// 【上一版为什么会"镜头被挡住"】下限写死 1.9 米，理由是"1.6 米处角色占屏
        /// 113%，太挤"。但这个下限是**无条件**的：当障碍物离取景点只有 0.6 米时
        /// （屋里的门扇、床头、书柜、柜门后面），回缩到 1.9 米等于**把镜头按在
        /// 障碍物内部**——画面就是玩家截图里那样：半屏是门板、半屏是柜子的背面。
        /// 「角色占屏太大」是构图问题，「镜头在柜子里」是画面报废，孰轻孰重很清楚。
        ///
        /// 所以下限必须让位给通透：室内 0.5 米（贴到肩后，等同越肩视角），
        /// 室外 1.2 米。镜头真的贴近时由 CharacterCloseFade 把角色淡开
        /// （它 1.1 米起淡、0.45 米最透），所以近到 0.5 米也不会"整屏一张脸"。
        /// </summary>
        const float BoomFloorIndoor = 0.5f, BoomFloorOutdoor = 1.2f;

        // ===== 取景窗（camera window）：镜头运动的第一原则 =====
        // 出自 Mark Haigh-Hutchinson《Real-Time Cameras》，也是所有成熟动作游戏的骨架：
        // **主体在窗内 ⇒ 镜头一动不动；主体出窗 ⇒ 只把它推回窗沿，不推到画面中央。**
        // 推到中央意味着镜头得跟着敌人的每一步走，那正是"永远在轻微动"。
        //
        // 窗宽必须由【真实的水平半视野】导出，不能拍脑袋写死角度：
        // 62° 垂直 FOV、19.5:9 屏幕 ⇒ 水平半视野 52.5°。此前写死 34° 的那一版
        // 在近身时【几何上永远够不着】——镜头在角色背后 4.6m，敌人在 2m 处的
        // 屏幕角上限只有 25.8°，于是"框敌"这条逻辑从来没有生效过。
        //   敌我距离  1.5m   2.0m   2.5m   3.0m
        //   屏幕角上限 19.0°  25.8°  32.9°  40.7°
        /// <summary>框敌取景窗占水平半视野的比例（非战斗基准）。</summary>
        const float FocusWindowRatio = 0.58f;
        /// <summary>战斗中把框敌窗放宽到 0.74（≈39°）：稳定第一。
        /// 3m 以内几乎不可能触发框敌 ⇒ 镜头在近身缠斗里彻底静止；
        /// 而敌人最远也只到画面半宽的 74%，仍看得清清楚楚。</summary>
        const float FocusWindowCombatRatio = 0.74f;
        const float FocusEdgeSoft = 10f;       // 窗沿软区：不在边界上硬启停
        const float FocusAcquireRange = 12f;   // 12m 内选聚焦目标
        const float FocusKeepRange = 16f;      // 已选中的目标到 16m 才放弃（迟滞）
        /// <summary>出窗需持续多久才动机位。战斗中加长（0.12→0.28s）：闪避、突进、
        /// 被击退都会让敌人短暂掠出窗沿，那些都会自己回来，不值得为它动镜。</summary>
        const float FocusFrameHold = 0.12f, FocusFrameHoldCombat = 0.28f;
        /// <summary>出画兜底只管【近身】的那个对手：十米开外跑动中的追兵不属于
        /// "我把对手打丢了"。</summary>
        const float CombatFollowRange = 8f;

        // ===== 视野开阔度：决定运镜快慢的唯一裁决者（本轮）=====
        // 规则用一句话说完：**看得见远方 ⇒ 慢慢跟；看不见远方 ⇒ 赶紧转过去。**
        // 转向本身不是动镜的理由——你在开阔地斜着跑，前方一样一览无余，
        // 这时候把镜头摆正只是"整理构图"，代价却是画面一直在动。
        // 反过来，走廊、拐角、贴墙这些地方，前方就那么几米，
        // 不立刻把要去的方向框进画面就是真盲区，那才值得快。
        //
        // 更要紧的是**别让盲区发生**：判据用【撞上的剩余时间】而不是距离，
        // 于是墙还在 15m 外时镜头就开始不紧不慢地转，等你真到墙边早已对好——
        // 永远不必做那种"突然快速转一下"的运镜，晕动自然就没有了。
        const float SightMax = 26f;          // 通视探测上限（够远就不必再分辨）
        const float SightProbeRadius = 0.35f;
        /// <summary>「开阔」的门槛。18→9m：这条判据【只】该表达"镜头顶着墙、
        /// 什么都看不见"，而不是"我在一个房间里"。18m 的量程下，任何围合场地
        /// （截图里那种带墙的房间）通视都长期 <18m ⇒ closedUrge 常年 0.25~0.83 ⇒
        /// 镜头常年握着旋转授权 ⇒ 角色常年被带着偏——这正是"摇杆与跑动方向
        /// 还会出现偏差"的来源。吊杆才 4.6m，能看到 9m 就完全够用了。</summary>
        const float SightOpen = 9f;
        /// <summary>只看得到这么近＝真的顶墙了。6→3m。</summary>
        const float SightTight = 3f;
        const float TtiRelaxed = 3.0f;       // 距离撞上还有 3 秒：完全不急
        const float TtiUrgent = 0.9f;        // 只剩 0.9 秒：满级紧迫
        /// <summary>行进方向偏离画面正前多少（占半视野的比例）开始算"看不见要去的地方"。
        /// 这一项不能省：开阔地里【朝镜头跑】时通视距离很长，但那个方向根本不在画面里，
        /// 只看开阔度会漏掉这种最典型的盲区。</summary>
        const float OffScreenStart = 0.65f, OffScreenFull = 0.95f;
        /// <summary>视野开阔时的跟随死区（度）。开阔＝28°内的转向完全不动镜；
        /// 盲区时收回到 exploreReorientAngle（10°），细碎修正也照跟。</summary>
        const float SightDeadZoneOpen = 28f;
        /// <summary>转向缓冲的最大拉远量（占基准吊杆的比例）。
        /// 0.22 ⇒ 吊杆 4.6m 推到 5.6m，可见前方约多出 1.7m，角色占屏 40%→33%——
        /// 还在主流第三人称的构图区间内，不会读作"人物变小"。</summary>
        const float TurnDollyOut = 0.22f;
        /// <summary>方向跟随的驱动强度（0~1，临界阻尼）。**跟随的前提永远是
        /// "你正在往某处去"**——停下时它滑到 0，镜头自己减速停住，而不是被掐断
        /// （硬关伺服＝速度台阶＝无限 jerk，读作"卡一下"）。</summary>
        float _followDrive, _followDriveVel;
        // ---- 拇指角（θ）：唯一与镜头无关的方向信号 ----
        // 摇杆是镜头相对的，世界方向会被镜头带着转；而按住不动的手指其 θ 恒定。
        // 这里只用它做一件事：判断【方向定没定下来】，供引导留白与转向拉远
        // 决定该跟得快还是压得住（搓杆时 θ 一直在扫 ⇒ 永不判定为已定）。
        float _thumbPrev, _thumbSettleT;
        float _thumbRatePeak;                // 刚才那一下转向有多急（相机无关，峰值保持）
        float _thumbAbs;                     // |θ|：要去的方向离画面正前多远
        /// <summary>本次转向的幅度，在瞬态起手的那一刻锁存。
        /// **必须锁存，不能用瞬时偏差**——偏差一降倍率就塌，大幅掉头会在半途失速。</summary>
        float _turnMag;
        bool _thumbInit;
        float _turnBurst, _turnBurstVel;     // 转向缓冲强度（起快收慢，临界阻尼）
        float _sightNow = SightMax, _sightGoing = SightMax;
        float _urgency, _urgencyVel;         // 0=一览无余，1=前方全是盲区
        /// <summary>**只**取"当前画面闭塞"这一项（沿画面正前的通视距离）。
        /// 这是三项紧迫度里唯一【转镜头能解决】的一项，因此也是唯一有资格
        /// 授权镜头旋转的一项——另两项转多少度都白转，只换来角色画弧。</summary>
        float _viewBlocked, _viewBlockedVel;
        Transform _focusEnemy;                 // 「必须看得见的那个敌人」
        float _focusDist = 99f;                // 到聚焦敌人的水平距离
        Vector3 _focusPos;                     // 低通后的敌人位置（滤位置而不是滤屏幕角）
        bool _focusInit;
        float _focusScreenAng;                 // 敌人相对画面正前的水平夹角（带符号）
        float _focusFrameT;                    // 已出窗的持续时长

        /// <summary>当前的水平半视野（度）。垂直 FOV 与宽高比都可能被设备改写，
        /// 必须实测而不是写死——写死的那一版让整条框敌逻辑从未触发过。</summary>
        float HalfHorizontalFov()
        {
            var cam = GetComponent<Camera>();
            float v = cam != null ? cam.fieldOfView : fieldOfView;
            float asp = cam != null && cam.aspect > 0.01f ? cam.aspect
                      : Mathf.Max(0.01f, (float)Screen.width / Mathf.Max(1, Screen.height));
            return Mathf.Atan(Mathf.Tan(v * 0.5f * Mathf.Deg2Rad) * asp) * Mathf.Rad2Deg;
        }

        /// <summary>某个世界点相对【画面正前】的水平夹角（度，带符号，右为正）。
        /// 用真实的 transform.forward 而不是 _yaw：肩位偏移、吊杆回缩、
        /// 焦点留白都会让"画面正前"与 _yaw 差出好几度，而判"看不看得见"要的是画面。</summary>
        float ScreenAngleTo(Vector3 worldPoint)
        {
            Vector3 to = worldPoint - transform.position; to.y = 0f;
            if (to.sqrMagnitude < 1e-4f) return 0f;
            Vector3 fwd = transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) return 0f;
            return Vector3.SignedAngle(fwd.normalized, to.normalized, Vector3.up);
        }

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
        // ---- 战斗机位（与探索共用稳定设施；此前战斗分支是裸伺服）----
        /// <summary>「玩家→敌人」方位的低通滤波值。近身缠斗时该方位的原始变化率
        /// 可达 70°/s 以上（距离 2m、敌人横移 2.5m/s），必须滤波后再喂给镜头。</summary>
        float _aimAvg;
        bool _aimInit;
        /// <summary>方位低通收敛率（1/秒）。6.5→3.2：低通按【频率】筛选，
        /// 能压掉 1Hz 以上的缠斗抖动而几乎不影响 0.25Hz 的真实绕行；
        /// 死区按【幅度】筛选，会把两者一起杀掉——抗抖该靠这里，不该靠加大死区。</summary>
        const float AimLowPass = 3.2f;
        /// <summary>3/4 侧位的偏角：正后方是最平庸的机位，偏开一个肩位才是对峙构图。
        /// 22° 足以让双方错开、拳脚横穿画面，又不至于把玩家推到画面边缘。</summary>
        const float ShoulderAngle = 22f;
        /// <summary>遮挡换角的候选偏移（由小到大：换角幅度能小则小）。</summary>
        static readonly float[] OccProbe = { 22f, -22f, 38f, -38f, 58f, -58f, 80f, -80f };
        float _shoulderSide = 1f;          // +1=偏右肩 -1=偏左肩（迟滞选择，切换极慢）
        float _shoulderTarget = 1f;
        float _shoulderSwitchT = -99f;   // 上次换肩时刻（最短驻留，防来回横跳）

        // ---- 遮挡换角（战斗中被柱子/墙体挡住时，换一个看得见的机位）----
        // 此前遇到遮挡只会缩短吊杆（dolly in），把镜头一路推到 1.9m 贴脸，
        // 视野塌掉却仍然对着那根柱子——镜头【从不换角度】。
        // 摄影指导遇到前景挡住主体时的第一反应是换机位，不是往前挤。
        float _occludedT;                  // 当前机位被遮挡的持续时长
        float _occYawBias, _occYawTarget;  // 换角偏置（缓慢插值生效）
        float _occSwitchT = -99f;          // 上次换角时刻（最短驻留）
        const float OccludeHold = 0.55f;   // 遮挡持续这么久才换角（穿过一下不换）
        const float OccludeDwell = 4f;     // 换角后的最短驻留（防来回横跳）

        float _speedAvg;                   // 低通后的水平速度（构图/变焦共用，防单帧毛刺）
        /// <summary>交战程度（0~1 平滑）：锁定 / 出招中 / 身边有敌人在打，都算"正在战斗"。
        /// 此前位置侧的稳镜只认 _lockBlend（是否锁定），而锁定默认是手动开的，
        /// 于是绝大多数近身互殴一直吃着"探索档"的跟手参数——这是"打起来镜头抖"
        /// 里属于【位置】的那一半。</summary>
        float _combatBlend;
        int _engagedEnemies;               // 真的在打你的敌人数（Idle/Patrol 不算）
        float _lenFactorVel;               // 变焦的临界阻尼速度（Lerp 对阶跃目标会一帧给满速）
        float _offAxisVel;                 // 引导留白强度的临界阻尼速度
        Vector2 _leadVec, _leadVel;        // 引导留白【向量】（搓杆时各方向互相抵消）
        float _pivotYAnchor;               // 纵向焦点死区锚（贴地/台阶的高频跳动不推镜）
        /// <summary>纵向焦点死区（米）：CharacterController 贴地会让 y 每帧小幅跳动。</summary>
        const float PivotYDeadZone = 0.06f;
        float _headingAvg;                 // 平滑朝向（低通滤波）
        /// <summary>朝向的【单位向量均值】。模长即"这串输入有多像一个方向"的置信度：
        /// 按住不动→1.00，两向来回切→0.71，搓杆一圈/秒→0.33。
        /// 一个量同时给出"平均朝哪"与"该不该跟"，取代了会被急转清零的稳定计时器。</summary>
        Vector2 _headingMean;
        bool _headingMeanInit;
        /// <summary>方向均值的低通速率（1/秒）。2.2 ⇒ τ≈0.45s：
        /// 足够长到能把搓杆平均掉，又足够短到一次干净的转向 1 秒内就建立起置信度。</summary>
        const float DirMeanRate = 2.2f;
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
        public void UltimateShot(float duration) => CloseUp(duration, 1f);

        // ---- 特写的克制（"知道何时特写"的另一半是"知道何时不特写"）----
        float _lastCloseUp = -99f;
        float _closeStrength;              // 本次特写的推近强度（0=不推 1=大招级）
        /// <summary>两次特写的最小间隔。此前每个敌人死亡都触发 1.1s 特写，
        /// 群战里连续击杀会把镜头长期焊在贴脸位——与"群战该拉远看局势"完全相反。</summary>
        const float CloseUpGap = 2.6f;

        /// <summary>
        /// 推近特写。三条克制规则，缺一条都会变成"学徒工乱推镜头"：
        ///   · 节流：间隔不足一律忽略（弱特写），避免连杀时镜头贴脸不放；
        ///   · 群战抑制：身边还有 ≥3 个敌人时，看清包围态势远比欣赏这一击重要——
        ///     强度砍半、时长压短；
        ///   · 强度分级：处决/击杀是轻推，超必杀才是满推。
        /// </summary>
        public void CloseUp(float duration, float strength)
        {
            bool strong = strength >= 0.9f;
            if (!strong && Time.unscaledTime - _lastCloseUp < CloseUpGap) return;
            if (_nearbyEnemies >= 3 && !strong) { strength *= 0.5f; duration *= 0.6f; }
            _lastCloseUp = Time.unscaledTime;
            _ultimateTimer = Mathf.Max(_ultimateTimer, duration);
            _closeStrength = Mathf.Max(_closeStrength, Mathf.Clamp01(strength));
        }

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
            // 【近裁剪面按视角给】深度缓冲的精度几乎全被 near 决定：near=0.04 时
            // 五米开外相邻两个深度值能差到几厘米，两块只差 2 厘米的板子就会互相
            // 打架（照片上盖色块、看板文字闪烁都是这么来的）。第一人称要看清眼前的
            // 手和兵器，只能给 0.04；第三人称镜头最近也在半米外，给 0.1 即可，
            // 精度直接好一倍多。
            var camc = GetComponent<Camera>();
            if (camc != null) camc.nearClipPlane = p.fp ? 0.04f : 0.1f;
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
            // ===== 镜头必须和世界共用同一把时钟 =====
            // 原先整套跑在 Time.unscaledDeltaTime 上。而每一次命中都会触发顿帧
            //（CombatFeedback.HitStop：timeScale=0.08，持续 0.035~0.12s），连打时每秒好几次。
            // 于是顿帧期间【世界冻住、镜头照常全速跑】：
            //   · 位置/偏航平滑在这 0.1 秒里照样收敛，等于每挨一拳镜头就抢跑一段，
            //     世界恢复后再被拽回来——这就是与出招同频的"画面频繁抖动"；
            //   · 所有由位移推出来的量（速度、引导留白、拉远系数）也一起失真：
            //     moveSpeed = 位移(8%) / dt(100%) ≈ 真实速度的 8%，每次命中塌一次。
            // 改用缩放时间后三件事一起对齐：镜头随世界一起顿、速度估计自洽、
            // 打开暂停面板时镜头不再漂。
            float dt = Time.deltaTime;
            // 暂停（timeScale=0）时必须把累积的转镜增量丢掉再退出：否则面板开着的
            // 这几秒里 LookDelta 一直在累加，恢复的瞬间会被一次性灌进 _yaw，镜头猛甩一下。
            if (dt <= 0f) { MobileInput.ConsumeLook(); return; }

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
            // 速度再过一道低通：引导留白里的 Clamp01(moveSpeed/5.2) 原本没有任何平滑，
            // 任何单帧速度突变都会被它一比一放大成焦点位移（最多 0.45m/帧）。
            // 上面已把 dt 统一成缩放时间，顿帧的失真已消除；这里是防单帧毛刺的保险。
            _speedAvg = Mathf.Lerp(_speedAvg, frameDelta.magnitude / dt, 8f * dt);
            float moveSpeed = _speedAvg;
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
            // 下限 5→8：转向瞬间 _headingHoldT 被清零、lpRate 掉到最低，
            // τ=0.2s 的滞后正好压在最需要镜头动起来的那一刻。8 ⇒ τ=0.125s，
            // 省下 0.075s 的纯延迟；抗抖由跟随的 28° 死区负责，不靠这里。
            float lpRate = Mathf.Lerp(8f, 18f, Mathf.Clamp01(_headingHoldT / 0.25f));
            _headingAvg = Mathf.LerpAngle(_headingAvg, rawHeading, 1f - Mathf.Exp(-lpRate * dt));

            // ===== 方向均值【向量】：连续换向时"逐步摆正"的依据 =====
            // 原先跟随要求 _headingHoldT > steadyNeed，而它在朝向角速度 >220°/s 时清零。
            // 连续换向（手指不松、方向一个接一个切）时角色不停急转 ⇒ 计时反复清零
            // ⇒ **跟随一次都不成立、镜头完全不动**。那正是需要它慢慢摆正的时候。
            //
            // 改用把朝向当【单位向量】做低通。均值的**模长天然就是置信度**——
            // 它一个量同时回答了"平均朝哪"和"这串输入有多像一个方向"：
            //   按住一个方向不动 → 1.00 ⇒ 满速跟
            //   两个方向来回切   → 0.71 ⇒ 半速朝两者的中分线缓缓摆正（角色走弧线）
            //   搓杆 0.5 圈/秒   → 0.57 ⇒ 几乎不动
            //   搓杆 1 圈/秒     → 0.33 ⇒ 完全不动
            // 于是"连续换向要逐步摆正"与"搓杆不许动镜"由同一个量分开，
            // 不再需要一个会被急转清零的计时器。
            float hRad = rawHeading * Mathf.Deg2Rad;
            Vector2 hVec = new Vector2(Mathf.Sin(hRad), Mathf.Cos(hRad));
            if (!_headingMeanInit) { _headingMeanInit = true; _headingMean = hVec; }
            _headingMean = Vector2.Lerp(_headingMean, hVec, 1f - Mathf.Exp(-DirMeanRate * dt));
            float dirConf = _headingMean.magnitude;
            float meanYaw = _headingMean.sqrMagnitude > 1e-6f
                ? Mathf.Atan2(_headingMean.x, _headingMean.y) * Mathf.Rad2Deg : _headingAvg;
            // 置信度 → 可用速率：0.55 起、0.85 满。低于 0.55 一律不动。
            float dirTrust = Mathf.Clamp01((dirConf - 0.55f) / 0.30f);

            // ---- 模式判定：大招 > 战斗（有敌可锁）> 探索 ----
            if (_ultimateTimer > 0f) _ultimateTimer -= dt;
            else _closeStrength = Mathf.MoveTowards(_closeStrength, 0f, dt / 0.4f);

            // ===== 镜头导演：选景别 + 平滑推轨过渡 =====
            // 敌情与场地扫描节流（0.3s 一次，避免每帧全场遍历）
            if (Time.unscaledTime >= _nextShotScan)
            {
                _nextShotScan = Time.unscaledTime + 0.3f;
                _nearbyEnemies = 0;
                _engagedEnemies = 0;
                _threatDirs.Clear();
                Transform focusPick = null;
                float focusBest = float.MaxValue;
                foreach (var e in Object.FindObjectsOfType<AI.EnemyController>())
                {
                    if (e.State == AI.EnemyState.Dead) continue;
                    Vector3 to = e.transform.position - target.position; to.y = 0;
                    float d2 = to.sqrMagnitude;
                    if (d2 < 81f) _nearbyEnemies++;
                    // 「真的在打」才算交战：待机/巡逻的敌人只影响景别——路过一个
                    // 没搭理你的敌人不该把镜头切成战斗模式
                    bool engaged = e.State != AI.EnemyState.Idle && e.State != AI.EnemyState.Patrol;
                    if (d2 < 81f && engaged) _engagedEnemies++;
                    // 威胁方位缓存（14m 内）：供每帧判断"我正在转向的方向上有没有敌人"
                    if (d2 < 196f && d2 > 0.01f)
                        _threatDirs.Add(Quaternion.LookRotation(to.normalized).eulerAngles.y);
                    // 「必须看得见的那个敌人」：最近的交战目标（迟滞见 FocusKeepRange）
                    if (engaged && d2 < focusBest &&
                        d2 < (_focusEnemy == e.transform ? FocusKeepRange * FocusKeepRange
                                                         : FocusAcquireRange * FocusAcquireRange))
                    { focusBest = d2; focusPick = e.transform; }
                }
                _focusEnemy = focusPick;
                // 场地宽敞度：只看【吊杆真正要退去的方向】，而不是四周的最短距离。
                // 旧实现取六向最短值，于是只要玩家靠近任何一面墙就判定"狭窄"并推近镜头——
                // 哪怕镜头正对着一片开阔地。狭窄与否是机位问题，不是站位问题。
                Vector3 probePivot = target.position + Vector3.up * _pivotH;
                float probeBoom = offset.magnitude * _lenFactor;
                _roomAround = FreeBoomDistance(probePivot, _yaw, probeBoom);

                // ---- 选肩位（"最佳拍摄角度"的实处）----
                // 左右两侧各探一次，谁更开阔就站谁那边；差距不显著就保持不动。
                // 迟滞必须给足：机位左右横跳比站在平庸位置糟糕得多。
                var lt = lockOn != null ? lockOn.CurrentTarget : null;
                if (lt != null)
                {
                    Vector3 aim = lt.position - target.position; aim.y = 0;
                    if (aim.sqrMagnitude > 0.09f)
                    {
                        float a = Quaternion.LookRotation(aim.normalized).eulerAngles.y;
                        float roomR = FreeBoomDistance(probePivot, a + ShoulderAngle, probeBoom);
                        float roomL = FreeBoomDistance(probePivot, a - ShoulderAngle, probeBoom);
                        // 需领先 0.6m 才换边——小差距不值得动机位。
                        // 另加【最短驻留 6 秒】：仅靠距离死区挡不住"绕着柱子打"这类
                        // 两侧空间反复互换的场景，而每换一次边就是 44° 的来回摆镜，
                        // 是典型晕动源。宁可暂时站在略差的一侧，也不要来回横跳。
                        float sideWant = _shoulderTarget;
                        if (roomR > roomL + 0.6f) sideWant = 1f;
                        else if (roomL > roomR + 0.6f) sideWant = -1f;
                        if (!Mathf.Approximately(sideWant, _shoulderTarget) &&
                            Time.unscaledTime - _shoulderSwitchT > 6f)
                        {
                            _shoulderTarget = sideWant;
                            _shoulderSwitchT = Time.unscaledTime;
                        }
                    }
                }
                // ---- 遮挡换角 ----
                // 被挡住超过 OccludeHold 才动：短暂穿过的前景（跑过一棵树）不该引发换角。
                // 从小到大试偏移，取【第一个足够通透的】——换角幅度要尽可能小，
                // 大幅绕机位比稍微斜一点更让人不适。
                bool careAboutView = _nearbyEnemies > 0 || lt != null;
                if (careAboutView && _occludedT > OccludeHold &&
                    Time.unscaledTime - _occSwitchT > OccludeDwell)
                {
                    // _yaw 是镜头【当前实际】方位（已含现有偏置），遮挡就发生在这个方位上。
                    // 候选角同样按绝对方位探测，因此新偏置＝现有偏置＋相对偏移，
                    // 这样目标方位才正好等于探到的那个通透方向（坐标系必须一致）。
                    float baseFree = FreeBoomDistance(probePivot, _yaw, probeBoom);
                    for (int i = 0; i < OccProbe.Length; i++)
                    {
                        if (FreeBoomDistance(probePivot, _yaw + OccProbe[i], probeBoom) > baseFree + 1.0f)
                        {
                            _occYawTarget = Mathf.Clamp(_occYawBias + OccProbe[i], -90f, 90f);
                            _occSwitchT = Time.unscaledTime;
                            break;
                        }
                    }
                }
                // 撤销换角：要看【原机位】是否重新通透，而不是看当前机位——
                // 当前机位本来就是为了避开遮挡才换过来的，它当然是通的。
                else if (!Mathf.Approximately(_occYawTarget, 0f) &&
                         Time.unscaledTime - _occSwitchT > OccludeDwell &&
                         FreeBoomDistance(probePivot, _yaw - _occYawBias, probeBoom) > probeBoom - 0.6f)
                {
                    _occYawTarget = 0f;
                    _occSwitchT = Time.unscaledTime;
                }
            }
            // 肩位与换角都走极慢插值：换机位是分镜级动作，绝不能是瞬切
            _shoulderSide = Mathf.MoveTowards(_shoulderSide, _shoulderTarget, dt / 0.7f);
            // 换角 2 秒走完（比换肩更慢——它的幅度更大）
            _occYawBias = Mathf.MoveTowards(_occYawBias, _occYawTarget, 45f * dt);
            // ---- 腾空判定：只认【真的在往下掉】，不认起跳的上升段 ----
            // 上升段人眼看到的是自己在往上走，落点还没成为问题；而且短跳（0.3s 以内）
            // 拉一下又收回来纯粹是呼吸感。故要求：离地 + 下坠 + 持续 0.25s。
            bool fallingRaw = player != null && player.Airborne && player.VerticalVelocity < -1.5f;
            _fallT = fallingRaw ? _fallT + dt : 0f;
            bool falling = _fallT > 0.25f;
            // ---- 群战迟滞：3 个进、1 个出 ----
            // 阈值上一个人来回进出，就是一次 0.55m 的推拉。宁可多留一会儿群战景别。
            if (_engagedEnemies >= 3) _crowdLatch = true;
            else if (_engagedEnemies <= 1) _crowdLatch = false;
            var wantShot = CameraDirector.Pick(
                _ultimateTimer > 0f, _crowdLatch ? Mathf.Max(3, _engagedEnemies) : _engagedEnemies,
                lockOn != null && lockOn.CurrentTarget != null, _roomAround,
                falling, player != null && player.IsCrouched);
            if (!_shotInit) { _shot = _shotTarget = wantShot; _shotInit = true; }
            else
            {
                // 最短驻留：换景别是分镜级的决定，不该每秒发生一次。
                // 演出/腾空/狭窄是短节拍，豁免——它们晚一步就没意义了。
                if (wantShot.name != _shotTarget.name &&
                    (CameraDirector.Urgent(wantShot) ||
                     Time.unscaledTime - _shotSwitchT > CameraDirector.Dwell))
                {
                    _shotTarget = wantShot;
                    _shotSwitchT = Time.unscaledTime;
                }
                // 参数级插值＝推轨，而不是切镜：这是"平稳、不适感为零"的关键
                float rate = CameraDirector.BlendRate(_shot, _shotTarget);
                _shot = ShotProfile.Lerp(_shot, _shotTarget, 1f - Mathf.Exp(-rate * dt));
            }
            Transform lockTarget = lockOn != null ? lockOn.CurrentTarget : null;
            bool combat = lockTarget != null;
            bool ultimate = _ultimateTimer > 0f;
            bool manualLook = Time.unscaledTime - _lastManualLook < autoFollowDelay;

            // ---- 交战程度：锁定 / 出招中 / 身边有敌人在打 ----
            // 未锁定的近身互殴同样是"战斗"，位置侧的稳镜与偏航策略都要认它。
            // 锁定默认是手动开的，只认 _lockBlend 等于绝大多数互殴都吃不到稳镜档。
            if (_playerFsm == null && player != null)
                _playerFsm = player.GetComponent<Combat.CombatStateMachine>();
            bool fightingNow = lockTarget != null || _engagedEnemies > 0 ||
                               (_playerFsm != null && _playerFsm.InCombat);
            _combatBlend = Mathf.MoveTowards(_combatBlend, fightingNow ? 1f : 0f, dt / 0.6f);
            // **零延迟的"还想不想往某处去"**：直接读手指/按键的原始状态，
            // 而不是平滑后的摇杆量（后者松手后还要几十毫秒才落到阈值以下，
            // 而镜头在那几十毫秒里多转的每一度都会被看见）。
            bool stickHeld = player != null && player.StickHeld;

            // ---- 「那个必须看得见的敌人」在不在画面里 ----
            // 度量每帧都算，且【不藏在任何分支里】：一旦藏进战斗分支，
            // 就会变成"只要附近有敌人，探索跟随整条被吞掉"。度量与决策必须分开。
            float halfHFov = HalfHorizontalFov();
            float enemyWindow = halfHFov *
                Mathf.Lerp(FocusWindowRatio, FocusWindowCombatRatio, _combatBlend);
            _focusScreenAng = 0f;
            _focusDist = 99f;
            bool disengaging = false;
            if (_focusEnemy != null)
            {
                if (!_focusInit) { _focusPos = _focusEnemy.position; _focusInit = true; }
                // 低通滤【位置】而不是屏幕角：后者含镜头自身的转动，滤它等于把回路
                // 反馈也延迟 τ≈0.31s，与伺服时间常数同量级，必然振荡
                _focusPos = Vector3.Lerp(_focusPos, _focusEnemy.position,
                    1f - Mathf.Exp(-AimLowPass * dt));
                _focusScreenAng = ScreenAngleTo(_focusPos);
                Vector3 toFoe = _focusEnemy.position - target.position; toFoe.y = 0f;
                _focusDist = toFoe.magnitude;
                // 脱战撤离：持续朝【背离这个敌人】的方向全速跑。这时玩家的意图是"走"，
                // 不是"打"——兜底若照旧生效，镜头会扭回去盯着你正在逃离的那个敌人，
                // 你就变成对着屏幕底部往里跑，完全违背意图。
                float runRef0 = player != null ? player.runSpeed : 5.2f;
                disengaging = moveSpeed > runRef0 * 0.5f && _focusDist > 0.2f &&
                              Vector3.Angle(Quaternion.Euler(0f, _headingAvg, 0f) * Vector3.forward,
                                            toFoe.normalized) > 115f;
                if (Mathf.Abs(_focusScreenAng) > enemyWindow) _focusFrameT += dt;
                else _focusFrameT = 0f;
            }
            else { _focusInit = false; _focusFrameT = 0f; }
            // 敌人真的出窗了才算"看不见"，且要持续一小会儿——这条兜底的唯一职责
            // 是保证"打着打着敌人不知道在哪"不会发生，其余时刻它必须闭嘴。
            // 只管【近身】的那个对手（8m 内）：十米开外跑动中的追兵不属于
            // "我把对手打丢了"，那种情形由 threatAhead 那条路负责。
            bool enemyLost = _focusEnemy != null && !manualLook && !disengaging &&
                _focusDist < CombatFollowRange &&
                _focusFrameT > Mathf.Lerp(FocusFrameHold, FocusFrameHoldCombat, _combatBlend);

            // ===== 视野开阔度 → 运镜紧迫度（本轮的核心裁决）=====
            // 三条来源取【最大】——任何一条成立都算"看不清要去的地方"：
            //   ① 当前画面本身就闭塞（沿画面正前只看得到几米）；
            //   ② 要去的方向马上要撞上东西（剩余时间，不是剩余距离——
            //      走得慢就不急，跑得快才急，这才是玩家真实的紧迫感）；
            //   ③ 要去的方向压根不在画面里（开阔地朝镜头跑：通视很长，却完全看不见路）。
            // 用【剩余时间】的好处是它提前很多就开始上升：5.2m/s 跑速下
            // TtiRelaxed=3s 对应 15.6m，也就是墙还在十几米外镜头就不紧不慢地开始转，
            // 等真到墙边早已对好——**盲区被提前化解掉，而不是发生后再抢救**。
            // 这正是"避免镜头突然快速转动导致头晕"的正解：不是把快的那一下调慢，
            // 而是让它根本不需要发生。
            _sightNow = Mathf.Lerp(_sightNow, SightDistance(_curYaw), 1f - Mathf.Exp(-4f * dt));
            _sightGoing = Mathf.Lerp(_sightGoing, SightDistance(_headingAvg),
                1f - Mathf.Exp(-4f * dt));
            float closedUrge = 1f - Mathf.InverseLerp(SightTight, SightOpen, _sightNow);
            float tti = _sightGoing / Mathf.Max(1.2f, moveSpeed);
            float ttiUrge = 1f - Mathf.InverseLerp(TtiUrgent, TtiRelaxed, tti);
            // 「要去的地方」在画面里的偏角：用 0.9 秒后的位置作为视察点
            float lookAhead = Mathf.Clamp(moveSpeed * 0.9f, 2f, 6f);
            float aheadAng = Mathf.Abs(ScreenAngleTo(target.position +
                Quaternion.Euler(0f, _headingAvg, 0f) * Vector3.forward * lookAhead));
            float offUrge = Mathf.InverseLerp(halfHFov * OffScreenStart,
                                              halfHFov * OffScreenFull, aheadAng);
            // 站定时没有"要去的方向"，②③ 两条一律不成立（否则"转身看正脸"会被
            // 读成"前方 180° 全是盲区"，镜头随即绕走——那是上一版最典型的误判）
            if (moveSpeed < 0.6f) { ttiUrge = 0f; offUrge = 0f; }
            // 紧迫度本身必须临界阻尼：它调制着转速，若阶跃，转速就跟着阶跃，
            // 又变回"猛地开始转、猛地停住"。0.5s 让快慢之间是渐变而不是换挡。
            _urgency = Mathf.SmoothDamp(_urgency,
                Mathf.Max(closedUrge, Mathf.Max(ttiUrge, offUrge)),
                ref _urgencyVel, 0.5f, Mathf.Infinity, dt);

            // ===== 只有 ① 是【转镜头能解决】的那一项 =====
            // 逐项检查"转镜头能不能改善它"：
            //   ① 当前画面闭塞（沿画面正前 C 的通视）→ **能**，转镜头就是换看的方向；
            //   ② 要去的方向快撞上（沿 H 的剩余时间）→ 不能，H 与 C 绑在一起转；
            //   ③ 要去的方向不在画面里（前视点屏幕角）→ 不能，已算出它恒为 45.5°，与 C 无关。
            // 而"跑动中转向"触发的恰恰是 ②③。用三项的最大值去授权旋转，
            // 等于**为两个转多少度都白转的理由付出角色画弧的代价**——
            // 这就是"转向后偏离目标方向、走弧线、还得用摇杆补方向"的全部来源。
            // 所以：**旋转额度与持续绕行只由 ① 支配。**
            // ②③ 仍然照常驱动死区/转速/确认时长与取景（留白+吊杆），
            // 那些要么不影响角色轨迹，要么本来就是"更不动"的方向。
            _viewBlocked = Mathf.SmoothDamp(_viewBlocked, closedUrge,
                ref _viewBlockedVel, 0.5f, Mathf.Infinity, dt);

            // ===== 转向额度：镜头该响应的是【转向这个动作】，不是【偏差这个状态】=====
            //
            // 这是"转向时镜头几乎不跟"的根治办法。原因在于代数环：移动是镜头相对的
            //（H = C + θ ⇒ H − C ≡ θ），偏差**恒等于摇杆离轴角**，镜头转多少都不减小。
            // 于是任何"按偏差决定转速"的伺服都会被持续绕行限速（30°/s）压住——
            // 那是为了不让屏幕一直转而必须存在的闸，代价就是转向时几乎看不出镜头在跟。
            //
            // 大作的实际做法（魂系/法环/只狼/黑神话/地平线都是这套观感）：
            // **一次明确的转向 → 镜头迅速摆过去一个有限的量 → 停。**
            // 快，但有始有终；不是持续伺服，也不是无限绕行。玩家转向后画面很快
            // 重新定向，于是他自然会把摇杆推回"画面正前"，环就此收敛——
            // 人在回路里，而镜头只负责把那一下做得干脆。
            //
            // 实现成一份【额度】：
            //   · 计量用**拇指角 θ**（相对画面），它与镜头无关——镜头怎么转，
            //     按住不动的手指其 θ 恒定。用世界方向计量会被环带着走，永不停止。
            //   · **拇指稳下来**才记账：搓杆/绕圈时 θ 一直在扫，压根不给额度，
            //     镜头维持慢速绕行（这正是"摇杆转圈屏幕不该跟着转"）；
            //     一旦定在某个方向上，立刻把这次转了多少度换成额度。
            //   · 额度随镜头**实际转过的度数**扣减，也随时间泄漏。
            //     用完即回落到常规限速——**幅度有上限，所以永远不会变成转圈**。
            if (stickHeld && player.StickWorldDir.sqrMagnitude > 0.01f)
            {
                // **必须减 _curYaw，不能减 _yaw**——这是上一版"镜头又开始晃 + 按住摇杆
                // 会绕圈回到原方向"的全部原因。摇杆的世界方向由 PlayerController 用
                // 【渲染】偏航（cameraTransform.eulerAngles.y，即 _curYaw）解算，
                // 而 _curYaw 比目标偏航 _yaw 滞后约 转速×rotationSmoothTime：
                // 170°/s 时滞后 18.7°。拿 _yaw 去减就等于
                //     thumb = θ + (_curYaw − _yaw)
                // 那个滞后项在镜头起停时来回摆 19°，正好顶在 25° 的记账门槛上：
                //     发额度 → 镜头快转 → 滞后变大 → thumb 又跳 → 再发额度 → …
                // 自激循环，170°/s 下 2.1 秒就能转满一圈——角色也就跟着绕回原方向。
                // 减 _curYaw 得到的才是干净的 θ：**镜头怎么转它都不变。**
                // 实时映射下没有任何偏置，thumb 就是干净的 θ：镜头怎么转它都不变。
                float thumb = Mathf.DeltaAngle(_curYaw,
                    Quaternion.LookRotation(player.StickWorldDir.normalized).eulerAngles.y);
                if (!_thumbInit) { _thumbInit = true; _thumbPrev = thumb; }
                float thumbRate = Mathf.Abs(Mathf.DeltaAngle(_thumbPrev, thumb)) / dt;
                _thumbPrev = thumb;
                // 稳＝拇指角变化率低于 70°/s；连续稳住 0.09s 才算"这就是我要的方向"
                _thumbSettleT = thumbRate < 70f ? _thumbSettleT + dt : 0f;
                // 刚才这一下转向【有多急】的峰值保持（衰减 400°/s²）。
                // **必须用拇指角速率，不能用朝向角速度**：后者含镜头自己转动的份额
                //（镜头转 ⇒ 角色被带转 ⇒ 朝向角速度升高 ⇒ 提速 ⇒ 转更快…），
                // 那是一个正反馈，正是之前"绕圈回到原方向"的同款成因。
                // 拇指角速率与镜头无关：手指不动它就是 0，镜头转多快都不变。
                _thumbRatePeak = Mathf.Max(_thumbRatePeak - 400f * dt, thumbRate);
                _thumbAbs = Mathf.Abs(thumb);   // 要去的方向离画面正前多远＝镜头要转多远
            }
            else { _thumbInit = false; _thumbSettleT = 0f; _thumbRatePeak = 0f; }

            // ===== 转向缓冲拉远（本轮）=====
            // 跑动中突然改方向的那一刻，正是最需要看清周围的时刻：先把吊杆推出去
            // 一截，视野立刻多出一圈，镜头就有余裕慢慢转过去——**用空间换时间，
            // 免掉那记"猛转一下"**。与紧迫度是同一思路的两只手：一个把盲区提前化解，
            // 一个在转向当下直接给出更多可见范围。
            // 用【拉远吊杆】而不是【放大 FOV】：变焦会显著加剧晕动，纯位移只是把画面
            // 推开，是这几个杠杆里唯一还有余量且无副作用的。
            //
            // 判据是【拇指角速率的峰值】——玩家刚才那一下转向有多急。
            // 不能用镜头偏差（恒等于 θ，稳态推着不放也一直大，吊杆会长期停在拉远位）；
            // 更不能用朝向角速度（含镜头自己转动的份额，构成正反馈，
            // 正是"绕圈回到原方向"的同款成因）。拇指角速率与镜头无关。
            // 再乘"方向已定下来"：搓杆时永不成立 ⇒ 搓杆不触发。
            // 200°/s 起算、600°/s 满级（90° 的甩杆约 600~900°/s）；再乘速度。
            //
            // **不走 _lenFactor**（那条是 0.75s 的分镜级慢插值，一次 0.4 秒的转向
            // 在它那里几乎看不出来）。这里单独走一条起快收慢的临界阻尼：
            // 起 0.45s（峰值推拉约 1.2 m/s，属舒适区）、收 1.2s（转完慢慢还回去，
            // 不会"推出去又立刻缩回来"的呼吸感）。
            // 再乘一道"方向已定下来"：搓杆时朝向角速度一直很高，
            // 若不加这一条，吊杆会被长期顶在拉远位（+22%），读作画面忽远忽近。
            float turnWant = Mathf.Clamp01((_thumbRatePeak - 200f) / 400f)
                             * Mathf.Clamp01(moveSpeed / 3f)
                             * (_thumbSettleT > 0.09f ? 1f : 0f);
            // 瞬态起手的那一刻锁存【本次要转多远】：小转向本来就没多少盲区，
            // 慢一点无所谓；180° 掉头则是盲区最深、慢下来绕得最远的那一档，必须快。
            if (turnWant > 0.05f && _turnBurst < 0.05f) _turnMag = _thumbAbs;
            _turnBurst = Mathf.SmoothDamp(_turnBurst, turnWant, ref _turnBurstVel,
                turnWant > _turnBurst ? 0.45f : 1.2f, Mathf.Infinity, dt);

            // ---- 一键回正（玩家显式触发）：触屏双击转镜区 / 桌面 V 键 ----
            if (MobileInput.ConsumeRecenter() ||
                (!Application.isMobilePlatform && Input.GetKeyDown(KeyCode.V)))
                StartRecenter(RecenterTargetYaw(lockTarget),
                              ManualRecenterAccelBudget, ManualRecenterMinTime);

            // ===== 「停下来之后自动回正」已撤销（本轮）=====
            // 对照成熟大作，这一档在**任何一款都不存在**：
            //   · 魂系 / 只狼 / 艾尔登法环 / 黑神话悟空：未锁定时镜头【只在你移动时】
            //     缓缓跟到身后；你一停，镜头就停在原处，不会再自己摆。回正是一个
            //     显式按键（右摇杆按下 / 锁定键），从来不是"停下"这个事件的后果。
            //   · 战神(2018/诸神黄昏)、地平线、对马、刺客信条：同样只有跟随 + 显式回正键。
            //   · 塞尔达：ZL / X 是玩家自己按的复位，停步不触发任何运镜。
            // 道理也站得住：**停下来恰恰是玩家想看看四周、看看自己角色的时刻**，
            // 这时候把镜头摆走是最不合时宜的一次运镜；而且它无法预测——
            // 玩家没做任何动作画面却动了，那正是"镜头有自己的想法"这种糟糕观感的来源。
            //
            // 功能上它也已经没有存在的必要：连续跟随本来就在你【移动时】把镜头带到
            // 身后，且盲区时会自动提速（见 _urgency）；停下之后即使构图偏一点，
            // 按你的开阔度原则——只要看得见远方，那就不是问题。
            // 下一次起跑时跟随会立刻接管，自己就正回来了。
            // 保留【一键回正】：那是这个类型的通行解，玩家自己按的运动不引起晕动。

            if (_recenterT > 0f)
            {
                _recenterT -= dt;
                // 平滑收尾曲线（SmoothStep）：起止都无速度突变，不违反限幅精神
                float u = 1f - Mathf.Clamp01(_recenterT / Mathf.Max(0.01f, _recenterDur));
                _yaw = Mathf.LerpAngle(_recenterFrom, _recenterTo, Mathf.SmoothStep(0f, 1f, u));
                _yawFollowVel = 0f;
                _autoYawRate = 0f;
                _lastManualLook = -99f;   // 回正后立刻允许自动跟随接管，不留 1.2s 空窗
            }

            // 自动运镜的角加速度限幅——从这里开始记录基线。
            // 手动转镜（_yaw += lookX）发生在本方法更上方，因此不在限幅范围内：
            // 玩家自己甩镜是自发运动，不引起晕动，限它只会读作迟钝。
            float yawBeforeAuto = _yaw;

            if (_recenterT > 0f) { /* 回正接管本帧的偏航，跳过常规自动运镜 */ }
            else if (combat)
            {
                // ===== 战斗机位（此前是全片最粗糙的一段，现与探索共用同一套稳定设施）=====
                //
                // 旧实现：wantYaw = 瞬时「玩家→敌人」方位，死区 4°，直接 MoveTowardsAngle。
                // 探索分支拥有的低通滤波、稳定门槛、迟滞、意图识别、威胁感知、墙壁减速、
                // 手动让位——战斗分支【一条都没有】。而近身缠斗恰恰是方位角变化最剧烈的
                // 时刻：距离 2m、敌人横移 2.5m/s，方位角变化率就有 71°/s，远超 4° 死区，
                // 于是镜头全程追着这个抖动摆。最需要稳的时候用了最原始的伺服。
                Vector3 toEnemy = lockTarget.position - target.position;
                toEnemy.y = 0;
                float enemyDist = toEnemy.magnitude;
                if (enemyDist > 0.3f)
                {
                    float rawAim = Quaternion.LookRotation(toEnemy / enemyDist).eulerAngles.y;
                    if (!_aimInit) { _aimAvg = rawAim; _aimInit = true; }
                    // ① 低通滤波：滤掉近身横移带来的高频方位抖动
                    _aimAvg = Mathf.LerpAngle(_aimAvg, rawAim, 1f - Mathf.Exp(-AimLowPass * dt));

                    // ② 3/4 侧位（"最佳拍摄角度"）：镜头不站在玩家正后方的延长线上。
                    // 正后方是最平庸的机位——玩家挡住敌人、双方都只有背影/正面两个平面。
                    // 偏开一个肩位后是对峙的经典三分构图：玩家侧背、敌人侧脸，
                    // 拳脚的运动轨迹横穿画面而不是朝镜头戳过来（纵向运动在屏幕上没有位移感）。
                    // 偏哪一侧由空间决定（见 _shoulderSide 的迟滞选择），且切换极慢。
                    // 换角偏置叠加在肩位之上：被柱子挡住时整体绕开一点，
                    // 而不是把镜头一路推到贴脸还对着那根柱子
                    float wantYaw = _aimAvg + _shoulderSide * ShoulderAngle * _lockBlend
                                    + _occYawBias;

                    // ③ 自适应软区：越近，同样的敌人横移带来的方位角变化越大，软区也越大。
                    // 上限由 20° 收到 12°——渐进软区已经不靠尺寸抗抖（那是低通的活），
                    // 软区只负责"别追极小的偏差"，过大只会换来松弛感。
                    float aimDead = Mathf.Clamp(4f + 8f / Mathf.Max(0.8f, enemyDist), 4f, 12f);
                    float aimErr = Mathf.Abs(Mathf.DeltaAngle(_yaw, wantYaw));

                    // ④ 软死区：目标退到死区边缘，驱动量随偏差连续趋零。
                    // 此前是二值迟滞开关，关断时硬置零速度＝无限 jerk（见 SoftTarget 注释）
                    float softAim = SoftTarget(_yaw, wantYaw, aimDead);

                    // ⑤ 手动转镜让位：此前锁定时玩家的手动取景被完全无视，
                    // 刚摆好角度镜头立刻抢回去，读作"和我抢镜头"
                    if (!manualLook)
                    {
                        // ⑥ 速度按偏差连续映射（与探索同一条曲线）：小偏差慢、掉头快
                        float t = Mathf.Clamp01((aimErr - aimDead) / 120f);
                        float smoothT = Mathf.Lerp(0.42f, 0.14f, t);
                        float maxSpd = Mathf.Lerp(autoFollowSpeed * 1.1f,
                                                  autoFollowSpeed * 6.4f, t);
                        // ⑦ 墙壁减速：别把镜头甩进墙里（探索分支早就有，战斗分支没有）
                        if (aimErr > 45f)
                        {
                            float freeAt = FreeBoomDistance(target.position + Vector3.up * _pivotH,
                                wantYaw, offset.magnitude * _lenFactor);
                            float damp = Mathf.Lerp(0.3f, 1f, Mathf.InverseLerp(1.8f, 3.2f, freeAt));
                            maxSpd *= damp;
                            smoothT /= Mathf.Max(0.3f, damp);
                        }
                        _yaw = Mathf.SmoothDampAngle(_yaw, softAim, ref _yawFollowVel,
                            smoothT, maxSpd, dt);
                    }
                    // 速度衰减而非硬置零——硬置零正是 1509°/s² 的来源
                    else _yawFollowVel = Mathf.MoveTowards(_yawFollowVel, 0f, 900f * dt);
                }
            }
            else if (autoFollow)
            {
                bool moving = moveSpeed > 0.6f;
                bool manualRecently = manualLook;
                bool fighting = fightingNow;
                // ===== 「正在往某处去」＝【手指还在推】且【确实在移动】=====
                //
                // 这一行直接决定"停下来镜头还会不会转"。此前只看 moveSpeed，
                // 而 moveSpeed 是 8/s 的低通均速（τ=0.125s）：松杆后角色 0.12 秒就停了，
                // 均速却要 **0.27 秒**才掉到 0.6 的门槛，再加驱动滑停 0.45 秒——
                // 合计 0.72 秒的继续转动；开阔地 90° 偏差下峰值 89°/s，扫过约 **40°**。
                // 玩家早已松手站住，画面还在慢慢转，这就是"停下来还会转动镜头"。
                //
                // 而【松开摇杆】这个信号**零延迟**：移动完全由摇杆驱动，手指一离开
                // 就是"我不去了"，不需要等任何滤波追上来。80ms 宽限只为挡住摇杆读数
                // 在阈值上的偶发抖动，不构成可感知的拖尾。
                //
                // 交战中额外放行"只推杆不移动"：出招/技能期间移动被定步锁住，
                // 角色只转不移动，此时仍要跟。反过来非战斗时推杆必然产生位移，
                // 由 moving 覆盖；站着点一下改朝向撑不到 0.6m/s 的均速，正好被滤掉。
                bool active = stickHeld && (moving || fighting);

                // ===== 战斗中：稳定是第一性原则（本轮的核心）=====
                // 交战时方位角的变化率最高（2m 距离、敌人横移 2.5m/s ⇒ 71°/s），
                // 而玩家的手指也最忙——出招、闪避、格挡之间摇杆一直在小幅游走。
                // 偏偏跟随的偏差【恒等于摇杆离轴角 θ】（H = C + θ ⇒ H − C ≡ θ），
                // 于是 θ 的抖动被一比一地翻译成镜头角速度：这就是"打着打着画面频繁抖动"。
                // 治法不是加平滑（平滑只改相位，不改幅频比），而是**在战斗中把死区
                // 整体抬高、把速度整体压低**，让小幅的方向游走完全落在死区里：
                //   · 死区 10° → 30°（交战满档）：贴身互殴的日常摇杆游走全被吃掉；
                //   · 大幅换向门槛 45° → 62°：真的掉头才动镜；
                //   · 转速压到 55%：万一要动，也是慢慢地动。
                // 代价是"构图偏一点"——那由下面的兜底与松杆自动回正来收，
                // 而不是靠镜头每帧微调。镜头要么纹丝不动，要么在做一个明确的运镜。
                float calm = _combatBlend;
                // ===== 视野开阔度决定"该不该动、动多快"（本轮）=====
                // 开阔（_urgency→0）：死区 28°、转速 45%——斜着跑也不动镜，动也是慢的；
                // 盲区（_urgency→1）：死区回到 10°、转速全开——赶紧把要去的方向框进来。
                // 中间是连续过渡，没有换挡感（_urgency 本身已做临界阻尼）。
                float dzSight = Mathf.Lerp(SightDeadZoneOpen, exploreReorientAngle, _urgency);
                float sightSpd = Mathf.Lerp(0.45f, 1f, _urgency);
                // 交战时另有一套（稳定第一）：死区固定抬到 30°、转速压到 55%。
                // 战斗里"看得见敌人"由下面的出画兜底负责，不靠通视距离。
                float dz = Mathf.Lerp(dzSight, 30f, calm);
                float calmSpd = Mathf.Lerp(sightSpd, 0.55f, calm);
                // 唯一的例外：那个必须看得见的敌人已经【真的出画】了。
                // 稳定压倒一切，但"看不见敌人"不是稳定，是失职——这条兜底存在的
                // 全部理由，就是让"打着打着敌人不知道在哪"不可能发生。
                if (enemyLost) { dz = exploreReorientAngle; calmSpd = 1f; }

                // ===== 统一渐进回正（转向尺度 → 镜头同步幅度，连续映射，无分档断层）=====
                // 此前战斗/探索是互斥的两条分支：战斗中只有 >55° 才回正，
                // 12°—55° 的中等转向【完全没有跟随】——战斗中长期存在的部分盲区。
                // 现在合并为一条：
                //   · 中小幅转向（>10°）+ 有移动或推杆 → 温和跟随，越大越快；
                //   · 大幅换向（战斗 >55° / 探索 >45°）→ 迟滞开关锁定追击，直到 <12° 才松开，
                //     保证掉头/转身打背后敌人时迅速把新方向框进画面；
                //   · 手动转镜期间一律让位给玩家。
                //
                // 跟随的目标：平常是角色朝向；那个必须看得见的敌人真的出画时，
                // 改为"把它推回取景窗沿"——不是推到画面中央。推到中央意味着镜头得
                // 跟着敌人的每一步走，那正是"永远在轻微动"；推回窗沿则一到位就停。
                float aimAt = meanYaw;
                if (enemyLost)
                    aimAt = _yaw + Mathf.Sign(_focusScreenAng) *
                            (Mathf.Abs(_focusScreenAng) - enemyWindow + FocusEdgeSoft);
                float err = Mathf.Abs(Mathf.DeltaAngle(_yaw, aimAt));

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
                // 判据修正（此前用"朝镜头行进了多久"，把掉头往回跑一并冻住 0.6 秒，
                // 实测造成 2.2~3.6 秒盲区——正是"掉头时镜头跟不上"的主因）：
                // 「转身看正脸／贴墙调整站位」与「掉头往回跑」的输入相同、意图相反，
                // 可靠的区分不是时长而是【速度】——前者原地转身或碎步，后者全速持续移动。
                // 半速以上一律认定为"我要往那个方向去"，镜头立刻跟，不再等待。
                float runRef = player != null ? player.runSpeed : 5.2f;
                bool committedRun = moveSpeed > runRef * 0.55f;
                bool backingIntent = towardCamera && !fighting && !threatAhead
                                     && !committedRun
                                     && _towardCamT < TowardCameraHold;

                // 大幅换向门槛：交战中抬高到 62°（只有真的掉头才值得动镜）
                // 大幅换向的追击闸门同样吃视野系数：开阔地里连 70° 的转向都不追
                //（反正看得见），闭塞处 45° 就追。
                float bigAngle = Mathf.Max(Mathf.Lerp(70f, 45f, _urgency),
                                           Mathf.Lerp(0f, 62f, calm));
                float holdNeed = Mathf.Lerp(0.15f, 0.28f, calm) * Mathf.Lerp(1.6f, 1f, _urgency);
                // 那个方向有敌人：几乎立刻开始转过去（把"发现追兵"的延迟压到最低）
                if (threatAhead || enemyLost) { holdNeed = 0.06f; bigAngle = 35f; }
                // 追击闸门同样要求【真的在往某处去】：此前它不看 active，于是站着原地
                // 转 90° 就会锁存住把镜头绕过去——正是"站着改朝向镜头却在转"的来源。
                // 背后有威胁 / 敌人已出画时豁免：那两种情形必须看过去。
                if (err > bigAngle && _headingHoldT > holdNeed && !backingIntent &&
                    (active || threatAhead || enemyLost)) _combatReorient = true;
                // 【停下来就解除】——这是"停下来镜头还在自己回正"的真正来源。
                // 此前的解除条件只有"偏差追到 <0.6×死区"：跑动中转向会把这个锁存打开，
                // 而代数环让偏差一直等于摇杆离轴角、根本追不下去；于是你一停，
                // 锁存还在，镜头就自顾自地继续绕到你背后——**玩家没做任何动作，
                // 画面却在动**。跟随的前提永远是"你正在往某处去"，停了就该停。
                else if (err < dz * 0.6f || _headingHoldT < 0.1f || backingIntent ||
                         !active) _combatReorient = false;

                // 稳定确认时长随偏差缩短：小幅修正需要确认（防摇杆抖动牵动镜头），
                // 但 180° 掉头本身就是无歧义的意图，再等 0.15s 纯粹是加盲区。
                // 再乘一道视野系数：**开阔时不必立刻决定**（0.33s 才起手，转向转一半
                // 又改主意的都被吃掉），盲区时按原值立刻起手。
                // 开阔加权（×2.2）的本意是"开阔时不必立刻决定"，防的是转一半又改主意。
                // 但**大幅转向本身就是无歧义的意图**，再压 0.33 秒纯粹是延迟。
                // 于是让加权随转向幅度退掉：45° 以下照旧稳一稳，90° 以上立即起手。
                float steadyNeed = Mathf.Lerp(0.15f, 0.02f, Mathf.Clamp01((err - 45f) / 90f))
                                   * Mathf.Lerp(Mathf.Lerp(2.2f, 1f, _urgency), 1f,
                                                Mathf.Clamp01((err - 45f) / 45f));
                // 门槛由"朝向稳定了多久"换成"这串输入有多像一个方向"：
                // 连续换向时前者永远不成立（急转把计时清零），后者仍有 0.7 的置信度，
                // 于是镜头朝那一串换向的均值**缓缓摆正**——正是"跑一段弧线逐步摆正"。
                bool gentle = active && dirTrust > 0.01f && err > dz && !backingIntent
                              && _headingHoldT > steadyNeed * 0.35f;

                // 跟随的【驱动强度】走临界阻尼，而不是布尔开关。
                // 停下时若直接把伺服关掉，_yaw 会在有速度的那一帧硬停——速度台阶＝
                // 无限 jerk，读作"卡一下"。改成 0.3s 内把上限速降到 0：
                // 镜头是**自己滑停**的，而不是被掐断的。
                // 起手要柔、收手要**当机立断**：起步慢是防抖，停得慢是拖尾，
                // 两者诉求相反，不该共用一个时间常数。
                //   · 起 0.18s（0.3→0.18：起手快一拍能让镜头早约 0.15 秒真正动起来，
                //     而峰值角加速度只有 26°/s ÷ 0.18s ≈ 145°/s²，远在舒适区 600 之内——
                //     **"平稳"由角加速度决定，不由起手时间常数决定**）；
                //   · 收 0.06s ≈ 4 帧：**手指离开摇杆的那一刻就不再往前转了**。
                // 这不会造成"卡一下"——_yaw 停住之后，渲染用的 _curYaw 还要经
                // rotationSmoothTime(0.11s) 的临界阻尼追上来，那 4~5° 的收尾
                // 是纯粹的减速曲线，读作"镜头稳稳停住"，不是一次运镜决定。
                bool followWant = !manualRecently && (_combatReorient || gentle);
                float driveT = followWant ? 0.18f : 0.06f;
                _followDrive = Mathf.SmoothDamp(_followDrive, followWant ? 1f : 0f,
                    ref _followDriveVel, driveT, Mathf.Infinity, dt);

                // enemyLost 独立放行：它的 err 只是"超出窗沿多少度"，通常很小，
                // 落不进 gentle（需要在移动/推杆）也落不进 _combatReorient（需要大角度）。
                // 而"敌人已经出画"本身就是充分理由，不该再要求玩家正在跑。
                bool enemyNet = !manualRecently && enemyLost;
                if (_followDrive > 0.02f || enemyNet)
                {
                    // 0°→180° 连续映射：平滑时间 0.5s→0.15s、转速上限 70→340°/s。
                    // 小幅修正依旧慢而稳（防抖），大角度掉头迅速跟上（防盲区）。
                    float t = Mathf.Clamp01((err - dz) / 130f);
                    float smoothT = Mathf.Lerp(exploreTurnSmoothTime, 0.15f, t)
                                    / Mathf.Max(0.3f, calmSpd);
                    // 「偏差越大转越快」这条升速曲线（最高 4×）**是为了压盲区时长而设的**，
                    // 而开阔地根本没有盲区可压——那里 90° 偏差也照样一览无余。
                    // 让升速倍率本身也吃视野系数：开阔 1.4×（90° 偏差约 42°/s，
                    // 是"缓缓跟过去"），盲区仍是原来的 4×（最高 340°/s，掉头不留盲区）。
                    // 顺带把停下时的余量转动也砍掉一半——峰值速度低了，滑停自然就短。
                    float esc = Mathf.Lerp(1.4f, 4f, _urgency);
                    float maxSpd = Mathf.Lerp(exploreMaxSpeed * 0.82f,
                                              exploreMaxSpeed * esc, t) * calmSpd
                                   * Mathf.Max(_followDrive, enemyNet ? 1f : 0f);
                    // （曾在此处加过「掉头甩镜」把上限提到 5×。加入角加速度限幅后，
                    //   4× 与 5× 的实测盲区都是 0.97s——限幅才是瓶颈，倍率已无影响。
                    //   一条不再改变行为的特殊分支只会让运镜更难预测，故移除。）

                    // ===== 意图识别 ②：不要把镜头甩进墙里 =====
                    // 玩家面壁转身时，"绕到角色背后"恰好是墙的方向——硬绕过去只会让镜头
                    // 顶在墙上被迫贴脸，视野瞬间塌掉。大幅回正前先探一下目标方位是否够宽敞：
                    // 越憋屈就转得越慢（最低降到 25%），把镜头留在看得见的地方。
                    // ===== 缓慢绕行：实时映射下唯一可用的对齐手段 =====
                    //
                    // 摇杆恒为镜头相对、一帧都不错开（PlayerController 里没有任何偏置）。
                    // 于是 H = C + θ 全时成立，镜头每转 1°，角色的行进方向就转 1°。
                    // 这条恒等式给出三个同时被同一个速率 ω 决定的量：
                    //     角色弧线半径 = v/ω     玩家需要的补杆速率 = ω     对齐耗时 = θ/ω
                    // 也就是说：**转得快 = 弧紧 + 补杆急**，转得慢 = 弧缓 + 补杆轻。
                    // 补杆的【总量】是固定的（θ 必须降到 0），速率只决定它摊在多长时间里。
                    //
                    // 更要紧的一点：**只要玩家在补杆，角色就是直线**——弧只在完全不补杆时
                    // 才出现。所以速率要慢到"补杆是无意识的"，人手自然就跟上了。
                    // 26°/s ⇒ 半径 11.5m、一次 90° 转向的补杆摊在 3.5 秒里。
                    // 这正是魂系/悟空那一派的做法，也是它们不显得"镜头跟你较劲"的原因。
                    //
                    // 速率随离轴角【比例】给出，而不是一到门槛就满速：
                    //   · 离轴 < 死区（开阔 28°）⇒ 跟随本身不成立 ⇒ 一动不动；
                    //   · 死区 → 死区+60° 之间线性升到满速；
                    //   · 再往上封顶。于是日常的小幅走位完全不动镜，只有真的换方向才缓缓跟。
                    // 室内不做跟随绕行：屋里两三步一堵墙，镜头绕着人转就是晕动的主因，
                    // 而房间里本来也不需要"看见远处的路"——那正是这条运镜存在的理由。
                    if (player != null && !enemyNet && !IndoorMode)
                    {
                        Vector3 sw = player.StickWorldDir;
                        if (sw.sqrMagnitude > 0.04f)
                        {
                            float stickYaw = Quaternion.LookRotation(sw.normalized).eulerAngles.y;
                            float off = Mathf.Abs(Mathf.DeltaAngle(stickYaw, _curYaw));
                            float ramp = Mathf.Clamp01((off - dz) / 60f);
                            float orbit = SustainedOrbitCap * ramp;
                            // 前方真的闭塞时允许快一点（那时"看得见路"值得多付一点弧）
                            orbit *= Mathf.Lerp(1f, 1.8f, _viewBlocked);
                            // ===== 转向起始的瞬态提速：让"看见前方"来得更早 =====
                            // 稳态速率决定弧的松紧，必须慢；但**刚转向的那一两秒**
                            // 玩家本来就在主动改方向，多转的那一点被他自己的操作盖住了，
                            // 是这条曲线上唯一还能提速而不显得"镜头跟你较劲"的窗口。
                            // 用 _turnBurst（低通后的转向角速度，且要求方向已定下来，
                            // 搓杆不算）做包络：起 0.45s、收 1.2s，峰值约 0.7。
                            // **一个反直觉但可以算出来的事实**：对齐一次 90° 转向，
                            // 角色走的是半径 R=v/ω 的圆弧、总共转过 90°，
                            // 所以绝对偏离 = R(1−cos90°) = R ——**速率越慢，绕出去越远**：
                            //     26°/s ⇒ 半径 11.5m、耗时 3.5s、横向绕出 11.5m
                            //     90°/s ⇒ 半径  3.3m、耗时 1.0s、横向绕出  3.3m
                            // 慢并不等于"弧小"，只等于"绕得久、绕得远"。
                            // 真正的约束只有角加速度（晕动），而 90°/s 配 0.45s 的平滑
                            // 升起只有约 120°/s²，舒适上限是 600——离得很远。
                            // 而且这个账**随转向幅度急剧恶化**：180° 掉头走的是半圆，
                            // 横向绕出是 2R——75°/s 要绕出 7.9m、跑 2.4 秒，
                            // 正是"走了很大一段弧才摆正"。所以倍率必须随幅度给：
                            //   60° 以下 ×2.5   （峰值 33°/s，本来就没多少盲区，慢无所谓）
                            //   90°      ×5.4   （峰值 94°/s，1.15s、半径 3.2m）
                            //   170°+    ×10    （峰值 203°/s，1.27s、半径 1.5m）
                            // 提速侧峰值角加速度 401°/s²，仍在舒适上限 600 之内；
                            // 减速侧不限（下游限幅本就只限提速——拦着镜头停下只会拖尾）。
                            float boost = Mathf.Lerp(2.5f, 10f,
                                Mathf.Clamp01((_turnMag - 60f) / 110f));
                            orbit *= Mathf.Lerp(1f, boost, _turnBurst);
                            // 置信度直接缩放速率：一串换向按其"有多像一个方向"
                            // 给出相应的摆正速度，搓杆则趋零。
                            orbit *= dirTrust;
                            maxSpd = Mathf.Min(maxSpd, orbit);
                        }
                    }

                    if (err > 45f)
                    {
                        Vector3 probePivot = target.position + Vector3.up * _pivotH;
                        float freeAtTarget = FreeBoomDistance(probePivot, aimAt,
                                                              offset.magnitude * _lenFactor);
                        float room = Mathf.InverseLerp(1.8f, 3.2f, freeAtTarget);   // 0=贴墙 1=开阔
                        // 下限 0.25→0.6：旧值把贴墙掉头的转镜从 1.15s 拖到 2.60s，
                        // 换来的只是吊杆好看一点——而吊杆顶墙本来就有碰撞回缩兜底，
                        // 盲区比构图要命得多。掉头幅度越大越不减速（见 urgency）。
                        float urgency = Mathf.Clamp01((err - 45f) / 90f);
                        float damp = Mathf.Lerp(Mathf.Lerp(0.6f, 1f, room), 1f, urgency);
                        maxSpd *= damp;
                        smoothT /= Mathf.Max(0.6f, damp);
                    }

                    // 软死区：驱动随偏差连续趋零，门槛开合处不再有速度突变
                    float softHeading = SoftTarget(_yaw, aimAt + _occYawBias, dz);
                    _yaw = Mathf.SmoothDampAngle(_yaw, softHeading, ref _yawFollowVel,
                        smoothT, maxSpd, dt);
                }
                else _yawFollowVel = Mathf.MoveTowards(_yawFollowVel, 0f, 900f * dt);
            }
            // 限幅落地：把本帧自动运镜的角速度变化钳在 MaxAutoYawAccel 内。
            // 晕动与角【加速度】相关，与角速度关系小得多——实测掉头时的峰值加速度
            // 由 2311°/s² 降到 795，代价只是盲区 0.77→0.97s，这个交换是值得的。
            {
                float autoRate = Mathf.DeltaAngle(yawBeforeAuto, _yaw) / dt;
                // 只限【提速】，放开【减速】：突然加速才引起晕动，而阻止镜头停下来
                // 只会制造拖尾。伺服本身是临界阻尼，它的减速曲线已经是平滑的。
                if (Mathf.Abs(autoRate) > Mathf.Abs(_autoYawRate))
                {
                    float lim = Mathf.Abs(_autoYawRate) + MaxAutoYawAccel * dt;
                    autoRate = Mathf.Clamp(autoRate, -lim, lim);
                }
                _yaw = yawBeforeAuto + autoRate * dt;
                _autoYawRate = autoRate;
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
                // 坠落时不等 2.5s 的闲置延迟、也不看水平速度：**落点在角色下方**，
                // 不压一点俯角它就在画面外，而"看清落点"正是腾空景别存在的理由。
                // 22°/s ⇒ 8° 的俯角偏置 0.4 秒到位，赶得上一次普通跳跃。
                else if (falling)
                    _pitch = Mathf.MoveTowards(_pitch, defaultPitch + _shot.pitchBias, 22f * dt);
                else if (Time.unscaledTime - _lastManualLook > pitchRecenterDelay && moveSpeed > 1.2f)
                    _pitch = Mathf.MoveTowards(_pitch, defaultPitch + _shot.pitchBias, 10f * dt);
            }

            _lastTargetPos = target.position;

            // ---- 临界阻尼转角（无过冲），位置刚性跟随（零滞后） ----
            // 二级平滑用【固定】时间常数。此前随偏差从 0.11 缩到 0.05——
            // 变时间常数意味着阻尼特性在运动中途改变，本身就制造加速度不连续。
            // 掉头的速度改由上游的角加速度限幅统一保证，不需要在这里再抄近路。
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
                _offAxisRun = Mathf.SmoothDamp(_offAxisRun, 0f, ref _offAxisVel,
                    0.45f, Mathf.Infinity, dt);
                _leadVec = Vector2.SmoothDamp(_leadVec, Vector2.zero, ref _leadVel,
                    0.35f, Mathf.Infinity, dt);
            }
            else if (moveSpeed > 1.5f)
            {
                // ===== 引导留白（lead room）：离轴奔跑时唯一有效的开阔视野手段 =====
                //
                // 摇杆是镜头相对的 ⇒ 角色朝向 H = 镜头偏航 C + 摇杆角 θ ⇒ H - C ≡ θ 恒成立。
                // 即【摇杆推在离轴 θ 时，镜头在几何上不可能转到角色行进方向的背后】，
                // 与镜头转多快毫无关系：实测持续推左 90°，绕行上限由 30°/s 提到 340°/s，
                // 偏离角只从 87.9° 缩到 75.2°，而角色自转从 29°/s 暴涨到 207°/s——
                // 提速换不来视野，只换来转圈。横向/后向奔跑的盲区必须靠取景解决。
                //
                // 做法：焦点朝移动方向前移，把角色让到画面后侧、前方空间纳入画面。
                // 留白量随【离轴角】增大——正前奔跑视锥纵深本就足够，横向/后向最需要。
                // 上限 2.2m：角色偏离画面中心 43%，仍属三分位构图；2.6m(51%) 就贴边了。
                Vector2 vdir = new Vector2(_planarVel.x, _planarVel.z);
                if (vdir.sqrMagnitude > 0.04f)
                {
                    // 留白量【本身】必须走平滑：offAxis 随摇杆瞬变，若直接用，
                    // 焦点会在 0.09s 的跟随时间内平移 1.75m（≈19m/s 的镜头平移），
                    // 那是比原盲区更糟的晕动源。0.5s 过渡把它压到 ≈3.5m/s。
                    Vector3 vn = new Vector3(vdir.x, 0, vdir.y).normalized;
                    Vector3 camFwd = Quaternion.Euler(0, _curYaw, 0) * Vector3.forward;
                    float offAxis = Mathf.Clamp01(Vector3.Angle(vn, camFwd) / 90f);
                    // 临界阻尼而非 MoveTowards：线性斜坡在起止两端各有一次加速度台阶，
                    // 焦点会"猛地开始平移、猛地停住"——与偏航的阶跃是同一个毛病。
                    //
                    // **转向当下把这条通路加快到 0.22s**（常态 0.45s）：
                    // 既然镜头不再靠旋转来响应转向（旋转只会让角色画弧），
                    // 那这次转向的可见响应就全落在【引导留白 + 拉远吊杆】上，
                    // 它必须来得及——0.45s 太温吞，读作"镜头没反应"。
                    // 实测这两样把前视点的屏幕角从 45.5° 压到 23.9°（取景窗 36.8°），
                    // 等价于 21.6° 的"虚拟转镜"，而角色的轨迹一点没被动。
                    _offAxisRun = Mathf.SmoothDamp(_offAxisRun,
                        offAxis * Mathf.Clamp01(moveSpeed / 5.2f), ref _offAxisVel,
                        Mathf.Lerp(0.45f, 0.22f, _turnBurst), Mathf.Infinity, dt);
                    // 交战中大幅收敛引导留白（2.2m → 0.5m）：留白是为【长距离奔跑】
                    // 看清前方而设的，而近身缠斗的走位是短促往复——每一次侧闪/后撤
                    // 都会让焦点前后甩动最多 2.2m，那是位置侧最大的一处晃动源，
                    // 换来的"看清前方"在两米开外的对峙里根本用不上。
                    float lead = Mathf.Clamp01(moveSpeed / 5.2f)
                                 * Mathf.Lerp(0.45f * (1f - 0.5f * _combatBlend),
                                              Mathf.Lerp(2.2f, 0.5f, _combatBlend), _offAxisRun);
                    // ===== 留白必须平滑【向量】，不能只平滑长度（本轮修的抖动源）=====
                    // 留白把取景点沿行进方向前移最多 2.2m，而镜头是刚性挂在取景点上的。
                    // 摇杆快速搓圈时行进方向在转 ⇒ 那个 2.2m 的偏移跟着绕圈 ⇒
                    // **镜头也在画一个半径 2.2m 的圆**：一秒一圈就是 13.8 m/s 的横移，
                    // 半秒一圈 27.6 m/s。这就是"搓杆时角色原地转圈、画面同时晃"里
                    // 属于镜头的那一半——它与偏航跟随毫无关系，所以之前把旋转压到 0
                    // 也没治到它。（之前只平滑了 _offAxisRun 这个"长度"，方向是瞬时的。）
                    //
                    // 改成对**留白向量本身**做临界阻尼：搓圈时各方向互相抵消，
                    // 均值趋零 ⇒ 取景点几乎不动；按住一个方向时它照常收敛到满值。
                    // 时间常数看【方向定没定下来】：拇指已稳 ⇒ 0.22s（转向要跟得上），
                    // 还在扫 ⇒ 0.7s（把搓杆彻底压平）。判据用拇指角而非世界方向，
                    // 与镜头无关，不会被镜头自己的运动带跑。
                    Vector2 leadWant = vdir.normalized * lead;
                    float leadT = _thumbSettleT > 0.09f ? 0.22f : 0.7f;
                    _leadVec = Vector2.SmoothDamp(_leadVec, leadWant, ref _leadVel,
                        leadT, Mathf.Infinity, dt);
                    focusXZ += _leadVec;
                }
                else
                {
                    _offAxisRun = Mathf.SmoothDamp(_offAxisRun, 0f, ref _offAxisVel,
                        0.45f, Mathf.Infinity, dt);
                    _leadVec = Vector2.SmoothDamp(_leadVec, Vector2.zero, ref _leadVel,
                        0.35f, Mathf.Infinity, dt);
                }
            }
            else
            {
                _offAxisRun = Mathf.SmoothDamp(_offAxisRun, 0f, ref _offAxisVel,
                    0.45f, Mathf.Infinity, dt);
                _leadVec = Vector2.SmoothDamp(_leadVec, Vector2.zero, ref _leadVel,
                    0.35f, Mathf.Infinity, dt);
            }
            if (!_pivotInit) { _pivotY = _pivotYAnchor = targetPivotY; _pivotXZ = focusXZ; _focusAnchor = focusXZ; _pivotInit = true; }

            // 电影三脚架感·焦点死区：小于死区的焦点位移完全不推镜——近身互殴时
            // 拳脚带来的细碎换位（突进/击退/侧闪的残余）不再传导成镜头晃动；
            // 只有真正的走位才移镜。战斗死区大（稳如三脚架），探索死区小（跟手）。
            // 判据用【交战程度】而不是【是否锁定】：锁定默认是手动开的，
            // 于是绝大多数近身互殴一直吃着"探索档"的跟手参数——
            // 这是"打起来镜头抖"里属于【位置】的那一半（另一半是偏航，见上方跟随）。
            float steady = Mathf.Max(_lockBlend, _combatBlend);
            float dead = Mathf.Lerp(0.03f, 0.15f, steady);
            Vector2 drift = focusXZ - _focusAnchor;
            if (drift.magnitude > dead) _focusAnchor = focusXZ - drift.normalized * dead;

            // 纵向死区（与水平的焦点死区同理）：CharacterController 贴地、上下台阶、
            // 斜坡行走会让 y 每帧小幅跳动，0.13s 的平滑挡不住这种高频输入，
            // 表现为画面持续轻微上下浮动。落地/跳跃这类真实高度变化远超死区，照常跟。
            float dyErr = targetPivotY - _pivotYAnchor;
            if (Mathf.Abs(dyErr) > PivotYDeadZone)
                _pivotYAnchor = targetPivotY - Mathf.Sign(dyErr) * PivotYDeadZone;
            _pivotY = Mathf.SmoothDamp(_pivotY, _pivotYAnchor, ref _pivotYVel, 0.13f,
                Mathf.Infinity, dt);
            // 战斗中位置阻尼加重（斯坦尼康式慢移），探索保持跟手
            float fst = Mathf.Lerp(followSmoothTime, 0.24f, steady) * _shot.damping;
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
                // 离轴奔跑再拉远 12%：与引导留白叠加后，横跑的可见前方由 5.5m 增至 8.1m
                wantFactor = 1f + runOut + _offAxisRun * 0.12f;
            }
            // 大招镜头：短暂拉近（覆盖当前构图，结束自动回稳）
            // 推近幅度随特写强度分级：击杀轻推、超必杀满推——不再一律贴到 0.82
            float zoomTo = Mathf.Lerp(1f, ultimateZoom, _closeStrength);
            wantFactor = Mathf.Lerp(wantFactor, zoomTo, _ultimateBlend);
            wantFactor *= _shot.distanceMult;   // 景别决定景深：群战拉远、狭窄收紧、决胜推近
            // 变焦极慢（电影推轨是分镜级动作，不是逐帧伺服）：缠斗中距离忽近忽远
            // 不再造成镜头前后泵动
            // 临界阻尼而非 Lerp：Lerp 面对阶跃目标会在第一帧就给出最大速度
            //（≈0.78 m/s 的推拉），临界阻尼则从 0 平滑加速再平滑停下
            _lenFactor = Mathf.SmoothDamp(_lenFactor, wantFactor, ref _lenFactorVel,
                0.75f, Mathf.Infinity, dt);

            Vector3 boomDir = (rot * offset).normalized;
            float maxDist = offset.magnitude * _lenFactor * (1f + _turnBurst * TurnDollyOut);

            // ===== 室内舒适模式（玩家反馈"在房间里转一圈就晕"）=====
            //
            // 成因不是帧率，是**吊杆在屋里一直被墙推来推去**：4.6 米的吊杆在一间
            // 十来米的房间里，每转一次身、每过一道门，碰撞回缩就在 1.9 与 4.6 之间
            // 抽一次——那是一次次幅度近 3 米的推轨，而人对"景深忽远忽近"最敏感。
            // 叠上跟随运镜的缓慢绕行，走一圈下来就是晕动。
            //
            // 屋里本来也不需要那么长的吊杆：把它收到 2.9 米、并且**两个方向用同一个
            // 平滑时间**（不再"回缩快、伸出慢"），墙就几乎碰不到它，画面稳下来。
            if (IndoorMode)
            {
                maxDist = Mathf.Min(maxDist, IndoorBoom);
                _lenFactor = Mathf.MoveTowards(_lenFactor, 1f, dt);   // 不做景别推拉
            }

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
                // 下限见 BoomFloorIndoor/Outdoor：**下限不能高过障碍物的距离**，
                // 否则镜头就被按在障碍物里面（那才是玩家看到的"镜头被遮挡"）
                float floor = IndoorMode ? BoomFloorIndoor : BoomFloorOutdoor;
                wantDist = Mathf.Min(wantDist, Mathf.Max(floor, hit.distance - 0.1f));
            }
            // 回缩仍然快（避免穿墙），但【伸出恢复】明显加快（0.3→0.14s）：
            // 转身扫过障碍后视野立刻回到正常景别，不再长时间贴脸发窄
            // 遮挡持续计时：供上方的换角决策使用（0.6m 以上的回缩才算真被挡）
            _occludedT = wantDist < maxDist - 0.6f ? _occludedT + dt : 0f;

            // 室内两个方向同一个时间常数：一快一慢正是"推拉抽动"的来源
            float smooth = IndoorMode ? 0.10f : (wantDist < _boomDist ? 0.03f : 0.14f);
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
            // 视线高度随景别变化：特写抬高到面部（看清神情与这一击落点），
            // 群战压低（把包围圈与脚下空位纳入画面）。此前恒定不变，
            // 于是特写推近了却仍然对着胸口，"推近"只是变大而没有变成特写。
            float lookUp = 0.38f + 0.12f * _lockBlend
                           + 0.30f * _closeStrength * _ultimateBlend
                           - 0.10f * Mathf.Clamp01(_shot.heightBias / 0.28f);
            transform.rotation = Quaternion.LookRotation(pivot + Vector3.up * lookUp - pos);

            // 景别的焦段：群战广角看局势、决胜长焦压缩更有分量。
            // 变焦本身极慢（跟随 _shot 的插值），不会形成"呼吸式"变焦的不稳感。
            var camc = GetComponent<Camera>();
            if (camc != null && !Presets[PresetIndex].fp)
                camc.fieldOfView = Mathf.MoveTowards(camc.fieldOfView,
                    fieldOfView + _shot.fovBias, 12f * dt);
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

        /// <summary>
        /// 【通视距离】：从取景点沿某个方位平射出去，多远才被挡住（封顶 SightMax）。
        /// 这是"视野开不开阔"的直接度量，也是本轮运镜快慢的唯一裁决者。
        /// 与 FreeBoomDistance 的区别：那条是往【镜头要退去的方向】探（吊杆会不会顶墙），
        /// 这条是往【要看/要去的方向】平探（看不看得见远方）。
        /// </summary>
        // 这条探测每帧要跑两次（画面正前 + 行进方向），必须无 GC：
        // SphereCastAll 每次都会新建数组，60fps 下就是每秒 120 次分配。
        static readonly RaycastHit[] SightHits = new RaycastHit[8];

        float SightDistance(float yawDeg)
        {
            Vector3 eye = target.position + Vector3.up * (_pivotH + 0.5f);
            Vector3 dir = Quaternion.Euler(0f, yawDeg, 0f) * Vector3.forward;
            float best = SightMax;
            int n = Physics.SphereCastNonAlloc(eye, SightProbeRadius, dir, SightHits, SightMax,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var hit = SightHits[i];
                if (hit.distance <= 0.001f) continue;
                var col = hit.collider;
                // 忽略动态碎屑、玩家与敌人本体：它们不构成"视野受限"，
                // 敌人挡一下反而是你正想看的东西
                if (col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic) continue;
                if (col.GetComponentInParent<PlayerController>() != null) continue;
                if (col.GetComponentInParent<AI.EnemyController>() != null) continue;
                best = Mathf.Min(best, hit.distance);
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
