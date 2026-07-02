using UnityEngine;
using UnityEngine.UI;

namespace AdversityRoad.Mobile
{
    /// <summary>
    /// 触屏操作层：运行时自动在 Canvas 上生成摇杆 + 转镜头区 + 7 个战斗按钮。
    /// forceShow=false 时只在安卓/iOS 真机显示，编辑器里不遮挡画面。
    /// </summary>
    public class MobileControls : MonoBehaviour
    {
        public bool forceShow = false;

        void Start()
        {
            if (!forceShow && !Application.isMobilePlatform) return;
            Build();
        }

        void Build()
        {
            var canvas = GetComponent<Canvas>();
            if (canvas == null) return;

            if (FindObjectOfType<MobileInputPump>() == null)
                gameObject.AddComponent<MobileInputPump>();

            // ---- 右侧转镜头区（透明，最底层）----
            var look = CreatePanel("TouchLookArea", new Vector2(0.45f, 0f), new Vector2(1f, 1f),
                new Color(0, 0, 0, 0.001f));
            look.AddComponent<TouchLookArea>();
            look.transform.SetAsFirstSibling();

            // ---- 左下角摇杆 ----
            var joyBg = CreatePanel("Joystick", Vector2.zero, Vector2.zero,
                new Color(1, 1, 1, 0.15f));
            var joyRt = joyBg.GetComponent<RectTransform>();
            joyRt.anchorMin = joyRt.anchorMax = new Vector2(0, 0);
            joyRt.pivot = new Vector2(0.5f, 0.5f);
            joyRt.anchoredPosition = new Vector2(260, 260);
            joyRt.sizeDelta = new Vector2(320, 320);
            MakeCircle(joyBg.GetComponent<Image>());

            var handle = new GameObject("Handle", typeof(Image));
            handle.transform.SetParent(joyBg.transform, false);
            var hrt = handle.GetComponent<RectTransform>();
            hrt.sizeDelta = new Vector2(130, 130);
            handle.GetComponent<Image>().color = new Color(1, 1, 1, 0.45f);
            MakeCircle(handle.GetComponent<Image>());

            var joy = joyBg.AddComponent<VirtualJoystick>();
            joy.handle = hrt;
            joy.radius = 110f;

            // ---- 右下角战斗按钮 ----
            AddButton("Light", "攻", new Vector2(-180, 200), 150, new Color(0.9f, 0.35f, 0.3f, 0.75f));
            AddButton("Heavy", "重", new Vector2(-380, 140), 130, new Color(0.85f, 0.55f, 0.2f, 0.75f));
            AddButton("Dodge", "闪", new Vector2(-180, 420), 130, new Color(0.3f, 0.7f, 0.95f, 0.75f));
            AddButton("Guard", "挡", new Vector2(-400, 330), 120, new Color(0.4f, 0.8f, 0.5f, 0.75f));
            AddButton("Lock", "锁", new Vector2(-560, 210), 100, new Color(0.7f, 0.7f, 0.75f, 0.7f));
            AddButton("Inner", "功", new Vector2(-570, 400), 100, new Color(0.95f, 0.8f, 0.3f, 0.75f));
            AddButton("Jump", "跳", new Vector2(-180, 620), 110, new Color(0.65f, 0.5f, 0.9f, 0.75f));
            AddButton("Skill1", "技1", new Vector2(-390, 520), 110, new Color(0.9f, 0.3f, 0.55f, 0.75f));
            AddButton("Skill2", "技2", new Vector2(-580, 570), 100, new Color(0.4f, 0.55f, 0.9f, 0.75f));
            AddButton("Interact", "互", new Vector2(-750, 170), 95, new Color(0.6f, 0.85f, 0.85f, 0.7f));
        }

        GameObject CreatePanel(string name, Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = color;
            go.GetComponent<Image>().raycastTarget = true;
            return go;
        }

        void AddButton(string btnName, string label, Vector2 pos, float size, Color color)
        {
            var go = new GameObject("Btn_" + btnName, typeof(Image));
            go.transform.SetParent(transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(size, size);
            var img = go.GetComponent<Image>();
            img.color = color;
            MakeCircle(img);

            var vb = go.AddComponent<VirtualButton>();
            vb.buttonName = btnName;

            var textGo = new GameObject("Label", typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var t = textGo.GetComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = Mathf.RoundToInt(size * 0.4f);
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.text = label;
            t.raycastTarget = false;
        }

        /// <summary>用内置 Knob 精灵把方形 Image 变圆形；打包后取不到内置精灵时保持方形。</summary>
        static void MakeCircle(Image img)
        {
            try
            {
                var knob = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");
                if (knob != null) img.sprite = knob;
            }
            catch { /* 内置精灵未包含在构建中：保持方形按钮 */ }
        }
    }
}
