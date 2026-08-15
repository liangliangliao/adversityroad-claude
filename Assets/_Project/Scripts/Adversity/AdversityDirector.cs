using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.AI;
using AdversityRoad.Core;
using AdversityRoad.Goals;
using AdversityRoad.OpenWorld;
using AdversityRoad.Player;
using AdversityRoad.World;

namespace AdversityRoad.Adversity
{
    /// <summary>
    /// Adversity Director · 逆境导演（方案第 7 章）。
    ///
    /// 它**不是作弊 AI**：它的职责是寻找"足够困难但可学习"的挑战区间。
    /// 依据目标阶段、玩家战斗能力、最近表现、连续失败、弱点/优势图谱、
    /// 强度设置、已有宿敌与世界状态，调整遭遇组合与压力维度。
    ///
    /// 公平边界（方案 7.4，硬性）：
    ///   允许调整——下一波敌人组合、AI 战术概率、远程敌人数量、资源点、
    ///             心理攻击冷却、道路分支、训练机会；
    ///   禁止调整——已经做出的成功闪避突然被判命中、敌人无预兆获得超范围攻击、
    ///             在玩家视野外近距离强行刷怪、因为知道玩家低血就必杀式作弊。
    /// 所有关键攻击必须有可学习的 Telegraph。
    ///
    /// 而且逆境不能只攻击弱点（方案 7.5）：导演必须同时安排**优势发挥窗口**，
    /// 否则玩家感觉到的是被针对，而不是成长。
    /// </summary>
    public class AdversityDirector : MonoBehaviour
    {
        public static AdversityDirector Instance { get; private set; }

        /// <summary>敌人生成器（由 GameBootstrap 注入，与 Assembler 同一个）。</summary>
        public System.Func<EnemyType, EnemyTier, Vector3, bool, GameObject> spawner;

        public float firstDelay = 90f;

        int _consecutiveDefeats;
        int _consecutiveEasyWins;
        float _encounterStartedAt;
        float _next;
        AdversityBudget _budget;
        bool _strengthWindowPending;

        public AdversityBudget CurrentBudget => _budget;
        public int ConsecutiveDefeats => _consecutiveDefeats;

        public static AdversityDirector Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("AdversityDirector");
            Instance = go.AddComponent<AdversityDirector>();
            return Instance;
        }

        void Start() => _next = Time.time + firstDelay;

        void OnEnable()
        {
            GameEvents.OnPlayerDied += OnPlayerDied;
            GameEvents.OnEnemyKilled += OnEnemyKilled;
        }

        void OnDisable()
        {
            GameEvents.OnPlayerDied -= OnPlayerDied;
            GameEvents.OnEnemyKilled -= OnEnemyKilled;
        }

        void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + Random.Range(85f, 150f);

            var player = FindObjectOfType<PlayerController>();
            if (player == null || player.Stats == null || player.Stats.IsDead) return;

            _budget = AdversityBudgetCalculator.Compute(GoalOS.Active, player.Stats,
                _consecutiveDefeats, _consecutiveEasyWins);

            // 连续失败保护：先给恢复与训练机会，不是继续加压（方案 7.3 / C.2）
            if (_consecutiveDefeats >= 2)
            {
                OfferRecovery(player);
                return;
            }

            // 优势发挥窗口优先于弱点针对：先让玩家赢一次自己擅长的
            if (_strengthWindowPending || ShouldShowcaseStrength())
            {
                _strengthWindowPending = false;
                SpawnStrengthWindow(player);
                return;
            }

