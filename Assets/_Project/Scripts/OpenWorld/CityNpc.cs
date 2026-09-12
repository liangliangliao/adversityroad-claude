using UnityEngine;
using UnityEngine.AI;
using AdversityRoad.Core;
using AdversityRoad.World;

namespace AdversityRoad.OpenWorld
{
    /// <summary>NPC 类型（方案 6.6）。</summary>
    public enum NpcKind
    {
        Ordinary,     // 普通市民：绝不因为玩家的心理主题被默认敌化
        Quest,        // 任务 NPC
        Ally,         // 盟友
        Neutral,      // 中立
        Challenge,    // 挑战型：会主动搭话施压，但**先亮明身份**
        Disguised,    // 伪装敌人：转为敌对前必须有清晰可读的前兆
        Projection    // 心理投影：只在逆境层/内心层出现
    }

    /// <summary>
    /// 世界生活周期（方案 6.6）：NPC 拥有简单日程——居住 → 出行 → 工作/活动 → 返回。
    ///
    /// 公平性红线：普通 NPC 不因为玩家的心理主题被默认敌化；
    /// 挑战型与伪装型 NPC 在转为敌对之前，必须给出**清晰可读的识别信号**
    /// （头顶标记 + 一句预告台词），否则就是作弊感的来源（方案 24 风险表）。
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class CityNpc : MonoBehaviour
    {
        public NpcKind kind = NpcKind.Ordinary;
        public Vector3 homeAnchor;
        public Vector3 workAnchor;
        public Vector3 leisureAnchor;

        NavMeshAgent _agent;
        DayNightCycle _clock;
        Combat.HumanoidAnimator _anim;
        int _phase = -1;
        float _nextCheck;
        bool _flagged;

        public static CityNpc Attach(GameObject go, NpcKind kind,
            Vector3 home, Vector3 work, Vector3 leisure)
        {
            var npc = go.AddComponent<CityNpc>();
            npc.kind = kind;
            npc.homeAnchor = home;
            npc.workAnchor = work;
            npc.leisureAnchor = leisure;
            return npc;
        }

        void Start()
        {
            _agent = GetComponent<NavMeshAgent>();
            _agent.speed = Random.Range(1.3f, 2.6f);
            _clock = FindObjectOfType<DayNightCycle>();
            _anim = GetComponent<Combat.HumanoidAnimator>();
        }

        void Update()
        {
            if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh) return;

            // 步态：把 NavMesh 的实际移速折算成 0-1 喂给骨骼动画，
            // 不喂就是一具站姿僵直、贴着地面平移的躯体（"滑行的人"）
            if (_anim != null)
            {
                float v = _agent.velocity.magnitude;
                _anim.SetLocomotion(Mathf.Clamp01(v / 3.2f), false, true, v);
                _anim.SetArmed(false);   // 路人是平民，用空手那一套
            }

            IdleVariety();

            if (Time.time < _nextCheck) return;
            _nextCheck = Time.time + 2.5f;

            int phase = PhaseNow();
            if (phase != _phase)
            {
                _phase = phase;
                Vector3 target = phase == 0 ? homeAnchor : phase == 1 ? workAnchor : leisureAnchor;
                Vector2 jitter = Random.insideUnitCircle * 6f;
                if (NavMesh.SamplePosition(target + new Vector3(jitter.x, 0, jitter.y),
                        out NavMeshHit hit, 8f, NavMesh.AllAreas))
                    _agent.SetDestination(hit.position);
            }

            MaybeFlagHostileIntent();
        }

        float _nextIdleAct;

        /// <summary>
        /// 站定时偶尔做点别的：一街人全是同一个待机循环，看久了就是一排复制粘贴。
        ///
        /// 按时段挑动作而不是随机挑——傍晚活动时段有人在跳舞、白天办事时段
        /// 有人在讲话、夜里举着灯，时段本身就是"这个人此刻在干什么"的解释。
        /// 只在真正停下来时播（速度阈值），走着播会和移动层打架。
        /// </summary>
        void IdleVariety()
        {
            if (_anim == null) return;
            if (_agent.velocity.sqrMagnitude > 0.04f) return;   // 还在走，不插动作
            if (Time.time < _nextIdleAct) return;
            // 每个人的间隔各不相同，否则一街人会**同时**做同一个动作，比不做还怪
            _nextIdleAct = Time.time + Random.Range(9f, 22f);

            switch (_phase)
            {
                case 2:   // 傍晚活动：跳舞 / 聊天
                    _anim.PlayFirstClip(1f, 0.3f,
                        Random.value < 0.35f ? "dance_loop" : "idle_talking_loop");
                    break;
                case 1:   // 白天办事：讲话
                    _anim.PlayFirstClip(1f, 0.3f, "idle_talking_loop");
                    break;
                default:  // 夜里：举灯
                    _anim.PlayFirstClip(1f, 0.3f, "idle_torch_loop", "idle_talking_loop");
                    break;
            }
        }

        /// <summary>0 = 在家 / 1 = 工作或办事 / 2 = 活动与出行。</summary>
        int PhaseNow()
        {
            float t = _clock != null ? _clock.time01 : (Time.time * 0.01f) % 1f;
            if (t > 0.62f || t < 0.02f) return 0;   // 夜里回家
            if (t < 0.35f) return 1;                // 白天上班/办事
            return 2;                               // 傍晚活动
        }

        /// <summary>
        /// 敌人识别必须清晰、公平：挑战/伪装型 NPC 在玩家靠近到一定距离时先亮标记，
        /// 而不是贴脸瞬间变敌。玩家永远有"看见→判断→选择回应"的空间。
        /// </summary>
        void MaybeFlagHostileIntent()
        {
            if (_flagged) return;
            if (kind != NpcKind.Challenge && kind != NpcKind.Disguised) return;
            var player = AdversityRoad.Core.ActorRegistry.Player;
            if (player == null) return;
            if (Vector3.Distance(transform.position, player.transform.position) > 14f) return;

            _flagged = true;
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "HostileIntentMarker";
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.transform.SetParent(transform, false);
            marker.transform.localPosition = new Vector3(0, 2.6f, 0);
            marker.transform.localScale = new Vector3(0.35f, 0.35f, 0.35f);
            marker.GetComponent<MeshRenderer>().sharedMaterial =
                Combat.CombatFeedback.EnergyMaterial(new Color(1f, 0.45f, 0.2f), 0.85f);

            GameEvents.RaiseSubtitle(kind == NpcKind.Disguised
                ? "前面那个人的语气不对——它在等你先动手。"
                : "有人朝你走过来，摆明了要试探你的底线。");
        }

        public static string KindLabel(NpcKind k)
        {
            switch (k)
            {
                case NpcKind.Quest: return "任务";
                case NpcKind.Ally: return "盟友";
                case NpcKind.Neutral: return "中立";
                case NpcKind.Challenge: return "挑战";
                case NpcKind.Disguised: return "伪装";
                case NpcKind.Projection: return "心理投影";
                default: return "普通市民";
            }
        }
    }
}
