using System.Collections.Generic;
using System.IO;
using UnityEngine;
using AdversityRoad.Core;

namespace AdversityRoad.OpenWorld
{
    /// <summary>家里可以换成玩家自己图片的位置。</summary>
    public enum UserImageSlot
    {
        BedroomArtA,   // 卧室床头的大幅艺术画
        BedroomArtB,   // 卧室侧墙的竖幅
        DeskPhoto,     // 办公桌上的相框照片
    }

    /// <summary>相册里的一张图（来自系统相册 MediaStore 或本机目录）。</summary>
    public class GalleryImage
    {
        /// <summary>文件路径（相册给的 _data；分区存储下可能读不到，那就走 uri）。</summary>
        public string path = "";
        /// <summary>内容 URI（content://media/...）：分区存储下唯一可靠的读法。</summary>
        public string uri = "";
        /// <summary>所在相册名（Camera / Screenshots / Download …）。</summary>
        public string album = "";
        /// <summary>列表里显示的名字。</summary>
        public string Display => string.IsNullOrEmpty(path) ? uri : Path.GetFileName(path);
    }

    /// <summary>
    /// 玩家自己的图片：墙上的画与桌上的相框都读它。
    ///
    /// 【这一版才算"从相册里选"】
    /// 上一版让玩家把照片放到指定目录再扫描——那是文件管理，不是相册。
    /// 玩家的要求很明确：**打开相册、翻看、点一张**。所以现在：
    ///   · 相册内容走系统 MediaStore（分区存储之后唯一正确的问法），
    ///     按拍摄时间倒序，并带上所属相册名（Camera / Screenshots / …）；
    ///   · 面板里显示**缩略图网格**，看图选图，不是看文件名猜图；
    ///   · 选中后把原图**复制进游戏自己的目录**再挂上去——
    ///     于是即使原图被删掉/移动，画框也不会变空；分区存储下读不到文件路径时，
    ///     用 ContentResolver 打开 URI 来复制（这是"选了却没显示"的根因）。
    /// </summary>
    public static class UserImageLibrary
    {
        public const string FolderName = "UserImages";
        const string PrefKey = "villa_picture_";
        /// <summary>贴到画框上的最大边长：手机照片动辄四千万像素，直接贴纯属浪费显存。</summary>
        const int MaxSide = 1024;

        static readonly Dictionary<UserImageSlot, Texture2D> _cache =
            new Dictionary<UserImageSlot, Texture2D>();

        public static string FolderPath => Path.Combine(Application.persistentDataPath, FolderName);

        // ================= 相册 =================

        /// <summary>列出相册里的图片（最多 300 张，按时间倒序）。</summary>
        public static List<GalleryImage> Browse()
        {
            RequestReadPermission();
            var list = new List<GalleryImage>();
            QueryGallery(list);
            ScanFolders(list);
            CloudDialogueService.AddLog("相册：读到 " + list.Count + " 张图片");
            return list;
        }

        static void ScanFolders(List<GalleryImage> into)
        {
            foreach (var dir in CandidateFolders())
            {
                if (string.IsNullOrEmpty(dir)) continue;
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    AddFiles(into, dir);
                    foreach (var sub in Directory.GetDirectories(dir))
                    {
                        if (into.Count >= 300) break;
                        try { AddFiles(into, sub); }
                        catch (System.Exception) { /* 单个子目录没权限就跳过 */ }
                    }
                }
                catch (System.Exception) { /* 某个目录不可读不影响其它目录 */ }
                if (into.Count >= 300) break;
            }
        }

        static void AddFiles(List<GalleryImage> into, string dir)
        {
            foreach (var f in Directory.GetFiles(dir))
            {
                if (into.Count >= 300) break;
                if (!IsImage(f)) continue;
                bool dup = false;
                foreach (var g in into) if (g.path == f) { dup = true; break; }
                if (dup) continue;
                into.Add(new GalleryImage
                {
                    path = f,
                    album = new DirectoryInfo(dir).Name
                });
            }
        }

