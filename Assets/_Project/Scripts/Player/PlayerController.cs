using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.Combat;
using AdversityRoad.Mobile;

namespace AdversityRoad.Player
{
    /// <summary>
    /// 第三人称移动控制器：走/跑（摇杆模拟量）/慢走/蹲伏潜行/跳跃/
    /// 翻滚闪避（无敌帧）/快速灵活转身。
    /// 每帧把运动状态喂给 HumanoidAnimator 驱动类人步态动画。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        // ===== 速度必须锚在动画片段自己的自然速度上 =====
        //
        // 【这是"经常性滑动、像被动画控制"的总根源，前面几轮我一直在下游修】
        // 三份实机日志把每条片段的自然速度量出来了（自然速度 = 实测地面速度 ÷
        // 该帧的播放倍率，日志里两列都有）：
        //
        //     片段            自然速度    改前游戏跑到    播放倍率
        //     Walking         2.05 m/s    1.3~1.7        0.64~0.81×
        //     Jog Forward     2.77        3.80           1.37×
        //     Running         3.63        5.20           1.43×
        //     Right Strafe    3.27        5.19           1.59×
        //     Fast Run        3.37        8.0~9.0        2.4~2.7×（被 2.0 上限截断）
        //
        // 也就是说**每一条跑动片段都在被快放**。步幅同步能让脚在数值上不打滑
        // （步幅比确实是 0.96），但腿的摆动频率比动捕快 40%，整个人读起来就是
        // 在冰上滑——这正是"滑动""不自然""像被动画控制"。Fast Run 更是要 2.4 倍
        // 才跟得上，而速率上限是 2.0，那一档是**真的**在滑。
        //
        // 业界经验值：播放倍率在 0.85~1.15 之间看不出来，超过 1.25 就明显是快放。
        // 所以速度按片段来定，而不是反过来：
        //     walkSpeed  ← Walking 2.05        → 2.0
        //     runSpeed   ← Running 3.63 × 1.15 → 4.2
        //     室内       ← 走路档              → 2.0（见 indoorPaceSpeed）
        // 代价是角色比以前慢约 20%。这是有意的：3.8 m/s 在自己家里是 13.7 km/h
        // 的全力奔跑，两三步一堵墙，"很难精准控制"有一大半是这么来的。
        // 4.2 m/s 的转弯半径（0.6g）是 3.0m，5.2 m/s 是 4.6m——差着一间屋子。
        [Header("移动（速度锚在动画片段的自然速度上，见上方推导）")]
        public float walkSpeed = 2.0f;
        /// <summary>步幅可拉伸的上限倍率：横移封顶 = 该方向片段自然速度 × 它。
        /// 超过这个倍数，播放速率就把步子撑得太开，脚开始打滑。</summary>
        const float StrideRateCap = 1.35f;
        /// <summary>静止起步时，身体要先转到离目标方向多少度以内才允许迈步。
        /// 50° 之内人本来就是边转边走的，再严就成了"起步要等一下"。</summary>
        const float StartAlignDeg = 50f;

        /// <summary>移动积分的单帧步长上限（秒）。见 Update 里的推导。</summary>
        public const float MaxSimStep = 0.05f;

        /// <summary>摇杆死区：低于它一律判为没推。</summary>
        const float StickDeadZone = 0.06f;

        /// <summary>算转向上限时速度的下限（m/s）。低于它上限不再随速度继续升，
        /// 这是"减速转弯"不会发散成陀螺的关键——推导见转向那一段。</summary>
        const float TurnCapSpeedFloor = 1.6f;
        /// <summary>待转角超过它才开始减速转弯（度）。以内的小修正顺势就转了。</summary>
        const float SharpTurnDeg = 45f;
        /// <summary>待转角到这个值就完全压到转身速度（度）。</summary>
        const float HardTurnDeg = 110f;
        /// <summary>转急弯时的速度（m/s）。取 Walking 自然速度 2.05 的 0.8 倍，腿仍在迈步。</summary>
        const float PivotSpeed = 1.6f;

        /// <summary>转向上限每秒最多升高多少（度/秒²）。防的是"憋一下然后猛甩"。</summary>
        const float TurnCapRise = 400f;
        float _turnCapSm;

        /// <summary>
        /// 只要在动，地面速度就不低于这个值（m/s）。
        /// 取 Walking 实测自然速度 2.05 的 0.63 倍——再慢腿就开始"停下来"了。
        /// 推导见 Update 里那段注释（"像鬼一样漂移"的根因）。
        /// </summary>
        const float MinLocomotionSpeed = 1.3f;
        public float runSpeed = 4.2f;
        // 起步/刹车响应（指数逼近速率，1/秒）：对齐 Unity 官方 ThirdPersonController
        // 的 SpeedChangeRate=10 思路，但按动作游戏上调——
        // 起步 k=20：0.05s 到 63%、0.15s 到 95%；刹车 k=26 更利落。
        // 防晕由镜头侧负责（软跟随 + 焦点死区 + 渐进回正），而不是靠拖慢角色本体。
        public float accelRate = 20f;              // 起步逼近速率
        public float decelRate = 26f;              // 停步逼近速率
        // 转向的角速度上限（度/秒）。**不是** Slerp 的比例系数——旧的 rotateSpeed /
        // quickTurnMultiplier 就是当比例用的，导致身体几乎瞬间贴到摇杆上，见移动方法里的说明。
        public float turnDegPerSecStill = 720f;    // 站定时的封顶：180° 用 0.25s，推杆即回身
        /// <summary>转向允许的最大横向加速度（m/s²）。角速度由它反推：ω = a / v。
        /// 这是本作转向手感的**唯一**旋钮，而且它有物理含义。
        ///
        /// 【录屏实测把这个值判死了】上一版写 16（≈1.63g）。旁边那段注释自己
        /// 就写着"10 ≈ 1g，接近真人极限"，我却把它设到了 1.6 倍真人极限。
        /// 录屏里 13 秒每一帧的读数：横向恒为 1.63g（一直顶着上限），
        /// 转向时 身197°/s、254°/s，半径 0.9~1.3m。按每步 1.39m 折算：
        ///
        ///     速度      a=16（旧，1.63g）              a=6（新，0.61g）
        ///     2.6m/s   ω353°/s 半径0.4m  一圈 2 步     ω132°/s 半径1.1m  一圈 5 步
        ///     3.8m/s   ω241°/s 半径0.9m  一圈 4 步     ω 90°/s 半径2.4m  一圈11 步
        ///     5.2m/s   ω176°/s 半径1.7m  一圈 8 步     ω 66°/s 半径4.5m  一圈20 步
        ///
        /// "摇杆转一圈只需要一两步就完成了"不是错觉，是 4 步——因为角色在用
        /// 3.8m/s 绕一个直径不到两米的圈。真人慢跑转弯的横向加速度是 0.4~0.6g，
        /// 我们给了 1.63g，所以无论腿上播什么片段，看上去都只能是被甩着转的陀螺。
        ///
        /// 低速端不受影响：v→0 时 ω 由 turnDegPerSecStill(720°/s) 封顶，
        /// 站定推杆照样立刻回身。变慢的只有"高速中途大角度换向"，
        /// 而那本来就该慢——那是重量感的来源。</summary>
        public float maxTurnLateralAccel = 6f;

        /// <summary>转向"重量"的运行时覆盖（m/s²，0=用上面的字段值）。设置面板可切。
        ///
        /// 这一档没有唯一正确答案，只能上手感觉，所以做成可切而不是我替你定：
        ///     12（1.22g）轻 —— 3.8m/s 时 ω181°/s 半径1.20m 一圈  5 步
        ///      9（0.92g）中 —— 3.8m/s 时 ω136°/s 半径1.60m 一圈  7 步
        ///      6（0.61g）重 —— 3.8m/s 时 ω 90°/s 半径2.41m 一圈 11 步
        /// 参考：实机原值是 16（1.63g，半径0.9m、一圈4步），真人慢跑转弯 0.4~0.6g。
        /// 默认取【中】：既离开了陀螺档，也不像 6 那样掉头要两秒。</summary>
        public static float TurnAccelOverride = 9f;
        float TurnAccel => TurnAccelOverride > 0f ? TurnAccelOverride : maxTurnLateralAccel;
        public float jumpForce = 7f;
        public float gravity = -20f;

        [Header("蹲伏")]
        public float crouchSpeedMult = 0.45f;

        [Header("闪避（翻跟头）")]
        public float dodgeSpeed = 10f;
        public float dodgeDuration = 0.32f;
        // 无敌帧不再是一个独立的短常数，而是【按翻滚时长的比例】给（见下方 IFrameRatio）。
        // 旧配置 0.25s 固定值配上 0.42~0.70s 的滚翻片段，意味着后半段整整
        // 0.2~0.45 秒里：人被锁在翻滚里动不了、判定框却已经能打到你。
        // 玩家反馈的"闪避太慢、很容易闪避失败还是被打到"就是这一段。
        public float dodgeIFrames = 0.28f;

        /// <summary>无敌帧占整个翻滚时长的比例：滚动主体全程无敌，只有收势的尾巴可被打中。</summary>
        const float IFrameRatio = 0.72f;

        /// <summary>翻滚可被攻击/再次翻滚打断的起点（占总时长比例）：过了这里就能接下一手。</summary>
        const float DodgeCancelAt = 0.62f;
        public float dodgeStaminaCost = 20f;

        public PlayerStats Stats = new PlayerStats();
        public Transform cameraTransform;

        CharacterController _cc;
        CombatStateMachine _combat;
        HumanoidAnimator _anim;
        LockOnSystem _lockOn;
        PlayerAppearance _appearance;
        Combat.PlayerCombatController _pcc;
        float _vy;
        float _dodgeTimer, _iframeTimer;
        float _dodgeSpd = 10f;   // 本次翻滚的实际速度（时长匹配片段时反比缩放）
        float _dodgeDur = 0.32f; // 本次翻滚的总时长（取消窗按它的比例算）
        Vector3 _dodgeDir;
        Vector3 _lastPos;
        Vector3 _hVel;   // 平滑后的水平速度
        float _lastMoveAngle;   // 上一个**有效**的行进夹角（速度太低时沿用它，见 LateUpdate）

        // ===== 外部位移通道：角色的位置只能有一个写入方 =====
        //
        // 【玩家的原话是"更像是被动画控制了"，这就是它的出处】
        // 在此之前，能移动这个 CharacterController 的地方有七处，各自在自己的
        // 脚本时序里独立调用 StepMove：
        //   PlayerController 三处（主移动 / 平台跟随 / 翻滚）、
        //   PlayerCombatController 两处（出招磁吸突进 / 受击后退）、
        //   SkillExecutor 一处（技能突进）、HomeComforts 一处（跑步机履带）。
        // 没有任何一方拥有角色的位置，于是：
        //   · 同一帧里两次 Move 叠加，实测速度可以远超指令速度——实机日志里
        //     293 帧（2.4%）出现这种情况，其中 287 帧发生在出招期间；
        //   · **最后一次 Move 决定 isGrounded**。出招磁吸只挪水平、不带重力，
        //     于是它一跑，这一帧就判成"离地"——日志里 grounded 每秒翻转 2.0 次、
        //     20.7% 的帧被判在空中。落地/起跳姿态、下落循环、踩地校准全跟着乱。
        //
        // 改成一条通道：外部系统只能"申请"本帧的额外位移，由 PlayerController
        // 在它自己那一次 StepMove 里**连同重力一起**落地。写入方只剩一个，
        // 于是位移可加、接地稳定、而且在日志里一眼看得出这一帧是谁在推人。
        Vector3 _extMove;

        // 嵌墙兜底用：上一个确认没有嵌进环境的位置
        Vector3 _safePos;
        bool _hasSafePos;

        /// <summary>带迟滞的接地判定：isGrounded 抖动时仍算在地面上（复用土狼时间）。
        /// 动画与姿态一律用它，别直接读 _cc.isGrounded。</summary>
        public bool GroundedStable => _cc != null && (_cc.isGrounded || _coyoteT > 0f);

        /// <summary>诊断：本帧外部位移的大小（米）。</summary>
        public float DbgExtMove { get; private set; }
        /// <summary>诊断：本帧外部位移的来源标签。</summary>
        public string DbgExtSrc { get; private set; } = "";

        /// <summary>外部系统申请本帧的额外位移（世界坐标）。source 只用于日志。</summary>
        public void AddExternalMove(Vector3 delta, string source)
        {
            _extMove += delta;
            if (!string.IsNullOrEmpty(source)) DbgExtSrc = source;
        }

        /// <summary>取走并清空本帧的外部位移。一帧只会被真正消费一次。</summary>
        Vector3 TakeExternalMove()
        {
            var v = _extMove;
            _extMove = Vector3.zero;
            DbgExtMove = v.magnitude;
            if (v.sqrMagnitude < 1e-10f) DbgExtSrc = "";
            return v;
        }

        /// <summary>拖延泥潭等减速效果的外部倍率（1 = 正常）。</summary>
        // ===== 移速减益：按【来源】登记，每帧取最小 =====
        //
        // 原来这是一个所有人共写的标量：拖延沼泽、冻结 debuff、法庭束缚、责任球、
        // 技能解冻五个系统各自 `Min(cur, x)` 压低、各自无条件写回 `1f`。两个后果：
        //   ① 两个减益重叠时，**先退出的那个会顺手替另一个解除**（写 1f 是无条件的）；
        //   ② 某个来源的 OnTriggerExit / OnDestroy 没跑到（被传送出触发体是典型情形），
        //      倍率就**永久卡低**——玩家从此只能慢走，且没有任何办法恢复。
        //
        // 改成登记制：谁减速谁登记，解除时只撤自己那一条，最终倍率 = 所有在册来源
        // 取最小。关键收益是**自愈**：来源对象一旦被销毁（组件没了、触发体没了），
        // 下一帧就会因为 Unity 的空判定被自动清掉，不再需要谁"记得"去解除。
        readonly Dictionary<Object, float> _slowSources = new Dictionary<Object, float>();
        readonly List<Object> _slowDead = new List<Object>();

        /// <summary>
        /// 只许走、不许跑（在住处这类室内空间里由 IndoorZone 打开）。
        ///
        /// 不是减益：减益会写进"移速倍率"里被当成中了负面状态，也会被清减益的地方抹掉。
        /// 这是一条场地规则——屋里就是不跑，出门自动恢复。
        /// </summary>
        /// <summary>【身份钉·冲刺锁】跑不起来，只能走。硬性 debuff，就是要难受。</summary>
        public bool WalkOnly { get; set; }

