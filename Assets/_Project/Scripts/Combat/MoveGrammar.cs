using System.Collections.Generic;
using UnityEngine;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 动作语法（方案 10.6）：每个招式必须定义
    /// Startup / Active / Recovery / Tracking / Poise / GuardDamage /
    /// CancelWindow / ParryWindow / DodgeWindow / Telegraph。
    ///
    /// 这不是为了好看的数据表——它是"可读、可学、可反制"的**技术定义**：
    /// 玩家能不能读出这一招、能不能弹反、能不能翻滚穿过去，
    /// 全由这十个字段决定。Boss 动作设计的底线也在这里：
    /// 靠高速特效遮蔽判定的招，在这张表上会立刻暴露成"前摇过短 + 无 Telegraph"。
    /// </summary>
    public struct MoveGrammar
    {
        public float startup;        // 前摇（秒）：可读窗口的长度
        public float active;         // 判定持续（秒）
        public float recovery;       // 后摇（秒）：被惩罚的窗口
        public float tracking;       // 追踪度 0-1：出招后还能修正多少朝向
        public float poise;          // 霸体值：受击是否被打断
        public float guardDamage;    // 对格挡的削减（破防进度）
        public float cancelWindow;   // 可取消窗口（秒，自连段起算）
        public float parryWindow;    // 可弹反窗口（秒，判定生效前）
        public float dodgeWindow;    // 可闪避窗口（秒，判定生效前）
        public bool telegraph;       // 是否带可学习的预警（危险攻击必须为 true）

        /// <summary>总帧长（秒）：读招 → 判定 → 收招。</summary>
        public float Total => startup + active + recovery;

        /// <summary>
        /// 可学习性：前摇越长、预警越明确、可反制窗口越宽，越"可学"。
        /// 高伤害招式必须靠这个换取公平——不能既快又无预警还打得重。
        /// </summary>
        public float Readability =>
            Mathf.Clamp01((telegraph ? 0.4f : 0f) +
                          Mathf.Clamp01(startup / 0.9f) * 0.35f +
                          Mathf.Clamp01(parryWindow / 0.35f) * 0.15f +
                          Mathf.Clamp01(dodgeWindow / 0.45f) * 0.10f);

        public string Summary() =>
            "起手 " + startup.ToString("F2") + "s · 判定 " + active.ToString("F2") +
            "s · 收招 " + recovery.ToString("F2") + "s · 追踪 " +
            Mathf.RoundToInt(tracking * 100f) + "% · 霸体 " + Mathf.RoundToInt(poise) +
            " · 削盾 " + Mathf.RoundToInt(guardDamage) +
            " · 可取消 " + cancelWindow.ToString("F2") + "s" +
            " · 弹反 " + parryWindow.ToString("F2") + "s" +
            " · 闪避 " + dodgeWindow.ToString("F2") + "s" +
            (telegraph ? " · 有预警" : " · 无预警");
    }

    /// <summary>
    /// 全招式动作语法表。与 MoveTable（空间规格）互补：
    /// MoveTable 回答"打到哪里"，MoveGrammar 回答"什么时候、能不能被反制"。
    /// 两张表共用 PoseState 作主键，改一处即全局生效。
    /// </summary>
    public static class MoveGrammarTable
    {
        static MoveGrammar G(float startup, float active, float recovery, float tracking,
            float poise, float guardDamage, float cancel, float parry, float dodge, bool tele) =>
            new MoveGrammar
            {
                startup = startup, active = active, recovery = recovery, tracking = tracking,
                poise = poise, guardDamage = guardDamage, cancelWindow = cancel,
                parryWindow = parry, dodgeWindow = dodge, telegraph = tele
            };

        // 玩家：轻招快而可取消，重招慢而霸体高——代价换收益，一目了然
        static readonly Dictionary<PoseState, MoveGrammar> Player =
            new Dictionary<PoseState, MoveGrammar>
        {
            { PoseState.PunchJab,    G(0.10f, 0.08f, 0.16f, 0.85f,  4f,  6f, 0.22f, 0.10f, 0.14f, false) },
            { PoseState.PunchCross,  G(0.13f, 0.09f, 0.20f, 0.75f,  6f,  8f, 0.22f, 0.12f, 0.16f, false) },
            { PoseState.Attack,      G(0.18f, 0.12f, 0.26f, 0.70f, 10f, 12f, 0.26f, 0.16f, 0.20f, false) },
            { PoseState.AttackUp,    G(0.22f, 0.12f, 0.30f, 0.60f, 12f, 14f, 0.24f, 0.18f, 0.22f, false) },
            { PoseState.AttackKick,  G(0.20f, 0.10f, 0.28f, 0.65f, 10f, 16f, 0.20f, 0.18f, 0.22f, false) },
            { PoseState.SideKick,    G(0.24f, 0.10f, 0.32f, 0.55f, 12f, 20f, 0.18f, 0.20f, 0.26f, false) },
            { PoseState.SwordThrust, G(0.26f, 0.10f, 0.34f, 0.45f, 14f, 18f, 0.16f, 0.22f, 0.28f, true) },
            { PoseState.SpinKick,    G(0.30f, 0.16f, 0.38f, 0.35f, 20f, 26f, 0.14f, 0.24f, 0.32f, true) },
            { PoseState.JumpKick,    G(0.28f, 0.18f, 0.40f, 0.30f, 18f, 22f, 0.12f, 0.22f, 0.30f, true) },
            { PoseState.AttackSpin,  G(0.34f, 0.22f, 0.44f, 0.25f, 26f, 30f, 0.12f, 0.26f, 0.34f, true) },
            { PoseState.Sweep,       G(0.24f, 0.16f, 0.34f, 0.40f, 14f, 18f, 0.16f, 0.20f, 0.26f, false) },
            { PoseState.JumpAttack,  G(0.30f, 0.16f, 0.42f, 0.30f, 20f, 22f, 0.12f, 0.22f, 0.30f, true) },
            { PoseState.AttackLeap,  G(0.36f, 0.18f, 0.48f, 0.20f, 28f, 28f, 0.10f, 0.26f, 0.36f, true) },
            { PoseState.HeavyAttack, G(0.55f, 0.20f, 0.60f, 0.15f, 40f, 36f, 0.08f, 0.32f, 0.45f, true) },
        };

        // 敌人：与玩家同构，但**危险攻击一律带 Telegraph**——
        // 这是方案 7.4 的硬性要求：所有关键攻击必须有可学习的预警。
        static readonly Dictionary<PoseState, MoveGrammar> Enemy =
            new Dictionary<PoseState, MoveGrammar>
        {
            { PoseState.PunchCross,  G(0.26f, 0.10f, 0.30f, 0.60f,  8f, 10f, 0f, 0.20f, 0.24f, false) },
            { PoseState.Attack,      G(0.34f, 0.12f, 0.36f, 0.55f, 12f, 14f, 0f, 0.26f, 0.30f, false) },
            { PoseState.AttackUp,    G(0.38f, 0.12f, 0.40f, 0.50f, 14f, 16f, 0f, 0.28f, 0.32f, false) },
            { PoseState.AttackKick,  G(0.36f, 0.10f, 0.38f, 0.55f, 12f, 18f, 0f, 0.28f, 0.32f, false) },
            { PoseState.SwordThrust, G(0.44f, 0.10f, 0.46f, 0.40f, 16f, 20f, 0f, 0.34f, 0.40f, true) },
            { PoseState.SideKick,    G(0.42f, 0.10f, 0.44f, 0.45f, 14f, 22f, 0f, 0.32f, 0.38f, true) },
            { PoseState.SpinKick,    G(0.50f, 0.16f, 0.52f, 0.30f, 22f, 28f, 0f, 0.38f, 0.46f, true) },
            { PoseState.JumpKick,    G(0.48f, 0.18f, 0.54f, 0.25f, 20f, 24f, 0f, 0.36f, 0.44f, true) },
            { PoseState.AttackSpin,  G(0.56f, 0.22f, 0.58f, 0.20f, 26f, 30f, 0f, 0.42f, 0.50f, true) },
            { PoseState.HeavyAttack, G(0.75f, 0.20f, 0.70f, 0.12f, 40f, 38f, 0f, 0.50f, 0.60f, true) },
        };

        static readonly MoveGrammar FallbackPlayer =
            G(0.20f, 0.12f, 0.28f, 0.60f, 10f, 12f, 0.20f, 0.16f, 0.20f, false);
        static readonly MoveGrammar FallbackEnemy =
            G(0.36f, 0.12f, 0.40f, 0.50f, 12f, 14f, 0f, 0.28f, 0.32f, false);

        public static MoveGrammar Get(PoseState p, bool isEnemy)
        {
            var table = isEnemy ? Enemy : Player;
            return table.TryGetValue(p, out var g) ? g : (isEnemy ? FallbackEnemy : FallbackPlayer);
        }

        public static IEnumerable<KeyValuePair<PoseState, MoveGrammar>> All(bool isEnemy) =>
            isEnemy ? Enemy : Player;

        /// <summary>
        /// 公平性自检：**重招必须可读**。
        /// 任何削韧 ≥24 却可读性不足的招都会在这里被点名——
        /// 与其等玩家骂"这招没法躲"，不如让数据表先说出来。
        /// </summary>
        public static List<string> AuditUnreadableHeavyMoves(bool isEnemy)
        {
            var bad = new List<string>();
            foreach (var kv in All(isEnemy))
            {
                var g = kv.Value;
                if (g.guardDamage >= 24f && g.Readability < 0.7f)
                    bad.Add(kv.Key + "（可读性 " + g.Readability.ToString("F2") + "）");
            }
            return bad;
        }
    }
}
