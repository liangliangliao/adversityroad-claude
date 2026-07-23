using System;
using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.Player;

namespace AdversityRoad.Core
{
    [Serializable]
    class FailureData
    {
        public int totalDeaths;
        public List<string> killerIds = new List<string>();     // 与下面两个平行
        public List<string> killerLabels = new List<string>();
        public List<int> killerCounts = new List<int>();

        // 复盘种子：最近一次死亡的诊断，供再战后「复盘」面板预填
        public bool seedPending;
        public string seedFeeling = "";
        public string seedAction = "";
        public string lastKillerLabel = "";
    }

    /// <summary>
    /// 失败档案（方案"失败不惩罚、失败即复盘的起点"落地的数据层）：
    /// 记录累计倒下次数、每个心魔把你击倒的次数、最近一次死亡的诊断种子。
    /// 让重复的失败变成可识别的模式与逐步升级的引导——失败是事实，不是身份。
    /// 本地持久化，可随存档删除。
    /// </summary>
    public static class FailureLog
    {
        const string Key = "adversity_failure_log";
        static FailureData _d;

        /// <summary>最近一次对玩家造成伤害的心魔 id（用于死亡归因）。</summary>
        public static string LastAttackerId { get; private set; }
        public static float LastAttackerTime { get; private set; }

        static FailureData D()
        {
            if (_d == null)
            {
                string json = PlayerPrefs.GetString(Key, "");
                _d = string.IsNullOrEmpty(json) ? new FailureData()
                    : JsonUtility.FromJson<FailureData>(json) ?? new FailureData();
            }
            return _d;
        }

