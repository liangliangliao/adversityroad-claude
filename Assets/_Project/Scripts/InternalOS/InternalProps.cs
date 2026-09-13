using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.OpenWorld;

namespace AdversityRoad.InternalOS
{
    /// <summary>
    /// 关键物的六大类（V2.2 PRD 第 10.2 节 Prefab 类别）。
    ///
    /// 90 关一共点名了 54 个 PF_ 名字，但它们不是 54 种行为——
    /// PF_SubmitConsole / PF_ReleaseConsole / PF_LoadDock 都是同一件事：
    /// 这一关最后那个"按下去就算数"的动作。所以按**行为**分类，
    /// 名字只决定它叫什么、长什么样，不决定它做什么。
    /// </summary>
    public enum InternalPropKind
    {
        /// <summary>Execution Gate：按下它这一关才算通关。每关必须有且只有一个。</summary>
        GateConsole,
        /// <summary>Done / DoD 锁：先把"做到什么算完"钉死，Gate 才肯开。</summary>
        DoneLock,
        /// <summary>工作台：产出第一版、改稿、装箱——推进目标的那个动作。</summary>
        WorkStation,
        /// <summary>检查点：失败后从这里继续，而不是从头再来。</summary>
        Checkpoint,
        /// <summary>脱离门：战略撤退的合法出口（要求填重返条件）。</summary>
        DisengageGate,
        /// <summary>证据：预测与现实之间的那一点可验证材料。</summary>
        Evidence,
        /// <summary>判据板 / 可逆门：决定要不要走、走了还能不能回头。</summary>
        DecisionBoard,
        /// <summary>触发物 / 习惯回路 / If-Then 锻炉：自动行为链的那个起点。</summary>
        TriggerObject,
        /// <summary>归档柜 / 重播机 / 停止线：处理完必要行动之后收起来。</summary>
        ArchiveVault,
        /// <summary>诱饵：看起来也该做、做了就偏离的那些东西。</summary>
        Decoy,
        /// <summary>目标箱 / 货物：要被推到 Gate 去的那个东西，本身不是 Gate。</summary>
        Cargo,
    }

    /// <summary>计划里的一件关键物：叫什么，它到底做什么，以及牌面写什么。</summary>
    public struct InternalPropPlan
    {
        public string pfName;
        public InternalPropKind kind;
        /// <summary>
        /// 牌面覆盖。空 = 按类别取默认名。
        ///
        /// 【为什么需要它】9-3 要摆几个"旧箱"当干扰项，它们的行为就是诱饵（Decoy），
        /// 但牌面绝不能写"诱饵"——写了这一关就没有判断可做了，
        /// 玩家一眼就知道该躲开哪几个。行为和名字必须能分开设。
        /// </summary>
        public string label;

        /// <summary>
        /// 走近时那句解释的覆盖。空 = 按类别取默认句。
        ///
        /// 旧箱需要这个：按类别取的那句是"它看起来也该做、做了不推进这一关"——
        /// 那等于把答案印在脸上，这一关的判断当场作废。
        /// 换成一句**只陈述事实**的话，玩家得自己把它和"只送当前这一箱"对上。
        /// </summary>
        public string explain;

        public InternalPropPlan(string n, InternalPropKind k, string lbl = null, string exp = null)
        { pfName = n; kind = k; label = lbl; explain = exp; }
    }

