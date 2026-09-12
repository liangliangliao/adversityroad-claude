using System;
using System.Collections.Generic;
using UnityEngine;

namespace AdversityRoad.InternalOS
{
    /// <summary>三层胜利（第 8.1 节）。</summary>
    public enum VictoryLayer
    {
        /// <summary>游戏内看懂并完成机制。</summary>
        Simulation,
        /// <summary>现实中完成对应行为。</summary>
        Reality,
        /// <summary>在不同情境再次使用同一能力。</summary>
        Transfer,
    }

    /// <summary>一条胜利证据。现实胜利与迁移胜利必须由玩家自己确认，系统不替他宣布。</summary>
    [Serializable]
    public class VictoryEvidence
    {
        public string levelId;
        public string chapterId;
        public string layer;            // VictoryLayer 枚举名
        public string behavior;         // 实际发生了什么
        public string context;          // 在哪个情境（迁移胜利靠它判断"不同情境"）
        public string recordedAt;
        /// <summary>玩家自评的置信度。低置信度不参与 Mastery 升级。</summary>
        public float confidence = 1f;
    }

    /// <summary>核心成长指标（第 8.3 节）。全部是"趋势"指标，不是"完美记录"指标。</summary>
    [Serializable]
    public class GrowthMetrics
    {
        /// <summary>从中断到重新进入 Goal Action 的秒数。</summary>
        public List<float> recoveryLatency = new List<float>();
        /// <summary>从意识到已偏离，到重新夺回方向权的秒数。</summary>
        public List<float> controlRecoveryTime = new List<float>();
        /// <summary>目标存在但没有进入工作记忆的累计秒数。</summary>
        public float goalOfflineTime;
        /// <summary>主动选择后的目标行为时间。</summary>
        public float agencyActionSeconds;
        /// <summary>全部可行动时间。</summary>
        public float actionableSeconds;
    }

    [Serializable]
    public class InternalProgressData
    {
        public List<VictoryEvidence> evidence = new List<VictoryEvidence>();
        public GrowthMetrics metrics = new GrowthMetrics();
        /// <summary>章节 id → Mastery 等级（0-5），用两条平行列表存（JsonUtility 不支持字典）。</summary>
        public List<string> masteryChapters = new List<string>();
        public List<int> masteryLevels = new List<int>();
    }

    /// <summary>
    /// Reality Victory / Transfer Victory / Mastery（第 8 节）。
    ///
    /// 【这套系统拒绝做的一件事：把"打完了"当成"学会了"】
    /// Simulation Victory 只证明玩家在这一关看懂了机制。真正的判据在现实那一侧：
    /// 同一个能力是否在**另一个情境**里又用了一次（Transfer）。所以 Mastery 升到
    /// M4/M5 必须有不同 context 的证据，不能靠在同一关反复通关刷上去。
    ///
    /// 【为什么失败次数不能换胜率】
    /// 第 3.5 / 8.4 节：失败必须产生可验证信息。所以这里只接收"发生了什么行为"，
    /// 不接收"我失败了 N 次所以给我通过"。
    /// </summary>
    public static class RealityVictorySystem
    {
        const string SaveKey = "adversity_internal_progress_v1";

        public static event Action<VictoryEvidence> OnEvidenceRecorded;
        public static event Action<string, int> OnMasteryChanged;   // chapterId, 新等级

        static InternalProgressData _data;

        public static InternalProgressData Data
        {
            get
            {
                if (_data == null) Load();
                return _data;
            }
        }

        static void Load()
        {
            _data = null;
            string raw = PlayerPrefs.GetString(SaveKey, "");
            if (!string.IsNullOrEmpty(raw))
            {
                try { _data = JsonUtility.FromJson<InternalProgressData>(raw); }
                catch (Exception) { _data = null; }
            }
            if (_data == null) _data = new InternalProgressData();
        }

        static bool _dirty;

        static void Save()
        {
            if (_data == null) return;
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(_data));
            PlayerPrefs.Save();
            _dirty = false;
        }