            SpawnBudgetedEncounter(player);
        }

        // ================= 遭遇编排 =================

        void SpawnBudgetedEncounter(PlayerController player)
        {
            if (spawner == null) return;

            int alive = 0;
            foreach (var e in FindObjectsOfType<EnemyController>())
                if (e.State != EnemyState.Dead) alive++;
            int allowed = AdversityBudgetCalculator.EnemyCountFor(_budget);
            if (alive >= allowed) return;

            // 只调整"下一波的组合"，不改已经在场敌人的判定
            var edges = AdversityProfile.TargetableEdges();
            EnemyType type = PickTypeFor(edges);
            EnemyTier tier = TierFor(_budget);

            Vector3 pos = SpawnPointInView(player);
            if (pos == Vector3.zero) return;   // 找不到"玩家能看见的合法位置"就不刷（禁止视野外贴脸刷怪）

            var go = spawner(type, tier, pos, true);
            if (go == null) return;
            AdaptiveEnemyController.Attach(go, edges);
            _encounterStartedAt = Time.time;

            CloudDialogueService.AddLog("逆境导演：" + _budget.Describe() +
                " → " + EnemyCatalog.TypeLabel(type) + "(" + EnemyCatalog.TierLabel(tier) + ")");
            GameEvents.RaiseSubtitle("有什么东西循着你的路线找上来了——它比上一个更清楚你会怎么打。");
        }

        /// <summary>
        /// 优势发挥窗口（方案 7.5）：玩家善于格挡，就给一个格挡真正能产生收益的高价值机会。
        /// 随后再用新型敌人逼迫玩家扩展能力——先奖励，再扩展，而不是一路针对。
        /// </summary>
        void SpawnStrengthWindow(PlayerController player)
        {
            if (spawner == null) return;
            var strengths = AdversityProfile.ShowcaseStrengths();
            string tag = strengths.Count > 0 ? strengths[0].strengthTag : PlayerBehaviorAnalyzer.StrengthParry;

            EnemyType type;
            string line;
            switch (tag)
            {
                case PlayerBehaviorAnalyzer.StrengthParry:
                    type = EnemyType.OverreactGhost;      // 大开大合、招式可读——格挡收益极高
                    line = "这一个的起手很大——你的格挡正好吃它。";
                    break;
                case PlayerBehaviorAnalyzer.StrengthFactCall:
                    type = EnemyType.RuleTwister;         // 模糊叙事型：说清事实即破防
                    line = "它靠含糊其辞撑着——把事实说清楚就能撬开它。";
                    break;
                case PlayerBehaviorAnalyzer.StrengthResource:
                    type = EnemyType.HungerHound;         // 资源战：会用补给的人吃得下
                    line = "这一带资源不少——会规划的人打这种消耗战最省力。";
                    break;
                default:
                    type = EnemyType.ProvokerPasserby;
                    line = "又是这种一上来就挑衅的——你已经知道怎么不接招了。";
                    break;
            }

            Vector3 pos = SpawnPointInView(player);
            if (pos == Vector3.zero) return;
            var go = spawner(type, EnemyTier.Standard, pos, true);
            if (go != null) GameEvents.RaiseSubtitle(line);
            CloudDialogueService.AddLog("逆境导演：优势发挥窗口 → " + tag);
        }

        /// <summary>连续失败时：给恢复、补给与训练机会，并降低下一次的预算。</summary>
        void OfferRecovery(PlayerController player)
        {
            var d = DistrictCatalog.NearestTo(player.transform.position, 90f);
            string where = d != null ? d.name : "附近";
            GameEvents.RaiseSubtitle("〔恢复机会〕连续受挫之后，" + where +
                "出现了补给与训练点——先把状态和策略补回来，再回去。");
            if (d != null)
            {
                WorldState.AddAdversity(d.id, -0.15f);
                WorldState.ReopenRoute(d.id);
            }
            _strengthWindowPending = true;   // 恢复之后下一次先给一个能赢的
            CloudDialogueService.AddLog("逆境导演：连续失败 " + _consecutiveDefeats +
                " 次 → 降低预算并给出恢复/训练机会");
        }

        // ================= 公平边界 =================

        /// <summary>
        /// 合法出生点：必须在玩家**视野方向的前方半球**且有一定距离——
        /// 禁止在玩家视野外近距离强行刷怪（方案 7.4 禁止项）。
        /// </summary>
        Vector3 SpawnPointInView(PlayerController player)
        {
            Transform cam = player.cameraTransform != null ? player.cameraTransform : player.transform;
            for (int i = 0; i < 8; i++)
            {
                float ang = Random.Range(-55f, 55f);
                Vector3 dir = Quaternion.Euler(0, ang, 0) * new Vector3(cam.forward.x, 0, cam.forward.z).normalized;
                Vector3 p = player.transform.position + dir * Random.Range(12f, 20f);
                if (UnityEngine.AI.NavMesh.SamplePosition(p, out var hit, 6f, UnityEngine.AI.NavMesh.AllAreas))
                    return hit.position + Vector3.up * 1.1f;
            }
            return Vector3.zero;
        }

        static EnemyType PickTypeFor(List<VulnerabilityEdge> edges)
        {
            // 只有"稳定模式"才允许被针对；否则退回与目标障碍相关的类型
            foreach (var e in edges)
            {
                if (e.triggerTag == PlayerBehaviorAnalyzer.TriggerTaunt) return EnemyType.ProvokerPasserby;
                if (e.triggerTag == PlayerBehaviorAnalyzer.TriggerSurrounded) return EnemyType.RuminationSwarm;
                if (e.triggerTag == PlayerBehaviorAnalyzer.TriggerLowHp) return EnemyType.HungerHound;
                if (e.triggerTag == PlayerBehaviorAnalyzer.TriggerVerbalDenial) return EnemyType.GuiltThrower;
            }

            var goal = GoalOS.Active;
            if (goal != null)
                foreach (var o in goal.obstacles)
                    if (!o.removed)
                    {
                        ChapterModuleLibrary.SuggestEnemies(o.axis, out var ext, out _, out _);
                        return ext;
                    }
            return EnemyType.TomorrowPhantom;
        }

        static EnemyTier TierFor(AdversityBudget b)
        {
            if (b.physical >= 0.45f) return EnemyTier.Elite;
            if (b.physical >= 0.30f) return EnemyTier.Standard;
            return EnemyTier.Novice;
        }

        static bool ShouldShowcaseStrength() => AdversityProfile.ShowcaseStrengths().Count > 0
            && Random.value < 0.35f;

        // ================= 表现追踪 =================

        void OnPlayerDied(string reason)
        {
            _consecutiveDefeats++;
            _consecutiveEasyWins = 0;

            var player = FindObjectOfType<PlayerController>();
            var d = player != null ? DistrictCatalog.NearestTo(player.transform.position, 90f) : null;
            if (d != null) WorldState.NoteDefeat(d.id);

            CloudDialogueService.AddLog("逆境导演：记录失败 ×" + _consecutiveDefeats +
                "（下一次预算下调，并优先给恢复/训练）");
        }

        void OnEnemyKilled(string enemyId)
        {
            _consecutiveDefeats = 0;
            // "轻松取胜" = 遭遇开始后很快解决且几乎没掉状态
            float dur = Time.time - _encounterStartedAt;
            var player = FindObjectOfType<PlayerController>();
            bool easy = dur > 0f && dur < 18f && player != null && player.Stats != null &&
                player.Stats.hp > player.Stats.maxHp * 0.8f;
            if (easy) _consecutiveEasyWins++;
            else _consecutiveEasyWins = 0;
        }
    }
}
