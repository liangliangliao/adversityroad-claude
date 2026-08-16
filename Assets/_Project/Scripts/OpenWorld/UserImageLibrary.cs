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
    /// 玩家自己的图片：墙上的艺术画与桌上的相框都读它。
    ///
    /// 【为什么是"放进文件夹"而不是弹一个文件选择框】
    /// Unity 在安卓上没有内建的文件选择器，弹窗要靠原生插件——这个项目
    /// 从头到尾不引入任何外部资源与插件（世界全是运行时几何）。
    /// 所以用最直白也最可靠的一条路：约定一个目录，把图片丢进去就生效。
    /// 目录路径在游戏里能看到（住处的画框走近会念出来），复制得走。
    ///
    /// 命名规则（不区分大小写，png/jpg 都认）：
    ///   art1.png   → 卧室床头大幅
    ///   art2.png   → 卧室侧墙竖幅
    ///   photo.png  → 办公桌相框
    /// 没找到就保持占位色，不报错、不影响任何别的东西。
    /// </summary>
    public static class UserImageLibrary
    {
        public const string FolderName = "UserImages";

        static readonly Dictionary<UserImageSlot, Texture2D> _cache =
            new Dictionary<UserImageSlot, Texture2D>();
        static bool _scanned;

        public static string FolderPath => Path.Combine(Application.persistentDataPath, FolderName);

        static string[] NamesFor(UserImageSlot slot)
        {
            switch (slot)
            {
                case UserImageSlot.BedroomArtA: return new[] { "art1", "art", "picture1" };
                case UserImageSlot.BedroomArtB: return new[] { "art2", "picture2" };
                default: return new[] { "photo", "me", "family" };
            }
        }

        /// <summary>把目录里的图片读进来（只做一次；换了图片可以调 Rescan）。</summary>
        public static void Scan()
        {
            if (_scanned) return;
            _scanned = true;
            EnsureFolder();

            foreach (UserImageSlot slot in System.Enum.GetValues(typeof(UserImageSlot)))
            {
                var tex = Load(slot);
                if (tex != null) _cache[slot] = tex;
            }
            CloudDialogueService.AddLog("玩家图片目录：" + FolderPath + "（已载入 " + _cache.Count + " 张）");
        }

        public static void Rescan()
        {
            _scanned = false;
            _cache.Clear();
            Scan();
            foreach (var f in Object.FindObjectsByType<UserPictureFrame>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                f.ApplyTexture();
        }

        public static Texture2D Get(UserImageSlot slot)
        {
            Scan();
            return _cache.TryGetValue(slot, out var t) ? t : null;
        }

        static void EnsureFolder()
        {
            try
            {
                if (!Directory.Exists(FolderPath)) Directory.CreateDirectory(FolderPath);
                // 放一份说明进去：玩家打开目录第一眼就知道该怎么放
                string readme = Path.Combine(FolderPath, "把图片放这里.txt");
                if (!File.Exists(readme))
                    File.WriteAllText(readme,
                        "把你的图片放进这个文件夹，重新进入游戏（或在住处的画框上按 E 选「重新扫描」）即可生效：\n" +
                        "  art1.png  → 卧室床头的大幅艺术画\n" +
                        "  art2.png  → 卧室侧墙的竖幅画\n" +
                        "  photo.png → 办公桌上的相框照片\n" +
                        "支持 png 与 jpg。文件名不区分大小写。\n");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[UserImage] 创建目录失败：" + e.Message);
            }
        }

        static Texture2D Load(UserImageSlot slot)
        {
            try
            {
                if (!Directory.Exists(FolderPath)) return null;
                foreach (var stem in NamesFor(slot))
                    foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".PNG", ".JPG" })
                    {
                        string p = Path.Combine(FolderPath, stem + ext);
                        if (!File.Exists(p)) continue;
                        var bytes = File.ReadAllBytes(p);
                        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (tex.LoadImage(bytes)) return tex;
                    }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[UserImage] 读取失败：" + e.Message);
            }
            return null;
        }
    }

    /// <summary>
    /// 一个画框/相框：启动时把玩家的图片贴上去；没有图片就保持占位色，
    /// 并在走近时告诉玩家该把文件放到哪儿。
    /// </summary>
    public class UserPictureFrame : MonoBehaviour
    {
        public UserImageSlot slot = UserImageSlot.BedroomArtA;
        public float range = 3f;

        float _lastHint = -99f;
        bool _hasImage;

        void Start() => ApplyTexture();

        public void ApplyTexture()
        {
            var tex = UserImageLibrary.Get(slot);
            var r = GetComponent<MeshRenderer>();
            if (tex == null || r == null) return;

            // 材质是按颜色共享的，直接改会把同色的构件一起改掉——这里换一份自己的
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var m = new Material(shader);
            m.color = Color.white;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            m.mainTexture = tex;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            r.sharedMaterial = m;
            _hasImage = true;
        }

        Player.PlayerController _player;

        void Update()
        {
            if (_player == null)
            {
                _player = FindObjectOfType<Player.PlayerController>();
                if (_player == null) return;
            }
            if (Vector3.Distance(transform.position, _player.transform.position) > range) return;

            // 提示十秒一次，按键每帧都收——把按键判断塞进提示的冷却里，
            // 结果是"只有提示弹出的那一帧按 E 才有用"，玩家会以为这功能坏了
            if (Time.time - _lastHint > 10f)
            {
                _lastHint = Time.time;
                GameEvents.RaiseSubtitle(_hasImage
                    ? "【画框】这是你自己放进来的那张。按 E 重新扫描图片目录。"
                    : "【画框】还空着——把图片放进 " + UserImageLibrary.FolderPath +
                      " 命名为 art1/art2/photo，按 E 重新扫描。");
            }

            if (Input.GetKeyDown(KeyCode.E) || Mobile.MobileInput.GetDown("Interact"))
            {
                UserImageLibrary.Rescan();
                GameEvents.RaiseSubtitle("【画框】已重新扫描图片目录。");
            }
        }
    }
}
