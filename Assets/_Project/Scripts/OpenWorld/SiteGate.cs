using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Goals;
using AdversityRoad.Player;
using AdversityRoad.World;

namespace AdversityRoad.OpenWorld
{
    /// <summary>
    /// Site Gate：开放城区里通往**AI 现场生成的那个场景**的入口。
    ///
    /// 它长在章节所属区域的遭遇位上，牌子上写着 AI 给这个地方起的名字。
    /// 走近按 E 进去——场景是刚刚为这个目标建出来的，不是从 24 个固定关卡里挑的。
    /// 完成或退出后回到城里原地，场景整体卸载。
    /// </summary>
    public class SiteGate : MonoBehaviour
    {
        public string chapterId = "";
        public string siteName = "";
        public float range = 3.4f;

        static Vector3 _returnPoint;
        static string _returnZoneId = "";
        static bool _hasReturn;
        static string _insideChapterId = "";

        PlayerController _player;
        float _lastHint = -99f;

        /// <summary>玩家此刻是否正在某个生成场景内部。</summary>
        public static bool InsideSite => !string.IsNullOrEmpty(_insideChapterId);
        public static string InsideChapterId => _insideChapterId;

        /// <summary>
        /// 玩家所在生成场景的落点（场景还活着才给）。
        ///
        /// 掉出边沿要捞人时，唯一正确的落点是**这处场景自己的入口**——
        /// 它下面无条件铺了一块 10×10 的实地板，一定站得住。
        /// 拿区域表去反查是错的：查不到就会滑到别的区，人就被送出关卡了。
        /// </summary>
        public static bool TryCurrentSiteSpawn(out Vector3 spawn)
        {
            spawn = Vector3.zero;
            if (!InsideSite) return false;
            var inst = SiteBuilder.Find(_insideChapterId);
            if (inst == null || inst.root == null) return false;
            spawn = inst.playerSpawn;
            return true;
        }

        /// <summary>
        /// 只清"人在场景里"的状态，**不做任何传送**。
        ///
        /// 阵亡时用这条：死亡流程随后会整场重载，位置本来就会重来一遍；
        /// 但静态字段不会随重载清零，所以必须显式收干净，
        /// 否则重生之后程序仍以为玩家站在一处已经不存在的生成场景里。
        /// </summary>
        public static void ClearInsideState()
        {
            AI.DialogueLibrary.ClearChapterLines();
            UI.HUDController.SetObjective("");
            _hasReturn = false;
            _returnZoneId = "";
            _insideChapterId = "";
        }

        public static SiteGate Create(Vector3 pos, string chapterId, string siteName, Color tint)
        {
            var root = new GameObject("SiteGate_" + siteName);
            root.transform.position = pos;

            // 门框 + 能量幕：与 V1 的传送门同一套视觉语言，玩家一眼认得
            Pillar(root.transform, new Vector3(-1.7f, 1.7f, 0), tint);
            Pillar(root.transform, new Vector3(1.7f, 1.7f, 0), tint);

            var top = GameObject.CreatePrimitive(PrimitiveType.Cube);
            top.name = "GateTop";
            Object.DestroyImmediate(top.GetComponent<Collider>());
            top.transform.SetParent(root.transform, false);
            top.transform.localPosition = new Vector3(0, 3.5f, 0);
            top.transform.localScale = new Vector3(3.9f, 0.4f, 0.5f);
            top.GetComponent<MeshRenderer>().sharedMaterial =
                Combat.CombatFeedback.EnergyMaterial(tint, 0.8f);

            var veil = GameObject.CreatePrimitive(PrimitiveType.Quad);
            veil.name = "GateVeil";
            Object.DestroyImmediate(veil.GetComponent<Collider>());
            veil.transform.SetParent(root.transform, false);
            veil.transform.localPosition = new Vector3(0, 1.7f, 0);
            veil.transform.localScale = new Vector3(3.2f, 3.2f, 1f);
            veil.GetComponent<MeshRenderer>().sharedMaterial =
                Combat.CombatFeedback.EnergyMaterial(tint, 0.35f);

            OpenWorldBuilder.HomeSign(pos + new Vector3(0, 4.4f, 0), "▶ " + siteName);

            var gate = root.AddComponent<SiteGate>();
            gate.chapterId = chapterId;
            gate.siteName = siteName;
            return gate;
        }

