using UnityEngine;
using AdversityRoad.Mobile;

namespace AdversityRoad.Player
{
    /// <summary>
    /// 镜头每帧读出的【玩家意图】。这是"何时该动、何时绝不动"的唯一分派点——
    /// 一套规则套所有情景必然顾此失彼，成熟动作游戏（魂系/悟空/战神/对马）
    /// 的骨架都是先分情景、再给每个情景配一套行为。
    ///
    /// 分情景的关键在于：**同样的几何可以对应完全相反的意图**。
    /// 「玩家转身面对镜头站着」与「玩家转身朝镜头方向跑」——镜头相对角色的位置
    /// 一模一样，但前者绝不能动（那是玩家特意摆的机位），后者必须动
    /// （否则前方成为盲区，跟开车看不见路一样）。
    /// 区分它们的不是角度，是**有没有行进意图**：
    /// **站着不动的时候，「前方」这个概念根本不存在。**
    /// </summary>
    public enum CamIntent
    {
        /// <summary>演出中：大招/处决特写、一键回正进行中。镜头由那段演出全权接管。</summary>
        Cinematic,
        /// <summary>玩家刚手动拨过镜头：这是他要的构图，让位。</summary>
        ManualFraming,
        /// <summary>锁定交战：镜头对齐敌我方位（玩家按「锁」表达的显式意图）。</summary>
        LockOn,
        /// <summary>近身交战（未锁定）：镜头不跟方向，只负责别让敌人跑出画面。</summary>
        Melee,
        /// <summary>站定观察：没有任何行进意图。**镜头绝不自作主张**——
        /// 玩家特意让镜头面对角色的脸站着，就该一直是那样。</summary>
        Idle,
        /// <summary>行进中，前方看得见：拍得到就不动。</summary>
        Cruising,
        /// <summary>行进中，前方看不见：**唯一"必须回正"的常态情景**。
        /// 不转过去就等于开车看不见路，前方成为盲区。</summary>
        Blind,
    }

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
    /// **镜头与方向的分工（对齐黑神话悟空）**
    ///   · **近身交战**（聚焦敌人 8m 内）→ **镜头不跟方向**。战斗镜头归玩家（转镜区）
    ///     与锁定管；"看得见敌人"由取景窗保底，"看得正"由松杆自动回正负责。
    ///     战斗中角色朝向被出招磁吸每一击瞬间拧向敌人、推杆出招转向 150°/s 搅动，
    ///     跟它就是抖——所以干脆不跟。
    ///   · **其他一切情况**（无敌人／敌人在 8m 外／正在撤离）→ **实时同步**：
    ///     走连续伺服（10° 软区、70~340°/s）+ 方向突变预算，一转就跟。
    ///   （8m 进 / 9.5m 出，带迟滞——在阈值上反复开合＝跟随时有时无，比两者都糟。）
    ///
    /// **未锁定战斗的偏航：追【敌人】，用取景窗**
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
    ///     代价不可接受。现保住"摇杆↔画面一致"，把绕行速率压到分档恒值
    ///     （慢漂 14°/s＝弧半径 21m；看不见前方时封顶 90°/s＝3.3m）——
    ///     缺陷从来不是"会画弧"，而是弧太紧。
    ///  ③ 由 ① 还能推出一条更要紧的：**偏差 ≡ θ**，所以比例伺服的转速 = 增益 × θ，
    ///     **θ 的抖动会原样变成角速度的抖动**。虚拟摇杆上拇指本就在 ±10~20° 游走，
    ///     跑起来便是持续的角速度噪声——而人感受到的"晃"正是角速度与角加速度，
    ///     不是位置。加大平滑、加限幅都治不了根（只要速率由误差决定，噪声就进得来）。
    ///     故行进跟随改成**分档恒速转台**：速率与误差解耦，拇指再抖也调制不了它。
    ///
    /// **运镜先分情景，再配行为**（见 <see cref="CamIntent"/>）。一套规则套所有情景
    /// 必然顾此失彼——这套镜头前后返工多轮，根因大多是"拿一条判据去管所有场合"。
    ///   | 情景 | 判定 | 镜头 |
    ///   | 演出 Cinematic | 回正/大招进行中 | 由那段演出接管 |
    ///   | 取景 ManualFraming | 玩家刚拨过镜头 | 让位，不动 |
    ///   | 锁定 LockOn | 有锁定目标 | 敌我方位伺服 |
    ///   | 近身 Melee | 聚焦敌人 8m 内 | 不跟方向，只保证敌人在画面里 |
    ///   | **站定 Idle** | 无行进意图 0.25s | **不动，且绝不回正** |
    ///   | 巡航 Cruising | 在跑，前方看得见 | 不动 |
    ///   | **盲区 Blind** | 在跑，前方看不见 | **必须转过去**（≤90°/s） |
    /// 其中 Idle 与 Blind 这一对是整套设计的枢纽：**它们的几何一模一样**
    /// （镜头都不在角色背后），意图却完全相反。区分它们的不是角度，是有没有在往某处去。
    ///
    /// **电影级的第一原则：拍得到，就不动。**
    /// 机位调整是有代价的动作，只该为【画面里缺了必须看到的东西】而付。
    /// 于是所有会改变画面的杠杆都统一挂到同一个判据上——「要看的东西在不在取景窗内」：
    ///   · 偏航跟随：窗内 → 一动不动（连"缓缓转到身后"的慢漂也取消了：那是画面
    ///     本来就合格时仍在运镜，是"不平稳"的底噪，也让跑动持续画弧）；
    ///   · 吊杆拉远、引导留白：窗内不给，出窗才给，且是两档不是连续量——
    ///     它们原本随偏差连续变，而偏差恒等于拇指角 θ，
    ///     等于把拇指的抖动直接接到了【纵深】与【焦点位移】上；
    ///   · 景别切换：群战/狭窄判定都加迟滞，且换景别有 3s 最短驻留——
    ///     换景别是分镜级的决定（距离/取景点/俯仰/FOV 一起变），不该每秒发生一次。
    /// 剩下的运镜就只有两类：**玩家自己转的**，和**一次有始有终的回正**。
    ///
    /// 但"该不该动"判对了还不够，**"怎么开始、怎么停下"同样要管**。
    /// 门限式的判据天然给出阶跃的目标速率（门一开就从 0 变成六十几度每秒，
    /// 门一关又变回 0），直接拿去转镜就是"静止→猛地开始转→猛地停住"，
    /// 读作抑扬顿挫、像卡死后突然恢复，比一直缓慢转还晕。
    /// 所以**速率本身也走临界阻尼**：目标速率仍是分档恒定的（拇指抖动调制不了它），
    /// 而实际速率平滑地涨上去、平滑地落下来——无速率噪声，且无速度台阶。
    /// 峰值角加速度由 1100°/s²（限幅值，实际等于没限）降到 114~169°/s²。
    /// 同理，引导留白与吊杆变焦也从 MoveTowards/Lerp 改为临界阻尼——
    /// 前者是线性斜坡（两端各有一次加速度台阶），后者面对阶跃目标会在第一帧
    /// 就给出最大推拉速度。
    ///
    /// 而且这一切还要先过**意图确认**这一关：只有【已确认的方向】才配让机位动。
    /// 判据量的是**拇指角 θ**（锚点法）——它是玩家的真实意图，且与镜头无关：
    ///   · 搓杆/转圈 → θ 一直在扫 → 永不确认 → **镜头一动不动**，角色自己绕圈跑
    ///     （这才是大作的表现；否则摇杆转一圈屏幕就跟着转一圈）；
    ///   · 按住某个方向不放 → θ 恒定 → 立即确认 → 镜头平滑连续地转。
    /// 注意不能拿【世界方向】做这个判据：镜头一转，世界方向就被环带着转，
    /// 锚点被镜头自己不断打断，结果是"转 0.3s→判未确认→停→又确认→再转"的
    /// 脉冲式起停，比一直转还难受。
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

