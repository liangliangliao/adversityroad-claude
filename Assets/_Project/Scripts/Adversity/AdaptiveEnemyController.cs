using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.AI;
using AdversityRoad.Core;
using AdversityRoad.Player;
using AdversityRoad.World;

namespace AdversityRoad.Adversity
{
    /// <summary>敌人等级体系（方案 9.1）。</summary>
    public enum AdaptiveTier
    {
        T1_Stimulator,   // 单一物理或心理招式
        T2_Predator,     // 针对一个已知行为弱点
        T3_Manipulator,  // 使用连续心理战术链
        T4_Elite,        // 物理+心理+环境组合
        T5_Nemesis,      // 记忆过去战斗并调整策略
        T6_Boss,         // 多阶段、目标节点守门
        T7_LifeNemesis   // 跨章节长期演化
    }

    /// <summary>
    /// Adaptive Enemy AI（方案 9.2 / 9.3）。
    ///
    /// 敌人学习的是**游戏行为**，不是读取玩家隐私：系统发现"挑衅后追击率 82%"，
    /// 某类敌人就可以使用"挑衅-后撤-伏击"；当玩家连续识破后，该策略权重下降，
    /// 敌人转而尝试其他公平策略。
    ///
    /// 公平性由三条保证：
    /// 1. 只有"稳定模式"（≥10 次样本、跨场景、置信度达标）才允许被针对；
    /// 2. 每个战术在生效前都有可识别的信号（一句预告 + 头顶标记），可学、可反制；
    /// 3. 调整只发生在遭遇开始时，绝不在攻击动画中途偷偷改判定。
    /// </summary>
    public class AdaptiveEnemyController : MonoBehaviour
    {
        public const string TacticTauntAmbush = "挑衅-后撤-伏击";
        public const string TacticSwarmPress = "围堵-压迫翻滚";
        public const string TacticLowHpExecute = "低血追猎";
        public const string TacticVerbalChain = "心理战术链";
        public const string TacticPatientCounter = "诱导先手-防反";

        public AdaptiveTier tier = AdaptiveTier.T2_Predator;
        public string tactic = "";
        public bool isNemesis;
        public string nemesisArchetype = "";

        EnemyController _enemy;
        PlayerController _player;
        float _spawnAt;
        bool _announced;
        bool _counteredThisFight;
        float _startDistance = -1f;

        // 心理攻击链（方案 9.3）：每一步都有信号，也都有反制
        static readonly string[] ChainSteps =
        {
            "轻度否定", "社会比较", "诱导证明", "伏击", "责任倒置"
        };
        static readonly string[] ChainLines =
        {
            "「这点事你也做不完？」",
            "「别人早就做到了，你还在这儿。」",
            "「有本事你现在就证明给我看。」",
            "「——就等你冲过来。」",
            "「结果成这样，不还是你自己的问题？」"
        };
        static readonly string[] ChainCounters =
        {
            "不读心盾：无法确认的事不当成事实",
            "事实姿态：先说清楚发生了什么",
            "拒绝证明：不必用一次冲动回应质疑",
            "别追——挑衅落空，它自己会露破绽",
            "责任归还：把不属于我的推回去"
        };
        int _chainStep = -1;
        float _nextChainAt;

        /// <summary>把自适应能力挂到一个刚生成的敌人上。</summary>
        public static AdaptiveEnemyController Attach(GameObject go, List<VulnerabilityEdge> edges)
        {
            if (go == null) return null;
            var a = go.GetComponent<AdaptiveEnemyController>();
            if (a == null) a = go.AddComponent<AdaptiveEnemyController>();
            a.Configure(edges);
            return a;
        }

        void Awake()
        {
            _enemy = GetComponent<EnemyController>();
            _spawnAt = Time.time;
        }

        void Configure(List<VulnerabilityEdge> edges)
        {
            if (_enemy == null) _enemy = GetComponent<EnemyController>();
            tactic = PickTactic(edges);
            tier = TierFor(tactic, edges);

            var n = _enemy != null && _enemy.profile != null
                ? NemesisSystem.Find(_enemy.profile.displayName) : null;
            if (n != null)
            {
                isNemesis = true;
                nemesisArchetype = n.archetypeId;
                tier = n.currentRank >= 3 ? AdaptiveTier.T7_LifeNemesis
                    : n.currentRank == 2 ? AdaptiveTier.T6_Boss : AdaptiveTier.T5_Nemesis;
                if (!n.enemyAdaptations.Contains(tactic)) n.enemyAdaptations.Add(tactic);
            }

            ApplyTactic();
        }

