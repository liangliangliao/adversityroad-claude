using System;
using System.Collections.Generic;
using UnityEngine;

namespace AdversityRoad.InternalOS
{
    /// <summary>一次正在进行中的三选一：事件本体 + 本次实际呈现顺序。</summary>
    public class MentalAttackPresentation
    {
        public MentalAttackChoiceEvent source;
        /// <summary>本次显示顺序（已洗牌）。UI 只读这一份，绝不读 source.options 的原序。</summary>
        public List<MentalAttackOption> shown = new List<MentalAttackOption>();
        public float openedAt;

        public string OrderTag()
        {
            var parts = new string[shown.Count];
            for (int i = 0; i < shown.Count; i++) parts[i] = shown[i].canonicalSlot;
            return string.Join(",", parts);
        }
    }

    /// <summary>
    /// 内部语言攻击 · 三选一反制窗口（V2.2 增补章）。
    ///
    /// 【这套系统最容易被做错的三件事，这里各用一条规则挡住】
    /// ① 做成心理测验：答对就通关。——所以 <see cref="Choose"/> 只开 Counter Window，
    ///    真正的记账发生在 <see cref="CompleteFollowUp"/>；没做 Follow-up 就没有 Behavior Evidence。
    /// ② 答案永远在同一个位置：best 固定 A 或固定最长。——所以每次呈现都洗牌，
    ///    并把实际顺序写进历史；洗牌结果可被复盘核对。
    /// ③ 不选 = 答错：弹框超时就惩罚玩家。——PRD 明确写"不视为错误答案"，
    ///    所以 <see cref="Dismiss"/> 只让攻击的原效果继续生效，不额外扣任何东西、不出羞辱音效。
    /// </summary>
    public static class MentalAttackSystem
    {
        const string HistoryKey = "adversity_mental_attack_history_v1";
        const int HistoryCap = 400;

        /// <summary>Boss 非高速阶段的慢动作档（PRD：0.15-0.25）。</summary>
        public const float BossSlowMotionScale = 0.2f;
        /// <summary>Boss 弹框的存在时长（PRD：1.5-3 秒）。</summary>
        public const float BossPopupSeconds = 3f;
        /// <summary>高速战斗的 HUD 窗口：不完全暂停，只给一小段慢镜。</summary>
        public const float HighSpeedSlowMotionScale = 0.6f;
        /// <summary>Follow-up 没做就不算数的窗口（秒）。超时不惩罚，只是不记成长证据。</summary>
        public const float FollowUpWindowSeconds = 60f;

        static MentalAttackHistoryData _history;
        static MentalAttackPresentation _current;
        static string _pendingFollowUp;
        static string _pendingFollowUpLevel;
        static string _pendingFollowUpEvent;
        static float _pendingFollowUpUntil;

        /// <summary>弹框已准备好，UI 应当显示。</summary>
        public static event Action<MentalAttackPresentation> OnPresent;
        /// <summary>玩家做出处置（或超时）。参数：事件、结果、被选中的项（超时为 null）。</summary>
        public static event Action<MentalAttackChoiceEvent, MentalChoiceOutcome, MentalAttackOption> OnResolved;
        /// <summary>需要玩家去做的那件事（答对之后才出现）。</summary>
        public static event Action<string> OnFollowUpRequired;
        /// <summary>Follow-up 完成（true）或超时作废（false）。</summary>
        public static event Action<string, bool> OnFollowUpSettled;

        public static MentalAttackPresentation Current => _current;
        public static bool IsOpen => _current != null;
        public static bool IsFollowUpPending => !string.IsNullOrEmpty(_pendingFollowUp);
        public static string PendingFollowUp => _pendingFollowUp;
        /// <summary>这件事属于哪一关——复盘时把"答对了没做"挂回具体关卡。</summary>
        public static string PendingFollowUpLevel => _pendingFollowUpLevel;

        // ==================== 触发 ====================

        /// <summary>
        /// 在某一关触发一次内部语言攻击。
        ///
        /// mastery 是玩家在这条障碍线上的当前等级（M0-M5）：越高越少弹框——
        /// 第 12 条要求"Mastery 升高后弹框应压缩，避免用户永久依赖系统答题"。
        /// M3 起按等级递减触发概率，M5 默认不再弹（除非 force）。
        /// </summary>
        public static bool TryFire(string levelId, int mastery = 0, bool force = false)
        {
            if (_current != null) return false;

            var events = MentalAttackCatalog.ForLevel(levelId);
            if (events.Count == 0) return false;

            if (!force && SuppressedByMastery(mastery)) return false;

            var e = events[UnityEngine.Random.Range(0, events.Count)];
            return Present(e);
        }

