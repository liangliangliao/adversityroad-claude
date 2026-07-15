using UnityEngine;

namespace AdversityRoad.Player
{
    /// <summary>
    /// 背包跟随绑定 + 程序化肩带。
    ///
    /// 【为什么需要它】背包若只在装备瞬间按世界方向摆一次姿态，会把"装备时刻的骨骼姿势"
    /// 烘进本地变换——不同模型的绑定姿势与播放姿势差异（尤其 glb 角色）会让背包在运行时
    /// 横倒 90°、沉进躯干（"背包横穿身体"的根因）。本组件改为【每帧】从活动骨骼推算：
    ///   竖直 = 髋→颈方向（跟随躯干前倾），朝向 = 角色面向，
    ///   座位 = 上胸骨骼后方【躯干半厚 + 半包厚】处（背板贴背、绝不穿胸），包顶到肩线。
    ///
    /// 【肩带】背包模型普遍没有"跨肩到胸前"的肩带几何，无法靠摆模型实现。这里按真实
    /// 背包的穿戴方式程序化生成两条肩带（管状网格、Catmull-Rom 曲线）：
    ///   包顶前缘 → 肩上 → 锁骨前 → 肋侧收紧 → 绕到包底前角扣紧，
    /// 控制点全部取自活动骨骼与包体几何，每帧重算——任何体型、任何动作都贴身拉紧。
    /// </summary>
    [DefaultExecutionOrder(5100)]   // 在动画与骨骼驱动之后跑，姿态不被覆盖
    public class BackpackRig : MonoBehaviour
    {
        Transform _visual, _hips, _neck, _chest, _shL, _shR;
        Quaternion _qFix;                  // 模型轴修正：厚轴→+Z(鼓面朝外)、高轴→+Y
        Vector3 _lbCenter;                 // 包围盒中心（包本地）
        float _backOff, _liftOff;          // 后移量（躯干半厚+半包厚）、抬升量（包顶到肩线）
        float _w, _h, _d, _torsoHalf, _bodyH;
        Mesh _mL, _mR; Transform _tL, _tR;
        readonly Vector3[] _pts = new Vector3[6];
        Vector3[] _vertsL, _vertsR;        // 顶点缓存（每帧复用，避免移动端 GC）
        const int Sides = 6;               // 肩带管截面边数

        public void Setup(Transform visual, Transform hips, Transform neck, Transform chest,
            Transform shL, Transform shR, Quaternion qFix, Vector3 lbCenter,
            float backOff, float liftOff, float packW, float packH, float packD,
            float torsoHalf, float bodyH, Material baseMat)
        {
            _visual = visual; _hips = hips; _neck = neck; _chest = chest; _shL = shL; _shR = shR;
            _qFix = qFix; _lbCenter = lbCenter;
            _backOff = backOff; _liftOff = liftOff;
            _w = packW; _h = packH; _d = packD; _torsoHalf = torsoHalf; _bodyH = bodyH;
            _tL = MakeStrap("StrapL", baseMat, out _mL);
            _tR = MakeStrap("StrapR", baseMat, out _mR);
            Apply();
        }

        Transform MakeStrap(string name, Material baseMat, out Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            var mat = baseMat != null ? new Material(baseMat)
                : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = new Color(0.16f, 0.14f, 0.12f);   // 深灰织带色
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", mat.color);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);   // 双面，绕向无关
            mr.material = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mesh = new Mesh(); mesh.MarkDynamic();
            mf.sharedMesh = mesh;
            return go.transform;
        }

        void LateUpdate() => Apply();

