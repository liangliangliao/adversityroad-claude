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
        static bool _hasReturn;
        static string _insideChapterId = "";

        PlayerController _player;
        float _lastHint = -99f;

        /// <summary>玩家此刻是否正在某个生成场景内部。</summary>
        public static bool InsideSite => !string.IsNullOrEmpty(_insideChapterId);
        public static string InsideChapterId => _insideChapterId;

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

        /// <summary>在生成场景里阵亡：先回城，再由战败流程走复盘——不会复活在虚空里。</summary>
        static void OnPlayerDied(string reason)
        {
            if (InsideSite) ExitToCity();
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

        void Enter()
        {
            var inst = SiteBuilder.Find(chapterId);
            if (inst == null || inst.root == null)
            {
                GameEvents.RaiseSubtitle("这个场景还没准备好——稍等一下再试。");
                return;
            }

            _returnPoint = _player.transform.position;
            _hasReturn = true;
            _insideChapterId = chapterId;

            Teleport(_player, inst.playerSpawn);
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
            }
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

        /// <summary>从生成场景返回城区（通关、主动退出或死亡后调用）。</summary>
        public static void ExitToCity()
        {
            if (!_hasReturn) return;
            var player = FindObjectOfType<PlayerController>();
            if (player != null) Teleport(player, _returnPoint);
            if (OpenWorldBuilder.CityZoneIndex >= 0)
                ZoneBuilder.CurrentZoneId = ZoneBuilder.ZoneIdOf(OpenWorldBuilder.CityZoneIndex);

            AI.DialogueLibrary.ClearChapterLines();
            _hasReturn = false;
            _insideChapterId = "";
            GameEvents.RaiseSubtitle("你从那个地方走了出来——它是为这条旅程建的，也会随这条旅程收起。");
        }

        static void Teleport(PlayerController player, Vector3 to)
        {
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.position = to;
            if (cc != null) cc.enabled = true;
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
