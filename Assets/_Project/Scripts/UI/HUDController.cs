using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;

namespace AdversityRoad.UI
{
    /// <summary>
    /// HUD：血条 + 五维心理条 + 任务提示 + 受击暗角脉冲 + 底部字幕（敌人台词/系统提示）。
    /// 订阅 GameEvents，与游戏逻辑完全解耦。
    ///
    /// V2.0 情境化原则（方案 12.2 / 17.1）：高速动作时**不同时显示 9-10 根常驻条**。
    /// 常驻只留 HP / 体力 / 意志 / 当前目标 / 当前姿态；
    /// 专注、边界、自尊、反刍、关系消耗、公平刺痛、行动力在**受到相关攻击时**
    /// 短暂显现，几秒后淡出，暂停与复盘页仍然完整展示。底层数值一个都不删。
    /// </summary>
    public class HUDController : MonoBehaviour
    {
        public StatBar hpBar;
        public StatBar staminaBar;      // 体力：V2.0 常驻三条之一
        public StatBar willBar;
        public StatBar focusBar;
        public StatBar selfWorthBar;
        public StatBar boundaryBar;
        public StatBar actionPowerBar;  // 行动力：抵抗拖延；过低移速下降
        public StatBar ruminationBar;   // 反刍值：越满越糟（与其它条相反）
        public StatBar drainBar;        // 关系消耗值：越满越糟（技能冷却变长）
        public StatBar fairnessPainBar; // 公平刺痛值：越满越糟（三档规则，方案十七）
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

        public Text stressText;     // 压力阶段 + 逆转窗口提示（情境出现）
        public Text beaconText;     // 目标灯塔方向（弱化箭头，只给方向与距离）

        /// <summary>
        /// 当前关卡目标（只在 AI 生成场景里出现）。
        ///
        /// 玩家问"怎么挑战关卡"，说明进场那两句字幕根本不够——字幕会滚走，
        /// 而"我现在到底该干什么、还剩几个"必须是**随时抬头就能看到**的。
        /// </summary>
        public Text objectiveText;

        static HUDController _inst;

        /// <summary>设置关卡目标行；传空字符串即隐藏（离开生成场景时调用）。</summary>
        public static void SetObjective(string text)
        {
            if (_inst == null || _inst.objectiveText == null) return;
            _inst.objectiveText.text = text ?? "";
        }

        float _vignetteAlpha;
        Color _vignetteColor = Color.red;
        float _subtitleHideAt;
        float _lastHp = -1;

        // 情境化条：stat 名 → 隐藏时间戳（受到相关攻击时显现，之后淡出）
        readonly System.Collections.Generic.Dictionary<StatBar, float> _contextualUntil =
            new System.Collections.Generic.Dictionary<StatBar, float>();
        const float ContextualHold = 6f;
        float _nextBeaconRefresh;

        void OnEnable()
        {
            _inst = this;
            GameEvents.OnPlayerHpChanged += OnHp;
            GameEvents.OnStaminaChanged += OnStamina;
            GameEvents.OnMentalStatChanged += OnMental;
            GameEvents.OnQuestUpdated += OnQuest;
            GameEvents.OnSubtitle += OnSubtitle;
            GameEvents.OnMomentumChanged += OnMomentum;
            GameEvents.OnComboSeqChanged += OnComboSeq;
            GameEvents.OnLockStateChanged += OnLockState;
            GameEvents.OnSkillBanner += OnBanner;
            GameEvents.OnComboCount += OnComboCount;
            GameEvents.OnGoalChanged += RefreshGoal;
            Goals.GoalOS.Changed += RefreshGoal;
            RefreshGoal();
            SetupContextualBars();
        }

        void OnDisable()
        {
            GameEvents.OnGoalChanged -= RefreshGoal;
            Goals.GoalOS.Changed -= RefreshGoal;
            GameEvents.OnPlayerHpChanged -= OnHp;
            GameEvents.OnStaminaChanged -= OnStamina;
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

            TickContextual();
            TickStressAndBeacon();

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

        void OnStamina(float cur, float max)
        {
            if (staminaBar != null) staminaBar.SetValue(cur, max);
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
                    {
                        Pulse(new Color(0.4f, 0.05f, 0.35f), 0.3f);
                        FlashContextual(ruminationBar);
                    }
                    ruminationBar.SetValue(cur, max);
                }
                return;
            }
            if (stat == "relationshipDrain")
            {
                if (drainBar != null)
                {
                    if (cur > drainBar.LastRatio * max + 0.5f)
                    {
                        Pulse(new Color(0.45f, 0.2f, 0.05f), 0.25f);
                        FlashContextual(drainBar);
                    }
                    drainBar.SetValue(cur, max);
                }
                return;
            }
            if (stat == "fairnessPain")
            {
                if (fairnessPainBar != null)
                {
                    if (cur > fairnessPainBar.LastRatio * max + 0.5f)
                    {
                        Pulse(new Color(0.5f, 0.12f, 0.12f), 0.28f);
                        FlashContextual(fairnessPainBar);
                    }
                    fairnessPainBar.SetValue(cur, max);
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
                if (bar.LastRatio * max > cur + 0.5f)
                {
                    Pulse(new Color(0.35f, 0.05f, 0.5f), 0.35f);
                    FlashContextual(bar);   // 情境出现：被打到哪一条，哪一条才亮
                }
                bar.SetValue(cur, max);
            }
        }

