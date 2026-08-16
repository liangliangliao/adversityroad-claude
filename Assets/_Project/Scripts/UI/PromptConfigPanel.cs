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
        Text _modelHint;
        readonly List<Button> _presetButtons = new List<Button>();

        /// <summary>
        /// 各提供商**当前可用**的模型名格式与常用型号。
        ///
        /// 三家的命名规则并不一样，这是玩家最容易踩空的地方：
        ///   · DeepSeek 直连：不带厂商前缀，就叫 deepseek-chat；
        ///   · OpenRouter：必须是 `厂商/型号`，型号要和它目录里的完全一致；
        ///   · EdenAI 的 llm/chat：同样是 `厂商/型号`，但目录比 OpenRouter 小得多。
        /// 写错的后果不是提示"模型不存在"就完事——OpenRouter 上不存在的名字会一直挂到超时。
        /// </summary>
        static string[] PresetsFor(string provider)
        {
            switch (provider)
            {
                case "deepseek":
                    return new[] { "deepseek-chat", "deepseek-reasoner", "" };
                case "edenai":
                    return new[] { "openai/gpt-4o-mini", "anthropic/claude-3-5-sonnet-latest", "google/gemini-1.5-flash" };
                default: // openrouter
                    return new[] { "deepseek/deepseek-chat", "openai/gpt-4o-mini", "anthropic/claude-3.5-sonnet" };
            }
        }

        static string HintFor(string provider)
        {
            switch (provider)
            {
                case "deepseek":
                    return "格式：型号名，不带厂商前缀（默认 deepseek-chat）";
                case "edenai":
                    return "格式：厂商/型号，必须在 EdenAI llm/chat 目录内（默认 openai/gpt-4o-mini）";
                default:
                    return "格式：厂商/型号，须与 OpenRouter 目录完全一致（默认 deepseek/deepseek-chat）";
            }
        }

        void UsePreset(int slot)
        {
            var cfg = AIPromptConfig.Load();
            var presets = PresetsFor(string.IsNullOrEmpty(_provider) ? cfg.provider : _provider);
            if (slot < 0 || slot >= presets.Length) return;
            _modelInput.text = presets[slot];
        }

        void RefreshModelHints(string provider)
        {
            if (_modelHint != null) _modelHint.text = HintFor(provider);
            var presets = PresetsFor(provider);
            for (int i = 0; i < _presetButtons.Count && i < presets.Length; i++)
            {
                var label = _presetButtons[i].GetComponentInChildren<Text>();
                if (label == null) continue;
                bool empty = string.IsNullOrEmpty(presets[i]);
                label.text = empty ? "（用默认）" : presets[i];
                _presetButtons[i].interactable = true;
            }
        }

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
            _modelInput = UiUtil.MakeInput(_panel.transform, "留空即用该提供商的默认模型",
                new Vector2(0.5f, 1f), new Vector2(60, -566), new Vector2(1020, 58), false);

            // 模型名写错是最难自查的一类失败：EdenAI 回的是
            // 「Model 'xai/grok-4.5' in llm/chat does not exist」，OpenRouter 上不存在的模型
            // 则直接挂到超时。与其让玩家凭记忆敲，不如把每家的**正确格式**摆出来点一下就填。
            _modelHint = UiUtil.MakeText(_panel.transform, "MHint", "", 19,
                TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.5f, 0.85f));
            UiUtil.SetRect(_modelHint, new Vector2(0.5f, 1f), new Vector2(60, -604), new Vector2(1020, 28));

            for (int i = 0; i < 3; i++)
            {
                int slot = i;
                var pb = UiUtil.MakeButton(_panel.transform, "", new Vector2(0.5f, 1f),
                    new Vector2(-360 + i * 350, -640), new Vector2(330, 50),
                    new Color(0.22f, 0.3f, 0.36f, 0.95f), () => UsePreset(slot), 20);
                _presetButtons.Add(pb);
            }

            var kLabel = UiUtil.MakeText(_panel.transform, "KLabel", "API Key", 24,
                TextAnchor.MiddleLeft, Color.white);
            UiUtil.SetRect(kLabel, new Vector2(0.5f, 1f), new Vector2(-540, -702), new Vector2(120, 34));
            _keyInput = UiUtil.MakeInput(_panel.transform, "仅保存在本机，可随时删除",
                new Vector2(0.5f, 1f), new Vector2(60, -702), new Vector2(1020, 58), false);

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
            RefreshModelHints(_provider);   // 换提供商即换格式提示与预设按钮
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
                case "job": return 3;
                case "plaza": return 4;
                case "court": return 5;
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