        /// <summary>
        /// 【室内步速】在自己家里不冲刺。与 WalkOnly 是**两件事**，必须分开存：
        /// 两者此前共用 WalkOnly 一个字段，而 IndoorZone.Evaluate 与
        /// IdentityNailSystem.ApplyLocks 都在无条件写它——谁后跑谁说了算。
        /// 于是带着冲刺钉走出屋子会把钉子的效果一起清掉，反之亦然（铁律二）。
        /// </summary>
        public bool IndoorPace { get; set; }

        /// <summary>
        /// 室内步速上限：**走路档**，正好等于 Walking 片段的自然速度。
        ///
        /// 旧值 3.8 是按"屋里不冲刺但也别像在泥里走"定的，可 3.8 m/s 是
        /// 13.7 km/h——在自己家里全力奔跑。后果全是玩家反复报的那几条：
        ///   · 腿被以 1.37 倍速率驱动（Jog Forward 自然速度只有 2.77）⇒ 滑；
        ///   · 转弯半径 v²/a = 3.8²/5.9 = 2.4m，走廊里根本转不过来 ⇒ 蹭墙；
        ///   · 一间十米的屋子 2.6 秒穿过 ⇒ 每一次输入都过冲，"很难精准控制"。
        /// 2.0 m/s 下这三条同时消失：倍率 1.0×、转弯半径 0.68m、穿屋 5 秒。
        /// </summary>
        public float indoorPaceSpeed = 2.0f;

        /// <summary>当前移速倍率（所有在册减益取最小；无减益 = 1）。</summary>
        public float MoveSpeedMultiplier
        {
            get
            {
                float m = 1f;
                _slowDead.Clear();
                foreach (var kv in _slowSources)
                {
                    if (kv.Key == null) { _slowDead.Add(kv.Key); continue; }   // 来源已销毁：自愈
                    if (kv.Value < m) m = kv.Value;
                }
                foreach (var d in _slowDead) _slowSources.Remove(d);
                return Mathf.Clamp(m, 0.05f, 1f);
            }
            // 兼容旧写法：写 1 视为"清空我造成的减速"，写小于 1 视为一条匿名减益。
            // 新代码请直接用 SetSlow / ClearSlow，把来源说清楚。
            set { if (value >= 0.999f) ClearAllSlow(); else SetSlow(this, value); }
        }

        /// <summary>登记一条移速减益（同一来源重复登记即覆盖）。</summary>
        public void SetSlow(Object source, float mult)
        {
            if (source == null) return;
            _slowSources[source] = Mathf.Clamp(mult, 0.05f, 1f);
        }

        /// <summary>撤销某个来源的减益（只撤自己这一条，不影响别人）。</summary>
        public void ClearSlow(Object source)
        {
            if (source != null) _slowSources.Remove(source);
        }

        /// <summary>清空全部减益（「燃火·解冻」这类明确的全面解除才用）。</summary>
        public void ClearAllSlow() => _slowSources.Clear();

        // ===== 移动平台承载 =====
        // CharacterController 不会自动跟随脚下移动的物体：车在动、脚下的碰撞体在动，
        // 而角色的世界坐标是自己算出来的，于是"跳上车顶后车开走、人留在原地"。
        // Unity 从来没有内建这个——所有第三人称游戏都得自己补：每帧记住脚下那个
        // 物体的位移，原样加到角色身上。
        Transform _platform;
        Vector3 _platformLastPos;

        // ===== 掉出世界兜底 =====
        // 出生点/传送点偶尔会落在几何体外或寻路烘焙完成前的空档，人一路掉下去，
        // 画面只剩天空与雾——既没有死亡判定（不掉血），也没有任何出口。
        // 与其继续追查每一个可能的落点错误，不如先兜住结果：**低于世界底面就捞回来**。
        // 这是所有开放场景都会加的一条保险，成本只有一次 y 比较。
        const float WorldFloorY = -25f;
        Vector3 _lastSafePos;
        float _safeStamp;

        /// <summary>相对落差兜底：比"最近站稳的地方"低这么多就算掉出去了。</summary>
        const float FallCatchDrop = 12f;

        // ===== 反复踩空的升级处理 =====
        // 实机日志里这一行连着刷了十几秒，坐标一模一样：
        //   踩空捞回 掉落点 (20067,-11,-51) → 捞到 (20067,2,-51)
        // 说明"最近站稳的地方"本身就在一个会掉下去的边沿上：捞回原地 → 立刻再掉 → 再捞。
        // 而每次捞回都会清零水平速度，玩家的体感就是**推着摇杆却一直在原地被拽住**——
        // 他反馈的"在自动生成的关卡里不能跑动"，其实就是这个循环，不是移速出了问题。
        //
        // 所以捞回不能只是"放回去"，还要**升级**：同一处反复掉，就换更靠谱的落点。
        Vector3 _lastCatchAt;
        int _catchStreak;
        float _lastCatchTime;

        void FallGuard()
        {
            if (_cc == null) return;
            // 记录最近一次"站在实地上"的位置，作为捞回目标（每 0.4s 记一次足够）
            if (_cc.isGrounded && transform.position.y > WorldFloorY + 5f &&
                Time.time - _safeStamp > 0.4f)
            {
                _safeStamp = Time.time;
                _lastSafePos = transform.position;
            }
            // 捞回条件从"掉到世界底板（y=-25）"改成"绝对底板 **或** 比刚才站稳处低 12 米"。
            // 只看绝对底板的问题是：各区地板都在 y≈0，从边缘掉下去要坠落约 2.2 秒才够到
            // -25——那两秒钟玩家看到的就是自己在无尽虚空里往下掉（"掉进深渊"）。
            // 12 米约合 1.5 秒内触发，且远高于任何正常跳跃/落差，不会误捞。
            bool belowFloor = transform.position.y <= WorldFloorY;
            bool longDrop = _vy < -1f && _lastSafePos.sqrMagnitude > 0.01f &&
                            transform.position.y <= _lastSafePos.y - FallCatchDrop;
            if (!belowFloor && !longDrop) return;

            // 捞回目标必须是**脚下确实有地**的地方，否则就是把人从虚空捞进虚空：
            // 旧版在 _lastSafePos 还没记下来时用 `当前位置 + 30m`——那儿同样没有地，
            // 于是掉 12m→捞高 30m→再掉，字幕一遍遍刷"脚下踩空了"，永远出不来。
            // 这正是玩家看到的死循环。
            // 连续踩空计数：3 秒内又掉在同一处（10 米内）就算"卡在同一个坑里"
            bool sameSpot = Time.time - _lastCatchTime < 3f &&
                            (transform.position - _lastCatchAt).sqrMagnitude < 100f;
            _catchStreak = sameSpot ? _catchStreak + 1 : 1;
            _lastCatchAt = transform.position;
            _lastCatchTime = Time.time;

            // 掉第二次开始就不再信"最近站稳的地方"——它显然是个边沿。
            // 直接回本区出生点；再掉就说明这处场景根本站不住人，整个退出去。
            if (_catchStreak >= 2)
            {
                // 【落点只能在人现在待的那处地方里找】
                bool insideSite = OpenWorld.SiteGate.InsideSite;
                Vector3 spawn = HomeSpawn(out bool spawnTrusted);

                // 【在生成场景里，任何情况都不把人送出关卡】
                // 玩家两次报的"穿越回独居小屋 / 训练武馆"，这条升级逻辑是其中一路：
                // 被击退摔出边沿 → 连续触发兜底 → ExitToCity → 回到进关时站的地方。
                // 之前只用"附近 30 米有没有活敌人"挡了一下，可击退能把人抛出三十米开外，
                // 于是照样漏过去。现在的规则简单得多：**人在生成场景里就只在场景内部捞**，
                // 场景自己的入口下面无条件铺了实地板，一定站得住；
                // 只有这处场景连同地板真的已经不存在了（被卸载），才谈得上退出去。
                if (insideSite && spawnTrusted)
                {
                    _lastSafePos = spawn;
                    Core.CloudDialogueService.AddLog("同一处反复踩空 ×" + _catchStreak +
                        " @" + World.ZoneBuilder.CurrentZoneId + " → 回本关入口 " + V(spawn));
                    Snap(spawn);
                    Core.GameEvents.RaiseSubtitle("刚才那处站不稳——已经把你送回这一关的入口。");
                    return;
                }

                bool fighting = EnemyNearby(transform.position, 30f);
                if (!fighting && (_catchStreak >= 4 || !spawnTrusted))
                {
                    Core.CloudDialogueService.AddLog("同一处反复踩空 ×" + _catchStreak +
                        " @" + World.ZoneBuilder.CurrentZoneId + " " + V(transform.position) + " → 退出该场景");
                    _catchStreak = 0;
                    _lastSafePos = Vector3.zero;
                    // 走到这里说明场景已经没了（或人本来就不在场景里）：回城/回小屋
                    if (insideSite) OpenWorld.SiteGate.ExitToCity();
                    else Snap(World.ZoneBuilder.PlayerSpawnOf(0));
                    Core.GameEvents.RaiseSubtitle("这块地方站不住人——已经把你带出来了。");
                    return;
                }
                _lastSafePos = spawn;
                Core.CloudDialogueService.AddLog("同一处反复踩空 ×" + _catchStreak +
                    " → 改回本关入口 " + V(spawn));
                Snap(spawn);
                Core.GameEvents.RaiseSubtitle("刚才那处站不稳——已经把你送回这一关的入口。");
                return;
            }

            Vector3 back;
            if (HasGroundUnder(_lastSafePos)) back = _lastSafePos;
            else
            {
                back = HomeSpawn(out _);
                _lastSafePos = back;   // 同时把"安全点"本身修正掉，不然下一次又回到虚空
                Core.CloudDialogueService.AddLog("踩空且无安全点 @" + World.ZoneBuilder.CurrentZoneId +
                    " 掉落点 " + V(transform.position) + " → 送回 " + V(back));
                Core.GameEvents.RaiseSubtitle("刚才那块地不存在——已经把你送回安全的落点。");
                Snap(back);
                return;
            }

            back.y += 1.2f;
            // 掉出去的**位置**记进日志：这是唯一能查出"从哪儿掉的"的线索。
            // 只发一句字幕的话，玩家截图给我的永远只有"又踩空了"，查不下去。
            Core.CloudDialogueService.AddLog("踩空捞回 @" + World.ZoneBuilder.CurrentZoneId +
                " 掉落点 " + V(transform.position) + " → 捞到 " + V(back));
            Snap(back);
            Core.GameEvents.RaiseSubtitle("脚下踩空了——已经把你拉回刚才站稳的地方。");
        }

        /// <summary>
        /// "这一关的入口在哪"——捞人时唯一该用的落点。
        ///
        /// 【为什么不能直接查区域表】
        /// 老代码写的是 `PlayerSpawnOf(IndexOfZone(CurrentZoneId))`，而 `IndexOfZone`
        /// 查不到时会**静默返回 0**，0 号区就是独居小屋。生成场景被卸载后 id 会被改成
        /// `xxx_closed`，此时这一行就把"我在自己关卡里"翻译成了"我在独居小屋"，
        /// 于是玩家在战斗中被一脚踢回经典关卡。
        /// 现在按可靠性排序找：场景自己的落点 → 区域表里**确实查到**的那一条 →
        /// 最后才是 0 号区。<paramref name="trusted"/> 为 false 表示"只剩最后的兜底了"，
        /// 调用方可以据此判断这处地方是不是真的没救了。
        /// </summary>
        static Vector3 HomeSpawn(out bool trusted)
        {
            if (OpenWorld.SiteGate.TryCurrentSiteSpawn(out var siteSpawn))
            {
                trusted = true;   // 落点下面有无条件铺的 GroundPad，不用再验地
                return siteSpawn;
            }
            int zone = World.ZoneBuilder.IndexOfZone(World.ZoneBuilder.CurrentZoneId);
            if (zone >= 0)
            {
                var p = World.ZoneBuilder.PlayerSpawnOf(zone);
                if (HasGroundUnder(p)) { trusted = true; return p; }
            }
            trusted = false;
            return World.ZoneBuilder.PlayerSpawnOf(0);
        }

        void Snap(Vector3 to)
        {
            _cc.enabled = false;
            transform.position = to + Vector3.up * 0.2f;
            _cc.enabled = true;
            _vy = 0f;
            _hVel = Vector3.zero;
            _hasSafePos = false;   // 传送后旧的安全点作废，否则回滚会把人拽回上一个场景
        }

        static string V(Vector3 p) =>
            "(" + Mathf.RoundToInt(p.x) + "," + Mathf.RoundToInt(p.y) + "," + Mathf.RoundToInt(p.z) + ")";

        /// <summary>附近有没有活着的敌人（判断"是不是正在打"，别在战斗中把人传走）。</summary>
        static bool EnemyNearby(Vector3 pos, float radius)
        {
            foreach (var e in AdversityRoad.Core.ActorRegistry.Enemies)
            {
                if (e == null || e.State == AI.EnemyState.Dead) continue;
                if ((e.transform.position - pos).sqrMagnitude <= radius * radius) return true;
            }
            return false;
        }

