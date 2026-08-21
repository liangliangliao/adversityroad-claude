using UnityEngine;
using AdversityRoad.Core;

namespace AdversityRoad.OpenWorld
{
    /// <summary>
    /// 打开【系统相册】选一张图片。
    ///
    /// 【为什么必须走系统相册】玩家要的是"打开手机本地相册浏览并选择"。
    /// Unity 侧自己查 MediaStore 画一个列表有两个死结：
    ///   ① 要相册权限，而权限弹窗是异步的——上一版查询发在用户点"允许"之前，
    ///      于是列表恒为空，点了按钮**什么也没发生**（玩家反馈的"无反应"）；
    ///   ② 那也不是系统相册的界面，翻看体验差得远。
    /// 系统选图（ACTION_GET_CONTENT）两个问题一起解决：界面是系统自己的相册，
    /// 而且**完全不需要权限**——用户选中哪一张就临时授权哪一张。
    ///
    /// 原生一侧是 Assets/Plugins/Android/GalleryPicker.java（无界面 Fragment，
    /// 不需要改 AndroidManifest）。结果通过 UnitySendMessage 回到这里的桥接对象。
    /// </summary>
    public static class NativeGallery
    {
        const string BridgeName = "GalleryPickerBridge";
        const string JavaClass = "com.adversityroad.gallery.GalleryPicker";

        /// <summary>这台设备上能不能拉起系统相册。</summary>
        public static bool Available =>
            Application.platform == RuntimePlatform.Android && !Application.isEditor;

        /// <summary>
        /// 拉起系统相册。选完回调参数是 content:// URI；取消/失败回调空串。
        /// 返回 false = 这台设备根本拉不起来（编辑器/PC），上层该用自己的面板兜底。
        /// </summary>
        public static bool Pick(System.Action<string> onPicked)
        {
            if (!Available) return false;
            var bridge = Bridge.Instance;
            if (bridge == null) return false;
            bridge.pending = onPicked;
            try
            {
                using (var cls = new AndroidJavaClass(JavaClass))
                    cls.CallStatic("pick", BridgeName, "选择一张图片");
                return true;
            }
            catch (System.Exception e)
            {
                // 插件没打进包/类名对不上：把话说清楚，别再"点了没反应"
                bridge.pending = null;
                CloudDialogueService.AddLog("打开系统相册失败：" + e.Message);
                GameEvents.RaiseSubtitle("这台设备打不开系统相册，改用游戏内相册列表。");
                return false;
            }
        }

        /// <summary>接收 Java 侧 UnitySendMessage 回调的常驻对象。</summary>
        class Bridge : MonoBehaviour
        {
            public System.Action<string> pending;
            static Bridge _inst;

            public static Bridge Instance
            {
                get
                {
                    if (_inst != null) return _inst;
                    var go = new GameObject(BridgeName);
                    DontDestroyOnLoad(go);
                    _inst = go.AddComponent<Bridge>();
                    return _inst;
                }
            }

            /// <summary>由 Java 调用（名字必须与 GalleryPicker.METHOD 一致）。</summary>
            void OnGalleryPicked(string uri)
            {
                var cb = pending;
                pending = null;
                if (cb != null) cb(uri ?? "");
            }
        }
    }
}