        /// <summary>Mastery 压缩规则：M0-M2 全弹，M3 六成，M4 三成，M5 不弹。</summary>
        public static bool SuppressedByMastery(int mastery)
        {
            if (mastery <= 2) return false;
            if (mastery == 3) return UnityEngine.Random.value > 0.6f;
            if (mastery == 4) return UnityEngine.Random.value > 0.3f;
            return true;
        }

        /// <summary>直接呈现一条事件（Boss 阶段边界等确定性触发点用）。</summary>
        public static bool Present(MentalAttackChoiceEvent e)
        {
            if (e == null || _current != null) return false;

            var issues = MentalAttackValidator.Validate(e);
            if (MentalAttackValidator.HasError(issues))
            {
                // 坏数据不该弹给玩家：宁可这一次不弹，也不弹一个两个干扰项同家族的框。
                for (int i = 0; i < issues.Count; i++)
                    if (issues[i].error) Debug.LogError("[MentalAttack] " + issues[i]);
                return false;
            }

            _current = new MentalAttackPresentation { source = e, openedAt = Time.unscaledTime };
            _current.shown = Shuffle(e.options);
            OnPresent?.Invoke(_current);
            return true;
        }

        /// <summary>
        /// 洗牌（第 5 条：best 在 A/B/C 中的位置每次随机，禁止固定为 B 或最长选项）。
        /// Fisher-Yates；用 UnityEngine.Random 以便回放种子可控。
        /// </summary>
        public static List<MentalAttackOption> Shuffle(List<MentalAttackOption> src)
        {
            var list = new List<MentalAttackOption>(src);
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                var t = list[i]; list[i] = list[j]; list[j] = t;
            }
            return list;
        }

        // ==================== 处置 ====================

        /// <summary>玩家选了某一项。返回本次结果。</summary>
        public static MentalChoiceOutcome Choose(string optionId)
        {
            if (_current == null) return MentalChoiceOutcome.NoResponse;

            var e = _current.source;
            var picked = e.ById(optionId);
            var outcome = picked != null && picked.IsBest
                ? MentalChoiceOutcome.Best : MentalChoiceOutcome.Distractor;

            Record(e, _current, picked, outcome);

            if (outcome == MentalChoiceOutcome.Best)
            {
                // 选对只换来一次"重新获得选择权"的机会，不加 Courage、不判胜（第 3 条）。
                _pendingFollowUp = e.followUpAction;
                _pendingFollowUpLevel = e.levelId;
                _pendingFollowUpEvent = e.eventId;
                _pendingFollowUpUntil = Time.unscaledTime + FollowUpWindowSeconds;
            }

            _current = null;
            OnResolved?.Invoke(e, outcome, picked);
            if (outcome == MentalChoiceOutcome.Best) OnFollowUpRequired?.Invoke(_pendingFollowUp);
            return outcome;
        }

        /// <summary>
        /// 弹框淡出，玩家没有选。
        /// **这不是答错**：攻击的原效果继续生效，不额外扣分、不出失败音、不记错误。
        /// </summary>
        public static void Dismiss()
        {
            if (_current == null) return;
            var e = _current.source;
            Record(e, _current, null, MentalChoiceOutcome.NoResponse);
            _current = null;
            OnResolved?.Invoke(e, MentalChoiceOutcome.NoResponse, null);
        }

        /// <summary>玩家真的去做了那件事（提交、穿鞋、离开邮箱……）。这才是记成长证据的那一刻。</summary>
        public static bool CompleteFollowUp()
        {
            if (!IsFollowUpPending) return false;

            MarkLastEntry(_pendingFollowUpEvent, true);
            string action = _pendingFollowUp;
            LinkLaterOutcome(_pendingFollowUpLevel, true);
            ClearFollowUp();
            OnFollowUpSettled?.Invoke(action, true);
            return true;
        }

        /// <summary>
        /// 每帧推进：只负责让过期的 Follow-up 作废。
        /// 作废不惩罚——它只是没有产生证据，下次这条攻击还会再来。
        /// </summary>
        public static void Tick()
        {
            if (!IsFollowUpPending) return;
            if (Time.unscaledTime < _pendingFollowUpUntil) return;

            string action = _pendingFollowUp;
            ClearFollowUp();
            OnFollowUpSettled?.Invoke(action, false);
        }

