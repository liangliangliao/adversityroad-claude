using UnityEngine;
using AdversityRoad.Mobile;

namespace AdversityRoad.Player
{
    /// <summary>
    /// 第三人称跟随镜头。
    ///
    /// 全部自动运镜共用同一套稳定设施——**探索与战斗不再是两套逻辑**：
    ///   ① 目标方位低通滤波（探索追朝向 _headingAvg，未锁定的战斗追行进方向 _travelAvg，
    ///      锁定时追敌我方位 _aimAvg）；
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
    /// **未锁定战斗的偏航：追【敌人】，用取景窗（本轮定案）**
    ///
    /// 这里走过两次弯路，都源于**追错了东西**：
    ///   · 先追【角色朝向】——战斗中它是全场最跳的量（出招磁吸每一击瞬转向敌人、
    ///     翻滚起手硬转、推杆出招转向 150°/s，都低于 220°/s 的清零阈值一路畅通），
    ///     于是"摇杆稍一动镜头就抖"；
    ///   · 改追【真实行进方向】——抖动收敛了，但残余是几何上必然的：移动是镜头相对的
    ///     ⇒ 行进方向 = 镜头偏航 + 摇杆角 ⇒ **偏差 ≡ 摇杆离轴角 θ**，
    ///     追一个恒等于 θ 的目标永远不会收敛，镜头一直缓缓爬，读作"永远不安定"；
    ///   · 于是干脆全关——**结果把镜头的首要职责也扔了：打着打着敌人跑出画面**。
    ///
    /// 两次弯路的共同点：追的都是 C+θ。而战斗中该框住的从来是**敌人**——
    /// 敌人方位不是镜头偏航的函数，没有代数环，追它天然收敛。
    ///
    /// 追法用**取景窗**而不是伺服到画面中央：窗内**完全不动**，出窗才**推回窗沿**
    /// （推到中央意味着镜头必须跟着敌人的每一步走，那正是"永远在轻微动"）。
    /// 于是"不抖"与"看得见"不再互相牺牲：不动是常态，动一定是因为真的要出画。
    ///
    /// 两个曾经踩过的实现坑：
    ///   · **窗宽必须按真实相机几何算**。第一版写死 34°（照 FOV 半宽估的），
    ///     漏掉了镜头在玩家身后 4.6m 造成的向心压缩——近身敌人的屏幕角天花板
    ///     只有 19°(1.5m)/25.8°(2m)/32.9°(2.5m)，**永远够不到 34°**，
    ///     那段代码于是每帧判"在窗内"而什么都不做（"修复没有任何效果"）。
    ///     现由 <see cref="HalfHorizontalFov"/> 从 fieldOfView+aspect 精确解出。
    ///   · **方位角要从镜头量、且要能表示"在镜头背后"**：用 Vector3.SignedAngle
    ///     相对镜头当前朝向求，|角|&gt;90° 自然表示已经转到镜头后方。
    ///
    /// **什么时候回正、什么时候绝不回正**（判据只有一条，战斗与探索共用）：
    ///     **你接下来要面对的东西，现在看得见吗？**
    ///     战斗看正在打的敌人；探索看角色前方 6m 的「视察点」；都用同一个取景窗量。
    /// 用"视察点在不在窗内"而不是"朝向转了多少度"，是因为前者自动把镜头在身后 4.6m
    /// 造成的向心压缩算进去了。代入几何得到的实际阈值——
    ///     转向 15°→屏幕角 8.5° · 30°→17.0° · 45°→25.6°（都在窗内，**不动镜**）
    ///     转向 55°→31.4° · 90°→52.5° · 180°→180°（出窗，**需要回正**）
    /// 即：**±50° 以内的转向、走位、闪避、出招转向一律不动镜；超过约 55° 才回正。**
    ///
    /// 「想回正」与「能回正」分开：想＝出窗持续 0.2s；能＝摇杆松开（或已推回接近画面
    /// 正前）。"能"是几何硬要求——推着离轴 θ 时把镜头转到行进方向背后无解，
    /// 转多快都只是让角色画更紧的圈，而且回正结束、参考系解冻的一刻会把玩家
    /// "掉头回去"。只想不能时由探索伺服走 30°/s 缓弧 + 引导留白撑住，
    /// 玩家松杆的那一瞬间立刻兑现（阈值 0.07s，掉头时拇指过中心很快，抓得紧）。
    ///
    /// 另有一条**无条件兜底**：敌人彻底出画超过 0.5s 就强制回正，不等松杆也不等冷却。
    /// 原一键回正照常保留，与自动回正共用同一条 <see cref="StartRecenter"/>。
    /// 探索分支原样保留（有绕行限速与实测调参兜底）。
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

        // 注：上一版在这里加过「镜头自动跟随」的三档设置（关闭/仅探索/智能）。
        // 它唯一的作用是提供一种【看不见敌人】的玩法，而且把战斗框敌与自动回正
        // 一并挡在了 Smart 档之后——玩家只要切过一次档，两条保命逻辑就全失效。
        // 一个设置项若能把镜头调到"看不见你正在打的人"，那不是选项，是坑。已撤回，
        // 恢复成原来的单一开关 autoFollow。

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
        /// <summary>离轴奔跑强度（0~1，平滑）：横向/后向跑时拉远取景，进一步扩大可见前方。</summary>
        float _offAxisRun;
        float _towardCamT;                 // 持续朝镜头行进的时长（区分"转身看一眼"与"真要往那走"）
        // ---- 镜头导演：景别选择与平滑过渡 ----
        ShotProfile _shot;                 // 当前生效的景别（插值后的实时值）
        float _nextShotScan;               // 敌情扫描节流
        int _nearbyEnemies;
        float _roomAround = 99f;           // 吊杆方位的可用空间（识别狭窄场地）
        bool _tightLatch;                  // 「狭窄」景别的迟滞锁存（2.6m 进 / 3.4m 出）
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
        const float SustainedOrbitCap = 30f;
        /// <summary>摇杆偏离画面正前多少度起算限速，以及渐变过渡的宽度。
        /// 12°/30°：正前小幅修正完全不限速（那正是"边跑边微调、镜头跟着走"该有的样子），
        /// 45° 以上进入满限速。实测弧线半径 正前=直线 / 30°=5.6m / 45°=10.1m /
        /// 90°=10.2m / 180°=10.5m，全部落在大作区间。</summary>
        const float OrbitGateAngle = 12f;
        const float OrbitGateWidth = 30f;
        /// <summary>方向突变后允许快速绕行的【角度预算】与其速度上限。
        /// 只钉死 30°/s 的代价是"跑着突然改方向，镜头好几秒才反应过来"；
        /// 一味提速又只会让角色画更紧的圈（环还在）。给一份有限预算：
        /// 突变后先快转 70°（约 0.55s），花完落回 30°/s 缓弧——
        /// "突然改方向"立刻有反应，"一直推着离轴不放"仍不转圈，
        /// 而被诱导的弧的上限就是这 70°，是个能算清的量。</summary>
        const float OrbitBurstBudget = 70f;
        const float OrbitBurstCap = 130f;
        float _orbitBudget = OrbitBurstBudget;
        float _lastStickOff;               // 上一帧的摇杆离轴角（＝拇指角，检测方向突变）
        bool _stickOffInit;

