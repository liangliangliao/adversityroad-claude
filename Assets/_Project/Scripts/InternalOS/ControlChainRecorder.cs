using System;
using System.Collections.Generic;
using UnityEngine;

namespace AdversityRoad.InternalOS
{
    /// <summary>控制链上的一个节点（第 7.2 节 event 字段）。</summary>
    [Serializable]
    public class ControlChainNode
    {
        public string timestamp;
        public int bossId;
        public string trigger;
        public string automaticThought;    // 自动想法（玩家可见的那一句内部语言）
        public string automaticBehavior;   // 自动行为（实际发生的动作）
        public float stress;
        public float focus;
        public float goalVisibility;       // 目标还在不在工作记忆里（0-1）
        public float actionPower;
        public string counterAction;       // 玩家这一步做了什么反制（空=没有）
        /// <summary>这一步交出去多少方向权（0-1）。多个节点接力时用它找最大劫持点。</summary>
        public float controlShift;
        /// <summary>距离目标行动时间点的秒数，用来算"最早可干预点"。</summary>
        public float secondsFromStart;
    }

    /// <summary>一次"今天没有行动"的完整控制链（第 7.2 节 ControlChainEvent）。</summary>
    [Serializable]
    public class ControlChainEvent
    {
        public string eventId;
        public string goalId;
        public string triggerTime;
        public string goalAction;          // 本来该发生的那个目标动作
        public List<ControlChainNode> events = new List<ControlChainNode>();
        public string finalOutcome;        // Completed / Partial / NotStarted / Withdrawn
    }

    [Serializable]
    public class ControlChainArchive
    {
        public List<ControlChainEvent> chains = new List<ControlChainEvent>();
    }

    /// <summary>
    /// Control Chain（第 7.2 节）。
    ///
    /// 【为什么不能只记"最后失败了"】
    /// 同一个"今天没写那份东西"，可能是冻结之王先夺走启动、习惯劫持者接手注意力、
    /// 最后由完美审判官宣布"反正也来不及了"。只记终点，玩家学到的是"我又失败了"；
    /// 记下整条链，玩家才看得见**第一个拐点**——那一步的干预成本通常只有几秒钟。
    ///
    /// 【前台只给三个数，不给控制百分比】
    /// PRD 明确写"不显示复杂控制百分比"。所以对外只暴露：第一个拐点、最大劫持点、
    /// 最早可干预点，其余留在数据里供复盘。
    /// </summary>
    public static class ControlChainRecorder
    {
        const string SaveKey = "adversity_control_chain_v1";
        const int ChainCap = 60;

        static ControlChainArchive _archive;
        static ControlChainEvent _open;
        static float _openedAt;

        public static ControlChainEvent Current => _open;
        public static bool IsRecording => _open != null;

        public static ControlChainArchive Archive
        {
            get
            {
                if (_archive == null) LoadArchive();
                return _archive;
            }
        }

        static void LoadArchive()
        {
            _archive = null;
            string raw = PlayerPrefs.GetString(SaveKey, "");
            if (!string.IsNullOrEmpty(raw))
            {
                try { _archive = JsonUtility.FromJson<ControlChainArchive>(raw); }
                catch (Exception) { _archive = null; }
            }
            if (_archive == null) _archive = new ControlChainArchive();
        }

        static void SaveArchive()
        {
            if (_archive == null) return;
            while (_archive.chains.Count > ChainCap) _archive.chains.RemoveAt(0);
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(_archive));
            PlayerPrefs.Save();
        }

        /// <summary>目标动作本该开始了：从这一刻起记录谁在夺权。</summary>
        public static void Begin(string goalId, string goalAction)
        {
            _open = new ControlChainEvent
            {
                eventId = "CC_" + DateTime.UtcNow.Ticks,
                goalId = goalId,
                goalAction = goalAction,
                triggerTime = DateTime.UtcNow.ToString("o"),
            };
            _openedAt = Time.unscaledTime;
        }

        /// <summary>记一步。controlShift 越大表示这一步交出去的方向权越多。</summary>
        public static void Log(int bossId, string trigger, string automaticThought,
            string automaticBehavior, float controlShift, string counterAction = "")
        {
            if (_open == null) return;
            _open.events.Add(new ControlChainNode
            {
                timestamp = DateTime.UtcNow.ToString("o"),
                bossId = bossId,
                trigger = trigger,
                automaticThought = automaticThought,
                automaticBehavior = automaticBehavior,
                controlShift = Mathf.Clamp01(controlShift),
                counterAction = counterAction,
                secondsFromStart = Time.unscaledTime - _openedAt,
            });
        }

        /// <summary>把当前的心理量补到最后一个节点上（由 HUD / StressStateMachine 调用）。</summary>
        public static void Annotate(float stress, float focus, float goalVisibility, float actionPower)
        {
            if (_open == null || _open.events.Count == 0) return;
            var n = _open.events[_open.events.Count - 1];
            n.stress = stress;
            n.focus = focus;
            n.goalVisibility = goalVisibility;
            n.actionPower = actionPower;
        }

