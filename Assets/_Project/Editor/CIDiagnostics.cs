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
            // 【必须把 PlayerModel2 一起打出来】角色·贰是 Avaturn 导出的 .glb，
            // 材质由 glTFast 生成、贴图**内嵌在文件里**——和 maria/Paladin 那套
            // "FBX + 独立 PNG" 完全是两条导入路径。我前几轮修的 sRGB / 法线类型 /
            // ASTC / 高光接线全都作用在后者上，一条都碰不到 .glb，
            // 而玩家拿来对比原图的正是角色·贰。诊断漏了它，就等于一直在看错对象。
            foreach (var name in new[] { "PlayerModel", "PlayerModel2", "EnemyModel" })
            {
                var prefab = Resources.Load<GameObject>("Characters/" + name);
                if (prefab == null)
                {
                    sb.Append("[CIDIAG][材质] 没有 Characters/").Append(name)
                      .Append("（.glb 若没被 glTFast 接管，这里就是 null，角色会回退到壹）\n");
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
                        // 【属性名必须两套都探】上一版只探 URP/Lit 的 _BaseMap/_BumpMap…，
                        // 于是角色·贰（Avaturn 的 .glb，走 glTFast 的
                        // Shader Graphs/glTF-pbrMetallicRoughness）一个槽都没打印出来——
                        // 不是槽是空的，是**属性名压根对不上**，HasProperty 全 false。
                        // 这同时说明我前几轮针对 URP/Lit 属性名做的材质改动
                        //（高光接线等）在角色·贰身上是彻底的空操作。
                        foreach (var prop in new[] { "_BaseMap", "_MainTex", "_BumpMap",
                                                     "_MetallicGlossMap", "_SpecGlossMap", "_OcclusionMap",
                                                     "baseColorTexture", "normalTexture",
                                                     "metallicRoughnessTexture", "occlusionTexture",
                                                     "emissiveTexture" })
                        {
                            if (!m.HasProperty(prop)) continue;
                            var t = m.GetTexture(prop) as Texture2D;
                            sb.Append("    ").Append(prop).Append(" = ")
                              .Append(t == null ? "（空）" : t.name);
                            if (t != null)
                                sb.Append("  ").Append(t.width).Append('x').Append(t.height)
                                  .Append("  格式=").Append(t.format)
                                  // graphicsFormat 才看得出色彩空间（…_SRGB 结尾＝sRGB）。
                                  // 法线/粗糙度/AO 若是 sRGB 就是错的——但那要先看清楚，
                                  // 不能顺手改：改错色彩空间比缺 mip 更毁颜色。
                                  .Append("  GPU格式=").Append(t.graphicsFormat)
                                  .Append("  mip=").Append(t.mipmapCount);
                            sb.Append('\n');
                        }
                        foreach (var c in new[] { "_BaseColor", "baseColorFactor" })
                            if (m.HasProperty(c))
                                sb.Append("    ").Append(c).Append(" = ").Append(m.GetColor(c)).Append('\n');
                        foreach (var f in new[] { "_Metallic", "_Smoothness",
                                                  "metallicFactor", "roughnessFactor" })
                            if (m.HasProperty(f))
                                sb.Append("    ").Append(f).Append(" = ")
                                  .Append(m.GetFloat(f).ToString("F2")).Append('\n');
                    }
                }
            }
            // 接线之后是什么样：在 CI 里实例化一份、跑一遍运行时的 WireSpecularMaps，
            // 把结果打出来。这样"高光图有没有真的接上"在**构建阶段**就能确认，
            // 不必再让玩家装包看一眼再回来告诉我——这一条我已经空跑两轮了。
            sb.Append("[CIDIAG][材质] --- 运行时接线后（WireSpecularMaps）---\n");
            foreach (var name in new[] { "PlayerModel", "PlayerModel2", "EnemyModel" })
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
            // mip 链补完之后是什么样：这一项直接回答玩家的"分辨率被降低了"。
            // 角色·贰进来时 mip=1（诊断已证），缩小采样必然糊；补完应当是 mip=11。
            sb.Append("[CIDIAG][材质] --- 补 mip 链之后（TextureFidelity）---\n");
            foreach (var name in new[] { "PlayerModel2" })
            {
                var prefab = Resources.Load<GameObject>("Characters/" + name);
                if (prefab == null) continue;
                var inst = Object.Instantiate(prefab);
                try
                {
                    Combat.TextureFidelity.EnsureMipmaps(inst);
                    foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                        foreach (var m in r.sharedMaterials)
                        {
                            if (m == null || m.shader == null) continue;
                            int pc = m.shader.GetPropertyCount();
                            for (int i = 0; i < pc; i++)
                            {
                                if (m.shader.GetPropertyType(i) !=
                                    UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
                                var t = m.GetTexture(m.shader.GetPropertyName(i)) as Texture2D;
                                if (t == null) continue;
                                sb.Append("  ").Append(m.name).Append('/')
                                  .Append(m.shader.GetPropertyName(i)).Append(" = ")
                                  .Append(t.name).Append("  mip=").Append(t.mipmapCount)
                                  .Append("  格式=").Append(t.format).Append('\n');
                            }
                        }
                }
                finally { Object.DestroyImmediate(inst); }
            }

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

        /// <summary>
        /// UAL 烘焙产物核对。这一段是必要的：整套重定向在没有 Unity 的机器上写不了、
        /// 也验不了，唯一能给出真实结果的地方就是这里。把"清单要几个 / 实际烤出几个 /
        /// 能不能按名字取到"打进日志，对不上一眼就能看见，而不是等装到手机上发现人不动。
        /// </summary>
        /// <summary>
        /// 战斗平衡：一套完整连段能打掉多少血，各档敌人要挨几套才倒。
        ///
        /// 【为什么这张表必须由 CI 打】敌人生命那张表是在
        /// GameDebug.TankyEnemies 默认为 true（全工程伤害 ×0.1）的年代写的，
        /// 从来没有在真实伤害下被算过一次。把那个调试开关关掉之后，
        /// "敌人很容易就被打死"是必然结果，而这件事**看代码看不出来**：
        /// 伤害在四个文件里被乘了六道（招式倍率、防御减免、部位、破绽、连携、耐揍），
        /// 生命在第七个文件里被乘了两道（档位、全局调校）。
        /// 靠手感回忆去调这种数只会来回摆，所以让构建每次都把结论打出来。
        /// </summary>
        static void DiagBalance(StringBuilder sb)
        {
            AdversityRoad.Combat.PlayerCombatController.ComboTotals(true, out float swMult, out float swSec);
            AdversityRoad.Combat.PlayerCombatController.ComboTotals(false, out float puMult, out float puSec);
            float bd = AdversityRoad.Combat.PlayerCombatController.DefaultBaseDamage;
            sb.Append("[CIDIAG][平衡] 基础伤害 ").Append(bd)
              .Append("；剑连 ").Append(swMult.ToString("0.00")).Append(" 倍 / ")
              .Append(swSec.ToString("0.00")).Append(" 秒（链取消），拳连 ")
              .Append(puMult.ToString("0.00")).Append(" 倍 / ")
              .Append(puSec.ToString("0.00")).Append(" 秒\n");
            if (AdversityRoad.Core.GameDebug.TankyEnemies)
                sb.Append("[CIDIAG][平衡] !! GameDebug.TankyEnemies 默认是 true —— ")
                  .Append("全工程每个敌人受到的伤害都会被乘 ")
                  .Append(AdversityRoad.Core.GameDebug.TankyDamageScale)
                  .Append("，这是调试开关，不该进发布包\n");
            var tiers = new[]
            {
                AdversityRoad.AI.EnemyTier.Novice, AdversityRoad.AI.EnemyTier.Standard,
                AdversityRoad.AI.EnemyTier.Elite,  AdversityRoad.AI.EnemyTier.Chief,
            };
            // 取一个"标准杂兵"作代表：目录里生命 100 / 防御 8 的那一档。
            // 逐档只看档位换算，不逐个敌人列——四十多种敌人列出来没人会读，
            // 而各档的换算是同一套，代表值就足够看出量级。
            foreach (var t in tiers)
            {
                var pf = AdversityRoad.AI.EnemyCatalog.Create(
                    AdversityRoad.AI.EnemyType.CoughAssassin, t);   // 目录里的 100 血 / 8 防
                float perHit = bd * 1.10f * (100f / (100f + pf.defense));      // 单下轻斩（巨剑横斩）
                float perCombo = bd * swMult * (100f / (100f + pf.defense));   // 一整套剑连
                float combos = perCombo > 0.01f ? pf.maxHealth / perCombo : 0f;
                sb.Append("[CIDIAG][平衡]   ")
                  .Append(AdversityRoad.AI.EnemyCatalog.TierLabel(t).PadRight(4))
                  .Append(" 生命=").Append(pf.maxHealth.ToString("0"))
                  .Append(" 防御=").Append(pf.defense.ToString("0.0"))
                  .Append("  单下轻斩=").Append(perHit.ToString("0.0"))
                  .Append("（血条 ").Append((perHit / Mathf.Max(1f, pf.maxHealth) * 100f).ToString("0.0")).Append("%）")
                  .Append("  打倒需 ").Append(combos.ToString("0.0")).Append(" 套剑连 ≈ ")
                  .Append((combos * swSec).ToString("0.0")).Append(" 秒不间断输出\n");
            }
            // 【最重要的一段】上表只算轻连段——而实机里收人头的从来不是连段。
            // 绝招 ×10、必杀 ×16~21，攒势又只要几次命中，这才是真实的击杀路径。
            // 上一版没有这一段，于是那份"标准杂兵要挨两套连招"的报告是好看的、错的。
            AdversityRoad.Combat.PlayerCombatController.PeakMultipliers(
                out float spMult, out float ultMult);
            int hpm = AdversityRoad.Combat.PlayerCombatController.HitsPerMomentum;
            sb.Append("[CIDIAG][平衡] 爆发路径：绝招 ").Append(spMult.ToString("0.#"))
              .Append(" 倍（1 势）、必杀 ").Append(ultMult.ToString("0.#"))
              .Append(" 倍（3 势）；每 ").Append(hpm).Append(" 次命中攒 1 势，")
              .Append("攒满必杀需 ").Append(hpm * 3).Append(" 次命中 ≈ ")
              .Append((hpm * 3f / 4f).ToString("0.#")).Append(" 套剑连\n");
            foreach (var t in tiers)
            {
                var pf = AdversityRoad.AI.EnemyCatalog.Create(
                    AdversityRoad.AI.EnemyType.CoughAssassin, t);
                float hp = pf.maxHealth * Mathf.Clamp(AdversityRoad.Core.GameDebug.EnemyToughness, 0.25f, 12f);
                float red = 100f / (100f + pf.defense);
                float sp = bd * spMult * red, ult = bd * ultMult * red;
                sb.Append("[CIDIAG][平衡]   ")
                  .Append(AdversityRoad.AI.EnemyCatalog.TierLabel(t).PadRight(4))
                  .Append(" 强度×").Append(AdversityRoad.Core.GameDebug.EnemyToughness.ToString("0.#"))
                  .Append(" 后生命=").Append(hp.ToString("0"))
                  .Append("  一记绝招=").Append(sp.ToString("0"))
                  .Append("（").Append((sp / Mathf.Max(1f, hp) * 100f).ToString("0")).Append("%）")
                  .Append("  一套必杀=").Append(ult.ToString("0"))
                  .Append("（").Append((ult / Mathf.Max(1f, hp) * 100f).ToString("0")).Append("%）");
                // 标记用 ※ 而不是 !!：!! 会让作业变红（见 Run 里的判定），
                // 而"必杀能一口气秒掉低档杂兵"本来就是必杀该有的样子，不是缺陷。
                // 需要警觉的是它在**高档**上也成立——那说明爆发把生命曲线整个吃掉了。
                if (ult >= hp) sb.Append("  ※ 一套必杀直接秒杀这一档");
                sb.Append('\n');
            }
            sb.Append("[CIDIAG][平衡] 轻连段那张表是「只用连段、不破防」的上限；")
              .Append("削韧破防后重击处决吃 2.8 倍，爆发路径见上\n");
            // 防连锁硬直的三道闸（见 EnemyController.TakeHit）。这几个数决定
            // "一直追打对方还有没有还手机会"，而那件事在日志里看不见任何伤害异常——
            // 不写出来就只能靠实机反馈，而实机反馈到这里已经绕了一个小时。
            // 受击反应分档：按「打中哪儿 × 轻斩/重击」把实际档位打成一张表。
            // 玩家反馈"踉跄没有按伤害程度区分"，而这件事在代码里只是一个 int，
            // 在日志里不打出来就完全看不见——打出来才对得上实机的感受。
            {
                var parts = new[]
                {
                    AdversityRoad.Combat.BodyPart.Head, AdversityRoad.Combat.BodyPart.Chest,
                    AdversityRoad.Combat.BodyPart.Abdomen, AdversityRoad.Combat.BodyPart.ArmL,
                    AdversityRoad.Combat.BodyPart.LegL, AdversityRoad.Combat.BodyPart.Extremity,
                };
                var std = AdversityRoad.AI.EnemyCatalog.Create(
                    AdversityRoad.AI.EnemyType.CoughAssassin, AdversityRoad.AI.EnemyTier.Standard);
                float stdHp = std.maxHealth * Mathf.Clamp(
                    AdversityRoad.Core.GameDebug.EnemyToughness, 0.25f, 12f);
                float red = 100f / (100f + std.defense);
                foreach (var bp in parts)
                {
                    var pp = AdversityRoad.Combat.BodyPartTable.Get(bp, false);
                    float light = bd * 1.10f * red * pp.damage;          // 巨剑横斩
                    float heavy = bd * 2.40f * red * pp.damage;          // 蓄力跳劈
                    int tl = AdversityRoad.AI.EnemyController.HitReactionTierOf(bp, light, false, stdHp);
                    int th = AdversityRoad.AI.EnemyController.HitReactionTierOf(bp, heavy, true, stdHp);
                    sb.Append("[CIDIAG][平衡]   受击 ").Append(pp.label.PadRight(4))
                      .Append(" 轻斩=").Append(light.ToString("0")).Append(" → ")
                      .Append(AdversityRoad.AI.EnemyController.TierLabel(tl))
                      .Append(' ').Append(AdversityRoad.AI.EnemyController.StaggerSeconds[tl].ToString("0.00")).Append("s")
                      .Append("   重击=").Append(heavy.ToString("0")).Append(" → ")
                      .Append(AdversityRoad.AI.EnemyController.TierLabel(th))
                      .Append(' ').Append(AdversityRoad.AI.EnemyController.StaggerSeconds[th].ToString("0.00")).Append("s")
                      .Append('\n');
                }
            }
            sb.Append("[CIDIAG][平衡] 命中质量：接触体积占比 × 刃位，合计倍率夹在 0.55~1.25")
              .Append("（擦到边／用剑柄怼 vs 刃中段罩满，最差与最好差约 2.3 倍）；")
              .Append("伤害与削韧同乘；投射物与心理攻击不走判定框，不参与\n");
            sb.Append("[CIDIAG][平衡] 防连锁硬直：起身霸体窗 0.90s（普通踉跄 0.45s；")
              .Append("连续 3 次硬直之后保底 1.20s）＋起身反击带霸体（前摇打不断）；")
              .Append("硬直递减窗口 ").Append(AdversityRoad.AI.EnemyController.StaggerChainWindow)
              .Append("s 内每多一次 ×0.72（下限 0.35）、霸体冷却 ×(1+0.45n)；")
              .Append("重击不再免检霸体，只削 0.35s；打断前摇的硬直也走同一套递减\n");
        }

        /// <summary>返回 false 表示这一项不合格，作业要变红。</summary>
        static bool DiagUal(StringBuilder sb)
        {
            bool ok = true;
            sb.Append("\n--- UAL 通用动作库 ---\n");
            const string list = "Assets/_Project/Animations/UAL/UAL_CLIPS.txt";
            int want = 0;
            if (System.IO.File.Exists(list))
            {
                foreach (var raw in System.IO.File.ReadAllLines(list))
                {
                    string t = raw.Trim();
                    if (t.Length > 0 && !t.StartsWith("#")) want++;
                }
            }
            else
            {
                sb.Append("  [警告] 找不到烘焙清单：").Append(list).Append('\n');
            }

            var baked = Resources.LoadAll<AnimationClip>("Characters/AnimsUAL");
            int got = baked != null ? baked.Length : 0;
            sb.Append("  清单 ").Append(want).Append(" 个 / 实际烤出 ")
              .Append(got).Append(" 个\n");
            // 【为什么这里必须让作业变红】上一版只是把"烤出 0 个"打出来，作业照样是绿的，
            // 于是一个"45 个动作全都没生效"的版本一路通过、打成 APK。
            // 诊断查出来的问题不让流程停下来，等于没查。
            if (want > 0 && got < want)
            {
                ok = false;
                sb.Append("  [严重] 烤出的数量少于清单——这些动作在游戏里不会出现。\n");
            }
            if (baked != null)
            {
                int empty = 0;
                foreach (var c in baked)
                {
                    if (c == null) continue;
                    // 长度为 0 或没有任何曲线绑定 = 烤出来是个空壳，运行时表现为"人不动"
                    var binds = UnityEditor.AnimationUtility.GetCurveBindings(c);
                    if (c.length <= 0.001f || binds == null || binds.Length == 0)
                    {
                        empty++;
                        sb.Append("  [空片段] ").Append(c.name).Append(" 时长=")
                          .Append(c.length.ToString("F2")).Append(" 曲线=")
                          .Append(binds == null ? 0 : binds.Length).Append('\n');
                    }
                }
                if (empty == 0 && baked.Length > 0)
                    sb.Append("  全部片段有时长且有曲线绑定\n");

                // 【内容检查，不只是结构】上一版只验了"有几个、多长、有没有曲线、路径对不对"——
                // 33 个静止 T-Pose 把这四项全都满足了，一路绿灯打进 APK，
                // 装到手机上才被玩家看出来。曲线存在不等于姿态会变，必须验数值。
                int frozen = 0;
                foreach (var c in baked)
                {
                    if (c == null) continue;
                    var binds = UnityEditor.AnimationUtility.GetCurveBindings(c);
                    if (binds == null || binds.Length == 0) continue;
                    bool varies = false;
                    foreach (var b in binds)
                    {
                        if (!b.propertyName.StartsWith("m_LocalRotation")) continue;
                        var curve = UnityEditor.AnimationUtility.GetEditorCurve(c, b);
                        if (curve == null || curve.length < 2) continue;
                        float lo = curve.keys[0].value, hi = lo;
                        for (int k = 1; k < curve.length; k++)
                        {
                            float v = curve.keys[k].value;
                            if (v < lo) lo = v;
                            if (v > hi) hi = v;
                        }
                        if (hi - lo > 0.01f) { varies = true; break; }
                    }
                    if (!varies)
                    {
                        frozen++;
                        sb.Append("  [静止片段] ").Append(c.name)
                          .Append("　全程骨骼旋转没有变化——这一段是废的\n");
                    }
                }
                if (frozen > 0)
                {
                    ok = false;
                    sb.Append("  [严重] ").Append(frozen)
                      .Append(" 个片段是静止的。多半是烘焙时 Animator 被剔除没写骨骼" +
                              "（cullingMode），读到的一直是绑定姿势（T-Pose）。\n");
                }
                else if (baked.Length > 0) sb.Append("  全部片段的姿态确实在变化\n");

                // 名字全列出来：下游全部按名字寻址，名字对不上就什么都接不上，
                // 而"接不上"在运行时是静默的。列出来才不用靠猜。
                var names = new System.Collections.Generic.List<string>();
                foreach (var c in baked) if (c != null) names.Add(c.name);
                names.Sort();
                sb.Append("  片段名：").Append(string.Join("、", names)).Append('\n');
                // 抽查一条：曲线的绑定路径必须是 mixamorig 的，否则重定向没生效
                foreach (var c in baked)
                {
                    if (c == null) continue;
                    var binds = UnityEditor.AnimationUtility.GetCurveBindings(c);
                    if (binds == null || binds.Length == 0) continue;
                    sb.Append("  抽查 ").Append(c.name).Append("：首条绑定路径 ")
                      .Append(binds[0].path).Append('\n');
                    if (!binds[0].path.Contains("mixamorig"))
                    {
                        ok = false;
                        sb.Append("  [严重] 绑定路径不是 mixamorig——重定向没生效，" +
                                  "这些片段在角色身上不会动\n");
                    }
                    break;
                }
                if (empty > 0) ok = false;
            }
            return ok;
        }

        public static void Run()
        {
            var sb = new StringBuilder("\n===== [CIDIAG] 资产运行时诊断开始 =====\n");
            int exit = 0;
            try
            {
                // 【烘焙必须排在最前面】DiagLocomotion 会真的建一份 PlayableAnimator
                // 并把招式表、方向移动表打出来——那份表是这份报告里最有价值的东西。
                // 上一版把烘焙放在它**后面**，于是打表时 AnimsUAL 目录还是空的，
                // 报告里看到的是"没有 UAL 参与"的假象：明明加了蹲伏前进和闪避变体，
                // 表里却一点没变，我差点据此去查一个根本不存在的 bug。
                // 诊断要么反映真实状态，要么不如不打。
                UalRetargetBaker.Bake(false);

                DiagWeapon(sb, "scene");
                DiagTrail(sb);
                DiagBackpacks(sb);
                DiagLocomotion(sb);
                DiagCharacterMaterials(sb);
                DiagBalance(sb);
                if (!DiagUal(sb)) exit = 1;
                // 变体池里出现重复片段（DescribeActionSet 自己标的 "!!"）也算红：
                // 「变体×3 里有两条是同一段」看起来是绿的，玩起来是"翻滚从不变化"。
                if (sb.ToString().Contains("!! ")) exit = 1;
            }
            catch (System.Exception e)
            {
                sb.Append("[CIDIAG][EXC] ").Append(e).Append('\n');
                exit = 1;
            }
            sb.Append("===== [CIDIAG] 诊断结束 =====\n");
            Debug.Log(sb.ToString());
            // 【也写一份文件】Unity 的 stdout 里混着几万行资产导入流水，
            // 想从 CI 日志里翻出这段报告要拉几 MB 日志。落成一个纯文本文件，
            // 工作流里 cat 一下即可——同一份内容，读起来是几十行而不是几万行。
            try { System.IO.File.WriteAllText("cidiag.txt", sb.ToString()); }
            catch (System.Exception e) { Debug.LogWarning("[CIDIAG] 报告落盘失败：" + e.Message); }
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
                // 上半身遮罩的分割：上半身 0 根 = 这套"跑动中只写上半身"整个失效
                //（Hips 是全身的根，"下半身"标记一沿层级传播就全军覆没，
                //  而症状是"动作动画没有了"，不报任何错——必须在构建期核对）。
                sb.Append("[CIDIAG][遮罩] ").Append(pa.MaskSplit)
                  .Append("  上半身可用=").Append(pa.MaskUpperCount > 0 ? "是" : "否（坏了）")
                  .Append('\n');
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
        /// <summary>刀光拖尾用的是哪条着色器、什么混合方式。
        /// 玩家报的"所有兵器都有白痕"根因就在这里：Lit + 加色 + 受光照，
        /// 白天场景一叠就饱和成纯白，而且 Lit 不读顶点色、尾端根本不淡出。
        /// 换成无光照 + 顶点色 + 普通 alpha 之后，这一行要能证明真的换过来了。</summary>
        static void DiagTrail(StringBuilder sb)
        {
            var m = Combat.CombatFeedback.TrailMaterial(new Color(0.8f, 0.9f, 1f), 0.6f);
            sb.Append("[CIDIAG][刀光] 着色器=")
              .Append(m != null && m.shader != null ? m.shader.name : "null")
              .Append("  受光照=")
              .Append(m != null && m.shader != null && m.shader.name.Contains("Lit") &&
                      !m.shader.name.Contains("Unlit") ? "是（不对）" : "否")
              .Append("  颜色=").Append(m != null ? m.color.ToString() : "-")
              .Append("  渲染队列=").Append(m != null ? m.renderQueue : -1)
              .Append('\n');
            if (m != null) Object.DestroyImmediate(m);
        }

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
