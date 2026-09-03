using System;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.Shame
{
    /// <summary>
    /// 自行陈述（方案 8.5.2 / 8.8.2 / 8.13.1 StatementSystem）。
    ///
    /// 【它与"被安排的当众处罚"的唯一区别，就是这三个参数由谁来定】
    /// 时机、对象、措辞——三项全部由玩家自选。系统不提供推荐选项，
    /// 不用箭头把玩家推进门里，也不因为选得"不够好"而扣分（8.5.1）。
    ///
    /// 【陈述完成的瞬间，法官失去权限并消失，没有战斗动画】
    /// 通关不是打赢他，是把裁判权收回来。
    ///
    /// 【随时可退出、可用文字复盘替代】
    /// 8.12.1：任何公开场景都必须能退出并返回，也必须能用文字复盘顶替而不阻断主线。
    /// 所以这块面板上永远有第三个按钮。
    /// </summary>
    public class StatementSystem : MonoBehaviour
    {
        public static StatementSystem Instance { get; private set; }

        static readonly string[] AudienceLabels =
        {
            "只对一个人说",
            "对相关的人说",
            "对在场所有人说",
        };

        static readonly string[] WordingLabels =
        {
            "只讲发生了什么",
            "讲事实，也讲我的判断",
            "讲事实，然后讲我下一步做什么",
        };

        GameObject _panel;
        Text _timingText, _audienceText, _wordingText, _hintText;
        bool _open;
        int _audience, _wording;
        float _timingRatio;

        public bool IsOpen => _open;

        public static StatementSystem Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("StatementSystem");
            Instance = go.AddComponent<StatementSystem>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>玩家自己走进广播室：开面板。时机参数在这一刻就已经定下了。</summary>
        public void Open()
        {
            if (_open) return;
            var timer = PendingCaseTimer.Instance;
            _timingRatio = timer != null ? timer.Remaining01 : 1f;
            _audience = 1;
            _wording = 0;
            _open = true;
            Build();
            if (_panel != null) _panel.SetActive(true);
            Refresh();
            GameEvents.RaiseSubtitle("门一直是开着的。你自己走进来了——现在，时间、对象、措辞都由你定。");
        }

        public void Close()
        {
            _open = false;
            if (_panel != null) _panel.SetActive(false);
        }

        void Refresh()
        {
            if (_timingText == null) return;
            _timingText.text = "时机：计时器剩余 " + Mathf.RoundToInt(_timingRatio * 100f) + "%" +
                (_timingRatio >= 0.4f ? "（主动，不是被逼到最后）" : "（已经被逼到很靠后了）");
            _audienceText.text = "对象：" + AudienceLabels[_audience];
            _wordingText.text = "措辞：" + WordingLabels[_wording];
            _hintText.text = "这三项没有标准答案，也不影响是否通关——只影响这次陈述留下什么记录。";
        }

        void Confirm()
        {
            var rec = new StatementRecord
            {
                timingRatio = _timingRatio,
                audienceScope = _audience,
                wordingProfile = _wording,
                resultRank = _timingRatio >= 0.4f ? "best" : "normal",
                createdAt = DateTime.UtcNow.ToString("o"),
            };

            var p = AdversityRoad.Core.ActorRegistry.Player;
            float restore = _timingRatio >= 0.4f ? 45f : 22f;
            if (p != null) p.Stats.RestoreAxis(Personalization.WeaknessAxis.Shame, restore);
            rec.selfWorthDelta = restore;

            var d = ShameLine.Data;
            d.statementHistory.Add(rec);
            ShameLine.Persist();

            var exposure = ExposureSystem.Instance;
            if (exposure != null) exposure.Clear("陈述完成——暴露度归零。你没有被清白，你只是不再需要躲。");

            ShameComboTracker.Push(ShameComboTracker.TagStatement);
            Adversity.AdversityProfile.ObserveStrength("陈述提前量", ShameLine.CurrentLevelId);
            Adversity.CourageSystem.NoteGoalAction("主动完成一次自行陈述");

            Close();
            var ctl = ShameLineController.Instance;
            if (ctl != null) ctl.OnStatementCompleted(rec);
        }

        /// <summary>用文字复盘替代这次公开场景（8.12.1）：不阻断主线，只影响结算评价。</summary>
        void SubstituteWithReflection()
        {
            var rec = new StatementRecord
            {
                timingRatio = _timingRatio,
                audienceScope = 0,
                wordingProfile = 0,
                resultRank = "normal",
                createdAt = DateTime.UtcNow.ToString("o"),
            };
            ShameLine.Data.statementHistory.Add(rec);
            ShameLine.Persist();

            var exposure = ExposureSystem.Instance;
            if (exposure != null) exposure.Clear("这一次写下来就够了。");
            GameEvents.RaiseSubtitle("以文字复盘替代了这次公开陈述——主线照常推进，评价按普通结算记。");

            Close();
            var ctl = ShameLineController.Instance;
            if (ctl != null) ctl.OnStatementCompleted(rec);
        }

        void Leave()
        {
            Close();
            GameEvents.RaiseSubtitle("你退了出来。门还开着，什么时候回来都可以。");
        }

        // ================= 面板 =================

        void Build()
        {
            if (_panel != null) return;
            var canvas = FindObjectOfType<Canvas>();
            if (canvas == null) return;

            _panel = UiUtil.MakePanel(canvas.transform, "StatementPanel",
                new Vector2(880, 560), new Color(0.07f, 0.07f, 0.1f, 0.97f));

            var title = UiUtil.MakeText(_panel.transform, "Title", "自 行 陈 述", 36,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.88f, 0.6f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -44), new Vector2(700, 48));

            var sub = UiUtil.MakeText(_panel.transform, "Sub",
                "由你决定何时说、对谁说、用什么措辞说。", 22,
                TextAnchor.MiddleCenter, new Color(0.85f, 0.88f, 0.92f));
            UiUtil.SetRect(sub, new Vector2(0.5f, 1f), new Vector2(0, -86), new Vector2(800, 30));

            _timingText = UiUtil.MakeText(_panel.transform, "Timing", "", 24,
                TextAnchor.MiddleLeft, new Color(0.9f, 0.9f, 0.95f));
            UiUtil.SetRect(_timingText, new Vector2(0.5f, 1f), new Vector2(-40, -140), new Vector2(700, 32));

            _audienceText = UiUtil.MakeText(_panel.transform, "Audience", "", 24,
                TextAnchor.MiddleLeft, new Color(0.9f, 0.9f, 0.95f));
            UiUtil.SetRect(_audienceText, new Vector2(0.5f, 1f), new Vector2(-40, -200), new Vector2(560, 32));
            UiUtil.MakeButton(_panel.transform, "换", new Vector2(0.5f, 1f),
                new Vector2(340, -200), new Vector2(100, 46), new Color(0.28f, 0.32f, 0.42f, 0.95f),
                () => { _audience = (_audience + 1) % AudienceLabels.Length; Refresh(); }, 24);

            _wordingText = UiUtil.MakeText(_panel.transform, "Wording", "", 24,
                TextAnchor.MiddleLeft, new Color(0.9f, 0.9f, 0.95f));
            UiUtil.SetRect(_wordingText, new Vector2(0.5f, 1f), new Vector2(-40, -260), new Vector2(560, 32));
            UiUtil.MakeButton(_panel.transform, "换", new Vector2(0.5f, 1f),
                new Vector2(340, -260), new Vector2(100, 46), new Color(0.28f, 0.32f, 0.42f, 0.95f),
                () => { _wording = (_wording + 1) % WordingLabels.Length; Refresh(); }, 24);

            _hintText = UiUtil.MakeText(_panel.transform, "Hint", "", 20,
                TextAnchor.MiddleCenter, new Color(0.72f, 0.76f, 0.8f));
            UiUtil.SetRect(_hintText, new Vector2(0.5f, 1f), new Vector2(0, -318), new Vector2(800, 28));

            UiUtil.MakeButton(_panel.transform, "开口说出来", new Vector2(0.5f, 0f),
                new Vector2(-250, 74), new Vector2(300, 72),
                new Color(0.34f, 0.42f, 0.3f, 0.96f), Confirm, 26);
            UiUtil.MakeButton(_panel.transform, "以文字复盘替代", new Vector2(0.5f, 0f),
                new Vector2(60, 74), new Vector2(300, 72),
                new Color(0.3f, 0.32f, 0.42f, 0.96f), SubstituteWithReflection, 24);
            UiUtil.MakeButton(_panel.transform, "先出去", new Vector2(0.5f, 0f),
                new Vector2(330, 74), new Vector2(180, 72),
                new Color(0.3f, 0.3f, 0.34f, 0.95f), Leave, 24);

            _panel.SetActive(false);
        }
    }
}
