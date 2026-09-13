using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.Core;

namespace AdversityRoad.InternalOS
{
    /// <summary>
    /// 9-1《白纸之门》的关卡循环。
    ///
    /// 【为什么必须有这个文件，而不是摆三个方块了事】
    /// 我第一版把这一关做成了"一个盒子 + 草稿桌/Done锁/提交台三个方块 + 一条字幕"。
    /// 玩家的原话是"不知道怎么玩"。回头看关卡表，这一关的玩法本来写得很清楚：
    ///
    ///   Reality→Adversity：停留越久，白墙按预制阶段向外扩展；粗稿生成后道路才收敛。
    ///   核心物理机制：创建第一版；识别 2 个 Critical 与 4 个 Cosmetic 修改；不要求全部优化。
    ///
    /// 也就是说：**你不动，房间就变大，提交台就离你越远**；你去修那四个无关紧要的，
    /// 时间在走、墙在扩；只修两个真正阻断交付的，路才收敛，才能提交。
    /// 空间本身就是完美主义的代价——这正是 PRD 11.3 第三条要的
    /// "心理机制通过空间、路径、机关表达，而非字幕"。
    /// 那三个方块只是终点，不是玩法；玩法是这中间的取舍。
    ///
    /// 【这一关教的判断】
    /// 六张修改卡摆在一起，长得一样，只有走近读了内容才分得出哪两张是真的阻断项。
    /// 判断不是"全都修"（那是完美主义），也不是"一个都不修"（那是鲁莽）——
    /// 而是**分得出哪两个真的挡住交付**。所以卡面写的是问题本身，不是"Critical/Cosmetic"标签：
    /// 标签直接印在脸上，这一关就没有判断可做了。
    /// </summary>
    public class Level0901BlankPage : MonoBehaviour
    {
        public const string LevelId = "9-1";

        /// <summary>白墙每隔多久往外推一阶（秒）。PRD 写的是"停留越久、按预制阶段扩展"。</summary>
        public const float ExpandEverySeconds = 9f;
        /// <summary>每一阶把提交区往外推多远（米）。</summary>
        public const float ExpandStep = 6f;
        /// <summary>最多扩几阶：再远就变成"走过去要半天"，那不是压力，是罚站。</summary>
        public const int MaxStages = 5;
        /// <summary>Done 最低标准：两个真正阻断交付的问题。</summary>
        public const int CriticalToDone = 2;
        /// <summary>六张修改卡的牌面。"怎么玩"卡片和 CI 门禁都引它，别各写各的。</summary>
        public const string CardLabel = "修改项";

        public static Level0901BlankPage Active { get; private set; }

        Transform _submit;          // 提交台：扩张时被往外推的那个
        Vector3 _submitHome;
        Vector3 _pushDir;
        readonly List<EditCard> _cards = new List<EditCard>();

        float _nextExpandAt;
        int _stage;

        /// <summary>粗稿已生成：扩张停止，道路收敛。</summary>
        public bool DraftMade { get; private set; }
        /// <summary>已处理的真·阻断项。</summary>
        public int CriticalFixed { get; private set; }
        public bool DoneReached => CriticalFixed >= CriticalToDone;
        public int Stage => _stage;

        // ==================== 安装 ====================

        /// <summary>在 9-1 建好之后装上这套循环。由 InternalProps 落位之后调用。</summary>
        public static void Install(Transform siteRoot, Vector3 spawn, Vector3 exit)
        {
            if (Active != null) Destroy(Active.gameObject);

            var go = new GameObject("Level0901BlankPage");
            if (siteRoot != null) go.transform.SetParent(siteRoot, true);
            var c = go.AddComponent<Level0901BlankPage>();
            Active = c;
            c.Setup(spawn, exit);
        }

