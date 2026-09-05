using UnityEngine;
using AdversityRoad.Combat;
using AdversityRoad.Core;
using AdversityRoad.Player;
using AdversityRoad.Shame;

namespace AdversityRoad.AI
{
    /// <summary>
    /// 身份钉兵（8.6.3）：专职施放指认招式，truthTag = true。唯一解是认领不终审。
    ///
    /// 【它不是一个高伤害单位】
    /// 它的全部威胁在于那一枚钉：把「我做了一件事」翻译成「我是一种人」。
    /// 所以它的物理伤害很低，出手很慢，前摇很长——因为这一招是要被读懂的，不是要打中人的。
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class NailAccuser : MonoBehaviour
    {
        public float accuseInterval = 11f;
        /// <summary>单次遭遇的指认次数上限（8.7 预算：不超过 5 次）。</summary>
        public int maxAccusations = 5;

        EnemyController _ec;
        Transform _player;
        float _cd = 4f;
        int _fired;

        void Awake() => _ec = GetComponent<EnemyController>();

        void Start()
        {
            var p = AdversityRoad.Core.ActorRegistry.Player;
            if (p != null) _player = p.transform;
        }

        void Update()
        {
            if (_ec == null || _ec.State == EnemyState.Dead || _player == null) return;
            if (_fired >= maxAccusations) return;
            if (Vector3.Distance(transform.position, _player.position) > 16f) return;

            float interval = accuseInterval;
            // 连续失败保护生效时，指认频率同步降下来（8.12.1）
            if (ShameLineController.ChallengeCeilingActive) interval *= 1.6f;
            _cd -= Time.deltaTime;
            if (_cd > 0f) return;
            _cd = interval;

            var claim = ClaimRegistry.Draw(_ec.profile != null ? _ec.profile.enemyId : "", true);
            if (claim == null)
            {
                // 指控全被认领过了：它没词了。这正是玩家想要的结果，不是 Bug
                if (_ec.dialogue != null) _ec.dialogue.Show("……", 1.6f);
                enabled = false;
                return;
            }
            var own = OwnNotFinalSystem.Instance;
            if (own != null && own.Accuse(_ec, claim,
                    _ec.profile != null ? _ec.profile.mentalDamage : 14f))
                _fired++;
        }
    }

    /// <summary>
    /// 新的把柄（8.5.3）：每次玩家隐瞒后生成，专门针对上一条隐瞒内容发起指认。
    /// 它是「隐瞒复利」在敌人层面的具体形态——盖住一次，场上就多站一个。
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class NewHandle : MonoBehaviour
    {
        EnemyController _ec;
        float _cd = 6f;

        void Awake()
        {
            _ec = GetComponent<EnemyController>();
            var accuser = gameObject.AddComponent<NailAccuser>();
            accuser.accuseInterval = 14f;
            accuser.maxAccusations = 2;
        }

        void Update()
        {
            if (_ec == null || _ec.State == EnemyState.Dead) return;
            _cd -= Time.deltaTime;
            if (_cd > 0f) return;
            _cd = 12f;
            if (_ec.dialogue != null)
                _ec.dialogue.Show("上次那句话，你自己记得吗？", 2.4f);
        }
    }

