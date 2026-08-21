using UnityEngine;

namespace AdversityRoad.Combat
{
    /// <summary>攻击的「征兆族」：同一族的招式共用一套可读征兆与同一个应对答案。</summary>
    public enum TelegraphKind
    {
        Overhead,    // 上段·劈斩：兵器高举、身体后仰
        Horizontal,  // 中段·横斩：兵器拉到体侧、肩背拧紧
        Thrust,      // 突刺：兵器收到腰际、正面对齐、下沉
        LowSweep,    // 下段·扫腿：整个人压低
        Kick,        // 腿法：提膝
        Spin         // 回旋：反向拧身蓄力
    }

    /// <summary>一族征兆的完整规格：怎么看出来、该怎么应对、亮多久。</summary>
    public struct TelegraphSpec
    {
        public TelegraphKind kind;
        public string mark;          // 头顶记号
        public string name;          // 招式族名（字幕/横幅）
        public string answer;        // 应对答案（教学提示）
        public Color color;          // 【颜色即答案】见 TelegraphTable 的注释
        public float windup;         // 起手时长（秒）：同族恒定，可被记住
        public Vector2 ring;         // 地面指示器：x=横向张开, z=纵深
        public float ringHeight;     // 指示器离地高度（下段贴地、上段抬高）
        public float pitch;          // 警示音高（听觉上也分得开）
    }

    /// <summary>
    /// 攻击征兆表——本作"敌人出手前必须可观测、且规律可学"的落点。
    ///
    /// 【为什么之前"几乎看不到征兆"】
    /// 不是没有征兆，是**征兆没有信息**：所有招式共用一个红圈、一个「！」、
    /// 一段同样的蓄力姿态，长度还随攻击性浮动。玩家能看见"要打了"，
    /// 却看不出"打哪一路、我该怎么办"——于是学不到任何东西，只能乱滚。
    ///
    /// 【这一版给出的三层信息】（对齐格斗/魂系/对马岛这类作品的通行做法）
    ///   ① 形体：兵器与身体的**预备姿态**本身就是征兆——高举=劈、后拉=刺、
    ///      压低=扫腿。这一层不依赖任何 UI，玩家最终靠它读招（见 HumanoidAnimator.SetWindup）。
    ///   ② 形状：地面指示器按招式轨迹取形——横斩是横向的宽弧、突刺是细长的直线、
    ///      扫腿是贴地的宽环。"往哪躲"于是有了画面依据。
    ///   ③ 颜色即答案：一种颜色对应一个应对动作，全作只有这四种，永不混用——
    ///        · 蓝白 = 可格挡（挡或架）
    ///        · 青色 = 侧移躲（突刺：往两边让，往后退没用）
    ///        · 黄色 = 跳起躲（下段扫腿：挡不住、蹲不掉）
    ///        · 橙红 = 只能闪避（不可格挡的危险攻击）
    ///   ④ 节奏：同族起手时长**恒定**。可学习的前提是可预期——
    ///      如果同一个动作这次 0.5 秒、下次 0.8 秒，那它就不是规律，是运气。
    /// </summary>
    public static class TelegraphTable
    {
        // ---- 颜色即答案（全作唯一的一张对照表）----
        public static readonly Color Blockable = new Color(0.55f, 0.78f, 1f);   // 蓝白：可挡
        public static readonly Color SideStep = new Color(0.25f, 0.95f, 0.85f); // 青：侧移
        public static readonly Color JumpOver = new Color(1f, 0.9f, 0.25f);     // 黄：跳
        public static readonly Color DodgeOnly = new Color(1f, 0.35f, 0f);      // 橙红：只能闪

        /// <summary>招式 → 征兆族。</summary>
        public static TelegraphKind KindOf(PoseState p)
        {
            switch (p)
            {
                case PoseState.HeavyAttack:
                case PoseState.AttackLeap:
                case PoseState.JumpAttack:
                case PoseState.AttackUp:      return TelegraphKind.Overhead;
                case PoseState.SwordThrust:
                case PoseState.PunchJab:
                case PoseState.PunchCross:    return TelegraphKind.Thrust;
                case PoseState.Sweep:         return TelegraphKind.LowSweep;
                case PoseState.AttackKick:
                case PoseState.SideKick:
                case PoseState.JumpKick:      return TelegraphKind.Kick;
                case PoseState.AttackSpin:
                case PoseState.SpinKick:      return TelegraphKind.Spin;
                default:                      return TelegraphKind.Horizontal;
            }
        }

        /// <summary>取一族征兆的规格。perilous=本次是不可格挡的危险攻击（颜色/记号改写）。</summary>
        public static TelegraphSpec Get(PoseState pose, bool perilous)
        {
            var k = KindOf(pose);
            TelegraphSpec s;
            switch (k)
            {
                case TelegraphKind.Overhead:
                    s = new TelegraphSpec
                    {
                        mark = "↓", name = "上段·劈斩", answer = "举盾格挡，或往两侧让开落点",
                        color = Blockable, windup = 0.78f,
                        ring = new Vector2(1.6f, 2.4f), ringHeight = 0.06f, pitch = 0.02f
                    };
                    break;
                case TelegraphKind.Thrust:
                    s = new TelegraphSpec
                    {
                        // 突刺是唯一"往后退没用"的招：它比你退得快，只能让开轴线
                        mark = "→", name = "突刺", answer = "向左右侧移让开——后退躲不掉",
                        color = SideStep, windup = 0.62f,
                        ring = new Vector2(0.9f, 4.2f), ringHeight = 0.06f, pitch = 0.22f
                    };
                    break;
                case TelegraphKind.LowSweep:
                    s = new TelegraphSpec
                    {
                        mark = "↧", name = "下段·扫腿", answer = "跳起来躲——这一下挡不住",
                        color = JumpOver, windup = 0.66f,
                        ring = new Vector2(3.4f, 3.4f), ringHeight = 0.02f, pitch = -0.18f
                    };
                    break;
                case TelegraphKind.Kick:
                    s = new TelegraphSpec
                    {
                        mark = "！", name = "腿法·踢击", answer = "可格挡；贴身时侧移更稳",
                        color = Blockable, windup = 0.58f,
                        ring = new Vector2(1.8f, 2.6f), ringHeight = 0.06f, pitch = 0.1f
                    };
                    break;
                case TelegraphKind.Spin:
                    s = new TelegraphSpec
                    {
                        mark = "◎", name = "回旋·环身扫", answer = "拉开身位——环身范围挡不干净",
                        color = JumpOver, windup = 0.82f,
                        ring = new Vector2(3.8f, 3.8f), ringHeight = 0.05f, pitch = -0.08f
                    };
                    break;
                default:
                    s = new TelegraphSpec
                    {
                        mark = "—", name = "中段·横斩", answer = "举盾格挡，或后撤半步让开弧线",
                        color = Blockable, windup = 0.7f,
                        ring = new Vector2(3.2f, 2.0f), ringHeight = 0.06f, pitch = 0f
                    };
                    break;
            }
            s.kind = k;
            if (perilous)
            {
                // 危险攻击改写答案层：记号、颜色、时长都换成"只能闪避"的那一套。
                // 前摇**更长**是刻意的——不可格挡的招必须给足闪避反应窗口。
                s.mark = "危";
                s.name = "危险攻击 · " + s.name;
                s.answer = "不可格挡——只能闪避";
                s.color = DodgeOnly;
                s.windup += 0.18f;
                s.pitch = 0.3f;
            }
            return s;
        }
    }
}
