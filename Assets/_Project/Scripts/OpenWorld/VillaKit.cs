using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.World;

namespace AdversityRoad.OpenWorld
{
    /// <summary>
    /// 建房子用的构件库。
    ///
    /// 【为什么要单独做这一层】
    /// 上一版的住宅玩家一句话就说透了："基本就是些方块形状的东西"。
    /// 原因不在建模能力，在于**整栋房子只用了一种图元、还全是轴对齐的**：
    /// 桌腿是方块、台灯是方块、马桶是方块、花盆是方块、门把手也是方块。
    /// 真实感的第一来源根本不是贴图，是**形状的多样性与非轴对齐**——
    /// 圆柱的腿、球形的灯罩、带倒角的台面、斜的屋顶、转过一点角度的椅子。
    ///
    /// 所以这里提供：圆柱 / 球 / 胶囊 / 可旋转的盒子 / 带线脚的板 / 玻璃 / 金属 /
    /// 自发光屏幕 / 贴墙的标牌。房间代码只管"放什么、放哪儿"，
    /// 形状与材质由这里统一负责。
    /// </summary>
    public static class VillaKit
    {
        public static Transform Root { get; private set; }
        static WorldContext _ctx;

        /// <summary>整栋房子挂在同一个根下：便于整体管理，也让层级不至于铺满场景。</summary>
        public static void Begin(WorldContext ctx, Vector3 origin)
        {
            _ctx = ctx;
            var go = new GameObject("PlayerVilla");
            go.transform.position = origin;
            Root = go.transform;
        }

        // ================= 基本图元 =================

        static GameObject Make(PrimitiveType type, string name, Vector3 pos, Vector3 size,
            Color color, Vector3 euler, bool solid)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (Root != null) go.transform.SetParent(Root, true);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(euler);
            go.transform.localScale = size;
            ZoneBuilder.Paint(_ctx, go, color);
            if (!solid)
            {
                var col = go.GetComponent<Collider>();
                if (col != null) Object.DestroyImmediate(col);
            }
            return go;
        }

        public static GameObject Box(string name, Vector3 pos, Vector3 size, Color c) =>
            Make(PrimitiveType.Cube, name, pos, size, c, Vector3.zero, true);

        public static GameObject Box(string name, Vector3 pos, Vector3 size, Color c, Vector3 euler) =>
            Make(PrimitiveType.Cube, name, pos, size, c, euler, true);

        public static GameObject Deco(string name, Vector3 pos, Vector3 size, Color c) =>
            Make(PrimitiveType.Cube, name, pos, size, c, Vector3.zero, false);

        public static GameObject Deco(string name, Vector3 pos, Vector3 size, Color c, Vector3 euler) =>
            Make(PrimitiveType.Cube, name, pos, size, c, euler, false);

        /// <summary>竖直圆柱（Unity 的 Cylinder 高度是 scale.y 的两倍）。</summary>
        public static GameObject Cyl(string name, Vector3 baseCenter, float radius, float height,
            Color c, bool solid = false)
        {
            var go = Make(PrimitiveType.Cylinder, name, baseCenter + Vector3.up * (height / 2f),
                new Vector3(radius * 2f, height / 2f, radius * 2f), c, Vector3.zero, solid);
            return go;
        }

        /// <summary>任意朝向的圆柱（管道、扶手、栏杆）。</summary>
        public static GameObject CylAxis(string name, Vector3 center, float radius, float length,
            Color c, Vector3 euler, bool solid = false) =>
            Make(PrimitiveType.Cylinder, name, center,
                new Vector3(radius * 2f, length / 2f, radius * 2f), c, euler, solid);

        public static GameObject Sph(string name, Vector3 center, float diameter, Color c,
            bool solid = false) =>
            Make(PrimitiveType.Sphere, name, center, Vector3.one * diameter, c, Vector3.zero, solid);

        public static GameObject Cap(string name, Vector3 center, float diameter, float height,
            Color c, Vector3 euler, bool solid = false) =>
            Make(PrimitiveType.Capsule, name, center,
                new Vector3(diameter, height / 2f, diameter), c, euler, solid);

        // ================= 材质变体 =================

        static readonly Dictionary<string, Material> _special = new Dictionary<string, Material>();

        static Material Special(string key, Color c, float metallic, float smooth, Color emis)
        {
            if (_special.TryGetValue(key, out var m) && m != null) return m;
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            m = new Material(sh) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
            if (emis.maxColorComponent > 0.01f)
            {
                m.EnableKeyword("_EMISSION");
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", emis);
            }
            _special[key] = m;
            return m;
        }

