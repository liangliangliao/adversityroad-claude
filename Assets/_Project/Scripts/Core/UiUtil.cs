using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AdversityRoad.Core
{
    /// <summary>运行时 UI 构建工具：字体、文本、面板、按钮、输入框。</summary>
    public static class UiUtil
    {
        /// <summary>
        /// 主界面画布（HUD_Canvas）。
        ///
        /// 【为什么不能用 FindObjectOfType&lt;Canvas&gt;()】
        /// 场上同时存在很多 Canvas：HUD、触屏操作层、性能读数、目标板，
        /// 以及**每一个敌人头顶都有一个世界空间 Canvas**（EnemyStatusBar，
        /// localScale 0.012）。FindObjectOfType 返回的是任意一个，
        /// 战场上有敌人时极可能抽中某个敌人的头顶血条。
        /// 面板于是被挂到那块 1.2% 缩放的世界空间画布上——代码全跑通了、
        /// 面板也真的建出来了，但它在敌人脑袋上只有几个像素大，玩家看到的是
        /// "按了没反应"。第八章的欠条台、抽屉、陈述面板、规则卡全踩了这一条。
        ///
        /// 这里按名字与渲染模式确定地取那一个：屏幕空间 + 名字为 HUD_Canvas。
        /// 找不到时退回任意一个屏幕空间画布，仍然不碰世界空间的头顶血条。
        /// </summary>
        public static Canvas MainCanvas()
        {
            if (_main != null) return _main;
            var all = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            Canvas fallback = null;
            foreach (var c in all)
            {
                if (c == null || c.renderMode == RenderMode.WorldSpace) continue;
                if (c.name == "HUD_Canvas") { _main = c; return _main; }
                if (fallback == null) fallback = c;
            }
            _main = fallback;
            return _main;
        }

        static Canvas _main;

        public static Font DefaultFont() =>
            Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static Text MakeText(Transform parent, string name, string content,
            int size, TextAnchor anchor, Color color)
        {
            var go = new GameObject(name, typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = DefaultFont();
            t.fontSize = size;
            t.alignment = anchor;
            t.color = color;
            t.text = content;
            t.raycastTarget = false;
            return t;
        }

        public static RectTransform SetRect(Component c, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var rt = c.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static GameObject MakePanel(Transform parent, string name, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            SetRect(go.GetComponent<Image>(), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            go.GetComponent<Image>().color = color;
            // 自适应：面板尺寸按 1080 参考系写死，超宽屏上画布只有 ≈864 单位高，
            // 装不下就整体等比缩小（只对直接挂在 Canvas 下的顶层面板生效，见 PanelFit）
            go.AddComponent<UI.PanelFit>();
            return go;
        }

        public static Button MakeButton(Transform parent, string label, Vector2 anchor,
            Vector2 pos, Vector2 size, Color color, UnityAction onClick, int fontSize = 26)
        {
            var go = new GameObject("Btn_" + label, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            SetRect(go.GetComponent<Image>(), anchor, pos, size);
            go.GetComponent<Image>().color = color;
            var txt = MakeText(go.transform, "Label", label, fontSize, TextAnchor.MiddleCenter, Color.white);
            var trt = txt.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            if (onClick != null) go.GetComponent<Button>().onClick.AddListener(onClick);
            return go.GetComponent<Button>();
        }

        public static InputField MakeInput(Transform parent, string placeholder,
            Vector2 anchor, Vector2 pos, Vector2 size, bool multiLine)
        {
            var go = new GameObject("Input", typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            SetRect(go.GetComponent<Image>(), anchor, pos, size);
            go.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.15f, 0.95f);

            var text = MakeText(go.transform, "Text", "", 24, TextAnchor.UpperLeft, Color.white);
            var trt = text.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(12, 8); trt.offsetMax = new Vector2(-12, -8);
            text.supportRichText = false;

            var ph = MakeText(go.transform, "Placeholder", placeholder, 24, TextAnchor.UpperLeft,
                new Color(1, 1, 1, 0.3f));
            var prt = ph.GetComponent<RectTransform>();
            prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
            prt.offsetMin = new Vector2(12, 8); prt.offsetMax = new Vector2(-12, -8);
            ph.fontStyle = FontStyle.Italic;

            var input = go.GetComponent<InputField>();
            input.textComponent = text;
            input.placeholder = ph;
            input.lineType = multiLine ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;

            // 【手机上输入法全屏盖住整个游戏画面的根因，就在这一行】
            //
            // 安卓输入法在**横屏**下默认会切到"全屏编辑模式"（extract UI）：
            // 它自带一个占满整块屏幕的文本框，把游戏画面整个盖掉——
            // 这时候无论把面板往上挪多少都没用，因为屏幕上根本已经没有游戏了。
            // 之前只做了避让（SoftKeyboardShifter），治的是"键盘遮住下半屏"，
            // 治不了"输入法接管全屏"，所以玩家看到的问题一点没变。
            //
            // shouldHideMobileInput = true 让 UGUI 不使用系统那块原生输入框，
            // 文字始终画在游戏内的输入框里；配合 SoftKeyboardShifter 里
            // TouchScreenKeyboard.hideInput 一起，输入法就只在屏幕下方出一块键盘。
            input.shouldHideMobileInput = true;
            return input;
        }
    }
}
