using UnityEngine;

namespace AdversityRoad.Combat
{
    public enum WeaponKind { None, Sword, Blade, Staff, Claw }

    public class WeaponRig
    {
        public Transform pivot;
        public TrailRenderer trail;
    }

    /// <summary>
    /// 程序化兵器工厂：剑 / 刀 / 棍 / 爪，全部由基础几何体拼装，
    /// 刀尖带拖尾（TrailRenderer），挥击时由 SimpleAnimator 开启形成刀光。
    /// </summary>
    public static class WeaponFactory
    {
        public static WeaponRig Build(WeaponKind kind, Transform hand, Material baseMat) =>
            Build(kind, hand, baseMat, new Vector3(0.55f, 0.15f, 0.1f), new Vector3(25f, 0, 0));

        /// <summary>指定挂点位置与静息角度（人形骨骼挂右手关节时使用）。</summary>
        public static WeaponRig Build(WeaponKind kind, Transform hand, Material baseMat,
            Vector3 localPos, Vector3 localEuler)
        {
            if (kind == WeaponKind.None) return null;

            var pivotGo = new GameObject("WeaponPivot");
            pivotGo.transform.SetParent(hand, false);
            pivotGo.transform.localPosition = localPos;
            pivotGo.transform.localRotation = Quaternion.Euler(localEuler);

            var rig = new WeaponRig { pivot = pivotGo.transform };

            switch (kind)
            {
                case WeaponKind.Sword:
                    Part(pivotGo.transform, new Vector3(0, 0.14f, 0), new Vector3(0.06f, 0.3f, 0.06f),
                        new Color(0.3f, 0.2f, 0.12f), baseMat);                       // 剑柄
                    Part(pivotGo.transform, new Vector3(0, 0.31f, 0), new Vector3(0.26f, 0.05f, 0.09f),
                        new Color(0.75f, 0.65f, 0.3f), baseMat);                      // 护手
                    Part(pivotGo.transform, new Vector3(0, 0.85f, 0), new Vector3(0.09f, 1.05f, 0.03f),
                        new Color(0.82f, 0.86f, 0.95f), baseMat);                     // 剑身
                    rig.trail = Trail(pivotGo.transform, new Vector3(0, 1.35f, 0),
                        new Color(0.6f, 0.85f, 1f), baseMat);
                    break;

                case WeaponKind.Blade:
                    Part(pivotGo.transform, new Vector3(0, 0.14f, 0), new Vector3(0.07f, 0.3f, 0.07f),
                        new Color(0.25f, 0.15f, 0.1f), baseMat);                      // 刀柄
                    Part(pivotGo.transform, new Vector3(0, 0.3f, 0), new Vector3(0.2f, 0.05f, 0.12f),
                        new Color(0.55f, 0.4f, 0.2f), baseMat);                       // 刀镡
                    Part(pivotGo.transform, new Vector3(0.03f, 0.82f, 0), new Vector3(0.16f, 0.95f, 0.03f),
                        new Color(0.7f, 0.72f, 0.78f), baseMat);                      // 刀身（宽）
                    Part(pivotGo.transform, new Vector3(-0.06f, 0.82f, 0), new Vector3(0.05f, 0.95f, 0.032f),
                        new Color(0.4f, 0.42f, 0.48f), baseMat);                      // 刀背
                    rig.trail = Trail(pivotGo.transform, new Vector3(0.03f, 1.28f, 0),
                        new Color(1f, 0.75f, 0.45f), baseMat);
                    break;

                case WeaponKind.Staff:
                    Part(pivotGo.transform, new Vector3(0, 0.35f, 0), new Vector3(0.08f, 1.7f, 0.08f),
                        new Color(0.5f, 0.35f, 0.2f), baseMat);                       // 长棍
                    Part(pivotGo.transform, new Vector3(0, 1.2f, 0), new Vector3(0.12f, 0.12f, 0.12f),
                        new Color(0.8f, 0.7f, 0.4f), baseMat);                        // 棍首
                    rig.trail = Trail(pivotGo.transform, new Vector3(0, 1.2f, 0),
                        new Color(0.85f, 0.75f, 0.5f), baseMat);
                    break;

                case WeaponKind.Claw:
                    for (int i = -1; i <= 1; i++)
                        Part(pivotGo.transform, new Vector3(i * 0.08f, 0.35f, 0),
                            new Vector3(0.04f, 0.5f, 0.03f), new Color(0.35f, 0.3f, 0.4f), baseMat);
                    rig.trail = Trail(pivotGo.transform, new Vector3(0, 0.62f, 0),
                        new Color(0.7f, 0.4f, 0.8f), baseMat);
                    break;
            }
            return rig;
        }

        static void Part(Transform parent, Vector3 localPos, Vector3 scale, Color color, Material baseMat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            Material m;
            if (baseMat != null) m = new Material(baseMat);
            else
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh == null) sh = Shader.Find("Standard");
                m = new Material(sh);
            }
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            r.sharedMaterial = m;
        }

        static TrailRenderer Trail(Transform parent, Vector3 localPos, Color color, Material baseMat)
        {
            var tipGo = new GameObject("WeaponTip");
            tipGo.transform.SetParent(parent, false);
            tipGo.transform.localPosition = localPos;
            var trail = tipGo.AddComponent<TrailRenderer>();
            trail.time = 0.32f;
            trail.startWidth = 0.28f;   // 加宽刀光：剑花轨迹清晰可见
            trail.endWidth = 0f;
            trail.minVertexDistance = 0.03f;
            trail.emitting = false;
            Material m;
            if (baseMat != null) m = new Material(baseMat);
            else
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh == null) sh = Shader.Find("Standard");
                m = new Material(sh);
            }
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            trail.material = m;
            trail.startColor = color;
            trail.endColor = new Color(color.r, color.g, color.b, 0f);
            return trail;
        }
    }
}
