using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.Combat;

namespace AdversityRoad.Player
{
    /// <summary>
    /// 玩家外观系统：角色与武器分离的双资产体系。
    ///
    /// 角色（二选一，资产分离，可持久化）：
    ///   预设 0「角色·壹」= Resources/Characters/PlayerModel + 动作库 Characters/Anims
    ///   预设 1「角色·贰」= Resources/Characters/PlayerModel2（支持 .glb/.gltf/.fbx，
    ///     Resources.Load 按名加载与扩展名无关；glTFast 包负责 glb/gltf 导入），
    ///     动作库默认【沿用】角色壹的 Characters/Anims（Mixamo 标准骨架按路径绑定通用）；
    ///     若 Characters/Anims2/ 放了片段则优先用作角色贰专属动作库。
    ///   （模型缺失自动回退 PlayerModel；两者都缺才回退程序化方块骨骼）
    ///
    /// 武器库（与角色独立选择）：
    ///   Resources/Characters/Weapons/ 下每个模型文件即一件武器（文件名=武器名），
    ///   支持 .glb/.fbx/.gltf，也可直接丢 .zip（编辑器自动解压出模型并删除压缩包）；
    ///   武器数量=目录下模型文件数量。玩家默认持剑（模型自带兵器或 Characters/Weapon），
    ///   从武器库重选即替换手中武器：隐藏模型自带兵器 → 新武器挂右手 →
    ///   自动归一尺寸 → 刀光轴随武器切换。
    /// </summary>
    public class PlayerAppearance : MonoBehaviour
    {
        public Transform visualRoot;
        public HumanoidAnimator poser;
        public Material baseMaterial;

        public int Preset { get; private set; }
        public HumanoidRig Rig { get; private set; }

        /// <summary>当前武器名（"" = 默认佩剑：模型自带兵器或 Characters/Weapon）。</summary>
        public string CurrentWeapon { get; private set; } = "";

        const string PrefKey = "player_preset";
        const string WeaponPref = "player_weapon";
        const string EquippedName = "EquippedWeapon";

        public static readonly string[] PresetNames = { "角色·壹（青岚）", "角色·贰" };

        public void Init()
        {
            Preset = PlayerPrefs.GetInt(PrefKey, 0);
            CurrentWeapon = PlayerPrefs.GetString(WeaponPref, "");
            Rebuild();
        }

        /// <summary>武器库清单（Resources/Characters/Weapons/ 下全部预制体名）。</summary>
        public static string[] ListWeapons()
        {
            var prefabs = Resources.LoadAll<GameObject>("Characters/Weapons");
            var names = new List<string>();
            foreach (var p in prefabs)
                if (p != null && !names.Contains(p.name)) names.Add(p.name);
            names.Sort();
            return names.ToArray();
        }

        /// <summary>选择角色（0/1），立即重建外观并持久化。</summary>
        public void SetPreset(int idx)
        {
            Preset = Mathf.Clamp(idx, 0, PresetNames.Length - 1);
            PlayerPrefs.SetInt(PrefKey, Preset);
            PlayerPrefs.Save();
            Rebuild();
            Core.GameEvents.RaiseSubtitle("已切换角色：" + PresetNames[Preset]);
        }

        /// <summary>兼容旧入口：循环切换角色。</summary>
        public void TogglePreset() => SetPreset((Preset + 1) % PresetNames.Length);

        /// <summary>从武器库选一件武器拿在手中（null/"" = 恢复默认佩剑），重选即替换。</summary>
        public void EquipWeapon(string weaponName)
        {
            CurrentWeapon = weaponName ?? "";
            PlayerPrefs.SetString(WeaponPref, CurrentWeapon);
            PlayerPrefs.Save();
            ApplyWeapon();
            Core.GameEvents.RaiseSubtitle(string.IsNullOrEmpty(CurrentWeapon)
                ? "已装备：默认佩剑" : "已装备：" + CurrentWeapon);
        }

