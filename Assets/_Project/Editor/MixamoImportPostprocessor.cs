#if UNITY_EDITOR
using UnityEditor;

namespace AdversityRoad.EditorTools
{
    /// <summary>
    /// 一键就绪：凡是放进 Resources/Characters/ 的 FBX（Mixamo 角色/动作），
    /// 导入时自动设为 Humanoid 骨骼；走/跑/待机片段自动勾选 Loop。
    /// 让用户「把文件丢进去即可」，无需在 Inspector 里手动配置 Rig/Loop。
    ///
    /// 编辑器脚本，不进入最终包体；但导入设置会写进 .meta，提交后 CI 打包沿用。
    /// </summary>
    public class MixamoImportPostprocessor : AssetPostprocessor
    {
        bool InScope =>
            assetPath.Replace('\\', '/').Contains("/Resources/Characters/") &&
            assetPath.ToLowerInvariant().EndsWith(".fbx");

        void OnPreprocessModel()
        {
            if (!InScope) return;
            var mi = assetImporter as ModelImporter;
            if (mi == null) return;
            mi.animationType = ModelImporterAnimationType.Human;             // 人形骨骼（可跨模型重定向）
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;   // 各自从自身骨架生成 Avatar
        }

        void OnPreprocessAnimation()
        {
            if (!InScope) return;
            var mi = assetImporter as ModelImporter;
            if (mi == null) return;
            var clips = mi.defaultClipAnimations;
            bool changed = false;
            for (int i = 0; i < clips.Length; i++)
            {
                string n = clips[i].name.ToLowerInvariant();
                bool loop = n.Contains("idle") || n.Contains("walk") || n.Contains("run");
                if (clips[i].loopTime != loop) { clips[i].loopTime = loop; changed = true; }
            }
            if (changed) mi.clipAnimations = clips;
        }
    }
}
#endif
