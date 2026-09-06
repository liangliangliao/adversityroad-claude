using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.OpenWorld
{
    /// <summary>
    /// 能打开的出水口（水龙头 / 花洒）。
    ///
    /// 【上一版为什么是"满屏白色划痕"——两个数字算一遍就清楚】
    /// 一、粒子寿命是按 fallLength / speed 算的，**把重力漏掉了**。
    ///     花洒 fallLength=2.3、初速 3.2，算出寿命 0.84 秒；但这 0.84 秒里
    ///     水实际下落 3.2×0.84 + ½×9.81×0.84² = 6.13 米——
    ///     喷头到地砖只有 2.3 米，剩下的 3.8 米水穿过地板一直落到楼下去了。
    ///     玩家看到的"从天到地的一片白"，就是这段本不该存在的水。
    /// 二、Stretch 渲染模式下，水珠长度 = velocityScale × 速度。
    ///     末速已经 11.4 m/s，×0.06 = 每颗被拉成 0.69 米长的一条。
    ///     真实淋浴的水珠在快门下也就拉出几厘米，0.69 米只能读成划痕。
    ///
    /// 【这一版怎么做】
    /// 落地时间由抛体公式解出来：t = (-v₀ + √(v₀² + 2gh)) / g，水正好落在托盘上，
    /// 一滴都不会穿过地板。拉伸长度按"落地那一刻"反推并封顶，快也只是几厘米的水线。
    ///
    /// 淋浴不是一股水，是四层叠起来的：
    ///   ① 主射流——从花洒面盘整个圆面射出的细密水线，越往下越快、越细、越散；
    ///   ② 边缘水雾——射流外圈那层慢而淡的细雾，淋浴间里"看不清对面"就靠它；
    ///   ③ 落点溅射——打在托盘上四散弹起的小水珠，再被重力拽回去；
    ///   ④ 升腾的水汽——热水才有，大颗、极淡、缓慢上升，是"这水是热的"唯一的视觉证据。
    /// 再加托盘上一层随水波动的湿膜，和一段贴着面盘的连续水束（射流出口那一小截还没碎）。
    ///
    /// 声音也是细节的一部分：淋浴声本质就是被低通滤过的白噪声，
    /// 所以直接在运行时合成一段循环噪声，不引入任何音频资源。
    /// </summary>
    public class WaterOutlet : MonoBehaviour
    {
        public Vector3 spoutAt;
        public float radius = 0.06f;
        public float fallLength = 0.4f;
        public string label = "水 龙 头";
        public float range = 2.8f;

        Transform _core;          // 出水口下那一小段还没碎的连续水束
        ParticleSystem _jet;      // 主射流
        ParticleSystem _mist;     // 边缘水雾（只有花洒有）
        ParticleSystem _splash;   // 落点溅射
        ParticleSystem _steam;    // 升腾的水汽（只有花洒有）
        Transform _film;          // 落点的湿膜
        AudioSource _audio;
        bool _on;
        float _lastHint = -99f;
        PlayerController _player;

        /// <summary>口径大于这个值算喷淋（花洒），否则是一股水柱（水龙头）。</summary>
        const float SprayRadius = 0.09f;

        bool Spray => radius >= SprayRadius;

        static readonly Color WaterTint = new Color(0.78f, 0.90f, 0.97f);

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

        /// <summary>
        /// 竖直下抛，落 h 米要多久：h = v₀t + ½gt² 解出 t。
        /// 粒子的寿命必须等于这个数——短了水在半空消失，长了水穿过地板。
        /// </summary>
        static float FallTime(float h, float v0, float gMul)
        {
            float g = 9.81f * Mathf.Max(0.05f, gMul);
            return (-v0 + Mathf.Sqrt(v0 * v0 + 2f * g * Mathf.Max(0.01f, h))) / g;
        }

        void Start()
        {
            float drop = Mathf.Max(0.05f, fallLength);
            Vector3 hit = spoutAt - new Vector3(0, drop, 0);

            // ---- 出口那一小截连续水束 ----
            float coreLen = Mathf.Min(drop * 0.16f, Spray ? 0.08f : 0.20f);
            float coreR = radius * (Spray ? 0.62f : 0.42f);
            var core = VillaKit.Cyl("WaterCore", spoutAt - new Vector3(0, coreLen, 0),
                coreR, coreLen, WaterTint);
            VillaKit.Glass(core, WaterTint, 0.5f);
            _core = core.transform;

            // ---- ① 主射流 ----
            // 花洒出水初速比水龙头高：面盘把水压成细流射出来，不是"淌"下来的
            float v0 = Spray ? 3.4f : 2.2f;
            float life = FallTime(drop, v0, 1f);
            _jet = MakeSystem("WaterJet", spoutAt, false, new ParticleSpec
            {
                rate = Spray ? 460f : 190f,
                coneAngle = Spray ? 7f : 1.6f,     // 花洒的锥角很小：水是"射"下来的，不是喷雾罐
                startRadius = radius * (Spray ? 0.92f : 0.34f),
                speedMin = v0 * 0.9f,
                speedMax = v0 * 1.12f,
                sizeMin = Spray ? 0.006f : 0.016f,
                sizeMax = Spray ? 0.014f : 0.034f,
                lifeMin = life * 0.94f,
                lifeMax = life,
                gravity = 1f,
                // 落地末速 v0+g·t，按它反推拉伸系数，让最长的一条也只有 5 厘米
                stretch = 0.05f / Mathf.Max(0.5f, v0 + 9.81f * life),
                alpha = Spray ? 0.5f : 0.62f,
                fadeIn = false,
                max = 320,
            });

            if (Spray)
            {
                // ---- ② 边缘水雾 ----
                // 淋浴间里"看不清对面"的那层雾。它不能有拉伸——雾是团的，不是线的
                _mist = MakeSystem("WaterMist", spoutAt, false, new ParticleSpec
                {
                    rate = 70f,
                    coneAngle = 24f,
                    startRadius = radius * 1.05f,
                    speedMin = 0.7f,
                    speedMax = 1.7f,
                    sizeMin = 0.05f,
                    sizeMax = 0.13f,
                    lifeMin = 0.6f,
                    lifeMax = 1.0f,
                    gravity = 0.28f,
                    stretch = 0f,
                    alpha = 0.10f,
                    fadeIn = true,
                    max = 90,
                });

                // ---- ④ 升腾的水汽 ----
                // 从托盘往上飘，大而极淡。没有它，这水看着就是凉的
                _steam = MakeSystem("WaterSteam", hit + new Vector3(0, 0.06f, 0), true,
                    new ParticleSpec
                    {
                        rate = 16f,
                        coneAngle = 34f,
                        startRadius = radius * 2.2f,
                        speedMin = 0.22f,
                        speedMax = 0.55f,
                        sizeMin = 0.22f,
                        sizeMax = 0.5f,
                        lifeMin = 1.6f,
                        lifeMax = 2.6f,
                        gravity = -0.015f,      // 比空气轻，缓慢上浮
                        stretch = 0f,
                        alpha = 0.055f,
                        fadeIn = true,
                        max = 60,
                    });
            }

            // ---- ③ 落点溅射 ----
            _splash = MakeSystem("WaterSplash", hit, true, new ParticleSpec
            {
                rate = Spray ? 170f : 90f,
                coneAngle = 74f,                   // 近乎半球：溅起来是四散的
                startRadius = radius * (Spray ? 1.5f : 0.85f),
                speedMin = 0.45f,
                speedMax = Spray ? 1.25f : 1.5f,
                sizeMin = 0.006f,
                sizeMax = 0.018f,
                lifeMin = 0.16f,
                lifeMax = 0.32f,
                gravity = 1.7f,
                stretch = 0.012f,
                alpha = 0.5f,
                fadeIn = false,
                max = 120,
            });

            // ---- 落点湿膜 ----
            var film = VillaKit.Cyl("WaterFilm", hit + new Vector3(0, 0.004f, 0),
                radius * 2.4f, 0.008f, WaterTint);
            VillaKit.Glass(film, WaterTint, 0.26f);
            _film = film.transform;

            BuildAudio();
            SetOn(false);
        }

        struct ParticleSpec
        {
            public float rate, coneAngle, startRadius, speedMin, speedMax;
            public float sizeMin, sizeMax, lifeMin, lifeMax, gravity, stretch, alpha;
            public bool fadeIn;      // 雾与水汽要淡入；水珠一出口就是实的
            public int max;
        }

        /// <summary>建一个粒子系统。upward=true 朝上（溅射、水汽），否则朝下出水。</summary>
        ParticleSystem MakeSystem(string name, Vector3 at, bool upward, ParticleSpec spec)
        {
            var go = new GameObject(name);
            if (VillaKit.Root != null) go.transform.SetParent(VillaKit.Root, true);
            go.transform.position = at;
            // 粒子系统的喷射方向是本地 +Y：出水朝下要绕 X 转 180°，朝上不转
            go.transform.rotation = upward ? Quaternion.identity : Quaternion.Euler(180f, 0, 0);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop();

            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(spec.lifeMin, spec.lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(spec.speedMin, spec.speedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(spec.sizeMin, spec.sizeMax);
            main.startColor = new Color(WaterTint.r, WaterTint.g, WaterTint.b, spec.alpha);
            main.gravityModifier = spec.gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = spec.max;
            main.playOnAwake = false;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = spec.rate;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = spec.coneAngle;
            shape.radius = Mathf.Max(0.004f, spec.startRadius);
            shape.radiusThickness = 1f;   // 整个圆面出水，不是一圈——花洒面盘布满孔

            // 水珠越掉越细（射流被拉断），雾与水汽反过来越飘越大
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, spec.fadeIn
                ? new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.5f))
                : new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.6f)));

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            var alphaKeys = spec.fadeIn
                ? new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f),
                          new GradientAlphaKey(0f, 1f) }
                : new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.92f, 0.7f),
                          new GradientAlphaKey(0f, 1f) };
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                alphaKeys);
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var r = ps.GetComponent<ParticleSystemRenderer>();
            if (spec.stretch > 0f)
            {
                // 拉伸公告板：水珠沿速度方向拉成一根短水线，这是"水在流"最直接的读法。
                // 长度 = velocityScale × 速度，所以这个系数已经在上面按末速反推过，
                // 保证最快的那一颗也只有几厘米——而不是上一版的 0.69 米。
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = spec.stretch;
                r.lengthScale = 1f;
            }
            else
            {
                r.renderMode = ParticleSystemRenderMode.Billboard;
            }
            r.sharedMaterial = WaterMaterial();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingFudge = -1f;
            return ps;
        }

        // ---- 粒子材质与贴图：运行时画一张软圆点，全项目共用一份 ----
        static Material _waterMat;

        static Material WaterMaterial()
        {
            if (_waterMat != null) return _waterMat;
            // 走 SafeShader：这些名字里只有 Sprites/Default 一定在安卓包里，
            // 直接 Shader.Find 拿到 null 就会 new Material(null)，结果是一片洋红
            var sh = World.SafeShader.Find(
                "Universal Render Pipeline/Particles/Unlit",
                "Legacy Shaders/Particles/Alpha Blended",
                "Sprites/Default",
                "Universal Render Pipeline/Unlit");
            // Find 一个都没找到时会返回 null，而 new Material(null) 的结果就是满屏洋红。
            // SafeShader.Unlit 内部还有一层"退回基础材质"的保底，借它的 shader 用。
            if (sh == null) sh = World.SafeShader.Unlit(Color.white, "waterfx").shader;
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

        // ---- 水声：低通白噪声，运行时合成 ----
        static AudioClip _noise;

        /// <summary>
        /// 淋浴声本质上就是被低通滤过的白噪声——所以直接合成一段两秒的循环，
        /// 不引入任何音频资源。滤波用的是最朴素的一阶滑动平均：
        /// 纯白噪声太"嘶"，滤一下才像水打在托盘上。
        /// </summary>
        static AudioClip NoiseClip()
        {
            if (_noise != null) return _noise;
            const int rate = 22050, len = rate * 2;
            var data = new float[len];
            var rnd = new System.Random(7);
            float lp = 0f;
            for (int i = 0; i < len; i++)
            {
                float w = (float)(rnd.NextDouble() * 2.0 - 1.0);
                lp += (w - lp) * 0.22f;                 // 一阶低通
                data[i] = lp * 0.8f;
            }
            // 首尾各做 512 采样的交叉淡入淡出，循环接缝处不会"啪"一声
            const int fade = 512;
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                data[i] = Mathf.Lerp(data[len - fade + i], data[i], k);
            }
            var clip = AudioClip.Create("WaterNoise", len, 1, rate, false);
            clip.SetData(data, 0);
            _noise = clip;
            return clip;
        }

        void BuildAudio()
        {
            var go = new GameObject("WaterSound");
            go.transform.SetParent(transform, false);
            go.transform.position = spoutAt - new Vector3(0, fallLength * 0.5f, 0);
            _audio = go.AddComponent<AudioSource>();
            _audio.clip = NoiseClip();
            _audio.loop = true;
            _audio.playOnAwake = false;
            _audio.spatialBlend = 1f;                  // 全 3D：走远了自然听不见
            _audio.rolloffMode = AudioRolloffMode.Linear;
            _audio.minDistance = 1.2f;
            _audio.maxDistance = Spray ? 11f : 6f;
            _audio.volume = Spray ? 0.35f : 0.22f;
            _audio.pitch = Spray ? 1f : 1.25f;          // 水龙头细，音色更高
        }

        void SetOn(bool on)
        {
            _on = on;
            if (_core != null) _core.gameObject.SetActive(on);
            if (_film != null) _film.gameObject.SetActive(on);
            Toggle(_jet, on);
            Toggle(_mist, on);
            Toggle(_splash, on);
            Toggle(_steam, on);
            if (_audio != null) { if (on) _audio.Play(); else _audio.Stop(); }
        }

        static void Toggle(ParticleSystem ps, bool on)
        {
            if (ps == null) return;
            if (on) ps.Play(); else ps.Stop();
        }

        void Update()
        {
            if (_player == null)
            {
                _player = AdversityRoad.Core.ActorRegistry.Player;
                if (_player == null) return;
            }

            // 水束与湿膜的脉动：一动不动的水看着像塑料。
            // 两个频率不同且互质——同频会读成"整体在呼吸"，那比不动还假
            if (_on)
            {
                if (_core != null)
                {
                    float w = radius * (Spray ? 1.24f : 0.9f) *
                              (1f + Mathf.Sin(Time.time * 31f) * 0.07f);
                    var s = _core.localScale;
                    _core.localScale = new Vector3(w, s.y, w);
                }
                if (_film != null)
                {
                    float p = radius * 4.8f * (1f + Mathf.Sin(Time.time * 6.3f) * 0.16f
                                                 + Mathf.Sin(Time.time * 11.7f) * 0.07f);
                    _film.localScale = new Vector3(p, 0.006f, p);
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
            }
        }
    }
}