        void Apply()
        {
            if (_visual == null || _chest == null) return;

            // 竖直跟随躯干（髋→颈），朝向跟随角色面向——与绑定姿势/骨轴约定无关
            Vector3 up = (_neck != null && _hips != null)
                ? (_neck.position - _hips.position) : Vector3.up;
            if (up.sqrMagnitude < 1e-6f || float.IsNaN(up.x)) up = Vector3.up;
            up.Normalize();
            Vector3 fwd = Vector3.ProjectOnPlane(_visual.forward, up);
            if (fwd.sqrMagnitude < 1e-6f) fwd = _visual.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(up, fwd).normalized;

            transform.rotation = Quaternion.LookRotation(-fwd, up) * _qFix;
            Vector3 seat = _chest.position - fwd * _backOff + up * _liftOff;
            transform.position += seat - transform.TransformPoint(_lbCenter);

            // ---- 程序化肩带：包顶前缘→肩上→锁骨前→肋侧→包底前角（左右各一条）----
            Vector3 packFront = seat + fwd * (_d * 0.5f);   // 贴背的一面
            for (int s = 0; s < 2; s++)
            {
                float sd = s == 0 ? -1f : 1f;
                Transform sh = s == 0 ? _shL : _shR;
                Vector3 shoulder = sh != null
                    ? sh.position + up * (_bodyH * 0.035f)
                    : _chest.position + up * (_bodyH * 0.10f) + right * (sd * _bodyH * 0.09f);

                _pts[0] = packFront + up * (_h * 0.42f) + right * (sd * _w * 0.26f);
                _pts[1] = shoulder + right * (sd * _bodyH * 0.01f);
                _pts[2] = _chest.position + fwd * (_torsoHalf * 1.02f)
                        + right * (sd * _bodyH * 0.055f) + up * (_bodyH * 0.02f);
                _pts[3] = Vector3.Lerp(_chest.position,
                            _hips != null ? _hips.position : _chest.position - up * (_bodyH * 0.2f), 0.7f)
                        + fwd * (_torsoHalf * 0.85f) + right * (sd * _bodyH * 0.075f);
                _pts[4] = Vector3.Lerp(_chest.position,
                            _hips != null ? _hips.position : _chest.position - up * (_bodyH * 0.2f), 0.85f)
                        + right * (sd * _bodyH * 0.105f);   // 绕过肋侧（避免直穿躯干）
                _pts[5] = packFront - up * (_h * 0.40f) + right * (sd * _w * 0.30f);

                if (s == 0) _vertsL = BuildTube(_mL, _tL, _pts, _vertsL);
                else _vertsR = BuildTube(_mR, _tR, _pts, _vertsR);
            }
        }

        /// <summary>沿控制点的 Catmull-Rom 曲线生成管状肩带网格（每帧更新顶点，拓扑只建一次）。</summary>
        Vector3[] BuildTube(Mesh m, Transform t, Vector3[] cp, Vector3[] verts)
        {
            int n = cp.Length;
            int rings = (n - 1) * 4 + 1;
            float r = _bodyH * 0.012f;
            if (verts == null || verts.Length != rings * Sides) verts = new Vector3[rings * Sides];
            Vector3 prevPos = cp[0];
            for (int i = 0; i < rings; i++)
            {
                float u = (float)i / (rings - 1) * (n - 1);
                int seg = Mathf.Min(Mathf.FloorToInt(u), n - 2);
                float f = u - seg;
                Vector3 p0 = cp[Mathf.Max(seg - 1, 0)], p1 = cp[seg];
                Vector3 p2 = cp[seg + 1], p3 = cp[Mathf.Min(seg + 2, n - 1)];
                Vector3 pos = CatmullRom(p0, p1, p2, p3, f);
                Vector3 tan = CatmullRom(p0, p1, p2, p3, Mathf.Min(f + 0.05f, 1f))
                            - CatmullRom(p0, p1, p2, p3, Mathf.Max(f - 0.05f, 0f));
                if (tan.sqrMagnitude < 1e-8f) tan = pos - prevPos;
                if (tan.sqrMagnitude < 1e-8f) tan = Vector3.up;
                tan.Normalize();
                Vector3 n1 = Vector3.Cross(tan, Vector3.up);
                if (n1.sqrMagnitude < 1e-4f) n1 = Vector3.Cross(tan, Vector3.right);
                n1.Normalize();
                Vector3 n2 = Vector3.Cross(tan, n1).normalized;
                for (int k = 0; k < Sides; k++)
                {
                    float a = k * Mathf.PI * 2f / Sides;
                    verts[i * Sides + k] = t.InverseTransformPoint(
                        pos + (n1 * Mathf.Cos(a) + n2 * Mathf.Sin(a)) * r);
                }
                prevPos = pos;
            }

            if (m.vertexCount != verts.Length)
            {
                var tris = new int[(rings - 1) * Sides * 6];
                int ti = 0;
                for (int i = 0; i < rings - 1; i++)
                    for (int k = 0; k < Sides; k++)
                    {
                        int a = i * Sides + k, b = i * Sides + (k + 1) % Sides;
                        int c = a + Sides, d = b + Sides;
                        tris[ti++] = a; tris[ti++] = c; tris[ti++] = b;
                        tris[ti++] = b; tris[ti++] = c; tris[ti++] = d;
                    }
                m.Clear();
                m.vertices = verts;
                m.triangles = tris;
            }
            else
            {
                m.vertices = verts;
            }
            m.RecalculateNormals();
            m.RecalculateBounds();
            return verts;
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * ((2f * p1) + (-p0 + p2) * t
                + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }
    }
}
