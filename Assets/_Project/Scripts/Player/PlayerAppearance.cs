using UnityEngine;
using AdversityRoad.Combat;

namespace AdversityRoad.Player
{
    /// <summary>
    /// 玩家外观系统：两套可切换角色预设（身材/肤色/着装/兵器各不相同）。
    /// 预设 0「青岚剑客」：清瘦、青蓝劲装、持剑；
    /// 预设 1「烈风刀客」：魁梧、赤褐短打、斗笠、持刀。
    /// 选择本地持久化。
    /// </summary>
    public class PlayerAppearance : MonoBehaviour
    {
        public Transform visualRoot;
        public SimpleAnimator poser;
        public Material baseMaterial;

        public int Preset { get; private set; }

        const string PrefKey = "player_preset";

        public static readonly string[] PresetNames = { "青岚剑客", "烈风刀客" };

        public void Init()
        {
            Preset = PlayerPrefs.GetInt(PrefKey, 0);
            Rebuild();
        }

        public void TogglePreset()
        {
            Preset = (Preset + 1) % PresetNames.Length;
            PlayerPrefs.SetInt(PrefKey, Preset);
            PlayerPrefs.Save();
            Rebuild();
            Core.GameEvents.RaiseSubtitle("已切换角色：" + PresetNames[Preset]);
        }

        void Rebuild()
        {
            for (int i = visualRoot.childCount - 1; i >= 0; i--)
                DestroyImmediate(visualRoot.GetChild(i).gameObject);
            visualRoot.localRotation = Quaternion.identity;
            visualRoot.localPosition = Vector3.zero;
            visualRoot.localScale = Vector3.one;

            if (Preset == 0) BuildSwordsman();
            else BuildBladesman();
        }

        // 预设 0：青岚剑客——清瘦、青蓝劲装、束发、佩剑
        void BuildSwordsman()
        {
            var body = Capsule(new Vector3(0, 0, 0), new Vector3(0.88f, 1f, 0.88f),
                new Color(0.22f, 0.42f, 0.72f));                                   // 青蓝劲装
            Sphere(new Vector3(0, 0.62f, 0.02f), 0.52f, new Color(0.95f, 0.8f, 0.68f)); // 面容
            Sphere(new Vector3(0, 0.92f, -0.05f), 0.4f, new Color(0.12f, 0.1f, 0.1f));  // 束发
            Box(new Vector3(0, -0.15f, 0), new Vector3(0.95f, 0.1f, 0.5f),
                new Color(0.85f, 0.75f, 0.4f));                                    // 腰带
            Box(new Vector3(0, 0.25f, 0.4f), new Vector3(0.5f, 0.55f, 0.08f),
                new Color(0.3f, 0.5f, 0.78f));                                     // 前襟

            MountWeapon(WeaponKind.Sword);
        }

        // 预设 1：烈风刀客——魁梧、赤褐短打、斗笠、佩刀
        void BuildBladesman()
        {
            var body = Capsule(new Vector3(0, 0, 0), new Vector3(1.12f, 0.95f, 1.12f),
                new Color(0.62f, 0.28f, 0.2f));                                    // 赤褐短打
            Sphere(new Vector3(0, 0.58f, 0.02f), 0.55f, new Color(0.85f, 0.66f, 0.5f)); // 面容（黝黑）
            Cylinder(new Vector3(0, 0.98f, 0), new Vector3(1.15f, 0.05f, 1.15f),
                new Color(0.78f, 0.68f, 0.42f));                                   // 斗笠
            Cylinder(new Vector3(0, 1.05f, 0), new Vector3(0.35f, 0.08f, 0.35f),
                new Color(0.65f, 0.55f, 0.32f));                                   // 笠顶
            Box(new Vector3(0.55f, 0.35f, 0), new Vector3(0.3f, 0.25f, 0.6f),
                new Color(0.4f, 0.22f, 0.15f));                                    // 护肩
            Box(new Vector3(0, -0.12f, 0), new Vector3(1.2f, 0.14f, 0.55f),
                new Color(0.25f, 0.18f, 0.12f));                                   // 宽腰带

            MountWeapon(WeaponKind.Blade);
        }

        void MountWeapon(WeaponKind kind)
        {
            var rig = WeaponFactory.Build(kind, visualRoot, baseMaterial);
            if (poser != null && rig != null)
            {
                poser.weaponPivot = rig.pivot;
                poser.weaponTrail = rig.trail;
            }
        }

        // ---------- 部件 ----------

        GameObject Capsule(Vector3 pos, Vector3 scale, Color c) =>
            Primitive(PrimitiveType.Capsule, pos, scale, c);

        GameObject Sphere(Vector3 pos, float d, Color c) =>
            Primitive(PrimitiveType.Sphere, pos, Vector3.one * d, c);

        GameObject Box(Vector3 pos, Vector3 scale, Color c) =>
            Primitive(PrimitiveType.Cube, pos, scale, c);

        GameObject Cylinder(Vector3 pos, Vector3 scale, Color c) =>
            Primitive(PrimitiveType.Cylinder, pos, scale, c);

        GameObject Primitive(PrimitiveType type, Vector3 pos, Vector3 scale, Color c)
        {
            var go = GameObject.CreatePrimitive(type);
            DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(visualRoot, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            Material m;
            if (baseMaterial != null) m = new Material(baseMaterial);
            else
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh == null) sh = Shader.Find("Standard");
                m = new Material(sh);
            }
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            r.sharedMaterial = m;
            return go;
        }
    }
}
