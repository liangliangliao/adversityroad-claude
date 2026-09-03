using UnityEngine;
using UnityEngine.UI;

namespace AdversityRoad.Mobile
{
    /// <summary>
    /// 触屏操作层：运行时自动在 Canvas 上生成摇杆 + 转镜头区 + 6 核心键 + 1 技能修饰键。
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

            // ---- 右下角战斗按钮：6 核心键 + 1 修饰键，全部落在拇指扇形内 ----
            //
            // 布局依据（触屏人体工学，坐标由拇指弧 + 间隙约束解出，非目测摆放）：
            // 以拇指根枢轴 ≈(-120,120) 为圆心，舒适扫掠半径 ≈42mm（1920 参考宽下约 530px）。
            //   · 拳（最高频）落在拇指自然静止点 r=115（9.1mm），最大（168）；
            //   · 剑/重/闪 排内弧 r=345（27.3mm），角度 6°/50°/94°；
            //   · 挡/术/跳 排外弧 r=560（44.3mm），角度 8°/50°/92°——**与内弧同辐射线**。
            // 外弧刻意与内弧对齐而不是错开：拇指沿同一方向扫出去，先碰内环再碰外环，
            // 肌肉记忆是一维的（"往左一点是剑，再往左是挡"），比二维找位置快得多；
            // 两环靠 215px 的半径差拉开距离，任意两键间隙 ≥84px(6.6mm)，高于触屏下限。
            //
            // 六个技能不再各占一个按钮（那正是旧布局 14 键、4 键超出可达区、
            // 「火」与「拔刀」视觉重叠 5.3mm 的原因），改用手柄的修饰键思路：
            // 点「术」亮起，六个核心键临时变成六个技能键（见 MobileInput.SkillMap）。
            // 技能因此也进入融合链——术→剑 打出的是「术后追斩」，
            // 一键技能成为组合的一环，而不是绕过组合的捷径。
            AddButton("Light", "拳", new Vector2(-208, 194), 168, new Color(0.9f, 0.35f, 0.3f, 0.78f));
            AddButton("Kick", "剑", new Vector2(-463, 156), 130, new Color(0.95f, 0.6f, 0.25f, 0.78f));
            AddButton("Heavy", "重", new Vector2(-342, 384), 130, new Color(0.8f, 0.45f, 0.15f, 0.8f));
            AddButton("Dodge", "闪", new Vector2(-96, 464), 130, new Color(0.3f, 0.7f, 0.95f, 0.78f));
            AddButton("Guard", "挡", new Vector2(-675, 198), 112, new Color(0.4f, 0.8f, 0.5f, 0.75f));
            AddButton(MobileInput.Modifier, "术", new Vector2(-480, 549), 118,
                new Color(0.45f, 0.55f, 0.95f, 0.82f));
            AddButton("Jump", "跳", new Vector2(-100, 680), 112, new Color(0.65f, 0.5f, 0.9f, 0.75f));

            // ---- 统一交互键：全游戏所有"按 E"的地方，手机上都是这一个键 ----
            //
            // 【之前这个键根本不存在】MobileInput 的注释里写着 Interact，
            // 家里的物件、生成关卡的门、猫食盆、画框也都在查 MobileInput.GetDown("Interact")，
            // 但 MobileControls 从来没有造出这个按钮——于是手机玩家永远够不到任何一处交互，
            // 只能看着"按 E 使用"的提示干瞪眼。
            // 位置放在拳键上方、拇指弧内但避开连打区：它是战斗外用的键，
            // 既要够得着，又不能在打斗时被误触。
            AddButton("Interact", "用", new Vector2(-230, 700), 124,
                new Color(0.35f, 0.72f, 0.62f, 0.82f));

            // ---- 低频操作移出战斗区（战间隙才用，不该占拇指黄金位）----
            AddButtonLeft("Crouch", "蹲", new Vector2(500, 170), 100, new Color(0.55f, 0.6f, 0.4f, 0.75f));
            // 锁定切换 / 拔刀收刀：右侧边缘竖排，远离连打区，避免打斗中误触
            AddButtonEdge("Lock", "锁", new Vector2(-92, -300), 84, new Color(0.75f, 0.35f, 0.55f, 0.62f));
            // 有敌人在追你、又不在画面里、你还没锁定——这颗键自己亮起来（见 LockButtonHint）
            var lockBtn = transform.Find("Btn_Lock");
            if (lockBtn != null) lockBtn.gameObject.AddComponent<LockButtonHint>();
            AddButtonEdge("Sheathe", "拔刀", new Vector2(-92, -404), 84, new Color(0.55f, 0.55f, 0.62f, 0.62f));

            // 「术」生效时把六个核心键的字面换成实际装备的技能名（可发现性）
            gameObject.AddComponent<SkillModifierDisplay>();
        }

        /// <summary>屏幕右侧边缘竖排小键（锚点右中，anchoredPosition 相对右侧中点）。</summary>
        void AddButtonEdge(string btnName, string label, Vector2 pos, float size, Color color)
        {
            var go = new GameObject("Btn_" + btnName, typeof(Image));
            go.transform.SetParent(transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(size, size);
            var img = go.GetComponent<Image>();
            img.color = color;
            MakeCircle(img);
            MakeLabel(go.transform, label, size);
            go.AddComponent<VirtualButton>().buttonName = btnName;
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
            MakeLabel(go.transform, label, size);
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

            MakeLabel(go.transform, label, size);
        }

        /// <summary>按钮字面：铺满按钮居中显示，不吃射线（点击穿透到按钮本身）。</summary>
        static Text MakeLabel(Transform parent, string label, float size)
        {
            var textGo = new GameObject("Label", typeof(Text));
            textGo.transform.SetParent(parent, false);
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
            return t;
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