    /// <summary>
    /// 第 9-26 章的关键物：把关卡表里的机制变成场上摸得着的东西。
    ///
    /// 【为什么必须有这个文件】
    /// 没有它，这 90 关走进去是一间空屋子加两个敌人——9-1 的通关条件是
    /// "生成粗稿 → 达到 Done → 按 Submit"，而场上没有草稿桌、没有 Done 锁、
    /// 没有提交台。机制在数据里是齐的，在场景里一个实体都没有。
    /// PRD 第 11.3 节的验收第三条问的就是这件事：
    /// "心理机制是否通过空间、路径、机关而非字幕表达"。
    ///
    /// 【它和 ChapterProp 的区别】
    /// ChapterProp 是"机制的提示牌"——走近了给一行字。
    /// 这里的关键物是**能按的**：按下去会真的推进或判定这一关，
    /// 接的是 InternalLevelRunner 的 FireTrigger / MarkGoalAction / ExecutionGate。
    /// </summary>
    public static class InternalProps
    {
        /// <summary>PF_ 名字 → 行为类别。认不出的按名字里的词归类，再认不出算工作台。</summary>
        public static InternalPropKind KindOf(string pfName)
        {
            string n = pfName ?? "";

            // 【为什么 Cargo 和 DecisionBoard 必须排在 GateConsole 前面】
            //
            // 这里用的是子串匹配，而"要被搬到 Gate 去的东西"名字里天然带着 Gate 的词：
            //   PF_UnsubmittedBox      —— "Unsubmitted" 里含 "Submit"
            //   PF_DecisionCriteriaBoard —— "Board" 本来是指"上车口"
            // 按原来的顺序，9-3 的目标箱会被判成 Execution Gate：玩家走到箱子边
            // 两米内就直接通关了，而这一关的玩法恰恰是"把正确的那一箱推到月台"。
            // 箱子成了终点，这一关就没有了。
            //
            // 所以先认更具体的类别，再认 Gate——Gate 是兜底语义，不是优先语义。
            // Mailbox/Inbox/Toolbox 里的 "Box" 不是货物，单独挡掉。
            if (Has(n, "Crate", "Cargo", "Package", "Parcel") ||
                (Has(n, "Box") && !Has(n, "Mailbox", "Inbox", "Outbox", "Toolbox", "Checkbox")))
                return InternalPropKind.Cargo;
            if (Has(n, "Criteria", "Decision", "Reversible", "Irreversible", "Compass",
                    "Calibrat", "Sorter"))
                return InternalPropKind.DecisionBoard;
            if (Has(n, "Submit", "Release", "Publish", "LoadDock", "Board", "ExitGate",
                    "ExitTrigger", "Threshold", "Deliver", "Acceptance"))
                return InternalPropKind.GateConsole;
            if (Has(n, "DoneLock", "DoD", "Freeze", "VersionFreeze", "ScopeDoor"))
                return InternalPropKind.DoneLock;
            if (Has(n, "Checkpoint", "Restart", "Reentry", "Recovery"))
                return InternalPropKind.Checkpoint;
            if (Has(n, "Disengage", "StrategicExit", "Withdraw", "SafeExit"))
                return InternalPropKind.DisengageGate;
            if (Has(n, "Evidence", "Ore", "Vault", "Proof", "BehaviorCompare", "Record"))
                return InternalPropKind.Evidence;
            if (Has(n, "Trigger", "Habit", "IfThen", "Cue", "Routine", "Notification", "RedDot"))
                return InternalPropKind.TriggerObject;
            if (Has(n, "Archive", "Replay", "StopLine", "Film", "Projector"))
                return InternalPropKind.ArchiveVault;
            if (Has(n, "Bait", "Decoy", "Comfort", "Reward", "Ranking", "Applause", "Scaffold"))
                return InternalPropKind.Decoy;
            return InternalPropKind.WorkStation;
        }