        /// <summary>这个点下方 30 米内有没有实地（有就敢往那儿捞人）。</summary>
        static bool HasGroundUnder(Vector3 p)
        {
            if (p.sqrMagnitude < 0.01f) return false;
            return Physics.Raycast(p + Vector3.up * 2f, Vector3.down, 30f,
                ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// 传送之后必须调用：清掉上一处的"最近站稳点"。
        ///
        /// 不清的话，人刚被传到新关卡、还没落地就触发一次兜底，会被**拽回上一关**——
        /// 表现成"刚进去就被弹出来"，比掉进虚空更让人摸不着头脑。
        /// </summary>
        public void NotifyTeleported()
        {
            _lastSafePos = Vector3.zero;
            _safeStamp = 0f;
            _vy = 0f;
            _hVel = Vector3.zero;

            // 传送 = 离开了此前所有触发体，所以在册的减速一律作废。
            //
            // 实机日志：连着三次「进入场景 … · 移速倍率 0.45 · 行动力 100 · 蹲伏 否」——
            // 0.45 正是拖延泥潭的减速值。玩家在独居小屋的泥潭里被登记了一条减速，
            // 然后传送进生成关卡：泥潭对象还在原地好好的（所以"来源销毁即自愈"不生效），
            // OnTriggerExit 也因为人是被瞬移走的而没跑到，于是这条减速**永久跟着他**。
            // 他反馈的"在自动生成的关卡里不能跑动"就是这个——不是关卡的问题。
            ClearAllSlow();
        }

        void CarryByPlatform()
        {
            if (_cc == null) return;
            Transform found = null;
            if (_cc.isGrounded)
            {
                // 从腰部往下扫一个略小于胶囊半径的球：命中脚下的实体碰撞体
                // 起点/长度必须按胶囊体算：角色 transform 原点在胶囊【中部】，
                // 上一版从 +0.7（胸口）只往下扫 1.3m，最远到 -0.6——而脚底在 -1.0，
                // 射线**从来没够到过地面**，所以车顶承载一直不生效。
                Vector3 castO = transform.position + _cc.center + Vector3.up * 0.1f;
                float castLen = _cc.height * 0.5f + 0.5f;
                if (Physics.SphereCast(castO,
                        Mathf.Max(0.1f, _cc.radius * 0.85f), Vector3.down,
                        out RaycastHit hit, castLen, ~0, QueryTriggerInteraction.Ignore))
                {
                    var col = hit.collider;
                    var t = col != null ? col.transform : null;
                    // 【只认真正的"会动的地板"，不是脚下随便什么东西】
                    // 原判据是"扫到的任何碰撞体，只要不是自己的子物体"，掩码还是 ~0。
                    // 于是家具、道具、门、路人的胶囊、敌人全被当成平台——只要那个
                    // transform 动一下，玩家就被 delta 拖着走。住所里家具最密，
                    // 所以"漂移/穿墙/不受控制"在屋里最明显。
                    // 角色一律排除：站在别人头上被一起搬走没有任何合理性。
                    if (t != null && !t.IsChildOf(transform) &&
                        col.GetComponentInParent<PlayerController>() == null &&
                        col.GetComponentInParent<AI.EnemyController>() == null &&
                        col.GetComponentInParent<Combat.HumanoidAnimator>() == null &&
                        !t.gameObject.isStatic)          // 静态几何永远不动，不必跟随
                        found = t;
                }
            }

            if (found != null && found == _platform)
            {
                Vector3 delta = found.position - _platformLastPos;
                // 【阈值 25 是 5 米——那不叫"合理幅度"，那是一次瞬移】
                // 原来允许单帧跟随 5 米，且用的是裸 Move 不是分步扫掠：
                // 5 米的单段扫掠必然跨过一整堵墙，这就是屋里"漂移穿墙"的直接来源。
                // 真实的移动平台（车顶、电梯）单帧位移不会超过几厘米；
                // 超过 0.4m 一律判为"平台被瞬移/重生"，不跟随。
                const float MaxCarryPerFrame = 0.4f;
                float d2 = delta.sqrMagnitude;
                if (d2 > 1e-8f && d2 < MaxCarryPerFrame * MaxCarryPerFrame)
                    AddExternalMove(delta, "平台跟随");   // 与重力同一次落地，接地才稳
            }
            _platform = found;
            if (found != null) _platformLastPos = found.position;
        }

        /// <summary>本帧摇杆的世界方向（相机相对，零向量=未推杆）。
        /// 技能连招据此判断玩家是否正在主动引导方向，从而让出自动吸附。</summary>
        public Vector3 StickWorldDir { get; private set; }

        // ---- 意图匹配：输入缓冲 + 土狼时间（大作通用）----
        const float DodgeBufferWindow = 0.3f;   // 闪避缓冲窗（硬直中按下也能在恢复帧兑现）
        const float JumpBufferWindow = 0.2f;    // 跳跃缓冲窗（落地前按下，落地即跳）
        const float CoyoteTime = 0.12f;         // 土狼时间（离开边缘后仍可起跳）
        readonly InputBuffer _inputBuf = new InputBuffer();

        /// <summary>供其它战斗组件共用的输入缓冲（技能在动作锁期间的排队兑现）。</summary>
        public InputBuffer Buffer => _inputBuf;
        float _coyoteT;
        float _attackSpeedFactor = 1f;   // 出招定步倍率（平滑过渡，防连打时速度震荡）

        public bool IsInvincible => _iframeTimer > 0;
        public bool IsDodging => _dodgeTimer > 0;
        public bool IsCrouched { get; private set; }
        /// <summary>手指/方向键此刻是否在推——**零平滑、零延迟**。
        /// 镜头的跟随起停以它为准（见 ThirdPersonCamera 的 active 判据）。</summary>
        public bool StickHeld { get; private set; }

        /// <summary>此刻胶囊是不是贴着地（调试日志用；_cc 是私有的）。</summary>
        public bool IsGroundedNow => _cc != null && _cc.isGrounded;
        /// <summary>是否离地（跳跃/坠落中）。镜头据此切"腾空·拉远看落点"景别。</summary>
        public bool Airborne => _cc != null && !_cc.isGrounded;
        /// <summary>纵向速度（负=下坠）。镜头用它区分"起跳上升"与"真的在往下掉"。</summary>
        public float VerticalVelocity => _vy;

        /// <summary>倒地起身等外部授予的无敌帧。</summary>
        public void SetInvincible(float duration) => _iframeTimer = Mathf.Max(_iframeTimer, duration);

        /// <summary>空中下劈等强制下坠。</summary>
        public void ForceFall(float verticalVelocity) => _vy = Mathf.Min(_vy, verticalVelocity);

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _combat = GetComponent<CombatStateMachine>();
            _anim = GetComponent<HumanoidAnimator>();
            _lastPos = transform.position;
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
        }

        void Update()
        {
            // 运行时组装顺序下 Awake 期间兄弟组件可能尚未挂载，惰性补齐
            if (_combat == null) _combat = GetComponent<CombatStateMachine>();
            if (_pcc == null) _pcc = GetComponent<Combat.PlayerCombatController>();

            // ===== 移动积分的步长必须自己钳，不能指望 Time.maximumDeltaTime =====
            //
            // 上一版把 Time.maximumDeltaTime 设成 0.05，指望它去钳 Time.deltaTime。
            // 实机日志否掉了这个指望：t=320.004 那一帧 hVel=5.20、位移 0.691m，
            // 0.691 / 5.20 = 0.133s —— 用的就是没被钳过的整帧时间。
            // 不管那是 Unity 6 的语义（maximumDeltaTime 只管 FixedUpdate 追帧）
            // 还是别处把它改了回去，结论一样：**这条上限必须由这里自己落地**，
            // 它就在积分的现场，谁都改不掉。
            //
            // 0.05s（20fps）的取法见 GameBootstrap 里的推导：实机 12281 帧里
            // 超过它的只有 24 帧（0.2%），而最狠的一帧位移由 0.691m 降到 0.260m。
            // 0.69 米是一步半，出现在一帧里就是"抑扬顿挫突然漂移到别处"。
            float dt = Mathf.Min(Time.deltaTime, MaxSimStep);
            Stats.TickRegen(dt, _combat != null && _combat.InCombat);
            if (Stats.IsDead) return;

            if (_iframeTimer > 0) _iframeTimer -= dt;

            // 输入采集 → 缓冲（意图匹配）：必须在任何 early-return 之前，
            // 否则翻滚中/硬直中按下的键会被整帧跳过而丢失（连续翻滚就是这样失效的）。
            // 消费式输入（MobileInput.GetDown）每帧只在这里读一次，其余系统一律走缓冲。
            // 按下时角色是否正处在"做不了别的事"的状态——动作锁 或 翻滚中。
            // 这一位决定该输入是【排队意图】还是【即时意图】：排队的活到动作结束，
            // 即时的用短窗口。此前不分意图，闪避窗 0.30s 连翻滚自身(0.42~0.7s)都撑不过，
            // 连续翻滚与"技能收招接闪避"必然丢键。
            bool busyNow = (_combat != null && _combat.IsActionLocked) || _dodgeTimer > 0;
            if (Input.GetKeyDown(KeyCode.LeftShift) || MobileInput.GetDown("Dodge"))
                _inputBuf.Press("Dodge", busyNow);
            if (Input.GetKeyDown(KeyCode.Space) || MobileInput.GetDown("Jump"))
                _inputBuf.Press("Jump", busyNow);

            if (_dodgeTimer > 0)
            {
                _dodgeTimer -= dt;
                Combat.CharacterMotion.StepMove(_cc,
                    _dodgeDir * _dodgeSpd * dt + Vector3.up * _vy * dt + TakeExternalMove());
                if (_dodgeTimer <= 0 && _combat != null) _combat.RequestState(CombatState.Locomotion);
                // 【收势取消】：滚动主体走完（>62%）之后，攻击/跳/再次翻滚可以立刻打断收势。
                // 此前必须把整段滚翻播完才能做别的事——"闪完还要等一下才打得出来"，
                // 于是玩家的体感是闪避又慢又亏。大作里翻滚的收招帧都是可取消的。
                if (_dodgeTimer > 0f && _dodgeTimer < _dodgeDur * (1f - DodgeCancelAt) &&
                    (_inputBuf.Has("Dodge", DodgeBufferWindow) || _inputBuf.Has("Jump", JumpBufferWindow) ||
                     (_pcc != null && _pcc.HasBufferedAttack)))
                {
                    _dodgeTimer = 0f;
                    if (_combat != null) _combat.RequestState(CombatState.Locomotion);
                }
                if (_dodgeTimer > 0f) return;   // 翻滚中按下的闪避已入缓冲，滚完立刻接下一次
            }

            bool dodgePressed = _inputBuf.Has("Dodge", DodgeBufferWindow);

            // 摇杆世界方向提前求出：硬锁动作（技能/绝招/重击）期间也要读它做「出招转向」，
            // 所以不能等到下面移动逻辑才算
            Vector2 stickInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            stickInput += MobileInput.Move;
            stickInput = Vector2.ClampMagnitude(stickInput, 1f);
            Vector3 stickDir = CameraRelative(stickInput);
            _stickMag = stickInput.magnitude;   // 移动过渡层用（起步该不该给"起步"那一段）
            StickWorldDir = stickDir;   // 供技能连招判断"玩家是否正在主动引导方向"
            // 未经平滑的"手指/按键此刻在不在"——镜头用它作为跟随起停的零延迟判据。
            // StickWorldDir 来自平滑后的摇杆量，松手后还要几十毫秒才落到阈值以下，
            // 而镜头在那几十毫秒里多转的每一度都会被玩家看见。
            StickHeld = MobileInput.MoveHeld ||
                        Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.1f ||
                        Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.1f;

            // 硬锁定（重击/倒地/硬直等）才禁止移动；轻击连段可以边移动边出招。
            // 例外——收招闪避取消（大作手感）：技能/绝招打完主要段进入恢复相位后，
            // 按闪避可立刻打断收招，不必干等动作播完。
            if (_combat != null && _combat.IsHardLocked)
            {
                if (!(dodgePressed && _combat.CanCancelRecovery && Stats.stamina >= dodgeStaminaCost))
                {
                    // 出招转向影响（大作 attack steering / directional influence）：
                    // 技能、绝招、重击期间【仍可用摇杆缓慢调整朝向】——此前这里直接 return，
                    // 摇杆完全无响应、动作结束才突然弹到新朝向，正是"出招+转向不连贯"的主因。
                    // 受击/倒地/硬直不给转向（挨打就该失控）。
                    SteerDuringAction(stickDir, dt);
                    ApplyGravityOnly(dt);
                    return;
                }
                _combat.RequestState(CombatState.Locomotion);   // 解除收招锁，落入下方翻滚
            }
            bool attacking = _combat != null && _combat.Current == CombatState.LightAttack;

            // 蹲伏切换（潜行/低姿态）
            if (Input.GetKeyDown(KeyCode.C) || MobileInput.GetDown("Crouch")) ToggleCrouch();

            // 拔刀/收刀（带剑鞘武器，手动按钮触发）
            if (Input.GetKeyDown(KeyCode.T) || MobileInput.GetDown("Sheathe"))
            {
                if (_appearance == null) _appearance = GetComponent<PlayerAppearance>();
                if (_appearance != null) _appearance.ToggleWeaponDrawn();
            }

            float inputMag = stickInput.magnitude;            // 摇杆已在本帧开头统一读取
            Vector3 moveDir = stickDir;

            // 锁定面向的目标（决定本帧是"横移"还是"朝哪走朝哪转"）
            Transform face = FacingTarget();
            StrafeActive = face != null;
            DbgStrafeCap = 0f;

            // 模拟量速度：摇杆半推=走路，全推=奔跑；桌面按住 Alt 慢走
            // 行动力过低时脚步沉重（拖延的具象体感）：35 以下开始线性减速，最低 ×0.65
            float apMult = Mathf.Lerp(0.65f, 1f, Mathf.Clamp01(Stats.actionPower / 35f));
            float speed = runSpeed * MoveSpeedMultiplier * apMult * inputMag;
            // 本帧的速度天花板（不含摇杆量）：下面的下限不能把它顶穿——
            // 室内封顶 2.0 时，下限 1.3 仍在其内；万一有更狠的减速 debuff
            // 把天花板压到 1.0，下限就得跟着降到 1.0，否则 debuff 等于失效。
            float speedCap = runSpeed * MoveSpeedMultiplier * apMult;
            // 诊断用：速度是好几层倍率连乘出来的（行动力、减速 debuff、锁定封顶、
            // 出招定步），"移动变慢"到底慢在哪一层，只看结果分不出来。
            DbgApMult = apMult;
            DbgInputMag = inputMag;
            DbgRawSpeed = speed;
            if (!Application.isMobilePlatform && Input.GetKey(KeyCode.LeftAlt))
            {
                speed = Mathf.Min(speed, walkSpeed * MoveSpeedMultiplier);
                speedCap = Mathf.Min(speedCap, walkSpeed * MoveSpeedMultiplier);
            }
            // 室内只走不跑：在自己家里冲刺既不合情理，也是"转一圈就晕"的一部分——
            // 屋里两三步一堵墙，全速跑动时镜头与碰撞都来不及跟上。
            // 唯一的例外由 IndoorZone.SetRunPass 开出来：健身房的跑步机就是用来跑的，
            // 走速正好等于履带速度，不许跑等于那台机器是坏的。
            if (WalkOnly)
            {
                speed = Mathf.Min(speed, walkSpeed * MoveSpeedMultiplier);
                speedCap = Mathf.Min(speedCap, walkSpeed * MoveSpeedMultiplier);
            }
            if (IndoorPace)
            {
                speed = Mathf.Min(speed, indoorPaceSpeed * MoveSpeedMultiplier);
                speedCap = Mathf.Min(speedCap, indoorPaceSpeed * MoveSpeedMultiplier);
            }
            if (IsCrouched) { speed *= crouchSpeedMult; speedCap *= crouchSpeedMult; }

            // ===== 速度不许落进"任何片段都演不出来"的那一段 =====
            //
            // 【玩家的原话："偶尔突然出现一会脚不在地上走路的动画，完全一动不动，
            //   像鬼一样漂移起来"。这就是它。】
            // 实机日志把它量清楚了：19 段、累计 4.5 秒、最长 0.60 秒，
            // **全部发生在摇杆轻推（0.10~0.30）的时候**：
            //     摇杆 0.12 ⇒ 速度 0.36 m/s ⇒ 移动层权重 0.20（80% 在播站立待机）
            //                ⇒ 步频 0.17 周期/秒，也就是**六秒才迈一步**
            // 人在滑，腿基本不动——正是"像鬼一样漂移"。
            //
            // 根因不是权重算错，是**库里根本没有这么慢的片段**：最慢的
            // Walking 实测自然速度 2.05 m/s。低于它只能靠两件事凑：往待机混，
            // 以及把片段放慢。两件事都在把腿"停下来"，凑得越多越像鬼。
            //
            // 所以摇杆的映射要避开这一段：推得动就至少给 MinLocomotionSpeed，
            // 推不动就干脆不动（下面那条死区）。1.3 m/s 是 Walking 的 0.63 倍速，
            // 腿仍然在实打实地迈步，读作"慢慢走"，而不是"站着滑过去"。
            // 代价是没有 0.3 m/s 那档微调——但那一档本来就是不可用的：
            // 它在画面上不是"走得很慢"，是"没在走"。
            if (inputMag > StickDeadZone)
                speed = Mathf.Max(speed, Mathf.Min(MinLocomotionSpeed * MoveSpeedMultiplier,
                                                   speedCap));
            else speed = 0f;
            // ===== 锁定期间是【步法】，不是冲刺 =====
            //
            // 锁定时角色横穿画面而不是走向画面深处，同样的米每秒在观感上要快出一截；
            // 而 runSpeed=5.2m/s 本来就是冲刺档（≈18.7km/h）。两者叠加，
            // 就是"移动非常快、不像在操纵一个人"。所以锁定期间整体降到步法档。
            //
            // 封顶值不是拍的，是照着 CI 实测的片段自然速度倒推的
            //（搜构建日志里的 [CIDIAG][移动]），保证任何方向的播放速率都在 2.0 以内，
            // 也就是**任何方向都不打滑**：
            //     正前 45° 内 → 3.77m/s：斜前慢跑片段 2.83 ⇒ 速率 1.33
            //     正侧  90°   → 2.60m/s：横移走片段  1.70 ⇒ 速率 1.53
            //     正后 180°   → 2.08m/s：后退走片段  1.12 ⇒ 速率 1.86
            // 想全速跑就按锁定键解除锁定——那是玩家自己的决定，不该由系统替他做。
            // 将来补上跑动版横移（Left/Right Strafe）后，侧向这一档可以同步放开。
            if (face != null && moveDir.sqrMagnitude > 0.01f)
            {
                float sideAng = Mathf.Abs(
                    Vector3.SignedAngle(transform.forward, moveDir, Vector3.up));
                float cap;
                // ===== 封顶按【库里这个方向最快的片段】反推，不再手写死表 =====
                // 原来那三行是常数表（walkSpeed×1.45 / ×1.0 / ×0.8），与素材无关。
                // 而库里明明已经有跑档横移（Left/Right Strafe）与跑档后退
                // （Running Backward），封顶却照旧按走档给——一进战斗就整整慢一倍，
                // 这正是"移动速度显然被放慢了"里锁定那一路的份额。
                // 改成：封顶 = 该方向最快片段的自然速度 × StrideRateCap。
                // StrideRateCap 是可接受的步幅拉伸上限（1.35≈脚还踩得实的极限），
                // 于是"不打滑"这条约束由**实测**给出，库里加了更快的片段自动放开。
                float natAt = _anim != null
                    ? _anim.NaturalSpeedAt(Vector3.SignedAngle(
                        transform.forward, moveDir, Vector3.up))
                    : 0f;
                if (natAt > 0.05f) cap = natAt * StrideRateCap;
                // 兜底（这个方向压根没片段）：退回原来的常数表
                else if (sideAng <= 45f) cap = walkSpeed * 1.45f;              // 正面推进
                else if (sideAng <= 90f)
                    cap = Mathf.Lerp(walkSpeed * 1.45f, walkSpeed * 1.0f,
                                     (sideAng - 45f) / 45f);                  // 斜向→正侧
                else
                    cap = Mathf.Lerp(walkSpeed * 1.0f, walkSpeed * 0.8f,
                                     (sideAng - 90f) / 90f);                  // 正侧→后撤
                speed = Mathf.Min(speed, cap * MoveSpeedMultiplier);
                DbgStrafeCap = cap * MoveSpeedMultiplier;
            }
            // 出招定步（平滑化）：攻击动画占据全身，照常位移会读作"脚不动人在滑"。
            // 但此前用【硬性 ×0.1】会造成速度震荡——推着摇杆连打时，每一段出招速度
            // 从全速骤降到 10%、收招again骤升回全速，配合已提速的加减速就是一顿一顿的
            // 抽搐感。改为对倍率本身做时间常数 ≈0.07s 的平滑过渡，并把下限抬到 0.3：
            // 既保留定步的分量感，又让"边推杆边连打"是一条连续的速度曲线。
            // 【0.3 这个下限只对"能只动上半身"的招成立】
            // 挥砍/突刺/直拳这类上半身发力的招，腿归移动层，人边走边打是对的。
            // 但腿法、旋身横扫、跃劈这些**腿本身就是招式主体**的招，遮罩帮不上忙：
            // 腿在演旋身，人却以 1.26 m/s 平移，就是玩家说的"没有脚步动画的漂移"。
            // 这类招必须真定步——脚踩在哪儿，人就停在哪儿。
            // 0.06 而不是 0：留一点点让"边打边微调站位"仍有反馈，但已经看不出滑。
            bool wholeBody = attacking && _anim != null &&
                             !Combat.HumanoidAnimator.IsUpperBodyAction(_anim.CurrentPose);
            float attackFloor = wholeBody ? 0.06f : 0.3f;
            _attackSpeedFactor = Mathf.MoveTowards(_attackSpeedFactor,
                attacking ? attackFloor : 1f, dt / 0.07f);
            speed *= _attackSpeedFactor;
            DbgAttackFactor = _attackSpeedFactor;
            DbgFinalSpeed = speed;

            // 土狼时间（coyote time）：离开地面边缘后仍有一小段时间可以起跳——
            // 玩家的意图是"我在跳台边缘按了跳"，而不是"我晚了两帧所以活该掉下去"
            if (_cc.isGrounded) { _vy = -1f; _coyoteT = CoyoteTime; }
            else { _vy += gravity * dt; _coyoteT -= dt; }

            // 跳跃：缓冲 + 土狼时间——落地前一瞬按下也能在落地帧兑现
            if (_coyoteT > 0f && _inputBuf.TryConsume("Jump", JumpBufferWindow))
            {
                if (IsCrouched) ToggleCrouch();
                _vy = jumpForce;
                _coyoteT = 0f;
                // 跳跃是连招元素，不是移动的附属品：起跳即入融合链，
                // 于是「跳→剑」「跳→重→剑」这些串法能被识别成招（踏空斩/踏空三叠）。
                if (_pcc != null) _pcc.Fusion.Push(Combat.MoveToken.Jump);
            }

            // 翻滚闪避（Shift / 闪）——走缓冲：硬直/翻滚中按下的闪避会在此兑现
            if (dodgePressed && Stats.SpendStamina(dodgeStaminaCost))
            {
                _inputBuf.Consume("Dodge");
                if (IsCrouched) ToggleCrouch();
                // 闪避取消：切断进行中的轻连段（清序列/收判定框），翻滚落地即可全新起手。
                // 注意——切断的是拳剑序列，**融合链不断**：闪避本身作为元素留在链上，
                // 所以「闪→剑」能接成闪身突刺，而不是把整段努力清零。
                var pcc = _pcc != null ? _pcc : GetComponent<Combat.PlayerCombatController>();
                if (pcc != null)
                {
                    pcc.CancelComboForDodge();
                    pcc.Fusion.Push(Combat.MoveToken.Dodge);
                }
                _dodgeDir = moveDir.sqrMagnitude > 0.01f ? moveDir : transform.forward;
                // 【方向闪避】锁定目标时不转身滚出去，而是按摇杆方向做左闪 / 右闪 / 后撤步——
                // 这是锁定战的标准做法：面向敌人向左推杆按闪避，人往左侧闪，眼睛始终盯着他。
                // 非锁定时仍是前滚翻（朝哪滚就朝哪转身，那时转身是对的）。
                var dodgePose = PoseState.Dodge;
                if (_anim != null && StrafeActive && moveDir.sqrMagnitude > 0.01f)
                {
                    float dodgeAng = Vector3.SignedAngle(transform.forward, moveDir, Vector3.up);
                    float absAng = Mathf.Abs(dodgeAng);
                    if (absAng > 135f) dodgePose = PoseState.StepBack;
                    else if (absAng > 45f) dodgePose = dodgeAng > 0f ? PoseState.DodgeRight : PoseState.DodgeLeft;
                    if (!_anim.HasPose(dodgePose)) dodgePose = PoseState.Dodge;
                }
                if (dodgePose == PoseState.Dodge) transform.rotation = Quaternion.LookRotation(_dodgeDir);
                if (_anim != null) _anim.DodgePose = dodgePose;
                Core.GameAudio.Play(Core.GameAudio.Sfx.Dodge, 0.7f);
                // 有专用翻滚片段时：闪避时长匹配片段（完整呈现整个滚翻动作），
                // 总位移保持恒定（速度反比时长），无片段沿用默认参数
                // 翻滚时长：**不再迁就片段长度**。
                // 之前是 clip×0.85 夹到 [0.42,0.70]——一段 0.8 秒的滚翻片段会让翻滚
                // 整整锁 0.68 秒，比大作里的翻滚（0.35~0.5s）慢一半，敌人一套连招
                // 打完你还在地上滚。现在固定夹到 [0.30,0.42]，片段由 PlayableAnimator
                // 按时长驱动加速播完（本作动作系统本来就是时长驱动的），
                // 动作照样完整演，只是演得跟得上战斗节奏。
                float dur = dodgeDuration;
                _dodgeSpd = dodgeSpeed;
                if (_anim != null)
                {
                    float clipLen = _anim.ActionClipLength(dodgePose);
                    if (clipLen > 0.1f)
                    {
                        dur = Mathf.Clamp(clipLen * 0.55f, 0.30f, 0.42f);
                        _dodgeSpd = dodgeSpeed * dodgeDuration / dur;   // 位移总量不变
                    }
                }
                _dodgeDur = dur;
                _dodgeTimer = dur;
                if (_anim != null) _anim.DodgeDuration = dur;   // 动画按同一时长播完
                // 无敌帧覆盖滚动主体（72%），只留收势尾巴可被打中：
                // "读招成功却还是被打到"必须是玩家读错了，而不是系统没给够帧。
                _iframeTimer = Mathf.Max(dodgeIFrames, dur * IFrameRatio);
                if (_combat != null) _combat.RequestState(CombatState.Dodge);
                return;
            }

            // 【跑动转身已移除】上一轮我把 Left/Right Turn 90 / Quick 180 Turn
            // 接成"跑动中的插步转身"，做法是刹住速度 + 播一段转身**动作**。
            // 但动作层是**盖在移动层上面**的：那一段里整个方向混合被替换成一段
            // 站姿转身片段，而位移仍在继续——画面上就是"突然漂移到别处，然后才
            // 继续朝原方向走"，也就是你说的抑扬顿挫、多个动画在打架。
            // 移动过渡属于移动状态机内部，不能盖在移动层之上。撤销。

            // ===== 顺序：先转向，再决定位移方向，最后位移 =====
            // 原来是先位移后转向，而位移方向直接取摇杆、与朝向无关——那正是
            // "被摇杆拖着走"的结构性来源（见下面两段根因说明）。
            float yawBefore = transform.eulerAngles.y;
            if (face != null)
            {
                Vector3 toT = face.position - transform.position; toT.y = 0;
                if (toT.sqrMagnitude > 0.04f)
                {
                    Quaternion look = Quaternion.LookRotation(toT.normalized);
                    // 对目标的朝向跟随要快而稳：敌人绕到侧面时脸要跟着转，
                    // 但不允许一帧甩过去（那会把画面也一起甩）。
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, look,
                        (attacking ? AttackSteerDegPerSec : FaceTargetDegPerSec) * dt);
                }
            }
            else if (moveDir.sqrMagnitude > 0.01f)
            {
                Quaternion target = Quaternion.LookRotation(moveDir);
                if (attacking)
                {
                    // 轻击连段中的转向影响：用明确的角速度上限（度/秒）而非低倍率 Slerp——
                    // 旧值 2.2 的 Slerp 每帧只挪动 3.7%，推杆几乎转不动，读作"出招就僵住"。
                    // 150°/s 既能顺着摇杆把连招引向新方向，又不会一帧甩离当前目标。
                    transform.rotation = Quaternion.RotateTowards(transform.rotation,
                        target, AttackSteerDegPerSec * dt);
                }
                else
                {
                    // 【根因一：转向根本没有角速度上限】
                    // 原来这里是
                    //     rs = rotateSpeed * (ang > 80 ? quickTurnMultiplier : 1);
                    //     rotation = Quaternion.Slerp(rotation, target, rs * dt);
                    // Slerp 的第三个参数是**比例**，不是角速度。60fps 下
                    // rotateSpeed=14 ⇒ 每帧吃掉剩余角度的 23%；大角度时
                    // quickTurn=2.1 ⇒ **每帧吃掉 49%**。
                    //
                    // 后果不是"转得快"，而是**转多远都花一样的时间**（指数收敛）：
                    // 实测转到 1° 以内，90° 要 0.267s、180° 要 0.283s——差别只有 6%。
                    // 真实的身体转两倍的角度要花两倍的时间；而这套写法里，
                    // 身体只是在"追踪摇杆当前指向"，它没有自己的转速。
                    // 这正是"完全是被摇杆拖动的"在物理上的定义。
                    //
                    // 改后（角速度上限）：站定 90°/180° = 0.15s / 0.25s，
                    // 全速 = 0.35s / 0.60s——与角度成正比，且跑得越快转得越慢。
                    //
                    // 讽刺的是：同一个方法里，攻击分支与上面的锁定分支**早就**用的是
                    // 正确的角速度上限（RotateTowards + 度/秒），攻击分支的注释还写着
                    // "用明确的角速度上限（度/秒）而非低倍率 Slerp"——
                    // 唯独最常用的自由移动分支漏在外面。
                    //
                    // 改为真正的角速度上限，且**随速度递减**：站着可以原地快转，
                    // 全速时转向必须走出半径。跑步的惯性感就来自这条。
                    // ===== 转向上限由【横向加速度】反推，不再是拍出来的常数 =====
                    //
                    // 我此前一直凭感觉挑角速度（620/260、720/300），却从没算过
                    // 它对应的横向加速度。实测 327°/s @ 4.9m/s 意味着：
                    //     半径 = 4.9 / (327°/s) = 0.86m
                    //     横向加速度 = v²/r = 28 m/s² ≈ **2.9g**
                    // 人类跑动急转最多 1g 上下。所以即便速度恒定、位移也沿正面，
                    // 角色仍在做物理上不可能的事——用 4.9m/s 绕 0.86m 的圈、一秒一圈，
                    // 看上去就是被甩着转的陀螺。何况我们没有"跑动转弯"的动画
                    //（无倾身、无转向混合），身体高速自转、腿演直线跑循环，
                    // 正是"看不到自己的移动节奏"。
                    //
                    // 正确的物理关系是 a = ω·v ⇒ **ω_max = a_max / v**：
                    // 速度越高越转不动，转弯半径 r = v²/a 恒定。
                    //     v=1.0 → 917°/s (r 0.06m)    v=3.8 → 241°/s (r 0.90m)
                    //     v=2.6 → 353°/s (r 0.42m)    v=5.2 → 176°/s (r 1.69m)
                    // 低速端由 turnDegPerSecStill 封顶，站定原地转身照样干脆。
                    // 【分母要有地板】ω = a/v 这条关系本身是对的，但它有个副作用：
                    // 速度一降，上限就升。上一轮我因此撤掉了"大转向减速"那一层——
                    // 减速 → 上限升 → 转更快 → 待转角不减，发散成陀螺。
                    //
                    // 断开这个回路不需要放弃减速，只需要**让分母停在某个速度以下不再变小**：
                    // 低于 TurnCapSpeedFloor 之后上限恒为一个常数，减速再多也不会让它继续升。
                    // 于是"大转向先减速"这条真人的转法可以放心用回来（见下面的 turnEase）。
                    float sp = _hVel.magnitude;
                    float capV = Mathf.Max(sp, TurnCapSpeedFloor);
                    float cap = Mathf.Min(turnDegPerSecStill, TurnAccel / capV * Mathf.Rad2Deg);

                    // ===== 方向意图置信度：输入不成向就不转身 =====
                    //
                    // 这是成熟动作游戏移动层的第三条规则，也是本项目**唯独角色这边缺失**
                    // 的一条——ThirdPersonCamera 里早就有同一套判据：
                    //     orbit *= dirTrust;   // "搓杆则趋零"
                    // 镜头知道"每帧都在变的方向不构成意图"，角色控制器却把摇杆方向
                    // 当帧直接当作朝向目标，摇杆转多快就试图转多快——于是成了陀螺。
                    //
                    // 魂系/怪猎那一类里搓杆转圈，角色**不会跟着转**：它保持大致朝向
                    // 继续跑。因为一个方向要先被确认成"玩家真的想去那边"，才配当目标。
                    //
                    // 判据与镜头侧完全一致（方向向量的滑动平均，相反方向互相抵消）：
                    // 稳定推一个方向 ⇒ 模长→1 ⇒ 满速转向；搓杆 ⇒ 模长→0 ⇒ 几乎不转。
                    // 地板 0.15 → 0.35：0.15 意味着置信度低时转速只剩 15%，而
                    // **推杆换向的那一刻恰恰是置信度最低的时候**（滑动平均还没追上
                    // 新方向）——等于在最需要转的一瞬间把转速砍到七分之一。
                    // 玩家 HUD 里读到的「身66°/s 意图0.45 待转126°」就是这么来的。
                    // 防搓杆的职责由平均本身承担（搓杆时模长趋零、置信度趋零），
                    // 地板只决定"最坏情况下还剩多少"，0.35 仍然远低于满速跟随。
                    float trust = DirTrust(moveDir, dt);
                    cap *= Mathf.Lerp(0.35f, 1f, trust);

                    // ===== 上限只许缓慢上升，可以立刻下降 =====
                    //
                    // 【实机逐帧抓到的那一下】玩家用拇指画圈（stickYaw 1.4 秒转了
                    // 320°，而 camYaw 只动了 3°——是手在转，不是镜头）：
                    //     69.87~70.20  意图0.00  身113°/s  待转 118→158°
                    //     70.33~70.60  意图0.60→1.00  身238→322°/s
                    // 防搓杆滤波先把画圈判成"无意图"、转速砍到 113°/s，角色落后
                    // 158°；接着滤波在 **0.27 秒内**从 0.00 恢复到 0.97，转速
                    // 猛甩到 322°/s——角加速度约 700°/s²。那一甩配 1.6m/s 的速度，
                    // 转弯半径只有 0.28m：人在原地自转，而腿一直在演正前方走。
                    // 玩家说的"原地漂移、脚步没有转身动作"就是这一下。
                    //
                    // 修的不是上限的**值**，是它的**变化率**：升要慢（<=400°/s²，
                    // 低于镜头那边用的 600°/s² 舒适上限），降可以立刻。
                    // 减速从来不会被读成甩，只有加速会。
                    if (cap > _turnCapSm)
                        _turnCapSm = Mathf.Min(cap, _turnCapSm + TurnCapRise * dt);
                    else _turnCapSm = cap;
                    cap = _turnCapSm;
                    DbgLateralG = sp * cap * Mathf.Deg2Rad / 9.81f;
                    transform.rotation = Quaternion.RotateTowards(transform.rotation,
                        target, cap * dt);
                }
            }

            // ===== 速度矢量跟着身体一起转（这才是"转向时漂移"的根因）=====
            //
            // 位移方向取的是 transform.forward，这一步是对的；错在**速度矢量本身**
            // 是世界坐标下的 _hVel，而它只通过一阶低通去追新方向：
            //     _hVel = Lerp(_hVel, forward*speed, 1−e^(−20·dt))   ⇒ τ = 0.05s
            // 身体以 720°/s 转，0.05 秒就是 **36° 的方向滞后**。
            // 滞后的那 36° 全部是相对身体的【横向分量】：
            //     横向速度 = sin36° × v = 0.59v
            // 也就是说急转的头几帧里，人有近六成的速度是**横着滑出去**的，
            // 而腿在演的是正前方跑动循环。屏幕上就是"不是一步一个脚印转的，
            // 而是快速大步漂移"。这跟动画一条都不缺没有关系——是速度矢量在滑。
            //
            // 修法不碰速度大小（你要求任何时候都不许快进或放慢，这条我记着）：
            // 身体这一帧转了多少度，就把 _hVel **刚性地跟着转多少度**。
            // 于是速度方向恒等于身体正面、横向分量恒为 0，低通只作用在【模长】上
            // ——加减速依旧平滑，方向不再有滞后。
            // 这等价于大作里把移动速度存成"沿朝向的标量"而不是世界矢量。
            // 锁定横移时不适用：那时位移方向本来就该独立于朝向。
            if (!StrafeActive)
            {
                float spun = Mathf.DeltaAngle(yawBefore, transform.eulerAngles.y);
                if (Mathf.Abs(spun) > 0.0001f)
                    _hVel = Quaternion.Euler(0f, spun, 0f) * _hVel;
            }

            // ===== 位移方向：永远沿身体正面，速度恒定 =====
            //
            // 实测证明这条是对的：改成 velDir = forward 之后，
            // **夹角从 75~80° 降到 20~29°**——位移方向与身体正面基本一致，
            // 横着滑行的现象消失了。
            //
            // 【但我在它上面加的"大转向降速"是错的，已删除】
            // 那一层造成了恶性循环：待转角大 ⇒ 速度压到 15% ⇒ 速度低 ⇒
            // 转向上限反而升高（cap 随速度递减）⇒ 身体转得更快 ⇒ 待转角依旧大。
            // 实测结果是【目标速度 0.6m/s、身体 784°/s】——人几乎不动，
            // 却像陀螺一样每秒转两圈，比改之前更糟。
            // 我当时称它"自稳"，那是错的：摇杆一直在动，这个环根本不收敛。
            //
            // 现在的规则只有两条，都不带任何速度调制：
            //   · 位移方向 = 身体正面（横滑消失）
            //   · 速度 = 命令速度，恒定不变（不快进、不放慢）
            // 转向快慢完全交给下面的角速度上限，速度不再参与其中。
            // 于是搓杆 = 以恒定速度沿一个半径 v/ω 的圆跑，脚步频率与地面速度
            // 始终匹配——这才是"正常移动"。
            Vector3 velDir = StrafeActive || moveDir.sqrMagnitude <= 0.01f
                ? moveDir : transform.forward;
            float needDeg = moveDir.sqrMagnitude > 0.01f
                ? Vector3.SignedAngle(transform.forward, moveDir, Vector3.up) : 0f;
            DbgTurnNeed = Mathf.Abs(needDeg);

            // ===== 转急弯要先减速——这是"推杆转向就漂移"的直接成因 =====
            //
            // 【玩家截图里的 HUD 就是证据】
            //     待转126°   身66°/s   半径3.6m   夹角0°   命令4.1→实际4.2m/s
            // 待转 126° 而身体只以 66°/s 转，126÷66 ≈ **1.9 秒**：推杆之后角色
            // 还要沿**旧方向**全速走将近两秒，同时画一个 3.6 米半径的弧才转过来。
            // 夹角 0° 说明腿一直在演"正前方走"——腿没错，是人根本没往你指的方向去。
            // 玩家读到的就是"推杆转向、360 转圈时漂移"。
            //
            // 半径 r = v²/a 是死的：速度不降，弧就小不了。真人转急弯的做法是
            // **先减速、拧身、再加速出弯**，也就是玩家第一条反馈里的原话
            // "一步一个脚印转"。所以按待转角把速度压向一个"转身速度"：
            //     45° 以内   不减速（顺势的小修正）
            //     110° 以上  压到 PivotSpeed，半径 v/ω ≈ 0.3m，就地拧过去
            // 中间连续过渡，没有换挡感。
            //
            // 上一轮撤掉这层是因为它与 ω=a/v 构成正反馈；那个回路已经在上面用
            // 分母地板断开了（TurnCapSpeedFloor），这里可以放心减速。
            // 【只对"换个方向"减速，不对"一直在转"减速】
            // 上一版我漏了这个区分，结果画圈时角色既减速又被上面那一甩带着转，
            // 弧线被压成原地自转——正是玩家报的"360 转圈就漂移"，是我改坏的。
            //   · 推向一个新方向然后稳住（意图→1）⇒ 真的要转弯 ⇒ 减速拧过去；
            //   · 拇指一直在画圈（意图→0）      ⇒ 他要的是跑一个圈 ⇒ 保持速度，
            //     让半径 v²/a 自然张开，而不是缩到 0.3m 原地转。
            if (DbgTurnNeed > SharpTurnDeg && !StrafeActive)
            {
                float turnEase = Mathf.InverseLerp(SharpTurnDeg, HardTurnDeg, DbgTurnNeed)
                                 * DbgDirTrust;
                speed = Mathf.Lerp(speed, Mathf.Min(speed, PivotSpeed * MoveSpeedMultiplier),
                                   turnEase);
            }



            // 加减速曲线：改用【指数逼近】而非线性匀加速——对齐 Unity 官方
            // ThirdPersonController 的做法（其注释原文：curved result rather than a
            // linear one giving a more organic speed change）。
            // 起步瞬间给足冲量（前几帧就到大半速度＝跟手），末段自然收敛（不突兀）。
            // 用 1-e^(-k·dt) 而非 Lerp(a,b,k·dt)：帧率无关，高低帧手感一致。
            // ===== 起步对齐闸门：静止起步先转到位，再迈步 =====
            //
            // "停下之后朝前起步会先向后漂一段"——这里是它的另一半（前一半是
            // Start Walking 被当动作播，已撤销）。站定时身体还朝着上一次的方向 A，
            // 你把杆推向 B；而 velDir 取的是 transform.forward，于是速度是沿着
            // **A** 建立起来的，身体要 |A−B|/720°/s 才转到 B。
            // 180° 掉头就是 0.25 秒，accelRate=20 下这 0.25 秒足够跑出半米——
            // 半米的反向位移，正是你看到的那一段后退。
            //
            // 【这不是"降速"，和上一轮翻车的那一层是两回事，必须分清】
            // 上一轮我按待转角连续地乘一个速度系数，结果与转向上限 ω=a/v 构成
            // 正反馈（速度越低转得越快、待转角却不减），发散成陀螺。
            // 这里是【静止时的一次性闸门】：v≈0 时转向上限恒为 turnDegPerSecStill，
            // **压根不含 v**，没有回路；而且闸门一开就再也不参与。
            // 最长延迟 180°/720°/s = 0.25s，观感是"先转身再迈步"，
            // 也就是你要的一步一个脚印，不是变慢。
            bool standingStart = _hVel.magnitude < walkSpeed * 0.25f;
            if (standingStart && !StrafeActive && DbgTurnNeed > StartAlignDeg) speed = 0f;
            DbgStartGate = standingStart && DbgTurnNeed > StartAlignDeg;

            Vector3 targetVel = velDir * speed;
            float k = targetVel.sqrMagnitude > _hVel.sqrMagnitude ? accelRate : decelRate;
            _hVel = Vector3.Lerp(_hVel, targetVel, 1f - Mathf.Exp(-k * dt));
            CarryByPlatform();          // 站在会动的东西上（车顶等）要跟着它走
            FallGuard();                // 掉出世界的兜底捞回
            // 分步位移：掉帧时 dt 可达 0.1s 以上，配冲刺速度一次 Move 就是半米开外，
            // 远超胶囊半径 —— 薄墙会整个落在扫掠的两次采样之间。**这是"卡顿时穿墙"
            // 的直接路径**，而且掉帧越厉害越容易穿。上一轮只给突进技能加了分步，
            // 漏掉了这条每帧都在走的主路径。
            // 【诊断】把"指令"与"实际"分开记：目标 5.2 而实测位移只有 2.0 时，
            // 丢失的那一截要么是速度矢量本身没建立起来（看 DbgVel），
            // 要么是位移被碰撞吃掉了（看 DbgHitSides）。两者的修法完全不同。
            DbgTargetVel = targetVel.magnitude;
            DbgVel = _hVel.magnitude;
            Combat.CharacterMotion.StepMove(_cc,
                _hVel * dt + Vector3.up * _vy * dt + TakeExternalMove());
            DbgHitSides = (_cc.collisionFlags & CollisionFlags.Sides) != 0;

            // 嵌墙兜底：贴着墙推+高速转向那一档，分步扫掠防不住（那时单帧位移
            // 只有几厘米，分步根本不触发）。每帧把嵌进环境的部分推出来，
            // 嵌得比半径还深就回滚到上一个确认安全的位置。见 ResolvePenetration。
            _safePos = Combat.CharacterMotion.ResolvePenetration(_cc, _safePos, _hasSafePos);
            _hasSafePos = true;
        }

