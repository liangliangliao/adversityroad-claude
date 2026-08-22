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
            var p = FindObjectOfType<PlayerController>();
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

        void Awake() => _ec = GetComponent<EnemyController>();

        void Start()
        {
            _ec.minHpFloor = 0.15f;
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
            if (t <= 0.02f && !_ec.pacified)
            {
                _ec.pacified = true;
                GameEvents.RaiseSubtitle("讨好回声安静下来了——它靠的从来不是自己的力气。");
            }
            else if (t > 0.02f) _ec.pacified = false;
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
            _ec.pacified = true;      // 它不打人，也打不动
            _ec.holdPosition = true;
            var p = FindObjectOfType<PlayerController>();
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

        EnemyController _ec;
        GazeCone _cone;
        float _phase;
        Quaternion _base;

        void Awake() => _ec = GetComponent<EnemyController>();

        void Start()
        {
            _ec.pacified = true;
            _ec.holdPosition = true;
            _base = transform.rotation;

            var go = new GameObject("GazeCone");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            _cone = go.AddComponent<GazeCone>();
            _cone.headSource = transform;
            _cone.data = new GazeConeData
            {
                coneId = "cone_" + GetInstanceID(),
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

        void Update()
        {
            if (_ec == null || _ec.State == EnemyState.Dead) return;
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

        void Start() => gameObject.AddComponent<Shame.WhisperNode>().rank = 2;

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

        void Start() => gameObject.AddComponent<Shame.WhisperNode>().rank = 0;

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
            _ec.minHpFloor = 0.2f;      // 不可击杀
            var p = FindObjectOfType<PlayerController>();
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
                if (_ec != null) _ec.pacified = true;
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

        void Update()
        {
            if (_ec == null || _player == null || _ec.State == EnemyState.Dead) return;

            if (_transparentUntil > 0f && Time.time >= _transparentUntil)
            {
                _transparentUntil = -1f;
                _ec.pacified = false;
            }
            if (Time.time < _transparentUntil) return;

            // 抢占回避路线：站到玩家背后的那条退路上
            if (Time.time < _nextMove) return;
            _nextMove = Time.time + 2.4f;
            Vector3 behind = _player.position - _player.forward * 4.5f;
            var agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null && agent.isOnNavMesh) agent.SetDestination(behind);
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
            _ec.pacified = true;
            _ec.holdPosition = true;
            if (_ec.statusBar != null) _ec.statusBar.gameObject.SetActive(false);
            var p = FindObjectOfType<PlayerController>();
            if (p != null) _player = p.transform;
        }

        void Update()
        {
            if (_hostile || _ec == null || _player == null) return;

            if (_revealAt < 0f)
            {
                if (Vector3.Distance(transform.position, _player.position) > revealRange) return;
                _revealAt = Time.time + revealLead;
                if (_ec.dialogue != null) _ec.dialogue.Show("……我一直都知道。", revealLead);
                if (_ec.statusBar != null) _ec.statusBar.gameObject.SetActive(true);
                GameAudio.Play(GameAudio.Sfx.Alert, 0.65f);
                GameEvents.RaiseSubtitle("有人转过身来了——这是它的识别信号，接下来才是敌对。");
                return;
            }
            if (Time.time < _revealAt) return;
            _hostile = true;
            _ec.pacified = false;
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

        EnemyController _ec;
        Transform _player;
        Vector3 _anchor;
        float _next;

        void Awake() => _ec = GetComponent<EnemyController>();

        void Start()
        {
            _ec.pacified = true;         // 追问本身就是攻击，不需要动手
            _anchor = transform.position;
            var p = FindObjectOfType<PlayerController>();
            if (p != null) _player = p.transform;
        }

        void Update()
        {
            if (_ec == null || _player == null || _ec.State == EnemyState.Dead) return;

            // 限制移动范围：站在这一段里，玩家跑不掉，但也没有人动手
            float d = Vector3.Distance(_player.position, _anchor);
            var pc = _player.GetComponent<PlayerController>();
            if (pc != null)
            {
                if (d > leashRadius) pc.SetSlow(this, 0.55f);
                else pc.ClearSlow(this);
            }

            if (Time.time < _next) return;
            _next = Time.time + 6.5f;
            if (_ec.dialogue != null) _ec.dialogue.Show("这周呢？", 2.2f);
        }

        void OnDestroy()
        {
            if (_player == null) return;
            var pc = _player.GetComponent<PlayerController>();
            if (pc != null) pc.ClearSlow(this);
        }
    }
}