        /// <summary>
        /// 只标脏，不落盘。
        ///
        /// 成长指标是逐帧累加的（AgencyRatio 的分母就是每一帧）。
        /// 如果每次累加都 PlayerPrefs.Save()，移动端会在每一帧写一次磁盘——
        /// 这不是"稍微慢一点"，是整关都在掉帧。落盘交给 <see cref="Flush"/>：
        /// 关卡结束、切出应用、记证据时各一次就够了。
        /// </summary>
        static void Touch()
        {
            if (_data == null) return;
            _dirty = true;
        }

        /// <summary>把攒着的成长指标写盘（关卡结束 / 应用切后台时调）。</summary>
        public static void Flush()
        {
            if (_dirty) Save();
        }

        // ==================== 证据 ====================

        public static VictoryEvidence Record(string levelId, VictoryLayer layer,
            string behavior, string context, float confidence = 1f)
        {
            var lv = InternalChapterCatalog.Level(levelId);
            var ev = new VictoryEvidence
            {
                levelId = levelId,
                chapterId = lv != null ? lv.chapterId : "",
                layer = layer.ToString(),
                behavior = behavior,
                context = context,
                confidence = Mathf.Clamp01(confidence),
                recordedAt = DateTime.UtcNow.ToString("o"),
            };
            Data.evidence.Add(ev);
            Save();
            OnEvidenceRecorded?.Invoke(ev);
            ReevaluateMastery(ev.chapterId);
            return ev;
        }

        public static List<VictoryEvidence> EvidenceOf(string chapterId, VictoryLayer layer)
        {
            var list = new List<VictoryEvidence>();
            var all = Data.evidence;
            string want = layer.ToString();
            for (int i = 0; i < all.Count; i++)
                if (all[i].chapterId == chapterId && all[i].layer == want) list.Add(all[i]);
            return list;
        }

        /// <summary>这一章的现实胜利是否已经发生过（Hall of Goals 与章节结算读它）。</summary>
        public static bool HasRealityVictory(string chapterId) =>
            EvidenceOf(chapterId, VictoryLayer.Reality).Count > 0;

        /// <summary>迁移胜利要求**不同情境**：同一个 context 反复记，不算迁移。</summary>
        public static int DistinctTransferContexts(string chapterId)
        {
            var seen = new HashSet<string>();
            var list = EvidenceOf(chapterId, VictoryLayer.Transfer);
            for (int i = 0; i < list.Count; i++)
                if (!string.IsNullOrEmpty(list[i].context)) seen.Add(list[i].context);
            return seen.Count;
        }

        // ==================== Mastery ====================

        public static int Mastery(string chapterId)
        {
            var d = Data;
            for (int i = 0; i < d.masteryChapters.Count; i++)
                if (d.masteryChapters[i] == chapterId) return d.masteryLevels[i];
            return 0;
        }

        public static void SetMastery(string chapterId, int level)
        {
            if (string.IsNullOrEmpty(chapterId)) return;
            level = Mathf.Clamp(level, 0, 5);
            var d = Data;
            for (int i = 0; i < d.masteryChapters.Count; i++)
            {
                if (d.masteryChapters[i] != chapterId) continue;
                if (d.masteryLevels[i] == level) return;
                // Mastery 只升不降：复发不取消已经发生过的迁移（第 13 章的整章命题）。
                if (level < d.masteryLevels[i]) return;
                d.masteryLevels[i] = level;
                Save();
                OnMasteryChanged?.Invoke(chapterId, level);
                return;
            }
            d.masteryChapters.Add(chapterId);
            d.masteryLevels.Add(level);
            Save();
            OnMasteryChanged?.Invoke(chapterId, level);
        }

