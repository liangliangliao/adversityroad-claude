using UnityEditor;
using UnityEngine;

namespace AdversityRoad.EditorTools
{
    /// <summary>
    /// 外部导入贴图的"还原出厂设置"。
    ///
    /// 【为什么需要它】玩家的原话：模型在别的软件里打开分辨率很高、颜色很真实，
    /// 一放进游戏场景，分辨率和颜色"都完全跟原来的不一致"。查下来根因很朴素——
    /// Assets/_Project/Resources/Characters/ 下的六张角色贴图**一个 .meta 都没有**：
    ///     Paladin_diffuse / _normal / _specular
    ///     maria_diffuse   / _normal / _specular
    /// 没有 .meta，Unity 就按默认导入。而默认里有两条对写实角色是致命的：
    ///
    ///   ① 法线贴图被当成**普通彩色贴图**导入（textureType = Default、sRGB = 开）。
    ///      法线图存的是方向向量，不是颜色；按 sRGB 解码等于把每个分量做了一次
    ///      伽马变换，凹凸方向整体偏掉。皮肤与布料的受光于是全错——这正是
    ///      "肤色变了、衣服颜色变了"最大的一份。
    ///   ② 高光/粗糙度图同样被当成 sRGB。它们是线性数据，一样不该做伽马变换。
    ///
    /// 项目本身是 Linear 色彩空间（ProjectSettings m_ActiveColorSpace: 1），
    /// 这是对的；错的是这几张图各自的解码方式。
    ///
    /// 【为什么做成 Postprocessor 而不是手改六个 .meta】玩家要的不止这两个角色：
    /// "任何外部引入到这个游戏场景里的 3D 原型，比如武器、剑、背包，都不能失去
    /// 它原来的效果"。手改的 .meta 只管当下这几张，下一张丢进来的图照样错。
    /// 写成导入钩子，规则就跟着目录走，以后任何人往里丢图都自动是对的。
    ///
    /// 判据用**文件名词元**，与 PlayerAppearance.FixModelMaterials 里认贴图用的是
    /// 同一套词（basecolor/diffuse/albedo、normal/nrm、metallic/roughness/specular…），
    /// 两处保持一致，免得"接得上却解码错"。
    /// </summary>
    public class TextureFidelityPostprocessor : AssetPostprocessor
    {
        // 只管我们自己引入的美术资产，不去动 TutorialInfo 之类的工程自带图。
        const string Root = "/_Project/Resources/";

        static readonly string[] NormalKeys =
            { "normal", "_nrm", "_norm", "normalmap", "_n." };
        // 线性数据（不是颜色）：按 sRGB 解码就会算错光照
        static readonly string[] LinearKeys =
            { "specular", "metallic", "metalness", "roughness", "gloss",
              "_ao", "occlusion", "height", "displacement", "mask", "_orm" };

        static bool Has(string path, string[] keys)
        {
            string p = path.ToLowerInvariant();
            foreach (var k in keys) if (p.Contains(k)) return true;
            return false;
        }

        void OnPreprocessTexture()
        {
            if (assetPath.Replace('\\', '/').IndexOf(Root, System.StringComparison.Ordinal) < 0)
                return;
            var im = (TextureImporter)assetImporter;

            // 只在**没有 .meta**（首次导入）时接管：这个钩子是来补默认值的，
            // 不是来抢方向盘的。谁真的手工设过导入参数，.meta 就在版本库里，
            // 那份设置说了算。importSettingsMissing 正是 Unity 用来表示
            // "这个资产此前没有导入设置"的标志，比自己去看文件在不在可靠。
            if (!im.importSettingsMissing) return;

            if (Has(assetPath, NormalKeys))
            {
                im.textureType = TextureImporterType.NormalMap;
                im.sRGBTexture = false;          // NormalMap 类型本身就是线性，写明以防误改
            }
            else if (Has(assetPath, LinearKeys))
            {
                im.textureType = TextureImporterType.Default;
                im.sRGBTexture = false;          // 线性数据：不做伽马变换
            }
            else
            {
                im.textureType = TextureImporterType.Default;
                im.sRGBTexture = true;           // 底色图：是颜色，该走 sRGB
            }

            // 分辨率与画质：源图是 2048，就让它以 2048 进来。
            // 移动端默认压缩档在法线与渐变皮肤上会出现明显色带，这里统一提到
            // 高质量档；2048 的 ASTC 6x6 一张约 1MB，两个主角完全付得起。
            im.maxTextureSize = Mathf.Max(im.maxTextureSize, 2048);
            im.textureCompression = TextureImporterCompression.CompressedHQ;
            im.compressionQuality = 100;
            im.crunchedCompression = false;      // crunch 是有损上再来一次有损
            im.mipmapEnabled = true;
            im.filterMode = FilterMode.Trilinear;
            im.anisoLevel = 4;                   // 斜看身体时纹理不糊

            var android = im.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = 2048;
            android.format = TextureImporterFormat.ASTC_6x6;
            android.compressionQuality = 100;
            im.SetPlatformTextureSettings(android);
        }
    }
}
