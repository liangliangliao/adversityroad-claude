using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;
using AdversityRoad.InternalOS;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 内部语言攻击的三选一弹框（V2.2 增补章第 7 节）。
    ///
    /// 【三种呈现，一套面板】
    /// · 普通探索 / 阶段切换：不倒计时，世界暂停（timeScale 0）。玩家可以一直看着那句话。
    /// · Boss 非高速阶段：timeScale 0.2，3 秒后自动淡出。
    /// · 高速动作战：不完全暂停（timeScale 0.6），面板压到屏幕下方，选项映射三个键位。
    ///
    /// 【这里刻意不做的两件事】
    /// ① 不显示"正确/错误"。选对只显示一行极短反馈 + 那件要去做的事；
    ///    选错也只显示这一步的代价，不判分、不出羞辱音（第 3、7 节）。
    /// ② 不显示 best 的内部标签（attackIntent、distractorFamily、bestReason）。
    ///    PRD 原话："这些标签不直接显示给玩家"。
    /// </summary>
    public class MentalChoicePanel : MonoBehaviour
    {
        static MentalChoicePanel _open;

        GameObject _panel;
        MentalAttackPresentation _pres;
        float _autoCloseAt = -1f;
        float _restoreTimeScale = 1f;
        bool _timeScaleTouched;

        public static bool AnyOpen => _open != null;

        /// <summary>挂上事件总线。GameBootstrap 调一次即可。</summary>
        public static void Hook()
        {
            MentalAttackSystem.OnPresent -= Show;
            MentalAttackSystem.OnPresent += Show;
        }

        public static void Show(MentalAttackPresentation pres)
        {
            if (pres == null || _open != null) return;
            var canvas = UiUtil.MainCanvas();
            if (canvas == null) return;

            var go = new GameObject("MentalChoicePanel");
            _open = go.AddComponent<MentalChoicePanel>();
            _open.Build(canvas.transform, pres);
        }

        void Build(Transform canvas, MentalAttackPresentation pres)
        {
            _pres = pres;
            var e = pres.source;
            var mode = e.Mode();

            // 高速战斗时面板压到下方，别挡住正在出招的敌人；其余居中。
            bool lowProfile = mode == MentalPopupMode.HighSpeedHud;
            float w = lowProfile ? 980f : 1080f;
            float h = lowProfile ? 300f : 420f;

            _panel = UiUtil.MakePanel(canvas, "MentalChoice", new Vector2(w, h),
                new Color(0.05f, 0.05f, 0.07f, lowProfile ? 0.82f : 0.94f));
            UiUtil.SetRect(_panel.GetComponent<Image>(),
                lowProfile ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 0.5f),
                lowProfile ? new Vector2(0, h * 0.5f + 120f) : Vector2.zero,
                new Vector2(w, h));

            // 攻击句：它是敌人说出来的那一句，不是旁白，所以给它最大的字号。
            var line = UiUtil.MakeText(_panel.transform, "AttackLine", e.attackLine, 28,
                TextAnchor.MiddleCenter, new Color(0.93f, 0.82f, 0.78f));
            line.horizontalOverflow = HorizontalWrapMode.Wrap;
            line.verticalOverflow = VerticalWrapMode.Overflow;
            UiUtil.SetRect(line, new Vector2(0.5f, 1f), new Vector2(0, -52), new Vector2(w - 90, 66));

            // 三个选项：顺序来自 pres.shown（已洗牌），绝不读 e.options 的原序。
            float btnW = w - 120f, btnH = lowProfile ? 52f : 62f;
            float top = lowProfile ? -118f : -140f;
            float gap = btnH + 14f;
            for (int i = 0; i < pres.shown.Count; i++)
            {
                var opt = pres.shown[i];
                string key = i == 0 ? "①" : (i == 1 ? "②" : "③");
                UiUtil.MakeButton(_panel.transform, key + " " + opt.text,
                    new Vector2(0.5f, 1f), new Vector2(0, top - gap * i),
                    new Vector2(btnW, btnH),
                    new Color(0.17f, 0.19f, 0.24f, 0.96f),
                    () => Pick(opt.optionId), lowProfile ? 19 : 21);
            }

            if (!lowProfile)
            {
                var hint = UiUtil.MakeText(_panel.transform, "Hint",
                    "没有哪一个是「最积极」的答案——挑当下证据下最合理的那一个。", 16,
                    TextAnchor.MiddleCenter, new Color(0.55f, 0.55f, 0.62f));
                UiUtil.SetRect(hint, new Vector2(0.5f, 0f), new Vector2(0, 34), new Vector2(w - 90, 22));
            }

            ApplyTimeScale(mode);
            if (mode == MentalPopupMode.BossSlowMotion)
                _autoCloseAt = Time.unscaledTime + MentalAttackSystem.BossPopupSeconds;
            else if (mode == MentalPopupMode.HighSpeedHud)
                _autoCloseAt = Time.unscaledTime + 2.2f;
        }

        void ApplyTimeScale(MentalPopupMode mode)
        {
            _restoreTimeScale = Time.timeScale;
            _timeScaleTouched = true;
            switch (mode)
            {
                case MentalPopupMode.ExplorePause:
                    Time.timeScale = 0f;
                    break;
                case MentalPopupMode.BossSlowMotion:
                    Time.timeScale = MentalAttackSystem.BossSlowMotionScale;
                    break;
                default:
                    Time.timeScale = MentalAttackSystem.HighSpeedSlowMotionScale;
                    break;
            }
        }

        void Update()
        {
            if (_autoCloseAt < 0f) return;
            // 用 unscaledTime：timeScale 已经被自己改过，用缩放时间会把 3 秒拖成 15 秒。
            if (Time.unscaledTime < _autoCloseAt) return;
            // 超时 = 没选。不是答错（第 7 节）。
            MentalAttackSystem.Dismiss();
            Close();
        }

        void Pick(string optionId)
        {
            var outcome = MentalAttackSystem.Choose(optionId);
            var picked = _pres != null && _pres.source != null ? _pres.source.ById(optionId) : null;
            Close();
            ShowFeedback(outcome, picked);
        }

        /// <summary>
        /// 极短反馈。选对了给的是"你现在可以去做什么"，不是"回答正确"；
        /// 选错了给的是这一步的代价，不是"回答错误"。
        /// </summary>
        static void ShowFeedback(MentalChoiceOutcome outcome, MentalAttackOption picked)
        {
            if (outcome == MentalChoiceOutcome.NoResponse) return;

            if (outcome == MentalChoiceOutcome.Best)
            {
                string todo = MentalAttackSystem.PendingFollowUp;
                GameEvents.RaiseSubtitle(string.IsNullOrEmpty(todo)
                    ? "窗口打开了。" : "窗口打开了 —— " + todo);
                return;
            }

            GameEvents.RaiseSubtitle(picked != null && !string.IsNullOrEmpty(picked.immediateEffect)
                ? "这一步的代价：" + picked.immediateEffect
                : "这一步没有让你更靠近目标。");
        }

        void Close()
        {
            if (_timeScaleTouched)
            {
                Time.timeScale = _restoreTimeScale;
                _timeScaleTouched = false;
            }
            if (_panel != null) Destroy(_panel);
            if (_open == this) _open = null;
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            // 面板被外力销毁（切场景等）时也必须把时间放回去，否则整个游戏停在 0 速。
            if (!_timeScaleTouched) return;
            Time.timeScale = _restoreTimeScale;
            _timeScaleTouched = false;
        }
    }
}
