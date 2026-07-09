#if UNITY_EDITOR
using UnityEditor;

namespace AdversityRoad.EditorTools
{
    /// <summary>
    /// 一键就绪：凡是放进 Resources/Characters/ 的 FBX（Mixamo 角色/动作），
    /// 导入时自动设为 Humanoid 骨骼；走/跑/待机片段自动勾选 Loop。
    /// 让用户「把文件丢进去即可」，无需在 Inspector 里手动配置 Rig/Loop。
    ///
    /// 关键修复（腿反/脚朝后）：动作 FBX 不带蒙皮，自建 Avatar 的 T-Pose 校准
    /// 常失败，重定向后腿部扭曲/朝向反。修复分两层保证在 CI 干净导入下也生效：
    ///   1) .meta 已直接写入 avatarSetup=2 + sourceAvatar=PlayerModel（引用带依赖，
    ///      资产管线会保证 PlayerModel 先导入，顺序无关、确定性生效）；
    ///   2) 本脚本绝不把已配置的 CopyFromOther 降级回 CreateFromThisModel
    ///      （旧版在找不到 Avatar 时会覆盖回自建，这正是 CI 上修复失效的原因）。
    ///
    /// 根变换设置：旋转按「原始朝向」烘焙进姿态——旋转类招式（旋风腿/回旋斩）
    /// 保留转体；纵向位移烘焙进姿态——飞踢/跃劈保留腾空；XZ 位移交给根运动并被
    /// 控制器丢弃（位移由 CharacterController/NavMeshAgent 负责，避免双重位移）。
    ///
    /// 编辑器脚本，不进入最终包体；但导入设置会写进 .meta，提交后 CI 打包沿用。
    /// </summary>
    public class MixamoImportPostprocessor : AssetPostprocessor
    {
        // 版本号变化会让 Unity 自动重导所有匹配资源（本地/CI 缓存都强制生效，
        // 无需手动 Reimport）。改动导入逻辑时 +1。
        public override uint GetVersion() => 4;

        bool InScope =>
            assetPath.Replace('\\', '/').Contains("/Resources/Characters/") &&
            assetPath.ToLowerInvariant().EndsWith(".fbx");

        void OnPreprocessModel()
        {
            if (!InScope) return;
            var mi = assetImporter as ModelImporter;
            if (mi == null) return;
            mi.animationType = ModelImporterAnimationType.Human;             // 人形骨骼（可跨模型重定向）

            bool isAnim = assetPath.Replace('\\', '/').Contains("/Characters/Anims/");
            if (!isAnim)
            {
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                return;
            }

            // 动作 FBX：复用角色模型的 Avatar。meta 已写好 CopyFromOther+sourceAvatar，
            // 这里只在尚未配置时补配；找不到源 Avatar 时【保持原样】，绝不降级。
            if (mi.avatarSetup != ModelImporterAvatarSetup.CopyFromOther || mi.sourceAvatar == null)
            {
                var src = FindModelAvatar();
                if (src != null)
                {
                    mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                    mi.sourceAvatar = src;
                }
                else if (mi.avatarSetup == ModelImporterAvatarSetup.NoAvatar)
                    mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            }
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
            var clips = mi.clipAnimations != null && mi.clipAnimations.Length > 0
                ? mi.clipAnimations : mi.defaultClipAnimations;
            for (int i = 0; i < clips.Length; i++)
            {
                string n = clips[i].name.ToLowerInvariant();
                bool loop = n.Contains("idle") || n.Contains("walk") || n.Contains("run");
                clips[i].loopTime = loop;
                // 根旋转一律烘焙进姿态（转体类招式保留转身），但"基于"分两类：
                //  · 循环动画（待机/格斗架势/走/跑）按【身体朝向】回正——这类片段
                //    原作常是侧身/偏轴站架（格斗架势尤甚），按"原始朝向"烘焙会把
                //    偏角带进姿态：根朝向敌人而身体/腿保持偏转 ≙ "搏击状态腿部
                //    朝向不对、走跑蟹行"的根因；身体朝向基准自动对齐正前方。
                //  · 一次性招式按【原始朝向】——保留原作的出击轴向（侧踢的侧身、
                //    回旋斩的旋转相位），命中方向与根一致由控制器保证。
                clips[i].lockRootRotation = true;
                clips[i].keepOriginalOrientation = !loop;
                // 纵向位移烘焙进姿态：飞踢/跃劈的腾空可见；以原始高度为准。
                clips[i].lockRootHeightY = true;
                clips[i].keepOriginalPositionY = true;
                clips[i].heightFromFeet = false;
                // XZ 位移不烘焙：交给根运动并被丢弃，位移由控制器负责（防双重位移）。
                clips[i].lockRootPositionXZ = false;
            }
            mi.clipAnimations = clips;
        }
    }
}
#endif
