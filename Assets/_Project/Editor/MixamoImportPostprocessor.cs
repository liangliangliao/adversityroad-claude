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
        // 版本号变化会让 Unity 自动重导所有匹配资源（本地/CI 缓存都强制生效，
        // 无需手动 Reimport）。改动导入逻辑时 +1。
        public override uint GetVersion() => 2;

        bool InScope =>
            assetPath.Replace('\\', '/').Contains("/Resources/Characters/") &&
            assetPath.ToLowerInvariant().EndsWith(".fbx");

        void OnPreprocessModel()
        {
            if (!InScope) return;
            var mi = assetImporter as ModelImporter;
            if (mi == null) return;
            mi.animationType = ModelImporterAnimationType.Human;             // 人形骨骼（可跨模型重定向）

            // 动作 FBX（Anims/ 下）：复用角色模型的 Avatar，而不是各自从自身骨架生成。
            // 动作文件不带蒙皮，自建 Avatar 的 T-Pose 常校准失败，重定向后腿部扭曲/
            // 左右反（"腿反向/鞋穿反"就是这个）。统一 Copy PlayerModel 的 Avatar 即修复。
            bool isAnim = assetPath.Replace('\\', '/').Contains("/Characters/Anims/");
            UnityEngine.Avatar src = isAnim ? FindModelAvatar() : null;
            if (src != null)
            {
                mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                mi.sourceAvatar = src;
            }
            else
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        }

        static UnityEngine.Avatar FindModelAvatar()
        {
            foreach (var name in new[] { "PlayerModel", "EnemyModel" })
                foreach (var guid in AssetDatabase.FindAssets(name + " t:Model"))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    if (!p.Replace('\\', '/').Contains("/Resources/Characters/")) continue;
                    foreach (var a in AssetDatabase.LoadAllAssetsAtPath(p))
                        if (a is UnityEngine.Avatar av && av.isHuman) return av;
                }
            return null;
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