        /// <summary>诊断：这一帧的目标速度矢量模长（m/s）。</summary>
        public float DbgTargetVel { get; private set; }
        /// <summary>诊断：平滑后实际下发给 Move 的速度矢量模长（m/s）。</summary>
        public float DbgVel { get; private set; }
        /// <summary>诊断：这一帧 CharacterController 有没有撞到侧面（墙）。</summary>
        public bool DbgHitSides { get; private set; }
        /// <summary>诊断：战斗状态机当前状态，以及它是不是【硬锁】。
        /// 硬锁时移动方法在最开头就 return——转向照跑、水平位移归零，
        /// 也就是说本文件下面所有的移动代码一行都不执行。</summary>
        public string DbgCombatState => _combat != null ? _combat.Current.ToString() : "无";
        public bool DbgHardLocked => _combat != null && _combat.IsHardLocked;
        /// <summary>诊断：摇杆方向与身体正面的夹角（度），也就是"还要转多少"。</summary>
        public float DbgTurnNeed { get; private set; }
        /// <summary>诊断：当前转向上限对应的横向加速度（g）。超过 1g 就不像人在跑。</summary>
        public float DbgLateralG { get; private set; }
        /// <summary>诊断：这一帧起步对齐闸门有没有拦住位移（静止起步先转身）。</summary>
        public bool DbgStartGate { get; private set; }