        // ---- 一键回正（业界通行的"逃生口"）----
        // 摇杆是镜头相对的 ⇒ H = C + θ，而"镜头对着角色正前方"要求 C = H ⇒ θ = 0。
        // 即：只要摇杆推在非正前方向，镜头在几何上就不可能对着角色正前方——
        // 与转速、聚焦点都无关。大作对此的解法不是让镜头去追一个无解的目标，
        // 而是给玩家一个显式动作，一次性把镜头拉到行进方向背后。
        // 回正期间【锁存移动参考系】：否则镜头快速绕行会把角色一起拖着转，
        // 0.35s 内可拖出 90°，回正反而把人转晕。
        const float RecenterTime = 0.35f;
        float _recenterT;
        float _recenterDur = RecenterTime;   // 本次回正的时长（自动回正更从容）
        float _recenterFrom, _recenterTo;
        float _lastRecenter = -99f;
        /// <summary>回正进行中——PlayerController 据此冻结移动参考系。</summary>
        public bool RecenterActive => _recenterT > 0f;

        // ===== 战斗自动聚焦（未锁定）：取景窗 =====
        //
        // 上一轮取消了交战中的连续伺服，理由是"追行进方向永远不收敛"——那个判断没错，
        // 但连带把**镜头的首要职责**也扔了：打着打着敌人跑出画面、不知道在哪。
        // 复盘下来，真正的错误在更早的地方——**追错了东西**：
        // 无论追"角色朝向"还是"行进方向"，追的都是 C+θ，既抖又不收敛；
        // 而战斗中该框住的从来是**敌人**。敌人方位不是镜头偏航的函数，没有代数环，
        // 追它天然收敛。
        //
        // 用【取景窗】而不是伺服到画面中央（Mark Haigh-Hutchinson 的经典做法）：
        //   · 敌人方位落在窗内 → 镜头**完全不动**（窗很大，走位的细碎变化根本够不着）；
        //   · 出窗 → 只把它**推回窗沿**，不追到中央——推到中央意味着镜头必须持续跟着
        //     敌人的每一步走，那正是上一轮要根治的"永远在轻微动"。
        // 于是"不抖"与"看得见"不再互相牺牲：不动是常态，动一定是因为真的要出画。
        /// <summary>
        /// 取景窗半宽占【水平半视野】的比例。窗宽必须按真实相机几何算，不能拍脑袋定角度：
        /// 上一版直接写死 34°（照 FOV 半宽 47° 估的），却漏掉了**镜头在玩家身后 4.6m
        /// 造成的向心压缩**——玩家身边的东西在画面上天然靠中间。实测天花板：
        ///   敌人距玩家 1.5m → 屏幕水平角最大 19.0°
        ///                2.0m → 25.8°     2.5m → 32.9°     3.0m → 40.7°
        /// 也就是说近身缠斗（1.5~2.5m）**永远达不到 34°**，那段代码每帧都判"在窗内"
        /// 而什么都不做——这正是"修复没有任何效果"的原因。
        /// 现在改为从 Camera 的 fieldOfView + aspect 精确解出水平半视野再取比例，
        /// 换 FOV / 换屏幕比例都自动跟着走。
        /// </summary>
        const float FocusWindowRatio = 0.58f;
        /// <summary>推回窗沿时的软区：驱动量在窗沿附近连续趋零，不在边界上硬启停。</summary>
        const float FocusEdgeSoft = 10f;
        const float FocusAcquireRange = 12f;   // 12m 内选聚焦目标
        const float FocusKeepRange = 16f;      // 已选中的目标到 16m 才放弃（迟滞）
        /// <summary>敌人持续出画多久就【强制】回正到它身上（绕过松杆与间隔条件）。
        /// 这是"永远看得见正在打的人"的硬兜底：取景窗调得再准也可能有漏网的情形，
        /// 而"看不见敌人"是不可接受的失败模式，必须有一条无条件生效的通路。</summary>
        const float FocusLostForce = 0.5f;
        Transform _focusEnemy;             // 未锁定时的战斗聚焦目标
        Vector3 _focusPos;                 // 其世界位置的低通（滤位置而不是滤屏幕角）
        bool _focusInit;
        float _focusOutT;                  // 聚焦敌人已出画多久
        bool _focusActive;                 // 本帧是否该框住聚焦敌人（撤离中为假）
        float _focusScreenAng;             // 聚焦敌人的有符号屏幕水平角
        bool _aheadOut;                    // 「前方视察点」已出取景窗＝看不见要去的地方
        bool _turnFollow;                  // 战斗中的大幅转向跟随已点火（<12° 熄火）
        Camera _cam;                       // 取水平视野用

        /// <summary>
        /// 「前方视察点」的距离：角色朝向前方这么远的一个点。
        /// 判断"玩家看不看得见自己要去的地方"就看这个点在不在取景窗内——
        /// 比直接比朝向角靠谱得多，因为它自动把【镜头在身后 4.6m】的向心压缩算进去了。
        /// 6m 约等于全速跑 1.2 秒的距离：够远，能代表"前方"；不至于远到永远出窗。
        /// </summary>
        const float LookAheadDist = 6f;

