using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Player;
using AdversityRoad.World;

namespace AdversityRoad.OpenWorld
{
    /// <summary>
    /// 可以坐下/躺下的家具。
    ///
    /// 家里摆了一屋子椅子、沙发和床，但玩家只能站着看——这正是"像样板间而不像家"
    /// 的地方。坐下不是装饰性动作：住处是这个游戏里唯一没有敌人也没有倒计时的空间，
    /// 坐一会儿、在床上躺一会儿本身就是它要提供的东西（躺下会慢慢回体力与意志）。
    ///
    /// 【这一版有真正的动作了】上一版没有坐姿动画，只能把整个人往下挪 0.42 米、
    /// 躺下就把外观绕 X 轴转 90°——站着的人平移到椅子上、或者僵直地"倒"在床上，
    /// 一眼假。现在动作库里有了 Sitting / Lying Down / Sleeping Idle / Getting Up，
    /// 坐与躺走真正的过程动画（见 SitController），这个组件只负责
    /// **这件家具能不能坐、能不能躺、座面在哪、人该朝哪**。
    ///
    /// 座面高度不再靠手填偏移量（那也是上一版坐姿高低不一的原因），
    /// 直接量这件家具的渲染包围盒顶面——沙发、餐椅、吧椅、床、躺椅、瑜伽垫
    /// 各自多高，量出来就是多高。
    /// </summary>
    public class Sittable : MonoBehaviour
    {
        public Vector3 faceDir = Vector3.forward;
        /// <summary>躺下时人的朝向：沙发要顺着长边躺（否则头会伸进靠背里），
        /// 和坐着面朝外的方向差 90°。默认与 faceDir 相同（床、躺椅、垫子都一样）。</summary>
        public Vector3 lieFaceDir = Vector3.zero;
        /// <summary>除了坐，还能躺下（床、沙发、躺椅、卧推凳）。</summary>
        public bool canLie;
        /// <summary>只能躺（地上的瑜伽垫：坐在垫子上没意义）。</summary>
        public bool lieOnly;
        public string label = "椅子";
        public float range = 2.8f;

        /// <summary>座面/床面的世界高度（包围盒顶面）。</summary>
        public float SurfaceY { get; private set; }
        /// <summary>座面中心（骨盆落点的水平位置）。</summary>
        public Vector3 SurfaceCenter { get; private set; }
        /// <summary>家具的半尺寸：起身要退到家具外面（床有 6 米长，退 1.5 米还在床上）。</summary>
        public Vector3 SurfaceExtents { get; private set; }

        PlayerController _player;
        float _lastHint = -99f;

        /// <summary>躺下时该朝哪（没单独指定就沿用坐着的朝向）。</summary>
        public Vector3 LieFace => lieFaceDir.sqrMagnitude < 0.01f ? faceDir : lieFaceDir.normalized;

        public static Sittable Attach(GameObject go, Vector3 faceDir, string label,
            bool canLie = false, bool lieOnly = false, Vector3 lieFaceDir = default)
        {
            if (go == null) return null;
            var s = go.AddComponent<Sittable>();
            s.faceDir = faceDir.sqrMagnitude < 0.01f ? Vector3.forward : faceDir.normalized;
            s.lieFaceDir = lieFaceDir;
            s.canLie = canLie || lieOnly;
            s.lieOnly = lieOnly;
            s.label = label;
            s.range = canLie || lieOnly ? 3.6f : 2.8f;

            // 量家具本体：顶面 = 座面，中心 = 骨盆落点
            var b = Measure(go);
            s.SurfaceY = b.max.y;
            s.SurfaceCenter = new Vector3(b.center.x, b.max.y, b.center.z);
            s.SurfaceExtents = b.extents;
            return s;
        }

        static Bounds Measure(GameObject go)
        {
            var mf = go.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                // 用网格包围盒自己算：刚建出来的物体 Renderer.bounds 未必已经更新
                Bounds mb = mf.sharedMesh.bounds;
                var t = go.transform;
                var acc = new Bounds(t.TransformPoint(mb.center), Vector3.zero);
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3(
                        (i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    acc.Encapsulate(t.TransformPoint(c));
                }
                return acc;
            }
            var col = go.GetComponent<Collider>();
            if (col != null) return col.bounds;
            return new Bounds(go.transform.position, Vector3.one * 0.5f);
        }

