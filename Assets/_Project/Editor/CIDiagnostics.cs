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
            // 韧性账：一套连段削多少韧 vs 敌人有多少韧。上一轮实机数据显示
            // 破防才是硬直的主要来源（进硬直 受击1/破防2），而这笔账此前没人算过。
            {
                float cp = AdversityRoad.Combat.PlayerCombatController.ComboPosture(true);
                foreach (var t2 in tiers)
                {
                    var pf2 = AdversityRoad.AI.EnemyCatalog.Create(
                        AdversityRoad.AI.EnemyType.CoughAssassin, t2);
                    sb.Append("[CIDIAG][平衡]   韧性 ")
                      .Append(AdversityRoad.AI.EnemyCatalog.TierLabel(t2).PadRight(4))
                      .Append(" 韧性=").Append(pf2.posture.ToString("0"))
                      .Append("  一套剑连削韧=").Append(cp.ToString("0"))
                      .Append("  → ").Append((pf2.posture / Mathf.Max(1f, cp)).ToString("0.0"))
                      .Append(" 套破防一次；脱手 ")
                      .Append(AdversityRoad.AI.EnemyController.PostureCalm)
                      .Append("s 后按每秒 ")
                      .Append((AdversityRoad.AI.EnemyController.PostureRegenPerSec * 100f).ToString("0"))
                      .Append("% 回复\n");
                }
            }
            sb.Append("[CIDIAG][平衡] 出招承诺：敌人进入前摇/挥击后击退衰减到 15%，")
              .Append("且不再因距离超限中途取消。实测每次命中把敌人推开 0.26~0.49m，")
              .Append("其中 54%~82% 推到了它自己够不着的距离外——前摇不是被打断的，是被推出去的\n");
            sb.Append("[CIDIAG][平衡] 【更正】此前报的「前摇打断率 86%」是计数错误，不是实机事实：")
              .Append("OpenAttackHitbox 里 _swingFiring 置位写在 ShowTelegraph(false) 之后，")
              .Append("于是每一次成功打出去的招都被记成一次「前摇被打断」。")
              .Append("按同一份日志扣掉这部分（teleCancel 10 − 出手 7），真实打断率约 23%\n");
            sb.Append("[CIDIAG][前摇] 受击框几何：根节点在身体中心（胶囊 height=2/center=0，身体占 root±1），")
              .Append("全身兜底受击框 center 已从 (0,身高/2,0) 改为 (0,0,0)——")
              .Append("原来那个是「根在脚下」的算法，实际把兜底框整体抬高一米（腰以下没有兜底）\n");
            sb.Append("[CIDIAG][前摇] 出手由前摇时钟唯一驱动（TickTelegraph 走满才 OpenAttackHitbox），")
              .Append("不再用 Invoke 排队——排队残留会让刀在下一次前摇刚亮起 0.1~0.4 秒时落下。")
              .Append("起手 ").Append(AdversityRoad.AI.EnemyController.MinWindup.ToString("0.00"))
              .Append("~0.82s，连击段 ")
              .Append(AdversityRoad.AI.EnemyController.ComboWindup.ToString("0.00"))
              .Append("s，远程 ")
              .Append(AdversityRoad.AI.EnemyController.RangedWindup.ToString("0.00"))
              .Append("s；任何一次会造成伤害的攻击，出手前必定有走满的可见警示\n");
            sb.Append("[CIDIAG][平衡] 【设计方向】玩家侧不做任何限制（开放能力，熟练度即胜率）；")
              .Append("差距一律从敌人侧补。实测攻防比 9.5:1（玩家 1.38 次/秒 vs 敌人 0.15 次/秒），")
              .Append("目标 ≤3:1、玩家受击时间 5~15%\n");
            sb.Append("[CIDIAG][平衡] 还手闸：\"刚挨打不还手\"原本要求连续 0.55s 未受击，")
              .Append("而玩家剑连的链取消间隔是 0.19~0.32s——不停手它就永远等不到。")
              .Append("现在霸体窗内/硬直预算用尽时不再受此限；连续被压制超过 ")
              .Append(AdversityRoad.AI.EnemyController.DizzySuppressCap)
              .Append("s 无条件放行（并给起身霸体）；被打过 3 秒内的目标不与他人抢攻击令牌\n");
            sb.Append("[CIDIAG][平衡] 硬直占空比上限：任意 ")
              .Append(AdversityRoad.AI.EnemyController.StaggerChainWindow).Append(" 秒内最多 ")
              .Append(AdversityRoad.AI.EnemyController.StaggerBudget)
              .Append(" 秒可处于硬直（≤")
              .Append((AdversityRoad.AI.EnemyController.StaggerBudget
                       / AdversityRoad.AI.EnemyController.StaggerChainWindow * 100f).ToString("0"))
              .Append("%）；超预算后受击/破防/打断前摇一律不再进硬直，")
              .Append("完美闪避与精准格挡打出的破绽不受限\n");
            sb.Append("[CIDIAG][平衡] 防连锁硬直：起身霸体窗 0.90s（普通踉跄 0.45s；")
              .Append("连续 3 次硬直之后保底 1.20s）＋起身反击带霸体（前摇打不断）；")
              .Append("硬直递减窗口 ").Append(AdversityRoad.AI.EnemyController.StaggerChainWindow)
              .Append("s 内每多一次 ×0.72（下限 0.35）、霸体冷却 ×(1+0.45n)；")
              .Append("重击不再免检霸体，只削 0.35s；打断前摇的硬直也走同一套递减\n");
        }

        /// <summary>
        /// 角色·贰的动作库自检。返回 false 让作业变红。
        ///
        /// 【这道检查是被一次真实的严重回归换来的】#41 我把 UalMap 从默认建图路径里
        /// 摘掉，理由是"主库 84 条把 41 个姿态占满了，UAL 一条都赢不了"。
        /// 那句话只对角色·壹成立：角色·贰的主库是 Characters/Anims2，
        /// 里面只有两条拔刀/收刀片段，它的攻击、受击、死亡、闪避、跳跃、施法
        /// **全部来自 UalMap**。摘掉之后角色·贰的动作动画被一次性删光，
        /// 而 CI 那时只实例化 PlayerModel（角色·壹）建一份 PlayableAnimator——
        /// 全绿，一路进了 APK，玩家在手机上才发现。
        ///
        /// 教训写成一条检查：**每个可玩角色都要各自验一遍**。
        /// 判据是硬的——角色·贰必须有 idle/walk/run（否则整层回退方块骨骼），
        /// 且攻击/受击/死亡这三个最基本的姿态都要有片段。
        ///
        /// 【第二次教训：光有片段不算对】上一版这条检查只问"有没有片段"，
        /// 于是 UAL 接管角色·贰的那十几个包体它一路全绿——每个姿态都有片段，
        /// 只是全换成了另一套动作。现在改成逐姿态与角色·壹比对片段名：
        /// 角色·贰没有自己的动作库，两人本就该一模一样，不同即是被换掉了。
        /// 并且不再在这里硬写目录名，改为与 PlayerAppearance 同源。
        /// </summary>
        static bool DiagSecondCharacter(StringBuilder sb)
        {
            var prefab = Resources.Load<GameObject>("Characters/PlayerModel2");
            if (prefab == null)
            {
                sb.Append("[CIDIAG][角色贰] 没有 Characters/PlayerModel2，跳过\n");
                return true;
            }
            var ref1 = Resources.Load<GameObject>("Characters/PlayerModel");
            var model = Object.Instantiate(prefab);
            GameObject model1 = ref1 != null ? Object.Instantiate(ref1) : null;
            bool ok = true;
            try
            {
                var animator = model.GetComponentInChildren<Animator>() ?? model.AddComponent<Animator>();
                // 【按玩法里真正走的那条路建】PlayerAppearance 传什么目录，这里就传什么，
                // 上一版我在这里硬写了 "Characters/Anims2"，等于把待验证的那个假设
                // 抄进了检查本身——检查于是永远同意我。
                var pa = new AdversityRoad.Combat.PlayableAnimator(animator, PlayerAnimsFolder());
                sb.Append("[CIDIAG][角色贰] 动作库有效=").Append(pa.Valid ? "是" : "否").Append('\n');
                if (!pa.Valid)
                {
                    sb.Append("[CIDIAG][角色贰] !! 动作库无效：idle/walk/run 缺任一，")
                      .Append("整个动捕层会回退成方块骨骼\n");
                    ok = false;
                }
                else
                {
                    var must = new[]
                    {
                        AdversityRoad.Combat.PoseState.Attack,
                        AdversityRoad.Combat.PoseState.Hit,
                        AdversityRoad.Combat.PoseState.Death,
                    };
                    foreach (var ps in must)
                        if (!pa.HasAction(ps))
                        {
                            sb.Append("[CIDIAG][角色贰] !! 姿态 ").Append(ps)
                              .Append(" 没有任何片段——角色贰的动作库被改坏了\n");
                            ok = false;
                        }
                    // 【真正的判据：两个角色必须用同一套片段】
                    // "有片段"挡不住"换成了另一套片段"——UAL 接管的那十几版里
                    // 每一个姿态都有片段，CI 一路全绿，而玩家看到的是一套完全陌生的动作。
                    // 角色·贰没有自己的动作库（Anims2 只是公共补充库），
                    // 所以它每个姿态的片段名都应当与角色·壹**逐条相同**，不同就是被换掉了。
                    if (model1 != null)
                    {
                        var an1 = model1.GetComponentInChildren<Animator>() ?? model1.AddComponent<Animator>();
                        var pa1 = new AdversityRoad.Combat.PlayableAnimator(an1, null);
                        if (pa1.Valid)
                        {
                            int diff = 0;
                            foreach (AdversityRoad.Combat.PoseState ps
                                     in System.Enum.GetValues(typeof(AdversityRoad.Combat.PoseState)))
                            {
                                string a = pa.ActionClipNameOf(ps), b = pa1.ActionClipNameOf(ps);
                                if (a == b) continue;
                                diff++;
                                sb.Append("[CIDIAG][角色贰] !! 姿态 ").Append(ps)
                                  .Append(" 与角色壹不一致：角色贰=").Append(a.Length > 0 ? a : "（无）")
                                  .Append("  角色壹=").Append(b.Length > 0 ? b : "（无）").Append('\n');
                            }
                            if (diff > 0) ok = false;
                            else sb.Append("[CIDIAG][角色贰] 与角色壹逐姿态片段一致（两人共用默认动作库）\n");
                        }
                        pa1.Destroy();
                    }
                    foreach (var line in pa.DescribeActionSet().Split('\n'))
                        if (line.Length > 0) sb.Append("[CIDIAG][角色贰] ").Append(line).Append('\n');
                }
                pa.Destroy();
            }
            catch (System.Exception e)
            {
                sb.Append("[CIDIAG][角色贰] 诊断异常：").Append(e.Message).Append('\n');
                ok = false;
            }
            finally
            {
                Object.DestroyImmediate(model);
                if (model1 != null) Object.DestroyImmediate(model1);
            }
            return ok;
        }

        /// <summary>
        /// 攻击距离表：把每一招的判定框前沿，和这个角色**实际量出来的**
        /// 手臂长 / 腿长摆在一起。
        ///
        /// 这一项要挡住的是玩家指出的那件事：空手（或剑还在鞘里）时，
        /// 横斩的判定框照样伸到身前 1.85 米——那是一把大剑的长度。
        /// 距离是几何量，光看代码看不出它对不对，必须把数摆出来比。
        /// </summary>
        static void DiagReach(StringBuilder sb)
        {
            var p1 = new AdversityRoad.Combat.ReachProfile();
            foreach (var name in new[] { "Characters/PlayerModel", "Characters/PlayerModel2", "Characters/EnemyModel" })
            {
                var prefab = Resources.Load<GameObject>(name);
                if (prefab == null) { sb.Append("[CIDIAG][距离] ").Append(name).Append(" 缺失\n"); continue; }
                var go = Object.Instantiate(prefab);
                try
                {
                    // 【兵器也要量】上一版这里传 null，于是日志里刃长恒为 0.00，
                    // 我因此没看出"敌人握着长刀却被当成空手"这件事——
                    // 而玩家在真机上一眼就看出来了：赤手空拳比敌人的长刀够得还远。
                    // 这里按装配时同一条路找兵器节点，把它的名字和量出来的刃长都打出来。
                    var wp = AdversityRoad.Combat.MecanimCharacter.FindWeaponInModel(go.transform);
                    var handBone = AdversityRoad.Combat.MecanimCharacter.FindBone(go.transform, "righthand");
                    var prof = AdversityRoad.Combat.ReachModel.Measure(
                        go.transform, go.transform, wp, wp != null);
                    if (name.EndsWith("PlayerModel")) p1 = prof;
                    sb.Append("[CIDIAG][距离] ").Append(name).Append("  兵器节点=")
                      .Append(wp != null ? wp.name : "（模型自带的认不出）")
                      .Append("  手骨=").Append(handBone != null ? handBone.name : "（没找到）")
                      .Append("  量得刃长=")
                      .Append(AdversityRoad.Combat.ReachModel.BladeLength(handBone, wp).ToString("0.00"))
                      .Append('m');
                    if (wp != null)
                    {
                        // 刃长是从网格包围盒量的，印出用的是哪个渲染器、盒子多大，
                        // 数字才解释得清（骨骼蒙皮的剑，包围盒可能比剑本身大一圈）。
                        var rs = wp.GetComponentsInChildren<Renderer>(true);
                        sb.Append("  渲染器=").Append(rs.Length);
                        for (int k = 0; k < rs.Length && k < 3; k++)
                            sb.Append("  [").Append(rs[k].GetType().Name).Append(' ')
                              .Append(rs[k].name).Append(" 盒=")
                              .Append(rs[k].bounds.size.ToString("0.00")).Append(']');
                    }
                    sb.Append('\n');
                    sb.Append("[CIDIAG][距离] ").Append(name).Append("  ")
                      .Append(prof.valid ? prof.ToString() : "!! 量不到骨骼，这个角色不会受距离约束")
                      .Append('\n');
                }
                finally { Object.DestroyImmediate(go); }
            }
            // 逐招对比：判定框前沿 vs 空手够得到的距离。
            var poses = new[]
            {
                AdversityRoad.Combat.PoseState.Attack,
                AdversityRoad.Combat.PoseState.SwordThrust,
                AdversityRoad.Combat.PoseState.HeavyAttack,
                AdversityRoad.Combat.PoseState.PunchJab,
                AdversityRoad.Combat.PoseState.AttackKick,
                AdversityRoad.Combat.PoseState.SideKick,
                AdversityRoad.Combat.PoseState.Sweep,
            };
            foreach (var ps in poses)
            {
                AdversityRoad.Combat.PlayerCombatController.PoseHitShape(ps, out Vector3 size, out Vector3 center);
                float far = center.z + size.z * 0.5f;
                var limb = AdversityRoad.Combat.ReachModel.LimbOf(ps);
                sb.Append("[CIDIAG][距离]   ").Append(ps)
                  .Append("  判定框前沿=").Append(far.ToString("0.00")).Append('m')
                  .Append("  靠 ").Append(limb).Append(" 够到");
                if (p1.valid)
                {
                    // 【这一列必须用空手的 profile】上一版我把量到兵器的 p1 直接拿来算，
                    // 于是标签写着"空手"、数字却是持械的（Attack 印成 1.85、突刺印成 2.10）。
                    // 又一次"日志描述的不是它声称的那件事"。把刃长清零再算。
                    var bareProf = p1;
                    bareProf.blade = 0f;
                    bareProf.bladeKnown = true;
                    float bare = bareProf.LimitOf(limb);
                    sb.Append("  空手裁到=").Append(Mathf.Min(far, bare).ToString("0.00")).Append('m');
                }
                sb.Append('\n');
            }
            sb.Append("[CIDIAG][距离] 规则：判定框前沿超过「肩前+臂长(+刃长)」或「髋前+腿长」加 ")
              .Append(AdversityRoad.Combat.ReachProfile.StepPad.ToString("0.00"))
              .Append("m 踏前余量的部分会被裁掉；空手/未出鞘时刃长按 0 算，剑招同时降级为拳连\n");
        }

        /// <summary>
        /// 关卡首领梯度表：每一关的**通关目标**到底有多硬。
        ///
        /// 玩家反馈"前两关的 boss 完全不堪一击"——查下来字面属实：序章其一的关底目标
        /// 挂的是**见习**等级，而 TierStat(见习)=0.55 是往下乘的，生命只有 77，
        /// 连一套剑连（116 伤害）都撑不满。这种事光看某一关的配置看不出来，
        /// 要把全部关卡排成一列比才看得见坡度是不是断的。
        /// 判据是硬的：任何一关的目标撑不满 1.5 套剑连就报红。
        /// </summary>
        static bool DiagStoryLadder(StringBuilder sb)
        {
            bool ok = true;
            AdversityRoad.Combat.PlayerCombatController.ComboTotals(true, out float swordMult, out float _);
            float perChain = AdversityRoad.Combat.PlayerCombatController.DefaultBaseDamage * swordMult;
            sb.Append("[CIDIAG][关卡] 一套剑连 ").Append(perChain.ToString("0"))
              .Append(" 伤害（未计减免；下表逐个按 100/(100+防御) 折算，与「平衡」段同口径）")
              .Append("；下表是每一关**通关目标**的硬度\n");
            var chapters = AdversityRoad.Core.StoryManager.Chapters;
            for (int i = 0; i < chapters.Length; i++)
            {
                var ch = chapters[i];
                var prof = AdversityRoad.AI.EnemyCatalog.Create(ch.enemyType, ch.enemyTier);
                // 【伤害口径必须与「平衡」段完全一致】防御是按 100/(100+防御) 减免的，
                // 不是线性相减。我第一版在这里另写了一个 (基础-防御)，
                // 算出来首领要 23 套连招——和同一份报告上面那张表自相矛盾。
                // 一份报告里两套算法，必定有一套在骗人。
                float red = 100f / (100f + prof.defense);
                float chain = AdversityRoad.Combat.PlayerCombatController.DefaultBaseDamage
                              * swordMult * red;
                float sets = chain > 0.01f ? prof.maxHealth / chain : 99f;
                sb.Append("[CIDIAG][关卡]   ").Append(ch.title)
                  .Append("  ").Append(AdversityRoad.AI.EnemyCatalog.TierLabel(ch.enemyTier))
                  .Append("·").Append(AdversityRoad.AI.EnemyCatalog.TypeLabel(ch.enemyType))
                  .Append("  生命=").Append(prof.maxHealth.ToString("0"))
                  .Append(" 防御=").Append(prof.defense.ToString("0.0"))
                  .Append("  需 ").Append(sets.ToString("0.0")).Append(" 套剑连");
                if (sets < 1.5f)
                {
                    sb.Append("  !! 关底目标撑不满 1.5 套连招——这一关等于没有 boss");
                    ok = false;
                }
                sb.Append('\n');
            }
            return ok;
        }

        /// <summary>
        /// 关卡通关规则表：每一关到底算"打倒它"还是"穿过去"，一次全列出来。
        ///
        /// 【为什么这张表必须由 CI 打出来】规则的来源是敌人来处表（EnemyOrigins），
        /// 一处改动会同时影响门的行为、任务行文案、进场播报和目标行——
        /// 而这四处玩家是分开看到的。把最终结论列成一张表，改错了当场就能看见，
        /// 不必进游戏走 27 关。
        ///
        /// 判据是硬的，两条：
        ///   ① "穿过去"的关卡必须**解析得出一扇向前门**——门的去向按章节顺序算
        ///      （Portal.Resolve），序列里没有下一章就没有向前门，那一关会变成
        ///      既不能打过也走不出去的死局。所以这里核两件事：它不是序列最后一章，
        ///      而且它的区域是经典静态区（开放城区与动态场景不在章节序列里）。
        ///      入口到门的实际距离由运行时的 LevelTraverse 把关（走不够会明说还差几米），
        ///      那段几何在编辑器里不建世界就量不到，不在这里假装量过。
        ///   ② "穿过去"的关卡必须写了 victoryByEscape——一仗没打的人不该读到
        ///      "你把它打碎了"；
        ///   ③ 已知按剧情自带专属通关动作的关卡（第八章）不得落进"穿过去"，
        ///      那会把它们自己的机制架空。
        /// </summary>
        static bool DiagLevelRules(StringBuilder sb)
        {
            bool ok = true;
            var chapters = AdversityRoad.Core.StoryManager.Chapters;
            int escape = 0, defeat = 0;
            sb.Append("[CIDIAG][规则] 外部心魔 → 从入口走到出口即通关（也可打倒）；")
              .Append("内心心魔 → 必须打倒。入口到出口的直线距离下限 ")
              .Append(AdversityRoad.World.LevelTraverse.MinDistance.ToString("0")).Append(" 米\n");

            for (int i = 0; i < chapters.Length; i++)
            {
                var ch = chapters[i];
                var origin = AdversityRoad.AI.EnemyOrigins.Of(ch.enemyType);
                var rule = AdversityRoad.Core.LevelRules.Of(ch);
                if (rule == AdversityRoad.Core.LevelClearRule.Escape) escape++; else defeat++;

                sb.Append("[CIDIAG][规则]   ").Append(ch.title)
                  .Append("  关底=").Append(AdversityRoad.AI.EnemyCatalog.TypeLabel(ch.enemyType))
                  .Append("（").Append(AdversityRoad.AI.EnemyOrigins.Label(origin)).Append("）")
                  .Append("  通关=").Append(AdversityRoad.Core.LevelRules.Name(rule));

                if (!ch.advanceOnKill)
                {
                    sb.Append("  〔专属通关动作，不走门到门〕");
                    if (rule == AdversityRoad.Core.LevelClearRule.Escape &&
                        AdversityRoad.Core.LevelRules.ZoneClearsByEscape(ch.zoneIndex, out int _))
                    {
                        sb.Append("  !! 专属通关动作的关卡被判成了门到门");
                        ok = false;
                    }
                }
                else if (rule == AdversityRoad.Core.LevelClearRule.Escape)
                {
                    // 不战而过的通关文案必须写过：否则玩家一仗没打，
                    // 面板却告诉他"你把它打碎了"——游戏在替他撒谎
                    if (string.IsNullOrEmpty(ch.victoryByEscape))
                    {
                        sb.Append("  !! 缺 victoryByEscape：不战而过会读到打赢版本的文案");
                        ok = false;
                    }
                    if (i + 1 >= chapters.Length)
                    {
                        sb.Append("  !! 是序列最后一章，解析不出向前门——走不出去");
                        ok = false;
                    }
                    if (ch.zoneIndex < 0 ||
                        ch.zoneIndex >= AdversityRoad.World.ZoneBuilder.StaticZoneCount ||
                        ch.zoneIndex == AdversityRoad.World.ZoneBuilder.IndexOfZone("city"))
                    {
                        sb.Append("  !! 区域不在经典章节序列里，向前门按章节解析不出去处");
                        ok = false;
                    }
                }
                sb.Append('\n');
            }
            sb.Append("[CIDIAG][规则] 合计：穿过去 ").Append(escape)
              .Append(" 关 / 打倒它 ").Append(defeat).Append(" 关\n");
            return ok;
        }

        /// <summary>
        /// 读招规律表：玩家要学的那套「固定规则」到底长什么样，一次全列出来。
        ///
        /// 判据是硬的：每一个敌人会用的招式，都必须能找到**它自己的动作片段**——
        /// 找不到就没有起手段可放慢，前摇会退回那段所有招共用的通用姿势，
        /// 「看动作认招」这件事对那一招就不成立。
        /// </summary>
        static bool DiagTelegraphRules(StringBuilder sb)
        {
            var prefab = Resources.Load<GameObject>("Characters/EnemyModel");
            AdversityRoad.Combat.PlayableAnimator pa = null;
            GameObject go = null;
            if (prefab != null)
            {
                go = Object.Instantiate(prefab);
                var an = go.GetComponentInChildren<Animator>() ?? go.AddComponent<Animator>();
                pa = new AdversityRoad.Combat.PlayableAnimator(an, null);
            }
            bool ok = true;
            sb.Append("[CIDIAG][读招] 屏幕提示层（头顶记号/脚下红圈/字幕教学/特写文字）= ")
              .Append(AdversityRoad.Core.GameDebug.TelegraphOverlays ? "开" : "关（读招只靠身体与节拍）")
              .Append('\n');
            sb.Append("[CIDIAG][读招] 每一招的固定规律（时长同族恒定，玩家据此学习）；")
              .Append("前摇：基底定格在该招动画最前端（")
              .Append((AdversityRoad.AI.EnemyController.WindupHold * 100f).ToString("0"))
              .Append("%），叠该族反方向蓄势姿态；落刀从 0 全速播完并卸掉蓄势\n");
            var poses = new[]
            {
                AdversityRoad.Combat.PoseState.Attack, AdversityRoad.Combat.PoseState.AttackUp,
                AdversityRoad.Combat.PoseState.SwordThrust, AdversityRoad.Combat.PoseState.HeavyAttack,
                AdversityRoad.Combat.PoseState.AttackSpin, AdversityRoad.Combat.PoseState.PunchCross,
                AdversityRoad.Combat.PoseState.AttackKick, AdversityRoad.Combat.PoseState.SideKick,
                AdversityRoad.Combat.PoseState.SpinKick,  AdversityRoad.Combat.PoseState.JumpKick,
                AdversityRoad.Combat.PoseState.Sweep,
            };
            foreach (var ps in poses)
            {
                var spec = AdversityRoad.Combat.TelegraphTable.Get(ps, false);
                string clip = pa != null && pa.Valid ? pa.ActionClipNameOf(ps) : "";
                sb.Append("[CIDIAG][读招]   ").Append(ps)
                  .Append("  族=").Append(spec.kind)
                  .Append(" 前摇=").Append(spec.windup.ToString("0.00")).Append('s')
                  .Append(" 记号=").Append(spec.mark)
                  .Append(" 应对=").Append(spec.answer)
                  .Append("  起手段=").Append(clip.Length > 0 ? clip : "（无·退回通用姿势）");
                if (clip.Length == 0) { sb.Append("  !! 这一招没有自己的动作片段"); ok = false; }
                sb.Append('\n');
            }
            // ---- 流派招串：变化从哪来，以及六个招式族是不是都有人用 ----
            var usedKinds = new System.Collections.Generic.HashSet<AdversityRoad.Combat.TelegraphKind>();
            foreach (var arch in AdversityRoad.AI.EnemyMoveSet.AllArchetypes)
            {
                foreach (bool elite in new[] { false, true })
                {
                    var set = AdversityRoad.AI.EnemyMoveSet.For(arch, elite);
                    foreach (var str in set)
                    {
                        var line = new StringBuilder();
                        float total = 0f;
                        for (int i = 0; i < str.stages.Length; i++)
                        {
                            var sp = AdversityRoad.Combat.TelegraphTable.Get(str.stages[i], false);
                            usedKinds.Add(sp.kind);
                            if (i > 0) line.Append(" → ");
                            line.Append(sp.kind).Append('(').Append(sp.windup.ToString("0.00")).Append("s)");
                            total += sp.windup;
                            if (pa != null && pa.Valid && pa.ActionClipNameOf(str.stages[i]).Length == 0)
                            {
                                sb.Append("[CIDIAG][读招] !! ").Append(arch).Append(' ').Append(str.name)
                                  .Append(" 第").Append(i + 1).Append("段 ").Append(str.stages[i])
                                  .Append(" 没有自己的动作片段，起手段无从播起\n");
                                ok = false;
                            }
                        }
                        sb.Append("[CIDIAG][读招]   ").Append(arch).Append(elite ? "·精英 " : "      ")
                          .Append(str.name).Append("  ").Append(line)
                          .Append("  合计前摇=").Append(total.ToString("0.00")).Append("s\n");
                    }
                }
            }
            // 【每一个敌人都必须有显式的流派】没归流派的会掉进 default → 拳法型，
            // 表现是"一个赌棍打起来像拳击手"，而代码里看不出任何问题。
            // 实机日志实测：536 次出手 99% 是拳法套路，因为第一章的两个首领
            //（两元赖账王、新车债王）正在那 5 个没映射的类型里。
            // 54 个类型映射了 50 个——"基本填满了"恰恰是这种错最容易藏住的形态。
            foreach (AdversityRoad.AI.EnemyType et
                     in System.Enum.GetValues(typeof(AdversityRoad.AI.EnemyType)))
            {
                if (!AdversityRoad.AI.MartialArchetypeCatalog.HasExplicitArchetype(et))
                {
                    sb.Append("[CIDIAG][读招] !! 敌人 ").Append(et)
                      .Append(" 没有归入任何武学流派，会掉进默认的拳法型\n");
                    ok = false;
                }
            }
            // 【每一个流派都必须有自己的招串】掉进 default 的那一档会悄悄变成拳法型。
            // 实机日志实测：736 次出手里 66% 是拳法系招串，因为 MindMixed
            //（覆盖约二十种敌人）没有分支。这种"沉默的归并"只能靠逐个枚举值比对来发现。
            foreach (AdversityRoad.AI.MartialArchetype arch
                     in System.Enum.GetValues(typeof(AdversityRoad.AI.MartialArchetype)))
            {
                var mine = AdversityRoad.AI.EnemyMoveSet.For(arch, false);
                var fist = AdversityRoad.AI.EnemyMoveSet.For(AdversityRoad.AI.MartialArchetype.Fist, false);
                if (arch != AdversityRoad.AI.MartialArchetype.Fist && ReferenceEquals(mine, fist))
                {
                    sb.Append("[CIDIAG][读招] !! 流派 ").Append(arch)
                      .Append(" 没有自己的招串，掉进了拳法型——这个流派的敌人打起来和拳法家一样\n");
                    ok = false;
                }
            }
            foreach (AdversityRoad.Combat.TelegraphKind k
                     in System.Enum.GetValues(typeof(AdversityRoad.Combat.TelegraphKind)))
                if (!usedKinds.Contains(k))
                {
                    // 一条玩家永远遇不到的规律，等于不存在——扫腿族此前正是这种情况。
                    sb.Append("[CIDIAG][读招] !! 招式族 ").Append(k)
                      .Append(" 没有任何流派会用——这条规律玩家永远学不到\n");
                    ok = false;
                }
            if (pa != null) pa.Destroy();
            if (go != null) Object.DestroyImmediate(go);
            return ok;
        }

        /// <summary>玩法里角色·贰实际使用的动作库目录（与 PlayerAppearance.Rebuild 同源）。</summary>
        static string PlayerAnimsFolder() { return null; }

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

        /// <summary>
        /// 把关键结论**重复一遍贴在报告最末尾**。
        /// 原因很实在：CI 日志只能按"末尾 N 行"取回，而这份报告的材质/贴图清单
        /// 有一百多行，角色贰的判定结论被顶到了取不回来的位置——上一轮我只能
        /// 拿"作业是绿的"当证据，而我自己刚说过"结构性检查不等于功能验证"。
        /// 结论行放末尾，取回来的永远是结论而不是贴图清单。
        /// </summary>
        static void AppendVerdict(StringBuilder sb)
        {
            string[] lines = sb.ToString().Split('\n');
            sb.Append("\n===== [CIDIAG] 结论摘要（关键判定一律重贴在末尾）=====\n");
            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i];
                if (ln.Length == 0) continue;
                if (ln.Contains("!! ") ||
                    (ln.StartsWith("[CIDIAG][角色贰]") && !ln.StartsWith("[CIDIAG][角色贰]     ")) ||
                    ln.StartsWith("[CIDIAG][距离]") ||
                    ln.StartsWith("[CIDIAG][前摇]") ||
                    ln.StartsWith("[CIDIAG][关卡]") ||
                    ln.StartsWith("[CIDIAG][读招]") ||
                    ln.StartsWith("[CIDIAG][平衡] 【设计方向】"))
                    sb.Append(ln).Append('\n');
            }
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
                if (!DiagSecondCharacter(sb)) exit = 1;
                DiagCharacterMaterials(sb);
                DiagBalance(sb);
                DiagReach(sb);
                if (!DiagStoryLadder(sb)) exit = 1;
                if (!DiagLevelRules(sb)) exit = 1;
                if (!DiagTelegraphRules(sb)) exit = 1;
                if (!DiagUal(sb)) exit = 1;
                // 变体池里出现重复片段（DescribeActionSet 自己标的 "!!"）也算红：
                // 「变体×3 里有两条是同一段」看起来是绿的，玩起来是"翻滚从不变化"。
                if (sb.ToString().Contains("!! ")) exit = 1;
                AppendVerdict(sb);
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
