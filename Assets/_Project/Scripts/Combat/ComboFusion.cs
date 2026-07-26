using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 战斗动作的「元素」——不再只有拳与剑，一切基础动作与技能都是可入连招的元素。
    /// 这是「放开限制」的前提：只有所有动作都成为同一套词汇里的字，
    /// 才谈得上自由组词造句。
    /// </summary>
    public enum MoveToken
    {
        Punch,    // 拳（P）
        Sword,    // 剑（K）
        Heavy,    // 重击/蓄力（H）
        Jump,     // 跳跃（J）
        Dodge,    // 闪避/翻滚（D）
        Skill,    // 技能（S）
        Guard,    // 格挡/招架（G）
    }

    /// <summary>
    /// 自由融合连招系统（本作对「无所不能的战斗天才」的机制化表达）。
    ///
    /// 传统做法：只认几条固定配方（PPKK、KKPP…），配方之外的组合一律按普通攻击处理——
    /// 玩家实际被限制在设计师预设的几套里。
    ///
    /// 本系统改为**双轨**：
    ///   ① 具名绝招：经典配方仍然存在，打出即触发专属演出与命名（辨识度）；
    ///   ② 【自由融合】：任何 3 元素以上的组合，只要**元素种类够多**就自动成招——
    ///      伤害随「用到几种不同元素」阶梯上升，招名由实际打出的元素动态生成。
    ///      拳→跳→剑→技能→闪 这种没人预设过的串法，一样能打出五元融合。
    ///
    /// 设计意图：奖励的不是"背下正确按键顺序"，而是**临场把各种手段串起来的能力**——
    /// 这才是武术高手与背招表的新手之间的真实差别。
    /// </summary>
    public class ComboFusion
    {
        /// <summary>元素在链中的最长存活时间：超时即断链（连招要连贯，不能慢慢凑）。</summary>
        public const float ChainWindow = 2.2f;
        const int MaxChain = 10;

        readonly List<MoveToken> _chain = new List<MoveToken>();
        readonly List<float> _times = new List<float>();

        /// <summary>记录一个元素（任何基础动作/技能被使用时调用）。</summary>
        public void Push(MoveToken t)
        {
            Trim();
            _chain.Add(t);
            _times.Add(Time.time);
            if (_chain.Count > MaxChain) { _chain.RemoveAt(0); _times.RemoveAt(0); }
        }

        /// <summary>清空链（连段收尾/被打断时调用）。</summary>
        public void Clear() { _chain.Clear(); _times.Clear(); }

        void Trim()
        {
            while (_times.Count > 0 && Time.time - _times[0] > ChainWindow)
            { _times.RemoveAt(0); _chain.RemoveAt(0); }
        }

        /// <summary>当前链长（有效窗口内）。</summary>
        public int Length { get { Trim(); return _chain.Count; } }

        /// <summary>当前链中用到的不同元素种类数——融合等级的依据。</summary>
        public int Variety
        {
            get
            {
                Trim();
                int mask = 0;
                foreach (var t in _chain) mask |= 1 << (int)t;
                int n = 0;
                while (mask != 0) { n += mask & 1; mask >>= 1; }
                return n;
            }
        }

        /// <summary>
        /// 融合增伤：按「用到几种不同元素」阶梯上升。
        /// 单一元素连打（只会 P P P P）没有加成——想要高伤害就必须真的把
        /// 拳、剑、重、跳、闪、技能串起来用。
        /// </summary>
        public float FusionMult
        {
            get
            {
                switch (Variety)
                {
                    case 0:
                    case 1: return 1.00f;
                    case 2: return 1.15f;
                    case 3: return 1.35f;
                    case 4: return 1.60f;
                    case 5: return 1.90f;
                    default: return 2.25f;   // 6 种以上：全能融合
                }
            }
        }

        /// <summary>融合是否成立（3 元素以上且至少 3 种不同）——低于此不打扰玩家。</summary>
        public bool FusionReady => Length >= 3 && Variety >= 3;

        static string Name(MoveToken t)
        {
            switch (t)
            {
                case MoveToken.Punch: return "拳";
                case MoveToken.Sword: return "剑";
                case MoveToken.Heavy: return "重";
                case MoveToken.Jump: return "跃";
                case MoveToken.Dodge: return "闪";
                case MoveToken.Skill: return "术";
                default: return "架";
            }
        }

        /// <summary>元素的单字母代号——跨元素配方用它做字符串匹配（P拳 K剑 H重 J跃 D闪 S术 G架）。</summary>
        public static char Code(MoveToken t)
        {
            switch (t)
            {
                case MoveToken.Punch: return 'P';
                case MoveToken.Sword: return 'K';
                case MoveToken.Heavy: return 'H';
                case MoveToken.Jump: return 'J';
                case MoveToken.Dodge: return 'D';
                case MoveToken.Skill: return 'S';
                default: return 'G';
            }
        }

        /// <summary>
        /// 当前链的代号串（如 "JKH"）。跨元素配方在此之上匹配：
        /// 与旧的 P/K 连段序列不同，这里**任何**基础动作、技能、绝招都在同一串里，
        /// 所以「跳→剑→重」「闪→术→剑」这类跨系统的组合才可能被识别成招。
        /// </summary>
        public string CodeString()
        {
            Trim();
            var sb = new StringBuilder(_chain.Count);
            foreach (var t in _chain) sb.Append(Code(t));
            return sb.ToString();
        }

        /// <summary>链尾是否为给定代号串（配方判定）。</summary>
        public bool TailIs(string code)
        {
            Trim();
            int n = code.Length;
            if (_chain.Count < n) return false;
            for (int i = 0; i < n; i++)
                if (Code(_chain[_chain.Count - n + i]) != code[i]) return false;
            return true;
        }

        /// <summary>抹掉链尾 n 个元素（成招后消耗掉这一串，避免同一串反复触发）。</summary>
        public void ConsumeTail(int n)
        {
            for (int i = 0; i < n && _chain.Count > 0; i++)
            {
                _chain.RemoveAt(_chain.Count - 1);
                _times.RemoveAt(_times.Count - 1);
            }
        }

        static readonly string[] Tiers =
            { "", "", "双元", "三元", "四元", "五元", "全能" };

        /// <summary>
        /// 动态招名：由**实际打出的元素**生成，而不是从预设表里查。
        /// 例：拳→跃→剑→术 打出「拳·跃·剑·术 — 四元融合」。
        /// 每一种打法都有自己的名字，这是"展现一切可能性"的直观反馈。
        /// </summary>
        public string FusionName()
        {
            Trim();
            var seen = new List<MoveToken>();
            foreach (var t in _chain) if (!seen.Contains(t)) seen.Add(t);
            var sb = new StringBuilder();
            foreach (var t in seen)
            {
                if (sb.Length > 0) sb.Append('·');
                sb.Append(Name(t));
            }
            int v = Mathf.Clamp(seen.Count, 0, Tiers.Length - 1);
            return sb.Append(" — ").Append(Tiers[v]).Append("融合").ToString();
        }

        /// <summary>链的可读序列（HUD 显示当前正在串的元素）。</summary>
        public string ChainLabel()
        {
            Trim();
            var sb = new StringBuilder();
            foreach (var t in _chain)
            {
                if (sb.Length > 0) sb.Append('·');
                sb.Append(Name(t));
            }
            return sb.ToString();
        }
    }
}