        static bool Has(string s, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
                if (s.IndexOf(keys[i], System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>每一类的外观与玩家看得到的名字。</summary>
        public static void Style(InternalPropKind kind, out string label, out Color color, out Vector3 size)
        {
            switch (kind)
            {
                case InternalPropKind.GateConsole:
                    label = "提交台"; color = new Color(1f, 0.72f, 0.28f);
                    size = new Vector3(1.6f, 1.2f, 1.0f); return;
                case InternalPropKind.DoneLock:
                    label = "完成标准锁"; color = new Color(0.55f, 0.85f, 0.95f);
                    size = new Vector3(1.0f, 1.6f, 1.0f); return;
                case InternalPropKind.Checkpoint:
                    label = "检查点"; color = new Color(0.5f, 0.9f, 0.6f);
                    size = new Vector3(1.2f, 0.5f, 1.2f); return;
                case InternalPropKind.DisengageGate:
                    label = "脱离门"; color = new Color(0.6f, 0.72f, 0.85f);
                    size = new Vector3(1.6f, 2.4f, 0.4f); return;
                case InternalPropKind.Evidence:
                    label = "证据台"; color = new Color(0.62f, 0.88f, 1f);
                    size = new Vector3(1.1f, 0.9f, 1.1f); return;
                case InternalPropKind.DecisionBoard:
                    label = "判据板"; color = new Color(0.85f, 0.85f, 0.7f);
                    size = new Vector3(1.8f, 1.5f, 0.3f); return;
                case InternalPropKind.TriggerObject:
                    label = "触发物"; color = new Color(0.9f, 0.45f, 0.5f);
                    size = new Vector3(0.8f, 0.8f, 0.8f); return;
                case InternalPropKind.ArchiveVault:
                    label = "归档柜"; color = new Color(0.6f, 0.55f, 0.75f);
                    size = new Vector3(1.3f, 1.8f, 0.9f); return;
                case InternalPropKind.Decoy:
                    label = "诱饵"; color = new Color(0.75f, 0.55f, 0.3f);
                    size = new Vector3(1.0f, 1.0f, 1.0f); return;
                case InternalPropKind.Cargo:
                    label = "目标箱"; color = new Color(0.85f, 0.7f, 0.45f);
                    size = new Vector3(1.1f, 1.1f, 1.1f); return;
                default:
                    label = "工作台"; color = new Color(0.7f, 0.75f, 0.8f);
                    size = new Vector3(1.6f, 0.9f, 0.9f); return;
            }
        }

        /// <summary>
        /// Gate 牌子上的字。
        ///
        /// 【为什么不能所有 Gate 都写"提交台"】
        /// Style 只认类别，于是 9-3 的装车月台、9-4 的终点门、11 关的脱离门
        /// 顶上全挂着"提交台"。而 HUD 的目标行说的是"去【装车月台】"——
        /// 玩家满场找一个不存在的牌子，这比不给牌子更糟。
        /// 牌面与目标行必须是同一个词，所以这里按 PF 名给出那个词，
        /// 由 <see cref="InternalProp.Create"/> 和目标行共用。
        /// </summary>
        public static string GateLabel(string pfName)
        {
            string n = pfName ?? "";
            if (Has(n, "LoadDock")) return "装车月台";
            if (Has(n, "Release", "Publish")) return "发布台";
            if (Has(n, "Board")) return "上车口";
            if (Has(n, "Deliver", "Acceptance")) return "交付台";
            if (Has(n, "Threshold")) return "门槛";
            if (Has(n, "ExitGate", "ExitTrigger")) return "终点门";
            return "提交台";
        }

        /// <summary>
        /// 这一关该摆哪些关键物。
        ///
        /// 来源有两处，缺一不可：
        /// ① PRD「复用资产」栏点名的 PF_xxx（51 关有）；
        /// ② 每一关都必须补上的那个 Execution Gate ——
        ///    39 关的复用资产栏没写 PF_，但它们同样有通关动作。
        ///    没有 Gate 的关卡是走不完的，所以这一条不能靠数据里有没有写。
        /// </summary>
        public static List<InternalPropPlan> PlanFor(InternalLevelData lv)
        {
            var plan = new List<InternalPropPlan>();
            if (lv == null) return plan;

            string src = lv.reusedAssets ?? "";
            int i = 0;
            while (i < src.Length)
            {
                int at = src.IndexOf("PF_", i, System.StringComparison.Ordinal);
                if (at < 0) break;
                int end = at + 3;
                while (end < src.Length && (char.IsLetterOrDigit(src[end]) || src[end] == '_')) end++;
                string id = src.Substring(at, end - at);
                bool dup = false;
                for (int k = 0; k < plan.Count; k++) if (plan[k].pfName == id) { dup = true; break; }
                if (!dup) plan.Add(new InternalPropPlan(id, KindOf(id)));
                i = end;
            }

            // Gate 兜底：这一关必须有一个"按下去就算数"的东西。
            //
            // 【这里曾经有个设计错误，值得记下来】
            // 原来兜底只补一个**名字**，类别再回头从名字猜。于是 18-4 的通关动作是
            // "重新进入"，兜底给出 PF_RestartStation，而分类器把带 Restart 的一律读成检查点——
            // 补出来的 Gate 不是 Gate，8 个关卡就这么没了通关物件。
            // 名字不该决定行为：兜底在这里**直接把类别钉成 GateConsole**，不再经过猜名字那一步。
            // 9-3 的核心机制原话是"运输**正确目标箱**而非处理全部旧箱"。
            // 只摆一个目标箱的话，这句话就没有落点——场上只有一个箱子，
            // 谈不上"选对哪一箱"。所以补三个旧箱：它们能搬、搬了不推进，
            // 牌面写"旧箱"而不是"诱饵"，分得出来要靠读走近那句解释。
            if (lv.levelId == "9-3")
            {
                const string oldBox = "上一批堆在这儿、一直没送出去的箱子。它也搬得动。";
                plan.Add(new InternalPropPlan("PF_OldBox_A", InternalPropKind.Decoy, "旧箱", oldBox));
                plan.Add(new InternalPropPlan("PF_OldBox_B", InternalPropKind.Decoy, "旧箱", oldBox));
                plan.Add(new InternalPropPlan("PF_OldBox_C", InternalPropKind.Decoy, "旧箱", oldBox));
            }

            bool hasGate = false;
            for (int k = 0; k < plan.Count; k++)
                if (plan[k].kind == InternalPropKind.GateConsole) { hasGate = true; break; }
            if (!hasGate) plan.Add(new InternalPropPlan(GateNameFor(lv), InternalPropKind.GateConsole));

            return plan;
        }

        /// <summary>这一关是否已经有能按的东西（CI 与面板共用）。</summary>
        public static bool HasGate(InternalLevelData lv, out string gateName)
        {
            gateName = "";
            var plan = PlanFor(lv);
            for (int i = 0; i < plan.Count; i++)
                if (plan[i].kind == InternalPropKind.GateConsole)
                { gateName = plan[i].pfName; return true; }
            return false;
        }

        /// <summary>
        /// 按通关条件给 Gate 起个对得上的名字。
        ///
        /// 玩家读到的是"跨过玄关门槛"还是"把箱子装车"，取决于这一关真正要做的动作——
        /// 一律叫"提交台"就又变回了通用关卡。
        /// </summary>
        public static string GateNameFor(InternalLevelData lv)
        {
            string v = (lv.realityVictory ?? "") + (lv.coreMechanic ?? "");
            if (v.Contains("提交") || v.Contains("Submit")) return "PF_SubmitConsole";
            if (v.Contains("发布") || v.Contains("Release")) return "PF_ReleaseConsole";
            if (v.Contains("装车") || v.Contains("货车")) return "PF_LoadDock";
            if (v.Contains("门槛") || v.Contains("出门") || v.Contains("离开") || v.Contains("走出"))
                return "PF_ExitThreshold";
            if (v.Contains("上车") || v.Contains("公交") || v.Contains("班车") || v.Contains("列车"))
                return "PF_BoardGate";
            if (v.Contains("归档")) return "PF_ArchiveDeliver";
            if (v.Contains("重新进入") || v.Contains("重启")) return "PF_RestartStation";
            if (v.Contains("撤退") || v.Contains("撤离")) return "PF_DisengageGate";
            if (v.Contains("出口") || v.Contains("穿过") || v.Contains("抵达")) return "PF_ExitGate";
            if (v.Contains("跨过") || v.Contains("进入") || v.Contains("敲")) return "PF_EntryGate";
            if (v.Contains("通过") || v.Contains("解除")) return "PF_CommitGate";
            if (v.Contains("决定") || v.Contains("锁定")) return "PF_DecisionCommit";
            return "PF_ExitGate";
        }

        /// <summary>
        /// 关卡循环自己摆出来的牌子（不在 PlanFor 里）。
        ///
        /// 9-1 的六张修改卡是 <see cref="Level0901BlankPage"/> 建的，不走关键物那条路。
        /// 但"怎么玩"的卡片上写着"走近读【修改项】"，门禁要能核对到它，
        /// 否则那条检查会把一个真实存在的牌子报成不存在。
        /// </summary>
        public static List<string> LoopLabelsFor(InternalLevelData lv)
        {
            var extra = new List<string>();
            if (lv == null) return extra;
            if (lv.levelId == Level0901BlankPage.LevelId) extra.Add(Level0901BlankPage.CardLabel);
            if (lv.levelId == Level0902ProofRoom.LevelId) extra.Add(Level0902ProofRoom.CardLabel);
            return extra;
        }

        /// <summary>
        /// 搭一个"带牌子的方块"。
        ///
        /// 【为什么不能直接 CreatePrimitive 然后往上挂牌子】
        /// 那样牌子成了**带非等比缩放的立方体**的子物体，缩放会继承下去，
        /// 字被横向拉三成——玩家说的"文字模糊看不清楚"有一半是这么来的。
        /// 所以结构分两层：根不缩放（牌子挂这儿），方块是它的子物体（缩放挂这儿）。
        /// 返回根；渲染器与碰撞体都在子物体上。
        /// </summary>
        public static GameObject LabeledBlock(string name, Vector3 pos, Vector3 size,
            Color color, float emissive, string label)
        {
            var root = new GameObject(name);
            root.transform.position = pos + Vector3.up * (size.y * 0.5f);

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale = size;
            body.GetComponent<MeshRenderer>().sharedMaterial =
                Combat.CombatFeedback.EnergyMaterial(color, emissive);
            var col = body.GetComponent<Collider>();
            if (col != null) col.isTrigger = false;

            // 牌子做成**钉在物体上的一块木牌，字是刻上去的**。
            // 玩家原话："文字需要刻在实体木质物体上"。
            // 之前是一块深色底板 + 亮字贴在方块表面——那看着是"贴了张标签"，
            // 不是这个物件本身的一部分。
            if (!string.IsNullOrEmpty(label)) WoodPlaque(root.transform, size, label);
            return root;
        }

        /// <summary>
        /// 往方块的前后两面各钉一块小木牌，字刻在木牌上。
        ///
        /// 木牌比物体窄一圈、厚 4 厘米，四角有铆钉——这样它读起来是
        /// "这个物件上挂了一块牌子"，而不是"这个面被涂了几个字"。
        /// </summary>
        static void WoodPlaque(Transform root, Vector3 size, string label)
        {
            var wood = new Color(0.44f, 0.30f, 0.17f);
            var ink = new Color(0.24f, 0.15f, 0.08f);
            var rivet = new Color(0.55f, 0.52f, 0.45f);

            float bw = Mathf.Min(size.x, size.z) > 0.5f
                ? Mathf.Max(size.x, size.z) * 0.86f
                : size.x * 0.86f;
            bw = Mathf.Clamp(bw, 0.8f, 2.6f);
            var board = new Vector3(bw, 0.46f, 0.04f);

            Vector3[] normals = { Vector3.forward, Vector3.back };
            for (int i = 0; i < normals.Length; i++)
            {
                Vector3 n = normals[i];
                float half = size.z * 0.5f;
                var holder = new GameObject("Plaque");
                holder.transform.SetParent(root, false);
                holder.transform.localPosition = n * (half + board.z * 0.5f)
                                               + Vector3.up * (size.y * 0.18f);
                holder.transform.localRotation = Quaternion.LookRotation(-n, Vector3.up);

                var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slab.name = "PlaqueBoard";
                slab.transform.SetParent(holder.transform, false);
                slab.transform.localScale = board;
                slab.GetComponent<MeshRenderer>().sharedMaterial =
                    Combat.CombatFeedback.EnergyMaterial(wood, 0f);
                Object.DestroyImmediate(slab.GetComponent<Collider>());

                // 四角铆钉：小球，不是又一个小方块
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sy = -1; sy <= 1; sy += 2)
                    {
                        var r = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                        r.name = "Rivet";
                        r.transform.SetParent(holder.transform, false);
                        r.transform.localPosition = new Vector3(
                            sx * (board.x * 0.5f - 0.07f), sy * (board.y * 0.5f - 0.07f), -0.025f);
                        r.transform.localScale = Vector3.one * 0.05f;
                        r.GetComponent<MeshRenderer>().sharedMaterial =
                            Combat.CombatFeedback.EnergyMaterial(rivet, 0f);
                        Object.DestroyImmediate(r.GetComponent<Collider>());
                    }

                OpenWorldBuilder.CarvedSign(holder.transform, board, label, ink);
            }
        }

        /// <summary>
        /// 把这一关的关键物建到场上。
        ///
        /// 落位规则：Gate 放在出口那一侧（它是这一关的终点），其余沿主路径依次排开。
        /// 不用随机——同一关每次进来东西都该在同一个地方，否则"空间记忆"无从谈起。
        /// </summary>
        public static int Build(InternalLevelData lv, Transform parent,
            Vector3 origin, Vector3 spawn, Vector3 exit)
        {
            if (lv == null) return 0;

            var plan = PlanFor(lv);
            var triggers = lv.triggerIds;
            Vector3 forward = exit - spawn;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);

            int made = 0, step = 0;
            Transform gateTransform = null;
            for (int i = 0; i < plan.Count; i++)
            {
                var kind = plan[i].kind;
                bool isGate = kind == InternalPropKind.GateConsole;

                // Gate 站在终点；其余沿路铺开，左右交替，别排成一条直线挡路
                Vector3 pos = isGate
                    ? exit + forward * 1.5f
                    : spawn + forward * (5f + step * 6f) + right * ((step % 2 == 0) ? 2.2f : -2.2f);
                if (!isGate) step++;

                if (UnityEngine.AI.NavMesh.SamplePosition(pos, out var hit, 12f,
                        UnityEngine.AI.NavMesh.AllAreas)) pos = hit.position;

                string trig = triggers.Count > 0
                    ? triggers[Mathf.Min(i, triggers.Count - 1)] : "";
                if (isGate && triggers.Count > 0) trig = triggers[triggers.Count - 1];

                var p = InternalProp.Create(pos, plan[i].pfName, kind, lv, trig,
                    plan[i].label, plan[i].explain);
                if (p == null) continue;
                if (parent != null) p.transform.SetParent(parent, true);
                if (isGate) gateTransform = p.transform;
                made++;
            }

            // 关卡循环：光有"三个能按的东西"还不是玩法。
            // 9-1 的玩法是"你不动房间就变大、六张修改卡里只有两张真的挡交付"，
            // 那一段逻辑属于这一关自己，装在这里。
            // 其余四关目前还只有关键物，没有各自的循环——见 README 的待办。
            if (lv.levelId == Level0901BlankPage.LevelId)
            {
                Level0901BlankPage.Install(parent, spawn, exit);
                if (Level0901BlankPage.Active != null)
                    Level0901BlankPage.Active.BindSubmitConsole(gateTransform);
            }
            else if (lv.levelId == Level0902ProofRoom.LevelId)
            {
                Level0902ProofRoom.Install(parent, spawn, exit);
            }

            return made;
        }
    }

