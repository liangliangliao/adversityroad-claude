using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Combat;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 暂停休养生息答题面板（方案 V3.0 第四十五节）：
    /// 战斗中任一正向心理能量低于 40% 或任一负向能量高于 70% 时，暂停战斗（timeScale=0，
    /// 位置/镜头/敌人状态原样冻结）进入答题——从当前章节 60 题中加权抽 5 题；
    /// 答对回能量（未满正能量 +20、高于 0 的负能量 −20），答错不扣、只讲依据与偏差；
    /// 五题结束返回原战斗，并给 2 秒免受心理伤害保护。
    /// 另提供练习模式（HUD「答题」按钮随时进入），规则相同。
    /// </summary>
    public class QuizPanel : MonoBehaviour
    {
        public static QuizPanel Instance { get; private set; }

        const float AutoTriggerCooldown = 45f;  // 两次自动触发的最小间隔（非缩放秒）

        GameObject _panel;
        Text _titleText, _reasonText, _progressText, _metaText, _questionText, _feedbackText;
        readonly Button[] _optionBtns = new Button[3];
        readonly Text[] _optionLabels = new Text[3];
        readonly Image[] _optionImages = new Image[3];
        Button _continueBtn;
        Text _continueLabel;

        static readonly Color OptionIdle = new Color(0.2f, 0.22f, 0.3f, 0.96f);
        static readonly Color OptionCorrect = new Color(0.2f, 0.48f, 0.28f, 0.96f);
        static readonly Color OptionWrong = new Color(0.5f, 0.22f, 0.2f, 0.96f);

        List<QuizQuestion> _session;
        int _index;
        int _correctCount;
        bool _answered;       // 当前题已作答，等待「下一题」
        bool _autoMode;       // true=战斗触发（结束给护体）；false=练习模式
        float _nextAutoAllowed;
        float _savedTimeScale = 1f;

        Player.PlayerController _player;
        CombatStateMachine _combat;

        public bool Active => _panel != null && _panel.activeSelf;

        public static QuizPanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<QuizPanel>();
            comp.Build(canvas);
            return comp;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "QuizPanel", Vector2.zero,
                new Color(0.03f, 0.04f, 0.08f, 0.93f));
            var prt = _panel.GetComponent<RectTransform>();
            prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
            prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;

            _titleText = UiUtil.MakeText(_panel.transform, "Title", "暂停 · 休养生息", 40,
                TextAnchor.MiddleCenter, new Color(0.55f, 0.85f, 0.7f));
            UiUtil.SetRect(_titleText, new Vector2(0.5f, 1f), new Vector2(0, -60), new Vector2(1400, 54));
            _titleText.fontStyle = FontStyle.Bold;

            _reasonText = UiUtil.MakeText(_panel.transform, "Reason", "", 24,
                TextAnchor.MiddleCenter, new Color(0.8f, 0.8f, 0.85f));
            UiUtil.SetRect(_reasonText, new Vector2(0.5f, 1f), new Vector2(0, -108), new Vector2(1500, 34));

            _progressText = UiUtil.MakeText(_panel.transform, "Progress", "", 24,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.55f));
            UiUtil.SetRect(_progressText, new Vector2(0.5f, 1f), new Vector2(0, -146), new Vector2(1400, 32));

            _metaText = UiUtil.MakeText(_panel.transform, "Meta", "", 21,
                TextAnchor.MiddleCenter, new Color(0.6f, 0.75f, 0.9f));
            UiUtil.SetRect(_metaText, new Vector2(0.5f, 1f), new Vector2(0, -180), new Vector2(1500, 30));

            _questionText = UiUtil.MakeText(_panel.transform, "Question", "", 28,
                TextAnchor.UpperCenter, Color.white);
            UiUtil.SetRect(_questionText, new Vector2(0.5f, 1f), new Vector2(0, -262), new Vector2(1440, 140));

            // 三枚纵排选项按钮（A/B/C），左对齐长文本
            for (int i = 0; i < 3; i++)
            {
                int idx = i; // 闭包捕获
                var btn = UiUtil.MakeButton(_panel.transform, "", new Vector2(0.5f, 1f),
                    new Vector2(0, -400 - i * 116), new Vector2(1440, 106), OptionIdle,
                    () => OnChoice(idx), 24);
                _optionBtns[i] = btn;
                _optionImages[i] = btn.GetComponent<Image>();
                var label = btn.GetComponentInChildren<Text>();
                label.alignment = TextAnchor.MiddleLeft;
                var lrt = label.GetComponent<RectTransform>();
                lrt.offsetMin = new Vector2(28, 6); lrt.offsetMax = new Vector2(-28, -6);
                _optionLabels[i] = label;
            }

            _feedbackText = UiUtil.MakeText(_panel.transform, "Feedback", "", 22,
                TextAnchor.UpperCenter, new Color(0.95f, 0.92f, 0.8f));
            UiUtil.SetRect(_feedbackText, new Vector2(0.5f, 1f), new Vector2(0, -810), new Vector2(1460, 210));

            _continueBtn = UiUtil.MakeButton(_panel.transform, "下一题", new Vector2(0.5f, 0f),
                new Vector2(0, 64), new Vector2(400, 84), new Color(0.85f, 0.55f, 0.2f, 0.95f),
                OnContinue, 30);
            _continueLabel = _continueBtn.GetComponentInChildren<Text>();

            // 安全出口：任何时候都可以离开答题（安全优先——不把玩家锁在面板里）
            UiUtil.MakeButton(_panel.transform, "跳过休整", new Vector2(1f, 1f),
                new Vector2(-110, -46), new Vector2(180, 60),
                new Color(0.35f, 0.35f, 0.4f, 0.8f), () => Close(false), 22);

            _panel.SetActive(false);
        }

        // ===================== 触发监测 =====================

        void Update()
        {
            if (Active || _panel == null) return;
            if (Time.timeScale <= 0f) return;                 // 其它模态面板（剧情/复盘）打开中
            if (Time.unscaledTime < _nextAutoAllowed) return;

            if (_player == null) _player = FindObjectOfType<Player.PlayerController>();
            if (_player == null) return;
            if (_combat == null) _combat = _player.GetComponent<CombatStateMachine>();

            var stats = _player.Stats;
            if (stats == null || stats.IsDead) return;
            if (_combat == null || !_combat.InCombat) return; // 「暂停战斗」——只在临战中触发
            var vd = VerbalDefenseController.Instance;
            if (vd != null && vd.IsActive) return;            // 言语攻防进行中不抢屏
            if (!QuizSystem.NeedsRecovery(stats)) return;

            OpenAuto(stats);
        }

        void OpenAuto(PlayerStats stats)
        {
            _autoMode = true;
            string reason = QuizSystem.ImbalanceLabel(stats);
            BeginSession(QuizSystem.ImbalancedEnergyTags(stats),
                string.IsNullOrEmpty(reason) ? "" : "触发：" + reason);
        }

        /// <summary>练习模式入口（HUD「答题」按钮）：不需要失衡条件，规则相同。</summary>
        public void OpenPractice()
        {
            if (Active) return;
            _autoMode = false;
            if (_player == null) _player = FindObjectOfType<Player.PlayerController>();
            var stats = _player != null ? _player.Stats : null;
            BeginSession(stats != null ? QuizSystem.ImbalancedEnergyTags(stats) : null,
                "练习模式 · 常识提醒与恢复训练");
        }

        void BeginSession(List<string> imbalanceTags, string reason)
        {
            string chapterId = QuizSystem.CurrentChapterId();
            _session = QuizSystem.DrawSession(chapterId, imbalanceTags);
            if (_session.Count == 0)
            {
                GameEvents.RaiseSubtitle("题库未加载——无法进入休养生息答题。");
                _nextAutoAllowed = Time.unscaledTime + AutoTriggerCooldown;
                return;
            }
            _index = 0;
            _correctCount = 0;

            var info = QuizSystem.ChapterInfo(chapterId);
            _reasonText.text = reason +
                (info != null ? "　｜　" + info.name + "——" + info.theme : "");

            _savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;   // 冻结战斗：位置、镜头、敌人状态原样保留
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            GameAudio.Play(GameAudio.Sfx.Cast, 0.5f);
            ShowQuestion();
        }

        // ===================== 答题流程 =====================

        void ShowQuestion()
        {
            var q = _session[_index];
            _answered = false;

            string chapterId = QuizSystem.CurrentChapterId();
            _progressText.text = "第 " + (_index + 1) + "/" + _session.Count + " 题　｜　本章已掌握 "
                + QuizSystem.ChapterMastered(chapterId) + "/"
                + QuizSystem.ChapterQuestions(chapterId).Count + " 题";
            _metaText.text = "场景：" + q.sceneTag + "　｜　概念：" + q.conceptTag +
                "　｜　形态：" + q.type + "　｜　难度：" + new string('★', Mathf.Clamp(q.difficulty, 1, 3));
            _questionText.text = q.question;

            for (int i = 0; i < 3; i++)
            {
                _optionLabels[i].text = (char)('A' + i) + ". " +
                    (q.options != null && i < q.options.Length ? q.options[i] : "");
                _optionImages[i].color = OptionIdle;
                _optionBtns[i].interactable = true;
            }
            _feedbackText.text = "";
            _continueBtn.gameObject.SetActive(false);
        }

        void OnChoice(int picked)
        {
            if (_answered || _session == null) return;
            _answered = true;

            var q = _session[_index];
            bool correct = picked == q.correctIndex;
            QuizSystem.RecordAnswer(q.questionId, correct);

            for (int i = 0; i < 3; i++)
            {
                _optionBtns[i].interactable = false;
                if (i == q.correctIndex) _optionImages[i].color = OptionCorrect;
                else if (i == picked) _optionImages[i].color = OptionWrong;
            }

            string sources = q.sourceTags != null && q.sourceTags.Length > 0
                ? "\n来源：" + string.Join(", ", q.sourceTags) : "";
            if (correct)
            {
                _correctCount++;
                var stats = _player != null ? _player.Stats : null;
                QuizSystem.ApplyCorrectReward(stats);
                _feedbackText.text = "回答正确——能量回补：未满正能量 +20、负能量 −20。\n\n依据："
                    + q.rationale + sources;
                GameAudio.Play(GameAudio.Sfx.Parry, 0.6f);
            }
            else
            {
                // 答错不扣能量、不播放羞辱性音效——只讲依据与两个干扰项的偏差
                _feedbackText.text = "这一项在当前情境下不成立。正确选项是 "
                    + (char)('A' + q.correctIndex) + "。\n\n依据：" + q.rationale + sources;
            }

            bool last = _index >= _session.Count - 1;
            _continueLabel.text = last ? (_autoMode ? "返回战斗" : "完成练习") : "下一题";
            _continueBtn.gameObject.SetActive(true);
        }

        void OnContinue()
        {
            if (!_answered) return;
            if (_index < _session.Count - 1)
            {
                _index++;
                ShowQuestion();
            }
            else Close(true);
        }

        void Close(bool completed)
        {
            _panel.SetActive(false);
            Time.timeScale = _savedTimeScale;
            _nextAutoAllowed = Time.unscaledTime + AutoTriggerCooldown;
            if (_session != null && _index < _session.Count)
                GameEvents.RaiseSubtitle(completed
                    ? "休养生息结束：答对 " + _correctCount + "/" + _session.Count + " 题。" +
                      (_autoMode ? "返回战斗——" + (int)QuizSystem.PostQuizShieldSeconds + " 秒内心理攻击无效。" : "")
                    : "暂离休整——错题会在之后的关卡里优先出现。");
            if (completed && _autoMode)
                QuizSystem.GrantMentalShield(QuizSystem.PostQuizShieldSeconds);
            _session = null;
        }
    }
}
