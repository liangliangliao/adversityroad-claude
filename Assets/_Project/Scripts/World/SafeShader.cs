using System.Collections.Generic;
using UnityEngine;

namespace AdversityRoad.World
{
    /// <summary>
    /// 运行时取 Shader 的统一入口。
    ///
    /// 【为什么必须有这一层】
    /// <c>Shader.Find</c> 在真机包里**只找得到进了包的 shader**。一个 shader 进包只有两条路：
    /// 写进 ProjectSettings 的 Always Included Shaders，或者被某个随包发布的材质资源引用到。
    /// 本工程实际满足条件的只有两类：
    ///   1) Always Included 里的 8 个内置 shader（其中就有 Sprites/Default）；
    ///   2) <c>M_Base.mat</c> 引用的 Universal Render Pipeline/Lit。
    /// 除此之外——URP/Unlit、Unlit/Color、Unlit/Transparent、Standard——在编辑器里都找得到，
    /// 打进安卓包以后**全是 null**。而 <c>new Material(null)</c> 的结果就是那块洋红。
    ///
    /// 敌人脚下的前摇警示圈原来写的是
    /// <c>Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color")</c>，
    /// 两个都不在包里，于是每个进入前摇的敌人脚下都会亮起一块洋红椭圆。
    /// 这不是某一关的问题，是全场景通病，只是暗场关卡里最扎眼。
    ///
    /// 这个类把"可用的 shader"收敛成一张有序表，并且永远给得出一个不是洋红的兜底材质。
    /// </summary>
    public static class SafeShader
    {
        /// <summary>随包发布的基础材质（M_Base，URP/Lit）。由 GameBootstrap 注入。</summary>
        static Material _base;

        static readonly Dictionary<string, Material> _cache = new Dictionary<string, Material>();

        public static void Init(Material baseMaterial)
        {
            _base = baseMaterial;
        }

        /// <summary>按优先级找第一个真的存在的 shader；一个都没有就返回 null。</summary>
        public static Shader Find(params string[] names)
        {
            if (names == null) return null;
            for (int i = 0; i < names.Length; i++)
            {
                if (string.IsNullOrEmpty(names[i])) continue;
                var sh = Shader.Find(names[i]);
                if (sh != null) return sh;
            }
            return null;
        }

        /// <summary>
        /// 不受光照影响的提示色材质（警示圈、视线锥、低语链这类"给玩家看的 UI"）。
        ///
        /// 首选 Sprites/Default：它在 Always Included 名单里，是本工程唯一**确定进包**的
        /// 无光着色器；顶点色与材质色都吃，透明混合也是现成的。
        /// 真拿不到时退回基础材质并把自发光拉满——亮度不如 Unlit 稳，但至少是对的颜色。
        /// </summary>
        public static Material Unlit(Color color, string tag = "u")
        {
            string key = tag + Key(color);
            if (_cache.TryGetValue(key, out var hit) && hit != null) return hit;

            var sh = Find("Sprites/Default",
                          "Universal Render Pipeline/Unlit",
                          "Unlit/Transparent",
                          "Unlit/Color");
            Material m;
            if (sh != null)
            {
                m = new Material(sh) { name = "SafeUnlit_" + key, color = color };
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            }
            else
            {
                m = FromBase(color);
                // 没有无光着色器可用时，靠自发光把颜色顶出来，暗场里才不至于变成一块深褐
                m.EnableKeyword("_EMISSION");
                if (m.HasProperty("_EmissionColor"))
                    m.SetColor("_EmissionColor", new Color(color.r, color.g, color.b) * 1.6f);
            }
            if (color.a < 0.999f) MakeTransparent(m);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            _cache[key] = m;
            return m;
        }

        /// <summary>受光的实体材质兜底（URP/Lit 经 M_Base 进包，一般拿得到）。</summary>
        public static Material Lit(Color color)
        {
            string key = "l" + Key(color);
            if (_cache.TryGetValue(key, out var hit) && hit != null) return hit;
            var m = FromBase(color);
            _cache[key] = m;
            return m;
        }

        static Material FromBase(Color color)
        {
            Material m;
            if (_base != null) m = new Material(_base);
            else
            {
                var sh = Find("Universal Render Pipeline/Lit", "Standard");
                // 到这一步还是 null 的话已经无计可施，但 Unity 至少会给出内置错误材质而不是空引用
                m = sh != null ? new Material(sh) : new Material(Shader.Find("Sprites/Default"));
            }
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            return m;
        }

        static void MakeTransparent(Material m)
        {
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_SrcBlend"))
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend"))
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }

        static string Key(Color c) =>
            Mathf.RoundToInt(c.r * 64) + "_" + Mathf.RoundToInt(c.g * 64) + "_" +
            Mathf.RoundToInt(c.b * 64) + "_" + Mathf.RoundToInt(c.a * 32);
    }
}
