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

        WeaponSheath _sheath;   // 带鞘武器的拔刀/收刀控制器（普通武器为 null）
        Transform _drawnPivot;  // 拔刀后右手掌心枢轴（收刀/换武器时销毁）
        string _sheathDiag = "当前武器不带剑鞘";   // 剑鞘装配诊断（按拔刀键随时可查看）

        /// <summary>当前是否装备了带剑鞘的成套武器（UI 决定是否显示「拔刀/收刀」按钮）。</summary>
        public bool HasSheathWeapon => _sheath != null;

        /// <summary>手动拔刀/收刀（在两状态间切换）：拔刀=剑身抽到右手正确握位、剑鞘留左手；
        /// 收刀=剑身插回鞘。同时播放对应的拔刀/收刀动画（Draw / Sheathing Sword）。
        /// 仅对带剑鞘的成套武器生效。</summary>
        public void ToggleWeaponDrawn()
        {
            if (_sheath == null)
            {
                // 无剑鞘时按键 = 查看装配诊断（开机装配时字幕系统可能尚未就绪，
                // 这里保证诊断随时可以调出来）
                Core.GameEvents.RaiseSubtitle(_sheathDiag);
                return;
            }
            bool willDraw = !_sheath.IsDrawn;
            string key = willDraw ? "draw" : "sheath";
            // 过渡时长与拔刀/收刀动画同步：取动作库对应片段时长，无片段则兜底
            float dur = poser != null ? poser.ClipLengthContaining(key) : 0f;
            if (dur <= 0.05f) dur = 0.7f;
            if (poser != null) poser.PlayClipContaining(key);   // 拔刀/收刀动画
            _sheath.Toggle(dur);
        }

        /// <summary>当前武器名（"" = 默认佩剑：模型自带兵器或 Characters/Weapon）。</summary>
        public string CurrentWeapon { get; private set; } = "";

        /// <summary>当前面具名（"" = 不戴面具）。</summary>
        public string CurrentMask { get; private set; } = "";

        /// <summary>当前背包名（"" = 不背背包）。</summary>
        public string CurrentBackpack { get; private set; } = "";

        const string PrefKey = "player_preset";
        const string WeaponPref = "player_weapon";
        const string MaskPref = "player_mask";
        const string BackpackPref = "player_backpack";
        const string EquippedName = "EquippedWeapon";
        const string EquippedMaskName = "EquippedMask";
        const string EquippedBackpackName = "EquippedBackpack";

        public static readonly string[] PresetNames = { "角色·壹（青岚）", "角色·贰" };

        public void Init()
        {
            Preset = PlayerPrefs.GetInt(PrefKey, 0);
            CurrentWeapon = PlayerPrefs.GetString(WeaponPref, "");
            CurrentMask = PlayerPrefs.GetString(MaskPref, "");
            CurrentBackpack = PlayerPrefs.GetString(BackpackPref, "");
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

        /// <summary>背包库清单（Resources/Characters/Backpacks/ 下全部模型名）。</summary>
        public static string[] ListBackpacks()
        {
            var prefabs = Resources.LoadAll<GameObject>("Characters/Backpacks");
            var names = new List<string>();
            foreach (var p in prefabs)
                if (p != null && !names.Contains(p.name)) names.Add(p.name);
            names.Sort();
            return names.ToArray();
        }

        /// <summary>背上/卸下背包（null/"" = 卸下），重选即替换，持久化。</summary>
        public void EquipBackpack(string backpackName)
        {
            CurrentBackpack = backpackName ?? "";
            PlayerPrefs.SetString(BackpackPref, CurrentBackpack);
            PlayerPrefs.Save();
            ApplyBackpack();
            Core.GameEvents.RaiseSubtitle(string.IsNullOrEmpty(CurrentBackpack)
                ? "已卸下背包" : "已背上：" + CurrentBackpack);
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
                ApplyBackpack();
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
            var lhand = MecanimCharacter.FindBone(model, "lefthand");

            // 卸下上一件外装武器（含收刀时挂在左手的成套剑鞘、掌心枢轴）与拔刀控制器
            if (_sheath != null) { Destroy(_sheath); _sheath = null; }
            _drawnPivot = null;
            _sheathDiag = "当前武器不带剑鞘";
            foreach (var h in new[] { hand, lhand })
            {
                if (h == null) continue;
                for (int i = h.childCount - 1; i >= 0; i--)
                    if (h.GetChild(i).name.StartsWith(EquippedName))
                        Destroy(h.GetChild(i).gameObject);
                var og = h.GetComponent<FingerGrip>();
                if (og != null) Destroy(og);
            }

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
                FixWeaponMaterials(w);   // 接回武器贴图（修白模）

                // 带剑鞘的成套武器（如 scene 剑）：默认收刀挂左手、按拔刀键出鞘到右手。
                // 剑鞘识别：先按名(scabbard/sheath/鞘)——但导入器可能不保留节点名
                // （实测该 gltf 的 4 个网格节点全叫 "Sword-material"，按名一律落空、
                // 装配从未运行=剑鞘始终分离的总根因）——再按【几何】识别兜底。
                var scab = FindDeep(w.transform, "scabbard") ?? FindDeep(w.transform, "sheath")
                    ?? FindDeep(w.transform, "鞘") ?? DetectScabbardByGeometry(w.transform);
                if (scab != null) AdoptScabbardAccessories(w.transform, scab);   // 挂环等小件归鞘
                var parts = scab != null ? BladeParts(w.transform, scab) : null;
                if (scab != null && parts != null && parts.Count > 0 && lhand != null)
                {
                    SetupSheathedWeapon(w.transform, parts, scab, lhand, hand);
                }
                else
                {
                    int meshN = w.GetComponentsInChildren<MeshFilter>(true).Length;
                    if (scab != null)
                        _sheathDiag = "剑鞘装配未启用：剑身网格 "
                            + (parts != null ? parts.Count.ToString() : "0")
                            + " 个 / 左手骨 " + (lhand != null ? "有" : "无");
                    else if (meshN >= 3)
                        _sheathDiag = "未识别到剑鞘：按名与按几何都未匹配（网格 " + meshN + " 件）";
                    else
                        _sheathDiag = "当前武器不带剑鞘（网格 " + meshN + " 件）";
                    Core.GameEvents.RaiseSubtitle(_sheathDiag);
                    Debug.LogWarning("[Sheath] " + _sheathDiag);
                    FitAndGripWeapon(w.transform, hand, out Vector3 bladeLocal, out Vector3 gripW);
                    var pv = WrapWeaponPivot(w.transform, hand, bladeLocal, gripW);
                    hand.gameObject.AddComponent<FingerGrip>().Setup(hand, bladeLocal, gripW);
                    if (poser != null) poser.weaponPivot = pv;
                }
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

        /// <summary>收集剑身部件：w 下所有【带网格且不属于剑鞘】的节点（去掉已被列表内
        /// 某项包含的子孙，保持最少的顶层集合）。之前按层级"回溯子树根"来猜剑身节点，
        /// 一旦导入器组织的层级与源文件不同，移动那个猜出来的节点就不会带动真正渲染的
        /// 网格（"剑永远插不进鞘"的根因之一）——现在直接抓【网格所在节点本身】。</summary>
        static List<Transform> BladeParts(Transform w, Transform scab)
        {
            var parts = new List<Transform>();
            void Consider(Transform t)
            {
                if (t == scab || t.IsChildOf(scab)) return;
                string n = t.name.ToLowerInvariant();
                if (n.Contains("scabbard") || n.Contains("sheath") || n.Contains("鞘") ||
                    n.Contains("ring") || n.Contains("环")) return;
                foreach (var p in parts)
                    if (t == p || t.IsChildOf(p)) return;
                parts.Add(t);
            }
            foreach (var mf in w.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) Consider(mf.transform);
            foreach (var smr in w.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.sharedMesh != null) Consider(smr.transform);
            return parts;
        }

        /// <summary>按几何识别剑鞘（节点名靠不住时的兜底）：取两个最长的【细长】网格，
        /// 若长度相近（0.55~1.05）且【非共线/相互分离】（带鞘武器在源文件里是摆拍分离
        /// 姿态；而同一把剑拆成刃+柄是共线相接的，不会误判），则其中【顶点更少】的是
        /// 剑鞘（素圆管），另一个是剑（护手/雕花网格更密）。识别不出返回 null。</summary>
        static Transform DetectScabbardByGeometry(Transform w)
        {
            var list = new List<(Transform t, Vector3 a, Vector3 b, float len, float aspect, int v)>();
            void Add(Transform t, Mesh m)
            {
                if (m == null) return;
                Bounds mb = m.bounds;
                int ax = mb.size.x >= mb.size.y && mb.size.x >= mb.size.z ? 0
                       : mb.size.y >= mb.size.z ? 1 : 2;
                float second = 0f;
                for (int i = 0; i < 3; i++)
                    if (i != ax) second = Mathf.Max(second, mb.size[i]);
                Vector3 eA = mb.center, eB = mb.center;
                eA[ax] = mb.min[ax]; eB[ax] = mb.max[ax];
                Vector3 wa = t.TransformPoint(eA), wb = t.TransformPoint(eB);
                float len = (wb - wa).magnitude;
                float aspect = second > 1e-6f ? mb.size[ax] / second : 999f;
                list.Add((t, wa, wb, len, aspect, m.vertexCount));
            }
            foreach (var mf in w.GetComponentsInChildren<MeshFilter>(true)) Add(mf.transform, mf.sharedMesh);
            foreach (var smr in w.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Add(smr.transform, smr.sharedMesh);
            if (list.Count < 2) return null;
            list.Sort((x, y) => y.len.CompareTo(x.len));
            var A = list[0]; var B = list[1];
            if (A.aspect < 4f || B.aspect < 4f) return null;             // 两件都得细长
            if (B.len < A.len * 0.55f || B.len > A.len * 1.05f) return null;   // 长度相近
            Vector3 dA = (A.b - A.a).normalized, dB = (B.b - B.a).normalized;
            float ang = Vector3.Angle(dA, dB); if (ang > 90f) ang = 180f - ang;
            Vector3 cA = (A.a + A.b) * 0.5f, cB = (B.a + B.b) * 0.5f;
            bool separated = ang > 12f || (cB - cA).magnitude > A.len * 0.55f;
            if (!separated) return null;                                  // 共线相接=同一把剑的部件
            return A.v <= B.v ? A.t : B.t;                                // 素圆管顶点少=鞘
        }

        /// <summary>把中心落在剑鞘包围盒（略放大）内、且长度不足鞘长 35% 的小网格
        /// （挂环等配件）挂到鞘节点下——节点名丢失时它们无法按名排除，若混进剑身组
        /// 会撑歪剑身包围盒、并在拔刀时跟着剑飞走。</summary>
        static void AdoptScabbardAccessories(Transform w, Transform scab)
        {
            if (!LocalBounds(scab, out Bounds sb)) return;
            LongAxisEnds(sb, out Vector3 s0, out Vector3 s1);
            float scabLenW = (scab.TransformPoint(s1) - scab.TransformPoint(s0)).magnitude;
            if (scabLenW < 1e-4f) return;
            Bounds grow = sb;
            grow.Expand(Mathf.Max(sb.size.x, Mathf.Max(sb.size.y, sb.size.z)) * 0.3f);
            var adopt = new List<Transform>();
            void Consider(Transform t, Mesh m)
            {
                if (m == null || t == scab || t.IsChildOf(scab)) return;
                Bounds mb = m.bounds;
                float lenW = (t.TransformPoint(mb.max) - t.TransformPoint(mb.min)).magnitude;
                if (lenW > scabLenW * 0.35f) return;                      // 只收编小件
                Vector3 cL = scab.InverseTransformPoint(t.TransformPoint(mb.center));
                if (grow.Contains(cL)) adopt.Add(t);
            }
            foreach (var mf in w.GetComponentsInChildren<MeshFilter>(true)) Consider(mf.transform, mf.sharedMesh);
            foreach (var smr in w.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Consider(smr.transform, smr.sharedMesh);
            foreach (var t in adopt) t.SetParent(scab, true);
        }

        /// <summary>装配成套带鞘武器（剑+鞘，该模型两部件在源文件里就是【摆拍分离】姿态，
        /// 必须运行时装配）：把剑身的【实际带网格节点】整体编组为 BladeGroup 挂到剑鞘
        /// 节点下（被移动的就是被渲染的网格本身，无论导入器怎么组织层级都不可能"没生效"），
        /// 在【剑鞘本地空间】解析求出"插入鞘中"的本地姿态——剑轴(柄→尖)对齐鞘轴(鞘口→鞘底)、
        /// 剑尖抵鞘底、剑柄自鞘口露出。纯本地 TRS 与骨骼缩放/世界姿态无关；此后每帧由
        /// WeaponSheath 复位，剑与鞘不可能再分离。整套挂左手横握鞘中部（鞘口朝拳眼侧）。
        /// 装配完成后做世界空间验收，结果打屏幕字幕（成功/偏差一目了然）。</summary>
        void SetupSheathedWeapon(Transform set, List<Transform> parts, Transform scab, Transform lhand, Transform rhand)
        {
            set.SetParent(lhand, false);
            set.localPosition = Vector3.zero;
            set.localRotation = Quaternion.identity;
            set.localScale = Vector3.one;

            // 0) 先量剑鞘（此刻剑身还没挂进来，鞘包围盒=鞘管+挂环，干净）
            bool sbOk = LocalBounds(scab, out Bounds sb);

            // 1) 剑身编组：网格节点保持相互姿态（成剑整体）挂入 BladeGroup（鞘节点之下）
            var blade = new GameObject("BladeGroup").transform;
            blade.SetParent(scab, false);
            blade.localPosition = Vector3.zero;
            blade.localRotation = Quaternion.identity;
            blade.localScale = Vector3.one;
            foreach (var p in parts) p.SetParent(blade, true);

            // 2) 剑身入鞘（剑鞘本地空间解析装配）
            if (!LocalBounds(blade, out Bounds bb) || !sbOk)
            {
                // 几何不可测：退化为普通武器整套右手握持
                _sheathDiag = "剑鞘装配失败：几何不可测，按普通武器持握";
                Core.GameEvents.RaiseSubtitle(_sheathDiag);
                set.SetParent(rhand, false);
                FitAndGripWeapon(set, rhand, out Vector3 bl0, out Vector3 gw0);
                rhand.gameObject.AddComponent<FingerGrip>().Setup(rhand, bl0, gw0);
                return;
            }
            LongAxisEnds(bb, out Vector3 ba0, out Vector3 ba1);
            LongAxisEnds(sb, out Vector3 sa0, out Vector3 sa1);
            bool gripAtA = DecideGripEnd(blade, bb, ba0, ba1);
            Vector3 gripL = gripAtA ? ba0 : ba1, tipL = gripAtA ? ba1 : ba0;
            Vector3 mouthL = sa1, botL = sa0;              // 约定：鞘口=长轴高端（下方持握时鞘口朝拳眼侧）
            Vector3 bDir = tipL - gripL;
            Vector3 sDir = botL - mouthL;                  // 鞘口→鞘底 = 插入方向
            float bladeLen = bDir.magnitude, scabLen = sDir.magnitude;
            if (bladeLen < 1e-4f || scabLen < 1e-4f) return;
            bDir /= bladeLen; sDir /= scabLen;

            Quaternion q = Quaternion.FromToRotation(bDir, sDir);
            // 同一模型拆出的剑与鞘原比例即正确（柄长=剑长-鞘长自然露出）；仅当比例
            // 明显异常（异源模型）才归一到"刃身入鞘、柄露鞘口"
            float s = 1f;
            float fit = scabLen * 1.12f / bladeLen;
            if (fit < 0.7f || fit > 1.45f) s = fit;
            Vector3 sLP = (botL - sDir * (scabLen * 0.02f)) - (q * tipL) * s;   // 剑尖抵鞘底(留 2% 余量)
            Quaternion sLR = q; Vector3 sLS = Vector3.one * s;
            blade.localPosition = sLP; blade.localRotation = sLR; blade.localScale = sLS;

            // 3) 定尺：整套按【剑鞘长度≈0.62 身高】等比缩放
            float targetLen = 0.62f * MecanimCharacter.TargetHeight * Mathf.Max(0.4f, visualRoot.lossyScale.y);
            float curLen = (scab.TransformPoint(sa1) - scab.TransformPoint(sa0)).magnitude;
            if (curLen > 1e-4f) set.localScale *= Mathf.Clamp(targetLen / curLen, 0.01f, 100f);

            // 4) 左手【横握剑鞘中部】：鞘轴对齐掌握持轴且鞘口朝拇指/拳眼侧（拔刀手可及），
            //    鞘中点落在掌心，手指绕鞘握拢
            HandGripFrame(lhand, out Vector3 palm, out Vector3 axisW, out float hw);
            Vector3 outW = scab.TransformPoint(mouthL) - scab.TransformPoint(botL);   // 鞘底→鞘口（世界）
            if (outW.sqrMagnitude > 1e-8f)
                set.rotation = Quaternion.FromToRotation(outW.normalized, axisW) * set.rotation;
            set.position += palm - scab.TransformPoint(sb.center);   // 鞘中点→掌心
            lhand.gameObject.AddComponent<FingerGrip>()
                .Setup(lhand, lhand.InverseTransformDirection(axisW).normalized, palm);

            // 5) 预算【拔刀】本地姿态（把剑身按握位对齐到右手一次，记录，再放回鞘中）
            blade.SetParent(rhand, false);
            FitAndGripWeapon(blade, rhand, out _, out _);
            Vector3 dLP = blade.localPosition; Quaternion dLR = blade.localRotation; Vector3 dLS = blade.localScale;
            blade.SetParent(scab, false);
            blade.localPosition = sLP; blade.localRotation = sLR; blade.localScale = sLS;

            // 6) 鞘口滑动参数：过渡时先对准鞘口、再沿鞘轴滑入/抽出（鞘本地空间）
            Vector3 mouthDir = -sDir;                 // 指向鞘口外 = 抽出方向
            float slide = bladeLen * s * 0.92f;       // 抽出距离≈剑长（柄在鞘口略留余量）

            // 7) 世界空间验收：剑身包围盒中心应落在鞘中心附近（鞘长 35% 内），
            //    结果直接打屏幕字幕——装配是否真正生效一目了然
            Vector3 bladeCtrW = blade.TransformPoint(bb.center);
            Vector3 scabCtrW = scab.TransformPoint(sb.center);
            float scabLenW = (scab.TransformPoint(sa1) - scab.TransformPoint(sa0)).magnitude;
            bool seated = scabLenW > 1e-4f && (bladeCtrW - scabCtrW).magnitude < scabLenW * 0.35f;
            _sheathDiag = seated
                ? "剑已入鞘（左手持鞘，按「拔刀」出鞘）"
                : "剑鞘装配偏差：偏离 " + ((bladeCtrW - scabCtrW).magnitude / Mathf.Max(scabLenW, 1e-4f)).ToString("F2") + " 鞘长";
            Core.GameEvents.RaiseSubtitle(_sheathDiag);

            if (_sheath != null) Destroy(_sheath);
            _sheath = visualRoot.gameObject.AddComponent<WeaponSheath>();
            _sheath.Setup(blade, scab, sLP, sLR, sLS, mouthDir, slide, rhand, dLP, dLR, dLS,
                // 拔刀到位：右手握拳 + 包掌心枢轴（耍花绕掌心挥、刀光轴跟随）
                rh =>
                {
                    FitAndGripWeapon(blade, rh, out Vector3 bl, out Vector3 gw);
                    _drawnPivot = WrapWeaponPivot(blade, rh, bl, gw);
                    var g = rh.GetComponent<FingerGrip>();
                    if (g == null) g = rh.gameObject.AddComponent<FingerGrip>();
                    g.Setup(rh, bl, gw);
                    if (poser != null) poser.weaponPivot = _drawnPivot;
                },
                // 收刀/过渡开始：撤右手握拳与掌心枢轴（先把剑身救出再销毁枢轴）
                rh =>
                {
                    var g = rh.GetComponent<FingerGrip>();
                    if (g != null) Destroy(g);
                    if (_drawnPivot != null)
                    {
                        blade.SetParent(_drawnPivot.parent, true);
                        Destroy(_drawnPivot.gameObject);
                        _drawnPivot = null;
                    }
                    if (poser != null) poser.weaponPivot = null;
                });
            if (poser != null) poser.weaponPivot = null;   // 收刀状态：耍花/刀光不驱动剑身
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

        /// <summary>背上背包：挂在上背脊骨（spine2/上胸，缺失逐级回退），自动定尺（高度
        /// ≈躯干上段）、贴合脊背并略微后移，随躯干转动。重选即替换，"" = 卸下。
        /// 支持 .glb/.gltf/.fbx（放进 Resources/Characters/Backpacks/ 即出现在背包菜单）。</summary>
        void ApplyBackpack()
        {
            if (visualRoot == null || visualRoot.childCount == 0) return;
            var model = visualRoot.GetChild(0);
            var back = MecanimCharacter.FindBone(model, "spine2")
                    ?? MecanimCharacter.FindBone(model, "upperchest")
                    ?? MecanimCharacter.FindBone(model, "spine1")
                    ?? MecanimCharacter.FindBone(model, "chest")
                    ?? MecanimCharacter.FindBone(model, "spine");
            if (back == null) return;

            for (int i = back.childCount - 1; i >= 0; i--)
                if (back.GetChild(i).name == EquippedBackpackName)
                    Destroy(back.GetChild(i).gameObject);
            if (string.IsNullOrEmpty(CurrentBackpack)) return;

            GameObject prefab = null;
            foreach (var p in Resources.LoadAll<GameObject>("Characters/Backpacks"))
                if (p != null && p.name == CurrentBackpack) { prefab = p; break; }
            if (prefab == null) return;

            var bp = Object.Instantiate(prefab, back, false);
            bp.name = EquippedBackpackName;
            FixModelMaterials(bp, ScopedTextures("Characters/Backpacks", CurrentBackpack));  // 有贴图才接线
            // 不做任何染色：保持模型本色（本背包原型即白色，按用户要求恢复本色）
            FitBackpack(bp.transform, back, model);
            DisableSelfShadow();
        }

        /// <summary>背包定尺 + 挂载跟随绑定（BackpackRig）。
        /// 只在装备瞬间摆一次姿态是"背包横倒/沉进躯干"的根因——装备时刻的骨骼姿势
        /// (尤其 glb 角色的绑定姿势)会被烘进本地变换，播放姿势一变就歪。这里改为：
        ///   · 轴分类与作者朝向无关：最薄轴=厚度(贴背方向)、最大轴=高度；
        ///   · 肩带面判定：本背包模型自带【完整的双肩带环】（离线点云验证），在 Unity
        ///     FBX 导入坐标系中位于厚轴【正】侧——映射到贴身侧，双肩正好穿进肩带环。
        ///     （此前按原始文件坐标推成负侧，肩带始终背对身体="肩带从来没套肩"的根因。
        ///     若个别模型相反，放 flippack 标记文件翻转，同 flipgrip 约定。）
        ///   · 定尺按最大边≈0.26 身高；之后交给 BackpackRig 每帧从活动骨骼摆位。</summary>
        void FitBackpack(Transform bp, Transform back, Transform model)
        {
            if (!LocalBounds(bp, out Bounds lb)) { Destroy(bp.gameObject); return; }
            Vector3 sz = lb.size;
            if (sz.x <= 1e-6f || sz.y <= 1e-6f || sz.z <= 1e-6f ||
                float.IsNaN(sz.x) || float.IsNaN(sz.y) || float.IsNaN(sz.z))
            { Destroy(bp.gameObject); return; }

            float bodyH = MecanimCharacter.TargetHeight * Mathf.Max(0.4f, visualRoot.lossyScale.y);
            Vector3 fwd = visualRoot.forward;
            Vector3 up = Vector3.up;

            // 【运行时实测】三轴与肩带面——不再对导入坐标系做任何假设（源文件坐标与
            // Unity 导入坐标手性/朝向可能不一致，靠推断已多次翻车）：
            //   把全部网格顶点变换到背包本地：最大跨度轴=高、最小=厚；
            //   肩带侧 = 厚轴两端各取 15% 外层薄片、按另两轴 12×12 栅格数占用格——
            //   肩带是细窄条带(占格少)，包体正面是整面(占格多)，占格少的一侧=肩带侧。
            //   （已用本背包真实网格离线验证：42 格 vs 84 格，判定正确。）
            // 顶点不可读时退回包围盒分类（此时肩带侧取正侧，可用 flippack 标记纠正）。
            bool measured = TryMeasureBackpack(bp, out int thin, out int big, out int strapSign);
            if (!measured)
            {
                thin = 0; big = 0;
                for (int i = 1; i < 3; i++)
                {
                    if (sz[i] < sz[thin]) thin = i;
                    if (sz[i] > sz[big]) big = i;
                }
                if (thin == big) { thin = 2; big = 1; }
                strapSign = 1;
            }
            Vector3 axisOfThin = thin == 0 ? Vector3.right : thin == 1 ? Vector3.up : Vector3.forward;
            Vector3 bigAxis = big == 0 ? Vector3.right : big == 1 ? Vector3.up : Vector3.forward;
            Vector3 strapDir = axisOfThin * strapSign;
            if (BackpackFlipMarked()) strapDir = -strapDir;
            // 修正四元数：肩带面→跟随系 +Z、高轴→+Y；
            // 跟随系每帧取 LookRotation(角色前方, 竖直)，即肩带面朝身体、鼓面朝身后
            Quaternion qFix = Quaternion.Inverse(Quaternion.LookRotation(strapDir, bigAxis));
            bp.rotation = Quaternion.LookRotation(fwd, up) * qFix;
            // 装配诊断打屏（下一张截图即可核对轴向判定是否正确）
            string axn = "XYZ";
            Core.GameEvents.RaiseSubtitle("背包装配：高轴" + axn[big] + " 厚轴" + axn[thin]
                + " 肩带朝" + (strapSign > 0 ? "+" : "-") + axn[thin]
                + (measured ? "（实测）" : "（包围盒估计）"));

            // 定尺：最大边 ≈ 0.26 身高（约 0.52m，正常背包尺度）。三轴世界跨度取最大，
            // 与模型自身朝向无关，绝不会因测到薄轴而把整包放大到吞屏。
            float target = bodyH * 0.26f;
            float maxDim = Mathf.Max(WorldExtentAlong(bp, lb, visualRoot.right),
                Mathf.Max(WorldExtentAlong(bp, lb, up), WorldExtentAlong(bp, lb, fwd)));
            if (maxDim > 1e-4f) bp.localScale *= Mathf.Clamp(target / maxDim, 0.001f, 200f);

            float packH = WorldExtentAlong(bp, lb, up);
            float packD = WorldExtentAlong(bp, lb, fwd);

            // 座位偏移：胸骨中心在躯干内部，外移【躯干半厚+0.38 包厚】——背板贴背表面、
            // 肩带环略嵌向双肩（正好"穿"在肩上）；抬升让包顶到肩线。
            // 实际摆位交给 BackpackRig 每帧执行。
            float torsoHalf = bodyH * 0.07f;
            float backOff = torsoHalf + packD * 0.38f;
            float liftOff = bodyH * 0.12f - packH * 0.5f;

            bp.gameObject.AddComponent<BackpackRig>().Setup(visualRoot,
                MecanimCharacter.FindBone(model, "hips"),
                MecanimCharacter.FindBone(model, "neck") ?? MecanimCharacter.FindBone(model, "head"),
                back, qFix, lb.center, backOff, liftOff);
        }

        /// <summary>实测背包三轴与肩带面（装备时一次性计算）：抽样子树全部网格顶点到
        /// root 本地——最大跨度轴=高、最小=厚；厚轴两端各取 15% 外层薄片，按另两轴
        /// 12×12 栅格数占用格，占格少的一侧（细窄条带=肩带）为肩带侧。
        /// 网格不可读/顶点太少返回 false。</summary>
        static bool TryMeasureBackpack(Transform root, out int thin, out int big, out int strapSign)
        {
            thin = 2; big = 1; strapSign = 1;
            try
            {
                var pts = new List<Vector3>(8192);
                void Sample(Transform t, Mesh m)
                {
                    if (m == null || !m.isReadable) return;
                    var vs = m.vertices;
                    if (vs == null || vs.Length == 0) return;
                    var mat = root.worldToLocalMatrix * t.localToWorldMatrix;
                    int stride = Mathf.Max(1, vs.Length / 6000);
                    for (int i = 0; i < vs.Length; i += stride)
                        pts.Add(mat.MultiplyPoint3x4(vs[i]));
                }
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) Sample(mf.transform, mf.sharedMesh);
                foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Sample(smr.transform, smr.sharedMesh);
                if (pts.Count < 100) return false;

                Vector3 mn = pts[0], mx = pts[0];
                foreach (var p in pts) { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
                Vector3 span = mx - mn;
                if (span.x < 1e-6f || span.y < 1e-6f || span.z < 1e-6f) return false;
                // 局部函数不能捕获 out 参数(CS1628)：先算到本地变量，最后再回填
                int tAx = 0, bAx = 0;
                for (int i = 1; i < 3; i++)
                {
                    if (span[i] < span[tAx]) tAx = i;
                    if (span[i] > span[bAx]) bAx = i;
                }
                if (tAx == bAx) return false;
                int u = -1, v = -1;
                for (int i = 0; i < 3; i++) if (i != tAx) { if (u < 0) u = i; else v = i; }

                const int G = 12;
                int Occupancy(int side)
                {
                    var cells = new HashSet<int>();
                    float lo = mn[tAx] + span[tAx] * 0.15f;
                    float hi = mx[tAx] - span[tAx] * 0.15f;
                    foreach (var p in pts)
                    {
                        if (side > 0 ? p[tAx] < hi : p[tAx] > lo) continue;
                        int cu = Mathf.Min((int)((p[u] - mn[u]) / span[u] * G), G - 1);
                        int cv = Mathf.Min((int)((p[v] - mn[v]) / span[v] * G), G - 1);
                        cells.Add(cu * G + cv);
                    }
                    return cells.Count;
                }
                thin = tAx; big = bAx;
                strapSign = Occupancy(-1) < Occupancy(+1) ? -1 : 1;
                return true;
            }
            catch
            {
                return false;   // 网格不可读等异常：退回包围盒估计
            }
        }

        /// <summary>当前背包是否放了 flippack 标记文件（翻转肩带面）。子目录或根目录均可。</summary>
        bool BackpackFlipMarked()
        {
            if (string.IsNullOrEmpty(CurrentBackpack)) return false;
            return Resources.Load<TextAsset>("Characters/Backpacks/" + CurrentBackpack + "/flippack") != null
                || Resources.Load<TextAsset>("Characters/Backpacks/flippack_" + CurrentBackpack) != null;
        }

        /// <summary>包围盒八角在给定世界方向上的投影跨度（用于测缩放后的高度/厚度）。</summary>
        static float WorldExtentAlong(Transform t, Bounds lb, Vector3 worldDir)
        {
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = lb.center + Vector3.Scale(lb.extents, new Vector3(
                    (i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                float d = Vector3.Dot(t.TransformPoint(c), worldDir);
                if (d < min) min = d;
                if (d > max) max = d;
            }
            return max - min;
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
            // 面具眼孔对准眼睛：猫耳/顶饰把面具几何中心【抬高】，眼孔落在中心【偏下】；
            // 若把中心对到眼睛，眼孔会降到眼睛下方（遮住眼睛）。把中心【上移】一小段，让
            // 偏下的眼孔升到眼睛高度对准双眼。rise 为正=上移。
            Vector3 up = Vector3.up;
            float rise = headW * 0.18f;                           // 上移量(实测 0.34 偏高、0 偏低，取中)
            var eyeL = FindEye(head, "lefteye", "eyel", "eye_l");
            var eyeR = FindEye(head, "righteye", "eyer", "eye_r");
            Vector3 target;
            if (eyeL != null && eyeR != null)
                target = (eyeL.position + eyeR.position) * 0.5f + fwd * (headW * 0.22f) + up * rise;
            else if (eyeL != null || eyeR != null)
                target = (eyeL != null ? eyeL : eyeR).position + fwd * (headW * 0.22f) + up * rise;
            else
                // 无眼骨：头骨原点上方约半个头宽(眼高)、前方约 0.4 头宽（贴脸不悬空）
                target = head.position + fwd * (headW * 0.4f) + up * (headW * 0.5f + rise);

            // 按面具【背面】贴脸就座，而不是把中心对到脸点——否则半个厚度陷进头里，
            // 头部几何(鼻/颊)戳穿面具只露出残片，看起来"残缺不完整"。把整块面具沿
            // 法向前推到脸表面之外(半厚 + 微量间隙)，正面完整呈现、不与头穿插。
            // 对齐后面具正面法向已转到角色前方(fwd)，沿 fwd 前推即"往脸外"。
            float halfDepthW = (mk.TransformPoint(lb.center + axisOf(thin) * sz[thin] * 0.5f)
                - mk.TransformPoint(lb.center)).magnitude;
            Vector3 seat = target + fwd * (halfDepthW + headW * 0.04f);
            mk.position += seat - mk.TransformPoint(lb.center);
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

        static readonly Dictionary<string, Texture2D[]> _texCache = new Dictionary<string, Texture2D[]>();
        static Texture2D[] FolderTextures(string folder)
        {
            if (!_texCache.TryGetValue(folder, out var t))
            { t = Resources.LoadAll<Texture2D>(folder); _texCache[folder] = t; }
            return t;
        }

        void FixWeaponMaterials(GameObject weapon) =>
            FixModelMaterials(weapon, ScopedTextures("Characters/Weapons", CurrentWeapon));

        /// <summary>取某件装备可用的贴图集：优先用它【自己的子目录】（zip 解压出的
        /// snake-katana/、backpack/ 等——只含它自己的贴图，多件装备互不串味），子目录没有
        /// 贴图再退回装备库根目录（散图直接丢在根目录的情况）。</summary>
        Texture2D[] ScopedTextures(string root, string itemName)
        {
            Texture2D[] sub = string.IsNullOrEmpty(itemName) ? null
                : FolderTextures(root + "/" + itemName);
            return (sub != null && sub.Length > 0) ? sub : FolderTextures(root);
        }

        /// <summary>换装白模修复：下载的武器/背包 FBX/glTF 常常材质与贴图没接上（贴图相对
        /// 路径不符 / Maya lambert 默认材质未连贴图），呈现一片白。运行时按【贴图文件名
        /// 与材质名、网格名的词元重合度】把 texes 里的 basecolor/normal/metallic 贴图接到
        /// URP/Lit，恢复原本纹理。已带底图的材质不动。对任何丢进对应目录的模型通用。</summary>
        void FixModelMaterials(GameObject go, Texture2D[] texes)
        {
            if (texes == null || texes.Length == 0) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is TrailRenderer || r is LineRenderer || r is ParticleSystemRenderer) continue;
                var mats = r.materials;   // 实例材质，安全修改
                string meshKey = r.name.ToLowerInvariant();
                foreach (var m in mats)
                {
                    if (m == null) continue;
                    string matKey = m.name.ToLowerInvariant();
                    // 已带底图(兼容 URP _BaseMap/_MainTex 与 glTFast 的 baseColorTexture)则不动
                    if (HasBaseTex(m)) continue;
                    var bm = PickWeaponTex(texes, matKey, meshKey,
                        "basecolor", "base_color", "diffuse", "albedo", "_col", "color");
                    if (bm == null) continue;      // 找不到对应贴图就保持原样
                    // 强制切到 URP/Lit：确保有 _BaseMap/_BumpMap 可接（glTFast 自有着色器
                    // 属性名不同，接不上就还是白）；只有"确实白且找到贴图"的材质才切。
                    var lit = Shader.Find("Universal Render Pipeline/Lit");
                    if (lit != null) m.shader = lit;
                    m.SetTexture("_BaseMap", bm);
                    if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", bm);
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
                    var nm = PickWeaponTex(texes, matKey, meshKey, "normal", "_nrm");
                    if (nm != null)
                    {
                        m.SetTexture("_BumpMap", nm);
                        m.EnableKeyword("_NORMALMAP");
                    }
                    if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.5f);
                    var mr = PickWeaponTex(texes, matKey, meshKey, "metallic", "roughness", "metalness");
                    if (mr != null && m.HasProperty("_MetallicGlossMap"))
                    {
                        m.SetTexture("_MetallicGlossMap", mr);
                        m.EnableKeyword("_METALLICSPECGLOSSMAP");
                    }
                }
                r.materials = mats;
            }
        }

        /// <summary>材质是否已有【真实】底色贴图（跨 URP 与 glTFast 命名）。排除 1×1/极小
        /// 占位贴图——glTFast 找不到外部贴图时可能挂一张纯白占位图，误判成"已有贴图"就还是
        /// 白模；≤4px 视为占位，仍走目录贴图接线。</summary>
        static bool HasBaseTex(Material m)
        {
            string[] props = { "_BaseMap", "_MainTex", "baseColorTexture", "_baseColorTexture" };
            foreach (var p in props)
            {
                if (!m.HasProperty(p)) continue;
                var tex = m.GetTexture(p);
                if (tex != null && tex.width > 4 && tex.height > 4) return true;
            }
            return false;
        }

        /// <summary>在武器目录贴图里择优：先按类型关键词过滤（basecolor/normal…），
        /// 再按与【材质名(权重更高)、网格名】的词元重合度打分，多武器共存时选名字最贴合的。
        /// 无任何名字重合时退回首个类型匹配（单武器目录即唯一那张）。</summary>
        static Texture2D PickWeaponTex(Texture2D[] texes, string matKey, string meshKey,
            params string[] typeKeys)
        {
            Texture2D best = null; int bestScore = -1;
            foreach (var t in texes)
            {
                if (t == null) continue;
                string tn = t.name.ToLowerInvariant();
                bool typeMatch = false;
                foreach (var tk in typeKeys) if (tn.Contains(tk)) { typeMatch = true; break; }
                if (!typeMatch) continue;
                int score = 2 * TokenOverlap(tn, matKey) + TokenOverlap(tn, meshKey);
                if (score > bestScore) { bestScore = score; best = t; }
            }
            return best;
        }

        static int TokenOverlap(string texName, string key)
        {
            if (string.IsNullOrEmpty(key)) return 0;
            int score = 0;
            foreach (var tok in texName.Split('_', '-', '.', ' '))
            {
                if (tok.Length < 3) continue;
                // 跳过通用类型词，避免"base/color/normal"这类通用词误配
                if (tok == "base" || tok == "color" || tok == "basecolor" ||
                    tok == "normal" || tok == "diffuse" || tok == "albedo") continue;
                if (key.Contains(tok)) score++;
            }
            return score;
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

        /// <summary>当前武器是否放了 flipgrip 标记文件(翻转握向)。子目录或根目录均可。</summary>
        bool WeaponGripFlipMarked()
        {
            if (string.IsNullOrEmpty(CurrentWeapon)) return false;
            return Resources.Load<TextAsset>("Characters/Weapons/" + CurrentWeapon + "/flipgrip") != null
                || Resources.Load<TextAsset>("Characters/Weapons/flipgrip_" + CurrentWeapon) != null;
        }

        /// <summary>用【建模原点】判柄端：艺术家几乎总把模型原点(网格局部 0,0,0)建在握柄处，
        /// 故原点最靠近的那端即柄端。直接在【网格自身局部空间(mesh.bounds)】里看原点(0)落在
        /// 长轴哪一端，再把该"柄端点"经真实 transform 映射到 w 局部，取最近的 endA/endB。
        /// 只在原点明显偏向一端(pivot 未被居中/烘焙)时判定，否则返回 -1 交给截面法。
        /// 返回 0=endA 为柄、1=endB 为柄、-1=判不了。</summary>
        static int GripEndByModelOrigin(Transform w, Vector3 endA, Vector3 endB)
        {
            Transform main = null; Mesh mesh = null; int bestV = 0;
            foreach (var mf in w.GetComponentsInChildren<MeshFilter>(true))
            {
                int v = mf.sharedMesh != null ? mf.sharedMesh.vertexCount : 0;
                if (v > bestV) { bestV = v; main = mf.transform; mesh = mf.sharedMesh; }
            }
            foreach (var smr in w.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                int v = smr.sharedMesh != null ? smr.sharedMesh.vertexCount : 0;
                if (v > bestV) { bestV = v; main = smr.transform; mesh = smr.sharedMesh; }
            }
            if (mesh == null || main == null) return -1;
            Bounds mb = mesh.bounds;   // 网格自身局部包围盒
            int ax = mb.size.x >= mb.size.y && mb.size.x >= mb.size.z ? 0
                   : mb.size.y >= mb.size.z ? 1 : 2;
            float lo = mb.min[ax], hi = mb.max[ax];
            if (hi - lo < 1e-5f) return -1;
            float frac = (0f - lo) / (hi - lo);          // 原点(0)在长轴上的归一化位置
            if (frac >= 0.35f && frac <= 0.65f) return -1;   // 原点居中/被烘焙居中→无从判定
            Vector3 handleML = mb.center; handleML[ax] = frac < 0.5f ? lo : hi;   // 靠近原点的那端
            Vector3 handleWL = w.InverseTransformPoint(main.TransformPoint(handleML));
            return (handleWL - endA).sqrMagnitude <= (handleWL - endB).sqrMagnitude ? 0 : 1;
        }

        /// <summary>柄端判定（多信号，优先级从高到低），返回 true=endA 为柄端：
        ///   ① 柄/握把子节点(grip/handle/hilt/tsuka/柄)——最可靠
        ///   ② 建模原点(主网格局部原点)所在的一端=柄端（艺术家几乎总把原点建在握柄处）
        ///   ③ 网格截面(最宽片=护手所在半段为柄端)——原点居中的直剑(如 Sword 18)靠它
        ///   ④ pivot 就近兜底
        /// 最后叠加 flipgrip 标记文件（产品级手动纠正，随资源分发，见 Weapons/README）。</summary>
        bool DecideGripEnd(Transform w, Bounds lb, Vector3 endA, Vector3 endB)
        {
            bool gripAtA;
            var gripNode = FindDeep(w, "grip") ?? FindDeep(w, "handle")
                ?? FindDeep(w, "hilt") ?? FindDeep(w, "tsuka") ?? FindDeep(w, "柄");
            int originGrip = GripEndByModelOrigin(w, endA, endB);   // 0=endA 柄,1=endB 柄,-1=判不了
            if (gripNode != null)
            {
                Vector3 g = w.InverseTransformPoint(gripNode.position);
                gripAtA = (g - endA).sqrMagnitude <= (g - endB).sqrMagnitude;
            }
            else if (originGrip == 0) gripAtA = true;    // 建模原点贴近低端=柄
            else if (originGrip == 1) gripAtA = false;   // 建模原点贴近高端=柄
            else if (GripEndByProfile(w, lb, out bool profA, endA, endB)) gripAtA = profA;
            else gripAtA = endA.sqrMagnitude <= endB.sqrMagnitude;
            // 对称武器(导入把 pivot 居中、几何无从判柄)的确定性兜底：flipgrip 标记翻转
            if (WeaponGripFlipMarked()) gripAtA = !gripAtA;
            return gripAtA;
        }

        /// <summary>把已按握位对齐的武器包进【掌心枢轴】节点：枢轴原点=掌心、+Y=刃向，
        /// 与程序化武器(WeaponFactory)同一约定。耍花(ApplyWeaponFlourish)只旋转枢轴——
        /// 武器绕掌心挥舞、柄端永远在手里。此前直接把导入模型根交给耍花，每帧被改写
        /// localRotation、绕模型自身原点(常在武器几何中心)整体乱转——握向修正全被覆盖，
        /// 这正是"katana 怎么修都像握反"的真正根因。</summary>
        Transform WrapWeaponPivot(Transform w, Transform hand, Vector3 bladeDirHandLocal, Vector3 gripWorld)
        {
            var pv = new GameObject(EquippedName + "Pivot").transform;
            pv.SetParent(hand, false);
            pv.position = gripWorld;
            Vector3 bladeDirW = hand.TransformDirection(bladeDirHandLocal);
            if (bladeDirW.sqrMagnitude > 1e-8f)
                pv.rotation = Quaternion.FromToRotation(Vector3.up, bladeDirW.normalized);
            w.SetParent(pv, true);
            return pv;
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

            bool atA = DecideGripEnd(w, lb, endA, endB);
            Vector3 gripL = atA ? endA : endB, tipL = atA ? endB : endA;
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

        /// <summary>网格截面分析判柄端（防"剑拿反、握住刀刃"）：沿长轴切 16 片，统计每片
        /// 最大截面半径，取【最宽片(护手/护拳最粗)所在半段】为柄端半段。真机网格实测通过
        /// (Sword 18 / Snake Sword)。斧/锤/镰最宽处是打击头=尖端，按名称翻转。
        /// 需网格可读(Weapons 目录 FBX 已由导入器开 Read/Write)；不可读返回 false 走 pivot 兜底。</summary>
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
                const int slices = 16;
                var radial = new float[slices];
                int total = 0;
                foreach (var mf in w.GetComponentsInChildren<MeshFilter>(true))
                    total += AccumProfile(w, mf.transform, mf.sharedMesh, endA, axis, axisLen, radial);
                foreach (var smr in w.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    total += AccumProfile(w, smr.transform, smr.sharedMesh, endA, axis, axisLen, radial);
                if (total < 24) return false;

                // 柄端判据：【最宽截面片(护手/护拳)所在的半段】就是柄端半段——真机网格
                // 实测验证(Sword 18：护手在长轴 75% 处即高半段，柄在其外侧→柄端=高端；
                // Snake Sword：护手在低半段→柄端=低端)。此法比"最外片对比"稳：刀身在近尖
                // 处仍可能很宽，用最外片会把尖端误判成柄端(那正是把 Sword 18 拿反的原因)。
                int widest = 0;
                for (int i = 1; i < slices; i++)
                    if (radial[i] > radial[widest]) widest = i;
                gripAtA = widest < slices / 2;   // 最宽片在前半段→柄在 endA 端，反之在 endB
                // 斧/锤/镰：最宽处是打击头=尖端，柄在细端，翻转
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
