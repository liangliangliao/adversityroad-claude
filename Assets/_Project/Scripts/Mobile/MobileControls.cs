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

            // ---- 右下角战斗按钮（大作标准：轻连段拳脚 / 重连段巨剑）----
            // 拳=轻连段（快攻削韧）| 剑=重连段（高伤击退）| 重=按住蓄力气场/轻点+方向=指令技
            // 闪=翻滚（完美闪避/受身）| 挡=格挡/定心格挡
            // 跳+拳=飞踢 跳+剑=空袭跳劈 蹲+拳=扫堂腿 蹲+剑=低位突刺 | 气/定/还=技能
            AddButton("Light", "拳", new Vector2(-180, 210), 165, new Color(0.9f, 0.35f, 0.3f, 0.78f));
            AddButton("Kick", "剑", new Vector2(-395, 145), 150, new Color(0.95f, 0.6f, 0.25f, 0.78f));
            AddButton("Dodge", "闪", new Vector2(-185, 445), 130, new Color(0.3f, 0.7f, 0.95f, 0.78f));
            AddButton("Heavy", "重", new Vector2(-425, 330), 125, new Color(0.8f, 0.45f, 0.15f, 0.8f));
            AddButton("Guard", "挡", new Vector2(-625, 235), 108, new Color(0.4f, 0.8f, 0.5f, 0.75f));
            AddButton("Jump", "跳", new Vector2(-190, 655), 112, new Color(0.65f, 0.5f, 0.9f, 0.75f));
            AddButton("Skill2", "气", new Vector2(-650, 425), 100, new Color(0.35f, 0.8f, 0.95f, 0.75f));
            AddButton("Skill1", "定", new Vector2(-435, 520), 100, new Color(0.4f, 0.55f, 0.9f, 0.75f));
            AddButton("Skill3", "还", new Vector2(-620, 560), 100, new Color(0.35f, 0.75f, 0.55f, 0.78f));
            AddButtonLeft("Crouch", "蹲", new Vector2(500, 170), 100, new Color(0.55f, 0.6f, 0.4f, 0.75f));
            // 拔刀/收刀（带剑鞘武器）：右侧战斗按钮区左上，易点；无鞘武器时点了无效
            AddButton("Sheathe", "拔刀", new Vector2(-815, 470), 96, new Color(0.55f, 0.55f, 0.62f, 0.78f));
        }

        void AddButtonLeft(string btnName, string label, Vector2 pos, float size, Color color)
        {
            var go = new GameObject("Btn_" + btnName, typeof(Image));
            go.transform.SetParent(transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0);
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

        static Sprite _circleSprite;

        /// <summary>把方形 Image 变圆形：用运行时生成的抗锯齿圆形贴图（Image.color 决定颜色）。
        /// 之前用内置 UI/Skin/Knob.psd，Unity 6 取不到会刷屏报错——改为自绘，零报错。</summary>
        static void MakeCircle(Image img) => img.sprite = CircleSprite();

        static Sprite CircleSprite()
        {
            if (_circleSprite != null) return _circleSprite;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float r = size * 0.5f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - r, dy = y + 0.5f - r;
                    float a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy)); // 1px 抗锯齿边
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            _circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _circleSprite;
        }
    }
}
