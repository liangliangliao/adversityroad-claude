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
        // 夜晚保底亮度再上调（实测截图：黄昏/夜间的地面与建筑几乎全黑，只剩天空有色）。
        // 0.30 的环境光在 URP 下配合 0.14 的主光，物体表面照度不足 0.2，手机屏幕上
        // 就是一片糊黑。抬到 0.42 并给一点冷蓝色，暗调仍在，但地面/墙面能分辨出层次。
        static readonly Color NightAmbient = new Color(0.46f, 0.48f, 0.58f);

        /// <summary>室内/有顶关卡的照明保底：这些地方晒不到太阳，白天照样黑。
        /// 每 0.5 秒探一次镜头头顶有没有遮挡，有就把环境光抬到夜间保底之上，
        /// 让"拖延线之后那一串走廊/大厅/审判庭"在任何时段都看得清人和物。</summary>
        static readonly Color IndoorAmbient = new Color(0.54f, 0.55f, 0.62f);
        float _nextRoofProbe;
        float _indoorBlend;
        static readonly Color DaySun = new Color(1f, 0.96f, 0.9f);
        static readonly Color DuskSun = new Color(1f, 0.55f, 0.3f);

        bool _lampsOn;
        bool _roofed;

        static float Luma(Color c) => c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;

        void Update()
        {
            time01 = Mathf.Repeat(time01 + Time.deltaTime / dayLength, 1f);
            float dayFactor = Mathf.Clamp01(Mathf.Sin(time01 * Mathf.PI * 2f)); // 白天 1 → 夜晚 0

            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(time01 * 360f, 40f, 0);
                // 夜间主光 0.14→0.30：只留轮廓是没错，但 0.14 下连轮廓都看不出来，
                // 角色身上大片区域直接黑掉（截图里"角色身上有影子"的主因——
                // 那不是投影，是背光面根本没有照度）
                sun.intensity = Mathf.Lerp(0.30f, 1.15f, dayFactor);
                sun.color = Color.Lerp(DuskSun, DaySun, Mathf.Clamp01(dayFactor * 2f));
            }

            // 有顶判定（0.5 秒一次，开销可忽略）：镜头头顶 25 米内有实体＝在室内/有顶结构下
            if (Time.time >= _nextRoofProbe)
            {
                _nextRoofProbe = Time.time + 0.5f;
                var cam = Camera.main;
                bool roofed = cam != null && Physics.Raycast(
                    cam.transform.position, Vector3.up, 25f, ~0, QueryTriggerInteraction.Ignore);
                _roofed = roofed;
            }
            _indoorBlend = Mathf.MoveTowards(_indoorBlend, _roofed ? 1f : 0f, Time.deltaTime / 0.8f);

            Color ambient = Color.Lerp(NightAmbient, DayAmbient, dayFactor);
            // 室内保底：只抬不压——室内环境光比当前昼夜值亮时才混过去，
            // 免得大白天走进屋里反而变暗（那正是现在这串关卡的观感）。
            if (_indoorBlend > 0.001f && Luma(IndoorAmbient) > Luma(ambient))
                ambient = Color.Lerp(ambient, IndoorAmbient, _indoorBlend);
            RenderSettings.ambientLight = ambient;

            // 镜头补光随昼夜收放：白天足量补光去除迎镜脸部阴影；夜晚收到很低，
            // 保留夜的暗调氛围（不把整片场景照成平光白昼）。
            // 镜头补光随昼夜收放，但夜间下限从 0.12 抬到 0.34：
            // 补光是照亮"迎着镜头那一面"的，玩家角色永远迎镜——它塌到 0.12 时，
            // 主光又在身后，角色正面就没有任何光源，看上去像蒙了一层灰。
            if (cameraFill != null)
                cameraFill.intensity = Mathf.Max(
                    Mathf.Lerp(0.34f, cameraFillDay, dayFactor),
                    _indoorBlend * 0.5f);   // 室内：补光不得低于 0.5，迎镜的脸不能是一团黑

            // 距离雾：极淡，只作远景层次，不遮挡视野（可看清整片区域与远方建筑）。
            // 雾色改为跟随"当前所在区域"的专属色彩脚本——沼泽偏绿、断桥幽蓝、
            // 图书馆昏黄……切换区域时平滑过渡（避免瞬切），强化分区氛围。
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = Mathf.Lerp(0.0028f, 0.0014f, dayFactor);
            Color zoneFog = ZoneBuilder.FogTintOf(ZoneBuilder.IndexOfZone(ZoneBuilder.CurrentZoneId));
            Color dayFog = Color.Lerp(zoneFog, new Color(0.72f, 0.78f, 0.84f), 0.55f);
            Color fogTarget = Color.Lerp(zoneFog, dayFog, dayFactor);
            RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, fogTarget, Time.deltaTime * 2.5f);

            // 路灯提前点亮（0.25→0.40）：黄昏时段场景已经暗下来了，灯却还没亮，
            // 那段时间最难看清——现实里的路灯也是天还没全黑就亮了
            bool night = dayFactor < 0.40f;
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
