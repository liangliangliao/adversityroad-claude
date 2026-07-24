using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;

namespace AdversityRoad.UI
{
    /// <summary>
    /// AI 命题审核面板（方案 V3.0 第四十六节的人工审核环节）：
    /// · 开关「AI 自动命题」：开启后低风险题经自动校验（结构/来源/单一答案/去重）立即临时使用；
    /// · 人工审核只决定能否进入长期正式题库——通过 → 永久入库（不受开关影响），否决 → 删除；
    /// · 高风险题（现实威胁/贫困/病房/创伤/人身安全）在人工通过前一律不参与抽题；
    /// · 支持手动「立即生成一批」，全过程见「日志」面板。
    /// </summary>
    public class QuizReviewPanel : MonoBehaviour
    {
        GameObject _panel;
        Text _statusText, _entryHeadText, _metaText, _questionText, _rationaleText;
        readonly Text[] _optionTexts = new Text[3];
        Text _toggleLabel;
        Button _approveBtn, _rejectBtn;

        List<AiQuizEntry> _pending = new List<AiQuizEntry>();
        int _index;

        public static QuizReviewPanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<QuizReviewPanel>();
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "QuizReviewPanel", Vector2.zero,
                new Color(0.05f, 0.04f, 0.09f, 0.96f));
            var prt = _panel.GetComponent<RectTransform>();
            prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
            prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;

            var title = UiUtil.MakeText(_panel.transform, "Title", "AI 命题审核", 40,
                TextAnchor.MiddleCenter, new Color(0.85f, 0.75f, 0.5f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -56), new Vector2(1400, 54));
            title.fontStyle = FontStyle.Bold;

            _statusText = UiUtil.MakeText(_panel.transform, "Status", "", 24,
                TextAnchor.MiddleCenter, new Color(0.8f, 0.85f, 0.8f));
            UiUtil.SetRect(_statusText, new Vector2(0.5f, 1f), new Vector2(0, -104), new Vector2(1500, 34));

            var hint = UiUtil.MakeText(_panel.transform, "Hint",
                "低风险题通过自动校验后已临时参与抽题；此处审核只决定能否进入长期正式题库。高风险题须先通过审核。",
                20, TextAnchor.MiddleCenter, new Color(0.65f, 0.65f, 0.72f));
            UiUtil.SetRect(hint, new Vector2(0.5f, 1f), new Vector2(0, -140), new Vector2(1600, 30));

            var toggleBtn = UiUtil.MakeButton(_panel.transform, "", new Vector2(0.5f, 1f),
                new Vector2(-250, -200), new Vector2(430, 72),
                new Color(0.25f, 0.4f, 0.35f, 0.95f), OnToggleAi, 26);
            _toggleLabel = toggleBtn.GetComponentInChildren<Text>();

            UiUtil.MakeButton(_panel.transform, "立即生成一批（本章）", new Vector2(0.5f, 1f),
                new Vector2(220, -200), new Vector2(430, 72),
                new Color(0.3f, 0.35f, 0.55f, 0.95f), OnGenerate, 26);