        /// <summary>按战术权重与"可被针对的稳定模式"挑一个战术；没有稳定模式就用最基础的。</summary>
        static string PickTactic(List<VulnerabilityEdge> edges)
        {
            var pool = new List<string>();
            if (edges != null)
                foreach (var e in edges)
                {
                    if (e.triggerTag == PlayerBehaviorAnalyzer.TriggerTaunt) pool.Add(TacticTauntAmbush);
                    else if (e.triggerTag == PlayerBehaviorAnalyzer.TriggerSurrounded) pool.Add(TacticSwarmPress);
                    else if (e.triggerTag == PlayerBehaviorAnalyzer.TriggerLowHp) pool.Add(TacticLowHpExecute);
                    else if (e.triggerTag == PlayerBehaviorAnalyzer.TriggerVerbalDenial) pool.Add(TacticVerbalChain);
                }
            if (pool.Count == 0) return TacticPatientCounter;

            // 权重轮盘：被识破的战术权重低，自然被换掉
            float sum = 0f;
            foreach (var t in pool) sum += NemesisSystem.TacticWeight(t);
            float pick = Random.value * sum;
            foreach (var t in pool)
            {
                pick -= NemesisSystem.TacticWeight(t);
                if (pick <= 0f) return t;
            }
            return pool[0];
        }

        static AdaptiveTier TierFor(string tactic, List<VulnerabilityEdge> edges)
        {
            if (tactic == TacticVerbalChain) return AdaptiveTier.T3_Manipulator;
            if (edges != null && edges.Count >= 2) return AdaptiveTier.T4_Elite;
            if (edges != null && edges.Count == 1) return AdaptiveTier.T2_Predator;
            return AdaptiveTier.T1_Stimulator;
        }

        /// <summary>
        /// 战术只改变**出手频率、警戒距离、交战距离、移动速度**这类公开可读的参数，
        /// 而且只在生成时改一次。绝不改已经发生的判定，也不给它无预兆的超范围攻击。
        /// </summary>
        void ApplyTactic()
        {
            if (_enemy == null || _enemy.profile == null) return;
            var p = _enemy.profile;
            switch (tactic)
            {
                case TacticTauntAmbush:
                    p.aggression = Mathf.Clamp01(p.aggression - 0.15f);   // 更沉得住气：等你先冲
                    p.detectRange += 4f;
                    p.attackRange = Mathf.Max(p.attackRange, 2.1f);
                    break;
                case TacticSwarmPress:
                    p.aggression = Mathf.Clamp01(p.aggression + 0.12f);
                    p.moveSpeed += 0.4f;
                    break;
                case TacticLowHpExecute:
                    p.detectRange += 6f;                                  // 咬得更紧，但伤害不变
                    break;
                case TacticVerbalChain:
                    p.mentalDamage *= 1.15f;
                    p.aggression = Mathf.Clamp01(p.aggression - 0.08f);
                    break;
                default:
                    p.aggression = Mathf.Clamp01(p.aggression - 0.2f);    // 防反型：诱导先手
                    p.defense += 4f;
                    break;
            }
        }

        void Update()
        {
            if (_enemy == null || _enemy.State == EnemyState.Dead) return;
            if (_player == null)
            {
                _player = AdversityRoad.Core.ActorRegistry.Player;
                if (_player == null) return;
            }

            float dist = Vector3.Distance(transform.position, _player.transform.position);
            if (_startDistance < 0f && dist < 20f) _startDistance = dist;

            if (!_announced && dist < 16f) Announce();
            if (tactic == TacticVerbalChain) TickChain(dist);
            TrackCounterPlay(dist);
        }

        /// <summary>
        /// 战术必须可读：进入交战距离时给一次明确预告 + 头顶标记。
        /// 玩家第一次可能吃亏，第二次就该认得出来——这就是"可学"。
        /// </summary>
        void Announce()
        {
            _announced = true;
            // 住所里不出现任何敌人的挑衅台词——哪怕真有敌人溜进了院子
            if (OpenWorld.Sanctuary.AtHome) return;
            string name = _enemy.profile != null ? _enemy.profile.displayName : "它";
            string line;
            switch (tactic)
            {
                case TacticTauntAmbush:
                    line = "它站在原地不动，只是挑衅——它在等你先冲过去。";
                    break;
                case TacticSwarmPress:
                    line = "它在绕你的侧面走位，想把你逼到不停翻滚。";
                    break;
                case TacticLowHpExecute:
                    line = "它盯着你的血量——你越虚它咬得越紧。";
                    break;
                case TacticVerbalChain:
                    line = "它开口的方式有章法：一句接一句，每一句都在往同一个方向推。";
                    break;
                default:
                    line = "它摆好了架势不出手——它想让你先落招。";
                    break;
            }
            // 武术类型一并报出来：玩家要学的是"怎么应对这一类打法"，不是背单个敌人
            string martial = MartialArchetypeCatalog.Describe(_enemy.archetype);
            if (isNemesis)
            {
                var n = NemesisSystem.Find(nemesisArchetype);
                string rank = n != null ? n.RankLabel() : "宿敌";
                GameEvents.RaiseSubtitle("【" + rank + "·" + name + "】" + martial + " " + line +
                    "（它记得你们上次的每一场）");
            }
            else
            {
                GameEvents.RaiseSubtitle("【" + TierLabel(tier) + "·" + name + "】" +
                    martial + " " + line);
            }
            MarkTelegraph();
        }