        /// <summary>某个世界点的有符号屏幕水平角（度）。
        /// 用镜头【真实的 forward】量：肩后构图的横向偏移让实际视线偏离 _curYaw 约 5.6°，
        /// 从 _curYaw 反推会让取景窗左右不对称。|角|&gt;90° 天然表示"在镜头背后"。</summary>
        float ScreenAngleTo(Vector3 worldPoint)
        {
            Vector3 camFwd = transform.forward; camFwd.y = 0f;
            if (camFwd.sqrMagnitude < 1e-4f)
                camFwd = Quaternion.Euler(0f, _curYaw, 0f) * Vector3.forward;
            camFwd.Normalize();
            Vector3 to = worldPoint - transform.position; to.y = 0f;
            if (to.sqrMagnitude < 1e-4f) return 0f;
            return Vector3.SignedAngle(camFwd, to.normalized, Vector3.up);
        }

        /// <summary>水平半视野（度）——由垂直 FOV 与画面宽高比精确解出。</summary>
        float HalfHorizontalFov()
        {
            if (_cam == null) _cam = GetComponent<Camera>();
            float vfov = _cam != null ? _cam.fieldOfView : fieldOfView;
            float aspect = _cam != null && _cam.aspect > 0.01f
                ? _cam.aspect : Mathf.Max(0.5f, (float)Screen.width / Mathf.Max(1, Screen.height));
            return Mathf.Atan(Mathf.Tan(vfov * 0.5f * Mathf.Deg2Rad) * aspect) * Mathf.Rad2Deg;
        }

        /// <summary>聚焦目标是否仍然有效（活着、在保持距离内、仍在交战）。</summary>
        bool FocusStillValid()
        {
            if (_focusEnemy == null || target == null) return false;
            var ec = _focusEnemy.GetComponentInParent<AI.EnemyController>();
            if (ec == null || ec.State == AI.EnemyState.Dead) return false;
            if (ec.State == AI.EnemyState.Idle || ec.State == AI.EnemyState.Patrol) return false;
            return (_focusEnemy.position - target.position).sqrMagnitude <
                   FocusKeepRange * FocusKeepRange;
        }

        // ===== 自动回正：把"一键回正"这个动作接进自动运镜 =====
        // 交战中连续伺服被取消（原因见 followHold），镜头因此完全静止。需要重新取景时
        // 不再靠每帧微调去逼近，而是**触发一次有始有终的回正动作**——
        // 镜头要么纹丝不动，要么在做一个明确的运镜，没有"一直在轻微动"的中间态。
        // 那个中间态正是"很敏感、频繁抖动"的本体。
        //
        // ===== 什么时候需要回正、什么时候绝不回正 =====
        //
        // 判据只有一条，并且对战斗与探索是同一条：
        //     **你接下来要面对的东西，现在看得见吗？**
        //         战斗 → 正在打的敌人；   探索 → 角色前方 6m 的「视察点」。
        //     看得见（在取景窗内）→ 镜头一动不动。看不见 → 需要回正。
        //
        // 用"视察点在不在窗内"而不是"朝向转了多少度"，是因为前者自动把
        // 【镜头在身后 4.6m 造成的向心压缩】算进去了。代入几何得到的实际阈值：
        //     转向  15° → 视察点屏幕角  8.5°   窗内 → 不动镜
        //     转向  30° →              17.0°   窗内 → 不动镜
        //     转向  45° →              25.6°   窗内 → 不动镜
        //     转向  55° →              31.4°   出窗 → 需要回正
        //     转向  90° →              52.5°   出窗
        //     转向 180° →             180.0°   出窗（用户说的"从前转朝后"）
        // 也就是说 **±50° 以内的转向、走位、闪避、出招转向一律不动镜**——
        // 那些方向你本来就看得见；**超过约 55° 才回正**——那时你确实什么都看不到。
        // 阈值是从几何解出来的，不是拍的。
        //
        // 「想回正」与「能回正」是分开的两件事：
        //   · **想**：视察点/敌人出窗持续 0.2s（滤掉一闪而过）→ 置起 _wantRecenterT；
        //   · **能**：摇杆松开，或摇杆已推回接近画面正前（离轴 <35°）。
        //     这一条是几何硬要求：移动是镜头相对的 ⇒ 行进方向 = 镜头偏航 + 摇杆角 θ。
        //     推着离轴 θ 时把镜头转到行进方向背后【无解】——转多快都一样，
        //     只会让角色画更紧的圈（实测提到 340°/s，偏离角只从 87.9° 缩到 75.2°，
        //     自转却涨到 207°/s）。更糟的是回正一结束、参考系解冻的那一刻，
        //     摇杆的世界方向会整体旋转 θ 度，玩家会被"掉头回去"。
        //     松杆时环消失，回正既收敛又无副作用——所以要等那一刻，并且抓得很紧。
        // 两者同时满足才执行；只"想"不"能"时，由探索伺服以 30°/s 走缓弧、
        // 配合引导留白与转向拉远撑住视野，等玩家松杆的那一瞬间立刻兑现。
        const float AutoRecenterGap = 1.5f;         // 两次自动回正的最小间隔
        const float AutoRecenterWantHold = 0.2f;    // 出窗需持续多久才算"想回正"
        const float AutoRecenterStickIdle = 0.07f;  // 摇杆松开多久即可兑现（掉头时拇指过中心很快，抓得紧一点）
        const float AutoRecenterStickAligned = 35f; // 摇杆离画面正前小于此角也可兑现（环很弱）
        const float AutoRecenterMinAmount = 25f;    // 摆不到这个幅度就不值得动镜
        const float AutoRecenterMinTime = 0.4f;
        const float AutoRecenterMaxTime = 0.8f;
        float _stickIdleT;                 // 摇杆松开时长
        float _wantRecenterT;              // 「看不见要去/要打的地方」已持续多久

        /// <summary>
        /// 回正目标方位，按"最能说明此刻该看哪"的顺序取：
        /// 锁定目标 &gt; 战斗聚焦的敌人 &gt; 角色朝向。
        /// 有敌人时就该看敌人——这是"打着打着不知道敌人在哪"的正面回答。
        /// </summary>
        float RecenterTargetYaw(Transform lockTarget)
        {
            if (target == null) return _yaw;
            Transform aim = lockTarget != null ? lockTarget
                                               : (_focusActive ? _focusEnemy : null);
            if (aim != null)
            {
                Vector3 toE = aim.position - target.position; toE.y = 0;
                if (toE.sqrMagnitude > 0.09f)
                    return Quaternion.LookRotation(toE.normalized).eulerAngles.y;
            }
            return target.eulerAngles.y;
        }

