using System;
using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.Player;

namespace AdversityRoad.Core
{
    [Serializable]
    public class QuizQuestion
    {
        public string questionId;
        public string chapterId;     // CH1—CH7
        public string sceneTag;      // 出题场景（两元赌桌/拖延沼泽…）
        public string conceptTag;    // 训练概念（事实先行/五分钟启动…）
        public string[] energyTags;  // 关联能量（Will/Focus/SelfWorth/Boundary/ActionPower/HP/Stamina/Rumination/RelationshipDrain…）
        public int difficulty;       // 1—3
        public string type;          // 原则理解/情境应用/日损辨识/迁移结果/恢复提示
        public string question;
        public string[] options;     // 恰好 3 项，仅 1 项成立
        public int correctIndex;
        public string rationale;     // 依据：正确项理由 + 两个干扰项的可命名偏差
        public string[] sourceTags;  // 来源矩阵标签（HPP-Lxx / DDJ-xx / CXL-xxx）
        public string sourceType;    // CROSS_SOURCE_SYNTHESIS 等
    }

    [Serializable]
    public class QuizChapterInfo
    {
        public string chapterId;
        public string name;
        public string theme;
    }

    [Serializable]
    class QuizBankData
    {
        public string version;
        public QuizChapterInfo[] chapters;
        public QuizQuestion[] questions;
    }

    [Serializable]
    class QuizProgressData
    {
        // 与 FailureLog 相同的平行列表持久化（JsonUtility 不支持字典）
        public List<string> ids = new List<string>();
        public List<int> correctCounts = new List<int>();
        public List<int> wrongCounts = new List<int>();
        public int totalAnswered;
        public int totalCorrect;
    }

    /// <summary>
    /// 暂停休养生息答题系统（方案 V3.0 第四十五—四十七节）的数据与规则层：
    /// · 题库：420 固定题（7 章 × 12 概念 × 5 形态），来源标签可追溯（哈佛积极心理学/道德经/传习录）；
    /// · 触发：任一正向心理能量低于 40%，或任一负向能量高于 70%，暂停战斗进入答题；
    /// · 抽题：从当前章节 60 题中加权不放回抽 5 题——优先未答题、曾答错题、当前失衡能量对应题；
    /// · 结算：每答对 1 题，所有未满正能量 +20、所有高于 0 的负能量 −20（各自 Clamp 0—100）；
    ///   答错不扣能量，只显示依据与干扰项偏差，不播放羞辱性音效；
    /// · 收尾：五题结束返回原战斗状态，并给予 2 秒免受心理伤害保护。
    /// 答题记录本地持久化：错题与未答题在后续关卡中被优先抽取。
    /// </summary>
    public static class QuizSystem
    {
        public const float PositiveLowRatio = 0.40f;   // 正向能量触发阈值
        public const float NegativeHighRatio = 0.70f;  // 负向能量触发阈值
        public const float RestorePerCorrect = 20f;    // 每答对一题的能量结算量
        public const int QuestionsPerSession = 5;      // 每次休养生息的题数
        public const float PostQuizShieldSeconds = 2f; // 答题结束后的心理免伤窗口

        const string ProgressKey = "adversity_quiz_progress";
        const string BankResourcePath = "Quiz/quiz_bank";

        static QuizBankData _bank;
        static QuizProgressData _progress;
        static Dictionary<string, List<QuizQuestion>> _byChapter;

        static float _shieldUntil = -999f;

        /// <summary>答题结束后的心理免伤保护是否生效（PlayerStats.TakeMentalDamage 查询）。</summary>
        public static bool IsMentalShielded => Time.unscaledTime < _shieldUntil;

        public static void GrantMentalShield(float seconds) =>
            _shieldUntil = Time.unscaledTime + seconds;

        // ===================== 题库加载 =====================