        void MarkTelegraph()
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "TacticTelegraph";
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.transform.SetParent(transform, false);
            marker.transform.localPosition = new Vector3(0, 2.9f, 0);
            marker.transform.localScale = new Vector3(0.4f, 0.4f, 0.4f);
            marker.GetComponent<MeshRenderer>().sharedMaterial =
                Combat.CombatFeedback.EnergyMaterial(
                    isNemesis ? new Color(0.95f, 0.25f, 0.35f) : new Color(0.95f, 0.7f, 0.25f), 0.85f);
        }

        // ================= 心理攻击链 =================

        /// <summary>
        /// 心理攻击不再只是一句台词，而是有状态、有前置条件、有反制的攻击链：
        /// 轻度否定 → 社会比较 → 诱导证明 →（玩家冲动追击）→ 伏击 → 责任倒置。
        /// 每一步都必须有可识别的信号和可用反制，绝不把现实操纵技巧包装成教程。
        /// </summary>
        void TickChain(float dist)
        {
            if (dist > 18f || Time.time < _nextChainAt) return;
            if (_enemy.State == EnemyState.Dead) return;

            // 第 4 步（伏击）只有在玩家真的被"诱导证明"钓过来时才触发——不是无条件推进
            if (_chainStep == 2 && _startDistance > 0f && dist > _startDistance - 3f)
            {
                // 玩家没上钩：链条断在这里，它得换别的路子
                NemesisSystem.AdjustTactic(TacticVerbalChain, true);
                _counteredThisFight = true;
                GameEvents.RaiseSubtitle("链条断了——它没能把你钓过来。");
                _chainStep = ChainSteps.Length;
                return;
            }

            _chainStep++;
            if (_chainStep >= ChainSteps.Length) return;
            _nextChainAt = Time.time + 5.5f;

            string step = ChainSteps[_chainStep];
            GameEvents.RaiseSubtitle("〔心理链 " + (_chainStep + 1) + "/" + ChainSteps.Length +
                " · " + step + "〕" + ChainLines[_chainStep] +
                "  ——反制：" + ChainCounters[_chainStep]);
            if (_enemy.dialogue != null) _enemy.dialogue.Show(ChainLines[_chainStep], 3f);
        }

        // ================= 识破判定 =================

        /// <summary>
        /// 玩家是否识破了这个战术。识破了 → 全局权重下降，这类敌人以后少用它；
        /// 没识破 → 权重缓慢回升。无论哪种，敌人得到的都只是"换个公平打法"的机会。
        /// </summary>
        void TrackCounterPlay(float dist)
        {
            if (_counteredThisFight || _startDistance < 0f) return;
            float fightTime = Time.time - _spawnAt;
            if (fightTime < 6f) return;

            switch (tactic)
            {
                case TacticTauntAmbush:
                    // 6 秒过去玩家没有明显缩短距离 = 没接招
                    if (dist > _startDistance - 3f) Countered();
                    break;
                case TacticLowHpExecute:
                    if (_player.Stats != null && _player.Stats.hp > _player.Stats.maxHp * 0.55f &&
                        fightTime > 14f) Countered();
                    break;
                case TacticPatientCounter:
                    if (fightTime > 16f && _enemy.HpRatio < 0.6f) Countered();
                    break;
            }
        }

        void Countered()
        {
            _counteredThisFight = true;
            NemesisSystem.AdjustTactic(tactic, true);
            AdversityProfile.ObserveStrength(PlayerBehaviorAnalyzer.StrengthRetreat,
                ZoneBuilder.CurrentZoneId);
            GameEvents.RaiseSubtitle("它这一手没用上——下次它会换别的。");
        }

        void OnDestroy()
        {
            // 敌人被清掉而战术从未被识破：说明这条路还有效
            if (!_counteredThisFight && _announced) NemesisSystem.AdjustTactic(tactic, false);
        }

        public static string TierLabel(AdaptiveTier t)
        {
            switch (t)
            {
                case AdaptiveTier.T1_Stimulator: return "T1 刺激者";
                case AdaptiveTier.T2_Predator: return "T2 捕食者";
                case AdaptiveTier.T3_Manipulator: return "T3 操纵者";
                case AdaptiveTier.T4_Elite: return "T4 精英";
                case AdaptiveTier.T5_Nemesis: return "T5 宿敌";
                case AdaptiveTier.T6_Boss: return "T6 Boss";
                default: return "T7 人生宿敌";
            }
        }

        /// <summary>战斗时长（宿敌生存时间统计用）。</summary>
        public float FightDuration => Time.time - _spawnAt;
    }
}
