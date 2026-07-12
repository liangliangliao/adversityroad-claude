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
        public Light cameraFill;                       // 镜头补光（headlight）：白天照亮迎镜脸部
        public float cameraFillDay = 0.5f;             // 白天补光强度（去脸部死黑即可，过强会冲淡模型本色）
        public float dayLength = 240f;                 // 一个完整昼夜的秒数
        [Range(0f, 1f)] public float time01 = 0.22f;   // 0=日出 0.25=正午 0.75=午夜

        public readonly List<Light> lamps = new List<Light>();
        public readonly List<Renderer> lampHeads = new List<Renderer>();

        static readonly Color DayAmbient = new Color(0.56f, 0.56f, 0.6f);
        // 夜晚保底亮度上调：录屏实测夜战/暗场景整体过暗，敌我动作看不清——
        // 战斗可读性优先于氛围，保底照度要能看清双方的拳脚与前摇
        static readonly Color NightAmbient = new Color(0.3f, 0.31f, 0.42f);
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
                sun.intensity = Mathf.Lerp(0.14f, 1.15f, dayFactor);   // 夜晚也留主光轮廓
                sun.color = Color.Lerp(DuskSun, DaySun, Mathf.Clamp01(dayFactor * 2f));
            }

            RenderSettings.ambientLight = Color.Lerp(NightAmbient, DayAmbient, dayFactor);

            // 镜头补光随昼夜收放：白天足量补光去除迎镜脸部阴影；夜晚收到很低，
            // 保留夜的暗调氛围（不把整片场景照成平光白昼）。
            if (cameraFill != null)
                cameraFill.intensity = Mathf.Lerp(0.12f, cameraFillDay, dayFactor);

            // 距离雾：极淡，只作远景层次，不遮挡视野（可看清整片区域与远方建筑）
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = Mathf.Lerp(0.0028f, 0.0014f, dayFactor);
            RenderSettings.fogColor = Color.Lerp(
                new Color(0.08f, 0.1f, 0.17f), new Color(0.72f, 0.78f, 0.84f), dayFactor);

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