        /// <summary>收线。finalOutcome 用 Completed / Partial / NotStarted / Withdrawn。</summary>
        public static ControlChainEvent End(string finalOutcome)
        {
            if (_open == null) return null;
            _open.finalOutcome = finalOutcome;
            Archive.chains.Add(_open);
            SaveArchive();
            var done = _open;
            _open = null;
            return done;
        }

        // ==================== 前台三个数 ====================

        /// <summary>第一个拐点：方向权第一次明显转移的那一步。</summary>
        public static ControlChainNode FirstTurningPoint(ControlChainEvent chain)
        {
            if (chain == null) return null;
            for (int i = 0; i < chain.events.Count; i++)
                if (chain.events[i].controlShift >= 0.25f) return chain.events[i];
            return chain.events.Count > 0 ? chain.events[0] : null;
        }

        /// <summary>最大劫持点：交出方向权最多的那一步。</summary>
        public static ControlChainNode MaxHijack(ControlChainEvent chain)
        {
            if (chain == null) return null;
            ControlChainNode best = null;
            for (int i = 0; i < chain.events.Count; i++)
                if (best == null || chain.events[i].controlShift > best.controlShift)
                    best = chain.events[i];
            return best;
        }

        /// <summary>
        /// 最早可干预点：第一个拐点**之前**的那一步——惯性还没形成，代价最低。
        /// 链只有一步时就是那一步本身：再早就没有可观察的东西了。
        /// </summary>
        public static ControlChainNode EarliestInterventionWindow(ControlChainEvent chain)
        {
            if (chain == null || chain.events.Count == 0) return null;
            var first = FirstTurningPoint(chain);
            int idx = chain.events.IndexOf(first);
            return idx > 0 ? chain.events[idx - 1] : chain.events[0];
        }

        /// <summary>
        /// Early Intervention 的提示词：必须短到能在一次呼吸里执行。
        /// PRD 原话是"穿鞋""提交""离开邮箱"，而不是长篇教育。
        /// </summary>
        public static string InterventionHint(ControlChainEvent chain)
        {
            var node = EarliestInterventionWindow(chain);
            if (node == null) return "";
            var boss = InternalChapterCatalog.Boss(node.bossId);
            if (boss == null) return string.IsNullOrEmpty(node.trigger) ? "" : node.trigger;
            return boss.executionGate;
        }

        // ==================== Keystone ====================

        /// <summary>
        /// Keystone Score（第 7.3 节）：多个 Boss 同时出现时，先解决哪一个。
        /// 五项相乘而不是相加——任何一项为 0（例如证据置信度为 0）都应当直接出局。
        /// </summary>
        public static float KeystoneScore(float causalCentrality, float goalImpact,
            float interventionEase, float transferBenefit, float evidenceConfidence)
        {
            return Mathf.Clamp01(causalCentrality) * Mathf.Clamp01(goalImpact)
                 * Mathf.Clamp01(interventionEase) * Mathf.Clamp01(transferBenefit)
                 * Mathf.Clamp01(evidenceConfidence);
        }

        /// <summary>
        /// 从历史控制链里推出 Keystone Boss：出现得最早、夺权最多、覆盖目标最广的那一个。
        /// 返回 bossId；没有足够证据时返回 0（不要在没有证据时同时开多套训练）。
        /// </summary>
        public static int SuggestKeystoneBoss()
        {
            var chains = Archive.chains;
            if (chains.Count == 0) return 0;

            var centrality = new Dictionary<int, float>();
            var appearances = new Dictionary<int, int>();

            for (int i = 0; i < chains.Count; i++)
            {
                var first = FirstTurningPoint(chains[i]);
                for (int j = 0; j < chains[i].events.Count; j++)
                {
                    var n = chains[i].events[j];
                    if (n.bossId == 0) continue;
                    float w = n.controlShift + (first != null && first.bossId == n.bossId ? 0.5f : 0f);
                    float cur;
                    centrality[n.bossId] = centrality.TryGetValue(n.bossId, out cur) ? cur + w : w;
                    int c;
                    appearances[n.bossId] = appearances.TryGetValue(n.bossId, out c) ? c + 1 : 1;
                }
            }

            int bestBoss = 0;
            float bestScore = 0f;
            foreach (var kv in centrality)
            {
                int shows;
                appearances.TryGetValue(kv.Key, out shows);
                // 只出现过一次的不作数：那是偶发，不是枢纽。
                if (shows < 2) continue;
                float score = KeystoneScore(
                    Mathf.Clamp01(kv.Value / 4f),
                    Mathf.Clamp01(shows / (float)chains.Count),
                    0.7f, 0.7f,
                    Mathf.Clamp01(shows / 3f));
                if (score <= bestScore) continue;
                bestScore = score;
                bestBoss = kv.Key;
            }
            return bestBoss;
        }

        public static void Clear()
        {
            _archive = new ControlChainArchive();
            _open = null;
            PlayerPrefs.DeleteKey(SaveKey);
            PlayerPrefs.Save();
        }
    }
}
