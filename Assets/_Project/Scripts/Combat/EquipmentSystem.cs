using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Player;
using AdversityRoad.Personalization;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 兵器与装备套装系统（方案「十、玩家兵器与装备系统」）：
    /// 每套装备象征一件兵器与一种成长能力，落成可切换的战斗加成——
    /// 边界守卫套减免索取伤害、专注夺回套过滤噪声、公平复盘套把刺痛转化为攻击、
    /// 行动起步套加速起步、旧我整合套弱化旧我回声。装备选择本地持久化。
    ///
    /// 加成全部映射到已有系统：来袭心理伤害减免（按弱点轴）、物理输出倍率、
    /// 反刍累积倍率、心理恢复速率、复盘归档回补加成、以及边界/专注上限提升。
    /// </summary>
    public class EquipmentSystem : MonoBehaviour
    {
        public struct SetDef
        {
            public string name;          // 套装名
            public string weapon;        // 象征兵器
            public string coreLine;      // 核心台词
            public string[] effects;     // 面板展示的效果条目

            public WeaknessAxis[] mitigated; // 减免来袭心理伤害的弱点轴
            public float mitigateMult;       // 命中上述轴时的伤害倍率（<1 减伤）
            public float outgoingMult;       // 物理输出倍率
            public float ruminationGainMult; // 反刍累积倍率（<1 更难被拖进反刍）
            public float archiveBonus;       // 战后复盘「归档」额外回补
            public float boundaryMaxBonus;   // 边界上限提升
            public float focusMaxBonus;      // 专注上限提升
            public float mentalRegenBonus;   // 心理属性每秒恢复加成
        }

        // 索引 0 = 无套装（自由行者），1-5 对应方案五套装备。顺序与面板一致。
        public static readonly SetDef[] Defs =
        {
            new SetDef {
                name = "自由行者", weapon = "赤手", coreLine = "先靠自己站稳，再谈其它。",
                effects = new[] { "无套装加成", "适合熟悉基础动作与姿态" },
                mitigated = new WeaknessAxis[0], mitigateMult = 1f, outgoingMult = 1f,
                ruminationGainMult = 1f },

            new SetDef {
                name = "边界守卫套", weapon = "边界盾", coreLine = "我不是你的钱包，也不是你的替身人生。",
                effects = new[] {
                    "边界上限 +30",
                    "索取 / 公平责任类心理伤害 ×0.7",
                    "适合老实人消耗与责任转嫁线" },
                mitigated = new[] { WeaknessAxis.BoundaryConflict, WeaknessAxis.FairnessSensitivity },
                mitigateMult = 0.7f, outgoingMult = 1f, ruminationGainMult = 1f,
                boundaryMaxBonus = 30f },

            new SetDef {
                name = "专注夺回套", weapon = "定心棍", coreLine = "我听见了，但我不跟随。",
                effects = new[] {
                    "专注上限 +30",
                    "咳嗽 / 眼神 / 低语等噪声干扰伤害 ×0.6",
                    "适合外界刺激线" },
                mitigated = new[] { WeaknessAxis.NoiseSensitivity },
                mitigateMult = 0.6f, outgoingMult = 1f, ruminationGainMult = 1f,
                focusMaxBonus = 30f },

            new SetDef {
                name = "公平复盘套", weapon = "事实之刃", coreLine = "我记住事实，但不被事实伤口困住。",
                effects = new[] {
                    "物理输出 ×1.08（事实之刃增伤）",
                    "公平刺痛更难转化为反刍（反刍累积 ×0.5）",
                    "战后归档额外回补 +15" },
                mitigated = new WeaknessAxis[0], mitigateMult = 1f, outgoingMult = 1.08f,
                ruminationGainMult = 0.5f, archiveBonus = 15f },

            new SetDef {
                name = "行动起步套", weapon = "行动拳套", coreLine = "不等动力，先开始。",
                effects = new[] {
                    "物理输出 ×1.06",
                    "拖延 / 低信心 / 求职焦虑伤害 ×0.75",
                    "心理恢复速率提升（行动力更快回涨）" },
                mitigated = new[] { WeaknessAxis.Procrastination, WeaknessAxis.LowConfidence,
                                    WeaknessAxis.JobAnxiety },
                mitigateMult = 0.75f, outgoingMult = 1.06f, ruminationGainMult = 1f,
                mentalRegenBonus = 1.5f },

            new SetDef {
                name = "旧我整合套", weapon = "旧事档案匣", coreLine = "你曾经保护过我，但现在我要继续向前。",
                effects = new[] {
                    "意志崩塌 / 失败恐惧 / 自我怀疑 / 羞耻伤害 ×0.7",
                    "旧事回声更难累积（反刍累积 ×0.6）",
                    "战后归档额外回补 +25（终局整合）" },
                mitigated = new[] { WeaknessAxis.WillpowerCollapse, WeaknessAxis.FailureFear,
                                    WeaknessAxis.SelfDoubt, WeaknessAxis.Shame },
                mitigateMult = 0.7f, outgoingMult = 1f, ruminationGainMult = 0.6f,
                archiveBonus = 25f },
        };

        const string PrefKey = "player_equipment";

        int _index;
        bool _applied;

        public int Index => _index;
        public SetDef CurrentDef => Defs[_index];

        void Start()
        {
            int saved = Mathf.Clamp(PlayerPrefs.GetInt(PrefKey, 0), 0, Defs.Length - 1);
            _index = saved;
            ApplyPassive(Defs[_index], +1);
            _applied = true;
        }

        /// <summary>切换套装：撤销旧套被动，应用新套被动，持久化并提示核心台词。</summary>
        public void Equip(int i)
        {
            if (i < 0 || i >= Defs.Length || i == _index) return;
            if (_applied) ApplyPassive(Defs[_index], -1);
            _index = i;
            ApplyPassive(Defs[_index], +1);
            _applied = true;

            PlayerPrefs.SetInt(PrefKey, _index);
            PlayerPrefs.Save();

            var d = Defs[_index];
            GameEvents.RaiseSubtitle("已装备 · " + d.name + "（" + d.weapon + "）：" + d.coreLine);
        }

        /// <summary>被匹配轴命中时按套装倍率减免这次心理伤害（与姿态减伤叠乘）。</summary>
        public float IncomingMentalMult(WeaknessAxis axis)
        {
            var d = Defs[_index];
            if (d.mitigated != null)
                for (int i = 0; i < d.mitigated.Length; i++)
                    if (d.mitigated[i] == axis) return d.mitigateMult;
            return 1f;
        }

        public float OutgoingPhysicalMult() => Defs[_index].outgoingMult;
        public float RuminationGainMult() => Defs[_index].ruminationGainMult;
        public float ArchiveBonus => Defs[_index].archiveBonus;

        /// <summary>应用/撤销套装的持久被动（上限提升、恢复加成、反刍倍率）。sign=+1 应用，-1 撤销。</summary>
        void ApplyPassive(SetDef d, int sign)
        {
            var stats = GetStats();
            if (stats == null) return;

            if (d.boundaryMaxBonus != 0f)
            {
                stats.maxBoundary += d.boundaryMaxBonus * sign;
                if (sign > 0) stats.boundary += d.boundaryMaxBonus;              // 装备时补满新增上限
                else stats.boundary = Mathf.Min(stats.boundary, stats.maxBoundary);
                GameEvents.RaiseMentalStatChanged("boundary", stats.boundary, stats.maxBoundary);
            }
            if (d.focusMaxBonus != 0f)
            {
                stats.maxFocus += d.focusMaxBonus * sign;
                if (sign > 0) stats.focus += d.focusMaxBonus;
                else stats.focus = Mathf.Min(stats.focus, stats.maxFocus);
                GameEvents.RaiseMentalStatChanged("focus", stats.focus, stats.maxFocus);
            }
            if (d.mentalRegenBonus != 0f)
                stats.mentalRegenPerSec = Mathf.Max(0f, stats.mentalRegenPerSec + d.mentalRegenBonus * sign);

            // 反刍累积倍率：多套并存时按当前套覆盖（撤销即还原为 1）。
            stats.ruminationGainMult = (sign > 0) ? d.ruminationGainMult : 1f;
        }

        PlayerStats GetStats()
        {
            var pc = GetComponent<PlayerController>();
            return pc != null ? pc.Stats : null;
        }
    }
}