        /// <summary>走近判定用的参考点（座面中心）。</summary>
        public Vector3 SeatPoint => SurfaceCenter;

        void Update()
        {
            if (SitController.Busy) return;
            if (_player == null)
            {
                _player = FindObjectOfType<PlayerController>();
                if (_player == null) return;
            }
            if (Vector3.Distance(SeatPoint, _player.transform.position) > range) return;

            if (Time.time - _lastHint > 8f)
            {
                _lastHint = Time.time;
                GameEvents.RaiseSubtitle("【" + label + "】" +
                    (lieOnly ? "躺下休息" : canLie ? "坐下（再按一次躺下）" : "坐下歇一会儿") +
                    "——" + Mobile.MobileInput.UseHint + "。");
            }
            if (Input.GetKeyDown(KeyCode.E) || Mobile.MobileInput.GetDown("Interact"))
                SitController.Sit(_player, this);
        }
    }

    /// <summary>
    /// 坐下 / 躺下 / 睡一会儿 / 起身的执行者：全局只有一处状态，起身时一定能还原。
    /// 挂在玩家身上（第一次坐下时自动补上）。
    ///
    /// 【动作链】动作库里这几段片段是配套的，按顺序接起来才是一个完整的过程：
    ///   站着 ──Sitting──▶ 坐着 ──Lying Down──▶ 躺着 ──Sleeping Idle（循环）
    ///                │                                    │
    ///                └──Sitting 倒放──▶ 站着   ◀──Getting Up──┘
    /// 椅子/凳子只有前半段（坐→站）；床、沙发、躺椅走全程；地上的瑜伽垫没有
    /// "坐"这一步，用 Getting Up 倒放当作"就地躺下"。
    ///
    /// 座面高度差由 HumanoidAnimator 的骨盆锚定吸收（见 BeginRest）：
    /// 动作骨架自带的椅子高度和家里这把椅子不一样，靠锚定对齐，
    /// 不靠手调偏移——手调过一次，换一件家具就又不对了。
    /// </summary>
    public class SitController : MonoBehaviour
    {
        /// <summary>坐姿骨盆在座面之上多高（米，按 2 米身高的角色比例）。</summary>
        const float SitPelvisRise = 0.20f;
        /// <summary>躺姿骨盆在床面之上多高。</summary>
        const float LiePelvisRise = 0.14f;
        /// <summary>片段原速偏慢（坐下 4.4 秒、躺下 5.7 秒），略提速到"从容但不拖"。</summary>
        const float SitSpeed = 1.15f, LieSpeed = 1.3f;
        /// <summary>就地躺下用的片段：Getting Up 倒放就是"站着躺下去"。</summary>
        const string FloorLieClip = "getting up";

        enum Phase { None, SittingDown, Seated, LyingDown, Lying, Rising }

        static SitController _inst;

        /// <summary>正在坐/躺（含起坐过程）——其它交互物件据此避让。</summary>
        public static bool Busy => _inst != null && _inst._phase != Phase.None;

        Sittable _seat;
        Phase _phase;
        Vector3 _standPos;
        Transform _visual;
        Vector3 _visualPos;
        Quaternion _visualRot;
        CharacterController _cc;
        PlayerController _pc;
        Combat.HumanoidAnimator _anim;
        bool _animated;          // 有动作片段（false = 退回到"压低外观"的兜底做法）
        float _phaseT, _phaseDur;
        float _loopAt;           // 下一次重播循环段的时刻（坐稳/睡着之后）
        float _inputLock;        // 刚坐下的一小段时间不接受"起身"，否则一按就弹起来
        Quaternion _turnFrom, _turnTo;   // 坐→躺的转身（沙发要顺着长边躺）
        Vector3 _moveFrom, _moveTo;      // 入座的短距离位移（瞬移到床中间太假）
        float _moveT;
        bool _moveReleased;      // 坐下后是否松过前后键（否则"按着 W 走过来"会立刻起身）
        const float ApproachDur = 0.45f;

