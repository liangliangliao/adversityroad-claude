using System.Collections.Generic;
using UnityEngine;

namespace AdversityRoad.World
{
    /// <summary>
    /// 世界里的字（门牌、路牌、仪表读数）统一从这里创建。
    ///
    /// 【为什么必须有这一层：字会穿墙】
    /// 玩家的原话是"空间中总是飘散着一些文字"。原因不在摆放位置，在**材质**：
    /// TextMesh 默认用的 `tm.font.material` 是内置的 `GUI/Text Shader`，
    /// 那个 shader 写着 `ZTest Always` —— 它**根本不做深度测试**。
    /// 于是每一块门牌的字都会穿过墙、穿过楼板、穿过家具画在最前面：
    /// 站在主卧里能看到隔壁主卫的牌子、楼下客厅的牌子，而牌子本体被墙挡住了，
    /// 只剩字浮在空中；那些字还是从背面看的，所以是镜像的——两个现象同一个成因。
    ///
    /// 这里换成一份**会做深度测试**的透明材质，只把字体图集当贴图用：
    ///   · ZTest 正常 ⇒ 墙后面的字被墙挡住，不再满屋飘字；
    ///   · 颜色放在材质里、按颜色缓存（顶点色一律给白）——这样无论落到哪一个
    ///     兜底 shader 上，颜色都是对的（URP Unlit 根本不读顶点色）；
    ///   · 字体图集重建时（动态字体遇到新字符会换贴图）自动跟着换，字不会变空白。
    ///
    /// 战斗浮字（伤害数字、招式名、敌人警示）**不**走这里：那些就该画在最前面，
    /// 被墙挡住的伤害数字才是 Bug。
    /// </summary>
    public static class WorldText
    {
        static readonly Dictionary<int, Material> _byColor = new Dictionary<int, Material>();
        static bool _hooked;

        /// <summary>内置字体（世界里所有字共用一套图集，省内存也省 DrawCall）。</summary>
        public static Font Builtin => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        /// <summary>
        /// 在 go 上挂一个会被遮挡的 TextMesh。
        /// 返回 TextMesh 以便后续改文本（仪表读数之类）。
        /// </summary>
        public static TextMesh Attach(GameObject go, string text, int fontSize, float charSize,
            Color color, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var tm = go.AddComponent<TextMesh>();
            tm.text = text ?? "";
            tm.font = Builtin;
            tm.fontSize = Mathf.Max(8, fontSize);
            tm.characterSize = charSize;
            tm.anchor = anchor;
            tm.alignment = TextAlignment.Center;
            // 顶点色给白：颜色由材质负责（见类注释）
            tm.color = Color.white;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = MaterialFor(tm.font, color);
            return tm;
        }

        /// <summary>
        /// 给一段世界文字配一块底板，并让它稍微离开被标注的那个面。
        ///
        /// 【为什么必须有底板】
        /// 没有底板的字就是几个悬在空中的字形：背景一花就读不出来，
        /// 站位一变还会被它自己标注的那面墙切掉一半（玩家截图里的「店铺」「任务挑战」
        /// 就是这样——半截字埋在墙里）。加一块比字略大的深色板，字才变成"牌子"。
        ///
        /// 板子的尺寸要等 TextMesh 真的排完版才知道（bounds 在第一帧之后才有效），
        /// 所以交给 WorldTextPlate 在第一次 LateUpdate 里量一次、贴好、然后自己停掉。
        /// </summary>
        public static TextMesh Plate(TextMesh tm, float pad = 0.16f, Color? back = null)
        {
            if (tm == null) return tm;
            var plate = GameObject.CreatePrimitive(PrimitiveType.Quad);
            plate.name = "TextPlate";
            Object.DestroyImmediate(plate.GetComponent<Collider>());
            plate.transform.SetParent(tm.transform, false);
            // 往字的背后让 2 厘米：同深度会和字打架（z-fighting），字会一闪一闪
            plate.transform.localPosition = new Vector3(0f, 0f, 0.02f);
            plate.transform.localRotation = Quaternion.identity;

            var c = back ?? new Color(0.06f, 0.07f, 0.10f, 0.82f);
            var mr = plate.GetComponent<MeshRenderer>();
            mr.sharedMaterial = MaterialFor(null, c);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var fit = plate.AddComponent<WorldTextPlate>();
            fit.target = tm;
            fit.pad = pad;
            return tm;
        }

