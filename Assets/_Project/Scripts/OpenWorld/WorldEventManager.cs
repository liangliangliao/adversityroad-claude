using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Goals;
using AdversityRoad.Player;

namespace AdversityRoad.OpenWorld
{
    /// <summary>开放世界事件类型（方案 16.1）。</summary>
    public enum WorldEventKind
    {
        Social,        // 请求、误解、挑衅、围观、责任转嫁——不必战斗
        Environment,   // 暴雨、黑夜、交通、封路、噪声区——不必战斗
        GoalEvent,     // 机会、截止、关键联系人、资源短缺——通常不必战斗
        Hostile,       // 伏击、宿敌、目标守门人——可能战斗
        Inner,         // 拖延、旧我、自我怀疑、反刍裂隙——可能战斗
        Recovery       // 盟友、休息、训练、补给、复盘——不必战斗
    }

    public class WorldEventDef
    {
        public WorldEventKind kind;
        public string title;
        public string line;
        public bool mustFight;
        public string districtHint = "";
    }

    /// <summary>
    /// World Event Manager（方案 16 章）：开放世界的随机事件由它与 Adversity Director 共同调度。
    ///
    /// 三条硬规则：
    /// 1. 主线由 Goal Graph 决定，支线给资源/技能/盟友，逆境事件由导演按状态注入——
    ///    三者不能混成一锅：玩家必须**始终知道哪一个行为直接推进目标**（方案 16.2）。
    /// 2. 大多数事件不需要战斗（六类里只有两类"可能"战斗）。
    /// 3. 每个事件都带一次 Goal Priority 提示：低价值战斗也给经验，但会花掉时间与机会。
    /// </summary>
    public class WorldEventManager : MonoBehaviour
    {
        public static WorldEventManager Instance { get; private set; }

        public float firstDelay = 55f;
        public float minGap = 70f;
        public float maxGap = 130f;

        float _next;
        readonly List<string> _recent = new List<string>();

        public static WorldEventManager Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("WorldEventManager");
            Instance = go.AddComponent<WorldEventManager>();
            return Instance;
        }

        void Start() => _next = Time.time + firstDelay;

        void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + Random.Range(minGap, maxGap);
            if (!DistrictCatalog.Ready) return;

            var player = FindObjectOfType<PlayerController>();
            if (player == null || player.Stats.IsDead) return;
            var d = DistrictCatalog.NearestTo(player.transform.position, 80f);
            if (d == null) return;   // 不在开放城区（在 V1 经典关卡里）就不加戏