        /// <summary>
        /// 目标前台化：优先显示 Goal OS 的当前主线与当前节点；
        /// 没有进行中的旅程时回落到 V1 的「今日唯一目标」（功能保留，不删）。
        /// </summary>
        void RefreshGoal()
        {
            if (goalText == null) return;
            string v2 = Goals.GoalOS.HudLine();
            goalText.text = !string.IsNullOrEmpty(v2) ? v2 : GoalSystem.HudLine();
        }

        // ================= 情境化属性条（方案 12.2） =================

        /// <summary>
        /// 常驻只有 HP / 体力 / 意志三条；其余七条改为"被打到哪一条，哪一条才亮"。
        /// 亮起的条按出现顺序紧贴常驻条堆叠——不留空洞，也不会压到姿态条。
        /// 由 GameBootstrap 在所有条建好之后调用。
        /// </summary>
        public void SetupContextualBars()
        {
            _contextualUntil.Clear();
            _contextualOrder.Clear();
            RegisterContextual(focusBar);
            RegisterContextual(selfWorthBar);
            RegisterContextual(boundaryBar);
            RegisterContextual(actionPowerBar);
            RegisterContextual(ruminationBar);
            RegisterContextual(drainBar);
            RegisterContextual(fairnessPainBar);
            LayoutPersistent();
        }

        /// <summary>
        /// 心理小条网格的左上角与行列间距（由 GameBootstrap 建 HUD 时写入）。
        /// 情境条亮起后要补进这张网格，位置必须和主血条、状态背板算的是同一套坐标——
        /// 所以这里不写死数字，跟着 HudLayout 走。
        /// </summary>
        public Vector2 miniOrigin = new Vector2(16f, -96f);
        public Vector2 miniStep = new Vector2(176f, 27f);

        /// <summary>HP 与体力是主条，位置固定不动；意志固定占住小条网格的第一格。</summary>
        void LayoutPersistent() => PlaceMini(willBar, 0);

        void PlaceMini(StatBar bar, int slot)
        {
            if (bar == null) return;
            var rt = bar.GetComponent<RectTransform>();
            if (rt == null) return;
            int col = slot % MiniColumns, row = slot / MiniColumns;
            rt.anchoredPosition = new Vector2(miniOrigin.x + col * miniStep.x,
                miniOrigin.y - row * miniStep.y);
        }

        void RegisterContextual(StatBar bar)
        {
            if (bar == null) return;
            _contextualUntil[bar] = 0f;
            bar.gameObject.SetActive(false);
        }

        /// <summary>某条属性受到攻击：把它临时显出来，并排进堆叠。</summary>
        void FlashContextual(StatBar bar)
        {
            if (bar == null || !_contextualUntil.ContainsKey(bar)) return;
            _contextualUntil[bar] = Time.unscaledTime + ContextualHold;
            if (bar.gameObject.activeSelf) return;

            // 同时最多亮四条：再多就把最早到期的那条先收起来（HUD 不允许被数值淹没）
            while (_contextualOrder.Count >= MaxContextualVisible)
            {
                var oldest = _contextualOrder[0];
                _contextualOrder.RemoveAt(0);
                if (oldest != null) oldest.gameObject.SetActive(false);
            }
            _contextualOrder.Add(bar);
            bar.gameObject.SetActive(true);
            RelayoutContextual();
        }

        void TickContextual()
        {
            if (_contextualOrder.Count == 0) return;
            bool dirty = false;
            for (int i = _contextualOrder.Count - 1; i >= 0; i--)
            {
                var bar = _contextualOrder[i];
                if (bar == null) { _contextualOrder.RemoveAt(i); dirty = true; continue; }
                if (Time.unscaledTime <= _contextualUntil[bar]) continue;
                bar.gameObject.SetActive(false);
                _contextualOrder.RemoveAt(i);
                dirty = true;
            }
            if (dirty) RelayoutContextual();
        }

        void RelayoutContextual()
        {
            // 第 0 格被意志占着，情境条从第 1 格起紧贴着排——不留空洞
            for (int i = 0; i < _contextualOrder.Count; i++)
                PlaceMini(_contextualOrder[i], 1 + i);
        }

        const int MiniColumns = 2;
        const int MaxContextualVisible = 4;   // 连同意志共五格，正好落在状态背板内

        readonly System.Collections.Generic.List<StatBar> _contextualOrder =
            new System.Collections.Generic.List<StatBar>();

        // ================= 压力与灯塔 =================

        void TickStressAndBeacon()
        {
            if (stressText != null)
            {
                var stress = Adversity.StressStateMachine.Instance;
                var resolve = Adversity.ResolveSystem.Instance;
                if (resolve != null && resolve.SurgeActive)
                {
                    stressText.text = "逆转 · Adversity Surge";
                    stressText.color = new Color(1f, 0.85f, 0.35f);
                }
                else if (resolve != null && resolve.WindowOpen)
                {
                    stressText.text = "逆转窗口开启：一次高质量的格挡/闪避/目标动作";
                    stressText.color = new Color(1f, 0.7f, 0.3f);
                }
                else if (stress != null && stress.Stage >= Adversity.StressStage.Destabilized)
                {
                    stressText.text = "压力 · " + Adversity.StressStateMachine.StageLabel(stress.Stage);
                    stressText.color = stress.Stage >= Adversity.StressStage.NearCollapse
                        ? new Color(1f, 0.4f, 0.4f) : new Color(0.9f, 0.75f, 0.5f);
                }
                else stressText.text = "";
            }

            if (beaconText != null && Time.unscaledTime >= _nextBeaconRefresh)
            {
                _nextBeaconRefresh = Time.unscaledTime + 1f;
                var beacon = OpenWorld.GoalBeaconController.Instance;
                beaconText.text = beacon != null ? beacon.DirectionLine() : "";
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
