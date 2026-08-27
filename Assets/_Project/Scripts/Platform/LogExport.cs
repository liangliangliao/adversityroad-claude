using UnityEngine;

namespace AdversityRoad.Platform
{
    /// <summary>
    /// 让玩家挑一个**自己能进得去**的目录来放调试日志，并把日志复制过去。
    ///
    /// 【为什么需要它】
    /// Application.persistentDataPath 在安卓上是
    ///   /storage/emulated/0/Android/data/&lt;包名&gt;/files/
    /// Android 11 起 scoped storage 把 Android/data 对文件管理器整个藏了起来，
    /// 玩家拿不到文件；而直接往 /sdcard/Download 写，在 API 29+ 上同样被挡。
    /// 官方留下的口子只有一个：**Storage Access Framework**——玩家用系统选择器
    /// 挑一次目录，应用拿到一份可持久化的写授权，此后往里写不需要任何运行时权限。
    ///
    /// 这个类是 LogExport.java 的 C# 壳，用法与本工程既有的 WebScreen / PipMode 一致。
    /// 非安卓平台（编辑器、桌面）没有这套东西，也不需要——那边直接填路径就行，
    /// 所以这里的方法在非安卓上一律返回"没做成"，由 MoveLogger 走普通路径那条分支。
    /// </summary>
    public static class LogExport
    {
        const string JavaClass = "com.adversityroad.logexport.LogExport";
        /// <summary>接收目录选择结果的 GameObject 名（Java 侧用 UnitySendMessage 回调它）。</summary>
        public const string BridgeObject = "MoveLoggerBridge";

        /// <summary>这个平台支不支持"挑一个目录"。</summary>
        public static bool Supported =>
#if UNITY_ANDROID && !UNITY_EDITOR
            true;
#else
            false;
#endif

        /// <summary>打开系统目录选择器。结果通过 MoveLoggerBridge.OnLogFolderPicked 回来。</summary>
        public static void PickFolder(string chooserTitle)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var cls = new AndroidJavaClass(JavaClass))
                    cls.CallStatic("pickFolder", BridgeObject, chooserTitle);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[LogExport] 打不开目录选择器：" + e.Message);
                var go = GameObject.Find(BridgeObject);
                if (go != null) go.SendMessage("OnLogFolderPicked", "");
            }
#else
            var g = GameObject.Find(BridgeObject);
            if (g != null) g.SendMessage("OnLogFolderPicked", "");
#endif
        }

        /// <summary>把本地文件复制进选定目录。返回文档 URI；失败返回空串。</summary>
        public static string Export(string treeUri, string srcPath, string name, string mime)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var cls = new AndroidJavaClass(JavaClass))
                    return cls.CallStatic<string>("exportFile", treeUri, srcPath, name, mime);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[LogExport] 导出失败：" + e.Message);
                return "";
            }
#else
            return "";
#endif
        }

        /// <summary>选定目录的可读名字（设置面板显示用）。</summary>
        public static string FolderLabel(string treeUri)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var cls = new AndroidJavaClass(JavaClass))
                    return cls.CallStatic<string>("folderLabel", treeUri);
            }
            catch (System.Exception)
            {
                return treeUri;
            }
#else
            return treeUri;
#endif
        }
    }

    /// <summary>
    /// 目录选择结果的接收端。Java 侧只能按**名字**找 GameObject 回调
    /// （UnitySendMessage 的限制），所以这里要有一个名字固定、常驻不销毁的对象。
    /// </summary>
    public class MoveLoggerBridge : MonoBehaviour
    {
        public static void Ensure()
        {
            if (GameObject.Find(LogExport.BridgeObject) != null) return;
            var go = new GameObject(LogExport.BridgeObject);
            Object.DontDestroyOnLoad(go);
            go.AddComponent<MoveLoggerBridge>();
        }

        // 名字由 Java 侧的 UnitySendMessage 指定，不能改。
        void OnLogFolderPicked(string treeUri)
        {
            if (string.IsNullOrEmpty(treeUri))
            {
                Core.GameEvents.RaiseSubtitle("没有选择目录。");
                return;
            }
            Core.MoveLogger.TreeUri = treeUri;
            Core.MoveLogger.CustomDir = "";   // 选了系统目录就以它为准，免得两个来源打架
            bool ok = Core.MoveLogger.ExportNow();
            Core.GameEvents.RaiseSubtitle(ok
                ? "日志目录已选定：" + LogExport.FolderLabel(treeUri) + "（已导出一份）"
                : "日志目录已选定：" + LogExport.FolderLabel(treeUri));
        }
    }
}
