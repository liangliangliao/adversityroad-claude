using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;

namespace AdversityRoad.Shame
{
    /// <summary>
    /// 第八章的前台（方案 8.3 HUD / 12.2 前台精简）。
    ///
    /// 【Exposure 不进常驻 HUD】
    /// 它只在**被有效注视时**出现，以视线图标 + 环形填充表现；
    /// 没人在看的时候整块消失（验收第 43 条）。底层数值照常运行并写进复盘页。
    ///
    /// 方案原文写的是"位于 SelfWorth 条上方"。实现落在左栏心法行**下方**：
    /// 心理小条是情境出现的，位置随亮起顺序浮动（见 HUDController 的情境条堆叠），
    /// 贴着它排会随时被顶开；固定在左栏下方反而始终在同一个地方，
    /// 而"只在被注视时出现"这条前台精简原则一字未改。
    ///
    /// 【身份钉与悬案计时器必须可读】
    /// 8.2 设计要点：本章禁止把心理机制做成纯数值暗箱——
    /// 钉子要看得见几枚、锁住了什么，计时器要看得见还剩多少。
    ///
    /// 【两枚章节技能按钮】
    /// 聚光灯校准（G）与不上庭（H）是本章语法的一部分，
    /// 触屏没有对应的核心键，所以在这里给它们一对屏上按钮。
    /// </summary>
    public class ShameHudOverlay : MonoBehaviour
    {
        public static ShameHudOverlay Instance { get; private set; }

        GameObject _root, _exposureGroup;
        Text _exposureText, _nailText, _timerText;
        RectTransform _exposureFill;
        Button _spotlightBtn, _refuseBtn;
        Text _spotlightLabel, _refuseLabel;

