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

    /// <summary>计划里的一件关键物：叫什么，以及它到底做什么。</summary>
    public struct InternalPropPlan
    {
        public string pfName;
        public InternalPropKind kind;
        public InternalPropPlan(string n, InternalPropKind k) { pfName = n; kind = k; }
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
            if (lv != null && lv.levelId == Level0901BlankPage.LevelId)
                extra.Add(Level0901BlankPage.CardLabel);
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

            // 牌子贴在方块四个侧面上，不飘在头顶、不跟镜头转
            if (!string.IsNullOrEmpty(label))
                OpenWorldBuilder.SurfaceSign(root.transform, size, label);
            return root;
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

                var p = InternalProp.Create(pos, plan[i].pfName, kind, lv, trig);
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

            return made;
        }
    }

    /// <summary>
    /// 一个能按的关键物。走近给一行字，按下去真的推进这一关。
    ///
    /// 交互不做成"按键提示"，而是走近到 2.2 米自动触发一次——
    /// 这个工程的移动端没有通用交互键，别为一个机关再造一套输入。
    /// </summary>
    public class InternalProp : MonoBehaviour
    {
        public string pfName = "";
        public InternalPropKind kind;
        public string levelId = "";
        public string boundTrigger = "";
        public string label = "";

        bool _used;
        float _lastHint = -99f;
        /// <summary>目标箱已经被带上：从此跟在玩家身侧走，直到装上车。</summary>
        bool _carried;

        public static InternalProp Create(Vector3 pos, string pfName, InternalPropKind kind,
            InternalLevelData lv, string boundTrigger)
        {
            string label; Color color; Vector3 size;
            InternalProps.Style(kind, out label, out color, out size);
            if (kind == InternalPropKind.GateConsole) label = InternalProps.GateLabel(pfName);

            var go = InternalProps.LabeledBlock("InternalProp_" + pfName, pos, size, color,
                kind == InternalPropKind.GateConsole ? 0.85f : 0.45f, label);

            var p = go.AddComponent<InternalProp>();
            p.pfName = pfName;
            p.kind = kind;
            p.levelId = lv != null ? lv.levelId : "";
            p.boundTrigger = boundTrigger;
            p.label = label;
            return p;
        }

        void Update()
        {
            var runner = InternalLevelRunner.Active;
            if (runner == null || runner.Level == null || runner.Level.levelId != levelId) return;

            var player = AdversityRoad.Core.ActorRegistry.Player;
            if (player == null) return;

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

            if (d > 3.6f) return;

            // 走近先说这是什么：机关必须可读，不能靠玩家瞎撞（第 11.3 节第二条）
            // 但带上的箱子永远贴在身边，这一条对它就成了每 8 秒一句的死循环——跳过。
            if (!_carried && Time.time - _lastHint > 8f)
            {
                _lastHint = Time.time;
                GameEvents.RaiseSubtitle("【" + label + "】" + Hint(runner));
            }

            if (d > 2.2f || _used) return;
            Use(runner);
        }

        string Hint(InternalLevelRunner runner)
        {
            switch (kind)
            {
                case InternalPropKind.GateConsole:
                    return "走到这里就算数：" + runner.Level.Objective;
                case InternalPropKind.DoneLock:
                    return "先把「做到什么算完」钉死，提交台才肯开。";
                case InternalPropKind.Checkpoint:
                    return "失败从这里继续——不是从头再来。";
                case InternalPropKind.DisengageGate:
                    return "撤退从这里走，但要带着重返条件。";
                case InternalPropKind.Evidence:
                    return "预测和现实之间，差的就是这一点材料。";
                case InternalPropKind.DecisionBoard:
                    return "先定判据，再决定走哪条。";
                case InternalPropKind.TriggerObject:
                    return "自动那条链从这里起头。";
                case InternalPropKind.ArchiveVault:
                    return "必要的处理完了，剩下的收起来。";
                case InternalPropKind.Decoy:
                    return "它看起来也该做——做了就偏离当前目标。";
                case InternalPropKind.Cargo:
                    return _carrying.Contains(runner.Level.levelId)
                        ? "已经带上了——把它送到装车月台。"
                        : "带上它，送到装车月台。别的旧箱子不用管。";
                default:
                    return runner.Level.coreMechanic;
            }
        }

        void Use(InternalLevelRunner runner)
        {
            _used = true;

            switch (kind)
            {
                case InternalPropKind.GateConsole:
                    // 9-1 有自己的判据（有没有粗稿、两个阻断项处理完没有）
                    var loop = Level0901BlankPage.Active;
                    if (loop != null && runner.Level.levelId == Level0901BlankPage.LevelId)
                    {
                        string why;
                        if (!loop.CanSubmit(out why))
                        {
                            _used = false;
                            _lastHint = Time.time;
                            GameEvents.RaiseSubtitle("【" + label + "】" + why);
                            return;
                        }
                        runner.ExecutionGate();
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
                    // 9-1 的 Done 不是走过去就算：它由"两个阻断项处理完"决定
                    var l2 = Level0901BlankPage.Active;
                    if (l2 != null && runner.Level.levelId == Level0901BlankPage.LevelId)
                    {
                        _used = false;
                        _lastHint = Time.time;
                        GameEvents.RaiseSubtitle(l2.DoneReached
                            ? "【" + label + "】已达到 Done 最低标准——可以去提交了。"
                            : "【" + label + "】Done 的最低标准是处理掉两个真正阻断交付的问题（已处理 "
                              + l2.CriticalFixed + "）。");
                        return;
                    }
                    _doneLocked.Add(runner.Level.levelId);
                    runner.MarkGoalAction("锁定完成标准");
                    GameEvents.RaiseSubtitle("完成标准已钉下——现在可以去提交了。");
                    return;

                case InternalPropKind.DisengageGate:
                    // 撤退要带重返条件，否则它是拖延不是战略（第 9 节）
                    runner.StrategicWithdraw("下一次进入时从这里继续");
                    return;

                case InternalPropKind.Decoy:
                    // 诱饵不推进目标，只把方向权交出去一点——它要有代价，但不惩罚
                    runner.MarkGoalOffline(0, "这个也顺手做了吧", "去处理了诱饵", 0.3f);
                    GameEvents.RaiseSubtitle("【" + label + "】做完了，但当前目标没有前进。");
                    return;

                case InternalPropKind.Cargo:
                    // 目标箱不是终点，是**要被搬到终点去的东西**。
                    // 带上之后它跟着走——"推、搬、绕、攀"这句核心机制要看得见，
                    // 不能只是走过去弹一行字。
                    _carrying.Add(runner.Level.levelId);
                    _carried = true;
                    // _used 保持 true：箱子只能被"带上"一次。
                    // 之前这里置回 false，而箱子带上之后就跟在玩家身边、距离恒小于 2.2m——
                    // 于是 Use 每帧重跑，字幕和 MarkGoalAction 一秒刷几十次。
                    if (!string.IsNullOrEmpty(boundTrigger)) runner.FireTrigger(boundTrigger);
                    runner.MarkGoalAction("带上目标箱");
                    GameEvents.RaiseSubtitle("带上了【" + label + "】——送到装车月台去。");
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
