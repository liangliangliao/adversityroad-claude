using UnityEngine;

namespace AdversityRoad.Platform
{
    /// <summary>
    /// 安卓网页层：贴在 Unity 画面之上的一块网页，用来当游戏里那台电视的**画面**。
    ///
    /// 【为什么是"贴在画面上"而不是贴图】
    /// 想在应用内看 YouTube，唯一正路是 WebView：把视频流解出来喂给 VideoPlayer
    /// 既违反 YouTube 条款，也每隔几周就失效一次。而 WebView 渲染进的是安卓的 View
    /// 树，不是我们能采样的 GL 贴图——所以画面只能是一块**盖在电视屏幕所在矩形上**
    /// 的原生视图：C# 每帧把屏幕四角投影到屏幕空间，算出包围矩形交给它；玩家不在
    /// 电视正面时收起网页层，改由世界里的屏幕自己显示程序生成的画面（见 WallTv）。
    ///
    /// 【触摸一律穿透】网页层可能盖住大半个屏幕（站在 5 米宽的电视前时），
    /// 要是它吃掉触摸，玩家就没法操作摇杆了。Java 侧的容器会先截走触摸再拒绝它，
    /// 于是事件继续派发给下面的 Unity 视图。所有播放控制走游戏内面板（本类的方法）。
    /// </summary>
    public static class WebScreen
    {
        const string JavaClass = "com.adversityroad.web.WebScreen";

        static AndroidJavaClass _cls;
        static bool _probed, _ok;

        /// <summary>这台设备上能不能用网页层（只有安卓真机能）。</summary>
        public static bool Available
        {
            get
            {
                if (_probed) return _ok;
                _probed = true;
#if UNITY_ANDROID && !UNITY_EDITOR
                try
                {
                    _cls = new AndroidJavaClass(JavaClass);
                    _ok = _cls != null && _cls.CallStatic<bool>("available");
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[WebScreen] 网页层不可用：" + e.Message);
                    _cls = null;
                    _ok = false;
                }
#else
                _ok = false;
#endif
                return _ok;
            }
        }

        /// <summary>
        /// 把网页层摆到屏幕上的这个矩形里（Unity 屏幕坐标，原点在左下）。
        /// 安卓的视图坐标原点在左上，这里负责翻过来；
        /// 同时把"这个矩形是按多大的 Unity 画面量出来的"一起传过去——
        /// Unity 的画面像素数和安卓视图的像素数**不一定相等**（分辨率缩放、刘海、
        /// 分屏、悬浮小窗都会让两者对不上），Java 侧按这个比例换算，
        /// 否则画面会摆到电视旁边去，还会随着差值缩放。
        /// </summary>
        public static void Place(Rect r)
        {
            if (!Available) return;
            int x = Mathf.RoundToInt(r.xMin);
            int y = Mathf.RoundToInt(Screen.height - r.yMax);
            int w = Mathf.RoundToInt(r.width);
            int h = Mathf.RoundToInt(r.height);
            Call("place", x, y, w, h, Screen.width, Screen.height);
        }

        /// <summary>收起画面，但**不停播**——走开时声音继续，才叫"后台播放"。</summary>
        public static void Hide() => Call("hide");

        /// <summary>
        /// 放一条 YouTube 视频。
        /// mode 0 = 自建 IFrame API 页面（默认，最通用）；
        /// mode 1 = 直接加载 embed 页并补上 Referer（对付"错误 153 播放器配置错误"）。
        /// 两条路都可能被个别视频拒绝，所以由调用方在探测失败后换一条重试。
        /// </summary>
        public static void PlayYouTube(string videoId, bool muted, int mode = 0)
        {
            if (string.IsNullOrEmpty(videoId)) return;
            Call("playYouTube", videoId, muted, mode);
        }

        public static void LoadUrl(string url) => Call("loadUrl", url);

        /// <summary>
        /// 查一下"到底有没有在播"，结果通过 <see cref="PlaybackChecked"/> 回来。
        /// 有些视频不许嵌入播放，页面上会出现 YouTube 自己的"此视频不能观看"卡片，
        /// 而游戏这边看起来和"还在缓冲"一模一样——玩家只能对着一个黑框发呆。
        /// </summary>
        public static void Probe()
        {
            if (!Available) return;
            EnsureBridge();
            Call("probe", BridgeName);
        }

        /// <summary>Probe 的结果：true = 页面里真的有个视频在播。</summary>
        public static event System.Action<bool> PlaybackChecked;
        public static void Play() => Call("play");
        public static void Pause() => Call("pause");
        public static void Mute(bool m) => Call("mute", m);
        public static void KeepAlive() => Call("keepAlive");
        public static void Close() => Call("close");
        public static void OpenExternal(string url) => Call("openExternal", url);

        const string BridgeName = "WebScreenBridge";
        static Bridge _bridge;

        static void EnsureBridge()
        {
            if (_bridge != null) return;
            var go = new GameObject(BridgeName);
            Object.DontDestroyOnLoad(go);
            _bridge = go.AddComponent<Bridge>();
        }

        /// <summary>接 Java 侧 UnitySendMessage 的固定收件人。</summary>
        class Bridge : MonoBehaviour
        {
            public void OnWebPlayback(string v) => PlaybackChecked?.Invoke(v == "1");
        }

        static void Call(string method, params object[] args)
        {
            if (!Available || _cls == null) return;
            try { _cls.CallStatic(method, args); }
            catch (System.Exception e) { Debug.LogWarning("[WebScreen] " + method + " 失败：" + e.Message); }
        }

        /// <summary>
        /// 从玩家粘进来的东西里抠出 YouTube 视频 id。
        /// 支持 watch?v= / youtu.be/ / /shorts/ / /embed/ / 直接就是 11 位 id。
        /// 抠不出来返回空串（调用方会把它当普通网址处理）。
        /// </summary>
        public static string ParseVideoId(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Trim();
            if (IsId(s)) return s;

            string[] keys = { "v=", "youtu.be/", "/shorts/", "/embed/", "/live/" };
            foreach (var k in keys)
            {
                int i = s.IndexOf(k, System.StringComparison.OrdinalIgnoreCase);
                if (i < 0) continue;
                string tail = s.Substring(i + k.Length);
                int cut = tail.IndexOfAny(new[] { '&', '?', '/', '#' });
                if (cut >= 0) tail = tail.Substring(0, cut);
                if (IsId(tail)) return tail;
            }
            return "";
        }

        /// <summary>id 只允许 11 位的 [A-Za-z0-9_-]：既是 YouTube 的格式，
        /// 也顺手挡掉往页面里塞脚本的可能（id 是直接拼进 JS 的）。</summary>
        public static bool IsId(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length != 11) return false;
            foreach (char c in s)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                       || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (!ok) return false;
            }
            return true;
        }
    }
}