            _entryHeadText = UiUtil.MakeText(_panel.transform, "EntryHead", "", 24,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.55f));
            UiUtil.SetRect(_entryHeadText, new Vector2(0.5f, 1f), new Vector2(0, -270), new Vector2(1500, 34));

            _metaText = UiUtil.MakeText(_panel.transform, "Meta", "", 20,
                TextAnchor.MiddleCenter, new Color(0.6f, 0.75f, 0.9f));
            UiUtil.SetRect(_metaText, new Vector2(0.5f, 1f), new Vector2(0, -304), new Vector2(1600, 30));

            _questionText = UiUtil.MakeText(_panel.transform, "Question", "", 26,
                TextAnchor.UpperCenter, Color.white);
            UiUtil.SetRect(_questionText, new Vector2(0.5f, 1f), new Vector2(0, -372), new Vector2(1460, 100));

            for (int i = 0; i < 3; i++)
            {
                var bg = UiUtil.MakePanel(_panel.transform, "Opt" + i, new Vector2(1460, 84),
                    new Color(0.16f, 0.18f, 0.24f, 0.9f));
                UiUtil.SetRect(bg.GetComponent<Image>(), new Vector2(0.5f, 1f),
                    new Vector2(0, -486 - i * 94), new Vector2(1460, 84));
                var t = UiUtil.MakeText(bg.transform, "Label", "", 22,
                    TextAnchor.MiddleLeft, Color.white);
                var trt = t.GetComponent<RectTransform>();
                trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
                trt.offsetMin = new Vector2(24, 4); trt.offsetMax = new Vector2(-24, -4);
                _optionTexts[i] = t;
            }

            _rationaleText = UiUtil.MakeText(_panel.transform, "Rationale", "", 20,
                TextAnchor.UpperCenter, new Color(0.9f, 0.88f, 0.78f));
            UiUtil.SetRect(_rationaleText, new Vector2(0.5f, 1f), new Vector2(0, -850), new Vector2(1500, 170));

            UiUtil.MakeButton(_panel.transform, "上一条", new Vector2(0.5f, 0f),
                new Vector2(-560, 60), new Vector2(180, 76),
                new Color(0.3f, 0.3f, 0.38f, 0.95f), () => Step(-1), 24);
            UiUtil.MakeButton(_panel.transform, "下一条", new Vector2(0.5f, 0f),
                new Vector2(-360, 60), new Vector2(180, 76),
                new Color(0.3f, 0.3f, 0.38f, 0.95f), () => Step(1), 24);
            _approveBtn = UiUtil.MakeButton(_panel.transform, "通过 · 入长期题库", new Vector2(0.5f, 0f),
                new Vector2(-70, 60), new Vector2(340, 76),
                new Color(0.25f, 0.5f, 0.3f, 0.95f), OnApprove, 24);
            _rejectBtn = UiUtil.MakeButton(_panel.transform, "否决 · 删除", new Vector2(0.5f, 0f),
                new Vector2(260, 60), new Vector2(260, 76),
                new Color(0.55f, 0.25f, 0.22f, 0.95f), OnReject, 24);

            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(1f, 1f),
                new Vector2(-110, -46), new Vector2(160, 60),
                new Color(0.35f, 0.35f, 0.4f, 0.85f), Hide, 24);

            _panel.SetActive(false);
        }

        public void Toggle()
        {
            if (_panel.activeSelf) { Hide(); return; }
            _index = 0;
            Refresh();
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            Time.timeScale = 0f;
        }

        void Hide()
        {
            _panel.SetActive(false);
            Time.timeScale = 1f;
        }

        // ===================== 操作 =====================

        void OnToggleAi()
        {
            QuizAiService.SetEnabled(!QuizAiService.FeatureEnabled);
            Refresh();
        }

        void OnGenerate()
        {
            if (QuizAiService.Instance != null)
                QuizAiService.Instance.RequestBatch("审核面板手动生成");
            Refresh();
        }

        void Step(int dir)
        {
            _index += dir;
            Refresh();   // 顺便拉取生成协程新入库的候选题
        }

        void OnApprove()
        {
            if (_index < _pending.Count && _pending[_index].question != null)
                QuizAiBank.Approve(_pending[_index].question.questionId);
            Refresh();
        }

        void OnReject()
        {
            if (_index < _pending.Count && _pending[_index].question != null)
                QuizAiBank.Reject(_pending[_index].question.questionId);
            Refresh();
        }

        // ===================== 展示 =====================

        void Refresh()
        {
            _pending = QuizAiBank.PendingReview();
            if (_pending.Count == 0) _index = 0;
            else _index = Mathf.Clamp(_index, 0, _pending.Count - 1);

            bool on = QuizAiService.FeatureEnabled;
            var cfg = AIPromptConfig.Load();
            bool cloudReady = cfg.useCloud && !string.IsNullOrEmpty(cfg.apiKey);
            _toggleLabel.text = on ? "关闭 AI 自动命题" : "开启 AI 自动命题";
            _statusText.text = "AI 自动命题：" + (on ? "已开启" : "已关闭") +
                (on && !cloudReady ? "（⚠未配置云端 LLM——见「AI台词」面板）" : "") +
                "　｜　临时可用 " + QuizAiBank.CountOf(QuizAiBank.StatusTemp) +
                "　待复核高风险 " + QuizAiBank.CountOf(QuizAiBank.StatusHighRisk) +
                "　已入库 " + QuizAiBank.CountOf(QuizAiBank.StatusApproved);

            bool has = _pending.Count > 0;
            _approveBtn.interactable = has;
            _rejectBtn.interactable = has;
            if (!has)
            {
                _entryHeadText.text = "暂无待审核题目";
                _metaText.text = "";
                _questionText.text = on
                    ? "生成的候选题通过自动校验后会出现在这里。"
                    : "开启 AI 自动命题并配置云端 LLM 后，系统会按当前章节自动生成候选题。";
                _rationaleText.text = "";
                for (int i = 0; i < 3; i++) _optionTexts[i].text = "";
                return;
            }

            var e = _pending[_index];
            var q = e.question;
            bool highRisk = e.status == QuizAiBank.StatusHighRisk;
            _entryHeadText.text = "第 " + (_index + 1) + "/" + _pending.Count + " 条　｜　" +
                (highRisk ? "⚠高风险·未启用（" + e.riskNote + "）" : "低风险·临时使用中") +
                "　｜　生成于 " + e.createdAt;
            _entryHeadText.color = highRisk
                ? new Color(0.95f, 0.6f, 0.5f) : new Color(0.6f, 0.9f, 0.7f);

            string sources = q.sourceTags != null ? string.Join(", ", q.sourceTags) : "";
            _metaText.text = q.questionId + "　｜　场景：" + q.sceneTag + "　｜　概念：" + q.conceptTag +
                "　｜　形态：" + q.type + "　｜　难度：" + q.difficulty + "　｜　来源：" + sources;
            _questionText.text = q.question;
            for (int i = 0; i < 3; i++)
            {
                bool correct = i == q.correctIndex;
                _optionTexts[i].text = (correct ? "✓ " : "　 ") + (char)('A' + i) + ". " +
                    (q.options != null && i < q.options.Length ? q.options[i] : "");
                _optionTexts[i].color = correct ? new Color(0.6f, 0.95f, 0.65f) : Color.white;
            }
            _rationaleText.text = "依据：" + q.rationale;
        }
    }
}
