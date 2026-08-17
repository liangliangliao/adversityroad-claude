using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.OpenWorld
{
    /// <summary>
    /// 能打开的出水口（水龙头 / 花洒）。
    ///
    /// 【上一版为什么"分明不是水"】玩家一句话说到底了："这是一个长圆图形而已"。
    /// 上一版就是一根半透明圆柱：**没有下落、没有破碎、没有溅起、没有随机**。
    /// 水看起来像水，靠的从来不是"蓝色半透明"，而是这四件事：
    ///   ① 水是**一颗颗往下掉的**，而且越掉越快（重力加速度）；
    ///   ② 出水口下面有一小段**完整的水束**，再往下才碎成水珠（真实射流的规律）；
    ///   ③ 落到台面上会**溅起来**，四散、回落；
    ///   ④ 全程都在随机抖动，没有两帧是一样的。
    ///
    /// 所以这一版改成粒子：下落的水是拉伸的水线/水珠，落点有溅射和一圈水花，
    /// 出水口下方保留一小段连续水束。花洒（口径大）自动变成锥形喷淋——
    /// 一片细雨而不是一根柱子。粒子贴图是运行时画出来的一张软圆点（径向渐变），
    /// 不引入任何美术资源。
    /// </summary>
    public class WaterOutlet : MonoBehaviour
    {
        public Vector3 spoutAt;
        public float radius = 0.06f;
        public float fallLength = 0.4f;
        public string label = "水 龙 头";
        public float range = 2.8f;

        Transform _core;          // 出水口下的一小段连续水束
        ParticleSystem _jet;      // 下落的水（水线 + 水珠）
        ParticleSystem _splash;   // 落点溅射
        Transform _ring;          // 落点扩散的水花圈
        bool _on;
        float _lastHint = -99f;
        PlayerController _player;

        /// <summary>口径大于这个值算喷淋（花洒），否则是一股水柱（水龙头）。</summary>
        const float SprayRadius = 0.15f;

        bool Spray => radius >= SprayRadius;

        static readonly Color WaterTint = new Color(0.72f, 0.88f, 0.97f);

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
            // ---- 出水口下的一小段连续水束（射流刚出口时是完整的） ----
            float coreLen = Mathf.Min(fallLength * 0.28f, Spray ? 0.12f : 0.22f);
            float coreR = radius * (Spray ? 0.5f : 0.42f);
            var core = VillaKit.Cyl("WaterCore", spoutAt - new Vector3(0, coreLen, 0),
                coreR, coreLen, WaterTint);
            VillaKit.Glass(core, WaterTint, 0.55f);
            _core = core.transform;

            // ---- 下落的水 ----
            float speed = Spray ? 3.2f : 2.4f;
            _jet = MakeSystem("WaterJet", spoutAt, false, new ParticleSpec
            {
                rate = Spray ? 300f : 170f,
                coneAngle = Spray ? 17f : 2.5f,
                startRadius = radius * (Spray ? 0.85f : 0.35f),
                speedMin = speed * 0.85f,
                speedMax = speed * 1.15f,
                sizeMin = Spray ? 0.012f : 0.022f,
                sizeMax = Spray ? 0.026f : 0.05f,
                life = Mathf.Clamp(fallLength / speed + 0.12f, 0.18f, 1.4f),
                gravity = 1f,
                stretch = Spray ? 2.2f : 3.4f,
                alpha = Spray ? 0.42f : 0.6f,
            });

            // ---- 落点：溅起的水珠 + 一圈扩散的水花 ----
            Vector3 hit = spoutAt - new Vector3(0, fallLength, 0);
            _splash = MakeSystem("WaterSplash", hit, true, new ParticleSpec
            {
                rate = Spray ? 150f : 85f,
                coneAngle = 78f,                  // 近乎半球：溅起来是四散的
                startRadius = radius * (Spray ? 1.6f : 0.9f),
                speedMin = 0.5f,
                speedMax = Spray ? 1.3f : 1.6f,
                sizeMin = 0.01f,
                sizeMax = 0.03f,
                life = 0.34f,
                gravity = 1.6f,
                stretch = 1.4f,
                alpha = 0.5f,
            });

            var ring = VillaKit.Cyl("WaterRing", hit + new Vector3(0, 0.004f, 0),
                radius * 2.2f, 0.012f, WaterTint);
            VillaKit.Glass(ring, WaterTint, 0.3f);
            _ring = ring.transform;

            SetOn(false);
        }

        struct ParticleSpec
        {
            public float rate, coneAngle, startRadius, speedMin, speedMax;
            public float sizeMin, sizeMax, life, gravity, stretch, alpha;
        }

        /// <summary>建一个粒子系统。upward=true 是落点的溅射（朝上），否则朝下出水。</summary>
        ParticleSystem MakeSystem(string name, Vector3 at, bool upward, ParticleSpec spec)
        {
            var go = new GameObject(name);
            if (VillaKit.Root != null) go.transform.SetParent(VillaKit.Root, true);
            go.transform.position = at;
            // 粒子系统的喷射方向是本地 +Y：出水要朝下，所以绕 X 转 180°；溅射朝上不转。
            go.transform.rotation = upward ? Quaternion.identity : Quaternion.Euler(180f, 0, 0);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop();

            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(spec.life * 0.75f, spec.life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(spec.speedMin, spec.speedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(spec.sizeMin, spec.sizeMax);
            main.startColor = new Color(WaterTint.r, WaterTint.g, WaterTint.b, spec.alpha);
            main.gravityModifier = spec.gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            main.playOnAwake = false;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = spec.rate;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = spec.coneAngle;
            shape.radius = Mathf.Max(0.005f, spec.startRadius);

            // 水珠越掉越细、越落越淡
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.55f)));

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.55f),
                        new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var r = ps.GetComponent<ParticleSystemRenderer>();
            // 拉伸公告板：水珠沿速度方向拉长成一根水线，这是"水在流"最直接的读法
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.06f;
            r.lengthScale = spec.stretch;
            r.sharedMaterial = WaterMaterial();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        // ---- 粒子材质与贴图：运行时画一张软圆点，全项目共用一份 ----
        static Material _waterMat;

        static Material WaterMaterial()
        {
            if (_waterMat != null) return _waterMat;
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            var m = new Material(sh) { name = "WaterDroplet" };
            var tex = DropletTexture();
            m.mainTexture = tex;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            m.color = Color.white;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            _waterMat = m;
            return m;
        }

        static Texture2D _droplet;

        /// <summary>一张 32×32 的软圆点（中心亮、边缘渐隐）：水珠就靠它。</summary>
        static Texture2D DropletTexture()
        {
            if (_droplet != null) return _droplet;
            const int n = 32;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "Droplet" };
            t.wrapMode = TextureWrapMode.Clamp;
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                    float d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) * 2f);   // 0=中心 1=边
                    float a = (1f - d) * (1f - d);                                 // 软边
                    // 中间一点高光：水珠有反光才不像一团雾
                    float spec = Mathf.Clamp01(1f - d * 2.2f) * 0.35f;
                    px[y * n + x] = new Color(1f + spec, 1f + spec, 1f + spec, a);
                }
            t.SetPixels(px);
            t.Apply(false, false);
            _droplet = t;
            return t;
        }

        void SetOn(bool on)
        {
            _on = on;
            if (_core != null) _core.gameObject.SetActive(on);
            if (_ring != null) _ring.gameObject.SetActive(on);
            if (_jet != null) { if (on) _jet.Play(); else _jet.Stop(); }
            if (_splash != null) { if (on) _splash.Play(); else _splash.Stop(); }
        }

        void Update()
        {
            if (_player == null)
            {
                _player = FindObjectOfType<PlayerController>();
                if (_player == null) return;
            }

            // 水束与水花的脉动：一动不动的水看着像塑料
            if (_on)
            {
                if (_core != null)
                {
                    float w = radius * 0.9f * (1f + Mathf.Sin(Time.time * 26f) * 0.09f);
                    var s = _core.localScale;
                    _core.localScale = new Vector3(w, s.y, w);
                }
                if (_ring != null)
                {
                    float p = radius * 4.4f * (1f + Mathf.Sin(Time.time * 7.5f) * 0.22f);
                    _ring.localScale = new Vector3(p, 0.006f, p);
                }
            }

            // 坐着/躺着的时候不抢交互键（消费式读取，只能有一个消费者）——
            // 否则在床上按"起身"会被旁边的水龙头吃掉，人永远起不来
            if (SitController.Busy) return;
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