        /// <summary>诊断：最近 6 秒内最大的单帧水平位移（米）与它发生在多久之前。
        /// 正常跑动 60fps 下是 0.09m；读到 0.8m 就是有别的东西在写位置。</summary>
        public float DbgMaxStep { get; private set; }
        public float DbgStepAge { get; private set; }
        /// <summary>诊断：方向意图置信度（0~1）。搓杆时趋 0，稳定推杆时趋 1。</summary>
        public float DbgDirTrust { get; private set; } = 1f;

        Vector2 _dirMean;
        float _prevDirYaw;
        bool _dirMeanInit;


        /// <summary>方向向量的滑动平均速率——与 ThirdPersonCamera.DirMeanRate 同值，
        /// 两层对"什么才算一个方向"必须用同一把尺子。</summary>
        const float DirMeanRate = 2.2f;

        /// <summary>
        /// 「这个方向有多像玩家真的想去的方向」。做法与镜头侧一致：把方向当成单位向量
        /// 做滑动平均，搓杆时相反方向互相抵消、模长趋零；稳定推一个方向则趋一。
        /// 0.55 起步、0.85 满信任（同镜头侧口径）。
        /// </summary>
        float DirTrust(Vector3 dir, float dt)
        {
            if (dir.sqrMagnitude < 0.01f)
            {
                _dirMeanInit = false;
                return DbgDirTrust = 1f;   // 没在推杆：不参与判定，别影响站定转身
            }
            Vector3 n = dir.normalized;
            Vector2 v = new Vector2(n.x, n.z);
            float yaw = Mathf.Atan2(n.x, n.z) * Mathf.Rad2Deg;
            if (!_dirMeanInit) { _dirMeanInit = true; _dirMean = v; _prevDirYaw = yaw; }

            // 【必须区分"掉头"与"搓杆"，否则会误伤真正的换向】
            // 单纯的滑动平均有个数学上的坑：180° 掉头时平均向量会**经过原点**，
            // 模长归零 ⇒ 被判成搓杆 ⇒ 玩家真想掉头时反而转不动。
            // 两者的可分特征是【持续性】：搓杆是一直在转，掉头是拨一下就停。
            // 所以让平均的收敛速率随瞬时角速度变化：
            //   · 拨完就稳住（瞬时速率低）⇒ 快速收敛(9)，掉头只被压住 <0.1s；
            //   · 一直在转（瞬时速率高）⇒ 慢速平均(2.2)，正反方向被充分抵消。
            float inst = Mathf.Abs(Mathf.DeltaAngle(_prevDirYaw, yaw)) / Mathf.Max(dt, 1e-4f);
            _prevDirYaw = yaw;
            float k = Mathf.Lerp(9f, DirMeanRate, Mathf.Clamp01(inst / 300f));
            _dirMean = Vector2.Lerp(_dirMean, v, 1f - Mathf.Exp(-k * dt));
            return DbgDirTrust = Mathf.Clamp01((_dirMean.magnitude - 0.55f) / 0.30f);
        }