        static string Key(string tag, Color c) =>
            tag + Mathf.RoundToInt(c.r * 40) + "_" + Mathf.RoundToInt(c.g * 40) + "_" +
            Mathf.RoundToInt(c.b * 40) + "_" + Mathf.RoundToInt(c.a * 20);

        /// <summary>抛光/金属：不锈钢、水龙头、灯杆、门把手。</summary>
        public static GameObject Metal(GameObject go, Color c, float smooth = 0.78f)
        {
            var r = go.GetComponent<MeshRenderer>();
            if (r != null) r.sharedMaterial = Special(Key("met", c) + smooth, c, 0.85f, smooth, Color.black);
            return go;
        }

        /// <summary>玻璃：透明 + 高光（窗、淋浴隔断、镜面、水面）。</summary>
        public static GameObject Glass(GameObject go, Color c, float alpha = 0.35f)
        {
            var r = go.GetComponent<MeshRenderer>();
            if (r == null) return go;
            var col = new Color(c.r, c.g, c.b, alpha);
            var m = Special(Key("gls", col), col, 0.1f, 0.94f, Color.black);
            // URP Lit 透明：一次性把渲染状态设好（缓存材质只会走一次）
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            r.sharedMaterial = m;
            return go;
        }

        /// <summary>自发光：屏幕、灯罩、指示灯——它们不该跟墙一样是死的。</summary>
        public static GameObject Emit(GameObject go, Color c, float strength = 1.6f)
        {
            var r = go.GetComponent<MeshRenderer>();
            if (r != null)
                r.sharedMaterial = Special(Key("emi", c) + strength, c, 0f, 0.5f, c * strength);
            return go;
        }

        // ================= 组合构件 =================

        /// <summary>
        /// 带线脚的板：一块面板 + 四周一圈略凸出的边框。
        /// 门扇、柜门、墙裙、画框全靠它——平板和"有做工的板"差别就在这一圈边上。
        /// </summary>
        public static void PanelWithFrame(string name, Vector3 center, Vector2 size, float thick,
            Color face, Color frame, bool alongZ)
        {
            Vector3 s = alongZ ? new Vector3(thick, size.y, size.x) : new Vector3(size.x, size.y, thick);
            Deco(name, center, s, face);
            const float b = 0.09f;
            Vector3 h = alongZ ? new Vector3(thick * 1.4f, b, size.x) : new Vector3(size.x, b, thick * 1.4f);
            Vector3 v = alongZ ? new Vector3(thick * 1.4f, size.y, b) : new Vector3(b, size.y, thick * 1.4f);
            Deco(name + "_T", center + new Vector3(0, size.y / 2f - b / 2f, 0), h, frame);
            Deco(name + "_B", center - new Vector3(0, size.y / 2f - b / 2f, 0), h, frame);
            Vector3 side = alongZ ? new Vector3(0, 0, size.x / 2f - b / 2f) : new Vector3(size.x / 2f - b / 2f, 0, 0);
            Deco(name + "_L", center - side, v, frame);
            Deco(name + "_R", center + side, v, frame);
        }