    /// <summary>
    /// 一个能按的关键物。走近给解释，**按【用】**才发生。
    ///
    /// 【为什么从"走近自动触发"改成"按键触发"】
    /// 原来是走到 2.2 米自动执行。那等于玩家没有做任何决定——
    /// 人是走过去的，事情是自己发生的。而这 90 关整套设计的落点是
    /// "让玩家把判断落实成一个动作"（PRD 11.3：心理机制要通过机关表达）。
    /// 一个不需要按下去的机关，表达不了"我决定了"。
    ///
    /// 而且自动触发会误伤：9-3 只是路过目标箱就被带上了，
    /// 9-1 想走近读一张修改卡，一读就等于处理掉了——玩家根本没机会选。
    ///
    /// 现在统一走这个工程既有的交互键：手机是「用」，桌面是 R
    /// （E 在战斗控制器里是踢击，见 ShameInteractable 的注释）。
    ///
    /// 【走近必须先解释它是什么】
    /// 玩家原话："物理场景中很多文字标识不知道有什么作用"。
    /// 所以牌面只给名字，靠近时用字幕补一句"它是干什么的、按下去会怎样"，
    /// 停留时长由 HUDController.SubtitleSeconds 按字数算。
    /// </summary>
    public class InternalProp : MonoBehaviour
    {
        public string pfName = "";
        public InternalPropKind kind;
        public string levelId = "";
        public string boundTrigger = "";
        public string label = "";
        /// <summary>走近时那句解释的覆盖（空 = 按类别取）。</summary>
        public string explain = "";

