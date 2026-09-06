using System.Collections.Generic;
using UnityEngine;

namespace AdversityRoad.World
{
    /// <summary>表面材质类别：决定运行时生成哪种可平铺纹理与哪套 PBR 响应。</summary>
    public enum SurfaceKind { None, Plaster, Concrete, Wood, Metal, Ground, Fabric, Brick }

    /// <summary>
    /// 运行时程序化表面（零美术资产、兼容 CI 无头打包）。
    ///
    /// 【为什么不做法线贴图，又怎么把凹凸做出来】
    /// 运行时 new 出来的 Texture2D 打不上 Import Settings 的「Normal map」标记，
    /// 于是它的通道布局到底按 RGB 解还是按 DXT5nm 的 AG 解，取决于目标平台是否定义
    /// UNITY_NO_DXT5nm——安卓包和编辑器（Windows/Mac）在这一点上结论相反。
    /// 猜错的结果不是"效果差一点"，是整面墙的法线指向错误、光照全乱。
    /// 这个风险不值得冒，所以这里**不出法线贴图**。
    ///
    /// 取而代之的是把浮雕**烘进 Albedo**：先算出这个类别的高度场，再按高度场的梯度
    /// 做一次定向着色（光从左上来），凹缝的一侧压暗、另一侧提亮。
    /// 这是没有法线贴图时的通行做法，格式上零风险，而且在本作这种大平面盒子上读得非常清楚。
    ///
    /// 【为什么值域要拉开】
    /// 上一版每个类别的灰度都挤在 0.85±0.06 这种范围里——那点差别在屏幕上等于没有，
    /// 于是不管贴不贴图，看到的都是一块纯色。现在各类别按材质本身该有的对比度给值，
    /// 并且配一套自己的金属度/光滑度（见 <see cref="Response"/>）：
    /// 金属反光、木头半哑、抹灰全哑，光一打上去就分得出来。
    /// </summary>
    public static class ProceduralTextures
    {
        const int Res = 512;
        static readonly Dictionary<SurfaceKind, Texture2D> _cache = new Dictionary<SurfaceKind, Texture2D>();

        /// <summary>某个表面类别的 PBR 响应：金属度与光滑度。</summary>
        public static void Response(SurfaceKind k, out float metallic, out float smoothness)
        {
            switch (k)
            {
                // 拉丝金属：真的按金属算，才会有环境反射而不是一块灰塑料
                // 金属度不敢给满：环境反射走的是天空盒探针（reflectionIntensity 0.7），
                // 0.8 以上的金属在夜间室内会变成一面映着白天天空的镜子。
                // 0.62/0.48 已经足够让钢管、储物柜、灯具与木头、抹灰明显分开。
                case SurfaceKind.Metal:    metallic = 0.62f; smoothness = 0.48f; break;
                // 上过漆的木面：不反射环境，但有一层薄高光
                case SurfaceKind.Wood:     metallic = 0f;    smoothness = 0.34f; break;
                // 水磨/自流平地面：湿冷的一点反光
                case SurfaceKind.Concrete: metallic = 0f;    smoothness = 0.20f; break;
                case SurfaceKind.Ground:   metallic = 0f;    smoothness = 0.13f; break;
                case SurfaceKind.Brick:    metallic = 0f;    smoothness = 0.09f; break;
                // 抹灰墙与织物：几乎全哑光
                case SurfaceKind.Plaster:  metallic = 0f;    smoothness = 0.07f; break;
                case SurfaceKind.Fabric:   metallic = 0f;    smoothness = 0.03f; break;
                default:                   metallic = 0f;    smoothness = 0.16f; break;
            }
        }

        /// <summary>浮雕强度：高度场梯度参与着色的比例。缝越深的材质给得越高。</summary>
        static float Relief(SurfaceKind k)
        {
            switch (k)
            {
                case SurfaceKind.Brick:    return 3.2f;
                case SurfaceKind.Wood:     return 2.0f;
                case SurfaceKind.Concrete: return 1.7f;
                case SurfaceKind.Ground:   return 2.4f;
                case SurfaceKind.Fabric:   return 1.4f;
                case SurfaceKind.Plaster:  return 1.1f;
                case SurfaceKind.Metal:    return 1.6f;
                default:                   return 0f;
            }
        }