        public static void Sit(PlayerController player, Sittable seat)
        {
            if (player == null || seat == null) return;
            if (_inst == null) _inst = player.gameObject.AddComponent<SitController>();
            if (_inst._phase != Phase.None) return;
            _inst.Begin(player, seat);
        }

        /// <summary>重建世界/离开住处时的兜底还原：绝不能让玩家带着"坐着"的状态离开。
        /// **不动玩家位置**——世界正在重建，那时的落点由重建流程决定，
        /// 拿坐下前记的旧坐标去覆盖它，人就被拽回上一个世界的位置了。</summary>
        public static void ForceStand()
        {
            if (_inst == null || _inst._phase == Phase.None) return;
            _inst.Finish(false, false);
        }

        void Begin(PlayerController player, Sittable seat)
        {
            _pc = player;
            _seat = seat;
            _cc = player.GetComponent<CharacterController>();
            _anim = player.GetComponent<Combat.HumanoidAnimator>();
            _visual = player.transform.Find("Visual");
            _standPos = player.transform.position;

            // 坐下的动作自带"退半步坐进去"的位移，髋骨被钉在角色原点上，
            // 所以把角色原点放到座面中心，人就正好坐在座位上。
            // 高度保持站立时的高度：模型的升降由骨盆锚定负责，角色胶囊别乱动。
            if (_cc != null) _cc.enabled = false;
            _moveFrom = _standPos;
            _moveTo = new Vector3(seat.SurfaceCenter.x, _standPos.y, seat.SurfaceCenter.z);
            _moveT = 0f;
            _moveReleased = false;
            Vector3 face = new Vector3(seat.faceDir.x, 0f, seat.faceDir.z);
            if (face.sqrMagnitude > 0.001f)
                player.transform.rotation = Quaternion.LookRotation(face.normalized);
            player.enabled = false;   // 停掉移动/重力，人稳稳待在座位上

            _animated = _anim != null && _anim.HasClip(seat.lieOnly ? FloorLieClip : "sitting");
            if (_anim != null)
            {
                // 移动层归零：控制器停了就不再喂速度，残留的走路权重会在坐姿下面透出来
                _anim.SetLocomotion(0f, false, true, 0f);
            }
            if (_visual != null)
            {
                _visualPos = _visual.localPosition;
                _visualRot = _visual.localRotation;
            }

            if (seat.lieOnly) EnterLie();
            else EnterSit();
        }

        // ---- 各阶段 ----

        void EnterSit()
        {
            _phase = Phase.SittingDown;
            _inputLock = 0.9f;
            if (_animated)
            {
                _anim.BeginRest(_seat.SurfaceY + SitPelvisRise);
                _phaseDur = _anim.PlayRestClip("sitting", false, true, SitSpeed, 0.35f);
            }
            else
            {
                Fallback(false);
                _phaseDur = 0.25f;
            }
            _phaseT = 0f;
            GameEvents.RaiseSubtitle(_seat.canLie
                ? "你坐了下来。" + Mobile.MobileInput.UseHint + "躺下。"
                : "你坐了下来。" + Mobile.MobileInput.UseHint + "起身。");
        }

        void EnterLie()
        {
            _phase = Phase.LyingDown;
            _inputLock = 0.9f;
            // 沙发这类家具躺下要顺着长边转过来：在躺的过程里慢慢转（见 Update），
            // 直接扭 90° 会像被人掰了一下
            _turnFrom = _pc != null ? _pc.transform.rotation : Quaternion.identity;
            Vector3 lf = new Vector3(_seat.LieFace.x, 0f, _seat.LieFace.z);
            _turnTo = lf.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(lf.normalized) : _turnFrom;
            if (_animated)
            {
                // 从站着直接躺（瑜伽垫）时还没进过休息锚定，这里补上
                if (_anim.Resting) _anim.SetRestPelvisY(_seat.SurfaceY + LiePelvisRise);
                else _anim.BeginRest(_seat.SurfaceY + LiePelvisRise);
                // 床/沙发：Lying Down 本来就是"从床沿的坐姿往后躺进去"，接在坐姿后面正好；
                // 地上的垫子：没有对应片段，用 Getting Up 倒放 = 站着躺下去。
                _phaseDur = _seat.lieOnly
                    ? _anim.PlayRestClip(FloorLieClip, true, true, LieSpeed, 0.4f)
                    : _anim.PlayRestClip("lying down", false, true, LieSpeed, 0.4f);
                if (_phaseDur <= 0f)
                    _phaseDur = _anim.PlayRestClip(FloorLieClip, true, true, LieSpeed, 0.4f);
            }
            else
            {
                Fallback(true);
                _phaseDur = 0.25f;
            }
            _phaseT = 0f;
            GameEvents.RaiseSubtitle("你躺了下来。什么都不用做——" +
                Mobile.MobileInput.UseHint + "起身。");
        }