        static QuizBankData Bank()
        {
            if (_bank != null) return _bank;
            var text = Resources.Load<TextAsset>(BankResourcePath);
            if (text == null)
            {
                Debug.LogError("[QuizSystem] 题库资源缺失：Resources/" + BankResourcePath);
                _bank = new QuizBankData { chapters = new QuizChapterInfo[0], questions = new QuizQuestion[0] };
                return _bank;
            }
            _bank = JsonUtility.FromJson<QuizBankData>(text.text);
            _byChapter = new Dictionary<string, List<QuizQuestion>>();
            foreach (var q in _bank.questions)
            {
                if (!_byChapter.TryGetValue(q.chapterId, out var list))
                    _byChapter[q.chapterId] = list = new List<QuizQuestion>();
                list.Add(q);
            }
            return _bank;
        }

        public static QuizChapterInfo ChapterInfo(string chapterId)
        {
            foreach (var c in Bank().chapters)
                if (c.chapterId == chapterId) return c;
            return null;
        }

        public static List<QuizQuestion> ChapterQuestions(string chapterId)
        {
            Bank();
            return _byChapter != null && _byChapter.TryGetValue(chapterId, out var list)
                ? list : new List<QuizQuestion>();
        }

        /// <summary>
        /// 当前主线所在的题库章节：大章 1—7 对应 CH1—CH7；
        /// 序章（自我怀疑/明日幻影）主题最接近拖延与目标线，映射到 CH3；主线完结后停在 CH7。
        /// </summary>
        public static string CurrentChapterId()
        {
            var sm = StoryManager.Instance;
            int act = sm != null && sm.Current != null ? sm.Current.actIndex : 7;
            if (act <= 0) return "CH3";
            return "CH" + Mathf.Clamp(act, 1, 7);
        }

        // ===================== 触发判定 =====================

        /// <summary>
        /// 是否达到休养生息触发条件：任一正向心理能量低于 40%，或任一负向能量高于 70%。
        /// 体力天然随翻滚/攻击频繁跌破 40%，生命另有倒下流程——两者参与结算与抽题加权，
        /// 但不作为触发源，避免答题面板在正常战斗节奏中反复打断。
        /// </summary>
        public static bool NeedsRecovery(PlayerStats s)
        {
            if (s == null || s.IsDead) return false;
            return Ratio(s.will, s.maxWill) < PositiveLowRatio
                || Ratio(s.focus, s.maxFocus) < PositiveLowRatio
                || Ratio(s.selfWorth, s.maxSelfWorth) < PositiveLowRatio
                || Ratio(s.boundary, s.maxBoundary) < PositiveLowRatio
                || Ratio(s.actionPower, s.maxActionPower) < PositiveLowRatio
                || Ratio(s.rumination, s.maxRumination) > NegativeHighRatio
                || Ratio(s.relationshipDrain, s.maxRelationshipDrain) > NegativeHighRatio;
        }

        /// <summary>当前失衡能量对应的题目 energyTags（供抽题加权与触发原因展示）。</summary>
        public static List<string> ImbalancedEnergyTags(PlayerStats s)
        {
            var tags = new List<string>();
            if (s == null) return tags;
            if (Ratio(s.will, s.maxWill) < PositiveLowRatio)
            { tags.Add("Will"); tags.Add("Fear"); tags.Add("Helplessness"); }
            if (Ratio(s.focus, s.maxFocus) < PositiveLowRatio) tags.Add("Focus");
            if (Ratio(s.selfWorth, s.maxSelfWorth) < PositiveLowRatio)
            { tags.Add("SelfWorth"); tags.Add("Shame"); }
            if (Ratio(s.boundary, s.maxBoundary) < PositiveLowRatio)
            { tags.Add("Boundary"); tags.Add("FairnessPain"); }
            if (Ratio(s.actionPower, s.maxActionPower) < PositiveLowRatio) tags.Add("ActionPower");
            if (Ratio(s.hp, s.maxHp) < PositiveLowRatio) tags.Add("HP");
            if (Ratio(s.stamina, s.maxStamina) < PositiveLowRatio) tags.Add("Stamina");
            if (Ratio(s.rumination, s.maxRumination) > NegativeHighRatio) tags.Add("Rumination");
            if (Ratio(s.relationshipDrain, s.maxRelationshipDrain) > NegativeHighRatio)
                tags.Add("RelationshipDrain");
            return tags;
        }