        void Setup(Vector3 spawn, Vector3 exit)
        {
            _pushDir = exit - spawn;
            _pushDir.y = 0f;
            if (_pushDir.sqrMagnitude < 0.01f) _pushDir = Vector3.forward;
            _pushDir.Normalize();

            _nextExpandAt = Time.time + ExpandEverySeconds;

            // 六张修改卡摆在「修改走廊」那一段：主路径中段，左右交替。
            // 两张是真的阻断项，四张只是可优化——卡面只写问题，不写标签。
            var mid = Vector3.Lerp(spawn, exit, 0.55f);
            Vector3 right = Vector3.Cross(Vector3.up, _pushDir);
            string[] critical =
            {
                "提交入口打不开——对方根本收不到",
                "核心结论缺一段，读完不知道要做什么",
            };
            string[] cosmetic =
            {
                "标题字号比正文大两号",
                "第三段和第五段顺序可以对调",
                "配色偏灰，换一套更精神",
                "结尾少一句客套话",
            };

            for (int i = 0; i < 6; i++)
            {
                bool crit = i < critical.Length;
                string text = crit ? critical[i] : cosmetic[i - critical.Length];
                Vector3 at = mid + _pushDir * ((i / 2) * 4.5f - 4.5f)
                                 + right * ((i % 2 == 0) ? 3.2f : -3.2f);
                if (UnityEngine.AI.NavMesh.SamplePosition(at, out var hit, 10f,
                        UnityEngine.AI.NavMesh.AllAreas)) at = hit.position;
                _cards.Add(EditCard.Create(at, text, crit, this, transform));
            }

            GameEvents.RaiseSubtitle("白纸之门：先做出第一版。站着不动，这地方会一直往外长。");
        }

        /// <summary>提交台建好之后告诉这套循环——扩张要推的就是它。</summary>
        public void BindSubmitConsole(Transform submit)
        {
            _submit = submit;
            if (submit != null) _submitHome = submit.position;
        }

        // ==================== 循环 ====================

        void Update()
        {
            var runner = InternalLevelRunner.Active;
            if (runner == null || runner.Level == null || runner.Level.levelId != LevelId) return;

            // 粗稿出来之后道路收敛：不再扩，并且把提交台往回收一段
            if (DraftMade)
            {
                if (_stage > 0 && _submit != null)
                {
                    _submit.position = Vector3.MoveTowards(_submit.position, _submitHome, 4f * Time.deltaTime);
                    if ((_submit.position - _submitHome).sqrMagnitude < 0.05f) _stage = 0;
                }
                return;
            }

            if (Time.time < _nextExpandAt) return;
            _nextExpandAt = Time.time + ExpandEverySeconds;
            Expand(runner);
        }

        void Expand(InternalLevelRunner runner)
        {
            if (_stage >= MaxStages)
            {
                // 到顶就不再推了：再远只是罚站。但这一关的压力已经表达清楚。
                return;
            }
            _stage++;

            if (_submit != null) _submit.position = _submitHome + _pushDir * (ExpandStep * _stage);

            // 第一次扩张时引那句内部语言——它正是这一关的 TRG_PerfectionExpansion
            if (_stage == 1) runner.FireTrigger("TRG_PerfectionExpansion");

            GameEvents.RaiseSubtitle(_stage == 1
                ? "还没动笔，地方就变大了一点——提交台退远了。"
                : "又远了一截（第 " + _stage + " 阶）。它不是在惩罚你，它是在等第一版。");
        }

        // ==================== 三个节点 ====================

        /// <summary>草稿桌：第一版出现。扩张停止，路开始收敛。</summary>
        public void MakeDraft(InternalLevelRunner runner)
        {
            if (DraftMade) return;
            DraftMade = true;
            runner.FireTrigger("TRG_FirstDraft");
            runner.MarkGoalAction("生成第一版粗稿");
            GameEvents.RaiseSubtitle("第一版有了。墙停下来，路开始收回来——现在去分清哪些改动真的挡着交付。");
        }