        void BeginRise(bool fromLying)
        {
            _phase = Phase.Rising;
            _phaseT = 0f;
            if (_animated)
            {
                // 从椅子上站起来 = 坐下倒放（腿先撑、身体再立起来）；
                // 从床上/地上起来 = Getting Up（撑起身体），比把躺姿倒放自然得多。
                _phaseDur = fromLying
                    ? _anim.PlayRestClip("getting up", false, false, 1.1f, 0.3f)
                    : _anim.PlayRestClip("sitting", true, false, 1.25f, 0.3f);
                if (_phaseDur <= 0f) _phaseDur = 0.3f;
            }
            else
            {
                _phaseDur = 0.15f;
            }
            GameEvents.RaiseSubtitle("你站了起来。");
        }

        void Update()
        {
            if (_phase == Phase.None) return;
            float dt = Time.deltaTime;
            if (_inputLock > 0f) _inputLock -= dt;
            _phaseT += dt;

            // 骨盆锚定权重：坐下/躺下的过程渐入，起身的过程渐出——
            // 一上来就锚死会把还站着的人按到座面高度（人陷进地板里）
            if (_animated && _anim != null)
            {
                float k = Mathf.Clamp01(_phaseDur > 0.01f ? _phaseT / _phaseDur : 1f);
                float w = _phase == Phase.Rising ? 1f - Mathf.SmoothStep(0f, 1f, k)
                        : _phase == Phase.SittingDown || _phase == Phase.LyingDown
                            ? Mathf.SmoothStep(0f, 1f, k)
                            : 1f;
                _anim.SetRestWeight(w);
            }

            // 入座位移：坐下的动作前半段人还站着，这段时间正好把人挪到座位上
            if (_moveT < ApproachDur && _pc != null)
            {
                _moveT += dt;
                _pc.transform.position = Vector3.Lerp(_moveFrom, _moveTo,
                    Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_moveT / ApproachDur)));
            }

            Restore(dt);