        bool _used;
        float _lastHint = -99f;
        /// <summary>上一帧还在不在范围里：用来在"刚走近"的那一刻立刻解释，而不是等冷却。</summary>
        bool _inRange;
        /// <summary>目标箱已经被带上：从此跟在玩家身侧走，直到装上车。</summary>
        bool _carried;
        /// <summary>它原本待的地方。被最后检查者打掉时要退回去。</summary>
        Vector3 _home;
        Vector3 _knockTo;
        bool _knocked;

        /// <summary>当前被玩家搬着的那个箱子（全场最多一个）。</summary>
        public static InternalProp Carried { get; private set; }

        public static InternalProp Create(Vector3 pos, string pfName, InternalPropKind kind,
            InternalLevelData lv, string boundTrigger, string labelOverride = null,
            string explainOverride = null)
        {
            string label; Color color; Vector3 size;
            InternalProps.Style(kind, out label, out color, out size);
            if (kind == InternalPropKind.GateConsole) label = InternalProps.GateLabel(pfName);
            if (!string.IsNullOrEmpty(labelOverride)) label = labelOverride;

            var go = InternalProps.LabeledBlock("InternalProp_" + pfName, pos, size, color,
                kind == InternalPropKind.GateConsole ? 0.85f : 0.45f, label);

            var p = go.AddComponent<InternalProp>();
            p.pfName = pfName;
            p.kind = kind;
            p.levelId = lv != null ? lv.levelId : "";
            p.boundTrigger = boundTrigger;
            p.label = label;
            p.explain = explainOverride;
            p._home = go.transform.position;
            return p;
        }

