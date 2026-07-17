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
        public StatBar actionPowerBar;  // 行动力：抵抗拖延；过低移速下降
        public StatBar ruminationBar;   // 反刍值：越满越糟（与其它条相反）
        public StatBar drainBar;        // 关系消耗值：越满越糟（技能冷却变长）
        public Text questText;
        public Text goalText;       // 今日目标常驻行（目标板系统）
        public Image vignette;      // 全屏暗角（raycastTarget 必须为 false）
        public Text subtitleText;   // 底部字幕
        public Image[] momentumPips; // 意势点（0-3）
        public Text comboText;       // 当前连段序列（拳·拳·腿）
        public RectTransform cineTop, cineBottom;   // 锁定战斗时的电影黑边
        public Text skillBanner;                    // 招式大字横幅（中央弹出）
        public Text comboCountText;                 // 连击计数器（屏幕固定，格斗游戏式）

        float _cineHeight;
        float _bannerUntil;
        float _comboCountUntil;

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
            GameEvents.OnSkillBanner += OnBanner;
            GameEvents.OnComboCount += OnComboCount;
            GameEvents.OnGoalChanged += RefreshGoal;
            RefreshGoal();
        }

        void OnDisable()
        {
            GameEvents.OnGoalChanged -= RefreshGoal;
            GameEvents.OnPlayerHpChanged -= OnHp;
            GameEvents.OnMentalStatChanged -= OnMental;
            GameEvents.OnQuestUpdated -= OnQuest;
            GameEvents.OnSubtitle -= OnSubtitle;
            GameEvents.OnMomentumChanged -= OnMomentum;
            GameEvents.OnComboSeqChanged -= OnComboSeq;
            GameEvents.OnLockStateChanged -= OnLockState;
            GameEvents.OnSkillBanner -= OnBanner;
            GameEvents.OnComboCount -= OnComboCount;
        }

        /// <summary>连击计数：固定在屏幕右侧的格斗游戏式计数器——每次命中弹一下缩放，
        /// 数字越高颜色越烈；断连 1.6s 后淡出（浮字不再满场乱飞）。</summary>
        void OnComboCount(int n)
        {
            if (comboCountText == null) return;
            comboCountText.text = n + " 连击";
            var col = Color.Lerp(new Color(1f, 0.85f, 0.3f), new Color(1f, 0.3f, 0.15f),
                Mathf.Clamp01((n - 2) / 10f));
            col.a = 1f;
            comboCountText.color = col;
            comboCountText.transform.localScale = Vector3.one * 1.45f;
            _comboCountUntil = Time.unscaledTime + 1.6f;
        }

        void OnBanner(string name)
        {
            if (skillBanner == null) return;
            skillBanner.text = name;
            skillBanner.transform.localScale = Vector3.one * 1.6f;
            var c = skillBanner.color; c.a = 1f; skillBanner.color = c;
            _bannerUntil = Time.unscaledTime + 0.9f;
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

            // 招式横幅：弹入缩放+超时淡出
            if (skillBanner != null && skillBanner.color.a > 0.01f)
            {
                skillBanner.transform.localScale = Vector3.Lerp(
                    skillBanner.transform.localScale, Vector3.one, 10f * Time.unscaledDeltaTime);
                if (Time.unscaledTime > _bannerUntil)
                {
                    var c = skillBanner.color;
                    c.a = Mathf.MoveTowards(c.a, 0, 3f * Time.unscaledDeltaTime);
                    skillBanner.color = c;
                }
            }

            // 连击计数器：弹入缩放 + 断连淡出
            if (comboCountText != null && comboCountText.color.a > 0.01f)
            {
                comboCountText.transform.localScale = Vector3.Lerp(
                    comboCountText.transform.localScale, Vector3.one, 12f * Time.unscaledDeltaTime);
                if (Time.unscaledTime > _comboCountUntil)
                {
                    var c = comboCountText.color;
                    c.a = Mathf.MoveTowards(c.a, 0, 4f * Time.unscaledDeltaTime);
                    comboCountText.color = c;
                }
            }

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
            // 反刍/关系消耗方向相反：数值"上升"才是受损，用紫色暗角脉冲提示
            if (stat == "rumination")
            {
                if (ruminationBar != null)
                {
                    if (cur > ruminationBar.LastRatio * max + 0.5f)
                        Pulse(new Color(0.4f, 0.05f, 0.35f), 0.3f);
                    ruminationBar.SetValue(cur, max);
                }
                return;
            }
            if (stat == "relationshipDrain")
            {
                if (drainBar != null)
                {
                    if (cur > drainBar.LastRatio * max + 0.5f)
                        Pulse(new Color(0.45f, 0.2f, 0.05f), 0.25f);
                    drainBar.SetValue(cur, max);
                }
                return;
            }

            StatBar bar = null;
            switch (stat)
            {
                case "will": bar = willBar; break;
                case "focus": bar = focusBar; break;
                case "selfWorth": bar = selfWorthBar; break;
                case "boundary": bar = boundaryBar; break;
                case "actionPower": bar = actionPowerBar; break;
            }
            if (bar != null)
            {
                // 数值下降 = 受到心理攻击 → 紫色暗角脉冲
                if (bar.LastRatio * max > cur + 0.5f) Pulse(new Color(0.35f, 0.05f, 0.5f), 0.35f);
                bar.SetValue(cur, max);
            }
        }

        void RefreshGoal()
        {
            if (goalText != null) goalText.text = GoalSystem.HudLine();
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