        /// <summary>启动一次回正动作（手动与自动共用同一条实现）。</summary>
        void StartRecenter(float duration, float targetYaw)
        {
            _recenterDur = Mathf.Max(0.05f, duration);
            _recenterT = _recenterDur;
            _recenterFrom = _yaw;
            _recenterTo = targetYaw;
            _lastRecenter = Time.unscaledTime;
            // 两个计时都必须清零，否则"回正结束 → 计时仍超阈值 → 立刻又触发"，
            // 卡成死循环（出画计时只在取景窗那条分支里累加，而回正期间那条分支被跳过）。
            _focusOutT = 0f;
            _wantRecenterT = 0f;
        }

        /// <summary>本帧是否该自动触发一次回正（条件见上方注释）。</summary>
        bool ShouldAutoRecenter(Transform lockTarget, bool fighting, bool manualLook)
        {
            if (!autoFollow) return false;
            if (Presets[PresetIndex].fp) return false;   // 第一人称：镜头即眼睛，绝不自动摆
            if (lockTarget != null) return false;        // 锁定有自己的取景伺服
            if (_recenterT > 0f || manualLook) return false;

            // ===== 硬兜底：敌人已经彻底出画 =====
            // "看不见正在打的人"是不可接受的失败模式，必须有一条【无条件】生效的通路：
            // 不等松杆、不等冷却，一律把镜头摆回敌人身上。取景窗调得再准也可能有漏网
            // 情形（贴墙、被挤到角落、敌人瞬移式突进），这条保证最多只持续 0.5 秒。
            if (_focusOutT > FocusLostForce) return true;

            // ---- 「想」：看不见要去/要打的地方，且已持续够久 ----
            if (_wantRecenterT < AutoRecenterWantHold) return false;
            if (Time.unscaledTime - _lastRecenter < AutoRecenterGap) return false;

            // ---- 「能」：摇杆松开，或已推回接近画面正前（此时代数环很弱）----
            bool stickFree = _stickIdleT >= AutoRecenterStickIdle;
            if (!stickFree && player != null)
            {
                Vector3 sw = player.StickWorldDir;
                stickFree = sw.sqrMagnitude > 0.04f &&
                            Mathf.Abs(ScreenAngleTo(target.position + sw.normalized * 4f))
                                < AutoRecenterStickAligned;
            }
            if (!stickFree) return false;

            float want = RecenterTargetYaw(lockTarget);
            // 摆不到一定幅度就不值得动镜（避免"为了 5° 也来一次运镜"）
            if (Mathf.Abs(Mathf.DeltaAngle(_yaw, want)) < AutoRecenterMinAmount) return false;
            // 别把镜头摆进墙里：目标方位退不开吊杆就放弃这次回正（下次再说）
            float free = FreeBoomDistance(target.position + Vector3.up * _pivotH, want,
                offset.magnitude * _lenFactor);
            return free > 2.2f;
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

        float _headingAvg;                 // 平滑朝向（低通滤波；探索时即镜头的追踪目标）
        float _headingHoldT;               // 当前朝向的稳定时长（大幅瞬转会清零重计）
        float _lastHeading;
        bool _headingInit;

        // ---- 行进方向（低通）----
        // 用于威胁判据（"我确实在朝那个方向跑"）与移动构图，而不是角色朝向：
        // 战斗中朝向被出招磁吸/推杆转向/翻滚硬转搅得剧烈跳变，位移方向则天然免疫
        //（站着挥拳时它根本不存在）。
        float _travelAvg;                  // 低通后的行进方向（世界偏航）
        bool _travelInit;
        /// <summary>低于此速度视为"没在走位"（runSpeed 的 ~23%：
        /// 碎步调整站位够不着它，真正的走位/拉开距离才算）。</summary>
        const float TravelMinSpeed = 1.2f;
        /// <summary>行进方向的低通收敛率（1/秒）：与 AimLowPass 同量级，
        /// 压掉 1Hz 以上的走位抖动而不影响真实的转向绕行。</summary>
        const float TravelLowPass = 4.5f;
        int _engagedEnemies;               // 9m 内【真的在交战】的敌人数（追击/攻击/硬直）
        float _combatBlend;                // 交战程度 0→1：位置阻尼与焦点死区随之加重
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

            // ---- 行进方向滤波 ----
            // 与朝向滤波同构，但信号取自【真实位移】：站着出招/转身时它压根不存在，
            // 因此出招磁吸、推杆转向、翻滚硬转都不会经由它传导成镜头运动。
            float travelSpeed = _planarVel.magnitude;
            bool travelValid = travelSpeed > TravelMinSpeed;
            if (!travelValid) _travelInit = false;
            else
            {
                float rawTravel = Quaternion.LookRotation(_planarVel / travelSpeed).eulerAngles.y;
                if (!_travelInit) { _travelInit = true; _travelAvg = rawTravel; }
                _travelAvg = Mathf.LerpAngle(_travelAvg, rawTravel,
                    1f - Mathf.Exp(-TravelLowPass * dt));
            }

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
                    // 聚焦候选：最近的交战敌人（未锁定时的取景窗就框它）
                    if (engaged && d2 < FocusAcquireRange * FocusAcquireRange && d2 < focusBest)
                    {
                        focusBest = d2;
                        focusPick = e.transform;
                    }
                    // 威胁方位缓存（14m 内）：供每帧判断"我正在转向的方向上有没有敌人"
                    if (d2 < 196f && d2 > 0.01f)
                        _threatDirs.Add(Quaternion.LookRotation(to.normalized).eulerAngles.y);
                }
                // 聚焦目标的迟滞：当前目标还有效就继续咬住，只有它失效才换人。
                // 换目标＝一次大幅摆镜，绝不能因为"另一个敌人刚好近了 0.2m"就换。
                if (!FocusStillValid())
                {
                    if (_focusEnemy != focusPick) { _focusInit = false; _focusOutT = 0f; }
                    _focusEnemy = focusPick;
                }
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
            // 「狭窄」判定加迟滞（2.6m 进 / 3.4m 出）。_roomAround 只探【吊杆要退去的
            // 那个方位】，战斗中走位一步就可能让它跨过 2.6m 阈值，于是景别在
            // 收紧贴身(距离×0.82、FOV+5) 与 对峙(×1.02、FOV−1) 之间来回切——
            // 而进狭窄的插值速率是 3.5/s（很快），读作吊杆与视野一起"泵动"。
            // 这是位置/景别侧仅次于引导留白的一处晃动源。
            if (_roomAround < 2.6f) _tightLatch = true;
            else if (_roomAround > 3.4f) _tightLatch = false;
            var wantShot = CameraDirector.Pick(
                _ultimateTimer > 0f, _nearbyEnemies,
                lockOn != null && lockOn.CurrentTarget != null,
                _tightLatch ? 0f : 99f);
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
            bool manualLook = Time.unscaledTime - _lastManualLook < autoFollowDelay;