        /// <summary>交战中锁面向的角速度上限（度/秒）：跟得住绕后，又不会甩镜。</summary>
        const float FaceTargetDegPerSec = 420f;

        /// <summary>横移态（脸锁在目标上、移动方向独立）——镜头与动画都要知道。</summary>
        public bool StrafeActive { get; private set; }

        // ===== 移动诊断读数（PerfHud 用；不参与任何逻辑）=====
        /// <summary>行动力倍率（0.65~1）。行动力过低会压移速，这是设计内的机制。</summary>
        public float DbgApMult { get; private set; } = 1f;
        /// <summary>本帧摇杆幅度。</summary>
        public float DbgInputMag { get; private set; }
        /// <summary>各层封顶之前的原始目标速度。</summary>
        public float DbgRawSpeed { get; private set; }
        /// <summary>锁定横移时的方向封顶（0=本帧没有封顶）。</summary>
        public float DbgStrafeCap { get; private set; }
        /// <summary>出招定步倍率（0.3~1）。卡在 0.3 就是"永远只有三成速度"。</summary>
        public float DbgAttackFactor { get; private set; } = 1f;
        /// <summary>所有倍率乘完之后、真正喂给加减速的目标速度。</summary>
        public float DbgFinalSpeed { get; private set; }
        /// <summary>诊断：喂给动画层的行进夹角（度）。恒 ≈0 ⇒ 方向片段永远轮不到。</summary>
        public float DbgMoveAngle { get; private set; }
        /// <summary>诊断：由真实位移测出的地面速度（m/s），不是目标速度。</summary>
        public float DbgActual { get; private set; }

        /// <summary>
        /// 当前该锁面向谁——**只认玩家自己按下的锁定**（Q 键 / 触屏「锁」按钮）。
        ///
        /// 【为什么不再自动锁】
        /// 上一版做成了"交战中且有敌人在 7 米内就自动锁面向"。而 InCombat 是
        /// 「出过一次手后 4 秒内为真」——打起来之后它几乎恒为真，于是软锁全程开着，
        /// 玩家**从没要求过**却整场都在横移。
        ///
        /// 后果不是"多了个功能"，是**手感没了**：
        /// 推杆最直接的反馈就是"角色转过去朝我指的方向"，那是玩家确认"是我在操纵它"
        /// 的唯一凭据。锁面向把这个反馈拿掉之后，摇杆只剩下平移向量，
        /// 角色读起来像被拖着走；再叠上锁定机位（人横穿画面而不是往画面深处走），
        /// 同样的速度看着快得多——也就是"不受精细控制、移动非常快"。
        ///
        /// 横移/后撤这项能力本身是要的，但它必须是玩家**主动进入**的模式：
        /// 按锁定键才锁，再按一次解除。这也是这类游戏的通行做法。
        /// 想要自动锁的人，设置面板里本来就有「自动锁定」开关（LockOnSystem.AutoAcquire）。
        /// </summary>
        Transform FacingTarget()
        {
            if (_lockOn == null) _lockOn = GetComponent<LockOnSystem>();
            var hard = _lockOn != null ? _lockOn.CurrentTarget : null;
            if (hard != null) { _softFace = null; return hard; }
            return SoftFaceTarget();
        }

        // ===== 软锁面向：这才是"横移/后退动画放进去了却永远看不到"的原因 =====
        //
        // 链路是这样的（每一环都对，合起来就永远走不到）：
        //   StrafeActive = FacingTarget() != null
        //   ↓ 不横移时 velDir = transform.forward（身体永远朝着走的方向）
        //   ↓ moveAngle = 身体前方 与 实际位移 的夹角 ≡ 0
        //   ↓ 方向环按 moveAngle 混合 ⇒ 只有 0°（正前）那一条有权重
        //   ⇒ Left/Right Strafe、Walking Backwards、Jog Backward… 17 条里 13 条恒为 0
        // 而 FacingTarget 此前只认【硬锁】，硬锁又是手动按 Q／「锁」按钮才有的，
        // 且 LockOnSystem.AutoAcquire 默认 **0（关）**。
        // 也就是说：不手动按锁定键，横移与后退的片段一帧都不会出现。
        // 片段没问题、映射没问题、混合没问题——是闸门根本没人开。
        //
        // 补法用【软锁】：临战 + 身边 6 米内有活着的敌人 ⇒ 面向它、改走横移。
        // 这是对马岛/地平线那一路的做法（身体对敌、镜头不强制锁），
        // 比魂系"必须先按锁定"更适合触屏。三条克制：
        //   · 只在 CombatStateMachine.InCombat（最后一次战斗动作 4 秒内）才成立，
        //     否则路过一个敌人就被迫横着走；
        //   · 进 6m 出 8.5m 的回差，站在边界上不来回抖；
        //   · **背身全速跑＝脱离**：朝着背离目标 120° 以上且跑起来了就立刻放开，
        //     否则想撤退会变成倒着跑（锁定类系统最经典的手感事故）。
        const float SoftFaceEnter = 6f, SoftFaceExit = 8.5f;
        Transform _softFace;
        float _softFaceScan;

        /// <summary>附近（SoftFaceEnter 内）有没有正在交战的敌人——
        /// "交战"用与镜头侧一致的判据：状态不是待机、也不是巡逻。</summary>
        bool EnemyEngagedNearby()
        {
            foreach (var e in AdversityRoad.Core.ActorRegistry.Enemies)
            {
                if (e == null || e.State == AI.EnemyState.Dead) continue;
                if (e.State == AI.EnemyState.Idle || e.State == AI.EnemyState.Patrol) continue;
                Vector3 d = e.transform.position - transform.position; d.y = 0f;
                if (d.sqrMagnitude < SoftFaceEnter * SoftFaceEnter) return true;
            }
            return false;
        }

        Transform SoftFaceTarget()
        {
            // ===== 判据放宽：不再要求"四秒内出过招" =====
            //
            // 横移/后退这几条片段你已经问过四次"为什么没效果"。片段没问题、
            // 接线没问题（对齐表 15 条全在库），卡在这道闸门上：
            // 旧判据是 _combat.InCombat——**必须四秒内真的出过招**。于是
            // "走近敌人、绕着它找角度"这段最需要横移的时间里，闸门是关的；
            // 等你出了招，人又已经贴上去了。最近这份日志里 strafe 为 1 的帧是 0。
            //
            // 改成【敌人真的在打你】：附近有处于交战状态（非待机/巡逻）的敌人即可，
            // 不必等玩家先动手。这与镜头侧判"是否交战"用的是同一条判据
            // （见 ThirdPersonCamera 的 engaged），两边口径一致。
            // 出过招仍然算数（InCombat），只是不再是**必要**条件。
            if (_combat == null) { _softFace = null; return null; }
            if (!_combat.InCombat && !EnemyEngagedNearby()) { _softFace = null; return null; }

            // 已有软锁：先判脱离，再判回差
            if (_softFace != null)
            {
                var keep = _softFace.GetComponentInParent<AI.EnemyController>();
                Vector3 to = _softFace.position - transform.position; to.y = 0f;
                if (keep == null || keep.State == AI.EnemyState.Dead ||
                    to.magnitude > SoftFaceExit)
                    _softFace = null;
                else if (_hVel.magnitude > runSpeed * 0.5f && to.sqrMagnitude > 1e-4f &&
                         Vector3.Angle(_hVel.normalized, to.normalized) > 120f)
                    _softFace = null;   // 背身全速跑＝我要走，不是要打
                else return _softFace;
            }

            if (Time.time < _softFaceScan) return null;
            _softFaceScan = Time.time + 0.25f;
            float best = SoftFaceEnter;
            foreach (var e in AdversityRoad.Core.ActorRegistry.Enemies)
            {
                if (e == null || e.State == AI.EnemyState.Dead) continue;
                Vector3 d = e.transform.position - transform.position; d.y = 0f;
                float dist = d.magnitude;
                if (dist < best) { best = dist; _softFace = e.transform; }
            }
            return _softFace;
        }

        // ---- 出招转向影响（大作 attack steering）速率表（度/秒）----
        const float AttackSteerDegPerSec = 150f;   // 轻击连段：顺杆引导连招方向
        const float SkillSteerDegPerSec = 110f;    // 技能/绝招：可调整但保持"已出招"的承诺感
        const float HeavySteerDegPerSec = 85f;     // 重击/蓄力：最重，转向最慢