        void Rebuild()
        {
            for (int i = visualRoot.childCount - 1; i >= 0; i--)
                DestroyImmediate(visualRoot.GetChild(i).gameObject);
            visualRoot.localRotation = Quaternion.identity;
            visualRoot.localPosition = Vector3.zero;
            visualRoot.localScale = Vector3.one;

            // 优先动捕模型（角色资产分离：各自模型 + 各自动作库）；缺失自动回退
            Rig = null;
            string modelName = Preset == 1 ? "PlayerModel2" : "PlayerModel";
            string animsFolder = Preset == 1 ? "Characters/Anims2" : null;
            if (poser != null && MecanimCharacter.TryBuild(visualRoot, poser, true,
                    baseMaterial, WeaponKind.Sword, -1f, modelName, animsFolder))
            {
                // 不染色：保持模型原本材质/肤色（按用户要求恢复原色）
                ApplyWeapon();
                return;
            }

            // 程序化方块骨骼回退（无任何模型资产时保底可玩）
            HumanoidRig.Config cfg;
            WeaponKind weapon;
            if (Preset == 0)
            {
                cfg = new HumanoidRig.Config
                {
                    skin = new Color(0.95f, 0.8f, 0.68f),
                    top = new Color(0.22f, 0.42f, 0.72f),
                    bottom = new Color(0.16f, 0.28f, 0.48f),
                    shoes = new Color(0.15f, 0.15f, 0.18f),
                    hair = new Color(0.12f, 0.1f, 0.1f),
                    eye = new Color(0.1f, 0.1f, 0.12f),
                    hasHat = false,
                    bulk = 0.92f
                };
                weapon = WeaponKind.Sword;
            }
            else
            {
                cfg = new HumanoidRig.Config
                {
                    skin = new Color(0.82f, 0.62f, 0.46f),
                    top = new Color(0.62f, 0.28f, 0.2f),
                    bottom = new Color(0.35f, 0.2f, 0.14f),
                    shoes = new Color(0.2f, 0.14f, 0.1f),
                    hair = new Color(0.1f, 0.08f, 0.08f),
                    hatColor = new Color(0.78f, 0.68f, 0.42f),
                    eye = new Color(0.1f, 0.08f, 0.06f),
                    hasHat = true,
                    bulk = 1.15f
                };
                weapon = WeaponKind.Blade;
            }

            Rig = HumanoidRig.Build(visualRoot, cfg, baseMaterial);
            if (poser != null)
            {
                poser.rig = Rig;
                // 静息持械：刃朝斜上方立于体侧（不插地）
                var wr = WeaponFactory.Build(weapon, Rig.handR, baseMaterial,
                    new Vector3(0, -0.06f, 0.03f), new Vector3(-32f, 0, 8f));
                if (wr != null)
                {
                    poser.weaponPivot = wr.pivot;
                    poser.weaponTrail = wr.trail;
                }
            }
        }

        /// <summary>把当前选择的武器装到右手：卸下上一件外装武器 → 隐藏/恢复模型自带
        /// 兵器 → 实例化新武器并做「定尺 + 握持对齐」→ 刀光轴跟随。
        /// 模型没有自带兵器（glb 角色常见）时，默认佩剑用程序化长剑兜底——
        /// 「默认佩剑」在任何角色手里都真实可见。</summary>
        void ApplyWeapon()
        {
            if (visualRoot == null || visualRoot.childCount == 0) return;
            var model = visualRoot.GetChild(0);
            var hand = MecanimCharacter.FindBone(model, "righthand");

            // 卸下上一件外装武器
            if (hand != null)
                for (int i = hand.childCount - 1; i >= 0; i--)
                    if (hand.GetChild(i).name == EquippedName)
                        Destroy(hand.GetChild(i).gameObject);

            var builtin = MecanimCharacter.FindWeaponInModel(model);
            // 按名扫描整个武器库（含 zip 解压出的子目录——Resources.LoadAll 递归）
            GameObject prefab = null;
            if (!string.IsNullOrEmpty(CurrentWeapon))
                foreach (var p in Resources.LoadAll<GameObject>("Characters/Weapons"))
                    if (p != null && p.name == CurrentWeapon) { prefab = p; break; }
            bool useCustom = prefab != null && hand != null;

            // 模型自带兵器：换装时隐藏，恢复默认时显示
            if (builtin != null)
                foreach (var r in builtin.GetComponentsInChildren<Renderer>(true))
                    r.enabled = !useCustom;

            if (useCustom)
            {
                var w = Object.Instantiate(prefab, hand, false);
                w.name = EquippedName;
                w.transform.localPosition = Vector3.zero;
                w.transform.localRotation = Quaternion.identity;
                FitAndGripWeapon(w.transform, hand);
                if (poser != null) poser.weaponPivot = w.transform;
            }
            else if (builtin != null)
            {
                if (poser != null) poser.weaponPivot = builtin;
            }
            else if (hand != null)
            {
                // 默认佩剑兜底：程序化长剑（刃沿 +Y、原点在柄端），同样走握持对齐
                var holder = new GameObject(EquippedName).transform;
                holder.SetParent(hand, false);
                var wr = WeaponFactory.Build(WeaponKind.Sword, holder, baseMaterial,
                    Vector3.zero, Vector3.zero);
                FitAndGripWeapon(holder, hand);
                if (poser != null && wr != null)
                {
                    poser.weaponPivot = wr.pivot;
                    poser.weaponTrail = wr.trail;
                }
            }
        }