        void Update()
        {
            var runner = InternalLevelRunner.Active;
            if (runner == null || runner.Level == null || runner.Level.levelId != levelId) return;

            var player = AdversityRoad.Core.ActorRegistry.Player;
            if (player == null) return;

            // 被最后检查者打掉之后：自己飞回检查区，玩家得重新去搬
            if (_knocked)
            {
                transform.position = Vector3.MoveTowards(transform.position, _knockTo,
                    9f * Time.deltaTime);
                if ((transform.position - _knockTo).sqrMagnitude < 0.09f)
                {
                    _knocked = false;
                    _used = false;          // 可以再搬一次
                    _inRange = false;
                }
                return;
            }

            // 带上的箱子跟着走：这一关的核心机制写的是"推、搬、绕、攀"，
            // 那就得看得见箱子在被搬。跟随不用刚体——这关要的是"它在我手上"，
            // 不是一套推箱子物理。
            if (_carried)
            {
                Vector3 want = player.transform.position
                             + player.transform.forward * 1.3f + Vector3.up * 0.9f;
                transform.position = Vector3.Lerp(transform.position, want,
                    1f - Mathf.Exp(-8f * Time.deltaTime));
            }

            float d = Vector3.Distance(transform.position, player.transform.position);

            if (d > InteractRange) { _inRange = false; return; }

            // ---- 进入范围：先解释这是什么，再告诉他按什么 ----
            // 带上的箱子永远贴在身边，对它重复播报会变成死循环——跳过。
            bool canUse = !_used && !_carried;
            if (!_carried && (!_inRange || Time.time - _lastHint > 9f))
            {
                _inRange = true;
                _lastHint = Time.time;
                string line = "【" + label + "】" + Hint(runner);
                if (canUse) line += "　——按【用】/ R";
                GameEvents.RaiseSubtitle(line);
            }

            if (!canUse) return;
            if (Input.GetKeyDown(KeyCode.R) || Mobile.MobileInput.GetDown("Interact"))
                Use(runner);
        }

        /// <summary>可交互距离。比原来的 2.2 米放宽一点：现在要玩家自己按，够得着才不别扭。</summary>
        public const float InteractRange = 3.4f;

        /// <summary>
        /// 走近时的解释：**这是什么、为什么在这儿、按下去会怎样**。
        ///
        /// 玩家原话："物理场景中很多文字标识不知道有什么作用，玩家很难明白"。
        /// 牌面只有两三个字（"提交台""目标箱"），那是名字，不是意思。
        /// 所以这里补的不是又一个名字，而是**它在这一关里承担什么**——
        /// 每一句都要能回答"我为什么要理它"。
        /// 停留时长由 HUDController.SubtitleSeconds 按字数算，长句子会自动留久一点。
        /// </summary>
        string Hint(InternalLevelRunner runner)
        {
            if (!string.IsNullOrEmpty(explain)) return explain;
            switch (kind)
            {
                case InternalPropKind.GateConsole:
                    return "这一关的终点。按下去就算交付——" + runner.Level.Objective;
                case InternalPropKind.DoneLock:
                    return "把「做到什么算完」钉死在这里。不钉，提交台永远觉得还差一点。";
                case InternalPropKind.Checkpoint:
                    return "记下你走到这儿了。之后失手，从这里继续，不是从头再来。";
                case InternalPropKind.DisengageGate:
                    return "撤退用的门。走它不算失败，但要带着「什么时候回来」才算数。";
                case InternalPropKind.Evidence:
                    return "把「我以为会怎样」和「实际怎样」摆在一起的地方。";
                case InternalPropKind.DecisionBoard:
                    return "先在这儿定好判据，再去选。判据没定就选，选完还会反悔。";
                case InternalPropKind.TriggerObject:
                    return "那条自动行为链的起点。看清它，你才有得选。";
                case InternalPropKind.ArchiveVault:
                    return "必要的事做完了，剩下的收进来——不是没处理，是到此为止。";
                case InternalPropKind.Decoy:
                    return "它看起来也该做。做了不推进这一关，只把时间花掉。";
                case InternalPropKind.Cargo:
                    return _carrying.Contains(runner.Level.levelId)
                        ? "已经在你手上了，送到装车月台去。"
                        : "今天真正要交出去的那一箱。带上它——别的旧箱不用管。";
                default:
                    return "在这儿动手做出第一版。先有东西，才谈得上改。";
            }
        }