    /// <summary>
    /// 讨好回声（8.5.3）：玩家自身讨好行为的具象化。讨好度越高它越强。
    /// 【降低讨好度是唯一削弱方式，无法直接击杀】——所以它有血线保护。
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class AppeaseEcho : MonoBehaviour
    {
        EnemyController _ec;
        float _next;
        bool _dissipating;

        void Awake() => _ec = GetComponent<EnemyController>();

        void Start()
        {
            // 方案 8.5.3 的原文是"降低讨好度是唯一削弱方式，**无法直接击杀**"。
            // 上一版我拿血线卡在 15% 来表达它，那是最坏的做法：玩家看着血条掉到 15%
            // 然后纹丝不动，读出来就是"这敌人有无限的生命"。
            // 现在它不画血条、伤害不进血（照常吃硬直），头顶常驻写着什么才管用。
            _ec.undying = true;
            _ec.undyingHint = "讨好回声不是打倒的——它是你自己的讨好行为变的。" +
                              "别再顺从应答，讨好度掉下去，它自己就散了。";
            _ec.emotionOverride = "打不倒 · 讨好度降到 0 它才会散";
            if (_ec.dialogue != null) _ec.dialogue.Show("你刚才不是答应得好好的吗？", 2.6f);
        }

        void Update()
        {
            if (_ec == null || _ec.State == EnemyState.Dead) return;
            if (Time.time < _next) return;
            _next = Time.time + 1f;

            var appease = AppeasementSystem.Instance;
            float t = appease != null ? Mathf.Clamp01(appease.Value / 100f) : 0f;

            // 讨好度越高它越强；讨好度归零时它几乎不构成威胁（但仍在场上）
            _ec.externalDamageMult = Mathf.Lerp(1.4f, 0.5f, t);
            if (_ec.profile != null)
                _ec.profile.mentalDamage = Mathf.Lerp(4f, 18f, t);
            // 讨好度归零 = 它没有来源了，于是**散场**——不是被打死，是没得可依附。
            // 这是方案给的唯一削弱方式走到底的结果，也是玩家真正能拿到的那个交代：
            // 场上少一个敌人，而且是靠"不再顺从"换来的。
            if (t <= 0.02f && !_dissipating)
            {
                _dissipating = true;
                GameEvents.RaiseSubtitle("讨好回声散了——它靠的从来不是自己的力气。" +
                    "你停止顺从的那一刻，它就没有东西可以依附了。");
                Destroy(gameObject, 0.6f);
            }
        }
    }

    /// <summary>
    /// 旁观耳语者（8.5.3）：背景压力单位，**不攻击**，仅提升 Exposure 增速。
    /// 反制是离开范围或用聚光灯校准看清它真实的注意力值。
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class BystanderWhisper : MonoBehaviour
    {
        public float radius = 12f;
        public float exposurePerSec = 2.2f;

        EnemyController _ec;
        Transform _player;

        void Awake() => _ec = GetComponent<EnemyController>();

        void Start()
        {
            // 它不打人——但**打得动**。用 passive 而不是 pacified：
            // pacified 连伤害都免疫，玩家挥刀过去只会看到"无需再战"，读作敌人打不死。
            _ec.passive = true;
            var p = AdversityRoad.Core.ActorRegistry.Player;
            if (p != null) _player = p.transform;
            gameObject.AddComponent<Shame.WhisperNode>().rank = 0;
        }

        void Update()
        {
            if (_player == null || _ec == null || _ec.State == EnemyState.Dead) return;
            if (Vector3.Distance(transform.position, _player.position) > radius) return;
            var ex = ExposureSystem.Instance;
            if (ex != null) ex.Add(exposurePerSec * Time.deltaTime, null);
        }
    }