        static Material MaterialFor(Font font, Color tint)
        {
            int key = (Mathf.RoundToInt(Mathf.Clamp01(tint.r) * 31) << 15)
                    | (Mathf.RoundToInt(Mathf.Clamp01(tint.g) * 31) << 10)
                    | (Mathf.RoundToInt(Mathf.Clamp01(tint.b) * 31) << 5)
                    | Mathf.RoundToInt(Mathf.Clamp01(tint.a) * 31);
            if (_byColor.TryGetValue(key, out var cached) && cached != null) return cached;

            var sh = Shader.Find("Sprites/Default");                       // 顶点色+深度测试都正常
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Transparent");
            if (sh == null)
            {
                // 实在找不到就退回内置字体材质：字会穿墙，但至少看得见字
                // （底板走同一条路，font 为 null，此时没有兜底可用，返回 null 即不画板）
                var fallback = font != null ? font.material : null;
                _byColor[key] = fallback;
                return fallback;
            }

            var m = new Material(sh) { name = "WorldText" };
            m.color = tint;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);      // URP：透明
            if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            _byColor[key] = m;
            if (font != null) Bind(font);
            if (!_hooked)
            {
                _hooked = true;
                // 注意要写全名：本类里的 Builtin 是属性，Font.textureRebuilt 是静态事件
                UnityEngine.Font.textureRebuilt += Bind;
            }
            return m;
        }

        /// <summary>把字体图集贴到所有缓存材质上（首次创建 + 图集重建时）。</summary>
        static void Bind(Font font)
        {
            // 这是 Font.textureRebuilt 的订阅者之一，而 UGUI 的 Text 也订阅了同一个事件
            // 去重建自己的网格。**这里抛异常会连累后面的订阅者**（UI 的字就会保持
            // 旧 UV，显示成一堆错位的乱码）。所以整段包起来。
            try
            {
                if (font == null || font.material == null) return;
                var tex = font.material.mainTexture;
                foreach (var kv in _byColor)
                {
                    var m = kv.Value;
                    if (m == null) continue;
                    m.mainTexture = tex;
                    if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[WorldText] 字体图集重绑失败：" + e.Message);
            }
        }
    }

    /// <summary>
    /// 把底板贴合到文字的实际排版尺寸。
    ///
    /// TextMesh 的 bounds 要等它真的排完一次版才有效，所以这件事不能在创建的那一帧做。
    /// 量到一次（宽高都大于 0）就贴好并把自己关掉——它不是一个需要每帧跑的东西。
    /// </summary>
    public class WorldTextPlate : MonoBehaviour
    {
        public TextMesh target;
        public float pad = 0.16f;

        void LateUpdate()
        {
            if (target == null) { enabled = false; return; }
            var mr = target.GetComponent<MeshRenderer>();
            if (mr == null) { enabled = false; return; }

            // 取本地空间的尺寸：bounds 是世界空间的，父物体有旋转时不能直接拿来当宽高
            Vector3 e = mr.localBounds.size;
            if (e.x <= 0.0001f || e.y <= 0.0001f) return;   // 还没排版，下一帧再量

            transform.localScale = new Vector3(e.x + pad * 2f, e.y + pad, 1f);
            // 文字锚点不一定在中心（多为 MiddleCenter，但也有别的），按实际中心对齐
            Vector3 c = mr.localBounds.center;
            transform.localPosition = new Vector3(c.x, c.y, 0.02f);
            enabled = false;
        }
    }
}
