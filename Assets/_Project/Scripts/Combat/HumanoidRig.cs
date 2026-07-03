using UnityEngine;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 程序化人形骨骼：完整人体结构——头（含五官/发型/斗笠）、躯干、骨盆、
    /// 双臂（肩/肘/手）、双腿（髋/膝/脚）。每个关节是独立枢轴，
    /// 供 HumanoidAnimator 做类人关节动画。零外部资产。
    /// </summary>
    public class HumanoidRig
    {
        public Transform root;
        public Transform pelvis, torso, head;
        public Transform shoulderL, shoulderR, elbowL, elbowR, handL, handR;
        public Transform hipL, hipR, kneeL, kneeR, footL, footR;

        public struct Config
        {
            public Color skin;      // 肤色
            public Color top;       // 上装
            public Color bottom;    // 下装
            public Color shoes;     // 鞋
            public Color hair;      // 发色
            public Color hatColor;  // 斗笠色
            public Color eye;       // 眼睛
            public bool hasHat;     // 戴斗笠（遮发）
            public float bulk;      // 体格宽度倍率（0.9 清瘦 / 1.15 魁梧）
        }

        static Material _fallbackBase;

        public static HumanoidRig Build(Transform parent, Config cfg, Material baseMat)
        {
            _fallbackBase = baseMat;
            float b = Mathf.Max(0.7f, cfg.bulk);
            var rig = new HumanoidRig();

            rig.root = parent;
            rig.pelvis = Joint(parent, "Pelvis", new Vector3(0, -0.05f, 0));

            // 骨盆与躯干
            Geo(rig.pelvis, PrimitiveType.Cube, new Vector3(0, -0.02f, 0),
                new Vector3(0.36f * b, 0.22f, 0.22f * b), cfg.bottom);
            rig.torso = Joint(rig.pelvis, "Torso", new Vector3(0, 0.08f, 0));
            Geo(rig.torso, PrimitiveType.Cube, new Vector3(0, 0.28f, 0),
                new Vector3(0.4f * b, 0.55f, 0.24f * b), cfg.top);
            Geo(rig.torso, PrimitiveType.Cube, new Vector3(0, 0.02f, 0),
                new Vector3(0.42f * b, 0.08f, 0.26f * b), Darker(cfg.bottom, 0.7f)); // 腰带

            // 头部与五官
            rig.head = Joint(rig.torso, "Head", new Vector3(0, 0.6f, 0));
            Geo(rig.head, PrimitiveType.Cube, new Vector3(0, 0.04f, 0),
                new Vector3(0.1f, 0.1f, 0.1f), cfg.skin);                            // 脖颈
            Geo(rig.head, PrimitiveType.Sphere, new Vector3(0, 0.2f, 0),
                new Vector3(0.34f, 0.36f, 0.34f), cfg.skin);                         // 头
            Geo(rig.head, PrimitiveType.Cube, new Vector3(0.065f, 0.23f, 0.145f),
                new Vector3(0.055f, 0.03f, 0.02f), cfg.eye);                         // 右眼
            Geo(rig.head, PrimitiveType.Cube, new Vector3(-0.065f, 0.23f, 0.145f),
                new Vector3(0.055f, 0.03f, 0.02f), cfg.eye);                         // 左眼
            Geo(rig.head, PrimitiveType.Cube, new Vector3(0, 0.17f, 0.165f),
                new Vector3(0.045f, 0.06f, 0.04f), Darker(cfg.skin, 0.92f));         // 鼻
            Geo(rig.head, PrimitiveType.Cube, new Vector3(0, 0.11f, 0.15f),
                new Vector3(0.09f, 0.02f, 0.02f), Darker(cfg.skin, 0.75f));          // 嘴
            if (cfg.hasHat)
            {
                Geo(rig.head, PrimitiveType.Cylinder, new Vector3(0, 0.36f, 0),
                    new Vector3(0.62f, 0.035f, 0.62f), cfg.hatColor);                // 斗笠沿
                Geo(rig.head, PrimitiveType.Cylinder, new Vector3(0, 0.42f, 0),
                    new Vector3(0.22f, 0.06f, 0.22f), Darker(cfg.hatColor, 0.85f));  // 斗笠顶
            }
            else
            {
                Geo(rig.head, PrimitiveType.Sphere, new Vector3(0, 0.3f, -0.04f),
                    new Vector3(0.32f, 0.22f, 0.32f), cfg.hair);                     // 头发
            }

            // 双臂：肩→肘→手
            rig.shoulderR = Joint(rig.torso, "ShoulderR", new Vector3(0.26f * b, 0.5f, 0));
            rig.elbowR = BuildArm(rig.shoulderR, cfg, out rig.handR);
            rig.shoulderL = Joint(rig.torso, "ShoulderL", new Vector3(-0.26f * b, 0.5f, 0));
            rig.elbowL = BuildArm(rig.shoulderL, cfg, out rig.handL);

            // 双腿：髋→膝→脚
            rig.hipR = Joint(rig.pelvis, "HipR", new Vector3(0.11f * b, -0.1f, 0));
            rig.kneeR = BuildLeg(rig.hipR, cfg, b, out rig.footR);
            rig.hipL = Joint(rig.pelvis, "HipL", new Vector3(-0.11f * b, -0.1f, 0));
            rig.kneeL = BuildLeg(rig.hipL, cfg, b, out rig.footL);

            return rig;
        }

        static Transform BuildArm(Transform shoulder, Config cfg, out Transform hand)
        {
            Geo(shoulder, PrimitiveType.Cube, new Vector3(0, -0.16f, 0),
                new Vector3(0.11f, 0.34f, 0.11f), cfg.top);                          // 上臂（袖）
            var elbow = Joint(shoulder, "Elbow", new Vector3(0, -0.32f, 0));
            Geo(elbow, PrimitiveType.Cube, new Vector3(0, -0.14f, 0),
                new Vector3(0.09f, 0.3f, 0.09f), cfg.skin);                          // 前臂
            hand = Joint(elbow, "Hand", new Vector3(0, -0.3f, 0));
            Geo(hand, PrimitiveType.Cube, new Vector3(0, -0.05f, 0.01f),
                new Vector3(0.1f, 0.13f, 0.07f), cfg.skin);                          // 手掌
            return elbow;
        }

        static Transform BuildLeg(Transform hip, Config cfg, float b, out Transform foot)
        {
            Geo(hip, PrimitiveType.Cube, new Vector3(0, -0.23f, 0),
                new Vector3(0.15f * b, 0.46f, 0.16f * b), cfg.bottom);               // 大腿
            var knee = Joint(hip, "Knee", new Vector3(0, -0.46f, 0));
            Geo(knee, PrimitiveType.Cube, new Vector3(0, -0.2f, 0),
                new Vector3(0.12f, 0.4f, 0.13f), Darker(cfg.bottom, 0.85f));         // 小腿
            foot = Joint(knee, "Foot", new Vector3(0, -0.4f, 0));
            Geo(foot, PrimitiveType.Cube, new Vector3(0, -0.035f, 0.08f),
                new Vector3(0.14f, 0.09f, 0.32f), cfg.shoes);                        // 脚
            return knee;
        }

        static Transform Joint(Transform parent, string name, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go.transform;
        }

        static void Geo(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Color c)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            Material m;
            if (_fallbackBase != null) m = new Material(_fallbackBase);
            else
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh == null) sh = Shader.Find("Standard");
                m = new Material(sh);
            }
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            r.sharedMaterial = m;
        }

        static Color Darker(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);
    }
}