        static IEnumerable<string> CandidateFolders()
        {
            EnsureFolder();
            yield return FolderPath;
#if UNITY_ANDROID && !UNITY_EDITOR
            const string ext = "/storage/emulated/0/";
            yield return ext + "Pictures";
            yield return ext + "DCIM";
            yield return ext + "Download";
            yield return ext + "Documents";
#else
            yield return System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyPictures);
            yield return Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Downloads");
#endif
        }

        static bool IsImage(string path)
        {
            string e = Path.GetExtension(path).ToLowerInvariant();
            return e == ".png" || e == ".jpg" || e == ".jpeg";
        }

        static void RequestReadPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // 安卓 13 起读相册要的是 READ_MEDIA_IMAGES，老系统才是 READ_EXTERNAL_STORAGE。
            // 两个都申请：系统会忽略与自己版本无关的那一个。
            const string mediaImages = "android.permission.READ_MEDIA_IMAGES";
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(mediaImages))
                UnityEngine.Android.Permission.RequestUserPermission(mediaImages);
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                    UnityEngine.Android.Permission.ExternalStorageRead))
                UnityEngine.Android.Permission.RequestUserPermission(
                    UnityEngine.Android.Permission.ExternalStorageRead);
#endif
        }

        /// <summary>
        /// 向系统相册（MediaStore）要图片列表：路径 + 内容 URI + 相册名，按时间倒序。
        ///
        /// 安卓 10 引入分区存储之后，App 已经不能直接列 DCIM 目录，相册的正确问法
        /// 是通过 ContentResolver 查 MediaStore.Images。这里同时取回 _id，
        /// 拼出 content:// 的 URI —— 因为分区存储下 _data 那个文件路径**经常打不开**，
        /// 而 URI 一定能打开（这正是"选了图却没显示"的根因）。
        /// </summary>
        static void QueryGallery(List<GalleryImage> into)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
                using (var media = new AndroidJavaClass("android.provider.MediaStore$Images$Media"))
                {
                    var uri = media.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI");
                    string baseUri = uri.Call<string>("toString");
                    string[] projection = { "_id", "_data", "bucket_display_name" };
                    var cursor = resolver.Call<AndroidJavaObject>("query", uri, projection,
                        null, null, "date_added DESC");
                    if (cursor == null) return;

                    int cId = cursor.Call<int>("getColumnIndex", "_id");
                    int cData = cursor.Call<int>("getColumnIndex", "_data");
                    int cBucket = cursor.Call<int>("getColumnIndex", "bucket_display_name");
                    int guard = 0;
                    while (cursor.Call<bool>("moveToNext") && into.Count < 300 && guard++ < 900)
                    {
                        var img = new GalleryImage();
                        if (cId >= 0)
                        {
                            string id = cursor.Call<string>("getString", cId);
                            if (!string.IsNullOrEmpty(id)) img.uri = baseUri + "/" + id;
                        }
                        if (cData >= 0) img.path = cursor.Call<string>("getString", cData) ?? "";
                        if (cBucket >= 0) img.album = cursor.Call<string>("getString", cBucket) ?? "";
                        if (string.IsNullOrEmpty(img.uri) && string.IsNullOrEmpty(img.path)) continue;
                        if (!string.IsNullOrEmpty(img.path) && !IsImage(img.path)) continue;
                        into.Add(img);
                    }
                    cursor.Call("close");
                    cursor.Dispose();
                }
            }
            catch (System.Exception e)
            {
                CloudDialogueService.AddLog("相册查询失败（改用目录扫描）：" + e.Message);
            }
