using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Platform;
using AdversityRoad.World;

namespace AdversityRoad.OpenWorld
{
    /// <summary>
    /// 客厅那面墙上的超大屏液晶电视。
    ///
    /// 建的是一台真电视：黑色窄边框、亮着的屏幕、下面一条音响、墙上的挂架、
    /// 待机时右下角一颗红色指示灯。屏幕本体挂 TvSet，负责画面与播放。
    /// </summary>
    public static class WallTv
    {
        /// <summary>
        /// center = 屏幕中心；faceDir = 屏幕朝向（从墙指向观众）；width = 屏幕对角那条边的**宽**（米）。
        /// 高按 16:9 算。
        /// </summary>
        public static TvSet Build(WorldContext ctx, Vector3 center, Vector3 faceDir, float width)
        {
            faceDir = faceDir.normalized;
            bool normalZ = Mathf.Abs(faceDir.z) >= Mathf.Abs(faceDir.x);   // 法线沿 Z ⇒ 画面横向沿 X
            float w = Mathf.Max(0.6f, width);
            float h = w * 9f / 16f;
            const float panelT = 0.10f;    // 屏幕板厚
            const float bezel = 0.11f;     // 边框宽
            const float bezelT = 0.17f;    // 边框比屏幕厚一点：像"框住"屏幕

            var black = new Color(0.06f, 0.06f, 0.07f);
            var dark = new Color(0.11f, 0.11f, 0.13f);

            Vector3 panelSize = normalZ ? new Vector3(w, h, panelT) : new Vector3(panelT, h, w);
            var screen = VillaKit.Deco("TvScreen", center, panelSize, black);

            // 边框走四条边（**不能**在屏幕正后方压一整块板：两个面同深度会打架，
            // 上一轮相框的色块就是这么来的）
            Vector3 wide = normalZ ? new Vector3(w + bezel * 2f, bezel, bezelT)
                                   : new Vector3(bezelT, bezel, w + bezel * 2f);
            Vector3 tall = normalZ ? new Vector3(bezel, h, bezelT)
                                   : new Vector3(bezelT, h, bezel);
            Vector3 side = normalZ ? new Vector3(w / 2f + bezel / 2f, 0, 0)
                                   : new Vector3(0, 0, w / 2f + bezel / 2f);
            VillaKit.Deco("TvBezel_T", center + new Vector3(0, h / 2f + bezel / 2f, 0), wide, dark);
            VillaKit.Deco("TvBezel_B", center - new Vector3(0, h / 2f + bezel / 2f, 0), wide, dark);
            VillaKit.Deco("TvBezel_L", center - side, tall, dark);
            VillaKit.Deco("TvBezel_R", center + side, tall, dark);

            // 挂架：藏在屏幕后面，只有从侧面能看见一点，电视才不像贴在墙上的一张纸
            Vector3 armSize = normalZ ? new Vector3(w * 0.22f, h * 0.5f, 0.16f)
                                      : new Vector3(0.16f, h * 0.5f, w * 0.22f);
            VillaKit.Deco("TvMount", center - faceDir * 0.16f, armSize, new Color(0.20f, 0.20f, 0.22f));

            // 音响条 + 两颗脚垫
            Vector3 barSize = normalZ ? new Vector3(w * 0.62f, 0.20f, 0.26f)
                                      : new Vector3(0.26f, 0.20f, w * 0.62f);
            var bar = VillaKit.Deco("TvSoundbar", center + new Vector3(0, -h / 2f - 0.46f, 0)
                - faceDir * 0.02f, barSize, new Color(0.14f, 0.14f, 0.16f));
            VillaKit.Metal(bar, new Color(0.18f, 0.18f, 0.20f), 0.5f);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 off = normalZ ? new Vector3(s * w * 0.26f, 0, 0) : new Vector3(0, 0, s * w * 0.26f);
                VillaKit.Deco("TvSpeakerGrill", center + new Vector3(0, -h / 2f - 0.46f, 0)
                    + off - faceDir * 0.14f, normalZ ? new Vector3(0.22f, 0.14f, 0.04f)
                                                     : new Vector3(0.04f, 0.14f, 0.22f),
                    new Color(0.07f, 0.07f, 0.08f));
            }