        [Header("行进跟随（分档恒速转台）：速率不由误差决定，拇指的抖动便调制不了它。" +
                "这是「跑步时镜头晃」的根治点——见类注释推论③")]
        [Tooltip("慢漂死区（度）：镜头与角色朝向的偏差小于它完全不动镜")]
        public float settleDeadZone = 9f;
        [Tooltip("看不见前方（前方视察点出取景窗）时的速率上限（度/秒）。" +
                 "环还在，转多快都不收敛，快只换更紧的弧，封顶即可——90°/s 对应弧半径 3.3m")]
        public float blindMaxRate = 90f;

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
        float _followRate, _followRateVel;   // 行进跟随的【实际】角速度（对目标速率做临界阻尼）
        /// <summary>速率的平滑时间。目标速率是分档恒定的（拇指抖动调制不了它），
        /// 而实际速率经此平滑地涨上去、落下来——无速率噪声，且无速度台阶。
        /// 0.32s 下 61°/s 的目标对应峰值角加速度 ≈190°/s²，远在舒适区（≈600）内。</summary>
        const float FollowRateSmooth = 0.32f;
        float _boomDist, _boomVel;
        float _boomWant = 99f, _boomClearT;   // 吊杆目标的抗颤保持（见碰撞回缩处）
        /// <summary>吊杆"空出来了"要持续这么久才承认，用来滤掉球投射逐帧漏检造成的
        /// 锯齿式伸缩。变近不受此限——不能拿穿墙冒险。</summary>
        const float BoomClearHold = 0.18f;
        float _kick;
        float _yawFollowVel;               // 回正弹簧速度（SmoothDampAngle 用）
        float _ultimateTimer, _ultimateBlend;   // 大招镜头计时与渐入渐出
        float _lastManualLook;
        Vector3 _lastTargetPos;
        float _pivotY, _pivotYVel;         // 纵向软化：跳跃落地不硬拽镜头
        float _pivotYAnchor;               // 纵向死区锚（滤台阶/斜坡的逐帧高度跳动）
        /// <summary>纵向死区（米）：小于这个高度变化完全不推镜。
        /// 胶囊贴地与台阶造成的逐帧跳动都在几厘米量级，真实的落地/跳跃远超它。</summary>
        const float PivotYDeadZone = 0.06f;
        Vector2 _pivotXZ, _pivotXZVel;     // 水平软跟随：消除刚性同步放大的逐帧抖动
        Vector2 _focusAnchor;              // 焦点死区锚：小位移不推镜（电影三脚架感）
        Vector3 _planarVel;                // 玩家水平速度（移动构图的引导留白用）
        float _speedAvg;                   // 低通后的水平速率：一切取景决策都读它，
                                           // 不读逐帧原始值（顿帧会让原始值每拳塌一次）
        Combat.CombatStateMachine _playerFsm;   // 临战判定（未锁定的战斗回正用）
        bool _combatReorient;              // 大幅换向追击中（迟滞开关防小幅摆镜）
        /// <summary>离轴奔跑强度（0~1，平滑）：横向/后向跑时拉远取景，进一步扩大可见前方。</summary>
        float _offAxisRun, _offAxisVel;
        float _towardCamT;                 // 持续朝镜头行进的时长（区分"转身看一眼"与"真要往那走"）
        // ---- 镜头导演：景别选择与平滑过渡 ----
        ShotProfile _shot;                 // 当前生效的景别（插值后的实时值）
        float _nextShotScan;               // 敌情扫描节流
        int _nearbyEnemies;
        float _roomAround = 99f;           // 吊杆方位的可用空间（识别狭窄场地）
        bool _tightLatch;                  // 「狭窄」景别的迟滞锁存（2.6m 进 / 3.4m 出）
        bool _crowdLatch;                  // 「群战」景别的迟滞锁存（≥3 进 / ≤1 退）
        ShotProfile _shotTarget;           // 已【承诺】的景别（_shot 朝它推轨）
        float _shotSwitchT = -99f;         // 上次换景别的时刻（最短驻留）
        /// <summary>换景别的最短间隔。换景别＝一次实打实的推轨（距离/取景点/俯仰/FOV
        /// 一起变），是分镜级的决定，不该每秒发生一次。大招特写豁免。</summary>
        const float ShotDwell = 3f;
        bool _shotInit;
        /// <summary>朝镜头持续行进多久才认定"真要往那个方向去"，才允许绕镜。
        /// 低于此值一律视为玩家在看正脸/调整站位，镜头保持不动。
        /// 0.9→0.6：有了威胁感知兜底，这里不必再压那么久。</summary>
        const float TowardCameraHold = 0.6f;

