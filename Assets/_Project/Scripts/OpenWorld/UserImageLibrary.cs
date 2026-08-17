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

    /// <summary>
    /// 玩家自己的图片：墙上的画与桌上的相框都读它。
    ///
    /// 【这一版才算真的实现】
    /// 上一版只是"约定一个目录、把文件改成指定文件名"——玩家看不到目录、
    /// 也没有任何界面能确认是否生效，等于没做。现在：
    ///   · 主动去手机/电脑常见的图片目录里**找**图片（相机、图片、下载、
    ///     以及游戏自己的 UserImages 目录）；
    ///   · 安卓上先申请读取权限，被拒也能退回到游戏自己的目录；
    ///   · 由「换一张画」面板列出找到的文件，点一下就挂上去；
    ///   · 选择存进 PlayerPrefs，下次进游戏照旧挂着。
    /// </summary>
    public static class UserImageLibrary
    {
        public const string FolderName = "UserImages";
        const string PrefKey = "villa_picture_";

        static readonly Dictionary<UserImageSlot, Texture2D> _cache =
            new Dictionary<UserImageSlot, Texture2D>();
        static List<string> _found;

        public static string FolderPath => Path.Combine(Application.persistentDataPath, FolderName);

        // ================= 查找 =================

        /// <summary>找出这台设备上能读到的图片文件（去重、按名字排序、最多 200 张）。</summary>
        public static List<string> FindImages()
        {
            RequestReadPermission();
            var list = new List<string>();
            foreach (var dir in CandidateFolders())
            {
                if (string.IsNullOrEmpty(dir)) continue;
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var f in Directory.GetFiles(dir))
                    {
                        if (!IsImage(f) || list.Contains(f)) continue;
                        list.Add(f);
                        if (list.Count >= 200) break;
                    }
                    // 相册通常还有一层子目录（Camera / Screenshots …）
                    foreach (var sub in Directory.GetDirectories(dir))
                    {
                        if (list.Count >= 200) break;
                        try
                        {
                            foreach (var f in Directory.GetFiles(sub))
                            {
                                if (!IsImage(f) || list.Contains(f)) continue;
                                list.Add(f);
                                if (list.Count >= 200) break;
                            }
                        }
                        catch (System.Exception) { /* 单个子目录没权限就跳过 */ }
                    }
                }
                catch (System.Exception) { /* 某个目录不可读不影响其它目录 */ }
                if (list.Count >= 200) break;
            }
            list.Sort(System.StringComparer.Ordinal);
            _found = list;
            CloudDialogueService.AddLog("图片扫描：找到 " + list.Count + " 张（游戏目录 " + FolderPath + "）");
            return list;
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
            // 相册要权限；玩家拒绝了也不影响游戏自己的目录
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                    UnityEngine.Android.Permission.ExternalStorageRead))
                UnityEngine.Android.Permission.RequestUserPermission(
                    UnityEngine.Android.Permission.ExternalStorageRead);
#endif
        }

        // ================= 指派与读取 =================

        /// <summary>把某张图片挂到某个画框上（空字符串＝取下）。</summary>
        public static void Assign(UserImageSlot slot, string path)
        {
            PlayerPrefs.SetString(PrefKey + (int)slot, path ?? "");
            PlayerPrefs.Save();
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
                string readme = Path.Combine(FolderPath, "把图片放这里.txt");
                if (!File.Exists(readme))
                    File.WriteAllText(readme,
                        "把图片（png / jpg）放进这个文件夹，或直接放在手机的 图片 / 下载 / 相机 目录里，\n" +
                        "然后在游戏里走到画框前按交互键，从列表里挑一张即可。\n");
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
                var bytes = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(bytes)) return null;
                // 手机相册动辄四千万像素，直接贴上去纯属浪费显存
                if (tex.width > 1024 || tex.height > 1024) tex.Compress(false);
                return tex;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[UserImage] 读取失败：" + e.Message);
                return null;
            }
        }
    }

    /// <summary>
    /// 一个画框/相框：把玩家指派的图片贴上去；走近按交互键打开「换一张画」面板。
    /// </summary>
    public class UserPictureFrame : MonoBehaviour
    {
        public UserImageSlot slot = UserImageSlot.BedroomArtA;
        public float range = 3.2f;

        /// <summary>由 GameBootstrap 注入：打开换画面板。</summary>
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
            }
            r.sharedMaterial = m;
        }

        void Update()
        {
            if (_player == null)
            {
                _player = FindObjectOfType<Player.PlayerController>();
                if (_player == null) return;
            }
            if (Vector3.Distance(transform.position, _player.transform.position) > range) return;

            if (Time.time - _lastHint > 9f)
            {
                _lastHint = Time.time;
                GameEvents.RaiseSubtitle("【画框】" + Mobile.MobileInput.UseHint + "换一张自己的图片。");
            }
            if (Input.GetKeyDown(KeyCode.E) || Mobile.MobileInput.GetDown("Interact"))
                OpenPicker?.Invoke(slot);
        }
    }
}
