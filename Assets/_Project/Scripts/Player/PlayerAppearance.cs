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

        /// <summary>当前面具名（"" = 不戴面具）。</summary>
        public string CurrentMask { get; private set; } = "";

        const string PrefKey = "player_preset";
        const string WeaponPref = "player_weapon";
        const string MaskPref = "player_mask";
        const string EquippedName = "EquippedWeapon";
        const string EquippedMaskName = "EquippedMask";

        public static readonly string[] PresetNames = { "角色·壹（青岚）", "角色·贰" };

        public void Init()
        {
            Preset = PlayerPrefs.GetInt(PrefKey, 0);
            CurrentWeapon = PlayerPrefs.GetString(WeaponPref, "");
            CurrentMask = PlayerPrefs.GetString(MaskPref, "");
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

        /// <summary>面具库清单（Resources/Characters/Masks/ 下全部模型名）。</summary>
        public static string[] ListMasks()
        {
            var prefabs = Resources.LoadAll<GameObject>("Characters/Masks");
            var names = new List<string>();
            foreach (var p in prefabs)
                if (p != null && !names.Contains(p.name)) names.Add(p.name);
            names.Sort();
            return names.ToArray();
        }

        /// <summary>戴上/摘下面具（null/"" = 摘下），重选即替换，持久化。</summary>
        public void EquipMask(string maskName)
        {
            CurrentMask = maskName ?? "";
            PlayerPrefs.SetString(MaskPref, CurrentMask);
            PlayerPrefs.Save();
            ApplyMask();
            Core.GameEvents.RaiseSubtitle(string.IsNullOrEmpty(CurrentMask)
                ? "已摘下面具" : "已戴上面具：" + CurrentMask);
        }

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

            // 优先动捕模型（角色资产分离：各自模型 + 各自动作库）；缺失自动回退。
            // 角色贰装配失败（模型缺失/导入异常/骨架异常）→ 回退角色壹模型，
            // 两个模型都不可用才落到程序化方块骨骼。不染色：保持模型原本材质/肤色。
            Rig = null;
            string modelName = Preset == 1 ? "PlayerModel2" : "PlayerModel";
            string animsFolder = Preset == 1 ? "Characters/Anims2" : null;
            bool built = poser != null && MecanimCharacter.TryBuild(visualRoot, poser, true,
                baseMaterial, WeaponKind.Sword, -1f, modelName, animsFolder);
            if (!built && Preset == 1 && poser != null)
            {
                built = MecanimCharacter.TryBuild(visualRoot, poser, true,
                    baseMaterial, WeaponKind.Sword);
                if (built) Core.GameEvents.RaiseSubtitle("角色·贰模型不可用，已回退角色·壹");
            }
            if (built)
            {
                ApplyWeapon();
                ApplyMask();
                DisableSelfShadow();
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

            // 手指握拳叠加：外装武器需要手指真实攥住剑柄（模型自带兵器的
            // WProp 动画已含握姿，无需叠加）——先移除上一次的握拳组件
            if (hand != null)
            {
                var oldGrip = hand.GetComponent<FingerGrip>();
                if (oldGrip != null) Destroy(oldGrip);
            }

            if (useCustom)
            {
                var w = Object.Instantiate(prefab, hand, false);
                w.name = EquippedName;
                w.transform.localPosition = Vector3.zero;
                w.transform.localRotation = Quaternion.identity;
                FitAndGripWeapon(w.transform, hand, out Vector3 bladeLocal, out Vector3 gripW);
                hand.gameObject.AddComponent<FingerGrip>().Setup(hand, bladeLocal, gripW);
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
                FitAndGripWeapon(holder, hand, out Vector3 bladeLocal, out Vector3 gripW);
                hand.gameObject.AddComponent<FingerGrip>().Setup(hand, bladeLocal, gripW);
                if (poser != null && wr != null)
                {
                    poser.weaponPivot = wr.pivot;
                    poser.weaponTrail = wr.trail;
                }
            }
            DisableSelfShadow();
        }

        /// <summary>戴面具：自动定尺（面具宽≈头宽）、法向对齐面部朝向、贴脸就位，
        /// 挂在头骨下随头部转动。Front 子节点可显式指定面具正面方向。</summary>
        void ApplyMask()
        {
            if (visualRoot == null || visualRoot.childCount == 0) return;
            var model = visualRoot.GetChild(0);
            var head = MecanimCharacter.FindBone(model, "head");
            if (head == null) return;

            for (int i = head.childCount - 1; i >= 0; i--)
                if (head.GetChild(i).name == EquippedMaskName)
                    Destroy(head.GetChild(i).gameObject);
            if (string.IsNullOrEmpty(CurrentMask)) return;

            GameObject prefab = null;
            foreach (var p in Resources.LoadAll<GameObject>("Characters/Masks"))
                if (p != null && p.name == CurrentMask) { prefab = p; break; }
            if (prefab == null) return;

            var mk = Object.Instantiate(prefab, head, false);
            mk.name = EquippedMaskName;
            FitMask(mk.transform, head);
            DisableSelfShadow();
        }

        /// <summary>面具贴脸定位：最薄轴=面法向（对齐角色前方），最大轴=面具纵向
        /// （对齐世界上方），宽度归一到头宽（≈身高 10.5%），中心放在脸面位置。</summary>
        void FitMask(Transform mk, Transform head)
        {
            if (!LocalBounds(mk, out Bounds lb)) return;
            float height = MecanimCharacter.TargetHeight * Mathf.Max(0.4f, visualRoot.lossyScale.y);

            // 三轴按尺寸排序：最薄=法向(厚度)，中间=宽，最大=纵向
            Vector3 sz = lb.size;
            int thin = 0, big = 0;
            for (int i = 1; i < 3; i++)
            {
                if (sz[i] < sz[thin]) thin = i;
                if (sz[i] > sz[big]) big = i;
            }
            int mid = 3 - thin - big;
            if (thin == big) { thin = 0; big = 1; mid = 2; }
            System.Func<int, Vector3> axisOf = i => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
            Vector3 nLocal = axisOf(thin);
            Vector3 hLocal = axisOf(big);

            // Front 子节点显式指定正面方向（面具戴反时的逃生舱）
            var front = FindDeep(mk, "front");
            if (front != null)
            {
                Vector3 f = mk.InverseTransformDirection(
                    (front.position - mk.TransformPoint(lb.center)).normalized);
                if (f.sqrMagnitude > 0.01f) nLocal = f.normalized;
            }

            // 定尺：面具宽（中间轴）≈ 头宽（≈身高 10.5%）
            float curW = (mk.TransformPoint(lb.center + axisOf(mid) * sz[mid] * 0.5f)
                - mk.TransformPoint(lb.center - axisOf(mid) * sz[mid] * 0.5f)).magnitude;
            if (curW > 1e-4f) mk.localScale *= (height * 0.105f) / curW;

            // 朝向：法向→角色前方，纵向→世界上方
            Vector3 fwd = visualRoot.forward;
            Vector3 nW = mk.TransformDirection(nLocal).normalized;
            Vector3 hW = mk.TransformDirection(hLocal).normalized;
            if (nW.sqrMagnitude > 0.5f && hW.sqrMagnitude > 0.5f)
                mk.rotation = Quaternion.LookRotation(fwd, Vector3.up)
                    * Quaternion.Inverse(Quaternion.LookRotation(nW, hW)) * mk.rotation;

            // 位置：面具中心贴在脸面（头骨前方一点、略微上移）
            Vector3 target = head.position + fwd * (height * 0.045f) + Vector3.up * (height * 0.008f);
            mk.position += target - mk.TransformPoint(lb.center);
        }

        /// <summary>角色不接收阴影：主光阴影不再盖住脸（清晰度优先于氛围）。</summary>
        void DisableSelfShadow()
        {
            if (visualRoot == null) return;
            foreach (var r in visualRoot.GetComponentsInChildren<Renderer>(true))
                r.receiveShadows = false;
        }

        /// <summary>参考握持姿态（取自角色壹自带巨剑——它在手里的姿态是已验证正确的）：
        /// 记录三个【跨骨架单位安全】的量——刃轴在手骨局部的单位方向（方向不受
        /// 缩放影响）、刃长相对参考身高的比例、柄端沿刃向相对手心的偏移比例。
        /// 任何武器在任何角色手里都按世界空间复刻，不再出现"局部单位不同导致
        /// 尺度爆炸→武器消失/漂浮"的问题。</summary>
        struct GripRef
        {
            public bool valid;
            public Vector3 bladeDirHandLocal;   // 刃轴单位方向（手骨局部，缩放无关）
            public float lenRel;                // 刃长 / 参考身高
            public float gripAlongRel;          // 柄端沿刃向偏移 / 参考身高
        }

        static GripRef _gripRef;
        static bool _gripRefTried;

        static GripRef GetGripRef()
        {
            if (_gripRefTried) return _gripRef;
            _gripRefTried = true;
            var refPrefab = Resources.Load<GameObject>("Characters/PlayerModel");
            if (refPrefab == null) return _gripRef;
            var hand = MecanimCharacter.FindBone(refPrefab.transform, "righthand");
            var weapon = MecanimCharacter.FindWeaponInModel(refPrefab.transform);
            if (hand == null || weapon == null) return _gripRef;
            // 参考剑必须挂在手骨之下（相对关系恒定）；蒙皮挂根上的相对关系随姿势变，不可作参考
            if (!weapon.IsChildOf(hand)) return _gripRef;
            if (!LocalBounds(weapon, out Bounds lb)) return _gripRef;
            LongAxisEnds(lb, out Vector3 endA, out Vector3 endB);
            // 世界空间（prefab 姿态）计算，再归一到身高比例——单位安全
            Vector3 aW = weapon.TransformPoint(endA);
            Vector3 bW = weapon.TransformPoint(endB);
            bool aNear = (aW - hand.position).sqrMagnitude <= (bW - hand.position).sqrMagnitude;
            Vector3 gripW = aNear ? aW : bW;
            Vector3 tipW = aNear ? bW : aW;
            Vector3 bladeW = tipW - gripW;
            if (bladeW.sqrMagnitude < 1e-8f) return _gripRef;
            float refHeight = MecanimCharacter.ModelHeight(refPrefab.transform);
            if (refHeight < 0.01f) return _gripRef;
            Vector3 bladeDirW = bladeW.normalized;
            _gripRef.valid = true;
            _gripRef.bladeDirHandLocal = hand.InverseTransformDirection(bladeDirW).normalized;
            _gripRef.lenRel = bladeW.magnitude / refHeight;
            _gripRef.gripAlongRel = Vector3.Dot(gripW - hand.position, bladeDirW) / refHeight;
            return _gripRef;
        }

        static void LongAxisEnds(Bounds lb, out Vector3 endA, out Vector3 endB)
        {
            int axis = 0;
            if (lb.size.y >= lb.size.x && lb.size.y >= lb.size.z) axis = 1;
            else if (lb.size.z >= lb.size.x && lb.size.z >= lb.size.y) axis = 2;
            Vector3 axisDir = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
            float ext = axis == 0 ? lb.extents.x : axis == 1 ? lb.extents.y : lb.extents.z;
            endA = lb.center - axisDir * ext;
            endB = lb.center + axisDir * ext;
        }

        /// <summary>武器定尺 + 握持对齐（修复"漂浮/握在刀刃上/柄不在掌心"）：
        /// ① 在武器自身坐标系求包围盒，最长轴=刃轴；柄端判定三级——
        ///    Grip/Handle 子节点 > 网格截面分析（剑的护手是最宽截面、靠近柄端；
        ///    斧/锤类最宽在头部按名称翻转）> 离武器原点近的一端；
        /// ② 全部在【世界空间】复刻参考巨剑姿态（刃长/柄位按身高比例换算）——
        ///    跨骨架单位安全，任何角色手里都不会尺度爆炸或消失。</summary>
        void FitAndGripWeapon(Transform w, Transform hand,
            out Vector3 bladeDirHandLocal, out Vector3 gripWorld)
        {
            bladeDirHandLocal = Vector3.up;
            gripWorld = hand.position;
            if (!LocalBounds(w, out Bounds lb)) return;
            LongAxisEnds(lb, out Vector3 endA, out Vector3 endB);

            // 柄端判定：① Grip/Handle 子节点 ② 网格截面分析 ③ pivot 就近
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
            else if (GripEndByProfile(w, lb, out bool gripAtA, endA, endB))
            {
                gripL = gripAtA ? endA : endB;
                tipL = gripAtA ? endB : endA;
            }
            else if (endA.sqrMagnitude <= endB.sqrMagnitude) { gripL = endA; tipL = endB; }
            else { gripL = endB; tipL = endA; }
            Vector3 bladeL = tipL - gripL;
            if (bladeL.sqrMagnitude < 1e-8f) return;

            float height = MecanimCharacter.TargetHeight * Mathf.Max(0.4f, visualRoot.lossyScale.y);
            var gr = GetGripRef();
            float targetLen = (gr.valid ? gr.lenRel : 0.5f) * height;
            float gripAlong = (gr.valid ? gr.gripAlongRel : 0.02f) * height;
            Vector3 bladeDirW = gr.valid
                ? hand.TransformDirection(gr.bladeDirHandLocal).normalized
                : hand.up;
            if (bladeDirW.sqrMagnitude < 0.5f) bladeDirW = hand.up;

            // 掌心锚点：手骨位置在手腕处，柄要放在【掌心】——向中指根方向前移一段
            Vector3 palm = hand.position;
            var middle = FindDeep(hand, "middle");
            if (middle != null) palm = Vector3.Lerp(hand.position, middle.position, 0.45f);

            // 定尺（世界空间长度→目标长度，单位安全）
            float curLenW = (w.TransformPoint(tipL) - w.TransformPoint(gripL)).magnitude;
            if (curLenW > 1e-4f) w.localScale *= targetLen / curLenW;
            // 朝向（世界空间旋转到参考刃轴方向）
            Vector3 curBladeW = (w.TransformPoint(tipL) - w.TransformPoint(gripL));
            if (curBladeW.sqrMagnitude > 1e-8f)
                w.rotation = Quaternion.FromToRotation(curBladeW.normalized, bladeDirW) * w.rotation;
            // 柄端放进掌心（沿刃向复刻参考偏移——手指正好握在剑柄合适位置）
            Vector3 gripTargetW = palm + bladeDirW * gripAlong;
            w.position += gripTargetW - w.TransformPoint(gripL);

            // 输出给手指握拳叠加：柄轴（手骨局部）与掌心柄点（手指绕它卷曲合拢）
            bladeDirHandLocal = hand.InverseTransformDirection(bladeDirW).normalized;
            gripWorld = palm + bladeDirW * (gripAlong + targetLen * 0.06f);
        }

        /// <summary>网格截面分析判柄端：沿刃轴切 12 片，统计每片的最大截面半径——
        /// 剑的护手(crossguard)是最宽截面且靠近柄端；斧/锤/镰类最宽在头部（尖端），
        /// 按名称翻转。需要网格可读（Weapons 目录的 FBX 已由导入器开启 Read/Write；
        /// 不可读时返回 false 走 pivot 就近规则）。</summary>
        static bool GripEndByProfile(Transform w, Bounds lb, out bool gripAtA,
            Vector3 endA, Vector3 endB)
        {
            gripAtA = true;
            try
            {
                Vector3 axis = (endB - endA);
                float axisLen = axis.magnitude;
                if (axisLen < 1e-4f) return false;
                axis /= axisLen;
                const int slices = 12;
                var radial = new float[slices];
                int total = 0;
                foreach (var mf in w.GetComponentsInChildren<MeshFilter>(true))
                    total += AccumProfile(w, mf.transform, mf.sharedMesh, endA, axis, axisLen, radial);
                foreach (var smr in w.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    total += AccumProfile(w, smr.transform, smr.sharedMesh, endA, axis, axisLen, radial);
                if (total < 24) return false;
                int widest = 0;
                for (int i = 1; i < slices; i++)
                    if (radial[i] > radial[widest]) widest = i;
                // 最宽截面（护手）靠近哪端，哪端就是柄端
                gripAtA = widest < slices / 2;
                // 斧/锤/镰：最宽处是头部=尖端，翻转
                string n = w.name.ToLowerInvariant();
                foreach (var t in w.GetComponentsInChildren<Transform>(true))
                    n += " " + t.name.ToLowerInvariant();
                if (n.Contains("axe") || n.Contains("hammer") || n.Contains("mace") ||
                    n.Contains("scythe") || n.Contains("斧") || n.Contains("锤"))
                    gripAtA = !gripAtA;
                return true;
            }
            catch
            {
                return false;   // 网格不可读等异常：退回 pivot 就近规则
            }
        }

        static int AccumProfile(Transform root, Transform t, Mesh mesh,
            Vector3 endA, Vector3 axis, float axisLen, float[] radial)
        {
            if (mesh == null || !mesh.isReadable) return 0;
            var verts = mesh.vertices;
            var m = root.worldToLocalMatrix * t.localToWorldMatrix;
            int slices = radial.Length;
            foreach (var v in verts)
            {
                Vector3 p = m.MultiplyPoint3x4(v);
                float along = Vector3.Dot(p - endA, axis);
                int si = Mathf.Clamp(Mathf.FloorToInt(along / axisLen * slices), 0, slices - 1);
                Vector3 onAxis = endA + axis * along;
                float r = (p - onAxis).magnitude;
                if (r > radial[si]) radial[si] = r;
            }
            return verts.Length;
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
