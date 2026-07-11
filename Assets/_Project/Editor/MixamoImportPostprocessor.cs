#if UNITY_EDITOR
using UnityEditor;

namespace AdversityRoad.EditorTools
{
    /// <summary>
    /// 一键就绪：凡是放进 Resources/Characters/ 的 FBX（Mixamo 角色/动作），
    /// 导入时统一设为 **Generic** 骨骼；走/跑/待机片段自动勾选 Loop。
    ///
    /// 为什么是 Generic 而不是 Humanoid（重要，勿改回）：
    /// 经 Blender 实测，本项目的模型与全部动作 FBX 骨骼层级完全一致
    /// （Armature/mixamorig:*，模型与动作同源导出），Generic 按路径直接绑定，
    /// 骨骼拿到的就是 Mixamo 原始曲线——脚跟落地、腿部朝向、站架朝向
    /// 全部所见即所得。此前 Humanoid 管线（Avatar 自动 T-Pose + 肌肉空间
    /// 重定向 + 身体朝向估计）在无蒙皮动作文件上反复产生踮脚/腿反/侧偏
    /// 变形，多轮修补无效，故整体弃用。
    ///
    /// 动作中的髋骨前进位移（实测 Walking +1.8m / Running +3.26m，非原地）
    /// 由运行时 HumanoidAnimator 将髋骨 XZ 锚定在绑定位处理（保留纵向起伏），
    /// 世界位移始终由 CharacterController/NavMeshAgent 负责。
    ///
    /// 编辑器脚本，不进入最终包体；导入设置在每次导入时确定性生效。
    /// </summary>
    public class MixamoImportPostprocessor : AssetPostprocessor
    {
        // 版本号变化会让 Unity 自动重导所有匹配资源（本地/CI 缓存都强制生效，
        // 无需手动 Reimport）。改动导入逻辑时 +1。
        public override uint GetVersion() => 9;

        bool InScope =>
            assetPath.Replace('\\', '/').Contains("/Resources/Characters/") &&
            assetPath.ToLowerInvariant().EndsWith(".fbx");

        void OnPreprocessModel()
        {
            if (!InScope) return;
            var mi = assetImporter as ModelImporter;
            if (mi == null) return;
            mi.animationType = ModelImporterAnimationType.Generic;
            // 材质/贴图：导入模型自带材质并保留内嵌贴图（下载的带皮肤 FBX 若不开这个，
            // 会因材质没连上贴图而显示为一片白模——"换了带皮肤模型却还是白模"的根因之一）
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;   // 内嵌材质，直接用内嵌贴图
            // 武器网格开启 Read/Write：运行时截面分析判柄端（握持对齐）需要读顶点
            if (assetPath.Replace('\\', '/').Contains("/Characters/Weapons"))
                mi.isReadable = true;
            // 匹配 Anims/ 与角色专属动作库 Anims2/（及未来的 AnimsN/）
            bool isAnim = assetPath.Replace('\\', '/').Contains("/Characters/Anims");
            if (isAnim)
            {
                mi.sourceAvatar = null;
                mi.avatarSetup = ModelImporterAvatarSetup.NoAvatar;   // 按路径绑定，无需 Avatar
            }
            else
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        }

        void OnPreprocessAnimation()
        {
            if (!InScope) return;
            var mi = assetImporter as ModelImporter;
            if (mi == null) return;
            var clips = mi.clipAnimations != null && mi.clipAnimations.Length > 0
                ? mi.clipAnimations : mi.defaultClipAnimations;

            // 片段名统一取自【文件名】：有 @ 用 @ 后缀，没有 @ 用整个文件名。
            // 关键容错：Mixamo 单独下载的动作 FBX 内部 take 名是 "mixamo.com"，
            // 若不按文件名重命名，运行时会因识别不到片段名而判定"动作缺失"——
            // 这样无论文件叫 `角色@Great Sword Blocking.fbx` 还是
            // `Great Sword Blocking.fbx`，都能被正确识别。
            string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            int at = file.IndexOf('@');
            string clipName = (at >= 0 && at < file.Length - 1)
                ? file.Substring(at + 1).Trim() : file.Trim();

            for (int i = 0; i < clips.Length; i++)
            {
                clips[i].name = clips.Length > 1 ? clipName + " " + (i + 1) : clipName;
                string n = clipName.ToLowerInvariant();
                clips[i].loopTime = n.Contains("idle") || n.Contains("walk") || n.Contains("run");
            }
            mi.clipAnimations = clips;
        }
    }
}
#endif
