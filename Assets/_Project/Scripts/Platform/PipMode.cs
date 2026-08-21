using UnityEngine;

namespace AdversityRoad.Platform
{
    /// <summary>
    /// 悬浮小窗（安卓画中画）：把正在跑的游戏缩进一个浮在其它应用之上的小窗口。
    ///
    /// 【三块拼起来才成立】
    /// ① 清单属性 android:supportsPictureInPicture —— 没有它，系统直接拒绝请求。
    ///    由 Assets/Plugins/Android/adversityroad.androidlib 合并进来（那里有说明）。
    /// ② Java 侧 enterPictureInPictureMode + 一个无界面 Fragment 监听进出小窗。
    /// ③ 这里：给游戏一个开关，并把"进/出小窗"广播出去，让 UI 自适应
    ///    （见 PipAdaptor —— 480×270 的小窗里放不下摇杆，而且小窗根本收不到触摸）。
    ///
    /// 小窗里游戏**继续跑**：这也是电视后台播放的靠得住的形态——
    /// 窗口还在前台可见，系统不会掐掉声音。
    /// </summary>
    public static class PipMode
    {
        const string JavaClass = "com.adversityroad.pip.PipBridge";
        const string BridgeName = "PipBridge";

        static AndroidJavaClass _cls;
        static bool _probed, _ok;
        static Bridge _bridge;

        /// <summary>进/出小窗时触发（true = 进了小窗）。</summary>
        public static event System.Action<bool> Changed;

        /// <summary>此刻是不是在小窗里。</summary>
        public static bool InPip { get; private set; }

        /// <summary>这台设备支持悬浮小窗吗（安卓 8.0+ 真机）。</summary>
        public static bool Supported
        {
            get
            {
                if (_probed) return _ok;
                _probed = true;
#if UNITY_ANDROID && !UNITY_EDITOR
                try
                {
                    _cls = new AndroidJavaClass(JavaClass);
                    _ok = _cls != null && _cls.CallStatic<bool>("supported");
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[PipMode] 画中画不可用：" + e.Message);
                    _cls = null;
                    _ok = false;
                }
#else
                _ok = false;
#endif
                return _ok;
            }
        }

        /// <summary>装好监听（进游戏时调一次；重复调用无害）。</summary>
        public static void Ensure()
        {
            if (!Supported || _bridge != null) return;
            var go = new GameObject(BridgeName);
            Object.DontDestroyOnLoad(go);
            _bridge = go.AddComponent<Bridge>();
            try { _cls.CallStatic("attach", BridgeName); }
            catch (System.Exception e) { Debug.LogWarning("[PipMode] attach 失败：" + e.Message); }
        }

        /// <summary>
        /// 请求进入悬浮小窗。系统只在应用处于前台时批准，所以要从按钮里调，
        /// 别指望在"应用已经被切走"的回调里补救。
        /// </summary>
        public static bool Enter(int num = 16, int den = 9)
        {
            if (!Supported)
            {
                Core.GameEvents.RaiseSubtitle("这台设备不支持悬浮小窗（需要安卓 8.0 以上）。");
                return false;
            }
            Ensure();
            try
            {
                _cls.CallStatic<bool>("enter", num, den);
                Core.GameEvents.RaiseSubtitle("已切到悬浮小窗——游戏继续跑，点小窗回到全屏。");
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[PipMode] enter 失败：" + e.Message);
                return false;
            }
        }

        static void Set(bool inPip)
        {
            if (InPip == inPip) return;
            InPip = inPip;
            Changed?.Invoke(inPip);
        }

        /// <summary>
        /// 桥：接 Java 的 UnitySendMessage，同时每半秒兜底查一次。
        /// 只靠回调不保险（Fragment 装不上就没人通知），只靠轮询又慢半拍。
        /// </summary>
        class Bridge : MonoBehaviour
        {
            float _next;

            public void OnPipChanged(string v) => Set(v == "1");

            void Update()
            {
                if (Time.unscaledTime < _next) return;
                _next = Time.unscaledTime + 0.5f;
                if (_cls == null) return;
                try { Set(_cls.CallStatic<bool>("inPip")); }
                catch (System.Exception) { /* 查不到就维持现状 */ }
            }
        }
    }
}
