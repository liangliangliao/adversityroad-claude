#if UNITY_EDITOR
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;

namespace AdversityRoad.EditorTools
{
    /// <summary>
    /// 压缩包自动解压：把 .zip 丢进 Resources/Characters/（含 Weapons/、Anims/、Anims2/
    /// 任意子目录），导入时自动就地解压出其中的模型与贴图（.glb/.gltf/.fbx/.bin/图片），
    /// 然后删除 zip 本体——武器库等目录"丢一个压缩包=多件武器"直接生效。
    ///
    /// 规则：
    /// - 平铺解出（忽略包内目录层级），与目录内同名文件冲突时跳过（不覆盖已有资源）；
    /// - 跳过 __MACOSX/ 与 . 开头的系统垃圾文件；
    /// - 只解出白名单扩展名，压缩包里夹带的无关文件不进工程。
    /// </summary>
    public class CharacterAssetZipExtractor : AssetPostprocessor
    {
        static readonly string[] AllowExt =
        {
            ".glb", ".gltf", ".fbx", ".bin",
            ".png", ".jpg", ".jpeg", ".tga", ".ktx2", ".webp"
        };

        static void OnPostprocessAllAssets(string[] imported, string[] deleted,
            string[] moved, string[] movedFrom)
        {
            bool changed = false;
            foreach (var path in imported)
            {
                string p = path.Replace('\\', '/');
                if (!p.Contains("/Resources/Characters/")) continue;
                if (!p.ToLowerInvariant().EndsWith(".zip")) continue;
                try
                {
                    changed |= Extract(p);
                }
                catch (System.Exception e)
                {
                    Debug.LogError("[CharacterAssets] 解压失败 " + p + "：" + e.Message);
                }
            }
            if (changed) AssetDatabase.Refresh();
        }

        static bool Extract(string assetPath)
        {
            string full = Path.GetFullPath(assetPath);
            string dir = Path.GetDirectoryName(full);
            int count = 0;
            using (var zip = ZipFile.OpenRead(full))
            {
                foreach (var entry in zip.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;          // 目录项
                    if (entry.FullName.Contains("__MACOSX")) continue;       // mac 垃圾
                    if (entry.Name.StartsWith(".")) continue;                // 隐藏文件
                    string ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                    bool allowed = false;
                    foreach (var a in AllowExt)
                        if (ext == a) { allowed = true; break; }
                    if (!allowed) continue;

                    string target = Path.Combine(dir, entry.Name);
                    if (File.Exists(target)) continue;                       // 不覆盖已有
                    entry.ExtractToFile(target, false);
                    count++;
                }
            }
            AssetDatabase.DeleteAsset(assetPath);   // 解压完删除 zip，避免反复解压
            Debug.Log("[CharacterAssets] " + assetPath + " 已解压 " + count + " 个文件并移除压缩包");
            return true;
        }
    }
}
#endif