        static void Pillar(Transform parent, Vector3 local, Color tint)
        {
            var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
            p.name = "GatePillar";
            p.transform.SetParent(parent, false);
            p.transform.localPosition = local;
            p.transform.localScale = new Vector3(0.5f, 3.4f, 0.5f);
            var mr = p.GetComponent<MeshRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var m = new Material(shader) { color = tint * 0.6f };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint * 0.6f);
            mr.sharedMaterial = m;
        }

        void OnEnable() => GameEvents.OnPlayerDied += OnPlayerDied;
        void OnDisable() => GameEvents.OnPlayerDied -= OnPlayerDied;

        /// <summary>
        /// 在生成场景里阵亡：只收状态，**不把尸体传走**。
        ///
        /// 【这是"和敌人打着打着穿越到训练武馆"的另一半成因】
        /// 老版本在这里调 ExitToCity()，而 ExitToCity 的落点是"当初从哪儿进来的"——
        /// 玩家如果是在训练武馆点开传送面板进的这一关，退出点就是训练武馆。
        /// 而复盘面板要等倒地动作播完（2~6 秒）才弹，这几秒里玩家眼睁睁看着自己
        /// 从战场被拽到一个八竿子打不着的经典关卡里。第一次报的是独居小屋，
        /// 这次是训练武馆——差别只是他那两次分别从哪儿进来的。
        /// 阵亡后本来就要整场重载，根本不需要挪人；这里只把静态状态收干净。
        /// </summary>
        static void OnPlayerDied(string reason)
        {
            if (InsideSite) ClearInsideState();
        }

        void Update()
        {
            if (_player == null)
            {
                _player = FindObjectOfType<PlayerController>();
                if (_player == null) return;
            }
            if (Vector3.Distance(transform.position, _player.transform.position) > range) return;

            if (Time.time - _lastHint > 8f)
            {
                _lastHint = Time.time;
                GameEvents.RaiseSubtitle("【" + siteName + "】这地方是为你这条旅程新长出来的——按 E 走进去。");
            }
            if (!Input.GetKeyDown(KeyCode.E) && !Mobile.MobileInput.GetDown("Interact")) return;
            Enter();
        }

        void Enter() => EnterChapter(chapterId);

        /// <summary>
        /// 进入某个生成场景。门上按 E 走这条，"传送"面板直达也走这条——
        /// 两条路必须共用同一套进场流程，否则从面板进去的玩家会少掉台词接管与规则播报，
        /// 拿到的是一个没有规则、敌人还在说通用台词的空场景。
        /// </summary>
        public static bool EnterChapter(string chapterId)
        {
            var inst = SiteBuilder.Find(chapterId);
            if (inst == null || inst.root == null)
            {
                GameEvents.RaiseSubtitle("这个场景还没准备好——稍等一下再试。");
                return false;
            }
            var player = FindObjectOfType<PlayerController>();
            if (player == null) return false;

            // 已经在别的生成场景里：先归还上一处的台词池，再进这一处
            if (InsideSite && _insideChapterId != chapterId) AI.DialogueLibrary.ClearChapterLines();

            // 返回点只记一次，且连区域 id 一起记：从经典关卡直接传送进来的玩家
            // 退出时该回到那一关，而不是被丢回开放城区
            if (!InsideSite)
            {
                _returnPoint = player.transform.position;
                _returnZoneId = ZoneBuilder.CurrentZoneId;
            }
            _hasReturn = true;
            _insideChapterId = chapterId;

            Teleport(player, inst.playerSpawn);
            ZoneBuilder.CurrentZoneId = inst.siteId;

            var goal = GoalOS.Active;
            var ch = goal != null ? goal.FindChapter(chapterId) : null;
            if (ch != null)
            {
                // AI 为这个场景写的言语攻击：进场即接管台词池，退出时归还
                AI.DialogueLibrary.SetChapterLines(ch.chapterId,
                    ch.site != null ? ch.site.externalLines : null,
                    ch.site != null ? ch.site.internalLines : null);
                GoalOS.NoteChapterAttempt(ch.chapterId);
                ShowRules(ch);
                SiteObjective.Attach(inst, ch);   // 顶部常驻目标行：打谁、还剩几个

                // 进场时把影响移速的三个因子记一笔："在生成场景里跑不动"如果真存在，
                // 成因只可能在这三个里（减益倍率 / 行动力 / 蹲伏），一行日志就能分辨。
                CloudDialogueService.AddLog("进入场景 " + inst.siteId +
                    " 落点 " + inst.playerSpawn.ToString("F0") +
                    " · 移速倍率 " + player.MoveSpeedMultiplier.ToString("F2") +
                    " · 行动力 " + Mathf.RoundToInt(player.Stats.actionPower) +
                    " · 蹲伏 " + (player.IsCrouched ? "是" : "否"));
            }
            return true;
        }