#endif
        }

        // ================= 读取图片字节（分区存储的关键） =================

        /// <summary>把这张图读成字节：先试文件路径，读不到就用内容 URI。</summary>
        public static byte[] ReadBytes(GalleryImage img)
        {
            if (img == null) return null;
            try
            {
                if (!string.IsNullOrEmpty(img.path) && File.Exists(img.path))
                    return File.ReadAllBytes(img.path);
            }
            catch (System.Exception) { /* 分区存储下常见：路径在、但不许直接读 */ }

            string tmp = CopyFromUri(img.uri);
            if (!string.IsNullOrEmpty(tmp))
            {
                try { return File.ReadAllBytes(tmp); }
                catch (System.Exception) { }
            }
            return null;
        }

        /// <summary>
        /// 用 ContentResolver 打开 content:// 并复制到 App 自己的临时文件。
        ///
        /// 复制这一步交给 Java 的 FileUtils.copy(in, out)（API 29+，正好是分区存储
        /// 开始生效的那些版本）——C# 侧不必来回搬 byte[]，也不必写 JNI 数组。
        /// 返回临时文件路径；失败返回空。
        /// </summary>
        static string CopyFromUri(string uriString)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (string.IsNullOrEmpty(uriString)) return "";
            string dst = Path.Combine(Application.temporaryCachePath, "picked_image.tmp");
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
                using (var uriCls = new AndroidJavaClass("android.net.Uri"))
                using (var uri = uriCls.CallStatic<AndroidJavaObject>("parse", uriString))
                using (var input = resolver.Call<AndroidJavaObject>("openInputStream", uri))
                using (var output = new AndroidJavaObject("java.io.FileOutputStream", dst))
                using (var fileUtils = new AndroidJavaClass("android.os.FileUtils"))
                {
                    if (input == null) return "";
                    fileUtils.CallStatic<long>("copy", input, output);
                    output.Call("flush");
                    output.Call("close");
                    input.Call("close");
                }
                return File.Exists(dst) ? dst : "";
            }
            catch (System.Exception e)
            {
                CloudDialogueService.AddLog("读取相册图片失败：" + e.Message);
                return "";
            }
#else
            return "";
