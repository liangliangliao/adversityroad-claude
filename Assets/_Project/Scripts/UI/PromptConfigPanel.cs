using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;
using AdversityRoad.World;

namespace AdversityRoad.UI
{
    /// <summary>
    /// AI 台词配置面板：
    /// - 提示词：全局 + 当前场景（有默认模板，可修改）；
    /// - 云端 LLM：OpenRouter / DeepSeek / EdenAI，API Key 与模型可配置；
    /// - 延迟方案：台词池后台预取，游戏内即取即用（见 CloudDialogueService）。
    /// </summary>
    public class PromptConfigPanel : MonoBehaviour
    {
        GameObject _panel;
        InputField _globalInput;
        InputField _sceneInput;
        InputField _modelInput;
        InputField _keyInput;
        Text _sceneLabel;
        Button _cloudToggle;
        readonly List<(Button btn, string provider)> _providerButtons = new List<(Button, string)>();

        bool _useCloud;
        string _provider = "openrouter";

        static readonly Color Off = new Color(0.25f, 0.25f, 0.3f, 0.95f);
        static readonly Color On = new Color(0.2f, 0.55f, 0.35f, 0.95f);

        public static PromptConfigPanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<PromptConfigPanel>();
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "PromptPanel", new Vector2(1240, 940),
                new Color(0.08f, 0.08f, 0.12f, 0.97f));

