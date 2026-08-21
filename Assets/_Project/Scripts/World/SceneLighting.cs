using System.Collections.Generic;
using UnityEngine;

namespace AdversityRoad.World
{
    /// <summary>
    /// 场景照明的统一口径：**灯的强度**与**关卡的照明覆盖**。
    ///
    /// 【为什么"后面的关卡一片黑"】
    /// 这些关卡不是没灯，是灯不亮。全项目的运行时点光源一律 intensity = 1.0，
    /// 而 range 却给到 30~55 米。URP 的点光源按平方反比衰减：range=40、intensity=1
    /// 的灯，在 10 米外的照度只有约 1/100——地面拿到的光比环境光还少一个量级，
    /// 于是"摆了两盏灯"和"没摆灯"在画面上没有区别。
    /// 真实的布光是按**要照亮的距离**给强度的：range 越大，强度必须同比抬起来。
    /// <see cref="PointIntensity"/> 就是这条换算，全项目的灯统一走它。
    ///
    /// 【为什么还要一道覆盖审计】
    /// 24 个手工关卡 + 运行时生成的关卡，靠人去数"这块地有没有灯"是数不完的。
    /// <see cref="EnsureLit"/> 在建完场之后按网格采样一遍照度，哪块暗就在哪块补灯——
    /// 已经够亮的地方一盏都不会多加。夜里也看得清人和物，是可验证的，而不是靠调参数碰运气。
    /// </summary>
    public static class SceneLighting
    {
        /// <summary>
        /// 点光源的合理强度。
        ///
        /// 【为什么不能"要多亮给多亮"】
        /// URP 的点光源按 1/d² 衰减。想让 10 米外还亮，强度就得开到几十——
        /// 而那盏灯下面 1 米处的照度会是它的一百倍，人走过去直接烧成一片白。
        /// 一盏灯**不可能**同时满足"远处够亮"和"近处不过曝"，这是平方反比定律，
        /// 不是调参能绕过去的。
        ///
        /// 所以口径是：**单灯强度保持温和（≤5），覆盖靠多摆几盏**——
        /// 这也是没有烘焙 GI 时的通行做法。铺开的活交给 <see cref="EnsureLit"/>。
        /// </summary>
        public static float PointIntensity(float range)
        {
            return Mathf.Clamp(range * 0.22f, 1.8f, 5f);
        }

        /// <summary>照度门槛：低于它就认为"这块地没被照到"。
        /// 与上面的强度口径配套——强度 ≈4 的灯，约 5 米内能过线。</summary>
        public const float MinLux = 0.12f;

        /// <summary>补吊灯的安装高度与射程：贴着屋顶装会把顶低的房间照爆
        /// （人头离灯不到一米，1/d² 直接放大百倍），所以离地不超过 3.4 米。</summary>
        public static float CeilingLightY(float floorY, float roofY) =>
            Mathf.Min(roofY - 0.5f, floorY + 3.4f);

        public const float CeilingLightRange = 16f;

        /// <summary>夜间环境光下限：低于这个值，角色与地面在手机屏幕上就糊成一片。</summary>
        public static readonly Color NightAmbientFloor = new Color(0.46f, 0.48f, 0.58f);

        /// <summary>建一盏点光源（强度按 range 自动换算，不再各处手写 1.0）。</summary>
        public static Light MakePoint(string name, Vector3 pos, Color color, float range,
            Transform parent = null, float intensityScale = 1f)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = range;
            l.intensity = PointIntensity(range) * intensityScale;
            l.color = color;
            l.shadows = LightShadows.None;   // 运行时补光不投影：省性能，也避免自遮挡变黑
            return l;
        }

        /// <summary>
        /// 照明覆盖审计：在 center 周围 radius 的范围内按网格采样照度，
        /// 低于门槛的格子补一盏灯。addLamp 由调用方提供（各场景的灯造型不同）。
        ///
        /// 返回补了几盏（0 = 本来就够亮，一盏都没加）。
        /// </summary>
        public static int EnsureLit(Vector3 center, float radius, System.Func<Vector3, bool> addLamp,
            float cell = 14f, float minLux = MinLux, int maxLamps = 12)
        {
            if (addLamp == null) return 0;
            var lights = CollectPointLights();
            var added = new List<Vector3>();
            int n = Mathf.Clamp(Mathf.CeilToInt(radius * 2f / cell), 1, 12);
            float step = radius * 2f / n;
            Vector3 start = center - new Vector3(radius, 0, radius) + new Vector3(step, 0, step) * 0.5f;

            for (int ix = 0; ix < n && added.Count < maxLamps; ix++)
                for (int iz = 0; iz < n && added.Count < maxLamps; iz++)
                {
                    Vector3 p = start + new Vector3(ix * step, 0, iz * step);
                    p.y = center.y;
                    if (Lux(p, lights, added) >= minLux) continue;
                    // 补在格子中心：造型交给调用方（街灯 / 顶灯 / 应急灯）
                    if (addLamp(p)) added.Add(p);
                }
            return added.Count;
        }

        /// <summary>某点拿到的近似照度（只算点光源；主光与环境光另算）。</summary>
        static float Lux(Vector3 p, List<Light> lights, List<Vector3> pending)
        {
            float sum = 0f;
            foreach (var l in lights)
            {
                if (l == null || l.type != LightType.Point) continue;
                float d = Vector3.Distance(p, l.transform.position);
                if (d > l.range) continue;
                sum += l.intensity / Mathf.Max(1f, d * d) * (1f - d / l.range);
            }
            // 本轮已排定但还没造出来的灯，也要计进去（否则会挨着补一片）
            foreach (var q in pending)
            {
                float d = Vector3.Distance(p, q);
                if (d > CeilingLightRange) continue;
                sum += PointIntensity(CeilingLightRange) / Mathf.Max(1f, d * d)
                       * (1f - d / CeilingLightRange);
            }
            return sum;
        }

        static List<Light> CollectPointLights()
        {
            var list = new List<Light>();
            foreach (var l in Object.FindObjectsByType<Light>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (l != null && l.type == LightType.Point) list.Add(l);
            return list;
        }
    }
}