        /// <summary>
        /// 一整套门：门套 + 两扇带面板的门扇（半开）+ 门槛 + 门楣上的小灯。
        ///
        /// 玩家反复说"找不到门、连门都没有"。真实原因是上一版的门只是墙上一个洞：
        /// 洞和墙同色、同高、没有任何视觉标记，站在十米外根本分辨不出来。
        /// 现在门有门套（浅色木线）、有门扇、有门槛（地上一条深色横线）、
        /// 头顶还有一盏小灯——从房间任何角落都能一眼看到"那里可以出去"。
        /// </summary>
        public static void Doorway(Vector3 at, float width, float height, bool alongZ,
            Color caseColor, Color leafColor)
        {
            float t = 0.34f;
            // 门套：两侧竖梃 + 上槛
            Vector3 sideSize = alongZ ? new Vector3(t + 0.2f, height, 0.18f) : new Vector3(0.18f, height, t + 0.2f);
            Vector3 sideOff = alongZ ? new Vector3(0, height / 2f, width / 2f) : new Vector3(width / 2f, height / 2f, 0);
            Deco("DoorCase_L", at - sideOff, sideSize, caseColor);
            Deco("DoorCase_R", at + sideOff, sideSize, caseColor);
            Vector3 topSize = alongZ ? new Vector3(t + 0.2f, 0.2f, width + 0.36f) : new Vector3(width + 0.36f, 0.2f, t + 0.2f);
            Deco("DoorCase_T", at + new Vector3(0, height, 0), topSize, caseColor);

            // 门槛：地面上一条深色横条，远远就能看出这里是通的
            Vector3 sillSize = alongZ ? new Vector3(t + 0.3f, 0.05f, width) : new Vector3(width, 0.05f, t + 0.3f);
            Deco("DoorSill", at + new Vector3(0, 0.03f, 0), sillSize, caseColor * 0.7f);

            // 两扇门扇：各自贴在门洞一侧、朝屋里转开
            for (int s = -1; s <= 1; s += 2)
            {
                float leafW = width / 2f - 0.06f;
                Vector3 hinge = alongZ ? new Vector3(0, 0, s * width / 2f) : new Vector3(s * width / 2f, 0, 0);
                // 开 75°：门扇几乎贴着墙，不挡路，但一眼看得出是"开着的门"
                Vector3 dir = alongZ ? new Vector3(0.94f, 0, -s * 0.26f) : new Vector3(-s * 0.26f, 0, 0.94f);
                Vector3 center = at + hinge + dir * (leafW / 2f) + new Vector3(0, height / 2f, 0);
                Vector3 euler = alongZ ? new Vector3(0, 15f * -s, 0) : new Vector3(0, 90f + 15f * s, 0);
                var leaf = Make(PrimitiveType.Cube, "DoorLeaf", center,
                    new Vector3(leafW, height - 0.1f, 0.08f), leafColor, euler, false);
                // 门芯板（两块凹格）与把手
                for (int k = -1; k <= 1; k += 2)
                {
                    var panel = Make(PrimitiveType.Cube, "DoorPanel",
                        center + leaf.transform.up * (k * height * 0.2f) + leaf.transform.forward * -0.05f,
                        new Vector3(leafW * 0.7f, height * 0.3f, 0.04f), leafColor * 0.88f, euler, false);
                }
                var knob = Sph("DoorKnob",
                    center + leaf.transform.right * (leafW * 0.38f) - new Vector3(0, height * 0.1f, 0),
                    0.14f, new Color(0.75f, 0.68f, 0.42f));
                Metal(knob, new Color(0.78f, 0.70f, 0.45f));
            }

            // 门头小灯：屋里最好认的路标
            var lamp = Deco("DoorLamp", at + new Vector3(0, height + 0.28f, 0),
                alongZ ? new Vector3(0.22f, 0.12f, 0.7f) : new Vector3(0.7f, 0.12f, 0.22f),
                new Color(1f, 0.94f, 0.78f));
            Emit(lamp, new Color(1f, 0.92f, 0.72f), 2.2f);
        }

        /// <summary>
        /// 贴墙的房间标牌：一块实体牌子 + 固定朝向的字。
        ///
        /// 上一版用的是会转向镜头的 TextMesh，且背后什么都没有——
        /// 玩家看到的就是"文字在空中飘着"。字必须贴在一块牌子上、
        /// 牌子必须钉在墙上、朝向必须固定，才像一块门牌。
        /// </summary>
        public static void WallSign(Vector3 at, string text, Vector3 faceDir, float width = 2.4f)
        {
            faceDir = faceDir.normalized;
            float yaw = Quaternion.LookRotation(faceDir).eulerAngles.y;
            var plate = Make(PrimitiveType.Cube, "SignPlate", at,
                new Vector3(width, 0.62f, 0.09f), new Color(0.18f, 0.19f, 0.22f),
                new Vector3(0, yaw, 0), false);
            Make(PrimitiveType.Cube, "SignEdge", at + faceDir * -0.01f,
                new Vector3(width + 0.12f, 0.74f, 0.06f), new Color(0.62f, 0.52f, 0.32f),
                new Vector3(0, yaw, 0), false);

            var go = new GameObject("SignText");
            if (Root != null) go.transform.SetParent(Root, true);
            go.transform.position = at + faceDir * 0.06f;
            go.transform.rotation = Quaternion.LookRotation(-faceDir);
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.fontSize = 64;
            tm.characterSize = 0.055f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = new Color(1f, 0.93f, 0.72f);
            var mr = go.GetComponent<MeshRenderer>();
            if (tm.font != null) mr.material = tm.font.material;
        }

        /// <summary>墙面护墙板：一条腰线 + 下半墙的分色，房间立刻不像毛坯。</summary>
        public static void Wainscot(Vector3 center, float length, bool alongZ, float y,
            Color lower, Color rail)
        {
            Vector3 lowSize = alongZ ? new Vector3(0.06f, 1.05f, length) : new Vector3(length, 1.05f, 0.06f);
            Vector3 railSize = alongZ ? new Vector3(0.1f, 0.1f, length) : new Vector3(length, 0.1f, 0.1f);
            Deco("Wainscot", center + new Vector3(0, y + 0.52f, 0), lowSize, lower);
            Deco("WainscotRail", center + new Vector3(0, y + 1.06f, 0), railSize, rail);
        }
    }
}
