using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.OpenWorld
{
    /// <summary>
    /// 能打开的出水口（水龙头 / 花洒）。
    ///
    /// 玩家的要求是"洗漱间可打开水龙头，有水从喷头流出"。
    /// 水柱是一根半透明的圆柱：打开时伸长到落水面、并轻微抖动；
    /// 底下同时出现一小圈溅起的水花。关掉就收回去。
    ///
    /// 不用粒子系统：这栋房子里所有东西都是运行时几何，一根会动的圆柱
    /// 已经把"水在流"表达清楚了，而且开销近乎为零。
    /// </summary>
    public class WaterOutlet : MonoBehaviour
    {
        public Vector3 spoutAt;
        public float radius = 0.06f;
        public float fallLength = 0.4f;
        public string label = "水 龙 头";
        public float range = 2.8f;

        Transform _stream;
        Transform _splash;
        bool _on;
        float _lastHint = -99f;
        PlayerController _player;

        public static WaterOutlet Attach(GameObject host, Vector3 spoutAt, float radius,
            float fallLength, string label)
        {
            if (host == null) return null;
            var w = host.AddComponent<WaterOutlet>();
            w.spoutAt = spoutAt;
            w.radius = radius;
            w.fallLength = fallLength;
            w.label = label;
            return w;
        }

        void Start()
        {
            var water = new Color(0.62f, 0.82f, 0.92f);
            var stream = VillaKit.Cyl("WaterStream", spoutAt - new Vector3(0, fallLength, 0),
                radius, fallLength, water);
            VillaKit.Glass(stream, water, 0.45f);
            _stream = stream.transform;

            var splash = VillaKit.Cyl("WaterSplash", spoutAt - new Vector3(0, fallLength, 0),
                radius * 2.4f, 0.05f, water);
            VillaKit.Glass(splash, water, 0.35f);
            _splash = splash.transform;

            SetOn(false);
        }

        void SetOn(bool on)
        {
            _on = on;
            if (_stream != null) _stream.gameObject.SetActive(on);
            if (_splash != null) _splash.gameObject.SetActive(on);
        }

        void Update()
        {
            // 坐着/躺着的时候不抢交互键（消费式读取，只能有一个消费者）——
            // 否则在床上按"起身"会被旁边的水龙头吃掉，人永远起不来
            if (SitController.Busy) return;
            if (_player == null)
            {
                _player = FindObjectOfType<PlayerController>();
                if (_player == null) return;
            }

            // 水流轻微抖动：一根一动不动的圆柱看着像塑料棒，不像水
            if (_on && _stream != null)
            {
                float w = 1f + Mathf.Sin(Time.time * 22f) * 0.07f;
                _stream.localScale = new Vector3(radius * 2f * w, fallLength / 2f, radius * 2f * w);
                if (_splash != null)
                {
                    float p = 1f + Mathf.Sin(Time.time * 9f) * 0.18f;
                    _splash.localScale = new Vector3(radius * 4.8f * p, 0.025f, radius * 4.8f * p);
                }
            }

            if (Vector3.Distance(spoutAt, _player.transform.position) > range) return;

            if (Time.time - _lastHint > 8f)
            {
                _lastHint = Time.time;
                GameEvents.RaiseSubtitle("【" + label + "】" + Mobile.MobileInput.UseHint +
                    (_on ? "关水。" : "放水。"));
            }
            if (Input.GetKeyDown(KeyCode.E) || Mobile.MobileInput.GetDown("Interact"))
            {
                SetOn(!_on);
                GameEvents.RaiseSubtitle(_on ? "水哗地流了下来。" : "水关上了。");
                GameAudio.Play(GameAudio.Sfx.Cast, 0.3f);
            }
        }
    }
}