    /// <summary>
    /// 侧目者（8.6.3）：静止单位，头部朝向生成视线锥；不主动攻击。
    /// 反制不是打倒它，是路线规划与聚光灯校准。
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class SideGlancer : MonoBehaviour
    {
        public float coneAngle = 62f;
        public float coneRange = 16f;
        public float exposureRate = 8f;
        /// <summary>头部左右缓慢摆动的幅度（度）。注视是活的，但永远可读。</summary>
        public float sweep = 28f;

        // 锥体编号自己发：Unity 6000.5 起 GetInstanceID() 被标记为 obsolete-as-error
        // （CS0619），而这里要的只是一个场内唯一的名字，不需要引擎的实例 id。
        static int _coneSeq;

        EnemyController _ec;
        GazeCone _cone;
        float _phase;
        Quaternion _base;

        void Awake() => _ec = GetComponent<EnemyController>();

        void Start()
        {
            _ec.passive = true;      // 不主动攻击，但可以被打倒（见 EnemyController.passive）
            _base = transform.rotation;

            var go = new GameObject("GazeCone");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            _cone = go.AddComponent<GazeCone>();
            _cone.headSource = transform;
            _cone.data = new GazeConeData
            {
                coneId = "cone_" + (++_coneSeq),
                ownerNpcId = _ec.profile != null ? _ec.profile.enemyId : "",
                angle = coneAngle,
                range = coneRange,
                visibility = 1f,
                exposureRate = exposureRate,
                isPersistent = true,
            };
            _phase = Random.value * 10f;
            gameObject.AddComponent<Shame.WhisperNode>().rank = 1;
        }

        bool _relayScheduled;

        void Update()
        {
            if (_ec == null) return;

            // 被打倒了：这道视线不是消失，是移开一会儿。20 秒后有人从别处补上。
            if (_ec.State == EnemyState.Dead)
            {
                if (_relayScheduled) return;
                _relayScheduled = true;
                var gaze = GazeConeSystem.Instance;
                if (gaze != null)
                    gaze.ScheduleRelay(transform.position,
                        transform.position + transform.forward * coneRange);
                if (_cone != null) _cone.gameObject.SetActive(false);
                return;
            }

            _phase += Time.deltaTime * 0.35f;
            transform.rotation = _base * Quaternion.Euler(0f, Mathf.Sin(_phase) * sweep, 0f);
        }
    }

    /// <summary>
    /// 放大镜围观者（8.6.3）：把任意一次玩家失误放大为全场事件，Exposure +15。
    /// 反制是**在它读条完成前完成目标动作**——不是把它打死。
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class MagnifierOnlooker : MonoBehaviour
    {
        public float castTime = 4.5f;
        public float interval = 16f;

        EnemyController _ec;
        float _cd = 8f;
        float _castUntil = -1f;
        int _doneAtCastStart;

        void Awake() => _ec = GetComponent<EnemyController>();

        void Start()
        {
            // 方案 8.6.3 给它的行为只有一条：「把任意一次玩家失误放大为全场事件，
            // Exposure +15」，反制是「在其读条完成前完成目标动作」。**没有近战**。
            // 上一版没设 passive，于是它一边读条一边追着玩家打——
            // 而玩家这时正需要站定长按目标动作，等于被逼着二选一。
            _ec.passive = true;
            _ec.emotionOverride = "读条中 · 在它说完之前把事做完";
            gameObject.AddComponent<Shame.WhisperNode>().rank = 2;
        }

        void Update()
        {
            if (_ec == null || _ec.State == EnemyState.Dead) return;
            var ctl = ShameLineController.Instance;

            if (_castUntil > 0f)
            {
                if (ctl != null && ctl.ObjectivesDone > _doneAtCastStart)
                {
                    _castUntil = -1f;
                    if (_ec.dialogue != null) _ec.dialogue.Show("……算了。", 1.8f);
                    GameEvents.RaiseSubtitle("你在它说完之前把事做完了——这一次没有被放大。");
                    return;
                }
                if (Time.time < _castUntil) return;
                _castUntil = -1f;
                var ex = ExposureSystem.Instance;
                if (ex != null) ex.Add(15f, "「你们看见了吗？」——一次小事被放大成了全场的事");
                GameAudio.Play(GameAudio.Sfx.Alert, 0.7f);
                return;
            }

            _cd -= Time.deltaTime;
            if (_cd > 0f) return;
            _cd = interval;
            _castUntil = Time.time + castTime;
            _doneAtCastStart = ctl != null ? ctl.ObjectivesDone : 0;
            if (_ec.dialogue != null) _ec.dialogue.Show("你们看见了吗——", castTime);
            GameEvents.RaiseSubtitle("放大镜围观者开始读条（" + castTime.ToString("0.0") +
                "s）——在它说完之前把手上的事做完。");
        }
    }

