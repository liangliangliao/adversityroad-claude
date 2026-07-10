#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;

namespace AdversityRoad.EditorTools
{
    /// <summary>
    /// 压缩包自动解压：把 .zip 丢进 Resources/Characters/（含 Weapons/ 等任意子目录），
    /// 导入时自动解压并删除 zip 本体——武器库"丢一个压缩包=一件武器"直接生效。
    ///
    /// 规则（为 Sketchfab 等常见打包习惯设计）：
    /// - 每个 zip 解到【以 zip 名命名的子目录】，且保留包内相对路径——
    ///   多个 zip 内同名的 scene.gltf/scene.bin/贴图互不覆盖，.gltf 的相对引用不断；
    /// - 包内若只有一个模型文件（glb/gltf/fbx），把它重命名为 zip 名——
    ///   游戏内武器名 = zip 文件名，且多把武器不重名；
    /// - 只解出白名单扩展名（模型/贴图/bin），跳过 __MACOSX 与 . 开头的系统垃圾。
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
            string parent = Path.GetDirectoryName(full);
            string zipName = Sanitize(Path.GetFileNameWithoutExtension(full));
            string dir = Path.Combine(parent, zipName);
            Directory.CreateDirectory(dir);

            int count = 0;
            var modelFiles = new List<string>();
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

                    // 保留包内相对层级：.gltf 对 bin/贴图的相对引用不被打断
                    string rel = entry.FullName.Replace('\\', '/').TrimStart('/');
                    string target = Path.Combine(dir, rel);
                    string targetDir = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);
                    if (!File.Exists(target))
                    {
                        entry.ExtractToFile(target, false);
                        count++;
                    }
                    if (ext == ".glb" || ext == ".gltf" || ext == ".fbx")
                        modelFiles.Add(target);
                }
            }

            // 单模型包：把模型文件重命名为 zip 名（武器名=zip 名，跨包不重名）。
            // .gltf 引用 bin/贴图用相对 URI，重命名 .gltf 本体不影响其引用。
            if (modelFiles.Count == 1)
            {
                string src = modelFiles[0];
                string dst = Path.Combine(Path.GetDirectoryName(src),
                    zipName + Path.GetExtension(src));
                if (!File.Exists(dst) && File.Exists(src)) File.Move(src, dst);
            }

            AssetDatabase.DeleteAsset(assetPath);   // 解压完删除 zip，避免反复解压
            Debug.Log("[CharacterAssets] " + assetPath + " → " + zipName +
                "/ 解出 " + count + " 个文件，压缩包已移除");
            return true;
        }

        static string Sanitize(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
                sb.Append(System.Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            string n = sb.ToString().Trim();
            return string.IsNullOrEmpty(n) ? "weapon" : n;
        }
    }
}
#endif