            var title = UiUtil.MakeText(_panel.transform, "Title", "AI 台词 · 提示词与模型", 38,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -40), new Vector2(900, 50));

            // ---- 提示词 ----
            var gLabel = UiUtil.MakeText(_panel.transform, "GLabel", "全局提示词（敌人恶意低语的生成方向）", 24,
                TextAnchor.MiddleLeft, Color.white);
            UiUtil.SetRect(gLabel, new Vector2(0.5f, 1f), new Vector2(-40, -86), new Vector2(1100, 34));
            _globalInput = UiUtil.MakeInput(_panel.transform, "留空则用默认模板",
                new Vector2(0.5f, 1f), new Vector2(0, -172), new Vector2(1160, 130), true);

            _sceneLabel = UiUtil.MakeText(_panel.transform, "SLabel", "当前场景提示词", 24,
                TextAnchor.MiddleLeft, Color.white);
            UiUtil.SetRect(_sceneLabel, new Vector2(0.5f, 1f), new Vector2(-40, -258), new Vector2(1100, 34));
            _sceneInput = UiUtil.MakeInput(_panel.transform, "只在当前区域生效",
                new Vector2(0.5f, 1f), new Vector2(0, -344), new Vector2(1160, 130), true);

            // ---- 云端 LLM ----
            var cloudLabel = UiUtil.MakeText(_panel.transform, "CLabel",
                "云端生成（后台预取台词池，即取即用，无网络时自动回退本地模板）", 24,
                TextAnchor.MiddleLeft, new Color(0.7f, 0.9f, 1f));
            UiUtil.SetRect(cloudLabel, new Vector2(0.5f, 1f), new Vector2(-40, -434), new Vector2(1100, 34));

            _cloudToggle = UiUtil.MakeButton(_panel.transform, "云端生成：关", new Vector2(0.5f, 1f),
                new Vector2(-440, -494), new Vector2(280, 62), Off, ToggleCloud, 24);

            string[] providers = { "openrouter", "deepseek", "edenai" };
            string[] providerNames = { "OpenRouter", "DeepSeek", "EdenAI" };
            for (int i = 0; i < providers.Length; i++)
            {
                string p = providers[i];
                var btn = UiUtil.MakeButton(_panel.transform, providerNames[i], new Vector2(0.5f, 1f),
                    new Vector2(-150 + i * 250, -494), new Vector2(230, 62), Off,
                    () => SelectProvider(p), 24);
                _providerButtons.Add((btn, p));
            }

            var mLabel = UiUtil.MakeText(_panel.transform, "MLabel", "模型", 24,
                TextAnchor.MiddleLeft, Color.white);
            UiUtil.SetRect(mLabel, new Vector2(0.5f, 1f), new Vector2(-540, -566), new Vector2(100, 34));
            _modelInput = UiUtil.MakeInput(_panel.transform,
                "留空用默认：deepseek/deepseek-chat | deepseek-chat | openai/gpt-4o-mini",
                new Vector2(0.5f, 1f), new Vector2(60, -566), new Vector2(1020, 58), false);

            var kLabel = UiUtil.MakeText(_panel.transform, "KLabel", "API Key", 24,
                TextAnchor.MiddleLeft, Color.white);
            UiUtil.SetRect(kLabel, new Vector2(0.5f, 1f), new Vector2(-540, -640), new Vector2(120, 34));
            _keyInput = UiUtil.MakeInput(_panel.transform, "仅保存在本机，可随时删除",
                new Vector2(0.5f, 1f), new Vector2(60, -640), new Vector2(1020, 58), false);

            var note = UiUtil.MakeText(_panel.transform, "Note",
                "台词生成有安全约束：只输出抽象心理施压短句，不含真实人名/地名/可操作话术",
                20, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.45f));
            UiUtil.SetRect(note, new Vector2(0.5f, 0f), new Vector2(0, 152), new Vector2(1160, 32));

            UiUtil.MakeButton(_panel.transform, "保存", new Vector2(0.5f, 0f), new Vector2(-130, 78),
                new Vector2(230, 78), new Color(0.2f, 0.55f, 0.3f, 0.95f), Save, 30);
            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f), new Vector2(130, 78),
                new Vector2(230, 78), new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 30);

            _panel.SetActive(false);
        }

        void ToggleCloud()
        {
            _useCloud = !_useCloud;
            RefreshCloudUi();
        }

        void SelectProvider(string p)
        {
            _provider = p;
            RefreshCloudUi();
        }

        void RefreshCloudUi()
        {
            _cloudToggle.GetComponent<Image>().color = _useCloud ? On : Off;
            _cloudToggle.GetComponentInChildren<Text>().text = _useCloud ? "云端生成：开" : "云端生成：关";
            foreach (var (btn, p) in _providerButtons)
                btn.GetComponent<Image>().color = p == _provider ? On : Off;
        }

        void Save()
        {
            var cfg = AIPromptConfig.Load();
            cfg.globalPrompt = _globalInput.text;
            cfg.SetScenePrompt(ZoneBuilder.CurrentZoneId, _sceneInput.text);
            cfg.useCloud = _useCloud;
            cfg.provider = _provider;
            cfg.model = _modelInput.text.Trim();
            cfg.apiKey = _keyInput.text.Trim();
            cfg.Save();

            if (CloudDialogueService.Instance != null)
            {
                CloudDialogueService.Instance.ClearPools();
                CloudDialogueService.Instance.WarmUp(ZoneBuilder.CurrentZoneId,
                    Personalization.WeaknessAxis.Procrastination,
                    Personalization.WeaknessAxis.SelfDoubt,
                    Personalization.WeaknessAxis.NoiseSensitivity);
            }

            GameEvents.RaiseSubtitle(_useCloud
                ? "已保存。云端台词池正在后台预取……"
                : "已保存。使用本地台词模板。");
            Hide();
        }

        public void Toggle()
        {
            if (_panel.activeSelf) { Hide(); return; }
            var cfg = AIPromptConfig.Load();
            _globalInput.text = cfg.globalPrompt;
            _sceneInput.text = cfg.GetScenePrompt(ZoneBuilder.CurrentZoneId);
            _modelInput.text = cfg.model;
            _keyInput.text = cfg.apiKey;
            _useCloud = cfg.useCloud;
            _provider = string.IsNullOrEmpty(cfg.provider) ? "openrouter" : cfg.provider;
            _sceneLabel.text = "当前场景提示词（" + ZoneBuilder.ZoneNameOf(ZoneIndex()) + "）";
            RefreshCloudUi();
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            Time.timeScale = 0f;
        }

        static int ZoneIndex()
        {
            switch (ZoneBuilder.CurrentZoneId)
            {
                case "dojo": return 1;
                case "street": return 2;
                case "plaza": return 3;
                default: return 0;
            }
        }

        void Hide()
        {
            _panel.SetActive(false);
            Time.timeScale = 1f;
        }
    }
}