        /// <summary>武器定尺 + 握持对齐（修复"握在刀刃上"）：
        /// ① 在武器自身坐标系里求包围盒，最长轴=刃轴；离武器原点近的一端视为柄端
        ///   （建模惯例 pivot 在柄部；武器预制体内若有名为 Grip/Handle 的子节点则优先以它为柄）；
        /// ② 按角色体型把刃长归一（≈1.35m）；
        /// ③ 用手部骨骼几何推握持方向：刃轴=拇指方向去掉手指分量（即拳眼方向），
        ///   取指向前臂延长线一侧——把刃轴转过去、柄端放进手心：握剑柄，刃朝外。</summary>
        void FitAndGripWeapon(Transform w, Transform hand)
        {
            if (!LocalBounds(w, out Bounds lb)) return;

            // 最长轴与两端
            int axis = 0;
            if (lb.size.y >= lb.size.x && lb.size.y >= lb.size.z) axis = 1;
            else if (lb.size.z >= lb.size.x && lb.size.z >= lb.size.y) axis = 2;
            Vector3 axisDir = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
            float ext = axis == 0 ? lb.extents.x : axis == 1 ? lb.extents.y : lb.extents.z;
            Vector3 endA = lb.center - axisDir * ext;
            Vector3 endB = lb.center + axisDir * ext;

            // 柄端判定：Grip/Handle 子节点优先，否则取离武器原点近的一端
            Vector3 gripL, tipL;
            var gripNode = FindDeep(w, "grip");
            if (gripNode == null) gripNode = FindDeep(w, "handle");
            if (gripNode != null)
            {
                Vector3 g = w.InverseTransformPoint(gripNode.position);
                bool aNear = (g - endA).sqrMagnitude <= (g - endB).sqrMagnitude;
                gripL = aNear ? endA : endB;
                tipL = aNear ? endB : endA;
            }
            else if (endA.sqrMagnitude <= endB.sqrMagnitude) { gripL = endA; tipL = endB; }
            else { gripL = endB; tipL = endA; }
            if ((tipL - gripL).sqrMagnitude < 1e-6f) return;

            // 定尺：刃长归一到角色体型（≈1.35m）
            float charScale = Mathf.Max(0.4f, visualRoot.lossyScale.y);
            float worldLen = (w.TransformPoint(tipL) - w.TransformPoint(gripL)).magnitude;
            if (worldLen > 0.001f) w.localScale *= 1.35f * charScale / worldLen;

            // 朝向：把刃轴转到手部握持方向
            Vector3 bladeW = HandBladeDir(hand);
            Vector3 curBladeW = (w.TransformPoint(tipL) - w.TransformPoint(gripL)).normalized;
            if (curBladeW.sqrMagnitude > 0.5f)
                w.rotation = Quaternion.FromToRotation(curBladeW, bladeW) * w.rotation;

            // 柄端放进手心（沿刃向前移一点，手握在柄中段）
            Vector3 gripTargetW = hand.position + bladeW * (0.1f * charScale);
            w.position += gripTargetW - w.TransformPoint(gripL);
        }

        /// <summary>由手部骨骼几何推刃轴（拳眼方向）：拇指方向去掉手指分量，
        /// 取指向前臂延长线的一侧（自然持剑刃朝外、不指向自己）。</summary>
        static Vector3 HandBladeDir(Transform hand)
        {
            Transform finger = FindDeep(hand, "middle");
            if (finger == null) finger = FindDeep(hand, "index");
            Transform thumb = FindDeep(hand, "thumb");
            Vector3 f = finger != null ? finger.position - hand.position : hand.forward;
            if (f.sqrMagnitude < 1e-8f) f = hand.forward;
            f.Normalize();
            Vector3 t = thumb != null ? (thumb.position - hand.position).normalized : hand.up;
            Vector3 blade = t - f * Vector3.Dot(t, f);
            if (blade.sqrMagnitude < 1e-6f) blade = Vector3.Cross(f, hand.right);
            blade.Normalize();
            Vector3 arm = hand.parent != null
                ? (hand.position - hand.parent.position).normalized : Vector3.down;
            if (Vector3.Dot(blade, arm) < 0f) blade = -blade;
            return blade;
        }

        static Transform FindDeep(Transform root, string key)
        {
            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
            {
                if (tr == root) continue;
                if (tr.name.ToLowerInvariant().Contains(key)) return tr;
            }
            return null;
        }

        /// <summary>武器自身坐标系下的合并包围盒（用网格局部 bounds 变换累计，
        /// 与当前姿势/世界朝向无关，结果确定可复现）。</summary>
        static bool LocalBounds(Transform w, out Bounds b)
        {
            b = default;
            bool has = false;
            foreach (var mf in w.GetComponentsInChildren<MeshFilter>(true))
                AccumMesh(w, mf.transform, mf.sharedMesh, ref b, ref has);
            foreach (var smr in w.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                AccumMesh(w, smr.transform, smr.sharedMesh, ref b, ref has);
            return has;
        }

        static void AccumMesh(Transform root, Transform t, Mesh mesh, ref Bounds b, ref bool has)
        {
            if (mesh == null) return;
            Bounds mb = mesh.bounds;
            var m = root.worldToLocalMatrix * t.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3(
                    (i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = m.MultiplyPoint3x4(c);
                if (!has) { b = new Bounds(p, Vector3.zero); has = true; }
                else b.Encapsulate(p);
            }
        }
    }
}