            switch (_phase)
            {
                case Phase.SittingDown:
                    if (_phaseT >= _phaseDur) { _phase = Phase.Seated; _loopAt = 0f; }
                    break;
                case Phase.Seated:
                    LoopTail("sitting", 0.82f);
                    break;
                case Phase.LyingDown:
                    if (_pc != null)
                        _pc.transform.rotation = Quaternion.Slerp(_turnFrom, _turnTo,
                            Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_phaseT / 0.7f)));
                    if (_phaseT >= _phaseDur) { _phase = Phase.Lying; _loopAt = 0f; }
                    break;
                case Phase.Lying:
                    // 睡姿循环：Sleeping Idle 是一段带呼吸起伏的长片段，播完接着播
                    LoopClip("sleeping idle");
                    break;
                case Phase.Rising:
                    if (_phaseT >= _phaseDur) Finish(true);
                    return;
            }

            // 交互键推进姿势（站→坐→躺→站）；跳跃键或重新推摇杆随时直接起身
            if (_inputLock > 0f) return;
            bool lying = _phase == Phase.Lying || _phase == Phase.LyingDown;
            bool moveNow = Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.1f ||
                           Mobile.MobileInput.MoveHeld;
            if (!moveNow) _moveReleased = true;
            if (Input.GetKeyDown(KeyCode.Space) || (moveNow && _moveReleased))
            {
                BeginRise(lying);
                return;
            }
            if (!(Input.GetKeyDown(KeyCode.E) || Mobile.MobileInput.GetDown("Interact"))) return;
            if (_phase == Phase.Seated && _seat != null && _seat.canLie) EnterLie();
            else BeginRise(lying);
        }

        /// <summary>坐稳之后循环播片段末段：完全静止的末帧看着像死机，末段里有细微起伏。</summary>
        void LoopTail(string key, float from01)
        {
            if (!_animated || _anim == null || Time.time < _loopAt) return;
            float len = _anim.PlayRestClip(key, false, true, 0.6f, 0.5f, from01, 1f);
            _loopAt = Time.time + Mathf.Max(0.5f, len - 0.25f);
        }

        void LoopClip(string key)
        {
            if (!_animated || _anim == null || Time.time < _loopAt) return;
            float len = _anim.PlayRestClip(key, false, true, 1f, 0.5f);
            _loopAt = Time.time + Mathf.Max(0.5f, len - 0.3f);
        }

        /// <summary>没有动作片段时的兜底：还是老办法把外观压低/放平，至少姿态对得上。</summary>
        void Fallback(bool lie)
        {
            if (_visual == null) return;
            if (lie)
            {
                _visual.localRotation = Quaternion.Euler(-90f, 0, 0);
                _visual.localPosition = _visualPos +
                    new Vector3(0, _seat.SurfaceY - _standPos.y + 0.35f, 0.55f);
            }
            else
            {
                _visual.localPosition = _visualPos +
                    new Vector3(0, _seat.SurfaceY - _standPos.y + 0.75f, 0);
            }
        }

        /// <summary>休息回复：躺着比坐着快。这是住处该有的作用，不是数值补丁——
        /// 从一场打不过的关卡退出来，回家躺一会儿再出门是这个游戏的节奏。</summary>
        void Restore(float dt)
        {
            if (_pc == null || _pc.Stats == null) return;
            bool lying = _phase == Phase.Lying || _phase == Phase.LyingDown;
            float rate = lying ? 6f : 2.5f;
            _pc.Stats.RestoreMental(rate * dt);
            var st = _pc.Stats;
            st.hp = Mathf.Min(st.maxHp, st.hp + rate * 0.5f * dt);
            GameEvents.RaisePlayerHpChanged(st.hp, st.maxHp);
        }

        void Finish(bool stepAside, bool movePlayer = true)
        {
            if (_anim != null) _anim.EndRest();
            if (_visual != null)
            {
                _visual.localPosition = _visualPos;
                _visual.localRotation = _visualRot;
            }
            if (_pc != null) _pc.enabled = true;
            // 起身站到座位旁边，避免卡在家具里
            if (_pc != null && movePlayer)
            {
                Vector3 side = _standPos;
                if (stepAside && _seat != null)
                {
                    Vector3 f = new Vector3(_seat.faceDir.x, 0, _seat.faceDir.z);
                    if (f.sqrMagnitude < 0.001f) f = Vector3.forward;
                    f = f.normalized;
                    // 退到家具**外面**：床 6.5 米长，只退 1.5 米人还站在床上
                    Vector3 ext = _seat.SurfaceExtents;
                    float reach = Mathf.Abs(f.x) * ext.x + Mathf.Abs(f.z) * ext.z + 1.2f;
                    side = _seat.SurfaceCenter + f * reach;
                    side.y = _standPos.y;
                }
                _pc.transform.position = side;
            }
            if (_cc != null) _cc.enabled = true;
            if (_pc != null) _pc.NotifyTeleported();
            _seat = null;
            _phase = Phase.None;
        }
    }

    /// <summary>
    /// 会动的跑步机。
    ///
    /// 玩家的要求是"跑步机可以正常工作"。做成一个播动画的摆设很容易，但那不叫工作。
    /// 这里按真实跑步机的原理来：**开机后履带往后送**，站上去不动就会被送下去，
    /// 想留在上面就得一直往前跑——于是"跑步"这件事真的发生了，
    /// 而不是站着看一个循环动画。跑的时间会转成体力与意志的训练收益。
    ///
    /// 【这一版补的两件事：真的能"跑"起来】
    /// ① 上一版加了"屋里只走不跑"，而履带速度正好等于走速——玩家在自己家的
    ///    跑步机上最多只能勉强不被送下去，永远跑不起来。站上履带即解开这条限制
    ///    （IndoorZone.SetRunPass），下履带即收回。
    /// ② 履带像真机器一样分档：走 / 慢跑 / 跑。按一次交互键升一档，最高档之后停机。
    ///    最高档 4.6 m/s 略低于奔跑速度，所以全速跑起来能稳稳留在履带上，
    ///    而不是要么被送下去、要么冲出机器。
    /// </summary>
    public class Treadmill : MonoBehaviour
    {
        /// <summary>三个档位的履带速度（走 / 慢跑 / 跑）。最高档略低于 runSpeed(5.2)。</summary>
        static readonly float[] Levels = { 2.4f, 3.6f, 4.6f };
        static readonly string[] LevelNames = { "走", "慢跑", "跑" };

        public float beltSpeed = 2.4f;
        public bool running;
        int _level = -1;      // -1 = 停机

        Transform[] _stripes;
        Transform _belt;
        TextMesh _panel;
        PlayerController _player;
        CharacterController _cc;
        float _lastHint = -99f;
        float _ranSeconds;
        float _rewardTick;
        bool _runPass;      // 已向 IndoorZone 领取"屋里可以跑"的豁免

        public static Treadmill Build(WorldContext ctx, Vector3 at)
        {
            var body = ZoneBuilder.Box(ctx, "TreadBase", at + new Vector3(0, 0.2f, 0),
                new Vector3(1.8f, 0.4f, 4.0f), new Color(0.20f, 0.21f, 0.24f));
            var belt = ZoneBuilder.Box(ctx, "TreadBelt", at + new Vector3(0, 0.44f, -0.2f),
                new Vector3(1.4f, 0.08f, 3.2f), new Color(0.12f, 0.12f, 0.13f));

            var steel = new Color(0.62f, 0.64f, 0.68f);
            for (int s = -1; s <= 1; s += 2)
                VillaKit.Metal(VillaKit.Cyl("TreadArm", at + new Vector3(s * 0.75f, 0.4f, 1.6f),
                    0.06f, 1.3f, steel), steel);
            VillaKit.Metal(VillaKit.CylAxis("TreadRail", at + new Vector3(0, 1.7f, 1.6f),
                0.06f, 1.5f, steel, new Vector3(0, 0, 90f)), steel);
            // 控制面板略向后仰，像真的操作面而不是一块立着的板
            VillaKit.Emit(VillaKit.Deco("TreadPanel", at + new Vector3(0, 1.62f, 1.78f),
                new Vector3(1.4f, 0.5f, 0.08f), new Color(0.18f, 0.22f, 0.28f),
                new Vector3(-22f, 0, 0)), new Color(0.25f, 0.55f, 0.7f), 0.9f);

            var t = body.AddComponent<Treadmill>();
            t._belt = belt.transform;

            // 履带上的横纹：跑起来时它们往后走，一眼能看出机器在转
            var stripes = new Transform[7];
            for (int i = 0; i < stripes.Length; i++)
            {
                var st = ZoneBuilder.Decoration(ctx, "BeltStripe",
                    at + new Vector3(0, 0.49f, -1.6f + i * 0.45f),
                    new Vector3(1.3f, 0.02f, 0.12f), new Color(0.32f, 0.34f, 0.36f));
                stripes[i] = st.transform;
            }
            t._stripes = stripes;

            var panelGo = new GameObject("TreadReadout");
            panelGo.transform.position = at + new Vector3(0, 1.62f, 1.64f);
            // 【朝向】TextMesh 的字面在自己的 -Z 侧（广告牌的写法就是让 forward 背对镜头）。
            // 跑步的人在履带上（面板的 -z 那边），所以 forward 要朝 +z ——
            // 上一版转了 180°，字正好背对读它的人，读出来是镜像的（"文字反过来"）。
            panelGo.transform.rotation = Quaternion.identity;
            // 读数走 WorldText：内置字体材质不做深度测试，字会穿墙飘到别的房间去
            t._panel = WorldText.Attach(panelGo, "跑步机 · 待机", 46, 0.05f,
                new Color(0.55f, 0.95f, 0.85f));

            return t;
        }

        /// <summary>机器没了/被禁用时把跑步豁免交回去，免得"屋里可以跑"漏在外面。</summary>
        void OnDisable()
        {
            if (!_runPass) return;
            _runPass = false;
            IndoorZone.SetRunPass(false);
        }

        void Update()
        {
            if (_player == null)
            {
                _player = FindObjectOfType<PlayerController>();
                if (_player == null) return;
                _cc = _player.GetComponent<CharacterController>();
            }

            Vector3 beltAt = _belt != null ? _belt.position : transform.position;
            Vector3 p = _player.transform.position;
            bool onBelt = Mathf.Abs(p.x - beltAt.x) < 1.0f &&
                          Mathf.Abs(p.z - beltAt.z) < 1.9f &&
                          p.y - beltAt.y > -0.4f && p.y - beltAt.y < 2.4f;
            bool near = Vector3.Distance(p, transform.position) < 3.4f;

            // 站上履带就解开"屋里只走不跑"——履带速度等于走速，不许跑等于器材是坏的。
            // 下履带立刻收回：出了这台机器，家里还是只走不跑。
            if (onBelt != _runPass)
            {
                _runPass = onBelt;
                IndoorZone.SetRunPass(onBelt);
                if (onBelt && running)
                    GameEvents.RaiseSubtitle("上履带了——在这台机器上可以放开跑。");
            }

            if (near && Time.time - _lastHint > 8f)
            {
                _lastHint = Time.time;
                GameEvents.RaiseSubtitle(running
                    ? "【跑步机】" + LevelNames[Mathf.Clamp(_level, 0, LevelNames.Length - 1)] +
                      "档——" + Mobile.MobileInput.UseHint + "升档/停机。"
                    : "【跑步机】" + Mobile.MobileInput.UseHint + "开机，然后站到履带上。");
            }
            if (near && !SitController.Busy &&
                (Input.GetKeyDown(KeyCode.E) || Mobile.MobileInput.GetDown("Interact")))
            {
                // 一路升档，到顶再按就停机——一个按钮走完真机器的整套操作
                _level++;
                if (_level >= Levels.Length) _level = -1;
                running = _level >= 0;
                if (running)
                {
                    beltSpeed = Levels[_level];
                    GameEvents.RaiseSubtitle("跑步机 · " + LevelNames[_level] + "档（" +
                        beltSpeed.ToString("F1") + " m/s）——站上履带跟住它。");
                }
                else
                {
                    GameEvents.RaiseSubtitle("跑步机停机。");
                }
            }

            if (!running)
            {
                if (_panel != null) _panel.text = "跑步机 · 待机";
                return;
            }

            float dt = Time.deltaTime;
            // 履带纹往后走并循环
            if (_stripes != null)
                foreach (var st in _stripes)
                {
                    if (st == null) continue;
                    var lp = st.position;
                    lp.z -= beltSpeed * dt;
                    if (lp.z < beltAt.z - 1.7f) lp.z += 3.15f;
                    st.position = lp;
                }

            if (onBelt && _cc != null && _cc.enabled)
            {
                // 履带把人往后送：站着不动就会被送下去，想留下就得跑
                _cc.Move(new Vector3(0, 0, -beltSpeed * dt));
                _ranSeconds += dt;
                _rewardTick += dt;
                if (_rewardTick > 5f)
                {
                    _rewardTick = 0f;
                    if (_player.Stats != null)
                    {
                        _player.Stats.RestoreMental(4f);
                        GameEvents.RaiseSubtitle("跑了 " + Mathf.RoundToInt(_ranSeconds) + " 秒——意志稳了一点。");
                    }
                }
            }

            if (_panel != null)
                _panel.text = LevelNames[Mathf.Clamp(_level, 0, LevelNames.Length - 1)] +
                              "档 " + beltSpeed.ToString("F1") + " m/s · 已跑 " +
                              Mathf.RoundToInt(_ranSeconds) + " 秒";
        }
    }
}
