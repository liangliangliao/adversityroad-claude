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
            if (Has(n, "Criteria", "Decision", "Reversible", "Irreversible", "Compass",
                    "Calibrat", "Sorter"))
                return InternalPropKind.DecisionBoard;
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
                default:
                    label = "工作台"; color = new Color(0.7f, 0.75f, 0.8f);
                    size = new Vector3(1.6f, 0.9f, 0.9f); return;
            }
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
                made++;
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

        public static InternalProp Create(Vector3 pos, string pfName, InternalPropKind kind,
            InternalLevelData lv, string boundTrigger)
        {
            string label; Color color; Vector3 size;
            InternalProps.Style(kind, out label, out color, out size);

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "InternalProp_" + pfName;
            go.transform.position = pos + Vector3.up * (size.y * 0.5f);
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial =
                Combat.CombatFeedback.EnergyMaterial(color, kind == InternalPropKind.GateConsole ? 0.85f : 0.45f);
            // 碰撞体留着当"站得上去"的实体，但别挡住导航
            var col = go.GetComponent<Collider>();
            if (col != null) col.isTrigger = false;

            OpenWorldBuilder.FollowSign(go.transform, new Vector3(0, size.y * 0.5f + 0.9f, 0), label);

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
            float d = Vector3.Distance(transform.position, player.transform.position);

            if (d > 3.6f) return;

            // 走近先说这是什么：机关必须可读，不能靠玩家瞎撞（第 11.3 节第二条）
            if (Time.time - _lastHint > 8f)
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
                    return "走到这里就算数：" + runner.Level.realityVictory;
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
                    // Done 锁还没合上就不给过：这一关的顺序本身就是它要教的东西
                    if (!GateReady(runner))
                    {
                        _used = false;
                        _lastHint = Time.time;
                        GameEvents.RaiseSubtitle("【" + label + "】还不能按——先把完成标准钉下来。");
                        return;
                    }
                    if (!runner.ExecutionGate())
                    {
                        _used = false;
                        GameEvents.RaiseSubtitle("【" + label + "】条件还没到。");
                    }
                    return;

                case InternalPropKind.DoneLock:
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

                default:
                    if (!string.IsNullOrEmpty(boundTrigger)) runner.FireTrigger(boundTrigger);
                    runner.MarkGoalAction(label);
                    return;
            }
        }

        /// <summary>本次进关里已经锁过完成标准的关卡（离关即清）。</summary>
        static readonly HashSet<string> _doneLocked = new HashSet<string>();

        public static void ResetSession() => _doneLocked.Clear();

        /// <summary>
        /// Gate 能不能按。
        ///
        /// 只有当这一关**确实摆了 Done 锁**时才要求先锁——
        /// 没有 Done 锁的关卡（例如 10-1 只要跨过门槛）不该被凭空加一道门槛。
        /// </summary>
        bool GateReady(InternalLevelRunner runner)
        {
            var plan = InternalProps.PlanFor(runner.Level);
            for (int i = 0; i < plan.Count; i++)
                if (plan[i].kind == InternalPropKind.DoneLock)
                    return _doneLocked.Contains(runner.Level.levelId);
            return true;
        }
    }
}