        /// <summary>
        /// 硬锁动作期间的摇杆转向（技能/绝招/重击）：按状态给不同角速度上限，
        /// 让「出招 + 转向」是一个连贯动作，而不是无响应后突然弹向新朝向。
        /// 受击/倒地/心理硬直/死亡不给转向——挨打就该失控，这是打击反馈的一部分。
        /// </summary>
        void SteerDuringAction(Vector3 dir, float dt)
        {
            if (_combat == null || dir.sqrMagnitude < 0.01f) return;
            float rate;
            switch (_combat.Current)
            {
                case CombatState.Finisher:
                case CombatState.InnerPowerCast:
                    rate = SkillSteerDegPerSec; break;
                case CombatState.HeavyAttack:
                    rate = HeavySteerDegPerSec; break;
                default:
                    return;   // HitReaction / Knockdown / MentalStagger / Death：不可转向
            }
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(dir), rate * dt);
        }

        void LateUpdate()
        {
            // 把实际位移换算成步态参数喂给人形动画
            if (_anim == null) _anim = GetComponent<HumanoidAnimator>();
            if (_anim == null) return;
            float dt = Mathf.Clamp(Time.deltaTime, 0.0001f, MaxSimStep);
            Vector3 planar = transform.position - _lastPos;
            planar.y = 0;
            // ===== 换算地面速度时，除数不能是顿帧那个极小的 dt =====
            //
            // 命中顿帧把 Time.timeScale 打到 0.07（CombatFeedback），于是
            // Time.deltaTime 只剩 1 毫秒。而这里是 位移 ÷ dt——分母一小，
            // 商就炸：实机日志里 actual 读到 57 m/s，最高 1327 m/s。
            // 全库 23.9% 的帧 timeScale < 0.9，这不是偶发。
            //
            // 后果全在动画上：actual 是喂给动画层的"地面速度"，它决定
            //   · 走/跑/冲刺档的混合权重 —— 直接跳到冲刺档；
            //   · 每条片段的播放速率（实际速度 ÷ 片段自然速度）—— 顶到 2.0 上限。
            // 也就是**顿帧的那零点几秒里，腿在以最高速率狂蹬**。
            // 玩家读到的就是"经常性滑动、像被动画控制"。
            //
            // 除数取 1/90 秒作地板：比任何真实帧都短，正常帧不受影响；
            // 顿帧时它把商压回真实量级（0.035m / 0.011s ≈ 3.1 m/s，
            // 而不是 0.035 / 0.001 = 35）。
            const float MinSpeedDt = 1f / 90f;
            float actual = planar.magnitude / Mathf.Max(dt, MinSpeedDt);
            float speed01 = Mathf.Clamp01(actual / Mathf.Max(0.1f, runSpeed));
            // 移动方向相对身体正面的夹角：0=正前、±90=横跨、180=后撤。
            // 动画层据此在方向片段之间混合（前/后/左/右/斜向）。
            //
            // 【为什么改成永远给真实夹角】
            // 上一版只在锁定横移态下才给，非锁定时硬写 0。理由写的是"非交战时身体
            // 本来就会转向行进方向，此刻的夹角只是转身还没转完的瞬时残差"。
            // 那个理由站不住，代价却极大：锁定是玩家手动按的，绝大多数时间没按，
            // 于是 **17 条方向片段里有 13 条永远轮不到播**——后退、左右横移、斜向
            // 全部形同虚设，画面上永远只有"向前走"。这正是"动作都接了却看不到效果"。
            //
            // 真实夹角本来就该一直给：身体转向行进方向要花零点几秒，那零点几秒里
            // 人的腿【本来就在做侧步/倒步】——真人掉头就是这么走的。让腿演出这一段
            // 是对的，不是瑕疵。当初担心的"闪一下"，根子在方向平滑太快（900°/s
            // 几乎等于不平滑），那是 PlayableAnimator 那边的事，已一并调稳。
            // ===== 行进夹角只有【真的在走】的时候才有意义 =====
            //
            // 【这就是"起步先后退"和"多个动画打架"的源头】
            // 上一版的门槛是 planar.sqrMagnitude > 1e-6，也就是**每帧 1 毫米**——
            // 60fps 下折合 0.06 m/s。可这个量级根本不是"走"，而是噪声：
            // CharacterController 的 skinWidth 沉降、贴墙/贴家具的解穿插推挤、
            // 斜面上的微滑、站在会动的东西上被带一下……每一帧都能轻松超过 1mm，
            // 而它们的**方向是随机的**，均匀撒在 ±180° 上。
            //
            // 于是站定与起步的那零点几秒里，喂给方向环的是一串随机角。方向环里
            // 有 Walking Backwards / Jog Backward / Left-Right Strafe——腿就真的
            // 去播了倒步和侧步。屏幕上就是"起步先向后漂一段"、"抑扬顿挫像有好几个
            // 动画在打架"。动画本身一条都没错，接线也没错，**错在喂进去的参数是噪声**。
            //
            // 动画那侧的平滑挡不住它：commit 速率 240°/s，噪声只要持续半秒就能把
            // 混合角推到 120°，早就进了倒步/侧步的地盘；而它追的目标本身在乱跳，
            // 平滑只会把随机游走走得慢一点，不会让它回到正前方。
            //
            // 门槛改成【地面速度】，并带回差：
            //   · 进 0.35 m/s（walkSpeed 2.6 的 13%）——低于此一律不更新夹角，
            //     沿用上一个有效值，噪声再大也进不来；
            //   · 出 0.21 m/s 以下判定为"停住了"，夹角**主动回到正前方**，
            //     免得下一次起步从上一次残留的 90° 侧步开始（那正是起步那一下的怪相）。
            // 撞墙原地不动时 actual≈0 ⇒ 夹角保持不变，腿继续演它原本在演的，
            // 这也是对的：贴着墙推杆时腿本来就该还在走。
            const float MoveAngleEnter = 0.35f, MoveAngleExit = 0.21f;
            float moveAngle = _lastMoveAngle;
            if (actual > MoveAngleEnter && planar.sqrMagnitude > 1e-6f)
            {
                moveAngle = Vector3.SignedAngle(transform.forward, planar.normalized, Vector3.up);
                _lastMoveAngle = moveAngle;
            }
            else if (actual < MoveAngleExit)
            {
                _lastMoveAngle = Mathf.MoveTowardsAngle(_lastMoveAngle, 0f, 360f * dt);
                moveAngle = _lastMoveAngle;
            }
            // ===== 瞬移探测：单帧位移峰值 =====
            // "魔法般改变位置"这类现象，用平均值永远看不见——它就发生在一帧里。
            // 记下最近 6 秒内最大的那一帧位移与它发生在多久之前：
            // 正常跑动一帧最多 5.2m/s ÷ 60fps ≈ 0.09m，掉到 20fps 也就 0.26m。
            // 屏幕上读到 0.8m 就说明**有别的东西在写位置**，那和加减速无关。
            float stepLen = planar.magnitude;
            DbgStepAge += dt;
            if (stepLen > DbgMaxStep || DbgStepAge > 6f)
            { DbgMaxStep = stepLen; DbgStepAge = 0f; }

            DbgMoveAngle = moveAngle;   // 诊断：喂给动画层的行进夹角
            DbgActual = actual;         // 诊断：由真实位移量出来的地面速度
            // 接地信号给动画层时要**带迟滞**：CharacterController.isGrounded 本身就抖
            //（实测每秒翻转两次），而它一抖，动画层就走"腾空"分支——下落循环是保持型
            // 姿态，会以满权重定在一帧上，人却还在地面上走。那正是"没有脚步动画的漂移"
            // 的另一个来源。土狼时间本来就是为这件事算的，直接复用：
            // 只有连土狼时间都耗尽了，才算真的离地。
            _anim.SetLocomotion(speed01, IsCrouched, GroundedStable, actual, moveAngle, StrafeActive);
            UpdateMoveStatePose(speed01, dt);
            // 临战架势：只有敌人【逼近到近身范围(≈6m)】或正在交战时才摆格斗预备架势；
            // 敌人在远处/无敌人时用普通待机（不再一有敌人在场就一直端着架势）
            if (_lockOn == null) _lockOn = GetComponent<LockOnSystem>();
            bool enemyClose = _lockOn != null && _lockOn.CurrentTarget != null &&
                Vector3.Distance(transform.position, _lockOn.CurrentTarget.position) < 6f;
            bool ready = enemyClose || (_combat != null && _combat.InCombat);
            _anim.SetCombatReady(ready);
            // 收刀之后换空手那一套（临战架势 / 蹲伏 / 踢击 / 倒下）：
            // 手里没剑却还端着持剑架势，人会显得凭空握着什么。
            if (_appearance == null) _appearance = GetComponent<PlayerAppearance>();
            _anim.SetArmed(_appearance == null || _appearance.IsWeaponDrawn);
            // 拔刀/收刀改为手动按钮触发（见 PlayerAppearance.ToggleWeaponDrawn），此处不再自动驱动
            _lastPos = transform.position;
        }

        // ===================== 移动过渡姿态层 =====================
        //
        // 起步 / 急停 / 原地转身 / 起跳 / 下落 / 落地 / 蹲伏待机。
        //
        // 大作里"角色是活的"有很大一部分来自这一层：推杆有起步的重心前压、
        // 松杆有刹住的踏步、站着扭方向有原地转身、落地有缓冲——
        // 而不是站姿直接平移、跑姿突然静止、整个人瞬间转过去、从空中直接贴地。
        //
        // 三条硬规矩，缺一条都会变成"手感变差"：
        //   ① **绝不锁移动**。这一层只往动作层丢一段片段，位移照旧由控制器算，
        //      玩家推杆的那一刻角色就已经在走了——过渡是画面上的，不是逻辑上的。
        //   ② **战斗动作优先**。攻击/受击/翻滚/技能占着身体时整层让开。
        //   ③ **有冷却**。同一类过渡短时间内不重复触发，否则贴身绕圈会
        //      每隔几帧插一次转身，读作抖动。
        bool _wasGrounded = true;
        bool _crouchPosed;
        bool _jumpPosePlayed;
        float _airT, _fallTopY;
        float _moveStateCd;
        float _prevSpeed01;
        float _stickMag;
        float _stillT;   // 已经站定多久（秒）——原地转身的前提

        /// <summary>算"站定"的速度阈值（相对冲刺速度的比例）。</summary>
        /// <summary>
        /// 起步/急停要不要走【动作层】。答案是不要，这里留成常量只为把理由钉在代码里。
        ///
        /// PlayableAnimator 是两层：移动层（方向环）与动作层，而动作层**盖在移动层上面**。
        /// 把"起步(Start Walking)/急停(Run To Stop)"当作动作播，那一段里整个方向混合
        /// 被替换成一段站姿片段，可位移仍在继续——
        ///   · 起步：Start Walking 起手有个后撤重心 ⇒ 画面上"先向后漂一段再前进"；
        ///   · 急停：人已经停了，腿却在演另一段，读作动作不连贯。
        /// 这正是"多个动作和动画存在冲突"。移动过渡属于移动状态机内部，
        /// 要做也得做进方向环的混合里（起步/急停片段与移动片段共享步态相位），
        /// 而不是盖在它上面。在那之前，宁可没有过渡，也不要打架的过渡。
        /// </summary>
        const bool MoveTransitionsOnActionLayer = false;

        const float StandStillSpeed = 0.06f;

        /// <summary>要"站定"多久才允许播原地转身。跑动中急转弯时速度会瞬间掠过零，
        /// 这个时间窗把那种情况挡在外面（0.2s ≈ 12 帧，急转穿越零速远短于此）。</summary>
        const float StandStillBeforeTurn = 0.2f;

        const float FallPoseAfter = 0.22f;   // 腾空多久之后才算"在下落"（小台阶不播）
        const float HardLandDrop = 3.2f;     // 掉落超过这个高度算重着陆（米）

        void UpdateMoveStatePose(float speed01, float dt)
        {
            if (_anim == null || _cc == null || _anim.Resting) return;

            bool busy = _dodgeTimer > 0f ||
                        (_combat != null && _combat.Current != CombatState.Locomotion &&
                                            _combat.Current != CombatState.Idle);
            if (busy)
            {
                // 战斗动作期间不插手，但地面/速度的"上一帧"要照常记，
                // 否则动作一结束就会因为"上一帧还在天上"而误播一次落地
                _wasGrounded = GroundedStable;   // 与下面的判据同源，否则一进一出就误报落地
                _prevSpeed01 = speed01;
                _stillT = 0f;   // 战斗动作结束的那一刻不算"站了很久"
                return;
            }

            _moveStateCd -= dt;
            // 同上：姿态决策也用带迟滞的接地信号。isGrounded 一抖就走"腾空"分支，
            // 而那条分支会 return，把后面的蹲伏/转身/起步收尾全部跳过——
            // 姿态因此可能卡在一个保持型片段上，人却在地面上移动。
            bool grounded = GroundedStable;

            // ---------- 腾空：起跳 → 下落循环 ----------
            if (!grounded)
            {
                if (_wasGrounded) { _airT = 0f; _fallTopY = transform.position.y; _jumpPosePlayed = false; }
                _airT += dt;
                _fallTopY = Mathf.Max(_fallTopY, transform.position.y);   // 最高点起算下落高度
                if (!_jumpPosePlayed && _vy > 0.5f && _anim.HasPose(PoseState.JumpUp))
                {
                    _jumpPosePlayed = true;
                    // ===== 起跳片段的时长必须等于【上升段的物理时长】=====
                    //
                    // "跳跃动画非常奇怪、不自然"的根子在这儿：起跳片段是按它自己的
                    // 节奏播的，而跳跃的上升段是物理算出来的，两者从来没对过表。
                    //     上升时长 = jumpForce / |gravity| = 7 / 20 = 0.35s
                    // 而 Jumping Up 裁掉首尾后按 1.2 倍速播是 0.8 秒上下。
                    // 于是人在 0.35 秒就到了最高点，腿却才演到起跳片段的一半；
                    // 紧接着过了顶点 _vy<−1，下落循环把它当场切断——
                    // **起跳动作从头到尾没有一次播完过**，看到的只是个残段。
                    //
                    // 交给 SetPose 的 duration 反推播放速度（它就是干这个的），
                    // 于是片段正好在最高点播完，下落循环接得上，不再互相切断。
                    _anim.SetPose(PoseState.JumpUp, jumpForce / Mathf.Max(1f, Mathf.Abs(gravity)));
                }
                else if (_airT > FallPoseAfter && _vy < -1f &&
                         _anim.CurrentPose != PoseState.FallLoop && _anim.HasPose(PoseState.FallLoop))
                {
                    _anim.SetPose(PoseState.FallLoop);
                }
                _wasGrounded = false;
                _prevSpeed01 = speed01;
                _stillT = 0f;        // 腾空不算站定
                return;
            }

            // ---------- 落地 ----------
            if (!_wasGrounded)
            {
                _wasGrounded = true;
                float drop = _fallTopY - transform.position.y;
                var land = drop > HardLandDrop ? PoseState.LandHard : PoseState.Land;
                if (!_anim.HasPose(land)) land = PoseState.Land;
                // 只在真的腾空过一会儿之后才播落地：走下路缘石也播一段缓冲会很碎。
                // 【还要求落地时基本是停下来的】动作层是盖在移动层上面的：跑跳落地
                // 若播一段"站定缓冲 + 起身"，人还在全速前进、腿却在演原地落地起身，
                // 又是一次动作与位移打架。大作的处理一样——助跑跳落地直接接跑动循环，
                // 缓冲只在原地跳/高处落下时才有。
                float landSpeed01 = Mathf.Clamp01(_hVel.magnitude / Mathf.Max(0.1f, runSpeed));
                if (_airT > FallPoseAfter && _anim.HasPose(land) &&
                    (landSpeed01 < 0.35f || land == PoseState.LandHard))
                {
                    // 落地缓冲的时长同样按物理给，不由片段自己说了算：
                    // 普通落地就是一次屈膝吸收（0.25s），重着陆多给一点（0.45s）。
                    // 片段本身是两秒的"落地 + 慢慢站直"，照它自己的节奏播，
                    // 移动层要被盖住一秒多——那一秒里人已经在走了。
                    float landDur = land == PoseState.LandHard ? 0.45f : 0.25f;
                    _anim.SetPose(land, landDur);
                    _moveStateCd = landDur;
                }
                else if (_anim.CurrentPose == PoseState.FallLoop || _anim.CurrentPose == PoseState.JumpUp)
                {
                    _anim.SetPose(PoseState.Idle);   // 保持型的下落姿态必须显式收掉
                }
                _prevSpeed01 = speed01;
                _stillT = 0f;        // 刚落地不算站定
                return;
            }

            // ---------- 一动起来就立刻收掉原地转身 ----------
            //
            // 【实机日志抓到的】「动作 Left Turn 90 权重1.00 速度1.8」——
            // 原地转身的片段在角色**正在位移**时满权重接管了整个移动层。
            // 动作层是替换移动层的，那一段里腿在演站着转身、人却在往前走，
            // 画面上就是"抑扬顿挫地漂到别处再继续"。这正是你说的动画打架。
            //
            // 触发条件本身没错（要求站定 _stillT 才起播），错在**没有收尾**：
            // 起播那一刻人是站着的，可玩家紧接着就推杆走了，而这段片段还要播
            // 0.22~0.5 秒。转身是"站着才成立"的动作，前提一消失就该立刻让位。
            if (speed01 > StandStillSpeed * 2f && !StrafeActive &&
                (_anim.CurrentPose == PoseState.TurnLeft ||
                 _anim.CurrentPose == PoseState.TurnRight ||
                 _anim.CurrentPose == PoseState.Turn180))
            {
                _anim.SetPose(PoseState.Idle);   // 交还移动层
                _moveStateCd = 0f;
            }

            // ---------- 蹲伏待机 ----------
            bool crouchStill = IsCrouched && speed01 < 0.03f;
            if (crouchStill != _crouchPosed)
            {
                _crouchPosed = crouchStill;
                if (crouchStill && _anim.HasPose(PoseState.CrouchIdle)) _anim.SetPose(PoseState.CrouchIdle);
                else if (_anim.CurrentPose == PoseState.CrouchIdle) _anim.SetPose(PoseState.Idle);
                _prevSpeed01 = speed01;
                return;
            }
            if (crouchStill)
            {
                _prevSpeed01 = speed01;
                return;
            }

            // ---------- 原地转身 ----------
            //
            // 【判据必须是"站定 + 把杆打向身后"，不能是"速度低 + 朝向在变"】
            // 速度低那一版是错的：跑动中把摇杆打向反方向时，速度会**经过零**——
            // 那一瞬人不是站着，是正在高速换向。于是每次急转弯都插一段全身转身片段，
            // 而动作层一旦接管就盖住整个移动混合：人还在平移，腿却在演原地转身。
            // 摇杆转一整圈＝连续急转＝转身片段一段接一段，这正是"移动完全失真"。
            //
            // 现在直接判本意：**已经站定一小会儿**（_stillT）+ 摇杆推向的方向与
            // 身体正面差得够多。急转弯时 _stillT 恒为 0，天然进不来；
            // 而站着把杆打到身后是明确的"我要掉头"，一次判定即可出招，
            // 不必靠累积转过的角度（那会把噪声也累进去）。
            _stillT = speed01 < StandStillSpeed ? _stillT + dt : 0f;
            if (!StrafeActive && _moveStateCd <= 0f && _stillT > StandStillBeforeTurn &&
                _stickMag > 0.5f && StickWorldDir.sqrMagnitude > 0.04f)
            {
                float need = Vector3.SignedAngle(transform.forward,
                    StickWorldDir.normalized, Vector3.up);
                float absNeed = Mathf.Abs(need);
                if (absNeed > 60f)
                {
                    var tp = absNeed > 150f
                        ? PoseState.Turn180
                        : (need > 0f ? PoseState.TurnRight : PoseState.TurnLeft);
                    if (_anim.HasPose(tp))
                    {
                        // 片段时长【跟着身体实际转完的时间走】，不再拍固定值：
                        // 原地转身触发时人是站定的，此刻角速度上限就是 turnDegPerSecStill，
                        // 转完所需时间可以直接算出来。拍固定值的写法在转速一改就会
                        // 重新对不上——这一段的注释已经因此过期过两次了。
                        float bodyTurnT = absNeed / Mathf.Max(1f, turnDegPerSecStill);
                        _anim.SetPose(tp, Mathf.Clamp(bodyTurnT, 0.22f, 0.5f));
                        _moveStateCd = 0.65f;
                    }
                }
            }

            // ---------- 起步 / 急停 ----------
            if (_moveStateCd <= 0f)
            {
                // 起步只在【慢速起步】时给：猛推满杆本来就该直接进跑，
                // 那时再插一段起步反而拖一拍（这正是"角色不跟手"的常见来源）。
                if (_prevSpeed01 < 0.02f && speed01 > 0.12f && _stickMag < 0.65f &&
                    MoveTransitionsOnActionLayer && _anim.HasPose(PoseState.StartMove))
                {
                    _anim.SetPose(PoseState.StartMove);
                    _moveStateCd = 0.4f;
                }
                // 急停只在【跑起来之后松杆】给。
                // 必须看摇杆而不只看速度：跑动中打反方向时速度同样会掉下来，
                // 那是换向不是停步——那时插一段刹车动作，人一边刹车一边全速侧移，
                // 与原地转身是同一类错误。
                else if (_prevSpeed01 > 0.55f && speed01 < 0.10f && _stickMag < 0.2f &&
                         MoveTransitionsOnActionLayer && _anim.HasPose(PoseState.StopMove))
                {
                    _anim.SetPose(PoseState.StopMove);
                    _moveStateCd = 0.45f;
                }
            }
            _prevSpeed01 = speed01;
        }

        void ToggleCrouch()
        {
            IsCrouched = !IsCrouched;
            // 碰撞体随姿态变化，底部保持贴地
            if (IsCrouched)
            {
                _cc.height = 1.3f;
                _cc.center = new Vector3(0, -0.35f, 0);
            }
            else
            {
                _cc.height = 2f;
                _cc.center = Vector3.zero;
            }
        }

        /// <summary>
        /// 摇杆方向 → 世界方向（镜头相对）。**始终用镜头当前偏航**，
        /// 保证"推左＝画面左、推上＝画面深处"这条映射任何时候都成立。
        ///
        /// 曾试过把参考系锁存、只跟手动转镜，以此切断"镜头绕行↔角色转向"的代数环。
        /// 环确实断了，但代价是镜头绕到背后之后，摇杆与画面就对不上了
        /// （手指还按着左，画面里角色却在往前跑）——实测下来这个代价不可接受。
        ///
        /// 这三条性质数学上最多同时满足两条：
        ///   (a) 摇杆↔画面始终一致  (b) 镜头自动绕到背后  (c) 角色不画弧线
        /// 因为 moveDir = 镜头偏航 + 摇杆角，镜头一转 moveDir 就跟着转。
        /// 主流第三人称动作游戏一律取 (a)+(b)、放弃 (c)：持续推一个非正前方向时，
        /// 角色走一条弧线。缺陷从来不是"会画弧"，而是弧的松紧——
        /// 本作曾达 207~322°/s（半径仅 0.9~1.4m，读作原地转圈）；
        /// 现由镜头侧把持续绕行速率压到 30°/s（半径 9.9m），即大作那种缓弧。
        /// 见 ThirdPersonCamera 的 SustainedOrbitCap。
        /// </summary>
        Vector3 CameraRelative(Vector2 input)
        {
            // 偏置的衰减放在**所有分支之前**：松杆时这个方法会提前 return，
            // 若把衰减写在下面的 else 里，松杆那一刻残留的偏置就冻在原地不动了，
            // 下次推杆时会拿一个过期的偏置去算方向（最坏 180°，持续 0.18s）。
            _frameBias = Mathf.MoveTowards(_frameBias, 0f,
                FrameBiasReleaseDegPerSec * Time.deltaTime);
            if (input.sqrMagnitude < 0.0001f) { _recenterFrameInit = false; return Vector3.zero; }
            if (cameraTransform == null) return new Vector3(input.x, 0, input.y).normalized;

            // ===== 移动参考系永远是【当前镜头】，唯一例外是一键回正期间 =====
            //
            // 曾经试过"镜头自主掉头时钉住参考系"，让角色在镜头绕行期间走直线。
            // 代数上它确实成立，但实机截图给出了否决性的证据：
            // **摇杆推在左下、画面里角色却在往画面深处跑**——偏差最大可达 180°。
            // 上游此前也因同一现象撤回过（cd4bc4a）。两次独立验证，结论一致：
            // 「摇杆方向 ↔ 画面方向」的一致性是不可交易的，它比"轨迹是直线"更基本，
            // 因为玩家是照着画面推杆的，一旦对不上，每一次输入都在赌。
            //
            // 于是恒等式 H = C + θ 全时成立，而它的推论是：
            // **镜头在玩家按着摇杆时每转 1°，角色就被带转 1°。**
            // 所以镜头侧的答案只能是——按着摇杆时压根不自动转（见 ThirdPersonCamera
            // 的 _viewBlocked：开阔时旋转授权为 0）。这也正是《原神》《塞尔达》
            // 《魂系》等虚拟摇杆/手柄第三人称作品的通行做法：**镜头偏航归玩家**，
            // 自动运镜只用于锁定、显式回正与战斗兜底。
            //
            // 一键回正是唯一例外：那是玩家自己按的、有始有终的 0.6~0.9s 动作，
            // 期间冻结参考系可避免镜头把角色一起拖转，动作一结束立即恢复。
            if (_cam == null) _cam = cameraTransform.GetComponent<ThirdPersonCamera>();
            float frameYaw;
            if (_cam != null && _cam.RecenterActive)
            {
                if (!_recenterFrameInit)
                {
                    _recenterFrameYaw = cameraTransform.eulerAngles.y;
                    _recenterFrameInit = true;
                }
                frameYaw = _recenterFrameYaw;
            }
            else
            {
                // 【回正结束的那一帧不能硬切参考系】
                // 冻结期间参考系停在 _recenterFrameYaw，而镜头已经转过去了 90~180°。
                // 直接切回实时镜头偏航，摇杆方向会在**一帧之内**跳同样的角度——
                // 玩家还按着同一个方向，角色却猛地拐向别处（"换方向时人先瞬移到别处"）。
                // 改为把这段偏置在 0.18 秒内衰减掉：比任何一次回正都短，
                // 玩家察觉不到"摇杆与画面错开"，却把那一下硬拐磨成了一小段过渡。
                if (_recenterFrameInit)
                {
                    _recenterFrameInit = false;
                    _frameBias = Mathf.DeltaAngle(cameraTransform.eulerAngles.y, _recenterFrameYaw);
                }
                // 偏置归零之后就是**实时映射，无任何偏置**：摇杆方向恒等于画面方向。
                // 代价由镜头侧承担——它只能以很慢的速率绕行（见 SustainedOrbitCap），
                // 慢到玩家的补杆是无意识的，于是角色路径几乎看不出弯。
                frameYaw = cameraTransform.eulerAngles.y + _frameBias;
            }

            Quaternion frame = Quaternion.Euler(0, frameYaw, 0);
            Vector3 fwd = frame * Vector3.forward;
            Vector3 right = frame * Vector3.right;
            return (fwd * input.y + right * input.x).normalized;
        }

        ThirdPersonCamera _cam;
        float _recenterFrameYaw;
        bool _recenterFrameInit;
        float _frameBias;   // 回正结束后残留的参考系偏置（度），衰减到 0

        /// <summary>参考系偏置的衰减速率：180° 的最坏情况在 0.18s 内化完。</summary>
        const float FrameBiasReleaseDegPerSec = 1000f;

        void ApplyGravityOnly(float dt)
        {
            // 硬锁状态(重击蓄力/聚气、施法、倒地等)：只落重力、水平零位移，且清空残余
            // 水平速度——聚气时玩家原地扎稳，不带着惯性前滑（"漂移"），锁定解除也不会
            // 突然窜出一段。
            _hVel = Vector3.zero;
            _vy = _cc.isGrounded ? -1f : _vy + gravity * dt;
            // 硬锁期间也要把外部位移落地：出招磁吸恰恰发生在蓄力/施法这类硬锁态里，
            // 漏掉它等于这一帧的突进被吞掉，读作"这一下没打中/位置对不上"。
            Combat.CharacterMotion.StepMove(_cc,
                Vector3.up * _vy * dt + TakeExternalMove());
        }
    }
}