            // ---- 交战程度：锁定 / 出招中 / 身边有敌人在打 ----
            // 未锁定的近身互殴同样是"战斗"，位置侧的稳镜与偏航策略都要认它。
            if (_playerFsm == null && player != null)
                _playerFsm = player.GetComponent<Combat.CombatStateMachine>();
            bool fightingNow = lockTarget != null || _engagedEnemies > 0 ||
                               (_playerFsm != null && _playerFsm.InCombat);
            _combatBlend = Mathf.MoveTowards(_combatBlend, fightingNow ? 1f : 0f, dt / 0.6f);

            // 摇杆松开时长：自动回正只在【松杆】时触发（推着杆时回正无解，
            // 且会在参考系解冻的瞬间把玩家正在跑的方向掰弯——见 AutoRecenter 注释）
            bool stickHeld = player != null && player.StickWorldDir.sqrMagnitude > 0.04f;
            _stickIdleT = stickHeld ? 0f : _stickIdleT + dt;

            // ---- 方向突变检测：补满绕行预算 ----
            // 量的是【拇指角】θ＝摇杆世界方向与镜头偏航之差。它的好处是：玩家把拇指
            // 按住不动时 θ 恒定（镜头再怎么转都不变），只有玩家真的改推方向才会跳变。
            if (stickHeld)
            {
                // 必须用【有符号】拇指角：取绝对值的话"从左推到右"是 |−90|→|+90|、
                // 差值为 0，完全检测不到——而那恰恰是最典型的一次方向突变。
                float stickOffNow = Mathf.DeltaAngle(_yaw,
                    Quaternion.LookRotation(player.StickWorldDir.normalized).eulerAngles.y);
                if (!_stickOffInit) { _stickOffInit = true; _lastStickOff = stickOffNow; }
                if (Mathf.Abs(Mathf.DeltaAngle(_lastStickOff, stickOffNow)) > 25f)
                    _orbitBudget = OrbitBurstBudget;   // 改推了别的方向 → 重新给预算
                _lastStickOff = stickOffNow;
            }
            else
            {
                _stickOffInit = false;
                _orbitBudget = OrbitBurstBudget;   // 松杆时环消失，下次推杆即可用满预算
            }

            // ---- 脱战撤离判定：持续朝【背离敌人】的方向全速跑 ----
            // 这时玩家的意图是"走"，不是"打"。框敌若照旧生效，镜头会扭回去盯着
            // 你正在逃离的那个敌人，你就变成对着屏幕底部往里跑——完全违背意图。
            // 撤离时把镜头交还给探索跟随（看清要去的方向），回正目标也改回角色朝向。
            bool disengaging = false;
            if (_focusEnemy != null && travelValid)
            {
                Vector3 toFoe = _focusEnemy.position - target.position; toFoe.y = 0f;
                Vector3 tdir = Quaternion.Euler(0f, _travelAvg, 0f) * Vector3.forward;
                float runRef0 = player != null ? player.runSpeed : 5.2f;
                disengaging = travelSpeed > runRef0 * 0.5f && toFoe.sqrMagnitude > 0.04f &&
                              Vector3.Angle(tdir, toFoe.normalized) > 115f;
            }
            _focusActive = _focusEnemy != null && !disengaging;

            // ---- 「看不看得见」的统一度量（每帧都算，不藏在某条分支里）----
            // 藏在分支里会出事：上一版把敌人的屏幕角算在框敌分支内，于是"只要 12m 内
            // 有敌人"就整条分支接管，探索跟随被完全吞掉——玩家跑着突然改方向，
            // 镜头一点反应都没有。度量与决策必须分开。
            float halfHFov = HalfHorizontalFov();
            float focusWindow = halfHFov * FocusWindowRatio;

            // ① 敌人的屏幕角（低通滤【位置】而不是屏幕角——后者含镜头自身转动，
            //    滤它等于把回路反馈也延迟 τ≈0.31s，与伺服时间常数同量级，必然振荡）
            _focusScreenAng = 0f;
            if (_focusActive)
            {
                if (!_focusInit) { _focusPos = _focusEnemy.position; _focusInit = true; }
                _focusPos = Vector3.Lerp(_focusPos, _focusEnemy.position,
                    1f - Mathf.Exp(-AimLowPass * dt));
                _focusScreenAng = ScreenAngleTo(_focusPos);
                if (Mathf.Abs(_focusScreenAng) > halfHFov * 0.95f) _focusOutT += dt;
                else _focusOutT = 0f;
            }
            else _focusOutT = 0f;

            // ② 前方视察点的屏幕角：看得见"要去的地方"吗。移动时用行进方向
            //    （战斗中朝向被出招磁吸拧得乱跳，位移方向才是真意图），静止时用朝向。
            float aheadYaw = travelValid ? _travelAvg : _headingAvg;
            float aheadAng = Mathf.Abs(ScreenAngleTo(
                target.position + Quaternion.Euler(0f, aheadYaw, 0f) *
                Vector3.forward * LookAheadDist));
            // 迟滞：出窗才算"看不见"，回到 0.7 倍窗宽才算"看见了"——
            // 免得在窗沿上反复开合
            if (aheadAng > focusWindow) _aheadOut = true;
            else if (aheadAng < focusWindow * 0.7f) _aheadOut = false;

            // ---- 「想不想回正」：要面对的东西出窗了 ----
            // 0.8 倍窗宽：敌人被取景窗推到窗沿后仍算"该整理一下构图"，
            // 于是松杆时会把它摆回画面中央，而不是长期停在画面边上。
            float wantAng = _focusActive ? Mathf.Abs(_focusScreenAng) : aheadAng;
            if (wantAng > focusWindow * 0.8f) _wantRecenterT += dt;
            else _wantRecenterT = 0f;

