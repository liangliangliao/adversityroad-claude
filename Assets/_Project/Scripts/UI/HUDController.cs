using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;

namespace AdversityRoad.UI
{
    /// <summary>
    /// HUD：血条 + 五维心理条 + 任务提示 + 受击暗角脉冲 + 底部字幕（敌人台词/系统提示）。
    /// 订阅 GameEvents，与游戏逻辑完全解耦。
    /// </summary>
    public class HUDController : MonoBehaviour
    {
        public StatBar hpBar;
        public StatBar willBar;
        public StatBar focusBar;
        public StatBar selfWorthBar;
        public StatBar boundaryBar;
        public StatBar resolveBar;
        public Text questText;
        public Image vignette;      // 全屏暗角（raycastTarget 必须为 false）
        public Text subtitleText;   // 底部字幕
        public Image[] momentumPips; // 意势点（0-3）
        public Text comboText;       // 当前连段序列（拳·拳·腿）
        public RectTransform cineTop, cineBottom;   // 锁定战斗时的电影黑边

        float _cineHeight;

        float _vignetteAlpha;
        Color _vignetteColor = Color.red;
        float _subtitleHideAt;
        float _lastHp = -1;

        void OnEnable()
        {
            GameEvents.OnPlayerHpChanged += OnHp;
            GameEvents.OnMentalStatChanged += OnMental;
            GameEvents.OnQuestUpdated += OnQuest;
            GameEvents.OnSubtitle += OnSubtitle;
            GameEvents.OnMomentumChanged += OnMomentum;
            GameEvents.OnComboSeqChanged += OnComboSeq;
            GameEvents.OnLockStateChanged += OnLockState;
        }

        void OnDisable()
        {
            GameEvents.OnPlayerHpChanged -= OnHp;
            GameEvents.OnMentalStatChanged -= OnMental;
            GameEvents.OnQuestUpdated -= OnQuest;
            GameEvents.OnSubtitle -= OnSubtitle;
            GameEvents.OnMomentumChanged -= OnMomentum;
            GameEvents.OnComboSeqChanged -= OnComboSeq;
            GameEvents.OnLockStateChanged -= OnLockState;
        }

        void OnLockState(bool locked) => _cineHeight = locked ? 52f : 0f;

        void OnComboSeq(string seq)
        {
            if (comboText != null) comboText.text = seq;
        }

        void OnMomentum(int m)
        {
            if (momentumPips == null) return;
            for (int i = 0; i < momentumPips.Length; i++)
                if (momentumPips[i] != null)
                    momentumPips[i].color = i < m
                        ? new Color(1f, 0.82f, 0.3f, 0.95f)
                        : new Color(1f, 1f, 1f, 0.18f);
        }

        void Update()
        {
            if (vignette != null)
            {
                _vignetteAlpha = Mathf.Lerp(_vignetteAlpha, 0, 3f * Time.unscaledDeltaTime);
                var c = _vignetteColor;
                c.a = _vignetteAlpha;
                vignette.color = c;
            }
            if (subtitleText != null && subtitleText.text.Length > 0 && Time.unscaledTime > _subtitleHideAt)
                subtitleText.text = "";

            // 电影黑边平滑滑入/滑出
            if (cineTop != null)
            {
                float h = Mathf.Lerp(cineTop.sizeDelta.y, _cineHeight, 5f * Time.unscaledDeltaTime);
                cineTop.sizeDelta = new Vector2(cineTop.sizeDelta.x, h);
                if (cineBottom != null)
                    cineBottom.sizeDelta = new Vector2(cineBottom.sizeDelta.x, h);
            }
        }

        void Pulse(Color color, float alpha)
        {
            _vignetteColor = color;
            _vignetteAlpha = Mathf.Max(_vignetteAlpha, alpha);
        }

        void OnHp(float cur, float max)
        {
            if (hpBar != null) hpBar.SetValue(cur, max);
            if (_lastHp >= 0 && cur < _lastHp) Pulse(new Color(0.7f, 0.05f, 0.05f), 0.4f);
            _lastHp = cur;
        }

        void OnMental(string stat, float cur, float max)
        {
            StatBar bar = null;
            switch (stat)
            {
                case "will": bar = willBar; break;
                case "focus": bar = focusBar; break;
                case "selfWorth": bar = selfWorthBar; break;
                case "boundary": bar = boundaryBar; break;
                case "resolve": bar = resolveBar; break;
            }
            if (bar != null)
            {
                // 数值下降 = 受到心理攻击 → 紫色暗角脉冲
                if (bar.LastRatio * max > cur + 0.5f) Pulse(new Color(0.35f, 0.05f, 0.5f), 0.35f);
                bar.SetValue(cur, max);
            }
        }

        void OnQuest(string questId)
        {
            if (questText == null || Quest.QuestManager.Instance == null) return;
            foreach (var q in Quest.QuestManager.Instance.activeQuests)
                if (!q.completed) { questText.text = q.title; return; }
            questText.text = "";
        }

        void OnSubtitle(string text)
        {
            if (subtitleText == null) return;
            subtitleText.text = text;
            _subtitleHideAt = Time.unscaledTime + 3.5f;
        }
    }
}
