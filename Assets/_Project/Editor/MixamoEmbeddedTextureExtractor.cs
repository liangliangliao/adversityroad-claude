#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AdversityRoad.EditorTools
{
    /// <summary>
    /// 从 Mixamo FBX 二进制里【抽出内嵌贴图】写成同目录 PNG（根治"带皮肤模型却是白模"）。
    ///
    /// 现象：PlayerModel/EnemyModel 的皮肤贴图(maria_diffuse/normal/specular、Paladin_*)
    /// 是【内嵌】在 FBX 里的，但本项目(URP + Unity 6)下 materialImportMode=
    /// ImportViaMaterialDescription 并没有把内嵌贴图抽成可用的 Texture2D，材质拿到的贴图
    /// 引用为空 → 角色/敌人渲染成一片纯白，重导也不生效。
    ///
    /// 办法：直接读 FBX 字节，按 PNG 结构(签名→逐块到 IEND)把内嵌图像切出来，用 FBX 里
    /// 记录的原始文件名(maria_diffuse.png 等)写到 FBX 同目录。之后 Unity 的 FBX 材质导入
    /// 会按文件名把这些【外部】贴图解析上去(同目录优先)，皮肤即恢复。幂等：贴图已存在就
    /// 跳过；抽出后强制重导对应 FBX。编辑器脚本，不进入最终包体。
    /// </summary>
    [InitializeOnLoad]
    public static class MixamoEmbeddedTextureExtractor
    {
        static readonly byte[] PngSig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        static readonly string[] Targets =
        {
            "Assets/_Project/Resources/Characters/PlayerModel.fbx",
            "Assets/_Project/Resources/Characters/EnemyModel.fbx",
        };

        static MixamoEmbeddedTextureExtractor()
        {
            // 延迟到编辑器空闲执行，避免在资源导入流程中再触发导入
            EditorApplication.delayCall += RunOnce;
        }

        [MenuItem("AdversityRoad/重新抽取角色内嵌贴图")]
        public static void ForceRun()
        {
            foreach (var p in Targets) if (File.Exists(p)) Extract(p, true);
            AssetDatabase.Refresh();
        }

        static void RunOnce()
        {
            bool wrote = false;
            foreach (var p in Targets)
                if (File.Exists(p)) wrote |= Extract(p, false);
            if (wrote) AssetDatabase.Refresh();
        }

        /// <summary>抽出一个 FBX 的内嵌贴图。force=true 时覆盖已存在文件。
        /// 返回是否写出了新文件（写出后强制重导该 FBX 让材质解析贴图）。</summary>
        static bool Extract(string fbxPath, bool force)
        {
            byte[] data;
            try { data = File.ReadAllBytes(fbxPath); }
            catch { return false; }

            string dir = Path.GetDirectoryName(fbxPath).Replace('\\', '/');
            var nameHits = FindTextureNameHits(data);   // (byteIndex, 纯文件名)
            var blobs = FindPngBlobs(data);             // (start,end)
            if (blobs.Count == 0) return false;

            bool wrote = false;
            int fallback = 0;
            foreach (var (start, end) in blobs)
            {
                string fname = NearestPrecedingName(nameHits, start);
                if (string.IsNullOrEmpty(fname))
                    fname = Path.GetFileNameWithoutExtension(fbxPath) + "_tex" + (fallback++) + ".png";
                string outPath = dir + "/" + fname;
                if (!force && File.Exists(outPath)) continue;   // 幂等
                try
                {
                    var png = new byte[end - start];
                    System.Array.Copy(data, start, png, 0, png.Length);
                    File.WriteAllBytes(outPath, png);
                    wrote = true;
                }
                catch { /* 单张失败不阻断其余 */ }
            }
            if (wrote)
                AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);
            return wrote;
        }

        // ---- FBX 里出现的贴图文件名（形如 xxx.fbm/maria_diffuse.png）及其字节位置 ----
        static List<(int idx, string name)> FindTextureNameHits(byte[] d)
        {
            var hits = new List<(int, string)>();
            for (int i = 0; i + 4 <= d.Length; i++)
            {
                if (d[i] == '.' &&
                    (d[i + 1] | 0x20) == 'p' && (d[i + 2] | 0x20) == 'n' && (d[i + 3] | 0x20) == 'g')
                {
                    int end = i + 4;
                    int s = i;
                    while (s > 0 && IsNameChar(d[s - 1])) s--;
                    int baseStart = s;
                    for (int k = s; k < end; k++)
                        if (d[k] == '/' || d[k] == '\\') baseStart = k + 1;
                    string name = System.Text.Encoding.ASCII.GetString(d, baseStart, end - baseStart);
                    if (name.Length > 4) hits.Add((baseStart, name));
                }
            }
            return hits;
        }

        static bool IsNameChar(byte b) =>
            (b >= 'A' && b <= 'Z') || (b >= 'a' && b <= 'z') || (b >= '0' && b <= '9') ||
            b == '_' || b == '-' || b == '.' || b == '/' || b == '\\';

        // 内嵌贴图的 Content(PNG 字节)紧跟在同一 Video 节点的 RelativeFilename 之后，
        // 故取【位置在 PNG 之前、且最靠近】的那个文件名作为该图的名字。
        static string NearestPrecedingName(List<(int idx, string name)> hits, int pos)
        {
            string best = null; int bestIdx = int.MinValue;
            foreach (var (idx, name) in hits)
                if (idx <= pos && idx > bestIdx) { bestIdx = idx; best = name; }
            return best;
        }

        // ---- 扫描内嵌 PNG：签名定位，逐块(len+type+data+crc)推进到 IEND 精确切段 ----
        static List<(int start, int end)> FindPngBlobs(byte[] d)
        {
            var res = new List<(int, int)>();
            int i = 0;
            while (i + 8 <= d.Length)
            {
                if (MatchSig(d, i))
                {
                    int end = PngEnd(d, i);
                    if (end > i) { res.Add((i, end)); i = end; continue; }
                }
                i++;
            }
            return res;
        }

        static bool MatchSig(byte[] d, int i)
        {
            for (int k = 0; k < 8; k++) if (d[i + k] != PngSig[k]) return false;
            return true;
        }

        /// <summary>逐 PNG 块推进求结尾字节(排他)：块 = 长度(4,大端)+类型(4)+数据+CRC(4)，
        /// 到 IEND 块结束。正确处理数据里恰好含 "IEND" 字样的情况。</summary>
        static int PngEnd(byte[] d, int start)
        {
            int p = start + 8;   // 跳过 8 字节签名
            while (p + 8 <= d.Length)
            {
                long len = ((long)d[p] << 24) | ((long)d[p + 1] << 16) |
                           ((long)d[p + 2] << 8) | d[p + 3];
                if (len < 0 || p + 12 + len > d.Length) return -1;
                bool iend = d[p + 4] == 'I' && d[p + 5] == 'E' && d[p + 6] == 'N' && d[p + 7] == 'D';
                int next = p + 8 + (int)len + 4;   // 长度(4)+类型(4)+数据(len)+CRC(4)
                if (iend) return next;
                p = next;
            }
            return -1;
        }
    }
}
#endif