        /// <summary>进场先把规则说清楚：关卡规则是玩家能读到的东西，不是藏在代码里的。</summary>
        static void ShowRules(GoalChapterData ch)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("〔").Append(ch.site != null ? ch.site.siteName : ch.chapterName).Append("〕");
            sb.Append(ch.successCondition);
            GameEvents.RaiseSubtitle(sb.ToString());

            if (ch.site == null) return;
            var rules = ch.site.rules;
            for (int i = 0; i < rules.Count && i < 3; i++)
            {
                string r = rules[i];
                var host = SiteBuilder.Find(ch.chapterId);
                if (host != null && host.root != null)
                    host.root.AddComponent<DelayedLine>().Setup("规则 " + (i + 1) + "：" + r, 3f + i * 3.5f);
            }
        }

        /// <summary>从生成场景返回城区（通关或主动退出时调用；阵亡不走这条）。</summary>
        public static void ExitToCity()
        {
            if (!_hasReturn) return;
            var player = FindObjectOfType<PlayerController>();
            if (player != null) Teleport(player, _returnPoint);
            if (!string.IsNullOrEmpty(_returnZoneId))
                ZoneBuilder.CurrentZoneId = _returnZoneId;
            else if (OpenWorldBuilder.CityZoneIndex >= 0)
                ZoneBuilder.CurrentZoneId = ZoneBuilder.ZoneIdOf(OpenWorldBuilder.CityZoneIndex);

            ClearInsideState();
            GameEvents.RaiseSubtitle("你从那个地方走了出来——它是为这条旅程建的，也会随这条旅程收起。");
        }

        static void Teleport(PlayerController player, Vector3 to)
        {
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.position = to;
            if (cc != null) cc.enabled = true;
            player.NotifyTeleported();
        }
    }

    /// <summary>
    /// 通关后延时送客：让胜利演出播完，再把玩家送回城区并卸载这处场景。
    /// 场景是为这条旅程建的，也随这条旅程收起——不常驻内存。
    /// </summary>
    public class SiteExitDelay : MonoBehaviour
    {
        string _chapterId;
        float _at;

        public void Setup(string chapterId, float delay)
        {
            _chapterId = chapterId;
            _at = Time.time + delay;
        }

        void Update()
        {
            if (Time.time < _at) return;
            SiteGate.ExitToCity();
            ProceduralQuestAssembler.DespawnChapter(_chapterId);
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 关卡目标行：站在生成场景里时，顶部常驻显示"打谁 · 还剩几个"。
    ///
    /// 玩家的原话是"敌人在哪里呢？怎么挑战关卡呢？"——那不是敌人没生成，
    /// 是**没人告诉他要干什么、还差多少**。进场那两句字幕几秒就滚没了，
    /// 而目标必须是随时抬头就能确认的一行字。剩余数归零时直接指出下一步。
    /// </summary>
    public class SiteObjective : MonoBehaviour
    {
        SiteInstance _site;
        string _chapterId = "";
        string _title = "";
        float _next;

        public static void Attach(SiteInstance site, GoalChapterData ch)
        {
            if (site == null || site.root == null) return;
            var o = site.root.GetComponent<SiteObjective>() ?? site.root.AddComponent<SiteObjective>();
            o._site = site;
            o._chapterId = ch.chapterId;
            // 顶部目标行也用章节名：面板、日志、HUD 三处必须是同一个名字，
            // 否则玩家没法把"我点的那一关"和"日志里生成的那一章"对上号
            o._title = ch.chapterName;
            o._next = 0f;
        }

        /// <summary>把「目标相对镜头的方位角」变成一个一眼能懂的箭头。</summary>
        static string Arrow(float angle)
        {
            float a = Mathf.Repeat(angle + 180f, 360f) - 180f;   // 归一到 [-180,180]
            if (a > -22.5f && a <= 22.5f) return "↑ ";
            if (a > 22.5f && a <= 67.5f) return "↗ ";
            if (a > 67.5f && a <= 112.5f) return "→ ";
            if (a > 112.5f && a <= 157.5f) return "↘ ";
            if (a > -67.5f && a <= -22.5f) return "↖ ";
            if (a > -112.5f && a <= -67.5f) return "← ";
            if (a > -157.5f && a <= -112.5f) return "↙ ";
            return "↓ ";
        }

        void OnDisable() => UI.HUDController.SetObjective("");

        void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + 0.5f;

            // 只在玩家真的站在这处场景里时显示——回到城里就该收起来
            if (!SiteGate.InsideSite || SiteGate.InsideChapterId != _chapterId)
            { UI.HUDController.SetObjective(""); return; }

            var player = FindObjectOfType<PlayerController>();
            int alive = 0;
            string bossName = "";
            Transform target = null;
            float bestSqr = float.MaxValue;
            bool targetIsBoss = false;

            foreach (var e in _site.root.GetComponentsInChildren<AI.EnemyController>(false))
            {
                if (e == null || e.State == AI.EnemyState.Dead) continue;
                alive++;
                bool isBoss = e.GetComponent<ChapterGateEnemy>() != null;
                if (isBoss && e.profile != null) bossName = e.profile.displayName;
                if (player == null) continue;

                // Boss 优先当指向目标；没有 Boss 时指最近的一个
                float sqr = (e.transform.position - player.transform.position).sqrMagnitude;
                if (isBoss && !targetIsBoss) { target = e.transform; bestSqr = sqr; targetIsBoss = true; }
                else if (isBoss == targetIsBoss && sqr < bestSqr) { target = e.transform; bestSqr = sqr; }
            }

            if (alive == 0)
            {
                UI.HUDController.SetObjective("◆ " + _title + " —— 这里清空了，从来路走出去");
                return;
            }

            // 方向 + 距离：这才是"敌人在哪里"的答案。
            // 户外场景放大之后敌人常在二三十米外、隔着一栋楼，只报"剩余 3"
            // 等于让玩家在一片空地上自己找——玩家的原话就是"连战斗入口都找不到"。
            string where = "";
            if (target != null && player != null)
            {
                Vector3 to = target.position - player.transform.position;
                to.y = 0f;
                float dist = to.magnitude;
                var cam = player.cameraTransform;
                Vector3 fwd = cam != null ? cam.forward : player.transform.forward;
                fwd.y = 0f;
                float ang = Vector3.SignedAngle(fwd.normalized, to.normalized, Vector3.up);
                where = " · " + Arrow(ang) + Mathf.RoundToInt(dist) + "m";
            }

            UI.HUDController.SetObjective("◆ " + _title + " —— " +
                (string.IsNullOrEmpty(bossName) ? "击败挡路的" : "击败【" + bossName + "】") +
                " · 剩余 " + alive + where);
        }
    }

    /// <summary>延时播一句字幕（进场规则逐条展示，不一次糊满屏）。</summary>
    public class DelayedLine : MonoBehaviour
    {
        string _line;
        float _at;
        bool _done;

        public void Setup(string line, float delay)
        {
            _line = line;
            _at = Time.time + delay;
        }

        void Update()
        {
            if (_done || Time.time < _at) return;
            _done = true;
            GameEvents.RaiseSubtitle(_line);
            Destroy(this);
        }
    }
}
