using UnityEngine;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 动捕角色装配：若 Resources 里存在 Mixamo 人形模型预制体，则实例化它并让
    /// HumanoidAnimator 进入 Playables 动捕模式；否则返回 false，上层继续用程序化
    /// 方块骨骼。这样在动捕资源到位前构建始终可用（自动回退）。
    ///
    /// 装配时统一处理三件事，避免"腾空 / 太小 / 手里多一把方块武器"：
    ///   1) 量测模型渲染包围盒，缩放到标准身高（TargetHeight，随根节点体型放大）；
    ///   2) 把脚底对齐到角色胶囊体底部（groundLocalY），确保站在地面而非腾空；
    ///   3) 不再挂程序化方块兵器——若模型自带兵器则直接用它，或从
    ///      Resources/Characters/Weapon 加载素材兵器挂到右手。
    ///
    /// 资源契约（详见 MIXAMO_SETUP.md）：
    ///   Resources/Characters/PlayerModel  （玩家人形，Humanoid Rig + Animator，Avatar 已指定）
    ///   Resources/Characters/EnemyModel   （敌人人形，可与玩家相同）
    ///   Resources/Characters/Anims/<Mixamo 动作 FBX>
    ///   Resources/Characters/Weapon       （可选：素材兵器预制体，挂到右手）
    /// </summary>
    public static class MecanimCharacter
    {
        /// <summary>标准站立身高（米）。根节点体型缩放会在此基础上叠加（大体型敌人更高）。
        /// 参考黑神话悟空的人物画面占比：配合镜头取景（FOV/距离），角色约占屏高一半。</summary>
        public const float TargetHeight = 2.3f;

        /// <summary>该项目是否配置了动捕资源（有任一模型预制体即认为启用）。</summary>
        public static bool Available =>
            Resources.Load<GameObject>("Characters/PlayerModel") != null ||
            Resources.Load<GameObject>("Characters/EnemyModel") != null;

        /// <summary>
        /// 尝试用动捕模型装配。成功则模型已挂在 visualRoot 下、poser 进入动捕模式并返回 true。
        /// </summary>
        /// <param name="groundLocalY">脚底在 visualRoot 局部空间的目标高度（角色胶囊体底部，通常 = -身高一半）。</param>
        public static bool TryBuild(Transform visualRoot, HumanoidAnimator poser, bool isPlayer,
            Material baseMaterial, WeaponKind weapon, float groundLocalY = -1f)
        {
            if (visualRoot == null || poser == null) return false;

            var prefab = Resources.Load<GameObject>("Characters/" + (isPlayer ? "PlayerModel" : "EnemyModel"))
                      ?? Resources.Load<GameObject>("Characters/PlayerModel");
            if (prefab == null) return false;

            var model = Object.Instantiate(prefab, visualRoot, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            // Generic 骨骼：动作按路径直接绑定（模型与动作 FBX 同源骨架），
            // 不再依赖 Avatar/isHuman——人形重定向的变形问题整类消失。
            var animator = model.GetComponentInChildren<Animator>();
            if (animator == null)
            {
                Object.Destroy(model);
                return false;
            }
            animator.applyRootMotion = false;   // 位移由 CharacterController/NavMesh 负责

            // ---- 缩放到标准身高 + 脚底落地（修复"太小 / 腾空"）----
            FitAndGround(visualRoot, model.transform, groundLocalY);

            if (!poser.TryEnableMecanim(animator))
            {
                Object.Destroy(model);
                return false;
            }

            // 髋骨 XZ 锚定：本包走/跑动作带前进位移（非原地），运行时把髋骨
            // 水平位置钉回绑定位（纵向起伏保留），世界位移交给控制器
            var hips = FindBone(model.transform, "hips");
            poser.SetMocapRoot(model.transform, hips);

            // ---- 兵器（修复"手里多一把方块武器"）----
            // 优先用模型自带兵器；否则加载素材兵器预制体；都没有则不挂（不再用程序化方块）。
            var hand = FindBone(model.transform, "righthand");
            var weaponPivot = FindWeaponInModel(model.transform);
            if (weaponPivot == null && hand != null)
            {
                var wprefab = Resources.Load<GameObject>("Characters/Weapon");
                if (wprefab != null)
                {
                    var w = Object.Instantiate(wprefab, hand, false);
                    w.transform.localPosition = Vector3.zero;
                    w.transform.localRotation = Quaternion.identity;
                    weaponPivot = w.transform;
                }
            }
            poser.weaponPivot = weaponPivot;   // 可为 null（无兵器则无刀光，正常）
            poser.weaponTrail = null;
            return true;
        }

        /// <summary>给动捕模型整体染色（敌我识别的关键）：BaseColor 乘色调 + 可选微自发光。
        /// 玩家与敌人的素模同为纯白，交战时完全分不清敌我——玩家染冷钢色、
        /// 敌人按心魔主题色染色并加微光，暗场景（法院/夜晚）中也一眼可辨。</summary>
        public static void Tint(Transform root, Color color, float emission = 0f)
        {
            if (root == null) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is TrailRenderer || r is LineRenderer || r is ParticleSystemRenderer) continue;
                if (r.GetComponent<TextMesh>() != null) continue;
                foreach (var m in r.materials)
                {
                    if (m == null) continue;
                    Color baseC = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
                    Color c = baseC * color;
                    c.a = baseC.a;
                    m.color = c;
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                    if (emission > 0.001f && m.HasProperty("_EmissionColor"))
                    {
                        m.EnableKeyword("_EMISSION");
                        m.SetColor("_EmissionColor", color * emission);
                    }
                }
            }
        }

        /// <summary>量测模型包围盒，缩放到目标身高并把脚底对齐到 groundLocalY。</summary>
        static void FitAndGround(Transform visualRoot, Transform model, float groundLocalY)
        {
            if (!TryBounds(model, out Bounds b)) return;

            // 缩放：使世界身高 = TargetHeight × 根节点体型缩放（visualRoot.lossyScale）
            float hw = b.size.y;
            if (hw > 0.01f)
            {
                float s = visualRoot.lossyScale.y;
                if (s <= 0.0001f) s = 1f;
                float mul = TargetHeight * s / hw;
                model.localScale *= mul;
            }

            // 落地：把（缩放后的）脚底世界高度换算到 visualRoot 局部，平移模型使其等于 groundLocalY
            if (!TryBounds(model, out b)) return;
            float feetLocal = visualRoot.InverseTransformPoint(new Vector3(b.center.x, b.min.y, b.center.z)).y;
            var lp = model.localPosition;
            lp.y += groundLocalY - feetLocal;
            model.localPosition = lp;
        }

        /// <summary>合并所有渲染器的世界包围盒。</summary>
        static bool TryBounds(Transform model, out Bounds bounds)
        {
            bounds = default;
            var rends = model.GetComponentsInChildren<Renderer>();
            bool has = false;
            foreach (var r in rends)
            {
                if (r == null || r is TrailRenderer || r is LineRenderer) continue;
                if (!has) { bounds = r.bounds; has = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return has;
        }

        /// <summary>按名字（小写包含）在模型层级里找骨骼，如 "righthand"/"hips"。</summary>
        static Transform FindBone(Transform model, string key)
        {
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant().Replace(":", "").Replace("_", "");
                if (n.EndsWith(key) || n.Contains(key)) return t;
            }
            return null;
        }

        static readonly string[] WeaponHints = { "sword", "blade", "katana", "weapon", "axe", "greatsword", "sabre", "saber" };

        /// <summary>在模型层级里找自带兵器（按名字关键词），用于绑定刀光轴。找不到返回 null。</summary>
        static Transform FindWeaponInModel(Transform model)
        {
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                foreach (var h in WeaponHints)
                    if (n.Contains(h) && !n.Contains("hand") && !n.Contains("mixamorig")) return t;
            }
            return null;
        }
    }
}