    /// <summary>
    /// 后排低语组（8.6.3）：双人单位，交替发声；击倒其一，另一人接管并加速。
    /// 破链只能拖延，不能解决。
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class BackRowPair : MonoBehaviour
    {
        public BackRowPair partner;

        EnemyController _ec;
        float _next;
        bool _tookOver;

        void Awake() => _ec = GetComponent<EnemyController>();

        void Start()
        {
            // 方案 8.6.3：「双人单位，**交替发声**；击倒其一，另一人接管并加速」。
            // 它的招式是发声，不是动手——所以 passive（不追不打），
            // 但照常掉血、照常能被击倒，"击倒其一"这条机制才成立。
            _ec.passive = true;
            _ec.emotionOverride = "交替发声 · 击倒其一，另一人会接管";
            gameObject.AddComponent<Shame.WhisperNode>().rank = 0;
        }

        void Update()
        {
            if (_ec == null) return;
            if (_ec.State == EnemyState.Dead) return;

            if (!_tookOver && partner != null && partner._ec != null &&
                partner._ec.State == EnemyState.Dead)
            {
                _tookOver = true;
                if (_ec.profile != null) _ec.profile.aggression =
                    Mathf.Clamp01(_ec.profile.aggression * 1.5f);
                GameEvents.RaiseSubtitle("另一个接过去了，而且说得更快——两个人的时候，闭嘴不是靠打。");
            }

            if (Time.time < _next) return;
            _next = Time.time + (_tookOver ? 4.5f : 7f);
            if (_ec.dialogue != null) _ec.dialogue.Show("（后排传来一句听不清的话）", 2.2f);
            var ex = ExposureSystem.Instance;
            if (ex != null) ex.Add(3f, null);
        }
    }