        public static ShameHudOverlay Ensure()
        {
            if (Instance != null) return Instance;
            var canvas = UiUtil.MainCanvas();
            if (canvas == null) return null;
            Instance = canvas.gameObject.AddComponent<ShameHudOverlay>();
            Instance.Build(canvas.transform);
            return Instance;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Build(Transform canvas)
        {
            // 【坐标全部按画布左上角算】
            // _root 铺满画布（anchorMin/Max 拉到对角、四边 offset 归零），
            // 于是下面每个子件的 anchoredPosition 就是"距画布左上角多少"——
            // 和 HUD 其他元素同一套口径，位置能逐个和现有面板核对。
            _root = new GameObject("ShameHud", typeof(RectTransform));
            _root.transform.SetParent(canvas, false);
            var rt = _root.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // 【为什么整块挂在左栏、且压到 -388 以下】
            // 左上角自上而下已经被占满：状态背板到 -226、意势点 -244、
            // 姿态按钮 -290、心法文字 -338（见 GameBootstrap.HudLayout）。
            // -380 往下才是空的，所以这一组从 -388 起排，不与任何既有元素重叠。
            const float Top = -388f;

            // ---- 暴露度：视线图标 + 环形填充（情境出现，不常驻）----
            _exposureGroup = new GameObject("Exposure", typeof(RectTransform));
            _exposureGroup.transform.SetParent(_root.transform, false);
            var ert = _exposureGroup.GetComponent<RectTransform>();
            ert.anchorMin = ert.anchorMax = new Vector2(0f, 1f);
            ert.pivot = new Vector2(0f, 0.5f);          // 用左边缘定位，坐标才好算
            ert.anchoredPosition = new Vector2(16f, Top);
            ert.sizeDelta = new Vector2(380f, 26f);

            var icon = UiUtil.MakeText(_exposureGroup.transform, "Icon", "◉", 22,
                TextAnchor.MiddleLeft, new Color(0.95f, 0.86f, 0.55f));
            UiUtil.SetRect(icon, new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(26f, 26f));

            var bg = new GameObject("ExposureBg", typeof(Image));
            bg.transform.SetParent(_exposureGroup.transform, false);
            bg.GetComponent<Image>().color = new Color(0.16f, 0.14f, 0.12f, 0.85f);
            // 条身 x∈[32,200]：图标右缘 27，留 5 的间隙
            UiUtil.SetRect(bg.GetComponent<Image>(), new Vector2(0f, 0.5f),
                new Vector2(116f, 0f), new Vector2(168f, 14f));

            var fill = new GameObject("ExposureFill", typeof(Image));
            fill.transform.SetParent(_exposureGroup.transform, false);
            fill.GetComponent<Image>().color = new Color(0.95f, 0.82f, 0.42f);
            _exposureFill = UiUtil.SetRect(fill.GetComponent<Image>(), new Vector2(0f, 0.5f),
                new Vector2(32f, 0f), new Vector2(0f, 14f));
            _exposureFill.pivot = new Vector2(0f, 0.5f);   // 从左往右长

            // 读数 x∈[215,365]：条身右缘 200 之后，不压在条上
            _exposureText = UiUtil.MakeText(_exposureGroup.transform, "ExposureText", "", 16,
                TextAnchor.MiddleLeft, new Color(0.92f, 0.88f, 0.78f));
            UiUtil.SetRect(_exposureText, new Vector2(0f, 0.5f),
                new Vector2(290f, 0f), new Vector2(150f, 22f));

            // ---- 身份钉：钉了几枚、锁住了什么（8.2 要求可读，不做暗箱）----
            _nailText = UiUtil.MakeText(_root.transform, "Nails", "", 17,
                TextAnchor.MiddleLeft, new Color(0.85f, 0.8f, 0.76f));
            UiUtil.SetRect(_nailText, new Vector2(0f, 1f), new Vector2(226f, Top - 30f),
                new Vector2(420f, 22f));

            // ---- 悬案计时器：替代 Boss 血条的那根读数 ----
            _timerText = UiUtil.MakeText(_root.transform, "CaseTimer", "", 17,
                TextAnchor.MiddleLeft, new Color(0.88f, 0.85f, 0.95f));
            UiUtil.SetRect(_timerText, new Vector2(0f, 1f), new Vector2(226f, Top - 56f),
                new Vector2(420f, 22f));

            // ---- 两枚章节技能：触屏没有对应核心键，给它们屏上按钮 ----
            // 按钮 1 x∈[12,208]，按钮 2 x∈[236,396]：中间留 28 的间隙
            _spotlightBtn = UiUtil.MakeButton(_root.transform, "聚光灯校准 G", new Vector2(0f, 1f),
                new Vector2(110f, Top - 96f), new Vector2(196f, 46f),
                new Color(0.26f, 0.28f, 0.36f, 0.92f), OnSpotlight, 18);
            _spotlightLabel = _spotlightBtn.GetComponentInChildren<Text>();

            _refuseBtn = UiUtil.MakeButton(_root.transform, "不上庭 H", new Vector2(0f, 1f),
                new Vector2(316f, Top - 96f), new Vector2(160f, 46f),
                new Color(0.3f, 0.26f, 0.3f, 0.92f), OnRefuse, 18);
            _refuseLabel = _refuseBtn.GetComponentInChildren<Text>();

            // ---- 本关规则：随时能把规则卡叫回来 ----
            // 进关那张卡只自动弹一次，但"这关到底要我干什么"是随时可能忘的。
            // 按钮 3 x∈[428,628]：接在「不上庭」右缘 396 之后，留 32 的间隙。
            UiUtil.MakeButton(_root.transform, "本关规则", new Vector2(0f, 1f),
                new Vector2(528f, Top - 96f), new Vector2(200f, 46f),
                new Color(0.24f, 0.3f, 0.3f, 0.92f), OnRules, 18);

            _root.SetActive(false);
        }

        void OnRules()
        {
            string id = ShameLine.CurrentLevelId;
            if (!string.IsNullOrEmpty(id)) ShameBriefPanel.Show(id);
        }

        void OnSpotlight() { var s = ShameSkills.Instance; if (s != null) s.CastSpotlight(); }
        void OnRefuse() { var s = ShameSkills.Instance; if (s != null) s.CastRefuse(); }

        float _nextTick;

        void Update()
        {
            if (_root == null) return;
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + 0.1f;

            bool inChapter = ShameLine.InChapter;
            if (_root.activeSelf != inChapter) _root.SetActive(inChapter);
            if (!inChapter) return;

            var exposure = ExposureSystem.Instance;
            // 情境出现：只有真的被看着（或者已经显形）时才显示这一组
            bool showExposure = exposure != null &&
                (exposure.RecentlyGazed || exposure.Revealed || exposure.Value > 0.5f);
            if (_exposureGroup.activeSelf != showExposure) _exposureGroup.SetActive(showExposure);
            if (showExposure && exposure != null)
            {
                _exposureFill.sizeDelta = new Vector2(168f * exposure.Ratio, 14f);
                _exposureFill.GetComponent<Image>().color = exposure.Revealed
                    ? new Color(0.95f, 0.45f, 0.35f)
                    : exposure.Value >= 60f ? new Color(0.95f, 0.68f, 0.35f)
                                            : new Color(0.95f, 0.82f, 0.42f);
                _exposureText.text = "暴露 " + Mathf.RoundToInt(exposure.Value) +
                    (exposure.Revealed ? "　· 显形" :
                     exposure.SteadyInCone ? "　· 锥内稳态" :
                     exposure.Value >= 85f ? "　· 自尊伤害 ×2.0" :
                     exposure.Value >= 60f ? "　· 自尊伤害 ×1.5" : "");
            }

            var nails = IdentityNailSystem.Instance;
            _nailText.text = nails == null || nails.Count == 0
                ? "" : "身份钉 " + nails.Count + "/" + IdentityNailSystem.MaxNails +
                       "　被钉住：" + nails.LockSummary() + "（认领不终审一次拔全部）";

            var timer = PendingCaseTimer.Instance;
            _timerText.text = timer != null ? timer.HudLine() : "";

            var skills = ShameSkills.Instance;
            if (skills != null)
            {
                if (_spotlightLabel != null)
                    _spotlightLabel.text = skills.SpotlightReady ? "聚光灯校准 G"
                        : "校准 " + Mathf.CeilToInt(skills.SpotlightCooldownLeft) + "s";
                if (_refuseLabel != null)
                    _refuseLabel.text = skills.RefuseArmed ? "不上庭 · 已举起"
                        : skills.RefuseReady ? "不上庭 H"
                        : "不上庭 " + Mathf.CeilToInt(skills.RefuseCooldownLeft) + "s";
            }
        }
    }
}
