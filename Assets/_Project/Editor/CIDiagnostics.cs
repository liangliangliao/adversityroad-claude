using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using AdversityRoad.Player;

namespace AdversityRoad.EditorTools
{
    /// <summary>
    /// CI 资产诊断：在 CI 里加载武器/背包资源，把【运行时真实数据】（导入后的节点名、
    /// 网格顶点数、包围盒、轴向实测、剑鞘识别与装配核对）打进构建日志——
    /// 用于远程排查"导入后层级/坐标与源文件不一致"这类只有运行时才能看到的问题，
    /// 不再依赖真机截图往返。由 workflow 的 diagnoseAssets 作业以 buildMethod 调用。
    /// </summary>
    public static class CIDiagnostics
    {
        /// <summary>
        /// 角色材质与贴图的真实状态。
        ///
        /// 玩家反复反馈"模型放进游戏就失真、颜色变质"，而我两轮修的都是**导入参数**
        /// （sRGB / 法线类型 / 压缩），两轮都"几乎没效果"。继续拍脑袋没有意义：
        /// 角色模型是直接从 FBX 实例化的，材质全程没有被代码碰过，所以真相只在
        /// 三个地方——用的什么 shader、哪几个贴图槽真的被赋值了、贴图进来之后
        /// 是什么格式/尺寸/色彩空间。把这三样打进 CI 日志，一次就能定死。
        ///
        /// 尤其要看 _BumpMap：如果它压根是空的，那我把法线图改成 NormalMap 类型
        /// 当然"没效果"——因为那张图根本没被材质用上，问题在 FBX 的材质描述里。
        /// </summary>
        static void DiagCharacterMaterials(StringBuilder sb)
        {
            sb.Append("\n----- [CIDIAG][材质] 角色模型的材质与贴图 -----\n");
            foreach (var name in new[] { "PlayerModel", "EnemyModel" })
            {
                var prefab = Resources.Load<GameObject>("Characters/" + name);
                if (prefab == null)
                {
                    sb.Append("[CIDIAG][材质] 没有 Characters/").Append(name).Append('\n');
                    continue;
                }
                sb.Append("[CIDIAG][材质] === ").Append(name).Append(" ===\n");
                foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null) { sb.Append("  渲染器 ").Append(r.name).Append(" 有空材质\n"); continue; }
                        sb.Append("  材质 ").Append(m.name)
                          .Append("  shader=").Append(m.shader != null ? m.shader.name : "null").Append('\n');
                        foreach (var prop in new[] { "_BaseMap", "_MainTex", "_BumpMap",
                                                     "_MetallicGlossMap", "_SpecGlossMap", "_OcclusionMap" })
                        {
                            if (!m.HasProperty(prop)) continue;
                            var t = m.GetTexture(prop) as Texture2D;
                            sb.Append("    ").Append(prop).Append(" = ")
                              .Append(t == null ? "（空）" : t.name);
                            if (t != null)
                                sb.Append("  ").Append(t.width).Append('x').Append(t.height)
                                  .Append("  格式=").Append(t.format)
                                  .Append("  mip=").Append(t.mipmapCount);
                            sb.Append('\n');
                        }
                        if (m.HasProperty("_BaseColor"))
                            sb.Append("    _BaseColor = ").Append(m.GetColor("_BaseColor")).Append('\n');
                    }
                }
            }
            // 接线之后是什么样：在 CI 里实例化一份、跑一遍运行时的 WireSpecularMaps，
            // 把结果打出来。这样"高光图有没有真的接上"在**构建阶段**就能确认，
            // 不必再让玩家装包看一眼再回来告诉我——这一条我已经空跑两轮了。
            sb.Append("[CIDIAG][材质] --- 运行时接线后（WireSpecularMaps）---\n");
            foreach (var name in new[] { "PlayerModel", "EnemyModel" })
            {
                var prefab = Resources.Load<GameObject>("Characters/" + name);
                if (prefab == null) continue;
                var inst = Object.Instantiate(prefab);
                try
                {
                    Combat.MecanimCharacter.WireSpecularMaps(inst);
                    foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                        foreach (var m in r.sharedMaterials)
                        {
                            if (m == null || !m.HasProperty("_SpecGlossMap")) continue;
                            var t = m.GetTexture("_SpecGlossMap");
                            sb.Append("  ").Append(name).Append('/').Append(m.name)
                              .Append("  _SpecGlossMap=").Append(t == null ? "（仍为空）" : t.name)
                              .Append("  高光工作流=")
                              .Append(m.IsKeywordEnabled("_SPECULAR_SETUP") ? "开" : "关")
                              .Append('\n');
                        }
                }
                finally { Object.DestroyImmediate(inst); }
            }

            // 贴图自身的导入结果（.meta 提交之后应当与我们写进去的一致）
            sb.Append("[CIDIAG][材质] --- 贴图导入结果 ---\n");
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/_Project/Resources/Characters" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (ti == null || tex == null) continue;
                sb.Append("  ").Append(System.IO.Path.GetFileName(path))
                  .Append("  类型=").Append(ti.textureType)
                  .Append("  sRGB=").Append(ti.sRGBTexture ? "是" : "否")
                  .Append("  上限=").Append(ti.maxTextureSize)
                  .Append("  实际=").Append(tex.width).Append('x').Append(tex.height)
                  .Append("  格式=").Append(tex.format).Append('\n');
            }
        }

        public static void Run()
        {
            var sb = new StringBuilder("\n===== [CIDIAG] 资产运行时诊断开始 =====\n");
            int exit = 0;
            try
            {
                DiagWeapon(sb, "scene");
                DiagBackpacks(sb);
                DiagLocomotion(sb);
                DiagCharacterMaterials(sb);
            }
            catch (System.Exception e)
            {
                sb.Append("[CIDIAG][EXC] ").Append(e).Append('\n');
                exit = 1;
            }
            sb.Append("===== [CIDIAG] 诊断结束 =====\n");
            Debug.Log(sb.ToString());
            EditorApplication.Exit(exit);
        }

        // ---------- 方向移动片段（后退/横移/斜向是否真的接上了） ----------
        //
        // 这一项查的是"看不见的失败"：片段没被找到、方向测反了、自然速度离谱——
        // 三种都不会报错，只会在真机上表现成"横移时脚在原地倒腾"。
        // 与其等截图往返，不如让 CI 每次构建都把这张表打出来。
        static void DiagLocomotion(StringBuilder sb)
        {
            var prefab = Resources.Load<GameObject>("Characters/PlayerModel");
            if (prefab == null)
            {
                sb.Append("[CIDIAG][移动] 没有 Characters/PlayerModel，跳过\n");
                return;
            }
            var model = Object.Instantiate(prefab);
            try
            {
                var animator = model.GetComponentInChildren<Animator>();
                if (animator == null) animator = model.AddComponent<Animator>();
                var pa = new AdversityRoad.Combat.PlayableAnimator(animator);
                sb.Append("[CIDIAG][移动] 动作库有效=").Append(pa.Valid ? "是" : "否").Append('\n');
                foreach (var line in pa.DescribeDirectionalSet().Split('\n'))
                    if (line.Length > 0) sb.Append("[CIDIAG][移动] ").Append(line).Append('\n');
                foreach (var line in pa.DescribeActionSet().Split('\n'))
                    if (line.Length > 0) sb.Append("[CIDIAG][招式] ").Append(line).Append('\n');
                pa.Destroy();
            }
            catch (System.Exception e)
            {
                // 诊断本身出问题不该让构建失败——它是来报信的，不是把关的
                sb.Append("[CIDIAG][移动] 诊断异常（不影响构建）：").Append(e.Message).Append('\n');
            }
            finally
            {
                Object.DestroyImmediate(model);
            }
        }

        // ---------- 武器（带鞘套件） ----------
        static void DiagWeapon(StringBuilder sb, string name)
        {
            GameObject prefab = null;
            var all = Resources.LoadAll<GameObject>("Characters/Weapons");
            sb.Append("[CIDIAG][武器] 武器库共 ").Append(all.Length).Append(" 个预制体：");
            foreach (var p in all) sb.Append(p != null ? p.name : "null").Append("、");
            sb.Append('\n');
            foreach (var p in all)
                if (p != null && p.name == name) { prefab = p; break; }
            if (prefab == null) { sb.Append("[CIDIAG][武器] 未找到 ").Append(name).Append('\n'); return; }

            var w = Object.Instantiate(prefab);
            try
            {
                sb.Append("[CIDIAG][武器] ").Append(name).Append(" 实例化层级：\n");
                Dump(sb, w.transform, 0);

                var byName = PlayerAppearance.FindDeep(w.transform, "scabbard")
                    ?? PlayerAppearance.FindDeep(w.transform, "sheath")
                    ?? PlayerAppearance.FindDeep(w.transform, "鞘");
                sb.Append("[CIDIAG][武器] 按名识别剑鞘：").Append(byName != null ? byName.name : "落空").Append('\n');

                // 细长件候选表（与 DetectScabbardByGeometry 同口径，便于核对阈值）
                sb.Append("[CIDIAG][武器] 网格候选表（长度/长径比/顶点）：\n");
                foreach (var mf in w.GetComponentsInChildren<MeshFilter>(true))
                {
                    var m = mf.sharedMesh; if (m == null) continue;
                    Bounds mb = m.bounds;
                    int ax = mb.size.x >= mb.size.y && mb.size.x >= mb.size.z ? 0 : mb.size.y >= mb.size.z ? 1 : 2;
                    float second = 0f;
                    for (int i = 0; i < 3; i++) if (i != ax) second = Mathf.Max(second, mb.size[i]);
                    Vector3 eA = mb.center, eB = mb.center;
                    eA[ax] = mb.min[ax]; eB[ax] = mb.max[ax];
                    float len = (mf.transform.TransformPoint(eB) - mf.transform.TransformPoint(eA)).magnitude;
                    sb.Append("    ").Append(Path(mf.transform, w.transform))
                      .Append(" 长=").Append(len.ToString("F3"))
                      .Append(" 长径比=").Append((second > 1e-6f ? mb.size[ax] / second : 999f).ToString("F1"))
                      .Append(" 顶点=").Append(m.vertexCount).Append('\n');
                }

                var geo = PlayerAppearance.DetectScabbardByGeometry(w.transform);
                sb.Append("[CIDIAG][武器] 几何识别剑鞘：").Append(geo != null ? Path(geo, w.transform) : "落空").Append('\n');

                var scab = byName != null ? byName : geo;
                if (scab == null) { sb.Append("[CIDIAG][武器] 无剑鞘可用，诊断到此\n"); return; }

                PlayerAppearance.AdoptScabbardAccessories(w.transform, scab);
                var parts = PlayerAppearance.BladeParts(w.transform, scab);
                sb.Append("[CIDIAG][武器] 剑身部件 ").Append(parts.Count).Append(" 件：");
                foreach (var p in parts) sb.Append(Path(p, w.transform)).Append("、");
                sb.Append('\n');
                if (parts.Count == 0) return;

                // 复演装配核心并做世界空间核对（与 SetupSheathedWeapon 同公式，含组轴归正）
                PlayerAppearance.LocalBounds(scab, out Bounds sbnd);
                var blade = new GameObject("BladeGroup").transform;
                blade.SetParent(scab, false);
                blade.localPosition = Vector3.zero; blade.localRotation = Quaternion.identity; blade.localScale = Vector3.one;
                Transform mainT = null; Mesh mainMesh = null; int mainV = 0;
                foreach (var p in parts)
                {
                    Mesh mm = null;
                    var mf2 = p.GetComponent<MeshFilter>();
                    if (mf2 != null) mm = mf2.sharedMesh;
                    if (mm == null)
                    {
                        var sm2 = p.GetComponent<SkinnedMeshRenderer>();
                        if (sm2 != null) mm = sm2.sharedMesh;
                    }
                    if (mm != null && mm.vertexCount > mainV) { mainV = mm.vertexCount; mainT = p; mainMesh = mm; }
                }
                if (mainT != null)
                {
                    Bounds mmb = mainMesh.bounds;
                    PlayerAppearance.LongAxisEnds(mmb, out Vector3 m0, out Vector3 m1);
                    Vector3 axW = mainT.TransformPoint(m1) - mainT.TransformPoint(m0);
                    if (axW.sqrMagnitude > 1e-10f)
                        blade.rotation = Quaternion.FromToRotation(blade.up, axW.normalized) * blade.rotation;
                }
                foreach (var p in parts) p.SetParent(blade, true);
                PlayerAppearance.LocalBounds(blade, out Bounds bbnd);
                PlayerAppearance.LongAxisEnds(bbnd, out Vector3 ba0, out Vector3 ba1);
                PlayerAppearance.LongAxisEnds(sbnd, out Vector3 sa0, out Vector3 sa1);
                int og = PlayerAppearance.GripEndByModelOrigin(blade, ba0, ba1);
                bool gripAtA = og != 1;   // 诊断口径：0/-1 视为 endA（真实代码另有截面/标记兜底）
                Vector3 gripL = gripAtA ? ba0 : ba1, tipL = gripAtA ? ba1 : ba0;
                Vector3 bDir = tipL - gripL, sDir = sa0 - sa1;
                float bladeLen = bDir.magnitude, scabLen = sDir.magnitude;
                sb.Append("[CIDIAG][武器] 原点判柄=").Append(og)
                  .Append(" 剑长=").Append(bladeLen.ToString("F3"))
                  .Append(" 鞘长=").Append(scabLen.ToString("F3")).Append('\n');
                if (bladeLen > 1e-4f && scabLen > 1e-4f)
                {
                    bDir /= bladeLen; sDir /= scabLen;
                    Quaternion q = Quaternion.FromToRotation(bDir, sDir);
                    Vector3 sLP = (sa0 - sDir * (scabLen * 0.02f)) - (q * tipL);
                    blade.localRotation = q; blade.localPosition = sLP;
                    Vector3 dCtr = blade.TransformPoint(bbnd.center) - scab.TransformPoint(sbnd.center);
                    sb.Append("[CIDIAG][武器] 装配后剑心-鞘心偏差=")
                      .Append(dCtr.magnitude.ToString("F4"))
                      .Append("（鞘长的 ").Append((dCtr.magnitude / scabLen).ToString("F2")).Append("）")
                      .Append(dCtr.magnitude < scabLen * 0.35f ? " ✔入鞘\n" : " ✘偏离\n");
                }
            }
            finally { Object.DestroyImmediate(w); }
        }

        // ---------- 背包 ----------
        static void DiagBackpacks(StringBuilder sb)
        {
            var all = Resources.LoadAll<GameObject>("Characters/Backpacks");
            sb.Append("[CIDIAG][背包] 背包库共 ").Append(all.Length).Append(" 个预制体\n");
            foreach (var prefab in all)
            {
                if (prefab == null) continue;
                // 与运行时同构：包装父节点（模型根可能自带轴向旋转+非均匀缩放，
                // 必须经由子节点 TRS 测量真实视觉几何）
                var holder = new GameObject("BackpackHolder").transform;
                var bp = Object.Instantiate(prefab, holder, false);
                try
                {
                    sb.Append("[CIDIAG][背包] ").Append(prefab.name).Append(" 层级：\n");
                    Dump(sb, holder, 0);
                    if (PlayerAppearance.LocalBounds(holder, out Bounds lb))
                        sb.Append("[CIDIAG][背包] 包装节点空间包围盒 size=").Append(lb.size.ToString("F3"))
                          .Append(" center=").Append(lb.center.ToString("F3")).Append('\n');
                    foreach (var mf in holder.GetComponentsInChildren<MeshFilter>(true))
                        if (mf.sharedMesh != null)
                            sb.Append("    网格 ").Append(Path(mf.transform, holder))
                              .Append(" 顶点=").Append(mf.sharedMesh.vertexCount)
                              .Append(" 可读=").Append(mf.sharedMesh.isReadable).Append('\n');
                    bool ok = PlayerAppearance.TryMeasureBackpack(holder,
                        out int thin, out int big, out int strapSign);
                    sb.Append("[CIDIAG][背包] 实测（包装节点空间=视觉几何）：成功=").Append(ok)
                      .Append(" 高轴=").Append("XYZ"[big])
                      .Append(" 厚轴=").Append("XYZ"[thin])
                      .Append(" 肩带朝=").Append(strapSign > 0 ? "+" : "-").Append("XYZ"[thin]).Append('\n');
                }
                finally { Object.DestroyImmediate(holder.gameObject); }
            }
        }

        // ---------- 工具 ----------
        static void Dump(StringBuilder sb, Transform t, int depth)
        {
            sb.Append("    ");
            for (int i = 0; i < depth; i++) sb.Append("  ");
            sb.Append(t.name);
            var mf = t.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
                sb.Append(" [mesh v=").Append(mf.sharedMesh.vertexCount)
                  .Append(" b=").Append(mf.sharedMesh.bounds.size.ToString("F2")).Append(']');
            var smr = t.GetComponent<SkinnedMeshRenderer>();
            if (smr != null && smr.sharedMesh != null)
                sb.Append(" [skin v=").Append(smr.sharedMesh.vertexCount).Append(']');
            sb.Append(" P=").Append(t.localPosition.ToString("F2"))
              .Append(" R=").Append(t.localRotation.eulerAngles.ToString("F0"))
              .Append(" S=").Append(t.localScale.ToString("F2")).Append('\n');
            for (int i = 0; i < t.childCount; i++) Dump(sb, t.GetChild(i), depth + 1);
        }

        static string Path(Transform t, Transform root)
        {
            var s = t.name;
            for (var p = t.parent; p != null && p != root; p = p.parent) s = p.name + "/" + s;
            return s;
        }
    }
}