        static void ClearFollowUp()
        {
            _pendingFollowUp = null;
            _pendingFollowUpLevel = null;
            _pendingFollowUpEvent = null;
            _pendingFollowUpUntil = 0f;
        }

        // ==================== 历史 ====================

        public static MentalAttackHistoryData History
        {
            get
            {
                if (_history == null) LoadHistory();
                return _history;
            }
        }

        static void LoadHistory()
        {
            _history = null;
            string raw = PlayerPrefs.GetString(HistoryKey, "");
            if (!string.IsNullOrEmpty(raw))
            {
                try { _history = JsonUtility.FromJson<MentalAttackHistoryData>(raw); }
                catch (Exception) { _history = null; }
            }
            if (_history == null) _history = new MentalAttackHistoryData();
        }

        static void SaveHistory()
        {
            if (_history == null) return;
            while (_history.entries.Count > HistoryCap) _history.entries.RemoveAt(0);
            PlayerPrefs.SetString(HistoryKey, JsonUtility.ToJson(_history));
            PlayerPrefs.Save();
        }

        static void Record(MentalAttackChoiceEvent e, MentalAttackPresentation p,
            MentalAttackOption picked, MentalChoiceOutcome outcome)
        {
            var h = History;
            int replay = 0;
            for (int i = 0; i < h.entries.Count; i++)
                if (h.entries[i].eventId == e.eventId) replay++;

            h.entries.Add(new MentalAttackHistoryEntry
            {
                levelId = e.levelId,
                eventId = e.eventId,
                shownAt = DateTime.UtcNow.ToString("o"),
                attackLineHash = (e.attackLine ?? "").GetHashCode(),
                optionOrder = p != null ? p.OrderTag() : "",
                selectedOptionRole = outcome.ToString(),
                selectedFamily = picked != null ? picked.distractorFamily : "",
                immediateStateChange = picked != null ? picked.immediateEffect : "",
                followUpCompleted = false,
                replayCount = replay,
            });
            SaveHistory();
        }

        static void MarkLastEntry(string eventId, bool followUpCompleted)
        {
            var h = History;
            for (int i = h.entries.Count - 1; i >= 0; i--)
            {
                if (h.entries[i].eventId != eventId) continue;
                h.entries[i].followUpCompleted = followUpCompleted;
                SaveHistory();
                return;
            }
        }

        /// <summary>把一次后续失败/成功挂到最近一次弹框上，供复盘拉出完整因果链。</summary>
        public static void LinkLaterOutcome(string levelId, bool success)
        {
            var h = History;
            for (int i = h.entries.Count - 1; i >= 0; i--)
            {
                if (h.entries[i].levelId != levelId) continue;
                if (success) h.entries[i].laterSuccessLinked = true;
                else h.entries[i].laterFailureLinked = true;
                SaveHistory();
                return;
            }
        }

        public static List<MentalAttackHistoryEntry> HistoryOf(string levelId)
        {
            var list = new List<MentalAttackHistoryEntry>();
            var h = History;
            for (int i = 0; i < h.entries.Count; i++)
                if (h.entries[i].levelId == levelId) list.Add(h.entries[i]);
            return list;
        }

        /// <summary>本地数据可整体删除（安全条款）。</summary>
        public static void ClearHistory()
        {
            _history = new MentalAttackHistoryData();
            PlayerPrefs.DeleteKey(HistoryKey);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// best 选中率。低于 0.3 说明这一关的干扰项可能过强或 best 表述不清；
        /// 高于 0.95 且 Follow-up 完成率很低，说明玩家在"会答不会做"——
        /// 这两种都不是靠调数值解决的，它们提示的是关卡表述本身要改。
        /// </summary>
        public static float BestRate(string levelId)
        {
            var list = HistoryOf(levelId);
            if (list.Count == 0) return 0f;
            int best = 0;
            for (int i = 0; i < list.Count; i++)
                if (list[i].selectedOptionRole == MentalChoiceOutcome.Best.ToString()) best++;
            return (float)best / list.Count;
        }

        /// <summary>答对之后真的去做的比例。这才是这套系统的核心指标。</summary>
        public static float FollowUpRate(string levelId)
        {
            var list = HistoryOf(levelId);
            int best = 0, done = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].selectedOptionRole != MentalChoiceOutcome.Best.ToString()) continue;
                best++;
                if (list[i].followUpCompleted) done++;
            }
            return best == 0 ? 0f : (float)done / best;
        }
    }
}