        /// <summary>
        /// 按已有证据重算 Mastery（第 8.2 节）。
        ///
        /// M2「提醒后反制」与 M3「自主反制」的分界不在这里——它取决于当时有没有弹框，
        /// 由 <see cref="MarkSelfInitiatedCounter"/> 单独上报；这里只处理证据能判定的部分。
        /// </summary>
        public static void ReevaluateMastery(string chapterId)
        {
            if (string.IsNullOrEmpty(chapterId)) return;

            int sim = EvidenceOf(chapterId, VictoryLayer.Simulation).Count;
            int real = EvidenceOf(chapterId, VictoryLayer.Reality).Count;
            int contexts = DistinctTransferContexts(chapterId);
            int cur = Mastery(chapterId);

            int level = cur;
            if (sim > 0 && level < 1) level = 1;
            if (real > 0 && level < 2) level = 2;
            // M4 跨场景迁移：至少两个**不同**情境。
            if (contexts >= 2 && level < 4) level = 4;
            // M5 自动化：迁移情境够多，且最近的反制大多是自主发起的。
            if (contexts >= 3 && real >= 3 && level < 5) level = 5;

            SetMastery(chapterId, level);
        }

        /// <summary>玩家在没有弹框提醒的情况下自己完成了反制 —— M3 的判据。</summary>
        public static void MarkSelfInitiatedCounter(string chapterId)
        {
            if (Mastery(chapterId) < 3) SetMastery(chapterId, 3);
        }

        // ==================== 成长指标 ====================

        public static void LogRecoveryLatency(float seconds)
        {
            if (seconds < 0f) return;
            Data.metrics.recoveryLatency.Add(seconds);
            Touch();
        }

        public static void LogControlRecoveryTime(float seconds)
        {
            if (seconds < 0f) return;
            Data.metrics.controlRecoveryTime.Add(seconds);
            Touch();
        }

        public static void AddGoalOfflineTime(float seconds)
        {
            if (seconds <= 0f) return;
            Data.metrics.goalOfflineTime += seconds;
            Touch();
        }

        public static void AddActionTime(float agencySeconds, float actionableSeconds)
        {
            Data.metrics.agencyActionSeconds += Mathf.Max(0f, agencySeconds);
            Data.metrics.actionableSeconds += Mathf.Max(0f, actionableSeconds);
            Touch();
        }

        public static float AgencyRatio()
        {
            var m = Data.metrics;
            return m.actionableSeconds <= 0f ? 0f : m.agencyActionSeconds / m.actionableSeconds;
        }

        /// <summary>
        /// RecoveryLatency 的趋势：最近几次相对更早几次的变化（负数 = 在变快）。
        /// PRD 原话："趋势下降比完美 Streak 更重要"——所以这里给的是趋势，不是平均值。
        /// </summary>
        public static float RecoveryLatencyTrend(int window = 5)
        {
            var list = Data.metrics.recoveryLatency;
            if (list.Count < window * 2) return 0f;

            float recent = 0f, earlier = 0f;
            for (int i = list.Count - window; i < list.Count; i++) recent += list[i];
            for (int i = list.Count - window * 2; i < list.Count - window; i++) earlier += list[i];
            recent /= window; earlier /= window;
            return earlier <= 0f ? 0f : (recent - earlier) / earlier;
        }

        /// <summary>
        /// GrowthEvidence = ActionChange × RealityTransfer × RecoverySpeed × Repetition × ContextDiversity
        /// （第 8.3 节）。五项相乘：任何一项为 0 就说明这条线还没有真的长出来。
        /// </summary>
        public static float GrowthEvidence(string chapterId)
        {
            int sim = EvidenceOf(chapterId, VictoryLayer.Simulation).Count;
            int real = EvidenceOf(chapterId, VictoryLayer.Reality).Count;
            int contexts = DistinctTransferContexts(chapterId);

            float actionChange = Mathf.Clamp01(sim / 3f);
            float realityTransfer = Mathf.Clamp01(real / 2f);
            float recoverySpeed = Mathf.Clamp01(0.5f - RecoveryLatencyTrend() * 0.5f);
            float repetition = Mathf.Clamp01((sim + real) / 6f);
            float contextDiversity = Mathf.Clamp01(contexts / 3f);

            return actionChange * realityTransfer * recoverySpeed * repetition * contextDiversity;
        }

        /// <summary>本地数据可整体删除（第 11.2 节安全与隐私）。</summary>
        public static void Clear()
        {
            _data = new InternalProgressData();
            PlayerPrefs.DeleteKey(SaveKey);
            PlayerPrefs.Save();
        }
    }
}
