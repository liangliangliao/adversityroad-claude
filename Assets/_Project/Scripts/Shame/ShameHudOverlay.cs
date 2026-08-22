using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;

namespace AdversityRoad.Shame
{
    /// <summary>
    /// 第八章的前台（方案 8.3 HUD / 12.2 前台精简）。
    ///
    /// 【Exposure 不进常驻 HUD】
    /// 它只在**被有效注视时**出现，位于自尊条上方，以视线图标 + 环形填充表现；
    /// 没人在看的时候整块消失（验收第 43 条）。底层数值照常运行并写进复盘页。
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
            var canvas = FindObjectOfType<Canvas>();
            if (canvas == null) return null;
            Instance = canvas.gameObject.AddComponent<ShameHudOverlay>();
            Instance.Build(canvas.transform);
            return Instance;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Build(Transform canvas)
        {
            _root = new GameObject("ShameHud", typeof(RectTransform));
            _root.transform.SetParent(canvas, false);
            var rt = _root.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(420f, 260f);

            // ---- 暴露度：视线图标 + 环形（这里用一条短横向填充条近似环形填充）----
            _exposureGroup = new GameObject("Exposure", typeof(RectTransform));
            _exposureGroup.transform.SetParent(_root.transform, false);
            var ert = _exposureGroup.GetComponent<RectTransform>();
            ert.anchorMin = ert.anchorMax = new Vector2(0f, 1f);
            ert.anchoredPosition = new Vector2(16f, -252f);
            ert.sizeDelta = new Vector2(360f, 26f);

            var icon = UiUtil.MakeText(_exposureGroup.transform, "Icon", "◉", 22,
                TextAnchor.MiddleLeft, new Color(0.95f, 0.86f, 0.55f));
            UiUtil.SetRect(icon, new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(26f, 26f));

            var bg = new GameObject("ExposureBg", typeof(Image));
            bg.transform.SetParent(_exposureGroup.transform, false);
            bg.GetComponent<Image>().color = new Color(0.16f, 0.14f, 0.12f, 0.85f);
            UiUtil.SetRect(bg.GetComponent<Image>(), new Vector2(0f, 0.5f),
                new Vector2(32f + 84f, 0f), new Vector2(168f, 14f));

            var fill = new GameObject("ExposureFill", typeof(Image));
            fill.transform.SetParent(_exposureGroup.transform, false);
            fill.GetComponent<Image>().color = new Color(0.95f, 0.82f, 0.42f);
            _exposureFill = UiUtil.SetRect(fill.GetComponent<Image>(), new Vector2(0f, 0.5f),
                new Vector2(32f, 0f), new Vector2(0f, 14f));
            _exposureFill.pivot = new Vector2(0f, 0.5f);

            _exposureText = UiUtil.MakeText(_exposureGroup.transform, "ExposureText", "", 16,
                TextAnchor.MiddleLeft, new Color(0.92f, 0.88f, 0.78f));
            UiUtil.SetRect(_exposureText, new Vector2(0f, 0.5f), new Vector2(206f, 0f),
                new Vector2(160f, 22f));

            // ---- 身份钉 ----
            _nailText = UiUtil.MakeText(_root.transform, "Nails", "", 17,
                TextAnchor.MiddleLeft, new Color(0.85f, 0.8f, 0.76f));
            UiUtil.SetRect(_nailText, new Vector2(0f, 1f), new Vector2(190f, -278f),
                new Vector2(360f, 22f));

            // ---- 悬案计时器 ----
            _timerText = UiUtil.MakeText(_root.transform, "CaseTimer", "", 17,
                TextAnchor.MiddleLeft, new Color(0.88f, 0.85f, 0.95f));
            UiUtil.SetRect(_timerText, new Vector2(0f, 1f), new Vector2(210f, -302f),
                new Vector2(400f, 22f));

            // ---- 两枚章节技能 ----
            _spotlightBtn = UiUtil.MakeButton(_root.transform, "聚光灯校准 G", new Vector2(0f, 1f),
                new Vector2(112f, -334f), new Vector2(196f, 46f),
                new Color(0.26f, 0.28f, 0.36f, 0.92f), OnSpotlight, 18);
            _spotlightLabel = _spotlightBtn.GetComponentInChildren<Text>();

            _refuseBtn = UiUtil.MakeButton(_root.transform, "不上庭 H", new Vector2(0f, 1f),
                new Vector2(316f, -334f), new Vector2(160f, 46f),
                new Color(0.3f, 0.26f, 0.3f, 0.92f), OnRefuse, 18);
            _refuseLabel = _refuseBtn.GetComponentInChildren<Text>();

            _root.SetActive(false);
        }

        void OnSpotlight() { var s = ShameSkills.Instance; if (s != null) s.CastSpotlight(); }
        void OnRefuse() { var s = ShameSkills.Instance; if (s != null) s.CastRefuse(); }

        float _nextTick;

        void Update()
        {
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