        public static Texture2D Albedo(SurfaceKind kind)
        {
            if (kind == SurfaceKind.None) return null;
            if (_cache.TryGetValue(kind, out var t) && t != null) return t;

            // 第一遍：算出高度场。它同时是明暗底色，也是下面算梯度的依据。
            var h = new float[Res * Res];
            for (int y = 0; y < Res; y++)
            {
                float v = (y + 0.5f) / Res;
                for (int x = 0; x < Res; x++)
                    h[y * Res + x] = ValueOf(kind, (x + 0.5f) / Res, v);
            }

            // 第二遍：按梯度做定向着色。光从左上方来（-u, +v），
            // 于是每一道缝、每一块砖的边缘都会一侧亮一侧暗——平面因此有了厚度。
            float relief = Relief(kind);
            var px = new Color32[Res * Res];
            for (int y = 0; y < Res; y++)
                for (int x = 0; x < Res; x++)
                {
                    int i = y * Res + x;
                    float du = h[y * Res + Wrap(x + 1)] - h[y * Res + Wrap(x - 1)];
                    float dv = h[Wrap(y + 1) * Res + x] - h[Wrap(y - 1) * Res + x];
                    float shade = 1f + (dv - du) * relief;
                    byte b = (byte)(Mathf.Clamp01(h[i] * shade) * 255f);
                    px[i] = new Color32(b, b, b, 255);
                }

            var tex = new Texture2D(Res, Res, TextureFormat.RGBA32, true, false)
            {
                name = "ProcTex_" + kind,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8
            };
            tex.SetPixels32(px);
            // 传第二个参数释放 CPU 端副本：8 张 512² 各 1MB，留着没有任何用处。
            // 不走 Texture2D.Compress——运行时压缩对灰度细节图的收益抵不上它的不确定性。
            tex.Apply(true, true);
            _cache[kind] = tex;
            return tex;
        }

        static int Wrap(int i) => ((i % Res) + Res) % Res;

        // ===== 每类别的高度/明度场。值域要真的拉开，否则贴不贴图都是一块纯色 =====

        static float ValueOf(SurfaceKind k, float u, float v)
        {
            switch (k)
            {
                case SurfaceKind.Plaster:  return Plaster(u, v);
                case SurfaceKind.Concrete: return Concrete(u, v);
                case SurfaceKind.Wood:     return Wood(u, v);
                case SurfaceKind.Metal:    return Metal(u, v);
                case SurfaceKind.Ground:   return Ground(u, v);
                case SurfaceKind.Fabric:   return Fabric(u, v);
                case SurfaceKind.Brick:    return Brick(u, v);
                default:                   return 1f;
            }
        }

        /// <summary>抹灰墙：批刀留下的斜向刮痕 + 沿墙面缓慢起伏的脏色。</summary>
        static float Plaster(float u, float v)
        {
            float trowel = TN(u + v * 0.35f, v, 9, 5, 11);      // 斜向刮痕
            float stain = Fbm(u, v, 13);                         // 大尺度污渍
            float val = 0.88f + (trowel - 0.5f) * 0.30f + (stain - 0.5f) * 0.20f;
            // 零星的鼓包/剥落
            float chip = TN(u, v, 24, 24, 29);
            if (chip > 0.90f) val -= (chip - 0.90f) * 3.0f;
            return val;
        }

        /// <summary>混凝土：模板留下的分格缝 + 气孔麻点。分格缝是它最好认的特征。</summary>
        static float Concrete(float u, float v)
        {
            float baseN = 0.80f + (Fbm(u, v, 23) - 0.5f) * 0.34f;
            // 模板分格：横竖各 4 格的浅凹缝
            float gu = Mathf.Repeat(u * 4f, 1f), gv = Mathf.Repeat(v * 4f, 1f);
            float joint = Mathf.Min(Mathf.Min(gu, 1f - gu), Mathf.Min(gv, 1f - gv));
            if (joint < 0.018f) baseN -= (0.018f - joint) * 9f;
            // 气孔
            float pit = TN(u, v, 56, 56, 71);
            if (pit > 0.90f) baseN -= (pit - 0.90f) * 5.5f;
            return baseN;
        }

        /// <summary>木：沿板长的木纹 + 压暗的板缝 + 每块板各自的深浅。</summary>
        static float Wood(float u, float v)
        {
            const float Planks = 5f;
            float pv = v * Planks;
            int plank = Mathf.FloorToInt(pv);
            float fv = pv - plank;
            // 每块板一个自己的底色，木地板才不会是一整片同色
            float tone = 0.86f + (Vlat(plank, 0, (int)Planks, 1, 97) - 0.5f) * 0.22f;
            // 木纹沿 u 拉长（各向异性）
            float grain = TN(u, v * 3f, 4, 40, 41) * 0.55f + TN(u, v * 3f, 2, 88, 42) * 0.45f;
            float val = tone + (grain - 0.5f) * 0.30f;
            // 板缝：比上一版深得多，浮雕才咬得住
            float seam = Mathf.Min(fv, 1f - fv);
            if (seam < 0.022f) val -= (0.022f - seam) * 13f;
            return val;
        }

