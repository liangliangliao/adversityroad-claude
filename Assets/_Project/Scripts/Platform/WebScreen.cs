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
        /// 安卓的视图坐标原点在左上，这里负责翻过来。
        /// </summary>
        public static void Place(Rect r)
        {
            if (!Available) return;
            int x = Mathf.RoundToInt(r.xMin);
            int y = Mathf.RoundToInt(Screen.height - r.yMax);
            int w = Mathf.RoundToInt(r.width);
            int h = Mathf.RoundToInt(r.height);
            Call("place", x, y, w, h);
        }

        /// <summary>收起画面，但**不停播**——走开时声音继续，才叫"后台播放"。</summary>
        public static void Hide() => Call("hide");

        public static void PlayYouTube(string videoId, bool muted)
        {
            if (string.IsNullOrEmpty(videoId)) return;
            Call("playYouTube", videoId, muted);
        }

        public static void LoadUrl(string url) => Call("loadUrl", url);
        public static void Play() => Call("play");
        public static void Pause() => Call("pause");
        public static void Mute(bool m) => Call("mute", m);
        public static void KeepAlive() => Call("keepAlive");
        public static void Close() => Call("close");
        public static void OpenExternal(string url) => Call("openExternal", url);

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