        /// <summary>触发原因的人话描述（面板标题下的一行说明）。</summary>
        public static string ImbalanceLabel(PlayerStats s)
        {
            if (s == null) return "";
            var parts = new List<string>();
            if (Ratio(s.will, s.maxWill) < PositiveLowRatio) parts.Add("意志过低");
            if (Ratio(s.focus, s.maxFocus) < PositiveLowRatio) parts.Add("专注过低");
            if (Ratio(s.selfWorth, s.maxSelfWorth) < PositiveLowRatio) parts.Add("自尊过低");
            if (Ratio(s.boundary, s.maxBoundary) < PositiveLowRatio) parts.Add("边界过低");
            if (Ratio(s.actionPower, s.maxActionPower) < PositiveLowRatio) parts.Add("行动力过低");
            if (Ratio(s.rumination, s.maxRumination) > NegativeHighRatio) parts.Add("反刍过高");
            if (Ratio(s.relationshipDrain, s.maxRelationshipDrain) > NegativeHighRatio) parts.Add("关系消耗过高");
            return string.Join("、", parts);
        }

        static float Ratio(float v, float max) => max > 0 ? v / max : 0f;

        // ===================== 抽题（加权不放回） =====================

        /// <summary>
        /// 从指定章节题池中加权不放回抽取一组题：固定 60 题 + AI 题
        /// （已入库题永远参与；低风险临时题仅在 AI 自动命题开启时参与）。
        /// 基础权重 1；未答过 +3；曾答错 +4；energyTags 命中当前失衡能量 +3。
        /// </summary>
        public static List<QuizQuestion> DrawSession(string chapterId, List<string> imbalanceTags,
            int count = QuestionsPerSession)
        {
            var pool = new List<QuizQuestion>(ChapterQuestions(chapterId));
            pool.AddRange(QuizAiBank.Usable(chapterId, QuizAiService.FeatureEnabled));
            var picked = new List<QuizQuestion>();
            if (pool.Count == 0) return picked;
            count = Mathf.Min(count, pool.Count);

            var weights = new List<float>(pool.Count);
            foreach (var q in pool) weights.Add(Weight(q, imbalanceTags));

            for (int n = 0; n < count; n++)
            {
                float total = 0f;
                for (int i = 0; i < weights.Count; i++) total += weights[i];
                float r = UnityEngine.Random.value * total;
                int chosen = weights.Count - 1;
                for (int i = 0; i < weights.Count; i++)
                {
                    r -= weights[i];
                    if (r <= 0f) { chosen = i; break; }
                }
                picked.Add(pool[chosen]);
                pool.RemoveAt(chosen);
                weights.RemoveAt(chosen);
            }
            return picked;
        }

        static float Weight(QuizQuestion q, List<string> imbalanceTags)
        {
            float w = 1f;
            int idx = ProgressIndex(q.questionId);
            bool answered = idx >= 0;
            if (!answered) w += 3f;                                  // 优先未答题
            if (answered && Progress().wrongCounts[idx] > 0) w += 4f; // 优先曾答错题
            if (imbalanceTags != null && q.energyTags != null)
                foreach (var t in q.energyTags)
                    if (imbalanceTags.Contains(t)) { w += 3f; break; } // 优先失衡能量对应题
            return w;
        }

        // ===================== 结算 =====================