        /// <summary>金属：竖向拉丝 + 分块钣金缝 + 一排铆钉。</summary>
        static float Metal(float u, float v)
        {
            float brush = TN(u, v, 3, 140, 51);
            float val = 0.84f + (brush - 0.5f) * 0.16f;
            // 钣金缝：每 3 格一道竖缝
            float su = Mathf.Repeat(u * 3f, 1f);
            float seam = Mathf.Min(su, 1f - su);
            if (seam < 0.012f) val -= (0.012f - seam) * 14f;
            // 铆钉：沿缝两侧的小凸点
            float rv = Mathf.Repeat(v * 12f, 1f);
            if (seam < 0.055f && seam > 0.02f && rv > 0.36f && rv < 0.64f)
                val += 0.12f;
            return val;
        }

        /// <summary>沥青/土地：粗骨料 + 砂砾暗点。</summary>
        static float Ground(float u, float v)
        {
            float coarse = 0.66f + (Fbm(u, v, 31) - 0.5f) * 0.34f;
            float grit = TN(u, v, 80, 80, 88);
            if (grit > 0.84f) coarse -= (grit - 0.84f) * 2.6f;
            if (grit < 0.10f) coarse += (0.10f - grit) * 1.8f;
            return coarse;
        }

        /// <summary>织物/地毯：经纬编织 + 起绒的不匀。</summary>
        static float Fabric(float u, float v)
        {
            float weave = 0.5f + 0.5f * (Mathf.Sin(u * Mathf.PI * 2f * 40f) *
                                          Mathf.Sin(v * Mathf.PI * 2f * 40f));
            float n = Fbm(u, v, 61);
            return 0.82f + (weave - 0.5f) * 0.22f + (n - 0.5f) * 0.14f;
        }

        /// <summary>砖：错缝排布 + 每块砖各自的窑变色 + 压暗的灰浆缝。</summary>
        static float Brick(float u, float v)
        {
            const int Rows = 8, Bricks = 4;
            float ry = v * Rows;
            int row = Mathf.FloorToInt(ry);
            float fy = ry - row;
            float offset = (row % 2 == 0) ? 0f : 0.5f;
            float rx = Mathf.Repeat(u * Bricks + offset, 1f);
            int col = Mathf.FloorToInt(u * Bricks + offset);

            float mortar = Mathf.Min(Mathf.Min(fy, 1f - fy), Mathf.Min(rx, 1f - rx));
            if (mortar < 0.055f) return 0.58f + Fbm(u, v, 19) * 0.08f;   // 灰浆缝

            // 每块砖一个自己的色（窑变），砖墙才不是一张重复的图案
            float kiln = Vlat(col, row, Bricks, Rows, 83);
            return 0.78f + (kiln - 0.5f) * 0.26f + (Fbm(u, v, 17) - 0.5f) * 0.14f;
        }

        // ===== 可无缝平铺的值噪声（整数格子取模回绕）=====

        static float Fbm(float u, float v, int seed)
        {
            float n = 0f, amp = 0.5f; int cells = 8;
            for (int o = 0; o < 4; o++)
            {
                n += amp * TN(u, v, cells, cells, seed + o * 17);
                amp *= 0.5f; cells *= 2;
            }
            return n; // ≈ 0..1
        }

        static float TN(float u, float v, int cx, int cy, int seed)
        {
            float x = u * cx, y = v * cy;
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            float sx = fx * fx * (3f - 2f * fx), sy = fy * fy * (3f - 2f * fy);
            float a = Vlat(xi, yi, cx, cy, seed);
            float b = Vlat(xi + 1, yi, cx, cy, seed);
            float c = Vlat(xi, yi + 1, cx, cy, seed);
            float d = Vlat(xi + 1, yi + 1, cx, cy, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy);
        }

        static float Vlat(int xi, int yi, int cx, int cy, int seed)
        {
            xi = ((xi % cx) + cx) % cx;   // 回绕保证平铺无缝
            yi = ((yi % cy) + cy) % cy;
            uint h = (uint)(xi * 374761393 + yi * 668265263 + seed * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xffffffu) / (float)0xffffffu;
        }
    }
}