            var ev = Pick(d, player);
            if (ev == null) return;
            Raise(ev, d);
        }

        WorldEventDef Pick(DistrictRuntime d, PlayerController player)
        {
            var pool = new List<WorldEventDef>();
            float adversity = WorldState.AdversityOf(d.id);
            var stats = player.Stats;

            // —— 恢复事件优先于加压：连续失败或状态低迷时，世界先给出路 ——
            bool lowState = stats.hp < stats.maxHp * 0.5f || stats.will < stats.maxWill * 0.45f;
            if (lowState || WorldState.TotalDefeats >= 2)
            {
                pool.Add(new WorldEventDef
                {
                    kind = WorldEventKind.Recovery,
                    title = "恢复点",
                    line = "路边有一处可以坐下来的地方——补给、复盘、或者只是停一会儿。",
                });
                pool.Add(new WorldEventDef
                {
                    kind = WorldEventKind.Recovery,
                    title = "训练机会",
                    line = "有人在空地上练手——跟着练一会儿，能把上次输掉的那一手补上。",
                });
            }

            // —— 目标事件：只有存在进行中的目标时才出现，且一定与它有关 ——
            var goal = GoalOS.Active;
            if (goal != null && !goal.completed)
            {
                var node = GoalJourneyGraph.CurrentTarget(goal);
                if (node != null)
                    pool.Add(new WorldEventDef
                    {
                        kind = WorldEventKind.GoalEvent,
                        title = "目标机会",
                        line = "一个和「" + node.title + "」直接相关的机会出现了——现在处理，成本最低。"
                    });
                if (goal.deadlineDays > 0)
                    pool.Add(new WorldEventDef
                    {
                        kind = WorldEventKind.GoalEvent,
                        title = "截止提醒",
                        line = "时间在走：这条路上的机会窗口不会一直开着。"
                    });
            }

            // —— 社会事件：城市里最常见的一类，通常不用打 ——
            pool.Add(new WorldEventDef
            {
                kind = WorldEventKind.Social,
                title = "一个请求",
                line = "有人过来请你帮个忙——「就这一次」。帮不帮、帮多少，由你决定。"
            });
            pool.Add(new WorldEventDef
            {
                kind = WorldEventKind.Social,
                title = "围观与议论",
                line = "几个人在旁边说着什么，偶尔看你一眼。它们可以存在，但不必接管你。"
            });

            // —— 环境事件 ——
            pool.Add(new WorldEventDef
            {
                kind = WorldEventKind.Environment,
                title = "封路",
                line = "前面临时封了路——绕行会多花时间，但也可能碰到别的东西。"
            });
            pool.Add(new WorldEventDef
            {
                kind = WorldEventKind.Environment,
                title = "噪声区",
                line = "这一段特别吵，专注会被持续拉扯——切「定心姿态」能减免。"
            });

            // —— 内在事件：反刍/拖延到阈值才出现 ——
            if (stats.rumination > 55f || stats.actionPower < stats.maxActionPower * 0.45f)
                pool.Add(new WorldEventDef
                {
                    kind = WorldEventKind.Inner,
                    title = "反刍裂隙",
                    line = "脑子里那段话又开始循环播放了——它正在这条街上找一个入口。"
                });

            // —— 敌对遭遇：只在逆境层较深时出现，且必须可撤退 ——
            if (adversity >= 0.35f)
                pool.Add(new WorldEventDef
                {
                    kind = WorldEventKind.Hostile,
                    title = "伏击",
                    line = "有人从侧面切了进来——解除锁定离开也是一种胜利。",
                    mustFight = false
                });

            // 去重：不要连续给同一个事件
            for (int i = pool.Count - 1; i >= 0; i--)
                if (_recent.Contains(pool[i].title) && pool.Count > 1) pool.RemoveAt(i);
            if (pool.Count == 0) return null;
            return pool[Random.Range(0, pool.Count)];
        }

        void Raise(WorldEventDef ev, DistrictRuntime d)
        {
            _recent.Add(ev.title);
            if (_recent.Count > 4) _recent.RemoveAt(0);

            GameEvents.RaiseSubtitle("〔" + KindLabel(ev.kind) + " · " + ev.title + "〕" + ev.line);

            // 机会成本提示：不是每件事都值得现在做（方案 16.3）
            var goal = GoalOS.Active;
            var pr = GoalPriorityEngine.Evaluate(new PriorityInput
            {
                goalRelevance = ev.kind == WorldEventKind.GoalEvent ? 0.95f
                    : ev.kind == WorldEventKind.Inner ? 0.6f
                    : ev.kind == WorldEventKind.Recovery ? 0.5f : 0.2f,
                urgency = ev.kind == WorldEventKind.GoalEvent ? 0.7f : 0.35f,
                opportunityValue = ev.kind == WorldEventKind.Recovery ? 0.8f : 0.35f,
                riskOfDelay = ev.kind == WorldEventKind.GoalEvent ? 0.65f : 0.2f,
                playerCommitment = goal != null ? 0.5f : 0.2f,
                cost = ev.kind == WorldEventKind.Hostile ? 0.7f : 0.3f
            });
            CloudDialogueService.AddLog("世界事件 " + ev.title + " @" + d.name +
                " priority=" + pr.score.ToString("F2"));

            if (ev.kind == WorldEventKind.Hostile || ev.kind == WorldEventKind.Inner)
                GameEvents.RaiseSubtitle("目标相关性提示：" + pr.verdict);

            switch (ev.kind)
            {
                case WorldEventKind.Environment:
                    WorldState.AddAdversity(d.id, 0.05f);
                    break;
                case WorldEventKind.Inner:
                    WorldState.AddAdversity(d.id, 0.12f);
                    break;
                case WorldEventKind.Recovery:
                    WorldState.AddAdversity(d.id, -0.08f);
                    break;
            }
        }

        public static string KindLabel(WorldEventKind k)
        {
            switch (k)
            {
                case WorldEventKind.Social: return "社会事件";
                case WorldEventKind.Environment: return "环境事件";
                case WorldEventKind.GoalEvent: return "目标事件";
                case WorldEventKind.Hostile: return "敌对遭遇";
                case WorldEventKind.Inner: return "内在事件";
                default: return "恢复事件";
            }
        }
    }
}
