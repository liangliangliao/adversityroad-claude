#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace AdversityRoad.EditorTools
{
    /// <summary>
    /// 把 Quaternius 通用动作库（UAL1_Standard.fbx）里的 45 个动作，
    /// **离线重定向**成本工程 Mixamo 骨架上的 Generic 片段。
    ///
    /// 【为什么必须重定向，而不是直接用】
    /// UAL 的骨架是虚幻式命名（root/pelvis/spine_01/upperarm_l/thigh_l…），
    /// 本工程的角色是 Mixamo（mixamorig:Hips/Spine/LeftArm…）。
    /// 而本工程的动画管线是 **Generic 按路径绑定**——路径对不上，
    /// 这 45 个动作放进去一条都不会动，连报错都不会有。
    ///
    /// 【为什么不把全工程改成 Humanoid】
    /// MixamoImportPostprocessor 里写着"重要，勿改回"：此前 Humanoid 管线
    /// （Avatar 自动 T-Pose + 肌肉空间重定向 + 身体朝向估计）在**无蒙皮动作文件**上
    /// 反复产生踮脚/腿反/侧偏，多轮修补无效才整体弃用。那 82 条已经调好的动作
    /// 全部是无蒙皮 Mixamo 文件，正是踩过雷的那一类——把它们改回 Humanoid，
    /// 等于用一个已知会翻车的方案去换 45 个新动作。
    ///
    /// 【这里的做法：只在离线这一步借用 Humanoid，产物仍是 Generic】
    /// UAL 这个文件自带蒙皮网格（Mannequin），Avatar 从真实绑定姿势建，
    /// 不存在"无蒙皮猜 T-Pose"的问题；目标端的 Mixamo Avatar 用
    /// AvatarBuilder 按**显式骨骼映射**建（不让 Unity 去猜，猜正是当年翻车的地方）。
    /// 然后逐帧让 Unity 自己的重定向把动作解算到 Mixamo 骨架上，
    /// 把每根骨头的局部旋转记下来，写成普通 .anim。
    /// 产物是纯 Generic 曲线，运行时管线一行都不用改，对那 82 条动作的影响是**零**。
    ///
    /// 【只记旋转 + 髋部位移】骨长是常量，逐骨记位移既没意义又让文件大一个数量级。
    /// 根位移（applyRootMotion=false 时被丢弃）本来也不该保留——
    /// 本工程的世界位移一律由 CharacterController/NavMeshAgent 负责，
    /// 髋骨 XZ 由 HumanoidAnimator 锚回绑定位（见 MixamoImportPostprocessor 的说明）。
    ///
    /// 烘焙在编辑器加载时自动跑一次（CI 的 batchmode 也会走到），
    /// 源文件或本脚本版本变了才重烤，否则直接跳过。
    /// </summary>
    public static class UalRetargetBaker
    {
        const string SourceFbx = "Assets/_Project/Animations/UAL/UAL1_Standard.fbx";
        const string OutFolder = "Assets/_Project/Resources/Characters/AnimsUAL";
        const string StampFile = OutFolder + "/.bake_stamp.txt";
        const string PlayerModel = "Assets/_Project/Resources/Characters/PlayerModel.fbx";
        const string ClipList = "Assets/_Project/Animations/UAL/UAL_CLIPS.txt";

        /// <summary>改了烘焙逻辑就 +1，让所有机器（含 CI 缓存）重烤。</summary>
        const int BakeVersion = 2;

        const int Fps = 30;

        [InitializeOnLoadMethod]
        static void AutoBake()
        {
            // 延迟一帧：InitializeOnLoad 阶段 AssetDatabase 还可能在导入中
            EditorApplication.delayCall += () => Bake(false);
        }

        [MenuItem("逆境之路/重新烘焙 UAL 动作库")]
        static void ForceBake() => Bake(true);

        public static void Bake(bool force)
        {
            if (!File.Exists(SourceFbx))
            {
                Debug.Log("[CIDIAG][UAL] 源文件不存在，跳过：" + SourceFbx);
                return;
            }
            string stamp = Stamp();
            if (!force && File.Exists(StampFile) && File.ReadAllText(StampFile).Trim() == stamp)
            {
                Debug.Log("[CIDIAG][UAL] 已是最新，跳过烘焙（" + CountBaked() + " 个片段）");
                return;
            }

            var wanted = LoadClipList();
            if (wanted.Count == 0)
            {
                Debug.LogError("[CIDIAG][UAL] 烘焙清单是空的：" + ClipList);
                return;
            }
            var src = LoadSourceClips();
            if (src.Count == 0)
            {
                Debug.LogError("[CIDIAG][UAL] 源 FBX 里没读到人形动作片段——" +
                               "检查 MixamoImportPostprocessor 是否把它设成了 Humanoid。");
                return;
            }
            // 清单里点了名、FBX 里却没有的，必须报出来：这正是"清单与素材悄悄对不上"
            // 的那一刻，而下游（animchain）会一直以为它存在
            var have = new HashSet<string>();
            foreach (var c in src) have.Add(CleanName(c.name));
            foreach (var w in wanted)
                if (!have.Contains(w))
                    Debug.LogError("[CIDIAG][UAL] 清单里有但 FBX 里没有：" + w);
            src.RemoveAll(c => !wanted.Contains(CleanName(c.name)));

            GameObject rig = null;
            try
            {
                rig = BuildTargetRig(out var avatar, out var animator, out var root);
                if (rig == null) return;

                Directory.CreateDirectory(OutFolder);
                var bones = CollectBones(root);
                Debug.Log("[CIDIAG][UAL] 目标骨架 " + bones.Count + " 根，开始烘焙 " +
                          src.Count + " 个动作");

                int ok = 0;
                foreach (var clip in src)
                {
                    if (BakeOne(clip, animator, root, bones)) ok++;
                }
                AssetDatabase.SaveAssets();
                File.WriteAllText(StampFile, stamp);
                AssetDatabase.Refresh();
                Debug.Log("[CIDIAG][UAL] 烘焙完成：" + ok + "/" + src.Count + " 个片段 → " + OutFolder);
            }
            finally
            {
                if (rig != null) Object.DestroyImmediate(rig);
            }
        }

        /// <summary>读烘焙清单。它同时是 tools/animchain.py 的依据——两边读同一份。</summary>
        static HashSet<string> LoadClipList()
        {
            var set = new HashSet<string>();
            if (!File.Exists(ClipList)) return set;
            foreach (var raw in File.ReadAllLines(ClipList))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                set.Add(line);
            }
            return set;
        }

        static string Stamp()
        {
            var fi = new FileInfo(SourceFbx);
            long listLen = File.Exists(ClipList) ? new FileInfo(ClipList).Length : 0;
            return "v" + BakeVersion + " " + fi.Length + " list" + listLen + " " +
                   AssetDatabase.AssetPathToGUID(SourceFbx);
        }

        static int CountBaked() =>
            Directory.Exists(OutFolder) ? Directory.GetFiles(OutFolder, "*.anim").Length : 0;

        static List<AnimationClip> LoadSourceClips()
        {
            var list = new List<AnimationClip>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(SourceFbx))
            {
                var c = o as AnimationClip;
                // FBX 里那条 __preview__ 片段不是动作，跳过
                if (c == null || c.name.StartsWith("__preview__")) continue;
                list.Add(c);
            }
            return list;
        }

        // ---------------- 目标骨架（Mixamo）----------------

        /// <summary>
        /// mixamorig 骨名 → Unity 人形骨位。**显式写死**，不让 Unity 去猜——
        /// 自动映射猜错正是当年 Humanoid 管线翻车的地方。
        /// 注意 Unity 把小指叫 Little 而不是 Pinky。
        /// </summary>
        static readonly (string bone, string human)[] Map =
        {
            ("mixamorig:Hips", "Hips"),
            ("mixamorig:Spine", "Spine"),
            ("mixamorig:Spine1", "Chest"),
            ("mixamorig:Spine2", "UpperChest"),
            ("mixamorig:Neck", "Neck"),
            ("mixamorig:Head", "Head"),
            ("mixamorig:LeftShoulder", "LeftShoulder"),
            ("mixamorig:LeftArm", "LeftUpperArm"),
            ("mixamorig:LeftForeArm", "LeftLowerArm"),
            ("mixamorig:LeftHand", "LeftHand"),
            ("mixamorig:RightShoulder", "RightShoulder"),
            ("mixamorig:RightArm", "RightUpperArm"),
            ("mixamorig:RightForeArm", "RightLowerArm"),
            ("mixamorig:RightHand", "RightHand"),
            ("mixamorig:LeftUpLeg", "LeftUpperLeg"),
            ("mixamorig:LeftLeg", "LeftLowerLeg"),
            ("mixamorig:LeftFoot", "LeftFoot"),
            ("mixamorig:LeftToeBase", "LeftToes"),
            ("mixamorig:RightUpLeg", "RightUpperLeg"),
            ("mixamorig:RightLeg", "RightLowerLeg"),
            ("mixamorig:RightFoot", "RightFoot"),
            ("mixamorig:RightToeBase", "RightToes"),
            ("mixamorig:LeftHandThumb1", "Left Thumb Proximal"),
            ("mixamorig:LeftHandThumb2", "Left Thumb Intermediate"),
            ("mixamorig:LeftHandThumb3", "Left Thumb Distal"),
            ("mixamorig:LeftHandIndex1", "Left Index Proximal"),
            ("mixamorig:LeftHandIndex2", "Left Index Intermediate"),
            ("mixamorig:LeftHandIndex3", "Left Index Distal"),
            ("mixamorig:LeftHandMiddle1", "Left Middle Proximal"),
            ("mixamorig:LeftHandMiddle2", "Left Middle Intermediate"),
            ("mixamorig:LeftHandMiddle3", "Left Middle Distal"),
            ("mixamorig:LeftHandRing1", "Left Ring Proximal"),
            ("mixamorig:LeftHandRing2", "Left Ring Intermediate"),
            ("mixamorig:LeftHandRing3", "Left Ring Distal"),
            ("mixamorig:LeftHandPinky1", "Left Little Proximal"),
            ("mixamorig:LeftHandPinky2", "Left Little Intermediate"),
            ("mixamorig:LeftHandPinky3", "Left Little Distal"),
            ("mixamorig:RightHandThumb1", "Right Thumb Proximal"),
            ("mixamorig:RightHandThumb2", "Right Thumb Intermediate"),
            ("mixamorig:RightHandThumb3", "Right Thumb Distal"),
            ("mixamorig:RightHandIndex1", "Right Index Proximal"),
            ("mixamorig:RightHandIndex2", "Right Index Intermediate"),
            ("mixamorig:RightHandIndex3", "Right Index Distal"),
            ("mixamorig:RightHandMiddle1", "Right Middle Proximal"),
            ("mixamorig:RightHandMiddle2", "Right Middle Intermediate"),
            ("mixamorig:RightHandMiddle3", "Right Middle Distal"),
            ("mixamorig:RightHandRing1", "Right Ring Proximal"),
            ("mixamorig:RightHandRing2", "Right Ring Intermediate"),
            ("mixamorig:RightHandRing3", "Right Ring Distal"),
            ("mixamorig:RightHandPinky1", "Right Little Proximal"),
            ("mixamorig:RightHandPinky2", "Right Little Intermediate"),
            ("mixamorig:RightHandPinky3", "Right Little Distal"),
        };

        static GameObject BuildTargetRig(out Avatar avatar, out Animator animator, out Transform root)
        {
            avatar = null; animator = null; root = null;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerModel);
            if (prefab == null)
            {
                Debug.LogError("[CIDIAG][UAL] 找不到目标模型：" + PlayerModel);
                return null;
            }
            var go = Object.Instantiate(prefab);
            go.name = "UAL_BakeRig";
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;

            var all = go.GetComponentsInChildren<Transform>(true);
            var byName = new Dictionary<string, Transform>();
            foreach (var t in all) byName[t.name] = t;

            var human = new List<HumanBone>();
            foreach (var (bone, hname) in Map)
            {
                if (!byName.ContainsKey(bone)) continue;   // 该骨不存在就跳过（可选骨位）
                human.Add(new HumanBone
                {
                    boneName = bone,
                    humanName = hname,
                    limit = new HumanLimit { useDefaultValues = true },
                });
            }
            // 必需骨位少一根 Avatar 就建不起来，先自己数一遍并报清楚
            if (human.Count < 15)
            {
                Debug.LogError("[CIDIAG][UAL] 目标骨架只映射到 " + human.Count +
                               " 根人形骨，太少，放弃烘焙。");
                Object.DestroyImmediate(go);
                return null;
            }

            var skeleton = new List<SkeletonBone>();
            foreach (var t in all)
                skeleton.Add(new SkeletonBone
                {
                    name = t.name,
                    position = t.localPosition,
                    rotation = t.localRotation,
                    scale = t.localScale,
                });

            var desc = new HumanDescription
            {
                human = human.ToArray(),
                skeleton = skeleton.ToArray(),
                upperArmTwist = 0.5f, lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                armStretch = 0.05f, legStretch = 0.05f,
                feetSpacing = 0f,
                hasTranslationDoF = false,
            };
            avatar = AvatarBuilder.BuildHumanAvatar(go, desc);
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                Debug.LogError("[CIDIAG][UAL] 目标 Avatar 建不起来（valid=" +
                               (avatar != null && avatar.isValid) + " human=" +
                               (avatar != null && avatar.isHuman) + "），放弃烘焙。");
                Object.DestroyImmediate(go);
                return null;
            }
            avatar.name = "UAL_BakeAvatar";

            animator = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.applyRootMotion = false;   // 根位移丢弃：世界位移由角色控制器负责
            root = go.transform;
            return go;
        }

        /// <summary>要记录的骨骼：路径 + Transform。网格节点没有动画，不记。</summary>
        static List<(string path, Transform t)> CollectBones(Transform root)
        {
            var list = new List<(string, Transform)>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root) continue;
                if (t.GetComponent<SkinnedMeshRenderer>() != null ||
                    t.GetComponent<MeshRenderer>() != null) continue;
                list.Add((AnimationUtility.CalculateTransformPath(t, root), t));
            }
            return list;
        }

        // ---------------- 逐帧烘焙 ----------------

        static bool BakeOne(AnimationClip src, Animator animator, Transform root,
            List<(string path, Transform t)> bones)
        {
            string name = CleanName(src.name);
            if (string.IsNullOrEmpty(name)) return false;

            int frames = Mathf.Max(2, Mathf.CeilToInt(src.length * Fps) + 1);
            int n = bones.Count;
            var rot = new Keyframe[n][];
            for (int i = 0; i < n; i++) rot[i] = new Keyframe[frames * 4];
            var hipsPos = new Keyframe[frames * 3];
            int hipsIdx = bones.FindIndex(b => b.t.name == "mixamorig:Hips");

            var graph = PlayableGraph.Create("UALBake_" + name);
            try
            {
                var output = AnimationPlayableOutput.Create(graph, "out", animator);
                var playable = AnimationClipPlayable.Create(graph, src);
                playable.SetApplyFootIK(false);
                output.SetSourcePlayable(playable);

                var prev = new Quaternion[n];
                for (int f = 0; f < frames; f++)
                {
                    float t = Mathf.Min(src.length, f / (float)Fps);
                    playable.SetTime(t);
                    graph.Evaluate(0f);
                    // 第一帧多解算一次：图刚建好时第一次 Evaluate 可能还没写到骨骼上
                    if (f == 0) { playable.SetTime(t); graph.Evaluate(0f); }

                    for (int i = 0; i < n; i++)
                    {
                        var q = bones[i].t.localRotation;
                        // 四元数连续化：相邻帧点积为负时取反，否则曲线插值会绕远路
                        if (f > 0 && Quaternion.Dot(prev[i], q) < 0f)
                            q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                        prev[i] = q;
                        rot[i][f * 4 + 0] = new Keyframe(t, q.x);
                        rot[i][f * 4 + 1] = new Keyframe(t, q.y);
                        rot[i][f * 4 + 2] = new Keyframe(t, q.z);
                        rot[i][f * 4 + 3] = new Keyframe(t, q.w);
                    }
                    if (hipsIdx >= 0)
                    {
                        var p = bones[hipsIdx].t.localPosition;
                        hipsPos[f * 3 + 0] = new Keyframe(t, p.x);
                        hipsPos[f * 3 + 1] = new Keyframe(t, p.y);
                        hipsPos[f * 3 + 2] = new Keyframe(t, p.z);
                    }
                }
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
            }

            var clip = new AnimationClip { frameRate = Fps, name = name };
            for (int i = 0; i < n; i++)
            {
                string path = bones[i].path;
                SetQuat(clip, path, rot[i], frames);
            }
            if (hipsIdx >= 0)
            {
                string hp = bones[hipsIdx].path;
                clip.SetCurve(hp, typeof(Transform), "m_LocalPosition.x", Curve(hipsPos, frames, 3, 0));
                clip.SetCurve(hp, typeof(Transform), "m_LocalPosition.y", Curve(hipsPos, frames, 3, 1));
                clip.SetCurve(hp, typeof(Transform), "m_LocalPosition.z", Curve(hipsPos, frames, 3, 2));
            }

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = name.EndsWith("_Loop") || name == "Idle" || name == "Sword_Idle";
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            string outPath = OutFolder + "/" + name + ".anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(clip, existing);
                Object.DestroyImmediate(clip);
            }
            else AssetDatabase.CreateAsset(clip, outPath);
            return true;
        }

        static void SetQuat(AnimationClip clip, string path, Keyframe[] keys, int frames)
        {
            clip.SetCurve(path, typeof(Transform), "m_LocalRotation.x", Curve(keys, frames, 4, 0));
            clip.SetCurve(path, typeof(Transform), "m_LocalRotation.y", Curve(keys, frames, 4, 1));
            clip.SetCurve(path, typeof(Transform), "m_LocalRotation.z", Curve(keys, frames, 4, 2));
            clip.SetCurve(path, typeof(Transform), "m_LocalRotation.w", Curve(keys, frames, 4, 3));
        }

        static AnimationCurve Curve(Keyframe[] flat, int frames, int stride, int off)
        {
            var ks = new Keyframe[frames];
            for (int f = 0; f < frames; f++) ks[f] = flat[f * stride + off];
            var c = new AnimationCurve(ks);
            for (int f = 0; f < frames; f++)
            {
                AnimationUtility.SetKeyLeftTangentMode(c, f, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(c, f, AnimationUtility.TangentMode.ClampedAuto);
            }
            return c;
        }

        /// <summary>
        /// "Armature|Idle_Loop" → "Idle_Loop"。Blender 导出的 take 名带 Armature| 前缀，
        /// 而下游全部按名字寻址，前缀留着只会让每个调用点各自去剥。
        /// </summary>
        static string CleanName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            int bar = raw.LastIndexOf('|');
            string s = bar >= 0 ? raw.Substring(bar + 1) : raw;
            return s.Trim();
        }
    }
}
#endif