    /// <summary>
    /// 心虚投影（8.6.3）：玩家自身影子。
    ///
    /// 【它读的是玩家最近的回避习惯，而不是玩家是谁】
    /// 取 Vulnerability Graph 里最高频的回避行为标签，抢先占住那条路——
    /// 用的全是可观察的游戏行为标签，不含任何玩家在游戏外输入的原文（8.12.3）。
    /// 不可击杀；认领不终审可使其透明化 12 秒。
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class GuiltProjection : MonoBehaviour
    {
        EnemyController _ec;
        Transform _player;
        float _transparentUntil = -1f;
        float _nextMove;
        string _readTag = "";

        void Awake() => _ec = GetComponent<EnemyController>();

        void Start()
        {
            // 方案 8.6.3 对它写的是"**不可击杀**；认领不终审可使其透明化 12 秒"。
            // 上一版我给了它 20% 的血线，又在透明期把血线拿掉当作"可以打倒的窗口"——
            // 那个窗口不在方案里，而那条掉到 20% 就卡住的血条正是"无限生命"的来源。
            // 现在它不画血条，认领带来的回报是那 12 秒它抢不到你的位。
            _ec.undying = true;
            _ec.undyingHint = "心虚投影打不倒——它就是你自己的回避习惯。" +
                              "用「认领不终审」接住指认，它会透明 12 秒，抢不到你的路线。";
            _ec.emotionOverride = "打不倒 · 认领可使其透明 12 秒";
            var p = AdversityRoad.Core.ActorRegistry.Player;
            if (p != null) _player = p.transform;
            _readTag = TopAvoidanceTag();
            if (_ec.dialogue != null)
                _ec.dialogue.Show(string.IsNullOrEmpty(_readTag)
                    ? "我知道你要往哪边走。" : "你又要「" + _readTag + "」了。", 3f);
            ShameLine.Changed += OnShameChanged;
        }

        void OnDestroy() { ShameLine.Changed -= OnShameChanged; }

        void OnShameChanged()
        {
            // 一次成功的认领会让它透明 12 秒：被认领之后，心虚就没有落点了
            var nails = IdentityNailSystem.Instance;
            if (nails != null && nails.Count == 0 && ShameLine.Data.ownCount > _lastOwnCount)
            {
                _lastOwnCount = ShameLine.Data.ownCount;
                _transparentUntil = Time.time + 12f;
                if (_ec != null)
                {
                    _ec.passive = true;     // 透明期它不再抢位，也不出手
                    _ec.emotionOverride = "透明中 · 这 12 秒它抢不到你的路线";
                }
                GameEvents.RaiseSubtitle("心虚投影透明了 12 秒——认领之后，它没有东西可以预判。");
            }
        }

        int _lastOwnCount;

        static string TopAvoidanceTag()
        {
            string best = "";
            int bestSamples = 0;
            foreach (var e in Adversity.AdversityProfile.Vulnerabilities)
            {
                if (e == null || e.sampleCount <= bestSamples) continue;
                if (string.IsNullOrEmpty(e.behaviorTag)) continue;
                best = e.behaviorTag;
                bestSamples = e.sampleCount;
            }
            // 样本 <3 只作为假设：不足以拿来抢位，就退回一句不指向任何人的通用台词
            return bestSamples >= 3 ? best : "";
        }

        bool _dodgePrev;

        void Update()
        {
            if (_ec == null || _player == null || _ec.State == EnemyState.Dead) return;

            if (_transparentUntil > 0f && Time.time >= _transparentUntil)
            {
                _transparentUntil = -1f;
                _ec.passive = false;
                _ec.emotionOverride = "打不倒 · 认领可使其透明 12 秒";
            }
            if (Time.time < _transparentUntil) { _dodgePrev = false; return; }

            var pc = _player.GetComponent<PlayerController>();
            if (pc == null) return;

            // 【怎么才算"抢先执行"】
            // 不去每帧改它的导航目标——EnemyController 自己每帧都在驱动同一个 Agent，
            // 两边抢方向盘的结果是谁也走不成，看上去只是在原地抽搐。
            // 改成在玩家**开始回避的那一瞬间**一步落到落点上：它到得比你早，
            // 这一次翻滚就没能把你带出去。这才读得出"它在预判你"。
            bool dodging = pc.IsDodging;
            bool dodgeStart = dodging && !_dodgePrev;
            _dodgePrev = dodging;
            if (!dodgeStart || Time.time < _nextMove) return;
            _nextMove = Time.time + 9f;

            Vector3 dir = pc.StickWorldDir.sqrMagnitude > 0.01f
                ? pc.StickWorldDir.normalized : -_player.forward;
            Vector3 landing = _player.position + dir * 3.4f;
            var agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent == null || !agent.isOnNavMesh) return;
            if (!UnityEngine.AI.NavMesh.SamplePosition(landing, out var hit, 4f,
                    UnityEngine.AI.NavMesh.AllAreas)) return;

            agent.Warp(hit.position);
            transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);
            CombatFeedback.ShockRing(hit.position, new Color(0.35f, 0.3f, 0.45f), 2.2f);
            if (_ec.dialogue != null)
                _ec.dialogue.Show(string.IsNullOrEmpty(_readTag)
                    ? "我早就在这儿了。" : "「" + _readTag + "」——你每次都是这样。", 2.4f);
        }
    }

    /// <summary>
    /// 伪装同学（8.6.3）：外形与普通 NPC 一致，接近后转为敌对。
    ///
    /// 【遵 §6.6：必须有明确的敌人识别信号，不得无预警敌化】
    /// 所以这里先给 1.2 秒的转身 + 台词 + 头顶状态条，再进入敌对——
    /// "被偷袭"可以是压力，"看不出来"不可以。
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class DisguisedClassmate : MonoBehaviour
    {
        public float revealRange = 6f;
        public float revealLead = 1.2f;

        EnemyController _ec;
        Transform _player;
        float _revealAt = -1f;
        bool _hostile;

        void Awake() => _ec = GetComponent<EnemyController>();

        void Start()
        {
            // 还没敌化时是路人：不动手、也不被排进战斗，但**可以被打**——
            // 玩家先动手时它必须当场给出识别信号并敌化，而不是回一句"无需再战"。
            _ec.passive = true;
            if (_ec.statusBar != null) _ec.statusBar.gameObject.SetActive(false);
            var p = AdversityRoad.Core.ActorRegistry.Player;
            if (p != null) _player = p.transform;
        }

        void Update()
        {
            if (_hostile || _ec == null || _player == null) return;

            if (_revealAt < 0f)
            {
                // 玩家先动手：立刻走识别信号，不让"我打了它却没反应"这一秒出现
                bool struck = _ec.HpRatio < 0.999f;
                if (!struck && Vector3.Distance(transform.position, _player.position) > revealRange)
                    return;
                _revealAt = Time.time + revealLead;
                if (_ec.dialogue != null) _ec.dialogue.Show("……我一直都知道。", revealLead);
                if (_ec.statusBar != null) _ec.statusBar.gameObject.SetActive(true);
                GameAudio.Play(GameAudio.Sfx.Alert, 0.65f);
                GameEvents.RaiseSubtitle("有人转过身来了——这是它的识别信号，接下来才是敌对。");
                return;
            }
            if (Time.time < _revealAt) return;
            _hostile = true;
            _ec.passive = false;
            _ec.holdPosition = false;
            _ec.provoked = true;
        }
    }

    /// <summary>
    /// 每周追问者（8.5.3）：定时出现、限制移动范围、提问链。**不发生正面战斗**。
    /// 反制是事实之刃（对模糊条款）与边界盾（对追加条件）。
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class WeeklyInquirer : MonoBehaviour
    {
        public float leashRadius = 9f;
        /// <summary>超出这个距离就【判定追问结束】，无条件解除减速。
        /// 没有这条时，leashRadius 之外一律减速＝玩家走到天边、换了章节，
        /// 这条 0.55 依然挂在身上——那不是拴绳，是全局永久减速。</summary>
        public float releaseRadius = 22f;

        EnemyController _ec;
        Transform _player;
        Vector3 _anchor;
        float _next;

        void Awake() => _ec = GetComponent<EnemyController>();

        void Start()
        {
            _ec.passive = true;          // 追问本身就是攻击，不需要动手——但它挨得住打
            _anchor = transform.position;
            var p = AdversityRoad.Core.ActorRegistry.Player;
            if (p != null) _player = p.transform;
        }

        void Update()
        {
            if (_ec == null || _player == null) return;
            var pc = _player.GetComponent<PlayerController>();

            // 【死了要放人】原来这里是 `|| _ec.State == Dead` 直接 return，
            // 于是追问者一死，最后一帧登记的减速就再也没人撤——永久挂着。
            if (_ec.State == EnemyState.Dead)
            {
                if (pc != null) pc.ClearSlow(this);
                return;
            }

            // 限制移动范围：站在这一段里，玩家跑不掉，但也没有人动手
            float d = Vector3.Distance(_player.position, _anchor);
            if (pc != null)
            {
                // 【拴绳必须有外沿】原来是 d > leashRadius 就减速，没有上界。
                // 拴绳的语义是"在这一段里走不快"，可它写出来的效果是
                // **离得越远越拴得住**——玩家走出这场追问、走出这个区域、
                // 甚至进了下一章，0.55 都还挂着。实机三张截图分属三个不同章节，
                // 全都显示"减速×0.55"，就是这条。
                // 超出 releaseRadius＝人已经走掉了，这场追问结束，无条件放人。
                if (d > leashRadius && d <= releaseRadius) pc.SetSlow(this, 0.55f);
                else pc.ClearSlow(this);
            }

            if (Time.time < _next) return;
            _next = Time.time + 6.5f;
            if (_ec.dialogue != null) _ec.dialogue.Show("这周呢？", 2.2f);
        }

        // OnDisable 也要放人：切场景/对象被禁用时 OnDestroy 不一定跑得到玩家还在的那一刻
        void OnDisable() => Release();
        void OnDestroy() => Release();

        void Release()
        {
            if (_player == null) return;
            var pc = _player.GetComponent<PlayerController>();
            if (pc != null) pc.ClearSlow(this);
        }
    }
}