        /// <summary>
        /// 按下去之后那一句**意义**：这一步刚才发生的事，对应这一关想教的是什么。
        ///
        /// 【为什么要专门有这一句】
        /// 玩家的要求原话是"通过交互让玩家深刻理解这一关卡的意义，并让玩家落实行动实践中"。
        /// 光有"完成了"是反馈，不是意义。反馈说的是"系统收到了"，
        /// 意义说的是"你刚才做的这件事，在现实里叫什么"。
        /// 两句都要，而且意义那句要短——它是一记敲钉子，不是一段课文。
        /// </summary>
        static string Meaning(InternalPropKind k)
        {
            switch (k)
            {
                case InternalPropKind.GateConsole:
                    return "交付的定义是「交出去了」，不是「我满意了」。";
                case InternalPropKind.DoneLock:
                    return "先定完成标准，才不会被无穷的「还能更好」拖住。";
                case InternalPropKind.Checkpoint:
                    return "把失误关在一段里，它就毁不掉整条路。";
                case InternalPropKind.DisengageGate:
                    return "带着重返条件离开叫撤退，不带就是拖延。";
                case InternalPropKind.Evidence:
                    return "预测和现实对一次，判断才会长进。";
                case InternalPropKind.DecisionBoard:
                    return "先有判据再选，选完就不必反复回头。";
                case InternalPropKind.TriggerObject:
                    return "看清起点，那条链就不再是自动的。";
                case InternalPropKind.ArchiveVault:
                    return "收起来不是逃避，是承认必要的部分已经做完。";
                case InternalPropKind.Decoy:
                    return "「顺手也做了」是目标被换掉最常见的方式。";
                case InternalPropKind.Cargo:
                    return "同时搬所有箱子，等于一箱也送不出去。";
                default:
                    return "先做出粗糙的第一版，剩下的才有东西可改。";
            }
        }

        /// <summary>
        /// 被最后检查者打掉：箱子脱手，退回检查区。
        ///
        /// PRD 9-3 的敌人栏原文："最后检查者×3 **会把目标箱推回检查区**"。
        /// 这是这一关战斗与机关咬合的地方：你不是在清怪，
        /// 你是在**护着一件东西穿过一段有人拦你的路**——
        /// 手上有箱子的时候打不还手，所以得躲、得绕、得挑时机。
        /// </summary>
        public void KnockCargoAway()
        {
            if (!_carried || kind != InternalPropKind.Cargo) return;
            _carried = false;
            _knocked = true;
            _knockTo = _home;
            if (Carried == this) Carried = null;

            var runner = InternalLevelRunner.Active;
            if (runner != null && runner.Level != null) _carrying.Remove(runner.Level.levelId);

            GameEvents.RaiseSubtitle("〔最后检查者〕「你确定这就能交了吗」——箱子被推回了检查区。");
            GameAudio.Play(GameAudio.Sfx.HeavyHit, 0.6f);
        }

        /// <summary>做成了一步：先说发生了什么，再敲一句这件事的意义。</summary>
        void Done(string what)
        {
            GameEvents.RaiseSubtitle("✔ " + what + "　◇ " + Meaning(kind));
            GameAudio.Play(GameAudio.Sfx.Cast, 0.45f);
        }

