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
        /// 兵器 → 实例化新武器并做「定尺 + 握持对齐 + 手指握拳」→ 刀光轴跟随。
        /// CurrentWeapon 为空 = 「默认（自带武器）」：显示模型自带兵器（不隐藏），
        /// 不再生成程序化长剑；模型无自带兵器则空手（由武器库另选）。</summary>
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
                // 默认（自带武器）：模型原生兵器，不隐藏、不叠加程序化剑
                if (poser != null) poser.weaponPivot = builtin;
            }
            else if (hand != null)
            {
                // 默认，但模型【没有自带兵器】（如替换后的角色模型）：给一把程序化长剑
                // 兜底——保证"默认"手里始终有剑（修"角色1选默认剑却消失"）
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
        /// （对齐世界上方），宽度归一到头宽；脸面锚点【优先用眼骨】（跨模型稳定，
        /// 不随头骨原点高低漂移），无眼骨才按头骨+自适应偏移估计。定尺带上下限钳制，
        /// 避免异常模型（面具建模单位过小）被放大到吞掉镜头=满屏白。</summary>
        void FitMask(Transform mk, Transform head)
        {
            if (!LocalBounds(mk, out Bounds lb)) return;
            Vector3 sz = lb.size;
            if (sz.x <= 1e-6f || sz.y <= 1e-6f || sz.z <= 1e-6f ||
                float.IsNaN(sz.x) || float.IsNaN(sz.y) || float.IsNaN(sz.z))
            { Destroy(mk.gameObject); return; }

            float height = MecanimCharacter.TargetHeight * Mathf.Max(0.4f, visualRoot.lossyScale.y);
            Vector3 fwd = visualRoot.forward;
            float headW = height * 0.11f;                       // 目标面具宽≈头宽

            // 三轴按尺寸分：最薄=法向(厚度)、最大=纵向、中间=宽
            int thin = 0, big = 0;
            for (int i = 1; i < 3; i++)
            {
                if (sz[i] < sz[thin]) thin = i;
                if (sz[i] > sz[big]) big = i;
            }
            int mid = 3 - thin - big;
            if (thin == big) { thin = 0; big = 1; mid = 2; }
            System.Func<int, Vector3> axisOf = i => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

            // 符号自动判定：面具此刻以 identity 局部姿态挂在头骨上（继承头骨世界朝向
            // ≈面向前方、头顶朝上），取与"前方/上方"贴合的一侧为正面/正上
            Vector3 nLocal = axisOf(thin);
            if (Vector3.Dot(mk.TransformDirection(nLocal), fwd) < 0f) nLocal = -nLocal;
            Vector3 hLocal = axisOf(big);
            if (Vector3.Dot(mk.TransformDirection(hLocal), Vector3.up) < 0f) hLocal = -hLocal;

            // Front / Top 子节点可显式覆盖（面具仍戴反时的逃生舱）
            var front = FindDeep(mk, "front");
            if (front != null)
            {
                Vector3 f = mk.InverseTransformDirection((front.position - mk.TransformPoint(lb.center)).normalized);
                if (f.sqrMagnitude > 0.01f) nLocal = f.normalized;
            }
            var top = FindDeep(mk, "top");
            if (top != null)
            {
                Vector3 u = mk.InverseTransformDirection((top.position - mk.TransformPoint(lb.center)).normalized);
                if (u.sqrMagnitude > 0.01f) hLocal = u.normalized;
            }

            // 定尺：面具宽（中间轴）≈ 头宽，倍率钳制到 [0.05, 40]（防异常单位放大爆屏）
            float curW = (mk.TransformPoint(lb.center + axisOf(mid) * sz[mid] * 0.5f)
                - mk.TransformPoint(lb.center - axisOf(mid) * sz[mid] * 0.5f)).magnitude;
            if (curW > 1e-5f)
                mk.localScale *= Mathf.Clamp(headW / curW, 0.05f, 40f);

            // 朝向：法向→角色前方，纵向→世界上方
            Vector3 nW = mk.TransformDirection(nLocal).normalized;
            Vector3 hW = mk.TransformDirection(hLocal).normalized;
            if (nW.sqrMagnitude > 0.5f && hW.sqrMagnitude > 0.5f)
                mk.rotation = Quaternion.LookRotation(fwd, Vector3.up)
                    * Quaternion.Inverse(Quaternion.LookRotation(nW, hW)) * mk.rotation;

            // 脸面锚点：优先眼骨中点（各模型一致地落在眼睛上，不随头骨原点高低漂移）
            var eyeL = FindEye(head, "lefteye", "eyel", "eye_l");
            var eyeR = FindEye(head, "righteye", "eyer", "eye_r");
            Vector3 target;
            if (eyeL != null && eyeR != null)
                target = (eyeL.position + eyeR.position) * 0.5f + fwd * (headW * 0.22f);
            else if (eyeL != null || eyeR != null)
                target = (eyeL != null ? eyeL : eyeR).position + fwd * (headW * 0.22f);
            else
                // 无眼骨：头骨原点上方约半个头宽（眼高）、前方约 0.4 头宽（贴脸不悬空）
                target = head.position + fwd * (headW * 0.4f) + Vector3.up * (headW * 0.5f);
            mk.position += target - mk.TransformPoint(lb.center);
        }

        /// <summary>在头骨下找眼睛骨（多种命名）。规范化去符号后包含匹配。</summary>
        static Transform FindEye(Transform head, params string[] keys)
        {
            foreach (var t in head.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant().Replace(":", "").Replace("_", "").Replace(" ", "");
                foreach (var k in keys)
                    if (n.Contains(k.Replace("_", ""))) return t;
            }
            return null;
        }

        /// <summary>角色不接收阴影：主光阴影不再盖住脸（清晰度优先于氛围）。</summary>
        void DisableSelfShadow()
        {
            if (visualRoot == null) return;
            foreach (var r in visualRoot.GetComponentsInChildren<Renderer>(true))
                r.receiveShadows = false;
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

        /// <summary>武器定尺 + 握持对齐（几何法·根治"漂浮/握在刀刃上/柄不在掌心"）：
        /// 不再依赖"参考巨剑必须被检测到"（那是之前武器悬空的根因——Maria 自带巨剑
        /// 网格名不含 sword 关键词，检测失败就退化成 hand.up 方向导致长剑戳在身前）。
        /// 改为完全从【手部手指骨骼几何】推握持坐标系（与骨骼 rest 朝向无关，跨骨架通用）：
        ///   · 柄轴 = 掌横向（食指根→小指根方向）——握拳时刀柄正是横穿蜷曲的四指；
        ///   · 掌心 = 手腕骨到中指根之间的点；
        ///   · 刀刃从掌心沿柄轴伸出（偏向拇指/食指侧=向上出拳眼）。
        /// 武器最长轴对齐柄轴、握持段（柄端往刃向一小截=四指环握处）落在掌心。</summary>
        void FitAndGripWeapon(Transform w, Transform hand,
            out Vector3 bladeDirHandLocal, out Vector3 gripWorld)
        {
            bladeDirHandLocal = Vector3.up;
            gripWorld = hand.position;
            if (!LocalBounds(w, out Bounds lb)) return;
            LongAxisEnds(lb, out Vector3 endA, out Vector3 endB);

            // 柄端判定：① Grip/Handle 子节点 ② 网格截面分析（护手最宽=柄端） ③ pivot 就近
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
            if ((tipL - gripL).sqrMagnitude < 1e-8f) return;

            // ---- 从手指骨骼几何推握持坐标系（rest 朝向无关，任何角色都对）----
            HandGripFrame(hand, out Vector3 palm, out Vector3 bladeDirW, out float handWidth);

            float height = MecanimCharacter.TargetHeight * Mathf.Max(0.4f, visualRoot.lossyScale.y);
            float targetLen = 0.62f * height;                 // 刀刃总长≈角色身高 0.62
            // 握持段：柄端沿刃向进入拳心——手往刀刃方向靠（不再贴在剑柄末端/护手根）：
            // 从柄端起 ≈1.6 个拳宽处握持，手落在剑柄中上段
            float gripInset = Mathf.Max(handWidth * 1.6f, targetLen * 0.14f);

            // 定尺（世界空间长度→目标长度，单位安全）
            float curLenW = (w.TransformPoint(tipL) - w.TransformPoint(gripL)).magnitude;
            if (curLenW > 1e-4f) w.localScale *= targetLen / curLenW;
            // 朝向：武器长轴（柄→尖）对齐柄轴（世界空间）
            Vector3 curBladeW = w.TransformPoint(tipL) - w.TransformPoint(gripL);
            if (curBladeW.sqrMagnitude > 1e-8f)
                w.rotation = Quaternion.FromToRotation(curBladeW.normalized, bladeDirW) * w.rotation;
            // 平移：握持段（柄端沿刃向 gripInset 处）落在掌心
            Vector3 gripHoldW = w.TransformPoint(gripL) + bladeDirW * gripInset;
            w.position += palm - gripHoldW;

            // 输出给手指握拳叠加：柄轴（手骨局部）与掌心（四指绕它卷曲合拢）
            bladeDirHandLocal = hand.InverseTransformDirection(bladeDirW).normalized;
            gripWorld = palm;
        }

        /// <summary>从手部手指骨骼几何推握持坐标系（与骨骼 rest 朝向无关）：
        /// palm=掌心世界点、bladeDir=刀柄/刀刃世界轴（横穿四指、偏拇指侧向上）、
        /// handWidth=拳宽。找不到手指时退回手骨轴的合理估计。</summary>
        static void HandGripFrame(Transform hand, out Vector3 palm, out Vector3 bladeDir, out float handWidth)
        {
            Transform idx = FingerBase(hand, "index");
            Transform pky = FingerBase(hand, "pinky");
            Transform mid = FingerBase(hand, "middle");
            Transform thb = FingerBase(hand, "thumb");

            Vector3 wrist = hand.position;
            Vector3 midP = mid != null ? mid.position : wrist + hand.forward * 0.1f;
            palm = Vector3.Lerp(wrist, midP, 0.55f);          // 掌心（略偏向指根）

            // 柄轴 = 横穿手掌（小指根→食指根）——握拳时刀柄正穿过蜷曲四指
            if (idx != null && pky != null)
            {
                bladeDir = (idx.position - pky.position).normalized;
                handWidth = (idx.position - pky.position).magnitude;
            }
            else
            {
                bladeDir = hand.right;   // 退化估计
                handWidth = 0.1f;
            }
            if (handWidth < 1e-3f) handWidth = 0.1f;

            // 刀刃应向上出拳眼（拇指侧）：让柄轴指向拇指一侧、并带正的世界 Y 分量
            Vector3 fingersDir = (midP - wrist).normalized;
            if (thb != null)
            {
                Vector3 toThumb = (thb.position - palm);
                if (Vector3.Dot(bladeDir, toThumb) < 0f) bladeDir = -bladeDir;
            }
            // 去掉沿手指方向的分量（刀柄与手指垂直），再保证略微朝上
            bladeDir = (bladeDir - fingersDir * Vector3.Dot(bladeDir, fingersDir)).normalized;
            if (bladeDir.sqrMagnitude < 0.25f) bladeDir = Vector3.up;
            if (Vector3.Dot(bladeDir, Vector3.up) < 0f && thb == null) bladeDir = -bladeDir;
        }

        /// <summary>手指近节骨（RightHandIndex1 等）：按关键词取层级最浅的匹配。</summary>
        static Transform FingerBase(Transform hand, string key)
        {
            Transform best = null; int bestDepth = int.MaxValue;
            foreach (var t in hand.GetComponentsInChildren<Transform>(true))
            {
                if (t == hand) continue;
                if (!t.name.ToLowerInvariant().Contains(key)) continue;
                int d = 0; var p = t; while (p != null && p != hand) { d++; p = p.parent; }
                if (d < bestDepth) { bestDepth = d; best = t; }
            }
            return best;
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