        /// <summary>
        /// 答对结算：所有未满正能量 +20（生命/体力/意志/专注/自尊/边界/行动力），
        /// 所有高于 0 的负能量 −20（反刍/关系消耗）；各自 Clamp 到 0—100。
        /// </summary>
        public static void ApplyCorrectReward(PlayerStats s)
        {
            if (s == null) return;
            if (s.hp < s.maxHp)
            {
                s.hp = Mathf.Min(s.maxHp, s.hp + RestorePerCorrect);
                GameEvents.RaisePlayerHpChanged(s.hp, s.maxHp);
            }
            s.stamina = Mathf.Min(s.maxStamina, s.stamina + RestorePerCorrect);
            s.will = Mathf.Min(s.maxWill, s.will + RestorePerCorrect);
            s.focus = Mathf.Min(s.maxFocus, s.focus + RestorePerCorrect);
            s.selfWorth = Mathf.Min(s.maxSelfWorth, s.selfWorth + RestorePerCorrect);
            s.boundary = Mathf.Min(s.maxBoundary, s.boundary + RestorePerCorrect);
            s.actionPower = Mathf.Min(s.maxActionPower, s.actionPower + RestorePerCorrect);
            GameEvents.RaiseMentalStatChanged("will", s.will, s.maxWill);
            GameEvents.RaiseMentalStatChanged("focus", s.focus, s.maxFocus);
            GameEvents.RaiseMentalStatChanged("selfWorth", s.selfWorth, s.maxSelfWorth);
            GameEvents.RaiseMentalStatChanged("boundary", s.boundary, s.maxBoundary);
            GameEvents.RaiseMentalStatChanged("actionPower", s.actionPower, s.maxActionPower);
            if (s.rumination > 0) s.ReduceRumination(RestorePerCorrect);
            if (s.relationshipDrain > 0) s.ReduceRelationshipDrain(RestorePerCorrect);
        }

        // ===================== 答题记录（持久化） =====================

        static QuizProgressData Progress()
        {
            if (_progress == null)
            {
                string json = PlayerPrefs.GetString(ProgressKey, "");
                _progress = string.IsNullOrEmpty(json) ? new QuizProgressData()
                    : JsonUtility.FromJson<QuizProgressData>(json) ?? new QuizProgressData();
            }
            return _progress;
        }

        static void SaveProgress()
        {
            PlayerPrefs.SetString(ProgressKey, JsonUtility.ToJson(Progress()));
            PlayerPrefs.Save();
        }

        static int ProgressIndex(string questionId)
        {
            var p = Progress();
            for (int i = 0; i < p.ids.Count; i++)
                if (p.ids[i] == questionId) return i;
            return -1;
        }

        public static void RecordAnswer(string questionId, bool correct)
        {
            var p = Progress();
            int idx = ProgressIndex(questionId);
            if (idx < 0)
            {
                p.ids.Add(questionId);
                p.correctCounts.Add(0);
                p.wrongCounts.Add(0);
                idx = p.ids.Count - 1;
            }
            if (correct) p.correctCounts[idx]++;
            else p.wrongCounts[idx]++;
            p.totalAnswered++;
            if (correct) p.totalCorrect++;
            SaveProgress();
        }

        /// <summary>玩家在本章答错过的概念（AI 自动命题的「动态题源」上下文用）。</summary>
        public static List<string> WrongConceptTags(string chapterId, int max)
        {
            var p = Progress();
            var list = new List<string>();
            foreach (var q in ChapterQuestions(chapterId))
            {
                int idx = ProgressIndex(q.questionId);
                if (idx >= 0 && p.wrongCounts[idx] > 0 && !list.Contains(q.conceptTag))
                {
                    list.Add(q.conceptTag);
                    if (list.Count >= max) break;
                }
            }
            return list;
        }

        /// <summary>章节掌握进度：已答对（至少一次）的题数。</summary>
        public static int ChapterMastered(string chapterId)
        {
            var p = Progress();
            int mastered = 0;
            foreach (var q in ChapterQuestions(chapterId))
            {
                int idx = ProgressIndex(q.questionId);
                if (idx >= 0 && p.correctCounts[idx] > 0) mastered++;
            }
            return mastered;
        }

        public static int TotalAnswered => Progress().totalAnswered;
        public static int TotalCorrect => Progress().totalCorrect;

        public static void DeleteAll()
        {
            PlayerPrefs.DeleteKey(ProgressKey);
            _progress = null;
        }
    }
}