        void Use(InternalLevelRunner runner)
        {
            _used = true;

            switch (kind)
            {
                case InternalPropKind.GateConsole:
                    // 这一关自己有判据就问它（9-1 的粗稿与阻断项、9-2 的停手时机……）。
                    // 机关只负责问，不替某一关写规则——见 ILevelGate 的注释。
                    if (runner.Gate != null)
                    {
                        string why;
                        if (!runner.Gate.CanSubmit(out why))
                        {
                            _used = false;
                            _lastHint = Time.time;
                            GameEvents.RaiseSubtitle("【" + label + "】" + why);
                            return;
                        }
                        string meaning = runner.Gate.SubmitMeaning();
                        if (runner.ExecutionGate() && !string.IsNullOrEmpty(meaning))
                            GameEvents.RaiseSubtitle("✔ 交付完成　◇ " + meaning);
                        return;
                    }
                    // Done 锁还没合上就不给过：这一关的顺序本身就是它要教的东西
                    if (!GateReady(runner))
                    {
                        _used = false;
                        _lastHint = Time.time;
                        GameEvents.RaiseSubtitle("【" + label + "】" +
                            (NeedsCargo(runner) && !_carrying.Contains(runner.Level.levelId)
                                ? "空着手——先把目标箱带过来。"
                                : "还不能按——先把完成标准钉下来。"));
                        return;
                    }
                    if (!runner.ExecutionGate())
                    {
                        _used = false;
                        GameEvents.RaiseSubtitle("【" + label + "】条件还没到。");
                    }
                    return;

                case InternalPropKind.DoneLock:
                    // 这一关有自己的循环时，Done 不是"走过去就算"——
                    // 它由循环判定（9-1 是"两个阻断项处理完"）。这里只如实转述还差什么。
                    if (runner.Gate != null)
                    {
                        _used = false;
                        _lastHint = Time.time;
                        string why2;
                        GameEvents.RaiseSubtitle("【" + label + "】" +
                            (runner.Gate.CanSubmit(out why2)
                                ? "已达到完成标准——可以去提交了。"
                                : why2));
                        return;
                    }
                    _doneLocked.Add(runner.Level.levelId);
                    runner.MarkGoalAction("锁定完成标准");
                    Done("完成标准已钉下，现在可以去提交了");
                    return;

                case InternalPropKind.DisengageGate:
                    // 撤退要带重返条件，否则它是拖延不是战略（第 9 节）
                    runner.StrategicWithdraw("下一次进入时从这里继续");
                    return;

                case InternalPropKind.Decoy:
                    // 诱饵不推进目标，只把方向权交出去一点——它要有代价，但不惩罚
                    runner.MarkGoalOffline(0, "这个也顺手做了吧", "去处理了诱饵", 0.3f);
                    GameEvents.RaiseSubtitle("【" + label + "】做完了，但当前目标一步没动。　◇ "
                        + Meaning(kind));
                    return;

                case InternalPropKind.Cargo:
                    // 目标箱不是终点，是**要被搬到终点去的东西**。
                    // 带上之后它跟着走——"推、搬、绕、攀"这句核心机制要看得见，
                    // 不能只是走过去弹一行字。
                    _carrying.Add(runner.Level.levelId);
                    _carried = true;
                    Carried = this;
                    // _used 保持 true：箱子只能被"带上"一次。
                    // 之前这里置回 false，而箱子带上之后就跟在玩家身边、距离恒小于 2.2m——
                    // 于是 Use 每帧重跑，字幕和 MarkGoalAction 一秒刷几十次。
                    if (!string.IsNullOrEmpty(boundTrigger)) runner.FireTrigger(boundTrigger);
                    runner.MarkGoalAction("带上目标箱");
                    Done("带上了【" + label + "】，送到装车月台去");
                    return;

                default:
                    // 9-1 的工作台就是草稿桌：按下去第一版才出现，墙才停
                    var l3 = Level0901BlankPage.Active;
                    if (l3 != null && kind == InternalPropKind.WorkStation &&
                        runner.Level.levelId == Level0901BlankPage.LevelId)
                    {
                        l3.MakeDraft(runner);
                        return;
                    }
                    if (!string.IsNullOrEmpty(boundTrigger)) runner.FireTrigger(boundTrigger);
                    runner.MarkGoalAction(label);
                    Done("用了【" + label + "】");
                    return;
            }
        }

        /// <summary>这一关有没有目标箱要搬。</summary>
        static bool NeedsCargo(InternalLevelRunner runner)
        {
            var plan = InternalProps.PlanFor(runner.Level);
            for (int i = 0; i < plan.Count; i++)
                if (plan[i].kind == InternalPropKind.Cargo) return true;
            return false;
        }

        /// <summary>本次进关里已经锁过完成标准的关卡（离关即清）。</summary>
        static readonly HashSet<string> _doneLocked = new HashSet<string>();

        /// <summary>本次进关里已经带上目标箱的关卡（离关即清）。</summary>
        static readonly HashSet<string> _carrying = new HashSet<string>();

        public static void ResetSession()
        {
            _doneLocked.Clear();
            _carrying.Clear();
        }

        /// <summary>
        /// Gate 能不能按。
        ///
        /// 只有当这一关**确实摆了 Done 锁**时才要求先锁——
        /// 没有 Done 锁的关卡（例如 10-1 只要跨过门槛）不该被凭空加一道门槛。
        /// </summary>
        bool GateReady(InternalLevelRunner runner)
        {
            var plan = InternalProps.PlanFor(runner.Level);
            bool needDone = false, needCargo = false;
            for (int i = 0; i < plan.Count; i++)
            {
                if (plan[i].kind == InternalPropKind.DoneLock) needDone = true;
                if (plan[i].kind == InternalPropKind.Cargo) needCargo = true;
            }
            if (needDone && !_doneLocked.Contains(runner.Level.levelId)) return false;
            if (needCargo && !_carrying.Contains(runner.Level.levelId)) return false;
            return true;
        }
    }
}
