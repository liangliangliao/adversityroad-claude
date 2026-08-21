using UnityEngine;

namespace AdversityRoad.Combat
{
    /// <summary>身体部位：受击判定的分区（头/躯干/腰腹/双臂/双腿/末端）。</summary>
    public enum BodyPart
    {
        None = 0,
        Head,        // 头颈：要害
        Chest,       // 胸口：标准躯干
        Abdomen,     // 腰腹：闷击，削韧偏高
        ArmL, ArmR,  // 上臂/前臂：护住要害的代价——伤害低但更容易被打散架势
        LegL, LegR,  // 大腿/小腿：伤害低但最容易失衡（扫腿）
        Extremity    // 手/脚：擦到边，伤害最低
    }

    /// <summary>
    /// 部位伤害表（参照大型战斗类游戏的通行分区设计）。
    ///
    /// 这一套系数的来源与取舍：
    ///   · 头部是**唯一的会心区**（射击类的爆头、魂系的头部弱点、只狼的要害），
    ///     倍率明显高于其它部位，但不做一击必杀——本作是格斗节奏，不是狙击；
    ///   · 躯干＝1.0 基准，一切别的部位都相对它读数；
    ///   · 四肢伤害**低于**躯干，但**削韧高于**躯干：这是格斗游戏里"打手打脚"的
    ///     真实收益——砍不动血，却能把对方的架势打散（腿被扫会失衡、手臂被打会脱招）。
    ///     没有这一条，四肢就只是"打了亏"的区域，玩家永远只会对着胸口糊；
    ///   · 末端（手掌/脚掌）只算擦到，最低。
    ///
    /// 【攻防两侧不同号】：玩家的头吃 1.5 倍、敌人的头吃 2.0 倍。
    /// 这是动作游戏的通行做法（玩家侧宽容、敌人侧锋利）——同一套规则玩家学得会，
    /// 但不会因为被一记看不见的横斩擦到头就掉半管血。
    /// </summary>
    public struct PartProfile
    {
        public float damage;    // 物理伤害倍率
        public float posture;   // 削韧倍率
        public float mental;    // 心理伤害倍率
        public string label;    // 弹字用的部位名
        public bool critical;   // 会心区（伤害数字加大加色 + 音效）
    }

    public static class BodyPartTable
    {
        /// <summary>取部位系数。forPlayer=受击方是玩家（玩家侧的要害倍率更宽容）。</summary>
        public static PartProfile Get(BodyPart part, bool forPlayer)
        {
            switch (part)
            {
                case BodyPart.Head:
                    return new PartProfile
                    {
                        // 头：唯一的会心区。玩家侧 1.5、敌人侧 2.0（见类注释）
                        damage = forPlayer ? 1.5f : 2.0f,
                        posture = 1.3f,
                        mental = 1.5f,          // 打头也打心气：晕眩＝心理层面的动摇
                        label = "头部",
                        critical = true
                    };
                case BodyPart.Abdomen:
                    return new PartProfile
                    {
                        damage = 1.05f, posture = 1.2f, mental = 1f, label = "腰腹"
                    };
                case BodyPart.ArmL:
                case BodyPart.ArmR:
                    return new PartProfile
                    {
                        // 手臂：伤害低（挡在要害前面的本来就是它），但打散架势的效率高
                        damage = 0.65f, posture = 1.35f, mental = 0.7f, label = "手臂"
                    };
                case BodyPart.LegL:
                case BodyPart.LegR:
                    return new PartProfile
                    {
                        // 腿：扫腿的价值不在掉血，在失衡
                        damage = 0.7f, posture = 1.5f, mental = 0.6f, label = "腿部"
                    };
                case BodyPart.Extremity:
                    return new PartProfile
                    {
                        damage = 0.5f, posture = 0.9f, mental = 0.5f, label = "末端"
                    };
                default:   // Chest / None：基准
                    return new PartProfile
                    {
                        damage = 1f, posture = 1f, mental = 1f, label = "躯干"
                    };
            }
        }

        /// <summary>部位标签色（伤害数字/部位浮字统一用它，一眼读出打中了哪儿）。</summary>
        public static Color TintOf(BodyPart part)
        {
            switch (part)
            {
                case BodyPart.Head: return new Color(1f, 0.72f, 0.2f);
                case BodyPart.LegL:
                case BodyPart.LegR: return new Color(0.6f, 0.85f, 1f);
                case BodyPart.ArmL:
                case BodyPart.ArmR: return new Color(0.75f, 1f, 0.75f);
                case BodyPart.Extremity: return new Color(0.8f, 0.8f, 0.85f);
                default: return new Color(1f, 0.62f, 0.45f);
            }
        }

        public static bool IsLeg(BodyPart p) => p == BodyPart.LegL || p == BodyPart.LegR;
        public static bool IsArm(BodyPart p) => p == BodyPart.ArmL || p == BodyPart.ArmR;

        /// <summary>
        /// 没有部位受击框时的兜底分区：只按命中点的相对高度粗分。
        /// 精确分区由 <see cref="BodyPartHurtboxes"/> 提供；这里保证任何角色
        /// （包括临时生成的、还没装上部位框的）也有一份合理的区分，而不是全身一个价。
        /// </summary>
        public static BodyPart FromHeight(float localY)
        {
            if (localY > 0.55f) return BodyPart.Head;
            if (localY > 0.1f) return BodyPart.Chest;
            if (localY > -0.45f) return BodyPart.Abdomen;
            return BodyPart.LegL;
        }
    }
}
