using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AdversityRoad.Core
{
    /// <summary>运行时 UI 构建工具：字体、文本、面板、按钮、输入框。</summary>
    public static class UiUtil
    {
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
            return input;
        }
    }
}
