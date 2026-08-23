using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.Combat;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.Shame
{
    /// <summary>
    /// 第八章新增连招（方案 8.8.4，并入 §10.4 核心连招）。
    ///
    /// 【为什么不做进 FusionRecipe】
    /// 既有的融合连招表用的是攻击元素的字母串（拳 P / 剑 K / 重 H / 跳 J / 闪 D / 术 S / 架 G），
    /// 它匹配的是"几秒内按了哪几个键"。而本章这四组连招里的动作根本不在那套字母表里：
    /// 认领不终审是指认判定窗内的一次输入、自行陈述是走进广播室之后的一段流程、
    /// 稳定站位是"在锥内连续行动 8 秒未回避"这个状态。
    /// 硬塞进字母串只会得到一串对不上号的代号。
    ///
    /// 所以这里另起一张表：**按语义步骤匹配**，每步之间给一个宽松的 14 秒窗口——
    /// 这四组本来就不是手速连段，是"先拔钉、再陈述事实、最后主动公开"这种打法顺序。
    /// </summary>
    public class ShameComboTracker : MonoBehaviour
    {
        public static ShameComboTracker Instance { get; private set; }

        /// <summary>两步之间的最长间隔：这几组是打法顺序，不是手速连段。</summary>
        public const float StepWindow = 14f;

        // ---- 步骤标签（各系统在做成一件事时推一个进来）----
        public const string TagOwn = "认领不终审";
        public const string TagFactBlade = "事实之刃";
        public const string TagStatement = "自行陈述";
        public const string TagParry = "精准格挡";
        public const string TagTrueStrike = "真实一击";
        public const string TagSpotlight = "聚光灯校准";
        public const string TagSteady = "稳定站位";
        public const string TagObjective = "目标动作";
        public const string TagRefuse = "不上庭";
        public const string TagBoundaryGuard = "边界盾";
        public const string TagUnlock = "解除锁定";

        class Recipe
        {
            public string name;
            public string role;
            public string[] steps;
            public int progress;
            public float lastAt;
        }

        static readonly Recipe[] Recipes =
        {
            new Recipe { name = "自述三段", role = "Boss 终局：先拔钉、再陈述事实、最后主动公开",
                steps = new[] { TagOwn, TagFactBlade, TagStatement } },
            new Recipe { name = "破钉式", role = "身份钉兵的连续指认链",
                steps = new[] { TagParry, TagOwn, TagTrueStrike } },
            new Recipe { name = "聚光穿越", role = "在视线锥内完成长按交互",
                steps = new[] { TagSpotlight, TagSteady, TagObjective } },
            new Recipe { name = "不上庭反制", role = "拒绝被拖入低价值的「判词」交互",
                steps = new[] { TagRefuse, TagBoundaryGuard, TagUnlock } },
        };

        PlayerController _player;
        LockOnSystem _lockOn;
        bool _hadTarget;
        float _nextPoll;

        public static ShameComboTracker Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("ShameComboTracker");
            Instance = go.AddComponent<ShameComboTracker>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>
        /// 推入一个步骤。章外调用直接忽略——本章的连招不该在别的章节里冒出来。
        /// 静态入口：调用方（战斗控制器等）不必关心这个组件在不在场上。
        /// </summary>
        public static void Push(string tag)
        {
            if (Instance == null || string.IsNullOrEmpty(tag)) return;
            if (!ShameLine.InChapter) return;
            Instance.Record(tag);
        }

        void Record(string tag)
        {
            foreach (var r in Recipes)
            {
                // 超窗即从头来过：连招要连贯，不能隔着半分钟慢慢凑
                if (r.progress > 0 && Time.time - r.lastAt > StepWindow) r.progress = 0;

                if (r.steps[r.progress] != tag)
                {
                    // 走错一步不清零整条：第一步又被打出来时，就从第一步重新起算
                    if (r.progress > 0 && r.steps[0] == tag) { r.progress = 1; r.lastAt = Time.time; }
                    continue;
                }
                r.progress++;
                r.lastAt = Time.time;
                if (r.progress < r.steps.Length)
                {
                    GameEvents.RaiseSubtitle("【" + r.name + " " + r.progress + "/" +
                        r.steps.Length + "】下一步：" + r.steps[r.progress]);
                    continue;
                }
                r.progress = 0;
                Complete(r);
            }
        }

        void Complete(Recipe r)
        {
            GameEvents.RaiseSkillBanner(r.name);
            GameEvents.RaiseSubtitle("【" + r.name + "】" + r.role);
            GameAudio.Play(GameAudio.Sfx.Parry, 0.95f);

            var p = Player();
            var exposure = ExposureSystem.Instance;
            switch (r.name)
            {
                case "自述三段":
                    if (p != null) p.Stats.RestoreAxis(Personalization.WeaknessAxis.Shame, 18f);
                    break;
                case "破钉式":
                    if (exposure != null) exposure.Add(-10f, null);
                    if (p != null)
                    {
                        var combat = p.GetComponent<PlayerCombatController>();
                        if (combat != null) combat.AddMomentum(1);
                    }
                    break;
                case "聚光穿越":
                    if (exposure != null) exposure.Add(-12f, null);
                    break;
                default:   // 不上庭反制
                    if (p != null) p.Stats.ReduceRumination(12f);
                    break;
            }
            var resolve = Adversity.ResolveSystem.Instance;
            if (resolve != null) resolve.NoteQualityAction("打出「" + r.name + "」");
        }

        PlayerController Player()
        {
            if (_player == null) _player = AdversityRoad.Core.ActorRegistry.Player;
            return _player;
        }

        void Update()
        {
            if (!ShameLine.InChapter) return;
            if (Time.time < _nextPoll) return;
            _nextPoll = Time.time + 0.1f;

            var p = Player();
            if (p == null) return;

            // 「真实一击」与「解除锁定」没有各自的事件，只能观察状态——
            // 而且**只在某条连招正好等着它们时**才观察，否则平时打个重击、
            // 松一次锁定都会被记成连招的一步。
            if (Waiting(TagTrueStrike))
            {
                var combat = p.GetComponent<PlayerCombatController>();
                if (combat != null && combat.Fusion.TailIs("H")) Record(TagTrueStrike);
            }

            if (_lockOn == null) _lockOn = p.GetComponent<LockOnSystem>();
            bool hasTarget = _lockOn != null && _lockOn.CurrentTarget != null;
            if (_hadTarget && !hasTarget && Waiting(TagUnlock)) Record(TagUnlock);
            _hadTarget = hasTarget;
        }

        static bool Waiting(string tag)
        {
            foreach (var r in Recipes)
            {
                if (r.progress <= 0 || r.progress >= r.steps.Length) continue;
                if (Time.time - r.lastAt > StepWindow) continue;
                if (r.steps[r.progress] == tag) return true;
            }
            return false;
        }

        /// <summary>招式面板用：本章四组连招的名称、步骤与用途。</summary>
        public static List<string> Describe()
        {
            var list = new List<string>();
            foreach (var r in Recipes)
            {
                var sb = new System.Text.StringBuilder(r.name).Append("：");
                for (int i = 0; i < r.steps.Length; i++)
                {
                    if (i > 0) sb.Append(" → ");
                    sb.Append(r.steps[i]);
                }
                sb.Append("　（").Append(r.role).Append('）');
                list.Add(sb.ToString());
            }
            return list;
        }
    }
}
