using System.Collections.Generic;
using UnityEngine;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 攻击轨迹形态（大型动作游戏的判定分类学）：
    /// 每一招的判定不是随便一个盒子，而是由它的**运动轨迹**决定形状。
    /// </summary>
    public enum HitTrajectory
    {
        Point,    // 点：拳类短促直击，判定小而集中——最精准，容错最低
        Line,     // 线：突刺/直踢，细长直线——穿透纵深，横向容错低
        ArcH,     // 横弧：横斩/扫击，横向展开——扫一片，适合多目标
        ArcV,     // 纵弧：撩斩/上挑，纵向展开——打高低差、挑飞
        Circle,   // 环：旋风斩/扫堂腿，环身 360°——被围时清场
        Plane,    // 面：蓄力斩/冲击波，大范围罩住前方——最霸道，前摇最长
        Aerial,   // 空：跳劈/飞踢，凌空罩住落点——带高低差与位移
    }

    /// <summary>
    /// 单招规格：轨迹 + 尺度 + 判定框 + 伤害系数。
    /// 大型动作游戏对每一招都有明确的数据表（reach/width/height/frame/damage），
    /// 而不是"差不多开个盒子"。本表把散落在各处的数值集中成可查、可校对的规格，
    /// 「招式」面板据此向玩家展示每招的轨迹与范围。
    /// </summary>
    public struct MoveSpec
    {
        public string label;            // 招式名
        public HitTrajectory traj;      // 轨迹形态
        public float width;             // 横向覆盖（米）
        public float height;            // 纵向覆盖（米）
        public float reach;             // 纵深/触及距离（米）
        public Vector3 center;          // 判定框中心（相对角色根）
        public float damageMult;        // 伤害系数（相对基础伤害）
        public float postureMult;       // 削韧系数
        public float knockback;         // 击退

        public Vector3 Size => new Vector3(width, height, reach);

        /// <summary>轨迹的中文说明（面板展示用）。</summary>
        public string TrajLabel
        {
            get
            {
                switch (traj)
                {
                    case HitTrajectory.Point: return "点·直击";
                    case HitTrajectory.Line: return "线·穿刺";
                    case HitTrajectory.ArcH: return "横弧·扫击";
                    case HitTrajectory.ArcV: return "纵弧·挑击";
                    case HitTrajectory.Circle: return "环·周身";
                    case HitTrajectory.Plane: return "面·范围";
                    default: return "空·凌空";
                }
            }
        }
    }

    /// <summary>
    /// 全招式规格表（玩家侧）。判定框由此统一派生——
    /// 改数值只需改这一处，战斗代码与 UI 面板共用同一份真相。
    /// </summary>
    public static class MoveTable
    {
        static MoveSpec M(string label, HitTrajectory t, float w, float h, float r,
            Vector3 c, float dmg, float posture, float knock) => new MoveSpec
            {
                label = label, traj = t, width = w, height = h, reach = r,
                center = c, damageMult = dmg, postureMult = posture, knockback = knock
            };

        static readonly Dictionary<PoseState, MoveSpec> Table = new Dictionary<PoseState, MoveSpec>
        {
            // ---- 剑系（重连段：伤害高、击退大）----
            { PoseState.Attack,      M("巨剑横斩", HitTrajectory.ArcH,  2.2f, 1.2f, 1.7f, new Vector3(0, 0.10f, 1.0f), 1.10f, 10f, 4.5f) },
            { PoseState.AttackUp,    M("巨剑撩斩", HitTrajectory.ArcV,  1.3f, 2.3f, 1.7f, new Vector3(0, 0.40f, 1.0f), 1.25f, 12f, 4.5f) },
            { PoseState.SwordThrust, M("弓步突刺", HitTrajectory.Line,  0.9f, 0.9f, 2.7f, new Vector3(0, 0.15f, 1.5f), 1.45f, 14f, 3.0f) },
            { PoseState.AttackSpin,  M("旋风绝斩", HitTrajectory.Circle,3.6f, 1.4f, 3.6f, new Vector3(0, 0.15f, 0.2f), 2.00f, 28f, 6.5f) },
            { PoseState.HeavyAttack, M("蓄力跳劈", HitTrajectory.Plane, 4.2f, 3.6f, 4.8f, new Vector3(0, 0.40f, 2.2f), 2.40f, 26f, 3.5f) },
            { PoseState.AttackLeap,  M("裂地跳劈", HitTrajectory.Aerial,2.8f, 2.6f, 2.8f, new Vector3(0,-0.10f, 1.1f), 1.90f, 30f, 3.0f) },

            // ---- 拳系（轻连段：出手快、削韧高、积势快）----
            { PoseState.PunchJab,    M("前手直拳", HitTrajectory.Point, 0.9f, 1.0f, 1.5f, new Vector3(0, 0.25f, 0.9f), 0.70f, 14f, 1.0f) },
            { PoseState.PunchCross,  M("交叉重拳", HitTrajectory.Point, 1.0f, 1.0f, 1.6f, new Vector3(0, 0.25f, 1.0f), 0.80f, 16f, 1.0f) },

            // ---- 腿系 ----
            { PoseState.AttackKick,  M("正踢",     HitTrajectory.Line,  1.0f, 1.3f, 1.8f, new Vector3(0, 0.00f, 1.1f), 0.90f, 20f, 2.0f) },
            { PoseState.SideKick,    M("侧踹",     HitTrajectory.Line,  1.1f, 1.1f, 2.0f, new Vector3(0, 0.10f, 1.2f), 1.05f, 24f, 2.0f) },
            { PoseState.SpinKick,    M("旋身空翻踢",HitTrajectory.ArcH, 2.8f, 1.7f, 2.4f, new Vector3(0, 0.20f, 0.6f), 1.20f, 34f, 9.0f) },
            { PoseState.JumpKick,    M("惊鸿飞踢", HitTrajectory.Aerial,1.3f, 1.7f, 2.4f, new Vector3(0, 0.20f, 1.3f), 1.40f, 26f, 4.0f) },
            { PoseState.Sweep,       M("扫堂腿",   HitTrajectory.Circle,3.3f, 0.8f, 3.3f, new Vector3(0,-0.55f, 0.15f),0.90f, 30f, 1.5f) },
            { PoseState.JumpAttack,  M("空袭下劈", HitTrajectory.Aerial,2.1f, 2.5f, 2.3f, new Vector3(0,-0.30f, 1.1f), 1.50f, 22f, 2.5f) },
        };

        static readonly MoveSpec Fallback =
            M("通用打击", HitTrajectory.Point, 1.4f, 1.4f, 1.8f, new Vector3(0, 0.1f, 1.1f), 1f, 12f, 2f);

        /// <summary>
        /// 派生变体：与基础招共用动画姿态、但判定形状/系数另有一套的招。
        /// （蹲姿下段突刺压低判定、空中续段放大判定……）
        /// 单靠 PoseState 索引不到它们，但它们同样是"要算清楚"的招，
        /// 所以一并入表，而不是散在各处写死。
        /// </summary>
        static readonly Dictionary<string, MoveSpec> Variants = new Dictionary<string, MoveSpec>
        {
            { "低位突刺",     M("低位突刺",   HitTrajectory.Line,   0.9f, 0.8f, 2.5f, new Vector3(0,-0.45f, 1.4f), 1.20f, 16f, 1.5f) },
            { "空中回旋绝斩", M("空中回旋绝斩", HitTrajectory.Circle, 4.1f, 1.6f, 4.1f, new Vector3(0, 0.17f, 0.23f), 1.90f, 28f, 5.0f) },
            { "空中连环踢",   M("空中连环踢",  HitTrajectory.ArcH,   3.2f, 2.0f, 2.8f, new Vector3(0, 0.23f, 0.69f), 1.70f, 30f, 3.0f) },
        };

        public static MoveSpec Get(PoseState p) => Table.TryGetValue(p, out var s) ? s : Fallback;

        public static bool Has(PoseState p) => Table.ContainsKey(p);

        public static MoveSpec Variant(string key) => Variants.TryGetValue(key, out var s) ? s : Fallback;

        /// <summary>面板展示用：全部招式规格（按轨迹分组阅读）。</summary>
        public static IEnumerable<KeyValuePair<PoseState, MoveSpec>> All => Table;

        /// <summary>面板展示用：全部派生变体规格。</summary>
        public static IEnumerable<KeyValuePair<string, MoveSpec>> AllVariants => Variants;
    }

    /// <summary>
    /// 敌人招式规格表——与玩家共用同一套**结构**（轨迹/范围/伤害/削韧/击退），
    /// 但各自独立**调值**。共用结构保证敌我的判定都算得清楚、可查可校对；
    /// 独立调值是因为玩家的大招要付蓄力与意势的代价，敌人不付，
    /// 照抄玩家系数会让敌人的重击白白强出一截。
    ///
    /// 判定框按轨迹给形：突刺细长（侧移可躲）、横斩横宽、旋风环身 360°、
    /// 蓄力斩罩住一片——敌人出什么招，就该有什么形状的威胁范围。
    /// </summary>
    public static class EnemyMoveTable
    {
        static MoveSpec M(string label, HitTrajectory t, float w, float h, float r,
            Vector3 c, float dmg, float posture, float knock) => new MoveSpec
            {
                label = label, traj = t, width = w, height = h, reach = r,
                center = c, damageMult = dmg, postureMult = posture, knockback = knock
            };

        static readonly Dictionary<PoseState, MoveSpec> Table = new Dictionary<PoseState, MoveSpec>
        {
            // ---- 基础招（BasicMoves）----
            // 纵深下限：判定框远端 ≥1.75m，加上玩家胶囊半径覆盖到 ~2.15m，
            // 确保 attackRange 最大（2.3）的重甲敌人不会"进入攻击距离却打不到人"。
            { PoseState.Attack,      M("横斩",   HitTrajectory.ArcH,  2.0f, 1.2f, 1.7f, new Vector3(0, 0.10f, 1.00f), 1.00f, 10f, 1.5f) },
            { PoseState.AttackUp,    M("撩斩",   HitTrajectory.ArcV,  1.2f, 2.1f, 1.7f, new Vector3(0, 0.40f, 1.00f), 1.00f, 12f, 1.5f) },
            { PoseState.SwordThrust, M("突刺",   HitTrajectory.Line,  0.8f, 0.9f, 2.4f, new Vector3(0, 0.15f, 1.35f), 1.15f, 14f, 1.5f) },
            { PoseState.PunchCross,  M("重拳",   HitTrajectory.Point, 0.9f, 1.0f, 1.6f, new Vector3(0, 0.25f, 0.95f), 0.90f, 14f, 1.2f) },
            { PoseState.AttackKick,  M("正踢",   HitTrajectory.Line,  0.9f, 1.2f, 1.8f, new Vector3(0, 0.00f, 1.05f), 0.95f, 18f, 2.5f) },

            // ---- 精英招（EliteMoves）：更大范围、更强击退，但前摇更长（读招窗口）----
            { PoseState.HeavyAttack, M("重砸",   HitTrajectory.Plane, 2.8f, 3.0f, 2.8f, new Vector3(0, 0.35f, 1.35f), 1.50f, 24f, 4.5f) },
            { PoseState.AttackSpin,  M("回旋斩", HitTrajectory.Circle,3.0f, 1.4f, 3.0f, new Vector3(0, 0.15f, 0.15f), 1.30f, 24f, 3.5f) },
            { PoseState.SpinKick,    M("旋踢",   HitTrajectory.ArcH,  2.5f, 1.6f, 2.2f, new Vector3(0, 0.20f, 0.55f), 1.25f, 28f, 4.0f) },
            { PoseState.SideKick,    M("侧踹",   HitTrajectory.Line,  1.0f, 1.1f, 1.9f, new Vector3(0, 0.10f, 1.10f), 1.00f, 22f, 3.5f) },
            { PoseState.JumpKick,    M("飞踢",   HitTrajectory.Aerial,1.2f, 1.6f, 2.2f, new Vector3(0, 0.20f, 1.20f), 1.20f, 24f, 4.0f) },
        };

        static readonly MoveSpec Fallback =
            M("挥击", HitTrajectory.Point, 1.2f, 1.2f, 1.6f, new Vector3(0, 0.1f, 1.0f), 1f, 12f, 1.5f);

        public static MoveSpec Get(PoseState p) => Table.TryGetValue(p, out var s) ? s : Fallback;

        public static IEnumerable<KeyValuePair<PoseState, MoveSpec>> All => Table;
    }
}
