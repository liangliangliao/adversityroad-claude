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
        public override uint GetVersion() => 5;

        bool InScope =>
            assetPath.Replace('\\', '/').Contains("/Resources/Characters/") &&
            assetPath.ToLowerInvariant().EndsWith(".fbx");

        void OnPreprocessModel()
        {
            if (!InScope) return;
            var mi = assetImporter as ModelImporter;
            if (mi == null) return;
            mi.animationType = ModelImporterAnimationType.Generic;
            bool isAnim = assetPath.Replace('\\', '/').Contains("/Characters/Anims/");
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
            for (int i = 0; i < clips.Length; i++)
            {
                string n = clips[i].name.ToLowerInvariant();
                clips[i].loopTime = n.Contains("idle") || n.Contains("walk") || n.Contains("run");
            }
            mi.clipAnimations = clips;
        }
    }
}
#endif
