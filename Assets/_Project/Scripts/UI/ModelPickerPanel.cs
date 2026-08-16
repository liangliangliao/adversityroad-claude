using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 模型选择器：把所选提供商**当前真实可用**的模型列出来，任选其一。
    ///
    /// 玩家的原话是"选择 openrouter 那么调用查询其所有可用模型列表显示，
    /// 用户任意选择其中一个"。OpenRouter 一家就有三百多个模型，所以列表必须
    /// 能筛、能翻页——一次糊 300 个按钮在屏幕上等于没列。
    ///
    /// 拉取失败时不假装成功：把失败原因原样写在面板上，同时给出内置的几个常用型号
    /// 作为退路（没网、Key 没填、目录接口改版时仍然能把游戏配起来）。
    /// </summary>
    public class ModelPickerPanel : MonoBehaviour
    {
        const int Cols = 2, Rows = 8;
        const int PageSize = Cols * Rows;

        GameObject _panel;
        Text _title, _status;
        InputField _filter;
        Text _pageText;
        readonly List<GameObject> _cells = new List<GameObject>();

        readonly List<string> _all = new List<string>();
        readonly List<string> _view = new List<string>();
        int _page;
        bool _loading;

        string _provider = "openrouter";
        string _key = "";
        Action<string> _onPick;

        public static ModelPickerPanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<ModelPickerPanel>();
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "ModelPickerPanel", new Vector2(1240, 940),
                new Color(0.07f, 0.08f, 0.11f, 0.985f));

            _title = UiUtil.MakeText(_panel.transform, "Title", "选择模型", 34,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(_title, new Vector2(0.5f, 1f), new Vector2(0, -40), new Vector2(1100, 46));

            _status = UiUtil.MakeText(_panel.transform, "Status", "", 20,
                TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.5f, 0.9f));
            UiUtil.SetRect(_status, new Vector2(0.5f, 1f), new Vector2(0, -84), new Vector2(1160, 30));

            var fLabel = UiUtil.MakeText(_panel.transform, "FLabel", "筛选", 22,
                TextAnchor.MiddleLeft, Color.white);
            UiUtil.SetRect(fLabel, new Vector2(0.5f, 1f), new Vector2(-560, -126), new Vector2(80, 32));
            _filter = UiUtil.MakeInput(_panel.transform, "输入关键字，例如 claude / gpt / free",
                new Vector2(0.5f, 1f), new Vector2(40, -126), new Vector2(940, 54), false);
            _filter.onValueChanged.AddListener(_ => { _page = 0; Refresh(); });

            UiUtil.MakeButton(_panel.transform, "重新拉取", new Vector2(0.5f, 1f),
                new Vector2(550, -126), new Vector2(160, 54),
                new Color(0.22f, 0.3f, 0.36f, 0.95f), () => Load(), 20);

            _pageText = UiUtil.MakeText(_panel.transform, "PageText", "", 22,
                TextAnchor.MiddleCenter, new Color(0.85f, 0.88f, 0.95f));
            UiUtil.SetRect(_pageText, new Vector2(0.5f, 0f), new Vector2(0, 158), new Vector2(400, 32));

            UiUtil.MakeButton(_panel.transform, "上一页", new Vector2(0.5f, 0f), new Vector2(-380, 78),
                new Vector2(200, 74), new Color(0.24f, 0.26f, 0.32f, 0.95f), () => Turn(-1), 26);
            UiUtil.MakeButton(_panel.transform, "下一页", new Vector2(0.5f, 0f), new Vector2(-160, 78),
                new Vector2(200, 74), new Color(0.24f, 0.26f, 0.32f, 0.95f), () => Turn(1), 26);
            UiUtil.MakeButton(_panel.transform, "用默认模型", new Vector2(0.5f, 0f), new Vector2(120, 78),
                new Vector2(240, 74), new Color(0.28f, 0.34f, 0.26f, 0.95f), () => Pick(""), 24);
            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f), new Vector2(380, 78),
                new Vector2(200, 74), new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 26);

            _panel.SetActive(false);
        }

        /// <summary>打开选择器：立刻发起一次拉取。</summary>
        public void Open(string provider, string apiKey, Action<string> onPick)
        {
            _provider = string.IsNullOrEmpty(provider) ? "openrouter" : provider;
            _key = apiKey ?? "";
            _onPick = onPick;
            _title.text = "选择模型 · " + DisplayName(_provider);
            _filter.text = "";
            _page = 0;
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            Load();
        }

        static string DisplayName(string p) =>
            p == "deepseek" ? "DeepSeek" : p == "edenai" ? "EdenAI" : "OpenRouter";

        /// <summary>拉不到时的退路：几个各家一直都在的型号，格式也是对的。</summary>
        static string[] FallbackFor(string provider)
        {
            switch (provider)
            {
                case "deepseek":
                    return new[] { "deepseek-chat", "deepseek-reasoner" };
                case "edenai":
                    return new[] { "openai/gpt-4o-mini", "anthropic/claude-3-5-sonnet-latest",
                                   "google/gemini-1.5-flash" };
                default:
                    return new[] { "deepseek/deepseek-chat", "openai/gpt-4o-mini",
                                   "anthropic/claude-3.5-sonnet", "google/gemini-flash-1.5" };
            }
        }

        void Load()
        {
            if (_loading) return;
            _loading = true;
            _all.Clear();
            _status.text = "正在向 " + DisplayName(_provider) + " 查询可用模型……";
            Refresh();
            StartCoroutine(ModelCatalogService.Fetch(_provider, _key, (ids, err) =>
            {
                _loading = false;
                if (ids != null && ids.Count > 0)
                {
                    _all.AddRange(ids);
                    _status.text = "共 " + ids.Count + " 个可用模型——点一个即选中";
                }
                else
                {
                    _all.AddRange(FallbackFor(_provider));
                    _status.text = "没能取到完整列表（" + err + "）——下面是内置的常用型号";
                }
                _page = 0;
                Refresh();
            }));
        }

        void Turn(int delta)
        {
            int pages = Mathf.Max(1, Mathf.CeilToInt(_view.Count / (float)PageSize));
            _page = Mathf.Clamp(_page + delta, 0, pages - 1);
            Refresh();
        }

        void Refresh()
        {
            foreach (var c in _cells) if (c != null) { c.SetActive(false); Destroy(c); }
            _cells.Clear();

            _view.Clear();
            string f = (_filter != null ? _filter.text : "").Trim().ToLowerInvariant();
            foreach (var id in _all)
                if (f.Length == 0 || id.ToLowerInvariant().Contains(f)) _view.Add(id);

            int pages = Mathf.Max(1, Mathf.CeilToInt(_view.Count / (float)PageSize));
            _page = Mathf.Clamp(_page, 0, pages - 1);
            _pageText.text = _view.Count == 0
                ? (_loading ? "加载中……" : "没有匹配的模型")
                : "第 " + (_page + 1) + " / " + pages + " 页 · 共 " + _view.Count + " 个";

            for (int i = 0; i < PageSize; i++)
            {
                int idx = _page * PageSize + i;
                if (idx >= _view.Count) break;
                string id = _view[idx];
                int col = i % Cols, row = i / Cols;
                var btn = UiUtil.MakeButton(_panel.transform, "", new Vector2(0.5f, 1f),
                    new Vector2(-290 + col * 580, -196 - row * 64), new Vector2(560, 58),
                    new Color(0.2f, 0.26f, 0.32f, 0.95f), () => Pick(id), 19);
                var label = btn.GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.text = id;
                    label.alignment = TextAnchor.MiddleLeft;
                    label.horizontalOverflow = HorizontalWrapMode.Wrap;
                    var lrt = label.GetComponent<RectTransform>();
                    lrt.offsetMin = new Vector2(14, 2);
                    lrt.offsetMax = new Vector2(-10, -2);
                }
                _cells.Add(btn.gameObject);
            }
        }

        void Pick(string id)
        {
            _onPick?.Invoke(id);
            Hide();
        }

        void Hide()
        {
            // 不动 timeScale：这块面板是叠在配置面板上打开的，配置面板还开着
            if (_panel != null) _panel.SetActive(false);
        }
    }
}
