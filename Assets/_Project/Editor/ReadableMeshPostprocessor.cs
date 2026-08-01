using UnityEditor;

namespace AdversityRoad.EditorTools
{
    /// <summary>
    /// 强制装配类模型开启 Read/Write（面具 / 武器 / 背包）。
    ///
    /// 为什么必须开：这些资产的摆位不是硬编码的，而是**运行时量测顶点**算出来的——
    /// 面具靠"纵向切片量截面宽"找眼孔线，武器靠"近尖端顶点拟合刃中轴"，
    /// 背包靠"厚轴两端薄片数占用格"判断肩带侧。所有这些量测都走 `Mesh.vertices`，
    /// 而 Unity 默认导入的模型 `isReadable == false`，取顶点直接失败。
    ///
    /// 失败时代码会静默退回经验常数（面具"中心上移 0.18 头宽"之类）。
    /// 那条兜底路径就是「面具戴歪、眼孔对不上眼睛」的来源：不是算错了，
    /// 而是**根本没算**，用的是一个对所有面具都不准的固定偏移。
    ///
    /// 只作用于这三个目录，不动角色本体与动作库（那些不需要读顶点，
    /// 开了反而白白多占一份内存）。
    /// </summary>
    public class ReadableMeshPostprocessor : AssetPostprocessor
    {
        static readonly string[] Dirs =
        {
            "/Resources/Characters/Masks/",
            "/Resources/Characters/Weapons/",
            "/Resources/Characters/Backpacks/",
        };

        void OnPreprocessModel()
        {
            string p = assetPath.Replace('\\', '/');
            bool wanted = false;
            foreach (var d in Dirs) if (p.Contains(d)) { wanted = true; break; }
            if (!wanted) return;

            var mi = (ModelImporter)assetImporter;
            if (!mi.isReadable) mi.isReadable = true;
        }
    }
}
