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

        readonly System.Collections.Generic.List<GameObject> _controls =
            new System.Collections.Generic.List<GameObject>();

        void Start()
        {
            if (!forceShow && !Application.isMobilePlatform) return;
            Build();
        }

        void OnEnable() => Core.GameEvents.OnPlayerDied += HideControls;
        void OnDisable() => Core.GameEvents.OnPlayerDied -= HideControls;

        /// <summary>玩家死亡：隐藏摇杆与全部战斗按钮（禁用触屏输入）。</summary>
        void HideControls(string reason)
        {
            foreach (var go in _controls)
                if (go != null) go.SetActive(false);
            MobileInput.Move = Vector2.zero;
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
            _controls.Add(look);

            // ---- 左侧大范围触控区：浮动摇杆（手指落在哪，摇杆就在哪）----
            var joyArea = CreatePanel("JoystickArea", new Vector2(0f, 0f), new Vector2(0.45f, 0.85f),
                new Color(0, 0, 0, 0.001f));
            _controls.Add(joyArea);
            joyArea.transform.SetAsFirstSibling();

            // 浮动外环（按下时显示并移到触点）
            var ring = new GameObject("JoyRing", typeof(Image));
            ring.transform.SetParent(transform, false);
            var ringRt = ring.GetComponent<RectTransform>();
            ringRt.sizeDelta = new Vector2(300, 300);
            ring.GetComponent<Image>().color = new Color(1, 1, 1, 0.14f);
            ring.GetComponent<Image>().raycastTarget = false;
            MakeCircle(ring.GetComponent<Image>());
            _controls.Add(ring);

            var handle = new GameObject("Handle", typeof(Image));
            handle.transform.SetParent(ring.transform, false);
            var hrt = handle.GetComponent<RectTransform>();
            hrt.sizeDelta = new Vector2(130, 130);
            handle.GetComponent<Image>().color = new Color(1, 1, 1, 0.42f);
            handle.GetComponent<Image>().raycastTarget = false;
            MakeCircle(handle.GetComponent<Image>());
            ring.SetActive(false);

            var joy = joyArea.AddComponent<VirtualJoystick>();
            joy.ring = ringRt;
            joy.handle = hrt;
            joy.radius = 130f;

            // ---- 右下角战斗按钮（KOF 拳腿分立布局）----
            // 拳/腿=双系连段可混接 | 重=按住蓄力/轻点+方向=指令技 | 闪=翻滚（完美闪避/受身）
            // 挡=格挡/定心格挡 | 跳（跳+拳=下劈，跳+腿=飞踢，蹲+攻=扫堂腿）| 气/定=技能
            AddButton("Light", "拳", new Vector2(-180, 210), 165, new Color(0.9f, 0.35f, 0.3f, 0.78f));
            AddButton("Kick", "腿", new Vector2(-395, 145), 150, new Color(0.95f, 0.6f, 0.25f, 0.78f));
            AddButton("Dodge", "闪", new Vector2(-185, 445), 130, new Color(0.3f, 0.7f, 0.95f, 0.78f));
            AddButton("Heavy", "重", new Vector2(-425, 330), 125, new Color(0.8f, 0.45f, 0.15f, 0.8f));
            AddButton("Guard", "挡", new Vector2(-625, 235), 108, new Color(0.4f, 0.8f, 0.5f, 0.75f));
            AddButton("Jump", "跳", new Vector2(-190, 655), 112, new Color(0.65f, 0.5f, 0.9f, 0.75f));
            AddButton("Skill2", "气", new Vector2(-650, 425), 100, new Color(0.35f, 0.8f, 0.95f, 0.75f));
            AddButton("Skill1", "定", new Vector2(-435, 520), 100, new Color(0.4f, 0.55f, 0.9f, 0.75f));
            AddButtonLeft("Crouch", "蹲", new Vector2(500, 170), 100, new Color(0.55f, 0.6f, 0.4f, 0.75f));
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
            _controls.Add(go);
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
            _controls.Add(go);
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