        // （曾在此处用 SustainedOrbitCap/OrbitGate 给"推着离轴方向时的绕行"限速。
        //   现在行进跟随只在【看不见前方】时才动，速率也是分档恒定的（见 blindMaxRate），
        //   速率不再由误差决定，也就不需要再在外面套一层限速门了。）

        // ===== 行进跟随的三档【恒定】速率 =====
        // 关键不在数值大小，而在**速率不由误差决定**：误差恒等于摇杆离轴角 θ，
        // 让速率正比于它，θ 的抖动就会原封不动变成角速度的抖动——那正是"晃"。
        // 分档恒定后，拇指怎么小幅游走都调制不了镜头的转速。
        /// <summary>摇杆松开时的收敛速率上限。此时代数环消失、误差真的会收敛，
        /// 可以放开跑（实测松杆后 0.68~0.70s 对准新行进方向）。
        /// 另一档（盲区封顶 blindMaxRate）在上方 Inspector 里。</summary>
        const float FreeConvergeMaxRate = 300f;

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
        float _focusFrameT;                // 聚焦敌人已出取景窗多久
        /// <summary>敌人出取景窗要持续这么久才动机位，滤掉绕圈跑时自身位移
        /// 造成的一帧越界。彻底出画另有 FocusLostForce 那条无条件兜底。</summary>
        const float FocusFrameHold = 0.12f;
        bool _focusActive;                 // 本帧是否该框住聚焦敌人（撤离中为假）
        float _focusScreenAng;             // 聚焦敌人的有符号屏幕水平角
        float _focusDist = 99f;            // 与聚焦敌人的距离
        bool _aheadOut;                    // 【方向已确认】且视察点出取景窗＝看不见要去的地方
        CamIntent _intent = CamIntent.Idle;   // 本帧读出的玩家意图（运镜的唯一分派点）
        float _idleT;                      // 无行进意图已持续多久
        /// <summary>没有任何行进意图持续这么久就进入「站定观察」——此后镜头绝不
        /// 自作主张。0.25s 足以滤掉碎步/落地的间隙，又不至于让站定的判定发木。</summary>
        const float IdleHold = 0.25f;
        float _dirAnchor, _dirSettleT;     // 方向确认检测（锚点法，量拇指角）
        bool _thumbInit;
        /// <summary>拇指角偏离锚点超过这个角度就重新计时。转摇杆一圈时拇指一直在扫，
        /// 计时永远起不来 ⇒ 镜头一动不动（大作的表现），角色自己绕圈跑。</summary>
        const float DirSettleBand = 20f;
        /// <summary>方向要稳住这么久才算"确认要去那儿"。转向途中的扫掠不算数，
        /// 转完停住才算——0.3s 既滤得掉搓杆，又不至于让真实转向读作迟钝。</summary>
        const float DirCommitTime = 0.3f;
        /// <summary>聚焦敌人近到这个距离以内就算「近身交战」——此时镜头不再跟方向
        /// （悟空式：战斗镜头归玩家与锁定管）。8m 大于所有近战招式的距离，
        /// 又明显小于聚焦目标的取用范围 12m，于是"正在打"与"在接近/在脱离"分得开。
        /// 退出用 9.5m（迟滞）：在阈值上反复开合＝跟随时有时无，比两者都糟。</summary>
        const float CombatFollowHoldRange = 8f;
        const float CombatFollowReleaseRange = 9.5f;
        bool _meleeHold;                   // 「近身交战·镜头不跟方向」的迟滞锁存
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
        float _lenFactor = 1f; float _lenFactorVel;             // 动态构图：战斗拉近/疾跑拉远
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

