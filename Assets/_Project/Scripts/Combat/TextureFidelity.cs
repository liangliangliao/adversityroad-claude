using System.Collections.Generic;
using UnityEngine;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 外部模型贴图的还原度兜底：**补上缺失的 mipmap 链**。
    ///
    /// 【为什么需要这个】CI 的材质诊断把事实打出来了：
    ///   角色·壹/敌人（FBX + 独立 PNG，走 TextureImporter）
    ///     _BaseMap = Paladin_diffuse  2048x2048  格式=BC7  mip=12
    ///   角色·贰（Avaturn 的 .glb，贴图内嵌，走 glTFast）
    ///     baseColorTexture = image_8  1024x1024  格式=RGB24  **mip=1**
    ///
    /// 分辨率没被降（源文件就是 1024，进来还是 1024），少的是整条 mip 链。
    /// 角色在手机屏幕上只占几百像素，一张 1024 的图要缩到 1/3 甚至更小去采样；
    /// 没有 mip 就只能点采样欠采样——皮革上的菱格缝线糊成噪点、边缘一动就闪，
    /// 观感正是"分辨率被降低了、质感失真"。这不是压缩问题，也不是色彩空间问题，
    /// 是采样问题，而且只会在**缩小**时出现，所以在编辑器里放大看反倒是好的。
    ///
    /// glTFast 走的是自己的 ScriptedImporter/运行时加载，不经过 TextureImporter，
    /// 我之前改的 sRGB / 法线类型 / ASTC 那一套对它一条都不生效（同一份诊断
    /// 也证明了这点：它连属性名都不是 URP/Lit 那一套）。所以在运行时补。
    ///
    /// 做法：把原贴图 Blit 进一张同色彩空间的 RT，再 ReadPixels 进一张
    /// **带 mip 链**的新贴图，生成 mip 之后压回 GPU 格式。
    /// 不读原贴图的 CPU 数据（glTFast 的贴图未必可读），所以对任何来源都成立。
    /// 色彩空间严格照抄原贴图，这一步**只补 mip，不改别的**——
    /// 颜色对不对是另一件事，不能借这次改动顺手动它。
    ///
    /// 对**任何**外部引入的 3D 资产通用：角色、武器、背包走的是同一个入口。
    /// </summary>
    public static class TextureFidelity
    {
        /// <summary>小图不处理：UI 图标、占位图重建了也看不出差别，只是浪费。</summary>
        const int MinSize = 128;

        /// <summary>同一张源贴图只重建一次（多个材质共用一张图是常态）。</summary>
        static readonly Dictionary<Texture2D, Texture2D> _done =
            new Dictionary<Texture2D, Texture2D>();

        /// <summary>把 go 下所有材质里【没有 mip 链】的贴图换成带 mip 链的等价贴图。</summary>
        public static void EnsureMipmaps(GameObject go)
        {
            if (go == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r is TrailRenderer || r is LineRenderer ||
                    r is ParticleSystemRenderer) continue;
                var mats = r.materials;             // 实例材质，安全修改
                bool touched = false;
                foreach (var m in mats)
                {
                    if (m == null || m.shader == null) continue;
                    // 【不写死属性名】上一轮就栽在这里：探的是 URP/Lit 的
                    // _BaseMap/_BumpMap，而 glTF 着色器叫 baseColorTexture/
                    // normalTexture，HasProperty 全 false，整套改动空转。
                    // 直接问着色器它有哪些贴图属性，两套命名一并覆盖。
                    int n = m.shader.GetPropertyCount();
                    for (int i = 0; i < n; i++)
                    {
                        if (m.shader.GetPropertyType(i) !=
                            UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
                        string prop = m.shader.GetPropertyName(i);
                        var src = m.GetTexture(prop) as Texture2D;
                        if (src == null || src.mipmapCount > 1) continue;
                        if (src.width < MinSize || src.height < MinSize) continue;
                        var better = WithMips(src);
                        if (better != null && better != src)
                        {
                            m.SetTexture(prop, better);
                            touched = true;
                        }
                    }
                }
                if (touched) r.materials = mats;
            }
        }

        static Texture2D WithMips(Texture2D src)
        {
            if (_done.TryGetValue(src, out var cached)) return cached;
            Texture2D dst = null;
            RenderTexture rt = null;
            var prevActive = RenderTexture.active;
            try
            {
                // 色彩空间必须与原贴图一致：底色图是 sRGB，法线/粗糙度/AO 是线性。
                // 判据取原贴图的 graphicsFormat（…_SRGB 结尾即 sRGB），
                // 不靠属性名去猜——猜错就是把颜色整体改掉，比缺 mip 还糟。
                bool srgb = src.graphicsFormat.ToString()
                    .IndexOf("SRGB", System.StringComparison.OrdinalIgnoreCase) >= 0;
                rt = RenderTexture.GetTemporary(src.width, src.height, 0,
                    RenderTextureFormat.ARGB32,
                    srgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                dst = new Texture2D(src.width, src.height, TextureFormat.RGBA32,
                                    true /* mipChain */, !srgb /* linear */);
                dst.name = src.name + "_mip";
                dst.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0, false);
                dst.wrapMode = src.wrapMode;
                dst.filterMode = FilterMode.Trilinear;   // 有了 mip 才谈得上三线性
                dst.anisoLevel = 4;                      // 掠射角（地面、衣褶）显著变清楚
                dst.Apply(true, false);                  // 生成整条 mip 链
                // 压回 GPU 格式：1024² RGBA32 带 mip 约 5.6MB，五张就是 28MB，
                // 手机上不能这么放。压完约 1/6。
                dst.Compress(true);
                dst.Apply(false, true);                  // 上传后释放 CPU 端副本
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[TextureFidelity] 重建 mip 失败（保留原贴图）：" +
                                 src.name + " —— " + e.Message);
                dst = null;
            }
            finally
            {
                RenderTexture.active = prevActive;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
            }
            _done[src] = dst;
            return dst;
        }
    }
}
