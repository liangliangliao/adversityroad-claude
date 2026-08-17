using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 能量分级警告系统：
    /// · 每项能量两级警告——偏低（&lt;40%，字幕提醒）与告急（&lt;20%，字幕+警示音）；
    ///   负向能量（反刍/关系消耗）反向判定（&gt;70% / &gt;85%）；带迟滞回差避免反复刷屏；
    /// · 生命字幕两级：偏低（35%）→ 危险（20%）；
    /// · 【休整答题弹窗只有一个触发条件：生命 &lt; 30%】——模态弹窗会冻结画面，
    ///   多一个触发口就多一次战斗被打断，所以这里刻意只留一条路；
    /// · 低生命期间持续红色边缘脉冲提示（非缩放时间驱动，暂停中也可见）。
    /// </summary>
    public class VitalAlertController : MonoBehaviour
    {
        const float LowRatio = 0.40f;       // 一级：偏低
        const float SevereRatio = 0.20f;    // 二级：告急
        // 【休整答题弹窗的唯一触发条件】：生命 < 30%。
        // 之前是两条路都能弹——① 每帧轮询 hp < CriticalHpRatio(15%)；
        // ② OnLifeThreatened 事件（穿越垂危线 / 濒死守护）无视静默期强制弹。
        // 模态弹窗会把 timeScale 归零、整块盖住画面，战斗中被它打断一次就等于送一次死，
        // 玩家明确要求"只在生命低于 30% 时出现，其他都不出现"。
        // 现在只保留这一条：一个阈值、一处判断，行为完全可预测。
        const float RestPromptRatio = 0.30f;
        const float Hysteresis = 0.08f;     // 迟滞回差：回升超过阈值+8%才允许再次警告
        const float PromptCooldown = 45f;   // 玩家选择「继续战斗」后弹窗静默时长

        QuizPanel _quiz;
        PlayerController _player;

        Image _vignette;                    // 低生命红色脉冲罩屏
        GameObject _prompt;                 // 生命垂危模态弹窗
        Text _promptDetail;

        // 各能量当前警告档位：0=正常 1=已警告偏低 2=已警告告急
        readonly Dictionary<string, int> _warnLevel = new Dictionary<string, int>();
        float _nextPromptAllowed;
        bool _promptOpen;

        public static VitalAlertController Create(Transform canvas, QuizPanel quiz)
        {
            var comp = canvas.gameObject.AddComponent<VitalAlertController>();
            comp._quiz = quiz;
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            // 低生命暗角：径向渐变精灵（中心透明→边缘泛红），只在屏幕四周泛红，
            // 中心区域始终清晰——不再用纯色全屏红盖住整个画面（那会让低血时画面
            // 发糊看不清）。raycast 关闭不挡操作。
            var vg = new GameObject("HpVignette", typeof(Image));
            vg.transform.SetParent(canvas, false);
            var vrt = vg.GetComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            vrt.offsetMin = Vector2.zero; vrt.offsetMax = Vector2.zero;
            _vignette = vg.GetComponent<Image>();
            _vignette.sprite = BuildVignetteSprite();
            _vignette.type = Image.Type.Simple;
            _vignette.color = new Color(1f, 1f, 1f, 0f);
            _vignette.raycastTarget = false;

            // 生命垂危弹窗
            _prompt = UiUtil.MakePanel(canvas, "CriticalPrompt", Vector2.zero,
                new Color(0.1f, 0.02f, 0.02f, 0.9f));
            var prt = _prompt.GetComponent<RectTransform>();
            prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
            prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;

            var title = UiUtil.MakeText(_prompt.transform, "Title", "⚠ 生命不足三成 ⚠", 52,
                TextAnchor.MiddleCenter, new Color(1f, 0.3f, 0.25f));
            UiUtil.SetRect(title, new Vector2(0.5f, 0.5f), new Vector2(0, 230), new Vector2(1200, 90));
            title.fontStyle = FontStyle.Bold;

            _promptDetail = UiUtil.MakeText(_prompt.transform, "Detail", "", 28,
                TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.85f));
            UiUtil.SetRect(_promptDetail, new Vector2(0.5f, 0.5f), new Vector2(0, 80), new Vector2(1400, 180));

            UiUtil.MakeButton(_prompt.transform, "休整答题 · 恢复能量", new Vector2(0.5f, 0.5f),
                new Vector2(-260, -130), new Vector2(460, 100),
                new Color(0.25f, 0.55f, 0.35f, 0.98f), OnAcceptRest, 30);
            UiUtil.MakeButton(_prompt.transform, "继续战斗", new Vector2(0.5f, 0.5f),
                new Vector2(260, -130), new Vector2(460, 100),
                new Color(0.5f, 0.28f, 0.24f, 0.98f), OnDeclineRest, 30);

            var hint = UiUtil.MakeText(_prompt.transform, "Hint",
                "休整不扣任何进度：答对回能量，答错也只讲依据。低谷不是身份，恢复也是任务的一部分。",
                20, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.5f));
            UiUtil.SetRect(hint, new Vector2(0.5f, 0.5f), new Vector2(0, -240), new Vector2(1500, 34));

            _prompt.SetActive(false);
        }

        void Update()
        {
            if (_player == null) _player = FindObjectOfType<PlayerController>();
            if (_player == null || _player.Stats == null) return;
            var s = _player.Stats;

            UpdateVignette(s);
            if (s.IsDead) { if (_promptOpen) ClosePrompt(false); return; }

            // ---- 分级字幕警告（迟滞回差防刷屏）----
            CheckPositive("hp", s.hp, s.maxHp, "生命", 0.35f,
                "生命偏低——注意闪避与格挡，可寻找恢复或休整答题。",
                "生命危险！再被击中几次就会倒下。");
            CheckPositive("will", s.will, s.maxWill, "意志", LowRatio,
                "意志偏低——低谷与压迫正在起效，别硬扛。",
                "意志告急！考虑休整答题或使用恢复机制。");
            CheckPositive("focus", s.focus, s.maxFocus, "专注", LowRatio,
                "专注偏低——噪声正在夺走注意力，锁定开始不稳。",
                "专注告急！先夺回注意力（定心格挡/注意力回收）。");
            CheckPositive("selfWorth", s.selfWorth, s.maxSelfWorth, "自尊", LowRatio,
                "自尊偏低——否定的话开始钻进心里。",
                "自尊告急！事实先于评价——去看清事实。");
            CheckPositive("boundary", s.boundary, s.maxBoundary, "边界", LowRatio,
                "边界偏低——索取与转嫁正在掏空你。",
                "边界告急！举盾拒绝，把不属于你的还回去。");
            CheckPositive("actionPower", s.actionPower, s.maxActionPower, "行动力", LowRatio,
                "行动力偏低——移动会开始变慢。",
                "行动力告急！五分钟火种——先动起来。");
            CheckNegative("rumination", s.rumination, s.maxRumination,
                "反刍偏高——同一个念头在反复回放。",
                "反刍告急！战后记得复盘归档，战斗中降反刍。");
            CheckNegative("relationshipDrain", s.relationshipDrain, s.maxRelationshipDrain,
                "关系消耗偏高——注意力与精力被持续索取。",
                "关系消耗告急！技能调息已变慢——守住边界。");

            // ---- 休整答题弹窗：唯一触发条件是生命 < 30% ----
            // 额外两道让路（不是新的触发条件，是"该弹也先别弹"）：
            // 言语攻防正在计时、或标题/暂停菜单开着的时候插一个模态弹窗，
            // 只会让玩家两个都做不成。
            if (_promptOpen || _quiz == null || _quiz.Active) return;
            if (Time.timeScale <= 0f) return;
            if (MainMenuPanel.AtTitle) return;
            if (VerbalDefenseController.Instance != null &&
                VerbalDefenseController.Instance.IsActive) return;
            if (s.hp / s.maxHp >= RestPromptRatio) return;
            if (Time.unscaledTime < _nextPromptAllowed) return;
            OpenPrompt(s);
        }

        // ===================== 分级警告 =====================

        void CheckPositive(string key, float cur, float max, string label, float lowAt,
            string lowMsg, string severeMsg)
        {
            float r = max > 0 ? cur / max : 1f;
            int level = _warnLevel.TryGetValue(key, out int l) ? l : 0;
            if (r < SevereRatio && level < 2)
            {
                _warnLevel[key] = 2;
                GameEvents.RaiseSubtitle("【" + label + "告急】" + severeMsg);
                GameAudio.Play(GameAudio.Sfx.Alert, 0.7f);
            }
            else if (r < lowAt && level < 1)
            {
                _warnLevel[key] = 1;
                GameEvents.RaiseSubtitle("【" + label + "偏低】" + lowMsg);
            }
            // 回差复位：回升到阈值+8% 以上才恢复正常档，允许下次再警告
            else if (level >= 2 && r > SevereRatio + Hysteresis) _warnLevel[key] = 1;
            else if (level >= 1 && r > lowAt + Hysteresis) _warnLevel[key] = 0;
        }

        void CheckNegative(string key, float cur, float max, string lowMsg, string severeMsg)
        {
            float r = max > 0 ? cur / max : 0f;
            int level = _warnLevel.TryGetValue(key, out int l) ? l : 0;
            if (r > 0.85f && level < 2)
            {
                _warnLevel[key] = 2;
                GameEvents.RaiseSubtitle("【警告】" + severeMsg);
                GameAudio.Play(GameAudio.Sfx.Alert, 0.7f);
            }
            else if (r > 0.70f && level < 1)
            {
                _warnLevel[key] = 1;
                GameEvents.RaiseSubtitle("【提醒】" + lowMsg);
            }
            else if (level >= 2 && r < 0.85f - Hysteresis) _warnLevel[key] = 1;
            else if (level >= 1 && r < 0.70f - Hysteresis) _warnLevel[key] = 0;
        }

        // ===================== 低生命罩屏脉冲 =====================

        void UpdateVignette(Player.PlayerStats s)
        {
            float r = s.maxHp > 0 ? s.hp / s.maxHp : 1f;
            float target = 0f;
            if (!s.IsDead && r < 0.25f)
            {
                // 边缘暗角（中心透明）→ 强度可以更明显而不糊住画面：
                // 越接近垂危脉冲越强，中心区域始终看得清
                float severity = Mathf.InverseLerp(0.25f, 0.05f, r);
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
                target = Mathf.Lerp(0.35f, 0.85f, severity) * (0.7f + 0.3f * pulse);
            }
            var c = _vignette.color;
            c.r = 0.85f; c.g = 0.05f; c.b = 0.05f;   // 红色泛边（径向 alpha 由精灵提供）
            c.a = Mathf.MoveTowards(c.a, target, Time.unscaledDeltaTime * 1.6f);
            _vignette.color = c;
        }

        /// <summary>径向暗角精灵：中心完全透明，向四周边缘平滑升到不透明——
        /// 只让屏幕边框泛红警示，中心画面保持清晰（大作低血提示的通行做法）。</summary>
        static Sprite _vignetteSprite;
        static Sprite BuildVignetteSprite()
        {
            if (_vignetteSprite != null) return _vignetteSprite;
            const int N = 128;
            // RGBA32 且 RGB=白：UI Image 用 color 着色时才能染成红色（Alpha8 会渲染成黑边）
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color[N * N];
            Vector2 c = new Vector2((N - 1) * 0.5f, (N - 1) * 0.5f);
            float maxD = c.magnitude;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), c) / maxD;
                    // 内 60% 半径完全透明；60%→100% 平滑升到 1（平方使边缘更收敛）
                    float a = Mathf.Clamp01((d - 0.6f) / 0.4f);
                    a = a * a;
                    px[y * N + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px);
            tex.Apply();
            _vignetteSprite = Sprite.Create(tex, new Rect(0, 0, N, N),
                new Vector2(0.5f, 0.5f), 100f);
            return _vignetteSprite;
        }

        // ===================== 生命垂危弹窗 =====================

        void OpenPrompt(Player.PlayerStats s)
        {
            _promptOpen = true;
            int hpPct = Mathf.RoundToInt(s.hp / s.maxHp * 100f);
            string imbalance = QuizSystem.ImbalanceLabel(s);
            _promptDetail.text =
                "当前生命仅剩 " + hpPct + "%（低于 30% 才会出现这个提示）——再打下去随时可能倒下。\n\n" +
                (string.IsNullOrEmpty(imbalance) ? "" : "当前状态：" + imbalance + "\n\n") +
                "是否暂停休整、进入答题补充能量？\n" +
                "（每答对 1 题：所有未满正能量 +20、负能量 −20）";
            _prompt.SetActive(true);
            _prompt.transform.SetAsLastSibling();
            Time.timeScale = 0f;
            GameAudio.Play(GameAudio.Sfx.Alert, 1f);
        }

        void OnAcceptRest()
        {
            ClosePrompt(true);
            _quiz.OpenEmergency();
        }

        void OnDeclineRest() => ClosePrompt(false);

        void ClosePrompt(bool acceptedRest)
        {
            _promptOpen = false;
            _prompt.SetActive(false);
            if (!acceptedRest)
            {
                Time.timeScale = 1f;
                GameEvents.RaiseSubtitle("继续战斗——" + (int)PromptCooldown + " 秒内不再提示。小心走位！");
            }
            // 接受休整时 timeScale 交由答题面板接管（打开置 0、结束恢复 1）
            else Time.timeScale = 1f;
            _nextPromptAllowed = Time.unscaledTime + PromptCooldown;
        }
    }
}