        /// <summary>本帧读出的玩家意图（调试/回归用：直接告诉你镜头为什么动或不动）。</summary>
        public CamIntent IntentNow => _intent;

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
            // ===== 镜头必须和世界同一把时钟（本轮的关键修复）=====
            // 原先整套用 Time.unscaledDeltaTime。而每一次命中都会触发顿帧
            //（CombatFeedback.HitStop：timeScale=0.08，0.035~0.12s），连打时每秒好几次。
            // 于是顿帧期间【世界冻住、镜头照常全速跑】：
            //   · 位置/偏航平滑在这 0.1 秒里照样收敛，等于每挨一拳镜头就抢跑一段，
            //     世界恢复后再被拽回来——这就是与出招同频的"画面频繁抖动"；
            //   · 所有由位移推出来的量（速度、行进方向、留白）也一起失真。
            // 顿帧的本意是【整帧定住】以强调打击，镜头当然也该定住。改用缩放时间后
            // 三件事一起对齐：镜头随世界一起顿、速度估计自洽、暂停面板时镜头不再漂。
            float dt = Time.deltaTime;
            // 暂停（timeScale=0）时必须把累积的转镜增量丢掉再退出：
            // 否则面板开着的这几秒里 LookDelta 一直在累加，恢复的瞬间会被一次性
            // 灌进 _yaw，镜头猛甩一下。
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
            // ===== 速度必须用【和角色位移同一把尺子】量：世界时间，不是无缩放时间 =====
            //
            // 这是"出招时画面频繁抖动"的主因，而且藏得很深：
            // 每一次命中都会触发顿帧（CombatFeedback.HitStop：timeScale=0.08，
            // 持续 0.035~0.12s），连打时每秒好几次。角色在 Update 里按【缩放】时间位移，
            // 而镜头整套跑在【无缩放】时间上，于是
            //     moveSpeed = 位移(8%) / dt(100%) ≈ 真实速度的 8%
            // 每次命中都瞬间塌一次。后果全都挂在这个数上，且全部同步于你的每一拳：
            //   · 引导留白 lead ∝ Clamp01(moveSpeed/5.2)（这一项没有任何平滑！）
            //     → 焦点在一帧内前后跳最多 0.45m，命中一次跳一次；
            //   · moveSpeed>1.5 的留白闸、>1.2 的俯仰回中闸反复开合；
            //   · 疾跑拉远 runOut 反复缩放。
            // 用世界时间量就自洽了：顿帧时位移 8%、dt 也 8%，商不变。
            // （上面已把 dt 统一成缩放时间，这里天然成立；再加一道低通做保险——
            //   引导留白的 Clamp01(moveSpeed/5.2) 这一项原本没有任何平滑，
            //   任何单帧速度突变都会被它一比一地放大成焦点位移。）
            _planarVel = Vector3.Lerp(_planarVel, frameDelta / dt, 5f * dt);
            _speedAvg = Mathf.Lerp(_speedAvg, frameDelta.magnitude / dt, 8f * dt);
            float moveSpeed = _speedAvg;

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
            // 群战判定也要迟滞（进 ≥3 / 退 ≤1）：敌人在 9m 边界上进进出出会让
            // 「对峙」与「群战」反复互换，而两者相差 距离×1.12、取景点 +0.28m、
            // 俯仰 +6°、FOV +4——那是一次实打实的推轨，反复发生就是画面持续起伏。
            if (_nearbyEnemies >= 3) _crowdLatch = true;
            else if (_nearbyEnemies <= 1) _crowdLatch = false;
            var wantShot = CameraDirector.Pick(
                _ultimateTimer > 0f, _crowdLatch ? 3 : Mathf.Min(_nearbyEnemies, 1),
                lockOn != null && lockOn.CurrentTarget != null,
                _tightLatch ? 0f : 99f);
            if (!_shotInit) { _shot = _shotTarget = wantShot; _shotInit = true; }
            else
            {
                // 最短驻留：换景别是分镜级的决定，不该每秒发生一次。
                // 大招/处决的推近特写是刻意的戏剧节拍，豁免。
                bool urgent = _ultimateTimer > 0f;
                if (wantShot.name != _shotTarget.name &&
                    (urgent || Time.unscaledTime - _shotSwitchT > ShotDwell))
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
            if (_playerFsm == null && player != null)
                _playerFsm = player.GetComponent<Combat.CombatStateMachine>();
            bool fightingNow = lockTarget != null || _engagedEnemies > 0 ||
                               (_playerFsm != null && _playerFsm.InCombat);
            _combatBlend = Mathf.MoveTowards(_combatBlend, fightingNow ? 1f : 0f, dt / 0.6f);

            // 摇杆松开时长：自动回正只在【松杆】时触发（推着杆时回正无解，
            // 且会在参考系解冻的瞬间把玩家正在跑的方向掰弯——见 AutoRecenter 注释）
            bool stickHeld = player != null && player.StickWorldDir.sqrMagnitude > 0.04f;
            _stickIdleT = stickHeld ? 0f : _stickIdleT + dt;

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
            _focusDist = 99f;
            if (_focusActive)
            {
                if (!_focusInit) { _focusPos = _focusEnemy.position; _focusInit = true; }
                _focusPos = Vector3.Lerp(_focusPos, _focusEnemy.position,
                    1f - Mathf.Exp(-AimLowPass * dt));
                _focusScreenAng = ScreenAngleTo(_focusPos);
                Vector3 flat = _focusEnemy.position - target.position; flat.y = 0f;
                _focusDist = flat.magnitude;
                if (Mathf.Abs(_focusScreenAng) > halfHFov * 0.95f) _focusOutT += dt;
                else _focusOutT = 0f;
                // 出窗需持续一小会儿才动机位：玩家绕圈跑时，是【自己的位移】把敌人
                // 在画面里推来推去，一帧的越界不值得动镜
                if (Mathf.Abs(_focusScreenAng) > focusWindow) _focusFrameT += dt;
                else _focusFrameT = 0f;
            }
            else { _focusOutT = 0f; _focusFrameT = 0f; }
            // 近身交战锁存（8m 进 / 9.5m 出）：进则镜头不跟方向，出则恢复实时同步
            if (!_focusActive || _focusDist > CombatFollowReleaseRange) _meleeHold = false;
            else if (_focusDist < CombatFollowHoldRange) _meleeHold = true;

            // ===== 有没有【行进意图】——这是"该不该回正"的真正分界 =====
            // 用户举的两个例子几何完全一样（镜头都不在角色背后），意图却相反：
            //   · 转身面对镜头【站着】→ 那是玩家特意摆的机位，镜头绝不能动；
            //   · 转身朝那个方向【跑】→ 不转过去就是开车看不见路，必须动。
            // 区分它们的不是角度，是有没有在往某处去。
            // **站着不动的时候，「前方」这个概念根本不存在**——而此前的代码在站定时
            // 仍用角色朝向虚构了一个"前方视察点"，于是"面对镜头站着"被读成
            // "前方 180° 全是盲区"，自动回正随即把镜头绕走。这就是那个 bug 的全貌。
            bool locomotion = moveSpeed > 0.6f || stickHeld;
            _idleT = locomotion ? 0f : _idleT + dt;
            bool idleNow = _idleT > IdleHold;

            // ② 前方视察点的屏幕角：看得见"要去的地方"吗。
            //    只在【有行进意图】时才成立；站定时不存在"前方"，一律视为看得见。
            float aheadYaw = travelValid ? _travelAvg : _headingAvg;
            float aheadAng = idleNow ? 0f : Mathf.Abs(ScreenAngleTo(
                target.position + Quaternion.Euler(0f, aheadYaw, 0f) *
                Vector3.forward * LookAheadDist));

            // ===== 方向必须【已确认】才算数（锚点法，量的是拇指角）=====
            // 这是"摇杆转一圈，屏幕跟着一直转"的正解。转圈时方位每时每刻都在变，
            // 它从来不是一个"要去的方向"，而是一串没定下来的意图——大作对此的
            // 处理是镜头压根不理它，角色自己绕圈跑。
            // 判据不能用低通后的方位：低通只加相位滞后，去不掉【持续的旋转速率】，
            // 镜头照样会跟着转一圈。
            //
            // 更要命的是**也不能量世界方向**：镜头一转，世界方向就被环带着转
            //（行进方向 = 镜头偏航 + 拇指角），锚点于是被镜头自己不断打断，
            // 结果是"转 0.3s → 判定未确认 → 停 → 又确认 → 再转"的脉冲式起停，
            // 比一直转还难受。
            // 必须量【拇指角 θ】：它是玩家的真实意图，且**与镜头无关**——
            // 镜头怎么转，按住不动的拇指其 θ 恒定。于是：
            //   · 搓杆/转圈 → θ 一直在扫 → 永不确认 → 镜头一动不动；
            //   · 按住某个方向不放 → θ 恒定 → 立即确认 → 镜头平滑连续地转，不会起停。
            if (stickHeld)
            {
                float thumb = Mathf.DeltaAngle(_yaw,
                    Quaternion.LookRotation(player.StickWorldDir.normalized).eulerAngles.y);
                if (!_thumbInit) { _thumbInit = true; _dirAnchor = thumb; _dirSettleT = 0f; }
                else if (Mathf.Abs(Mathf.DeltaAngle(_dirAnchor, thumb)) > DirSettleBand)
                {
                    _dirAnchor = thumb;
                    _dirSettleT = 0f;
                }
                else _dirSettleT += dt;
            }
            else
            {
                // 松杆：环消失，方向不会再被镜头带跑，直接视为已确认
                _thumbInit = false;
                _dirSettleT += dt;
            }
            bool dirCommitted = _dirSettleT > DirCommitTime;

            // 迟滞：出窗才算"看不见"，回到 0.7 倍窗宽才算"看见了"——
            // 免得在窗沿上反复开合。再叠加"方向已确认"：只有【确认了要去哪、
            // 而且那个方向看不见】才值得动机位。
            if (aheadAng > focusWindow && dirCommitted) _aheadOut = true;
            else if (aheadAng < focusWindow * 0.7f || !dirCommitted) _aheadOut = false;

            // ===== 情景判定：每帧读出一个玩家意图，后面所有运镜都按它分派 =====
            // 顺序＝优先级，从最特殊到最一般。一套规则套所有情景必然顾此失彼，
            // 成熟动作游戏的骨架都是先分情景再配行为（见 CamIntent 的说明）。
            if (_recenterT > 0f || _ultimateTimer > 0f) _intent = CamIntent.Cinematic;
            else if (manualLook) _intent = CamIntent.ManualFraming;
            else if (lockTarget != null) _intent = CamIntent.LockOn;
            else if (_meleeHold) _intent = CamIntent.Melee;
            else if (idleNow) _intent = CamIntent.Idle;
            else if (_aheadOut) _intent = CamIntent.Blind;
            else _intent = CamIntent.Cruising;

            // ---- 「想不想回正」：要面对的东西出窗了 ----
            // **站定观察时永不回正**——这是本轮的核心：玩家特意让镜头面对角色的脸
            // 站着，就该一直是那样；那时也根本没有"前方"需要看清。
            // 反过来，一旦开始朝某个方向跑而前方看不见（Blind），回正就是必须的，
            // 否则等于开车看不见路。
            float wantAng = _focusActive ? Mathf.Abs(_focusScreenAng) : aheadAng;
            bool mayRecenter = _intent != CamIntent.Idle && _intent != CamIntent.ManualFraming;
            if (mayRecenter && wantAng > focusWindow * 0.8f && (_focusActive || dirCommitted))
                _wantRecenterT += dt;
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

            // 其余分支接管偏航时，让行进跟随的速率平滑归零——
            // 切回来时才不会带着一段旧速度突然起步
            if (_recenterT > 0f)
            {
                // 回正接管本帧的偏航，跳过常规自动运镜
                _followRate = Mathf.SmoothDamp(_followRate, 0f, ref _followRateVel,
                    FollowRateSmooth, Mathf.Infinity, dt);
            }
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
                _followRate = Mathf.SmoothDamp(_followRate, 0f, ref _followRateVel,
                    FollowRateSmooth, Mathf.Infinity, dt);
            }
            else if (autoFollow && !fpNow && _focusActive && !manualLook &&
                     _focusFrameT > FocusFrameHold)
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
                _followRate = Mathf.SmoothDamp(_followRate, 0f, ref _followRateVel,
                    FollowRateSmooth, Mathf.Infinity, dt);
            }
            else if (autoFollow)
            {
                // ===== 行进跟随：改成【恒速转台】，不再是比例伺服 =====
                //
                // 为什么必须改：伺服的转速 = 增益 × 偏差，而这里的偏差**恒等于摇杆离轴角 θ**
                // （移动是镜头相对的 ⇒ 角色朝向 H = 镜头偏航 C + θ ⇒ H − C ≡ θ）。
                // 于是 **θ 一抖，镜头的角速度就跟着抖**。而人感受到的"晃"正是角速度与
                // 角加速度，不是位置——虚拟摇杆上拇指本来就在 ±10~20° 内游走，
                // 跑起来就成了持续的角速度噪声，这就是"跑步时镜头快速晃动的厉害"。
                // 加大平滑/限幅都治不了根：只要速率由误差决定，误差的噪声就会进到速率里。
                //
                // 改法：**速率与误差解耦，只分几档恒定值**。拇指的抖动便无从调制它。
                //   · 看得见前方（视察点在取景窗内）→ **完全不动**（电影级第一原则）。
                //   · 看不见前方（视察点出窗，≈转向 >55°）→ 按超出量映射但封顶 blindMaxRate。
                //     环还在，转多快都不收敛，快只换来更紧的弧，所以封顶就够。
                //   · 摇杆松开 → 环消失，误差真的会收敛，此时才用比例伺服全速追平。
                //   · 近身交战 / 手动转镜期 / 静止 → 完全不动。
                bool moving = moveSpeed > 0.6f;
                bool fighting = fightingNow;
                bool stickFree = _stickIdleT > 0.05f;
                // 方向跟随【只服务于 Blind 这一个情景】：在跑、而且前方看不见。
                // 站定观察(Idle)、看得见前方(Cruising)、近身交战(Melee)、
                // 玩家正在取景(ManualFraming)一律不动——这才是"分情景"而不是"一套规则"。
                bool active = _intent == CamIntent.Blind;

                float wantYaw = _headingAvg + _occYawBias;
                float err = Mathf.Abs(Mathf.DeltaAngle(_yaw, wantYaw));

                // 「朝镜头走来」≠「要把镜头甩到背后」：转身看正脸／短暂后退时不该绕镜。
                // 判据是【是否持续全速行进】而不是【是否转了身】；那个方向上有威胁时
                // 一律放行（迎击追兵不能落进盲区）。
                bool threatAhead = travelValid && ThreatNear(_travelAvg, 55f);
                bool towardCamera = err > 130f;
                if (towardCamera && moving && !fighting) _towardCamT += dt;
                else _towardCamT = 0f;
                float runRef = player != null ? player.runSpeed : 5.2f;
                bool committedRun = moveSpeed > runRef * 0.55f;
                bool backingIntent = towardCamera && !fighting && !threatAhead
                                     && !committedRun && _towardCamT < TowardCameraHold;

                // ===== 电影级的第一原则：拍得到，就不动 =====
                // 机位调整是有代价的动作，只该为【画面里缺了必须看到的东西】而付。
                // 视察点还在取景窗内 ⇒ 你已经看得见要去的地方 ⇒ **镜头一动不动**。
                // 此前这里还留了一档 14°/s 的"恒速慢漂"，想把镜头一点点带到身后；
                // 但那是在画面本来就合格的时候仍然持续运镜——它既让跑动持续画弧，
                // 也让画面永远在缓慢移动，正是"不平稳"的底噪。构图偏一点不是缺陷，
                // 是引导留白；真要摆正，松杆时的自动回正会一次做完。
                float rate = 0f;
                if (active && !backingIntent && err > settleDeadZone)
                {
                    if (stickFree)
                    {
                        // 松杆：无环，比例伺服可以全速收敛（实测 0.68~0.70s 对准新方向）
                        rate = Mathf.Min(FreeConvergeMaxRate, err * 3.2f);
                    }
                    else
                    {
                        // 推着杆且看不见要去的方向：必须转过去，但封顶
                        rate = Mathf.Min(blindMaxRate, (err - settleDeadZone) * 1.2f);
                        if (threatAhead) rate = Mathf.Min(blindMaxRate * 1.6f, rate * 1.6f);
                    }
                    // 墙壁减速：别把镜头甩进墙里（幅度越大越不减速——盲区比构图要命）
                    if (err > 45f)
                    {
                        float freeAtTarget = FreeBoomDistance(
                            target.position + Vector3.up * _pivotH, wantYaw,
                            offset.magnitude * _lenFactor);
                        float room = Mathf.InverseLerp(1.8f, 3.2f, freeAtTarget);
                        float urgency = Mathf.Clamp01((err - 45f) / 90f);
                        rate *= Mathf.Lerp(Mathf.Lerp(0.6f, 1f, room), 1f, urgency);
                    }
                }
                // ===== 速率本身必须临界阻尼，不能阶跃 =====
                // 上面算出的 rate 是【目标速率】，它天然是阶跃的：门一开就从 0 变成
                // 六十几度每秒，门一关又变回 0。直接拿它转镜就是"静止→猛地开始转→
                // 猛地停住"——读作抑扬顿挫、像卡死后突然恢复，比一直缓慢转还晕。
                // 原先只靠下游的角加速度限幅（1100°/s²）兜底，而 61°/s 在 0.055s 内
                // 就能到位，等于没限；何况它"只限提速不限减速"，停的那一下是无限 jerk。
                //
                // 这里对速率做临界阻尼平滑：目标速率仍是分档恒定的（拇指抖动
                // 调制不了它），而**实际速率**平滑地涨上去、平滑地落下来。
                // 两个性质同时拿到——无速率噪声，且无速度台阶。
                // 0.32s：61°/s 的目标对应峰值角加速度 ≈190°/s²，远在舒适区（≈600）内。
                _followRate = Mathf.SmoothDamp(_followRate, rate, ref _followRateVel,
                    FollowRateSmooth, Mathf.Infinity, dt);
                _yaw = Mathf.MoveTowardsAngle(_yaw, wantYaw, _followRate * dt);
                _yawFollowVel = Mathf.MoveTowards(_yawFollowVel, 0f, 900f * dt);
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
                _offAxisRun = Mathf.SmoothDamp(_offAxisRun, 0f, ref _offAxisVel, 0.45f, Mathf.Infinity, dt);
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
                    // 额外留白【只在看不见前方时给】，而且是两档不是连续量。
                    // 原写法按离轴角连续取值，而离轴角恒等于拇指角 θ ⇒
                    // **拇指的抖动被直接接到了焦点位移上**，画面持续左右微微游移。
                    // 按"拍得到就不动"：窗内只保留跑动的基础留白（恒定，不晃），
                    // 出窗才把额外留白摇上来——那时确实需要它把前方纳入画面。
                    // 临界阻尼而非 MoveTowards：线性斜坡在起止两端各有一次加速度台阶，
                    // 焦点会"猛地开始平移、猛地停住"，与偏航的阶跃是同一个毛病
                    _offAxisRun = Mathf.SmoothDamp(_offAxisRun,
                        _aheadOut ? Mathf.Clamp01(moveSpeed / 5.2f) : 0f,
                        ref _offAxisVel, 0.45f, Mathf.Infinity, dt);
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
                else _offAxisRun = Mathf.SmoothDamp(_offAxisRun, 0f, ref _offAxisVel, 0.45f, Mathf.Infinity, dt);
            }
            else _offAxisRun = Mathf.SmoothDamp(_offAxisRun, 0f, ref _offAxisVel, 0.45f, Mathf.Infinity, dt);
            if (!_pivotInit) { _pivotY = _pivotYAnchor = targetPivotY; _pivotXZ = focusXZ; _focusAnchor = focusXZ; _pivotInit = true; }

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

            // 纵向死区（与水平的焦点死区同理）：CharacterController 贴地/上下台阶/
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
                // 拉远【只在看不见前方时给】，而且是两档不是连续量。
                // 原写法 turnOut 随偏差连续变，
                // 而偏差恒等于拇指角 θ ⇒ **拇指的抖动被直接接到了吊杆长度上**，
                // 画面持续前后微微推拉——这是"不平稳"里属于纵深的一路。
                // 按"拍得到就不动"的原则：窗内不给拉远，出窗才给，转完即回。
                float turnOut = _aheadOut ? 0.15f : 0f;
                wantFactor = 1f + runOut + turnOut + _offAxisRun * 0.12f;
            }
            // 大招镜头：短暂拉近（覆盖当前构图，结束自动回稳）
            // 推近幅度随特写强度分级：击杀轻推、超必杀满推——不再一律贴到 0.82
            float zoomTo = Mathf.Lerp(1f, ultimateZoom, _closeStrength);
            wantFactor = Mathf.Lerp(wantFactor, zoomTo, _ultimateBlend);
            wantFactor *= _shot.distanceMult;   // 景别决定景深：群战拉远、狭窄收紧、决胜推近
            // 变焦极慢（电影推轨是分镜级动作，不是逐帧伺服）：缠斗中距离忽近忽远
            // 不再造成镜头前后泵动
            // 临界阻尼而非 Lerp：Lerp 面对阶跃目标会在第一帧就给出最大速度
            //（0.78 m/s 的推拉），临界阻尼则从 0 平滑加速再平滑停下
            _lenFactor = Mathf.SmoothDamp(_lenFactor, wantFactor, ref _lenFactorVel,
                0.75f, Mathf.Infinity, dt);

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
            // 遮挡持续计时：供上方的换角决策使用（0.6m 以上的回缩才算真被挡）
            _occludedT = wantDist < maxDist - 0.6f ? _occludedT + dt : 0f;

            // ===== 吊杆抗颤：取最近一小段时间内的【最短值】 =====
            // 球投射的命中是二值的，而战斗中吊杆常常在擦着地面/矮台/柱角扫来扫去，
            // 于是 wantDist 在"打到"与"没打到"之间逐帧翻转。配上"回缩 0.03s（几乎瞬时）、
            // 伸出 0.14s"的非对称平滑，就是一条锯齿——镜头以帧率级的频率前后弹，
            // 这是画面抖动里与偏航完全无关的一路，光调转镜永远治不好。
            // 做法：变近立刻采纳（不能穿墙），变远则要求"确实空出来了"持续
            // BoomClearHold 才承认。一两帧的漏检再也推不动吊杆。
            if (wantDist <= _boomWant) { _boomWant = wantDist; _boomClearT = 0f; }
            else
            {
                _boomClearT += dt;
                if (_boomClearT > BoomClearHold) _boomWant = wantDist;
            }
            // 回缩仍然快（避免穿墙）但不再是瞬时（0.03→0.06）；伸出保持 0.14s
            float smooth = _boomWant < _boomDist ? 0.06f : 0.14f;
            _boomDist = Mathf.SmoothDamp(_boomDist, _boomWant, ref _boomVel, smooth,
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