        static void Save()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(D()));
            PlayerPrefs.Save();
        }

        /// <summary>战斗中每次挨打记录来袭者（近 8 秒内的最后一击视为致死归因）。</summary>
        public static void NoteHit(string attackerId)
        {
            if (string.IsNullOrEmpty(attackerId)) return;
            LastAttackerId = attackerId;
            LastAttackerTime = Time.unscaledTime;
        }

        static int IndexOf(string id)
        {
            var d = D();
            for (int i = 0; i < d.killerIds.Count; i++)
                if (d.killerIds[i] == id) return i;
            return -1;
        }

        /// <summary>登记一次死亡；返回被同一心魔击倒的累计次数。</summary>
        public static int RecordDeath(string killerId, string killerLabel)
        {
            var d = D();
            d.totalDeaths++;
            int count = 1;
            if (!string.IsNullOrEmpty(killerId))
            {
                int idx = IndexOf(killerId);
                if (idx < 0)
                {
                    d.killerIds.Add(killerId);
                    d.killerLabels.Add(killerLabel ?? "");
                    d.killerCounts.Add(1);
                }
                else
                {
                    d.killerCounts[idx]++;
                    if (!string.IsNullOrEmpty(killerLabel)) d.killerLabels[idx] = killerLabel;
                    count = d.killerCounts[idx];
                }
            }
            d.lastKillerLabel = killerLabel ?? "";
            Save();
            return count;
        }

        public static int TotalDeaths => D().totalDeaths;

        public static int DeathsTo(string killerId)
        {
            int idx = IndexOf(killerId);
            return idx < 0 ? 0 : D().killerCounts[idx];
        }

        /// <summary>被击倒最多的心魔（label, 次数）；无记录返回空。</summary>
        public static (string label, int count) MostCommon()
        {
            var d = D();
            int best = -1, bestCount = 0;
            for (int i = 0; i < d.killerCounts.Count; i++)
                if (d.killerCounts[i] > bestCount) { bestCount = d.killerCounts[i]; best = i; }
            return best < 0 ? ("", 0) : (d.killerLabels[best], bestCount);
        }

        /// <summary>各心魔击倒次数明细（label, count），按次数降序。</summary>
        public static List<(string label, int count)> Breakdown()
        {
            var d = D();
            var list = new List<(string, int)>();
            for (int i = 0; i < d.killerCounts.Count; i++)
                list.Add((string.IsNullOrEmpty(d.killerLabels[i]) ? "未知心魔" : d.killerLabels[i],
                    d.killerCounts[i]));
            list.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            return list;
        }

        // ===== 复盘种子：死亡诊断 → 再战后预填「复盘」面板 =====

        public static void SetSeed(string feeling, string action)
        {
            var d = D();
            d.seedPending = true;
            d.seedFeeling = feeling ?? "";
            d.seedAction = action ?? "";
            Save();
        }

        public static bool HasSeed => D().seedPending;

        /// <summary>取用并清除复盘种子（供「复盘」面板首次打开预填）。</summary>
        public static (string feeling, string action) ConsumeSeed()
        {
            var d = D();
            var r = (d.seedFeeling, d.seedAction);
            d.seedPending = false;
            Save();
            return r;
        }

        public static void DeleteAll()
        {
            PlayerPrefs.DeleteKey(Key);
            _d = null;
            LastAttackerId = null;
        }
    }

    /// <summary>
    /// 失败诊断：从死亡时的心理数值 + 致死心魔，给出"为什么这次输了"的个性化分析
    /// 与针对性策略提示。围绕方案的心理数值轴（意志/专注/自尊/边界/反刍/体力）。
    /// </summary>
    public static class FailureAnalyzer
    {
        public struct Diagnosis
        {
            public string cause;    // 一句因果
            public string tip;      // 针对性策略
            public string axisKey;  // will/focus/selfWorth/boundary/rumination/physical
        }

        public static Diagnosis Diagnose(PlayerStats s)
        {
            if (s == null)
                return new Diagnosis
                {
                    cause = "这一战没能撑住。",
                    tip = "调整节奏，抓预警圈后的输出窗口再来。",
                    axisKey = "physical"
                };

            float rum = s.maxRumination > 0 ? s.rumination / s.maxRumination : 0f;
            float will = Ratio(s.will, s.maxWill);
            float focus = Ratio(s.focus, s.maxFocus);
            float self = Ratio(s.selfWorth, s.maxSelfWorth);
            float bound = Ratio(s.boundary, s.maxBoundary);

            // 反刍高企优先归因（同一个念头把状态磨空）
            if (rum > 0.6f)
                return Make("rumination",
                    "反刍缠住了你——同一个念头反复回放，把状态一点点磨空。",
                    "战后去『复盘』面板把它归档（反刍清零）；战斗中被缠住时用降反刍的技能。");

            // 最被击穿的心理轴
            float min = Mathf.Min(will, Mathf.Min(focus, Mathf.Min(self, bound)));
            if (min < 0.35f)
            {
                if (min == will) return Make("will",
                    "意志见底——低谷、放弃、旧我的压迫把你耗垮了。",
                    "优先补意志：用取暖点/求助电话/火种台等场景机制，别一味硬扛。");
                if (min == focus) return Make("focus",
                    "专注被打散——噪声与凝视夺走了你的注意力。",
                    "用『定心格挡』化解心理攻击，『不读心盾/注意力回收』清掉幻影再打真身。");
                if (min == self) return Make("selfWorth",
                    "自尊被击穿——否定与羞耻的攻击奏效了。",
                    "先去证据/事实点看清事实，浮动标签一击即碎；守住自己的判断。");
                return Make("boundary",
                    "边界失守——索取与责任转嫁把你一点点掏空。",
                    "举盾=明确拒绝，把红球挡回去（责任归还），站进边界圈恢复。");
            }

            // 心理都还稳：是体力/节奏问题
            return Make("physical",
                "体力管理失误——格挡/闪避/技能消耗过快，或硬拼输了节奏。",
                "留住体力：非必要不硬格挡，多用闪避找破绽，抓预警圈之后的输出窗口。");
        }

        static float Ratio(float v, float max) => max > 0 ? Mathf.Clamp01(v / max) : 1f;

        static Diagnosis Make(string key, string cause, string tip) =>
            new Diagnosis { axisKey = key, cause = cause, tip = tip };
    }
}