            // ---- 一键回正（玩家显式触发）：触屏双击转镜区 / 桌面 V 键 ----
            if (MobileInput.ConsumeRecenter() ||
                (!Application.isMobilePlatform && Input.GetKeyDown(KeyCode.V)))
                StartRecenter(RecenterTime, RecenterTargetYaw(lockTarget));

            // ---- 自动回正（本轮新增）：把同一个动作接进自动运镜 ----
            // 交战中不再做连续伺服（见下方 followHold 的说明），镜头因此完全静止。
            // 静止不等于放任盲区——需要重新取景时，走【一次有始有终的回正动作】，
            // 而不是每帧微调：镜头要么纹丝不动，要么在做一个明确的运镜，没有中间态。
            // 这正是"很敏感"的反面。
            if (ShouldAutoRecenter(lockTarget, fightingNow, manualLook))
            {
                float want = RecenterTargetYaw(lockTarget);
                float amount = Mathf.Abs(Mathf.DeltaAngle(_yaw, want));
                // 非玩家主动触发的运镜要更从容：时长随幅度加长，90° 峰值由 386°/s
                // 降到 ≈225°/s、180° 由 771°/s 降到 ≈353°/s，都落在舒适区内
                StartRecenter(Mathf.Lerp(AutoRecenterMinTime, AutoRecenterMaxTime,
                    Mathf.Clamp01(amount / 180f)), want);
            }

