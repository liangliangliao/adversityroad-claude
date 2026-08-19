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
            Bind(font);
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
}
