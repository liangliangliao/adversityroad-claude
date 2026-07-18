using System;
using System.Collections.Generic;
using UnityEngine;

namespace AdversityRoad.Core
{
    /// <summary>一条"现实行动承诺"：复盘归档时从「行动」栏生成，等下次回来确认。</summary>
    [Serializable]
    public class ActionCommitment
    {
        public string text;          // 承诺去做的现实小行动
        public string chapterTitle;  // 来自哪一战的复盘
        public string createdAt;     // 生成时间 yyyy-MM-dd HH:mm
        public string createdDay;    // 生成日 yyyy-MM-dd（用于"隔天回访"判定）
        public int status;           // 0 待确认 / 1 已做到 / 2 暂时没做
        public string resolvedDay;   // 确认日 yyyy-MM-dd
    }

    [Serializable]
    class ActionData
    {
        public List<ActionCommitment> items = new List<ActionCommitment>();
        public int streak;              // 连续行动天数
        public string lastDoneDay = ""; // 最近一次"做到"的日期
        public int totalDone;           // 累计完成的现实行动数
    }

    /// <summary>
    /// 现实行动追踪闭环（本作核心命题的落地）：
    /// 复盘里写下的「现实里：去做 X」不再只是一句话——它成为一条承诺，
    /// 下次回到安全屋时游戏会问「上次你说要做 X，做到了吗？」。
    /// 做到 → 复盘点奖励 + 连续行动天数 +（把游戏内成长与现实行动绑定）。
    /// 全部本地持久化，可随存档删除。
    /// </summary>
    public static class ActionSystem
    {
        const string Key = "adversity_action_log";
        static ActionData _d;

        static string Today => DateTime.Now.ToString("yyyy-MM-dd");
        static string Yesterday => DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");

        static ActionData D()
        {
            if (_d == null)
            {
                string json = PlayerPrefs.GetString(Key, "");
                _d = string.IsNullOrEmpty(json) ? new ActionData()
                    : JsonUtility.FromJson<ActionData>(json) ?? new ActionData();
            }
            return _d;
        }

        static void Save()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(D()));
            PlayerPrefs.Save();
        }

        /// <summary>从复盘「行动」栏登记一条现实承诺（同文本已在待办则不重复）。</summary>
        public static void AddCommitment(string text, string chapter)
        {
            text = (text ?? "").Trim();
            if (text.Length < 2) return;
            if (text.Length > 90) text = text.Substring(0, 90);
            var d = D();
            foreach (var c in d.items)
                if (c.status == 0 && c.text == text) return;   // 去重
            d.items.Add(new ActionCommitment
            {
                text = text,
                chapterTitle = chapter ?? "",
                createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                createdDay = Today,
                status = 0,
                resolvedDay = ""
            });
            // 待办上限：只留最近 15 条待确认，太久的自动落为"暂时没做"归档
            int pending = 0;
            for (int i = d.items.Count - 1; i >= 0; i--)
            {
                if (d.items[i].status != 0) continue;
                pending++;
                if (pending > 15) d.items[i].status = 2;
            }
            while (d.items.Count > 60) d.items.RemoveAt(0);
            Save();
        }

        /// <summary>待确认的承诺（最新在前）。</summary>
        public static List<ActionCommitment> Pending()
        {
            var d = D();
            var r = new List<ActionCommitment>();
            for (int i = d.items.Count - 1; i >= 0; i--)
                if (d.items[i].status == 0) r.Add(d.items[i]);
            return r;
        }

        /// <summary>最近已确认（做到/没做）的记录，最多 n 条，最新在前。</summary>
        public static List<ActionCommitment> Recent(int n)
        {
            var d = D();
            var r = new List<ActionCommitment>();
            for (int i = d.items.Count - 1; i >= 0 && r.Count < n; i--)
                if (d.items[i].status != 0) r.Add(d.items[i]);
            return r;
        }

        public static int PendingCount
        {
            get { int n = 0; foreach (var c in D().items) if (c.status == 0) n++; return n; }
        }

        /// <summary>昨天或更早留下、今天还没确认的承诺数（用于回访提醒）。</summary>
        public static int DuePendingCount
        {
            get
            {
                int n = 0;
                foreach (var c in D().items)
                    if (c.status == 0 && c.createdDay != Today) n++;
                return n;
            }
        }

        public static int Streak => D().streak;
        public static int TotalDone => D().totalDone;

        /// <summary>标记"做到了"：更新连续天数、累计数，发放复盘点与状态回补。</summary>
        public static void MarkDone(ActionCommitment c, Player.PlayerStats stats)
        {
            if (c == null || c.status != 0) return;
            var d = D();
            c.status = 1;
            c.resolvedDay = Today;

            if (d.lastDoneDay == Today) { /* 今天已计过连续，保持 */ }
            else if (d.lastDoneDay == Yesterday) d.streak++;
            else d.streak = 1;
            d.lastDoneDay = Today;
            d.totalDone++;
            Save();

            GrowthSystem.AddPoints(2);
            if (stats != null)
            {
                stats.RestoreAxis(Personalization.WeaknessAxis.Procrastination, 30f);
                stats.ReduceRumination(12f);
                stats.RestoreMental(15f);
            }
            GameEvents.RaiseSubtitle("现实行动达成 +复盘点2 · 连续行动 " + d.streak +
                " 天——把它做出来，比在游戏里赢一百次更算数。");
        }

        /// <summary>标记"暂时没做"：温和归档，不惩罚（现实行动允许失败与重来）。</summary>
        public static void MarkSkipped(ActionCommitment c)
        {
            if (c == null || c.status != 0) return;
            c.status = 2;
            c.resolvedDay = Today;
            Save();
            GameEvents.RaiseSubtitle("没关系，留个记号就好——现实行动不求完美，下次再试。");
        }

        public static void DeleteAll()
        {
            PlayerPrefs.DeleteKey(Key);
            _d = null;
        }
    }
}
