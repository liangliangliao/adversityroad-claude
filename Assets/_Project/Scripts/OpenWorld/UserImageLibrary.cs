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
        /// <summary>
        /// 贴到画框上的最大边长。
        ///
        /// 【为什么从 1024 提到 2048】画框做大之后（床头那幅将近 3 米宽），
        /// 1024 的图铺满 3 米墙面 ≈ 每厘米 3 个像素，凑近看就是一片糊。
        /// 2048 对手机显存来说仍然只有 16MB 一张，而住所里总共只有三个画框，
        /// 换来的是"走到画前也看得清"。再往上（原图动辄四千万像素）纯属浪费。
        /// </summary>
        const int MaxSide = 2048;

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

        /// <summary>相册读取权限拿到了吗（安卓以外一律视为有）。</summary>
        public static bool HasReadPermission
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                           "android.permission.READ_MEDIA_IMAGES")
                    || UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                           UnityEngine.Android.Permission.ExternalStorageRead);
#else
                return true;
#endif
            }
        }

        /// <summary>发起权限申请（弹窗是异步的，调用方要自己等结果）。</summary>
        public static void RequestReadPermission()
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
            string dst = Path.Combine(Application.temporaryCachePath,
                "picked_" + System.DateTime.Now.Ticks + ".tmp");
            try
            {
                // 首选自带插件里的复制（普通读写循环，所有安卓版本都成立）
                using (var picker = new AndroidJavaClass("com.adversityroad.gallery.GalleryPicker"))
                    if (picker.CallStatic<bool>("copyToFile", uriString, dst) && File.Exists(dst))
                        return dst;
            }
            catch (System.Exception e)
            {
                CloudDialogueService.AddLog("插件复制失败，改用 FileUtils：" + e.Message);
            }
            try
            {
                // 兜底：系统的 FileUtils.copy（API 29+）
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
                // 存一份缩到 MaxSide 以内的 PNG：贴图直接用它，省显存也省下次解码
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

        /// <summary>
        /// 系统相册选完之后的那一步：把 content:// URI 的图片挂到画框上。
        ///
        /// 系统选图给的授权只覆盖"这一张、这一次"，所以必须**当场复制到自己的目录**，
        /// 否则下次进游戏就读不到了（那正是"选了却没显示"的另一种死法）。
        /// </summary>
        public static bool AssignFromUri(UserImageSlot slot, string uri)
        {
            if (string.IsNullOrEmpty(uri)) return false;
            return Assign(slot, new GalleryImage { uri = uri, album = "系统相册" });
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

        /// <summary>解码并缩到 maxSide 以内（含 EXIF 摆正与缩放；失败返回 null）。</summary>
        public static Texture2D Decode(byte[] bytes, int maxSide)
        {
            if (bytes == null || bytes.Length < 16) return null;
            // 原图这一步**不要 mipmap**：手机照片动辄一两千万像素，
            // 给它建 mip 金字塔等于白白多占三分之一显存，而它下一行就被缩掉了。
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes)) { Object.Destroy(tex); return null; }
            // 先缩小再摆正：旋转是逐像素搬运，在小图上做便宜得多
            var small = Downscale(tex, maxSide);
            return Sharpen(Orient(small, ExifOrientation(bytes)));
        }

        // ================= EXIF 摆正 =================

        /// <summary>
        /// 从 JPEG 字节里读出 EXIF 方向标记（1~8；读不到返回 1＝不用转）。
        ///
        /// 【为什么必须自己读】手机拍照几乎从不旋转像素，而是把"这张图该怎么转"
        /// 记在 EXIF 的 Orientation 标记里，看图软件读了标记再转过来显示。
        /// 而 Texture2D.LoadImage **完全不看 EXIF**——于是同一张照片，
        /// 在手机相册里是正的，贴到墙上就是倒的（玩家反馈的"照片是倒过来的"）。
        /// </summary>
        public static int ExifOrientation(byte[] b)
        {
            try
            {
                if (b == null || b.Length < 8 || b[0] != 0xFF || b[1] != 0xD8) return 1;   // 不是 JPEG
                int i = 2;
                while (i + 4 < b.Length)
                {
                    if (b[i] != 0xFF) { i++; continue; }
                    int marker = b[i + 1];
                    if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
                    { i += 2; continue; }
                    if (marker == 0xDA || marker == 0xD9) break;          // 到了图像数据，没有 EXIF
                    int len = (b[i + 2] << 8) | b[i + 3];
                    if (len < 2) break;
                    if (marker == 0xE1 && i + 10 < b.Length &&
                        b[i + 4] == 'E' && b[i + 5] == 'x' && b[i + 6] == 'i' && b[i + 7] == 'f')
                        return ReadTiffOrientation(b, i + 10);            // APP1：跳过 "Exif\0\0"
                    i += 2 + len;
                }
            }
            catch (System.Exception) { }
            return 1;
        }

        static int ReadTiffOrientation(byte[] b, int tiff)
        {
            if (tiff + 8 >= b.Length) return 1;
            bool little = b[tiff] == 'I' && b[tiff + 1] == 'I';
            int ifd = tiff + (int)ReadU32(b, tiff + 4, little);
            if (ifd + 2 >= b.Length) return 1;
            int count = (int)ReadU16(b, ifd, little);
            for (int e = 0; e < count; e++)
            {
                int entry = ifd + 2 + e * 12;
                if (entry + 12 > b.Length) break;
                if (ReadU16(b, entry, little) != 0x0112) continue;        // Orientation
                int v = (int)ReadU16(b, entry + 8, little);
                return v >= 1 && v <= 8 ? v : 1;
            }
            return 1;
        }

        static uint ReadU16(byte[] b, int at, bool little) =>
            little ? (uint)(b[at] | (b[at + 1] << 8)) : (uint)((b[at] << 8) | b[at + 1]);

        static uint ReadU32(byte[] b, int at, bool little) => little
            ? (uint)(b[at] | (b[at + 1] << 8) | (b[at + 2] << 16) | (b[at + 3] << 24))
            : (uint)((b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3]);

        /// <summary>按 EXIF 方向标记把像素摆正（1=不动，直接返回原图）。</summary>
        public static Texture2D Orient(Texture2D src, int orientation)
        {
            if (src == null || orientation <= 1 || orientation > 8) return src;
            int w = src.width, h = src.height;
            var srcPx = src.GetPixels32();
            bool swap = orientation >= 5;                                  // 5~8 要转 90 度
            int dw = swap ? h : w, dh = swap ? w : h;
            var dstPx = new Color32[srcPx.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int nx, ny;
                    switch (orientation)
                    {
                        case 2: nx = w - 1 - x; ny = y; break;             // 水平镜像
                        case 3: nx = w - 1 - x; ny = h - 1 - y; break;     // 转 180
                        case 4: nx = x; ny = h - 1 - y; break;             // 垂直镜像
                        case 5: nx = y; ny = x; break;                     // 转置
                        case 6: nx = y; ny = w - 1 - x; break;             // 顺时针 90
                        case 7: nx = h - 1 - y; ny = w - 1 - x; break;     // 反转置
                        default: nx = h - 1 - y; ny = x; break;            // 8：逆时针 90
                    }
                    dstPx[ny * dw + nx] = srcPx[y * w + x];
                }
            var dst = new Texture2D(dw, dh, TextureFormat.RGBA32, true);
            dst.SetPixels32(dstPx);
            dst.Apply(true, false);
            Object.Destroy(src);
            return dst;
        }

        /// <summary>把已经挂上的那张图再转 90°（相册里方向本来就不对时手动救急）。</summary>
        public static bool Rotate(UserImageSlot slot, bool clockwise)
        {
            string path = PathFor(slot);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(File.ReadAllBytes(path))) { Object.Destroy(tex); return false; }
                var rot = Orient(tex, clockwise ? 6 : 8);
                File.WriteAllBytes(path, rot.EncodeToPNG());
                Object.Destroy(rot);
                if (_cache.TryGetValue(slot, out var old) && old != null) Object.Destroy(old);
                _cache.Remove(slot);
                foreach (var f in Object.FindObjectsByType<UserPictureFrame>(
                             FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (f.slot == slot) f.ApplyTexture();
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[UserImage] 旋转失败：" + e.Message);
                return false;
            }
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
            int nw = Mathf.Max(8, Mathf.RoundToInt(w * k));
            int nh = Mathf.Max(8, Mathf.RoundToInt(h * k));
            // 【本来就够小的图也要走这一趟】不是为了缩放，是为了拿到一张**带 mipmap**
            // 的贴图：原图那张是不带 mip 的（见 Decode），直接拿去贴大画框，
            // 斜着看会一格一格地闪。一次 Blit 很便宜，换的是画面稳定。
            var rt = RenderTexture.GetTemporary(nw, nh, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var dst = new Texture2D(nw, nh, TextureFormat.RGBA32, true);
            dst.ReadPixels(new Rect(0, 0, nw, nh), 0, 0);
            dst.Apply(true, false);          // true = 顺手生成 mipmap（见 Sharpen）
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Object.Destroy(src);
            return dst;
        }

        /// <summary>
        /// 采样设置：画框做大之后，"清不清楚"一半靠像素数、另一半靠采样。
        ///   · Trilinear + mipmap：斜着看画面时不再一格格闪（没有 mipmap 只会更糊）；
        ///   · anisoLevel 8：站在画的侧面看时，远端不糊成一条；
        ///   · Clamp：画面按比例铺满画框，边上不该出现重复的一条边。
        /// </summary>
        static Texture2D Sharpen(Texture2D t)
        {
            if (t == null) return null;
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Trilinear;
            t.anisoLevel = 8;
            return t;
        }
    }

    /// <summary>
    /// 一个画框/相框：把玩家指派的图片贴上去；走近按交互键打开相册面板。
    ///
    /// 【画框不是固定尺寸，是一个"上限盒"】
    /// 玩家的两句要求是一起提的："相框再做大" + "图片需要适应相框大小"。
    /// 这两件事天生打架：框子一固定，长宽比不一样的照片要么被拉变形，
    /// 要么被裁掉半张脸（上一版用的就是裁切填满）。
    ///
    /// 所以这里改成：房间代码给的尺寸是**画面允许占用的最大宽高**，
    /// 真正的画面尺寸在拿到照片后按照片自己的长宽比，在这个盒子里取最大内接矩形，
    /// 木边框随之重建。横幅照片得到一张宽画、竖幅照片得到一张高画，
    /// 两者都是**完整的、不裁、不变形**，而且总是把这面墙吃满。
    /// </summary>
    public class UserPictureFrame : MonoBehaviour
    {
        public UserImageSlot slot = UserImageSlot.BedroomArtA;

        /// <summary>画面允许占用的最大宽 / 最大高（米）。实际尺寸按照片比例内接。</summary>
        public float maxW = 4.2f, maxH = 2.6f;
        /// <summary>画板厚度（沿墙面法线方向）。</summary>
        public float thickness = 0.08f;
        /// <summary>true = 挂在侧墙上（法线是 X 轴）；false = 法线是 Z 轴。</summary>
        public bool alongZ;
        /// <summary>画面朝向（从画框指向看画的人）。背板挂在它的反面。</summary>
        public Vector3 faceDir = Vector3.back;
        /// <summary>木边框宽度，0 表示不要边框。</summary>
        public float border = 0.13f;
        /// <summary>true = 画面下沿固定（桌上的相框要坐在支架上，不能随比例上下飘）；
        /// false = 画面中心固定（墙上的画按中心对齐更自然）。</summary>
        public bool anchorBottom;

        /// <summary>交互距离。画挂在 2.5 米高的墙上，站在墙前时【竖直方向】就已经
        /// 占掉 2.5 米，3.2 米的范围意味着几乎要贴着墙站——那也是"按了没反应"的
        /// 一种：根本没进范围。放宽到 4.5 米。</summary>
        public float range = 4.5f;

        /// <summary>由 GameBootstrap 注入：打开相册面板。</summary>
        public static System.Action<UserImageSlot> OpenPicker;

        static readonly Color FrameWood = new Color(0.45f, 0.34f, 0.22f);

        GameObject _border;
        bool _anchored;
        float _bottomY;
        float _lastHint = -99f;
        Player.PlayerController _player;

        void Start() => ApplyTexture();

        public void ApplyTexture()
        {
            var r = GetComponent<MeshRenderer>();
            if (r == null) return;
            var tex = UserImageLibrary.Get(slot);
            Resize(tex);

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
                // 【不再需要裁切】画框已经按照片的长宽比变过形了（见 Resize），
                // 画面和照片是同一个比例，1:1 铺上去就是"填满且完整"。
                m.mainTextureScale = Vector2.one;
                m.mainTextureOffset = Vector2.zero;
                if (m.HasProperty("_BaseMap"))
                {
                    m.SetTextureScale("_BaseMap", Vector2.one);
                    m.SetTextureOffset("_BaseMap", Vector2.zero);
                }
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

        /// <summary>按照片比例在上限盒里取最大内接矩形，并重建木边框。</summary>
        void Resize(Texture2D tex)
        {
            float boxW = Mathf.Max(0.05f, maxW), boxH = Mathf.Max(0.05f, maxH);
            // 上限盒的下沿：只在第一次量一次，之后换几张照片都以它为准
            if (!_anchored) { _anchored = true; _bottomY = transform.position.y - boxH * 0.5f; }

            float aspect = (tex != null && tex.height > 0) ? tex.width / (float)tex.height : boxW / boxH;
            aspect = Mathf.Clamp(aspect, 0.2f, 5f);          // 全景图/长截图也不至于变成一条线
            float w = boxW, h = boxW / aspect;
            if (h > boxH) { h = boxH; w = boxH * aspect; }
            transform.localScale = alongZ ? new Vector3(thickness, h, w)
                                          : new Vector3(w, h, thickness);
            if (anchorBottom)
            {
                var p = transform.position;
                p.y = _bottomY + h * 0.5f;
                transform.position = p;
            }
            BuildBorder(w, h);
        }

        /// <summary>
        /// 木边框：上下左右四条，**画的正后方什么也没有**。
        /// 上一版在画背后 2 厘米处摆了一整块背板，两个面几乎同深度——手机的深度
        /// 精度分不出来，于是照片上盖着一块会随镜头变来变去的色块（玩家反馈的
        /// "照片上覆盖色块"）。四条边各自贴在画的四周，从构造上就不可能再打架。
        /// </summary>
        void BuildBorder(float w, float h)
        {
            if (_border != null) Destroy(_border);
            if (border <= 0.001f) return;

            Vector3 at = transform.position;
            _border = new GameObject("PictureFrame");
            _border.transform.position = at;
            if (transform.parent != null) _border.transform.SetParent(transform.parent, true);

            float b = border, d = 0.05f;                     // 边框比画略厚（框住画的观感）
            float thick = thickness + d;

            // 【背板：从背面看不该看见照片】画板是个方块，贴图会贴满六个面——
            // 桌上的相框背面因此顶着一张镜像的照片（玩家反馈的"相框背面显示照片"）。
            // 墙上的画背靠墙看不见，桌上的相框却是三百六十度都能绕过去的。
            // 所以在画的背后立一块不透明的板：从背面看到的是板，不是照片。
            // 离画面 2 厘米，既挡得住又不会和画面同深度。
            Vector3 back = faceDir.sqrMagnitude > 0.01f ? faceDir.normalized : Vector3.back;
            Vector3 boardSize = alongZ
                ? new Vector3(0.03f, h + b * 1.4f, w + b * 1.4f)
                : new Vector3(w + b * 1.4f, h + b * 1.4f, 0.03f);
            Bar("PictureBack", at - back * (thickness * 0.5f + 0.035f), boardSize);
            Vector3 wide = alongZ ? new Vector3(thick, b, w + b * 2f) : new Vector3(w + b * 2f, b, thick);
            Vector3 tall = alongZ ? new Vector3(thick, h, b) : new Vector3(b, h, thick);
            Vector3 side = alongZ ? new Vector3(0, 0, w / 2f + b / 2f) : new Vector3(w / 2f + b / 2f, 0, 0);
            Bar("PictureFrame_T", at + new Vector3(0, h / 2f + b / 2f, 0), wide);
            Bar("PictureFrame_B", at - new Vector3(0, h / 2f + b / 2f, 0), wide);
            Bar("PictureFrame_L", at - side, tall);
            Bar("PictureFrame_R", at + side, tall);
        }

        void Bar(string name, Vector3 pos, Vector3 size)
        {
            var go = VillaKit.Deco(name, pos, size, FrameWood);
            if (go != null) go.transform.SetParent(_border.transform, true);
        }

        void Update()
        {
            if (SitController.Busy) return;   // 坐着/躺着时不抢交互键（床边就挂着画）
            if (_player == null)
            {
                _player = AdversityRoad.Core.ActorRegistry.Player;
                if (_player == null) return;
            }
            if (Vector3.Distance(transform.position, _player.transform.position) > range) return;

            if (Time.time - _lastHint > 9f)
            {
                _lastHint = Time.time;
                GameEvents.RaiseSubtitle("【画框】" + Mobile.MobileInput.UseHint + "打开相册，挑一张自己的图片。");
            }
            if (Input.GetKeyDown(KeyCode.E) || Mobile.MobileInput.GetDown("Interact"))
            {
                // 先给一句反馈：相册要拉起来需要一两秒，中间不能是"点了没动静"
                GameEvents.RaiseSubtitle("正在打开手机相册……");
                OpenPicker?.Invoke(slot);
            }
        }
    }
}
