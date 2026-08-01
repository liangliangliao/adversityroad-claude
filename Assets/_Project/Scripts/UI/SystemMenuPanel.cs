using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 系统菜单（把散落在右上角的十三个功能按钮收进来）。
    ///
    /// 原先十三个按钮直接平铺在 HUD 右上角三行，横向一直排到屏幕中线，
    /// 正好压在顶部的主线任务文字上——「界面很乱、文字重叠」最主要的一处。
    /// 大型动作游戏的做法是：HUD 上只留【一个】入口，功能全部收进菜单页。
    /// 这里照办：右上角只剩一枚「≡ 菜单」，点开是分组的功能页。
    ///
    /// 分组按使用频率与性质划分，而不是按实现顺序堆叠：
    ///   养成 / 探索 / 角色 / 系统 / 调试——调试项单独一组放最后，
    ///   正式玩的时候视线不会被它们干扰。
    /// </summary>
    public class SystemMenuPanel : MonoBehaviour
    {
        /// <summary>一个菜单项：分组名 + 标签 + 点开后要做什么。</summary>
        public struct Entry
        {
            public string group, label;
            public System.Action action;
        }

        readonly List<Entry> _entries = new List<Entry>();
        GameObject _panel;
        bool _built;

        public static SystemMenuPanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<SystemMenuPanel>();
            comp._canvas = canvas;
            return comp;
        }

        Transform _canvas;

        /// <summary>登记一个菜单项（必须在 Build 之前全部登记完）。</summary>
        public void Add(string group, string label, System.Action action) =>
            _entries.Add(new Entry { group = group, label = label, action = action });

        // 分组顺序：常用在前，调试在最后
        static readonly string[] GroupOrder = { "养成", "探索", "角色", "系统", "调试" };

        static readonly Dictionary<string, Color> GroupColor = new Dictionary<string, Color>
        {
            { "养成", new Color(0.42f, 0.34f, 0.20f, 0.96f) },
            { "探索", new Color(0.20f, 0.34f, 0.45f, 0.96f) },
            { "角色", new Color(0.32f, 0.24f, 0.40f, 0.96f) },
            { "系统", new Color(0.26f, 0.28f, 0.33f, 0.96f) },
            { "调试", new Color(0.34f, 0.22f, 0.22f, 0.96f) },
        };

        public void Build()
        {
            if (_built) return;
            _built = true;

            _panel = UiUtil.MakePanel(_canvas, "SystemMenuPanel", new Vector2(1000, 880),
                new Color(0.06f, 0.07f, 0.10f, 0.985f));

            var title = UiUtil.MakeText(_panel.transform, "Title", "菜 单", 40,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -48), new Vector2(500, 56));

            // 分组渲染：每组一行标题 + 该组按钮按三列铺开
            float y = -110f;
            foreach (var g in GroupOrder)
            {
                var inGroup = _entries.FindAll(e => e.group == g);
                if (inGroup.Count == 0) continue;

                var head = UiUtil.MakeText(_panel.transform, "G_" + g, g, 24,
                    TextAnchor.MiddleLeft, new Color(0.72f, 0.78f, 0.88f));
                UiUtil.SetRect(head, new Vector2(0f, 1f), new Vector2(220, y), new Vector2(400, 30));
                y -= 40f;

                for (int i = 0; i < inGroup.Count; i++)
                {
                    var e = inGroup[i];
                    int col = i % 3;
                    int row = i / 3;
                    var col4 = GroupColor.TryGetValue(g, out var c)
                        ? c : new Color(0.25f, 0.27f, 0.32f, 0.96f);
                    UiUtil.MakeButton(_panel.transform, e.label, new Vector2(0f, 1f),
                        new Vector2(180 + col * 310, y - row * 78), new Vector2(290, 64),
                        col4, () => { Hide(); e.action?.Invoke(); }, 25);
                }
                y -= ((inGroup.Count + 2) / 3) * 78f + 14f;
            }

            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f),
                new Vector2(0, 48), new Vector2(280, 70),
                new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 26);

            _panel.SetActive(false);
        }

        public void Toggle()
        {
            if (_panel == null) return;
            if (_panel.activeSelf) { Hide(); return; }
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            Time.timeScale = 0f;
        }

        void Hide()
        {
            if (_panel != null) _panel.SetActive(false);
            Time.timeScale = 1f;
        }
    }
}