#endif
        }

        // ================= 指派与读取 =================

        /// <summary>
        /// 把相册里选中的这张图挂到某个画框上。
        ///
        /// **先复制进游戏自己的目录**再挂：① 分区存储下原路径未必能再次读到；
        /// ② 玩家删掉/移动原图后画框不该变空；③ 存档里记的是我们自己的稳定路径。
        /// </summary>
        public static bool Assign(UserImageSlot slot, GalleryImage img)
        {
            if (img == null) { Assign(slot, ""); return true; }
            var bytes = ReadBytes(img);
            if (bytes == null || bytes.Length < 16)
            {
                GameEvents.RaiseSubtitle("这张图读不出来，换一张试试。");
                return false;
            }
            EnsureFolder();
            string dst = Path.Combine(FolderPath, "slot_" + (int)slot + ".png");
            try
            {
                // 存一份缩到 1024 以内的 PNG：贴图直接用它，省显存也省下次解码
                var tex = Decode(bytes, MaxSide);
                if (tex == null)
                {
                    GameEvents.RaiseSubtitle("这张图不是能识别的图片格式。");
                    return false;
                }
                File.WriteAllBytes(dst, tex.EncodeToPNG());
                Object.Destroy(tex);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[UserImage] 保存失败：" + e.Message);
                GameEvents.RaiseSubtitle("保存失败：" + e.Message);
                return false;
            }
            Assign(slot, dst);
            return true;
        }

        /// <summary>直接指定一个本机文件路径（空字符串＝取下）。</summary>
        public static void Assign(UserImageSlot slot, string path)
        {
            PlayerPrefs.SetString(PrefKey + (int)slot, path ?? "");
            PlayerPrefs.Save();
            if (_cache.TryGetValue(slot, out var old) && old != null) Object.Destroy(old);
            _cache.Remove(slot);
            foreach (var f in Object.FindObjectsByType<UserPictureFrame>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (f.slot == slot) f.ApplyTexture();
            GameEvents.RaiseSubtitle(string.IsNullOrEmpty(path)
                ? "画取下来了。" : "换上了新的一张。");
        }

        public static string PathFor(UserImageSlot slot) => PlayerPrefs.GetString(PrefKey + (int)slot, "");

        public static Texture2D Get(UserImageSlot slot)
        {
            if (_cache.TryGetValue(slot, out var t) && t != null) return t;

            string path = PathFor(slot);
            if (string.IsNullOrEmpty(path))
            {
                // 没指派过：沿用老约定（UserImages/art1.png 之类），老用户不用重设
                path = LegacyPath(slot);
                if (string.IsNullOrEmpty(path)) return null;
            }
            var tex = Load(path);
            if (tex != null) _cache[slot] = tex;
            return tex;
        }

        static string LegacyPath(UserImageSlot slot)
        {
            string[] stems = slot == UserImageSlot.BedroomArtA ? new[] { "art1", "art" }
                : slot == UserImageSlot.BedroomArtB ? new[] { "art2" }
                : new[] { "photo", "me" };
            foreach (var stem in stems)
                foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
                {
                    string p = Path.Combine(FolderPath, stem + ext);
                    if (File.Exists(p)) return p;
                }
            return "";
        }

        static void EnsureFolder()
        {
            try
            {
                if (!Directory.Exists(FolderPath)) Directory.CreateDirectory(FolderPath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[UserImage] 创建目录失败：" + e.Message);
            }
        }

        static Texture2D Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                return Decode(File.ReadAllBytes(path), MaxSide);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[UserImage] 读取失败：" + e.Message);
                return null;
            }
        }

        /// <summary>解码并缩到 maxSide 以内（含缩放；失败返回 null）。</summary>
        public static Texture2D Decode(byte[] bytes, int maxSide)
        {
            if (bytes == null || bytes.Length < 16) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes)) { Object.Destroy(tex); return null; }
            return Downscale(tex, maxSide);
        }

        /// <summary>
        /// 缩放贴图。
        ///
        /// 【不能用 Texture2D.Compress】上一版用它压手机照片：DXT 压缩对非 4 的倍数
        /// 尺寸会失败或糊掉，而手机照片的尺寸五花八门——那正是"选了图却显示不对"
        /// 的另一半原因。这里改成走 GPU 重采样（Blit + ReadPixels），
        /// 任何尺寸都能得到一张干净的小图。
        /// </summary>
        public static Texture2D Downscale(Texture2D src, int maxSide)
        {
            if (src == null) return null;
            int w = src.width, h = src.height;
            float k = Mathf.Min(1f, maxSide / (float)Mathf.Max(w, h));
            if (k >= 0.999f) return src;
            int nw = Mathf.Max(8, Mathf.RoundToInt(w * k));
            int nh = Mathf.Max(8, Mathf.RoundToInt(h * k));
            var rt = RenderTexture.GetTemporary(nw, nh, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var dst = new Texture2D(nw, nh, TextureFormat.RGBA32, false);
            dst.ReadPixels(new Rect(0, 0, nw, nh), 0, 0);
            dst.Apply(false, false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Object.Destroy(src);
            return dst;
        }
    }

    /// <summary>
    /// 一个画框/相框：把玩家指派的图片贴上去；走近按交互键打开相册面板。
    /// </summary>
    public class UserPictureFrame : MonoBehaviour
    {
        public UserImageSlot slot = UserImageSlot.BedroomArtA;
        public float range = 3.2f;

        /// <summary>由 GameBootstrap 注入：打开相册面板。</summary>
        public static System.Action<UserImageSlot> OpenPicker;

        float _lastHint = -99f;
        Player.PlayerController _player;

        void Start() => ApplyTexture();

        public void ApplyTexture()
        {
            var r = GetComponent<MeshRenderer>();
            if (r == null) return;
            var tex = UserImageLibrary.Get(slot);

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var m = new Material(shader);
            var tint = tex != null ? Color.white : new Color(0.58f, 0.56f, 0.62f);
            m.color = tint;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
            if (tex != null)
            {
                m.mainTexture = tex;
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                // 画面别被光照压暗到看不清：画框自己给一点自发光
                if (m.HasProperty("_EmissionColor"))
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetTexture("_EmissionMap", tex);
                    m.SetColor("_EmissionColor", new Color(0.35f, 0.35f, 0.35f));
                }
            }
            r.sharedMaterial = m;
        }

        void Update()
        {
            if (SitController.Busy) return;   // 坐着/躺着时不抢交互键（床边就挂着画）
            if (_player == null)
            {
                _player = FindObjectOfType<Player.PlayerController>();
                if (_player == null) return;
            }
            if (Vector3.Distance(transform.position, _player.transform.position) > range) return;

            if (Time.time - _lastHint > 9f)
            {
                _lastHint = Time.time;
                GameEvents.RaiseSubtitle("【画框】" + Mobile.MobileInput.UseHint + "打开相册，挑一张自己的图片。");
            }
            if (Input.GetKeyDown(KeyCode.E) || Mobile.MobileInput.GetDown("Interact"))
                OpenPicker?.Invoke(slot);
        }
    }
}