            // 待机指示灯：屏幕右下角外侧一颗小灯
            Vector3 ledOff = normalZ ? new Vector3(w * 0.42f, -h / 2f - bezel * 0.5f, 0)
                                     : new Vector3(0, -h / 2f - bezel * 0.5f, w * 0.42f);
            var led = VillaKit.Emit(VillaKit.Deco("TvLed", center + ledOff + faceDir * 0.06f,
                new Vector3(0.07f, 0.05f, 0.07f), new Color(0.8f, 0.1f, 0.1f)),
                new Color(0.85f, 0.12f, 0.12f), 1.4f);   // 自发光材质：待机灯要真的亮

            var set = screen.AddComponent<TvSet>();
            set.faceNormal = faceDir;
            set.widthM = w;
            set.heightM = h;
            set.led = led.GetComponent<MeshRenderer>();

            // 走近按交互键 → 打开电视面板（频道 / 播放 / 后台 / 小窗）
            var fx = HomeFixture.Attach(screen, HomeFixtureKind.Tv);
            fx.range = Mathf.Max(4.5f, w * 0.9f);
            return set;
        }
    }

    /// <summary>
    /// 电视机本体：频道、开关、画面。
    ///
    /// 【画面分两套，是权衡不是偷懒】
    /// ① 玩家站在电视正面时：真正的 YouTube 播放。手机上唯一能内嵌 YouTube 的办法
    ///    是 WebView，而 WebView 出不到贴图里（详见 Platform/WebScreen），
    ///    所以它是一块盖在"电视屏幕投影出来的矩形"上的原生网页层。
    /// ② 玩家在侧面、离得远、或这台设备没有网页层时：世界里的屏幕自己显示
    ///    程序生成的动态画面（每个频道一套配色）。这样从侧面看过去电视是亮着的、
    ///    在动的，而不是一块黑板。
    ///
    /// 【后台播放】收起画面时**不停播**，声音继续；应用被切到后台时保活网页层。
    /// 最靠得住的形态是悬浮小窗（PipMode）：窗口还在前台，系统不会掐声音。
    /// </summary>
    public class TvSet : MonoBehaviour
    {
        /// <summary>住所里只有一台，面板直接找它。</summary>
        public static TvSet Current;

        /// <summary>
        /// 暂时别把网页层摆上来。
        ///
        /// 网页层是原生视图，画在**整个 Unity 画面之上**——包括 UI。要是玩家正站在
        /// 电视前打开面板，视频会盖住面板，人就没法换台了。所以面板打开期间由它按住，
        /// 游戏暂停（timeScale 归零）时也按住。
        /// </summary>
        public static bool Suppressed;

        public Vector3 faceNormal = Vector3.back;
        public float widthM = 4.8f, heightM = 2.7f;
        public MeshRenderer led;

        public bool On { get; private set; }
        public bool Muted { get; private set; }
        public bool Background { get; private set; } = true;
        public int Channel { get; private set; }

        /// <summary>一个频道。id 有值走 YouTube，否则按普通网址加载。</summary>
        public struct Chan
        {
            public string name, id, url;
            public Chan(string n, string i, string u) { name = n; id = i; url = u; }
            public bool Empty => string.IsNullOrEmpty(id) && string.IsNullOrEmpty(url);
        }

        const string PrefOn = "tv_on", PrefChan = "tv_chan", PrefMute = "tv_mute";
        const string PrefBg = "tv_bg", PrefCustom = "tv_custom_";
        const int CustomSlots = 3;

        static readonly List<Chan> _chans = new List<Chan>();
        Texture2D _tex;
        Material _mat, _ledMat;
        Color32[] _px;
        float _nextFrame, _phase;
        bool _overlayShown;
        bool _pausedByLeaving;      // 是"切后台"把它暂停的（回来要恢复），不是玩家按的
        Camera _cam;

        // ================= 频道表 =================

        /// <summary>
        /// 频道表 = 三个内置 + 三个玩家自己粘链接的槽位。
        /// 内置的只是开箱即用的默认值；任何一条都能在面板里替换成自己的链接
        /// （长视频、直播、播放列表里的单集都行）。
        /// </summary>
        public static List<Chan> Channels
        {
            get
            {
                if (_chans.Count == 0)
                {
                    _chans.Add(new Chan("专注 · Lofi 电台", "jfKfPfyJRdk", ""));
                    _chans.Add(new Chan("开源动画 · Big Buck Bunny", "aqz-KE-bpKQ", ""));
                    _chans.Add(new Chan("动画短片 · Sintel", "eRsGyueVLvQ", ""));
                    for (int i = 0; i < CustomSlots; i++)
                    {
                        string saved = PlayerPrefs.GetString(PrefCustom + i, "");
                        string id = WebScreen.ParseVideoId(saved);
                        bool isUrl = string.IsNullOrEmpty(id) && saved.StartsWith("http");
                        _chans.Add(new Chan("我的频道 " + (i + 1), id, isUrl ? saved : ""));
                    }
                }
                return _chans;
            }
        }

        /// <summary>把玩家粘进来的链接写进第 slot 个自定义频道（slot 从 0 起）。</summary>
        public static bool SetCustom(int slot, string linkOrId)
        {
            if (slot < 0 || slot >= CustomSlots) return false;
            linkOrId = (linkOrId ?? "").Trim();
            string id = WebScreen.ParseVideoId(linkOrId);
            bool isUrl = string.IsNullOrEmpty(id) && linkOrId.StartsWith("http");
            if (string.IsNullOrEmpty(id) && !isUrl) return false;

            PlayerPrefs.SetString(PrefCustom + slot, linkOrId);
            PlayerPrefs.Save();
            int idx = 3 + slot;
            var list = Channels;
            list[idx] = new Chan(list[idx].name, id, isUrl ? linkOrId : "");
            return true;
        }

        // ================= 生命周期 =================

        void Awake()
        {
            Current = this;
            _cam = Camera.main;

            _tex = new Texture2D(64, 36, TextureFormat.RGBA32, false);
            _tex.wrapMode = TextureWrapMode.Clamp;
            _tex.filterMode = FilterMode.Bilinear;
            _px = new Color32[_tex.width * _tex.height];

            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Texture");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            _mat = new Material(sh) { name = "TvScreen" };
            _mat.mainTexture = _tex;
            if (_mat.HasProperty("_BaseMap")) _mat.SetTexture("_BaseMap", _tex);
            var r = GetComponent<MeshRenderer>();
            if (r != null) r.sharedMaterial = _mat;

            if (led != null)
            {
                _ledMat = new Material(led.sharedMaterial) { name = "TvLed" };
                led.sharedMaterial = _ledMat;
            }

            Muted = PlayerPrefs.GetInt(PrefMute, 0) == 1;
            Background = PlayerPrefs.GetInt(PrefBg, 1) == 1;
            Channel = Mathf.Clamp(PlayerPrefs.GetInt(PrefChan, 0), 0, Channels.Count - 1);
            // 后台播放要求 Unity 在失去焦点后继续跑，否则连计时都停了
            Application.runInBackground = true;
            DrawStandby();
            if (PlayerPrefs.GetInt(PrefOn, 0) == 1) PowerOn();
            else SetLed(false);
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            if (_overlayShown) { WebScreen.Hide(); _overlayShown = false; }
            WebScreen.Close();
        }

        // ================= 开关与频道 =================

        public string ChannelName => Channels[Mathf.Clamp(Channel, 0, Channels.Count - 1)].name;

        public void Toggle() { if (On) PowerOff(); else PowerOn(); }

        public void PowerOn()
        {
            On = true;
            PlayerPrefs.SetInt(PrefOn, 1);
            SetLed(true);
            Tune();
            GameEvents.RaiseSubtitle("电视开机 · " + ChannelName);
        }

        public void PowerOff()
        {
            On = false;
            PlayerPrefs.SetInt(PrefOn, 0);
            SetLed(false);
            WebScreen.Pause();
            if (_overlayShown) { WebScreen.Hide(); _overlayShown = false; }
            DrawStandby();
            GameEvents.RaiseSubtitle("电视已关。");
        }

        public void SetChannel(int i)
        {
            var list = Channels;
            Channel = ((i % list.Count) + list.Count) % list.Count;
            PlayerPrefs.SetInt(PrefChan, Channel);
            if (!On) PowerOn();
            else { Tune(); GameEvents.RaiseSubtitle("切到 · " + ChannelName); }
        }

        public void Next() => SetChannel(Channel + 1);

        public void SetMuted(bool m)
        {
            Muted = m;
            PlayerPrefs.SetInt(PrefMute, m ? 1 : 0);
            WebScreen.Mute(m);
        }

        public void SetBackground(bool b)
        {
            Background = b;
            PlayerPrefs.SetInt(PrefBg, b ? 1 : 0);
        }

        public void Play() => WebScreen.Play();
        public void Pause() => WebScreen.Pause();

        /// <summary>当前频道的分享链接（面板上"在 YouTube 里打开"用）。</summary>
        public string CurrentLink()
        {
            var c = Channels[Mathf.Clamp(Channel, 0, Channels.Count - 1)];
            if (!string.IsNullOrEmpty(c.id)) return "https://www.youtube.com/watch?v=" + c.id;
            return c.url ?? "";
        }

        void Tune()
        {
            var c = Channels[Mathf.Clamp(Channel, 0, Channels.Count - 1)];
            if (c.Empty)
            {
                GameEvents.RaiseSubtitle("这个频道还是空的——在电视面板里粘一条链接进去。");
                return;
            }
            if (!WebScreen.Available) return;      // 屏幕仍会显示程序生成的画面
            if (!string.IsNullOrEmpty(c.id)) WebScreen.PlayYouTube(c.id, Muted);
            else WebScreen.LoadUrl(c.url);
        }

        // ================= 每帧：画面在哪儿 =================

        void Update()
        {
            if (_cam == null) _cam = Camera.main;
            if (!On)
            {
                if (_overlayShown) { WebScreen.Hide(); _overlayShown = false; }
                return;
            }

            // 世界里的屏幕：一直在动（侧面看过去也是亮的）
            if (Time.unscaledTime >= _nextFrame)
            {
                _nextFrame = Time.unscaledTime + 0.1f;
                _phase += 0.1f;
                DrawChannelFrame();
            }

            if (!WebScreen.Available) return;

            bool blocked = Suppressed || Time.timeScale < 0.01f;
            if (!blocked && TryScreenRect(out Rect rect) && rect.width > 40f && rect.height > 24f)
            {
                WebScreen.Place(rect);
                _overlayShown = true;
            }
            else if (_overlayShown)
            {
                // 收起画面但不停播：走开了声音还在，这就是玩家要的"后台播放"的一半
                WebScreen.Hide();
                _overlayShown = false;
            }
        }

        /// <summary>
        /// 把屏幕四角投影到屏幕空间，算出网页层该占的矩形。
        /// 只在玩家大致处于电视正面、且四角都在镜头前时才给——
        /// 斜着看时一块轴对齐的矩形贴不上一个透视的四边形，那种错位比不显示更糟。
        /// </summary>
        bool TryScreenRect(out Rect rect)
        {
            rect = default;
            if (_cam == null) return false;

            Vector3 c = transform.position;
            Vector3 toCam = _cam.transform.position - c;
            float dist = toCam.magnitude;
            if (dist > widthM * 4.5f + 6f) return false;                  // 太远了
            if (Vector3.Dot(faceNormal, toCam.normalized) < 0.45f) return false;  // 不在正面

            Vector3 right = Mathf.Abs(faceNormal.z) >= Mathf.Abs(faceNormal.x)
                ? Vector3.right : Vector3.forward;
            Vector3 up = Vector3.up;
            float hw = widthM * 0.5f, hh = heightM * 0.5f;

            float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = c + right * ((i & 1) == 0 ? -hw : hw) + up * ((i & 2) == 0 ? -hh : hh);
                Vector3 sp = _cam.WorldToScreenPoint(p);
                if (sp.z < 0.2f) return false;                            // 有角落在镜头后面
                x0 = Mathf.Min(x0, sp.x); x1 = Mathf.Max(x1, sp.x);
                y0 = Mathf.Min(y0, sp.y); y1 = Mathf.Max(y1, sp.y);
            }
            // 完全在屏幕外就不必摆了
            if (x1 < 0 || y1 < 0 || x0 > Screen.width || y0 > Screen.height) return false;
            rect = new Rect(x0, y0, x1 - x0, y1 - y0);
            return true;
        }

        // ================= 应用切后台 =================

        void OnApplicationPause(bool paused)
        {
            if (!On) return;
            if (paused)
            {
                if (Background)
                {
                    WebScreen.KeepAlive();   // 声音继续（后台播放开着）
                    // 顺手试一次悬浮小窗：这是安卓上唯一"系统保证不掐声音"的后台形态。
                    // 系统只在应用还没真正离开前台时批准，所以这里是尽力而为——
                    // 被拒绝也没关系，上面的保活已经生效（Java 侧全程 try/catch）。
                    if (PipMode.Supported && !PipMode.InPip) PipMode.Enter();
                }
                else
                {
                    WebScreen.Pause();
                    _pausedByLeaving = true;
                }
            }
            else if (_pausedByLeaving)
            {
                // 只恢复"我们自己因为切后台而暂停的"那一次；玩家手动按的暂停要留着
                _pausedByLeaving = false;
                WebScreen.Play();
            }
        }

        // ================= 程序生成的屏幕画面 =================

        void SetLed(bool on)
        {
            if (_ledMat == null) return;
            var c = on ? new Color(0.15f, 0.85f, 0.35f) : new Color(0.85f, 0.12f, 0.12f);
            _ledMat.color = c;
            if (_ledMat.HasProperty("_BaseColor")) _ledMat.SetColor("_BaseColor", c);
            if (_ledMat.HasProperty("_EmissionColor")) _ledMat.SetColor("_EmissionColor", c * 1.4f);
        }

        void DrawStandby()
        {
            if (_tex == null) return;
            int w = _tex.width, h = _tex.height;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    // 关机的屏幕不是纯黑：玻璃会反一点房间的光，上亮下暗
                    float v = 0.035f + 0.045f * (y / (float)h);
                    _px[y * w + x] = new Color(v, v, v * 1.15f, 1f);
                }
            _tex.SetPixels32(_px);
            _tex.Apply(false, false);
        }

        /// <summary>
        /// 每个频道一套配色的动态画面：横向色带在走、带一点扫描线与四角压暗。
        /// 它不是"假装在播 YouTube"，它是电视从侧面看过去该有的样子——
        /// 亮着、在动、每个台不一样。
        /// </summary>
        void DrawChannelFrame()
        {
            if (_tex == null) return;
            int w = _tex.width, h = _tex.height;
            int seed = Mathf.Abs(ChannelName.GetHashCode());
            float hue = (seed % 997) / 997f;
            float t = _phase;

            for (int y = 0; y < h; y++)
            {
                float fy = y / (float)h;
                for (int x = 0; x < w; x++)
                {
                    float fx = x / (float)w;
                    float band = Mathf.Repeat(fx * 3.1f + fy * 0.6f + t * 0.35f, 1f);
                    float wave = 0.5f + 0.5f * Mathf.Sin((fx * 6.28f + t * 1.7f) + fy * 3.1f);
                    var col = Color.HSVToRGB(Mathf.Repeat(hue + band * 0.22f, 1f),
                        0.55f, 0.30f + 0.55f * wave);
                    // 扫描线 + 四角压暗，才像一块屏幕而不是一张渐变图
                    if (y % 3 == 0) col *= 0.86f;
                    float vig = 1f - 0.55f * Mathf.Max(Mathf.Abs(fx - 0.5f), Mathf.Abs(fy - 0.5f));
                    col *= vig;
                    _px[y * w + x] = col;
                }
            }
            _tex.SetPixels32(_px);
            _tex.Apply(false, false);
        }
    }
}