            if (_recenterT > 0f)
            {
                _recenterT -= dt;
                // 平滑收尾曲线（SmoothStep）：起止都无速度突变，不违反限幅精神
                float u = 1f - Mathf.Clamp01(_recenterT / Mathf.Max(0.01f, _recenterDur));
                _yaw = Mathf.LerpAngle(_recenterFrom, _recenterTo, Mathf.SmoothStep(0f, 1f, u));
                _yawFollowVel = 0f;
                _autoYawRate = 0f;
                _lastManualLook = -99f;   // 回正后立刻允许自动跟随接管，不留 1.2s 空窗
                // 冷却从【动作结束】起算，而不是起始：否则 0.85s 的回正会把
                // 3s 间隔吃掉近三分之一，连续两次自动回正会显得过密
                _lastRecenter = Time.unscaledTime;
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
                    _yawErr = aimErr;
                }
            }
            else if (autoFollow && !fpNow && _focusActive && !manualLook &&
                     Mathf.Abs(_focusScreenAng) > focusWindow)
            {
                // ===== 战斗自动聚焦（未锁定）：取景窗 =====
                // 镜头的首要职责是【让你看得见正在打的人】。曾为了不抖把交战中的自动
                // 运镜整个关掉，敌人于是会跑出画面——那是把首要职责一起扔了。
                // 但也不能回到"伺服追朝向/行进方向"：那追的是 C+θ，既抖又不收敛。
                // 正解是追【敌人方位】——它不是镜头偏航的函数，没有代数环，天然收敛；
                // 并且用**取景窗**而不是追到画面中央：窗内完全不动，出窗才推回窗沿。
                //
                // 这条分支只在【敌人真的要出窗】时才接管。上一版写成"只要有聚焦敌人
                // 就走这条"，于是 12m 内有敌人时探索跟随被整条吞掉——玩家跑着突然
                // 改方向，镜头一点反应都没有。**框敌是一条约束，不是一种模式。**
                float outside = Mathf.Abs(_focusScreenAng) - focusWindow;
                // 只推回【窗沿】，不推到中央——推到中央意味着镜头得跟着敌人的
                // 每一步走，那正是"永远在轻微动"。推回窗沿则一到位就停。
                // 屏幕角超出多少，镜头就朝那一侧转多少，不多不少。
                float wantYaw = _yaw + Mathf.Sign(_focusScreenAng) * outside;
                float ft = Mathf.Clamp01(outside / 90f);
                float fSmoothT = Mathf.Lerp(0.45f, 0.16f, ft);
                float fMaxSpd = Mathf.Lerp(autoFollowSpeed * 0.9f, autoFollowSpeed * 5f, ft);
                // 墙壁减速：别为了框住敌人把镜头甩进墙里
                if (outside > 25f)
                {
                    float freeAt = FreeBoomDistance(target.position + Vector3.up * _pivotH,
                        wantYaw, offset.magnitude * _lenFactor);
                    float damp = Mathf.Lerp(0.45f, 1f, Mathf.InverseLerp(1.8f, 3.2f, freeAt));
                    fMaxSpd *= damp;
                    fSmoothT /= Mathf.Max(0.45f, damp);
                }
                // 软区：驱动量在窗沿附近连续趋零，不在边界上硬启停（无限 jerk）
                // 不叠 _occYawBias：那是遮挡换角的绝对偏置，与"把敌人框回画面"
                // 是两套目标，叠上去会互相拉扯（换角把敌人又推出窗外）
                _yaw = Mathf.SmoothDampAngle(_yaw, SoftTarget(_yaw, wantYaw, FocusEdgeSoft),
                    ref _yawFollowVel, fSmoothT, fMaxSpd, dt);
                _yawErr = outside;
            }
            else if (autoFollow)
            {
                bool moving = moveSpeed > 0.6f;
                bool manualRecently = manualLook;
                bool fighting = fightingNow;

                // ===== 交战中是否要静止：看【看不看得见要去的地方】，而不是"在不在战斗" =====
                // 上一版一刀切成 followHold = fighting，于是战斗中连 180° 掉头都不动镜，
                // 读作"镜头反应很慢、延迟很久"。但也不能全放开——小幅走位/出招转向
                // 追朝向正是最初那个抖动源。
                // 判据仍是同一条：前方视察点出了取景窗（≈转向 >55°）才跟，否则静止。
                // 撤离中同样放开：那时该看清要去的方向。
                bool followHold = fighting && !disengaging && !_aheadOut;

                // 战斗中的起步latch：必须先由"看不见前方"点着火（>55° 的大幅转向），
                // 才允许后续按常规的 10° 软区一路跟到位；跟到 <12° 就熄火。
                // 没有这道 latch，战斗中每一次出招磁吸拧动朝向都会重新点火 → 抖。
                if (_aheadOut) _turnFollow = true;
                else if (Mathf.Abs(Mathf.DeltaAngle(_yaw, _headingAvg)) < 12f) _turnFollow = false;
                // 不跟随时把目标设成镜头自身：偏差恒为 0，既不驱动镜头，
                // 也不会让下游的「大幅转向拉远视野」被一个陈旧的方位长期撑开。
                // 分支本身照常走完，末尾的 _yawFollowVel 衰减才不会被跳过
                //（硬置零速度＝无限 jerk，正是本文件反复强调要避免的）
                float followYaw = followHold ? _yaw : _headingAvg;
                float followHoldT = _headingHoldT;
                float softZone = exploreReorientAngle;

                // 「正在推杆」也算主动意图：出招/技能期间角色只转不移动（移动被定步锁住），
                // 若只看 moveSpeed 会误判成静止而完全不回正。
                bool steering = player != null && player.StickWorldDir.sqrMagnitude > 0.04f;
                bool active = (moving || steering) && !followHold;

                // ===== 统一渐进回正（转向尺度 → 镜头同步幅度，连续映射，无分档断层）=====
                // 此前战斗/探索是互斥的两条分支：战斗中只有 >55° 才回正，
                // 12°—55° 的中等转向【完全没有跟随】——战斗中长期存在的部分盲区。
                // 现在合并为一条：
                //   · 中小幅转向（>10°）+ 有移动或推杆 → 温和跟随，越大越快；
                //   · 大幅换向（战斗 >55° / 探索 >45°）→ 迟滞开关锁定追击，直到 <12° 才松开，
                //     保证掉头/转身打背后敌人时迅速把新方向框进画面；
                //   · 手动转镜期间一律让位给玩家。
                float err = Mathf.Abs(Mathf.DeltaAngle(_yaw, followYaw));

                // ===== 意图识别 ①：「朝镜头走来」≠「要把镜头甩到背后」=====
                // err≈180° 意味着角色正朝镜头方向来。此前一律读作"要换方向"，于是玩家
                // 一转身想看正脸、或贴墙后退调整站位，镜头立刻绕到背后——完全违背意图。
                // 大作的判据是【是否持续行进】而非【是否转了身】：
                //   · 只是转身看一眼／短暂后退 → 不满足持续时间 → 镜头岿然不动，看得到正脸；
                //   · 真的一直朝镜头跑（要往那个方向去） → 累积够时间后镜头才缓缓绕过去，
                //     避免角色跑出画面。
                // 威胁优先：正在去的方向上有敌人时，"看清那边"压倒"看正脸"——
                // 否则迎击追兵会落进盲区。判据挂在【行进方向】上而不是朝向：
                // 朝向版（ThreatNear(_headingAvg, 70°)）只要你面朝敌人就成立，
                // 会把 holdNeed 压到 0.06s、bigAngle 压到 35° 而几乎全程锁死；
                // 行进方向版只在"确实在朝那边跑"时成立，原地对峙完全不触发。
                bool threatAhead = travelValid && ThreatNear(_travelAvg, 55f);
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

                float bigAngle = fighting ? 55f : 45f;
                float holdNeed = fighting ? 0.24f : 0.15f;
                // 那个方向有敌人：几乎立刻开始转过去（把"发现追兵"的延迟压到最低）。
                // 现在它要求【真的在朝那边跑】，所以这条快车道只服务于迎击/追击，
                // 不再被原地对拳蹭到。
                if (threatAhead) { holdNeed = 0.06f; bigAngle = 35f; }
                if (!followHold && err > bigAngle && followHoldT > holdNeed && !backingIntent)
                    _combatReorient = true;
                else if (followHold || err < 12f || followHoldT < 0.1f || backingIntent)
                    _combatReorient = false;

                // 稳定确认时长随偏差缩短：小幅修正需要确认（防摇杆抖动牵动镜头），
                // 但 180° 掉头本身就是无歧义的意图，再等 0.15s 纯粹是加盲区
                float steadyNeed = Mathf.Lerp(0.15f, 0.02f, Mathf.Clamp01((err - 45f) / 90f));
                // 战斗中要先由 _turnFollow 点火（>55° 的大幅转向）才允许温和跟随；
                // 探索中沿用原判据（小幅转向也缓缓跟到身后，是已调好的手感）
                bool gentle = active && followHoldT > steadyNeed && err > softZone
                              && !backingIntent && (!fighting || _turnFollow);

                if (!manualRecently && (_combatReorient || gentle))
                {
                    // 0°→180° 连续映射：平滑时间 0.5s→0.15s、转速上限 70→340°/s。
                    // 小幅修正依旧慢而稳（防抖），大角度掉头迅速跟上（防盲区）。
                    float t = Mathf.Clamp01((err - softZone) / 130f);
                    float smoothT = Mathf.Lerp(exploreTurnSmoothTime, 0.15f, t);
                    float maxSpd = Mathf.Lerp(exploreMaxSpeed * 0.82f,
                                              exploreMaxSpeed * 4f, t);
                    // 交战中的走位跟随再压一档慢：即使真在走位，战斗镜头也该像
                    // 斯坦尼康那样缓缓平移，而不是随每一次侧闪就荡过去。
                    // 大幅掉头（迎击背后追兵）不受此限——那正是需要快的时候。
                    if (fighting) maxSpd *= Mathf.Lerp(0.5f, 1f, t);
                    // （曾在此处加过「掉头甩镜」把上限提到 5×。加入角加速度限幅后，
                    //   4× 与 5× 的实测盲区都是 0.97s——限幅才是瓶颈，倍率已无影响。
                    //   一条不再改变行为的特殊分支只会让运镜更难预测，故移除。）

                    // ===== 意图识别 ②：不要把镜头甩进墙里 =====
                    // 玩家面壁转身时，"绕到角色背后"恰好是墙的方向——硬绕过去只会让镜头
                    // 顶在墙上被迫贴脸，视野瞬间塌掉。大幅回正前先探一下目标方位是否够宽敞：
                    // 越憋屈就转得越慢（最低降到 25%），把镜头留在看得见的地方。
                    // 代数环限速：玩家正推着一个非正前方向时，镜头绕行会拖着角色画弧，
                    // 弧的角速度＝绕行速率。压到 SustainedOrbitCap 让它成为缓弧而非转圈。
                    // 摇杆回正/松开后不限速——那时环不存在，镜头可全速追平。
                    if (player != null)
                    {
                        Vector3 sw = player.StickWorldDir;
                        if (sw.sqrMagnitude > 0.04f)
                        {
                            float stickYaw = Quaternion.LookRotation(sw.normalized).eulerAngles.y;
                            float off = Mathf.Abs(Mathf.DeltaAngle(stickYaw, _yaw));
                            float gate = Mathf.Clamp01((off - OrbitGateAngle) / OrbitGateWidth);
                            if (gate > 0f)
                            {
                                // 【突变预算】：把 30°/s 死钉在离轴上，代价是"跑着突然
                                // 改方向，镜头要好几秒才反应过来"。但一味提速也不行——
                                // 环在那里，提速只换来角色画更紧的圈（实测 340°/s 时
                                // 自转 207°/s，偏离角只从 87.9° 缩到 75.2°）。
                                // 折中：方向突变后给一份【有限的角度预算】允许快转，
                                // 花完就落回 30°/s 的缓弧。于是"突然改方向"立刻有反应，
                                // 而"一直推着离轴不放"仍然不会转圈——被诱导的弧
                                // 上限就是这份预算（70°），是个可算清的量。
                                float cap = _orbitBudget > 0f
                                    ? OrbitBurstCap : SustainedOrbitCap;
                                maxSpd = Mathf.Lerp(maxSpd, cap, gate);
                            }
                        }
                    }

                    if (err > 45f)
                    {
                        Vector3 probePivot = target.position + Vector3.up * _pivotH;
                        float freeAtTarget = FreeBoomDistance(probePivot, followYaw,
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
                    float softHeading = SoftTarget(_yaw, followYaw + _occYawBias, softZone);
                    _yaw = Mathf.SmoothDampAngle(_yaw, softHeading, ref _yawFollowVel,
                        smoothT, maxSpd, dt);
                }
                else _yawFollowVel = Mathf.MoveTowards(_yawFollowVel, 0f, 900f * dt);
                _yawErr = err;   // 供下方「大幅转向时轻微拉远视野」使用
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
                // 绕行预算按【实际转过的角度】扣减：只有推着摇杆（环存在）时才扣，
                // 松杆时的收敛不占预算——那时本来就该全速追平
                if (stickHeld) _orbitBudget = Mathf.Max(0f,
                    _orbitBudget - Mathf.Abs(autoRate) * dt);
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
                _offAxisRun = Mathf.MoveTowards(_offAxisRun, 0f, dt / 0.5f);
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
                    _offAxisRun = Mathf.MoveTowards(_offAxisRun,
                        offAxis * Mathf.Clamp01(moveSpeed / 5.2f), dt / 0.5f);
                    // 交战中大幅收敛引导留白（2.2m → 0.5m）：留白是为【长距离奔跑】
                    // 看清前方而设的，而近身缠斗的走位是短促往复——每一次侧闪/后撤
                    // 都会让焦点前后甩动最多 2.2m，那是位置侧最大的一处晃动源，
                    // 换来的"看清前方"在两米开外的对峙里根本用不上。
                    float leadCap = Mathf.Lerp(2.2f, 0.5f, _combatBlend);
                    float lead = Mathf.Clamp01(moveSpeed / 5.2f)
                                 * Mathf.Lerp(0.45f * (1f - 0.5f * _combatBlend),
                                              leadCap, _offAxisRun);
                    focusXZ += vdir.normalized * lead;
                }
                else _offAxisRun = Mathf.MoveTowards(_offAxisRun, 0f, dt / 0.5f);
            }
            else _offAxisRun = Mathf.MoveTowards(_offAxisRun, 0f, dt / 0.5f);
            if (!_pivotInit) { _pivotY = targetPivotY; _pivotXZ = focusXZ; _focusAnchor = focusXZ; _pivotInit = true; }

            // 电影三脚架感·焦点死区：小于死区的焦点位移完全不推镜——近身互殴时
            // 拳脚带来的细碎换位（突进/击退/侧闪的残余）不再传导成镜头晃动；
            // 只有真正的走位才移镜。战斗死区大（稳如三脚架），探索死区小（跟手）。
            // 判据用【交战程度】而不是【是否锁定】：锁定默认是关的（手动锁），
            // 于是绝大多数近身互殴一直吃着"探索档"的跟手参数——
            // 这是"打起来镜头抖"里属于【位置】的那一半（另一半是偏航，见上方跟随信号）。
            float steady = Mathf.Max(_lockBlend, _combatBlend);
            float dead = Mathf.Lerp(0.03f, 0.15f, steady);
            Vector2 drift = focusXZ - _focusAnchor;
            if (drift.magnitude > dead) _focusAnchor = focusXZ - drift.normalized * dead;

            _pivotY = Mathf.SmoothDamp(_pivotY, targetPivotY, ref _pivotYVel, 0.13f,
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
                // 转向开阔（0.06→0.15）：转向瞬间恰是最需要看清周围的时刻。
                // 用【拉远吊杆】而不是【放大 FOV】——变焦会显著加剧晕动，
                // 而纯位移只是把画面推开，是这几个杠杆里唯一还有余量且无副作用的。
                // 转完即回（_lenFactor 的慢插值负责），角色占屏只在转向瞬间由 40%→35%。
                float turnOut = Mathf.Clamp01(_yawErr / 120f) * 0.15f;
                // 离轴奔跑再拉远 12%：与引导留白叠加后，横跑的可见前方由 5.5m 增至 8.1m
                wantFactor = 1f + runOut + turnOut + _offAxisRun * 0.12f;
            }
            // 大招镜头：短暂拉近（覆盖当前构图，结束自动回稳）
            // 推近幅度随特写强度分级：击杀轻推、超必杀满推——不再一律贴到 0.82
            float zoomTo = Mathf.Lerp(1f, ultimateZoom, _closeStrength);
            wantFactor = Mathf.Lerp(wantFactor, zoomTo, _ultimateBlend);
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
                // 下限 1.6→1.9m：1.6m 处角色占屏高达 113%（整个画面被身体填满），
                // 是贴墙时"突然极度压抑"的来源。1.9m 仍不穿模，画面留得住东西。
                wantDist = Mathf.Min(wantDist, Mathf.Max(1.9f, hit.distance - 0.1f));
            }
            // 回缩仍然快（避免穿墙），但【伸出恢复】明显加快（0.3→0.14s）：
            // 转身扫过障碍后视野立刻回到正常景别，不再长时间贴脸发窄
            // 遮挡持续计时：供上方的换角决策使用（0.6m 以上的回缩才算真被挡）
            _occludedT = wantDist < maxDist - 0.6f ? _occludedT + dt : 0f;

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