        /// <summary>处理掉一个真正阻断交付的问题。</summary>
        public void FixCritical(InternalLevelRunner runner, string what)
        {
            CriticalFixed++;
            runner.MarkGoalAction("处理阻断项：" + what);
            if (CriticalFixed >= CriticalToDone)
            {
                runner.FireTrigger("TRG_DoneThreshold");
                GameEvents.RaiseSubtitle("两个阻断项都处理完了——已经达到 Done 的最低标准，可以提交了。");
            }
            else
            {
                GameEvents.RaiseSubtitle("阻断项 " + CriticalFixed + "/" + CriticalToDone + "。");
            }
        }

        /// <summary>打磨了一个可优化项：不推进 Done，只是把时间花掉了。</summary>
        public void PolishCosmetic(InternalLevelRunner runner, string what)
        {
            // 不惩罚，只如实说代价：这一关的错误策略必须"有代价但可理解"（11.3 第四条）
            runner.MarkGoalOffline(28, "这个也顺手改了吧", "打磨了可优化项：" + what, 0.25f);
            if (!DraftMade) _nextExpandAt = Mathf.Min(_nextExpandAt, Time.time + 2f);
            GameEvents.RaiseSubtitle("改好了，但它本来就不挡交付——" +
                (DraftMade ? "时间花掉了。" : "而且第一版还没有。"));
        }

        /// <summary>提交台问它能不能按。</summary>
        public bool CanSubmit(out string why)
        {
            if (!DraftMade) { why = "还没有第一版——先去草稿桌做出来。"; return false; }
            if (!DoneReached)
            {
                why = "还差 " + (CriticalToDone - CriticalFixed) + " 个真正阻断交付的问题。";
                return false;
            }
            why = "";
            return true;
        }

        void OnDestroy()
        {
            if (Active == this) Active = null;
        }
    }

    /// <summary>
    /// 一张修改卡。走近读到的是**问题本身**，不是"Critical / Cosmetic"标签——
    /// 标签印在脸上，这一关就没有判断可做了。处理它要再走近一步。
    /// </summary>
    public class EditCard : MonoBehaviour
    {
        public string text;
        public bool critical;
        Level0901BlankPage _owner;
        bool _done;
        float _lastHint = -99f;

        public static EditCard Create(Vector3 pos, string text, bool critical,
            Level0901BlankPage owner, Transform parent)
        {
            // 卡是立着的一块板：牌面就写在板上，不挂在头顶飘着。
            // 六张一模一样——分得出来靠走近读那行字，不靠颜色、不靠标签。
            var size = new Vector3(1.1f, 1.4f, 0.14f);
            var go = InternalProps.LabeledBlock("EditCard", pos, size,
                new Color(0.88f, 0.88f, 0.84f), 0.25f, Level0901BlankPage.CardLabel);

            var c = go.AddComponent<EditCard>();
            c.text = text;
            c.critical = critical;
            c._owner = owner;
            return c;
        }

        void Update()
        {
            if (_done || _owner == null) return;
            var runner = InternalLevelRunner.Active;
            if (runner == null || runner.Level == null ||
                runner.Level.levelId != Level0901BlankPage.LevelId) return;

            var player = AdversityRoad.Core.ActorRegistry.Player;
            if (player == null) return;
            float d = Vector3.Distance(transform.position, player.transform.position);
            if (d > 3.4f) return;

            if (Time.time - _lastHint > 6f)
            {
                _lastHint = Time.time;
                GameEvents.RaiseSubtitle("〔修改项〕" + text);
            }
            if (d > 1.8f) return;

            _done = true;
            // 渲染器在子物体 Body 上（根不缩放，免得把牌面上的字拉变形）
            var mr = GetComponentInChildren<MeshRenderer>();
            if (mr != null)
                mr.sharedMaterial =
                    Combat.CombatFeedback.EnergyMaterial(new Color(0.35f, 0.38f, 0.4f), 0.1f);
            if (critical) _owner.FixCritical(runner, text);
            else _owner.PolishCosmetic(runner, text);
        }
    }
}
