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
        [Header("移动（速度按真实体感收敛，防晕）")]
        public float walkSpeed = 2.6f;
        public float runSpeed = 5.2f;
        // 起步/刹车响应（指数逼近速率，1/秒）：对齐 Unity 官方 ThirdPersonController
        // 的 SpeedChangeRate=10 思路，但按动作游戏上调——
        // 起步 k=20：0.05s 到 63%、0.15s 到 95%；刹车 k=26 更利落。
        // 防晕由镜头侧负责（软跟随 + 焦点死区 + 渐进回正），而不是靠拖慢角色本体。
        public float accelRate = 20f;              // 起步逼近速率
        public float decelRate = 26f;              // 停步逼近速率
        public float rotateSpeed = 14f;            // 转身更跟手（大作级的方向响应）
        public float quickTurnMultiplier = 2.1f;   // 大角度转身加速倍率：掉头近乎即时
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
        public bool WalkOnly { get; set; }

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
        }

        static string V(Vector3 p) =>
            "(" + Mathf.RoundToInt(p.x) + "," + Mathf.RoundToInt(p.y) + "," + Mathf.RoundToInt(p.z) + ")";

        /// <summary>附近有没有活着的敌人（判断"是不是正在打"，别在战斗中把人传走）。</summary>
        static bool EnemyNearby(Vector3 pos, float radius)
        {
            foreach (var e in Object.FindObjectsByType<AI.EnemyController>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
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
                    var t = hit.collider != null ? hit.collider.transform : null;
                    if (t != null && !t.IsChildOf(transform)) found = t;
                }
            }

            if (found != null && found == _platform)
            {
                Vector3 delta = found.position - _platformLastPos;
                // 只跟随合理幅度的位移：平台被瞬移/重生时不要把角色一起甩飞
                if (delta.sqrMagnitude > 1e-8f && delta.sqrMagnitude < 25f) _cc.Move(delta);
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

            float dt = Time.deltaTime;
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
                _cc.Move(_dodgeDir * _dodgeSpd * dt + Vector3.up * _vy * dt);
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

            // 交战锁面向的目标（决定本帧是"横移"还是"朝哪走朝哪转"）
            Transform face = FacingTarget();
            // 撤退意图：推杆方向持续背对目标 ⇒ 解除软锁，转身全速跑。
            // 【手动锁定不适用】：那是玩家自己按下去的，要解除请再按一次锁定键。
            // 这里对它放行的话会变成：本帧解除、下帧 FacingTarget 又把手动目标还回来，
            // 于是每 0.35 秒抖一下——比不给出口更糟。
            bool manualLocked = _lockOn != null && _lockOn.CurrentTarget != null;
            if (manualLocked) _disengageT = 0f;
            else if (face != null && moveDir.sqrMagnitude > 0.04f)
            {
                Vector3 away = transform.position - face.position; away.y = 0;
                // 推杆方向落在"正背离目标"的 ±50° 锥里 = 在往回撤，而不是侧向走位
                bool backing = away.sqrMagnitude > 0.01f &&
                               Vector3.Angle(moveDir, away.normalized) < DisengageCone;
                _disengageT = backing ? _disengageT + dt : 0f;
                if (_disengageT >= DisengageHold)
                {
                    _disengageT = 0f;
                    _disengageUntil = Time.time + DisengageGrace;
                    face = null;
                    if (Time.time - _disengageSaidAt > 6f)
                    {
                        _disengageSaidAt = Time.time;
                        Core.GameEvents.RaiseSubtitle("脱离交战——转身拉开距离。");
                    }
                }
            }
            else _disengageT = 0f;
            StrafeActive = face != null;
            SoftLockTarget = face;

            // 模拟量速度：摇杆半推=走路，全推=奔跑；桌面按住 Alt 慢走
            // 行动力过低时脚步沉重（拖延的具象体感）：35 以下开始线性减速，最低 ×0.65
            float apMult = Mathf.Lerp(0.65f, 1f, Mathf.Clamp01(Stats.actionPower / 35f));
            float speed = runSpeed * MoveSpeedMultiplier * apMult * inputMag;
            if (!Application.isMobilePlatform && Input.GetKey(KeyCode.LeftAlt))
                speed = Mathf.Min(speed, walkSpeed * MoveSpeedMultiplier);
            // 室内只走不跑：在自己家里冲刺既不合情理，也是"转一圈就晕"的一部分——
            // 屋里两三步一堵墙，全速跑动时镜头与碰撞都来不及跟上。
            // 唯一的例外由 IndoorZone.SetRunPass 开出来：健身房的跑步机就是用来跑的，
            // 走速正好等于履带速度，不许跑等于那台机器是坏的。
            if (WalkOnly) speed = Mathf.Min(speed, walkSpeed * MoveSpeedMultiplier);
            if (IsCrouched) speed *= crouchSpeedMult;
            // 横移/后撤要慢下来，两个理由都成立：
            //   ① 没有人能侧着或倒着跑出正面冲刺的速度，而且"绕圈找角度"本来就该是
            //      有代价的移动，不该比正面突进还快；
            //   ② 动作库里侧移与后退**只有走的片段**（Left/Right Strafe Walking、
            //      Walking Backwards）。移速给到冲刺档，步频再怎么提速也追不上位移，
            //      脚就会打滑——所以这里不只是乘个系数，还要**按片段撑得住的速度封顶**。
            //      将来补上跑动版的横移片段（Left/Right Strafe），把封顶放开即可。
            if (face != null && moveDir.sqrMagnitude > 0.01f)
            {
                float sideAng = Mathf.Abs(
                    Vector3.SignedAngle(transform.forward, moveDir, Vector3.up));
                if (sideAng > 60f)
                {
                    float back01 = Mathf.Clamp01((sideAng - 60f) / 120f);   // 0=正侧 1=正后
                    float cap = Mathf.Lerp(walkSpeed * 1.3f, walkSpeed * 1.05f, back01)
                                * MoveSpeedMultiplier;
                    speed = Mathf.Min(speed * Mathf.Lerp(0.78f, 0.6f, back01), cap);
                }
            }
            // 出招定步（平滑化）：攻击动画占据全身，照常位移会读作"脚不动人在滑"。
            // 但此前用【硬性 ×0.1】会造成速度震荡——推着摇杆连打时，每一段出招速度
            // 从全速骤降到 10%、收招again骤升回全速，配合已提速的加减速就是一顿一顿的
            // 抽搐感。改为对倍率本身做时间常数 ≈0.07s 的平滑过渡，并把下限抬到 0.3：
            // 既保留定步的分量感，又让"边推杆边连打"是一条连续的速度曲线。
            _attackSpeedFactor = Mathf.MoveTowards(_attackSpeedFactor,
                attacking ? 0.3f : 1f, dt / 0.07f);
            speed *= _attackSpeedFactor;

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
                // 翻滚方向即刻转身
                transform.rotation = Quaternion.LookRotation(_dodgeDir);
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
                    float clipLen = _anim.ActionClipLength(PoseState.Dodge);
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

            // 加减速曲线：改用【指数逼近】而非线性匀加速——对齐 Unity 官方
            // ThirdPersonController 的做法（其注释原文：curved result rather than a
            // linear one giving a more organic speed change）。
            // 起步瞬间给足冲量（前几帧就到大半速度＝跟手），末段自然收敛（不突兀）。
            // 用 1-e^(-k·dt) 而非 Lerp(a,b,k·dt)：帧率无关，高低帧手感一致。
            Vector3 targetVel = moveDir * speed;
            float k = targetVel.sqrMagnitude > _hVel.sqrMagnitude ? accelRate : decelRate;
            _hVel = Vector3.Lerp(_hVel, targetVel, 1f - Mathf.Exp(-k * dt));
            CarryByPlatform();          // 站在会动的东西上（车顶等）要跟着它走
            FallGuard();                // 掉出世界的兜底捞回
            _cc.Move(_hVel * dt + Vector3.up * _vy * dt);

            // ===== 朝向：交战时【脸对着敌人】，移动方向由摇杆决定（横移/后撤/绕行）=====
            //
            // 这是本轮补上的一条基础能力。之前只有一条规则——"朝哪走就转向哪"，
            // 于是想往左移动就必须先把身体转到左边：那是**转向**，不是横移。
            // 一切近身格斗类游戏都把这两件事分开：锁定/交战时身体始终对着目标，
            // 摇杆推左就左跨、推后就后撤、斜后就是撤步绕角，脸从头到尾没离开敌人。
            // 只有脱离交战（探索行进）才回到"朝哪走就朝哪转"。
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
                    float ang = Quaternion.Angle(transform.rotation, target);
                    float rs = rotateSpeed * (ang > 80f ? quickTurnMultiplier : 1f);
                    transform.rotation = Quaternion.Slerp(transform.rotation, target, rs * dt);
                }
            }
        }

        /// <summary>交战中锁面向的角速度上限（度/秒）：跟得住绕后，又不会甩镜。</summary>
        const float FaceTargetDegPerSec = 420f;

        /// <summary>横移态（脸锁在目标上、移动方向独立）——镜头与动画都要知道。</summary>
        public bool StrafeActive { get; private set; }

        /// <summary>当前软锁面向的目标（没有则为 null）：镜头取景据此把两人一起框住。</summary>
        public Transform SoftLockTarget { get; private set; }

        // ---- 脱战：一直往后推杆就解除软锁，转身跑 ----
        //
        // 软锁面向本身是对的（近身互殴不该被迫把背露给对方），但它有个必须留的出口：
        // **想撤退的时候**。锁着脸只能倒退，而倒退是 0.6 倍速——追你的敌人是全速，
        // 于是"逃跑"变成了一件在数值上不可能的事，玩家会觉得被黏在原地。
        // 大作的通行做法是给一个明确的解除条件：持续往远离目标的方向推杆
        //（不是瞬间的斜后走位，所以要压一段时间），就当作"我要走了"，
        // 解除软锁、转身、全速跑。松开或改推别的方向立刻恢复锁面向。
        const float DisengageCone = 50f;     // 推杆方向与"正背离目标"的夹角容差
        const float DisengageHold = 0.35f;   // 要持续这么久才算撤退意图（不误伤走位）
        const float DisengageGrace = 0.9f;   // 解除后这么久内不重新锁（让人跑出去）
        float _disengageT, _disengageUntil, _disengageSaidAt = -99f;

        /// <summary>
        /// 当前该锁面向谁：
        ///   ① 手动锁定的目标最优先（玩家明确说了"盯它"）；
        ///   ② 没锁定时，交战中且有敌人贴近（≤7m）也锁面向——
        ///      近身互殴时把背露给对方从来不是玩家的本意，而是"必须转身才能走"逼出来的。
        /// 脱战即解除，探索时回到"朝哪走朝哪转"。
        /// </summary>
        Transform FacingTarget()
        {
            if (_lockOn == null) _lockOn = GetComponent<LockOnSystem>();
            // 手动锁定是玩家明确的意思表示，撤退逻辑不插手（要脱锁请按锁定键）
            if (_lockOn != null && _lockOn.CurrentTarget != null) return _lockOn.CurrentTarget;
            if (_combat == null || !_combat.InCombat) return null;
            if (Time.time < _disengageUntil) return null;   // 刚判定为撤退：让他跑

            // 找最近敌人每帧全场扫一遍太贵：0.2 秒刷一次，中间沿用上次结果
            //（0.2 秒内敌人跑不出"贴近"与"没贴近"的区别）
            if (Time.time >= _faceScanAt)
            {
                _faceScanAt = Time.time + 0.2f;
                Transform best = null; float bestD = 7f;
                foreach (var e in FindObjectsOfType<AI.EnemyController>())
                {
                    if (e == null || e.State == AI.EnemyState.Dead) continue;
                    float d = Vector3.Distance(transform.position, e.transform.position);
                    if (d < bestD) { bestD = d; best = e.transform; }
                }
                _faceCache = best;
            }
            // 缓存的目标可能已经死了/远离：用之前再校一次，免得脸锁在一具尸体上
            if (_faceCache != null)
            {
                var ec = _faceCache.GetComponent<AI.EnemyController>();
                if (ec == null || ec.State == AI.EnemyState.Dead ||
                    Vector3.Distance(transform.position, _faceCache.position) > 9f)
                    _faceCache = null;
            }
            return _faceCache;
        }

        Transform _faceCache;
        float _faceScanAt;

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
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 planar = transform.position - _lastPos;
            planar.y = 0;
            float actual = planar.magnitude / dt;
            float speed01 = Mathf.Clamp01(actual / Mathf.Max(0.1f, runSpeed));
            // 移动方向相对身体正面的夹角：0=正前、±90=横跨、180=后撤。
            // 动画层据此在方向片段之间混合（前/后/左/右/斜向）。
            //
            // 【只在横移态下才给真实夹角】非交战时身体本来就会转向行进方向，
            // 此刻的夹角只是"转身还没转完"的瞬时残差——把它喂给方向混合，
            // 会在每次掉头的头零点几秒里闪一下后退/侧步的片段。
            // 那种情况下正确答案恒定是"向前走"，转身交给身体旋转去表达。
            float moveAngle = 0f;
            if (StrafeActive && planar.sqrMagnitude > 1e-6f)
                moveAngle = Vector3.SignedAngle(transform.forward, planar.normalized, Vector3.up);
            _anim.SetLocomotion(speed01, IsCrouched, _cc.isGrounded, actual, moveAngle);
            // 临战架势：只有敌人【逼近到近身范围(≈6m)】或正在交战时才摆格斗预备架势；
            // 敌人在远处/无敌人时用普通待机（不再一有敌人在场就一直端着架势）
            if (_lockOn == null) _lockOn = GetComponent<LockOnSystem>();
            bool enemyClose = _lockOn != null && _lockOn.CurrentTarget != null &&
                Vector3.Distance(transform.position, _lockOn.CurrentTarget.position) < 6f;
            bool ready = enemyClose || (_combat != null && _combat.InCombat);
            _anim.SetCombatReady(ready);
            // 拔刀/收刀改为手动按钮触发（见 PlayerAppearance.ToggleWeaponDrawn），此处不再自动驱动
            _lastPos = transform.position;
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
                _recenterFrameInit = false;
                // **实时映射，无任何偏置**：摇杆方向恒等于画面方向，一帧都不错开。
                // 代价由镜头侧承担——它只能以很慢的速率绕行（见 SustainedOrbitCap），
                // 慢到玩家的补杆是无意识的，于是角色路径几乎看不出弯。
                frameYaw = cameraTransform.eulerAngles.y;
            }

            Quaternion frame = Quaternion.Euler(0, frameYaw, 0);
            Vector3 fwd = frame * Vector3.forward;
            Vector3 right = frame * Vector3.right;
            return (fwd * input.y + right * input.x).normalized;
        }

        ThirdPersonCamera _cam;
        float _recenterFrameYaw;
        bool _recenterFrameInit;

        void ApplyGravityOnly(float dt)
        {
            // 硬锁状态(重击蓄力/聚气、施法、倒地等)：只落重力、水平零位移，且清空残余
            // 水平速度——聚气时玩家原地扎稳，不带着惯性前滑（"漂移"），锁定解除也不会
            // 突然窜出一段。
            _hVel = Vector3.zero;
            _vy = _cc.isGrounded ? -1f : _vy + gravity * dt;
            _cc.Move(Vector3.up * _vy * dt);
        }
    }
}
