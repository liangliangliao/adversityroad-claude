using System.Collections.Generic;
using UnityEngine;

namespace AdversityRoad.World
{
    /// <summary>
    /// 昼夜循环：太阳旋转 + 光强/色温 + 环境光 + 路灯自动开关。
    /// </summary>
    public class DayNightCycle : MonoBehaviour
    {
        public Light sun;
        public float dayLength = 240f;                 // 一个完整昼夜的秒数
        [Range(0f, 1f)] public float time01 = 0.22f;   // 0=日出 0.25=正午 0.75=午夜

        public readonly List<Light> lamps = new List<Light>();
        public readonly List<Renderer> lampHeads = new List<Renderer>();

        static readonly Color DayAmbient = new Color(0.56f, 0.56f, 0.6f);
        static readonly Color NightAmbient = new Color(0.09f, 0.1f, 0.17f);
        static readonly Color DaySun = new Color(1f, 0.96f, 0.9f);
        static readonly Color DuskSun = new Color(1f, 0.55f, 0.3f);

        bool _lampsOn;

        void Update()
        {
            time01 = Mathf.Repeat(time01 + Time.deltaTime / dayLength, 1f);
            float dayFactor = Mathf.Clamp01(Mathf.Sin(time01 * Mathf.PI * 2f)); // 白天 1 → 夜晚 0

            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(time01 * 360f, 40f, 0);
                sun.intensity = Mathf.Lerp(0.03f, 1.15f, dayFactor);
                sun.color = Color.Lerp(DuskSun, DaySun, Mathf.Clamp01(dayFactor * 2f));
            }

            RenderSettings.ambientLight = Color.Lerp(NightAmbient, DayAmbient, dayFactor);

            bool night = dayFactor < 0.25f;
            if (night != _lampsOn)
            {
                _lampsOn = night;
                foreach (var l in lamps) if (l != null) l.enabled = night;
                foreach (var r in lampHeads)
                {
                    if (r == null) continue;
                    var c = night ? new Color(1f, 0.9f, 0.55f) : new Color(0.45f, 0.45f, 0.4f);
                    r.material.color = c;
                    if (r.material.HasProperty("_BaseColor")) r.material.SetColor("_BaseColor", c);
                }
            }
        }
    }
}
