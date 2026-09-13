using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.Core;

namespace AdversityRoad.InternalOS
{
    /// <summary>
    /// 9-2《永久校稿室》的关卡循环。
    ///
    /// 【这一关的核心思想，以及怎么把它变成一个动作】
    /// 关卡表写的是："将修改卡分成 Critical/Useful/Cosmetic；边际收益下降后停止校稿"，
    /// 通关条件是"处理最后一个真正阻断项后**主动停止**修改并提交"。
    ///
    /// 注意它和 9-1 的区别：9-1 教的是"别不开始"，9-2 教的是"别不结束"。
    /// 所以这一关不能设计成"处理满 N 个就放行"——那样停手是系统替你做的，
    /// 玩家学不到任何东西。真正要玩家做的那个动作是：
    ///
    ///     场上还剩着能改的东西，而你自己决定不改了，走去按提交。
    ///
    /// 【代价要看得见，而且不能是一个数字】
    /// 我上一版把边际收益做成了"可交付度 +7（共 35），上一次 +22，在往下掉"。
    /// 那是文档语言：玩家在读一条曲线，不是在体会它。
    ///
    /// 现在换成一件看得见的事——**每打磨一处不挡交付的地方，屋里就多进来一只校稿幽灵**。
    /// 收益越来越小，而屋里的人越来越多、路越来越难走。
    /// "再改下去不划算"于是不需要任何一句说明，玩家自己会感觉到。
    /// 阻断项不招幽灵：处理它们正是这一关要你做的事，不该有惩罚。
    ///
    /// 提交台任何时候都能按（只要两处阻断项处理完了）——**停手永远是一个选项**。
    /// 交付之后按"你主动留下了多少没改"给不同的意义句：剩得越多，这一关学得越透。
    ///
    /// gain / Quality 仍然留着记账（交付时用来分档），但不再摆到玩家面前当主指标。
    /// </summary>
    public class Level0902ProofRoom : MonoBehaviour, ILevelGate
    {
        public const string LevelId = "9-2";
        /// <summary>牌面。CI 门禁与"怎么玩"卡片都引它。</summary>
        public const string CardLabel = "修改项";
        /// <summary>两个真正阻断交付的问题，处理完就可以交。</summary>
        public const int CriticalToDone = 2;

        public static Level0902ProofRoom Active { get; private set; }

        /// <summary>已处理的阻断项。</summary>
        public int CriticalFixed { get; private set; }
        /// <summary>已处理的非阻断项（有用的 + 润色的）。</summary>
        public int OptionalFixed { get; private set; }
        /// <summary>场上还剩几张没处理。</summary>
        public int Remaining { get; private set; }
        /// <summary>内部记账用的可交付度；不展示给玩家（见类注释）。</summary>
        public int Quality { get; private set; }

        public static void Install(Transform siteRoot, Vector3 spawn, Vector3 exit)
        {
            if (Active != null) Destroy(Active.gameObject);

            var go = new GameObject("Level0902ProofRoom");
            if (siteRoot != null) go.transform.SetParent(siteRoot, true);
            var c = go.AddComponent<Level0902ProofRoom>();
            Active = c;
            var runner = InternalLevelRunner.Active;
            if (runner != null) runner.Gate = c;
            c.Setup(spawn, exit);
        }

        void Setup(Vector3 spawn, Vector3 exit)
        {
            Vector3 fwd = exit - spawn;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);

            // 八张卡：2 个阻断、3 个有用、3 个润色。
            // 卡面一律只写"修改项"，内容要走近才读得到——
            // 和 9-1 同一条规矩：分得出来靠读，不靠标签。
            var items = new List<ProofItem>
            {
                new ProofItem("结论里引用的数字和附表对不上", 28, true),
                new ProofItem("对方要的那一节整段缺失", 22, true),
                new ProofItem("第二部分的论据顺序颠倒了，读起来费劲", 7, false),
                new ProofItem("有两处术语前后不一致", 5, false),
                new ProofItem("图表缺少单位说明", 3, false),
                new ProofItem("标题可以更精神一点", 1, false),
                new ProofItem("正文行距再松一档更好看", 1, false),
                new ProofItem("结尾可以加一句客套话", 0, false),
            };
            Remaining = items.Count;

            Vector3 mid = Vector3.Lerp(spawn, exit, 0.5f);
            for (int i = 0; i < items.Count; i++)
            {
                Vector3 at = mid + fwd * ((i / 2) * 5f - 7.5f)
                                 + right * ((i % 2 == 0) ? 3.6f : -3.6f);
                if (UnityEngine.AI.NavMesh.SamplePosition(at, out var hit, 10f,
                        UnityEngine.AI.NavMesh.AllAreas)) at = hit.position;
                ProofCard.Create(at, items[i], this, transform);
            }

            GameEvents.RaiseSubtitle(
                "永久校稿室：场上八处可以改的地方。真正挡住交付的只有两处，" +
                "其余的改了也只是更好看一点。什么时候停手，由你决定。");
        }

        /// <summary>处理掉一项。非阻断项会招来一只校稿幽灵——代价是看得见的，不是数字。</summary>
        public void Apply(InternalLevelRunner runner, ProofItem item)
        {
            Remaining--;
            Quality += item.gain;
            if (item.critical) CriticalFixed++;
            else OptionalFixed++;

            runner.MarkGoalAction((item.critical ? "处理阻断项：" : "打磨非阻断项：") + item.text);
            if (item.critical && CriticalFixed >= CriticalToDone)
                runner.FireTrigger(FirstTriggerOf(runner));

            // 【代价不写成数字，写成屋里多出来的一只幽灵】
            //
            // 上一版这里报的是"可交付度 +7（共 35）、上一次 +22，在往下掉"。
            // 那是文档语言：玩家在读一条曲线，不是在体会它。
            // 现在改成——**每打磨一处不挡交付的地方，屋里就多一只校稿幽灵**。
            // 边际收益递减于是变成一件看得见的事：收益越来越小，屋里的人越来越多。
            // 阻断项不会招来幽灵：处理它们是这一关要你做的事，不该有惩罚。
            if (!item.critical)
            {
                InternalEnemyTactics.SpawnOneMoreGhost(
                    InternalChapterBridge.ChapterIdOfLevel(LevelId), runner.Chapter);
            }

            string tail;
            if (CriticalFixed < CriticalToDone)
                tail = "　还差 " + (CriticalToDone - CriticalFixed) + " 处真正挡住交付的问题。";
            else
                tail = "　两处阻断项都处理完了——现在随时可以去【提交台】。还剩 "
                     + Remaining + " 处可以改。";

            GameEvents.RaiseSubtitle((item.critical
                ? "✔ 这一处真的挡着交付，改对了：" + item.text
                : "✔ 改好了：" + item.text + "　——又进来一只校稿幽灵。") + tail);
            GameAudio.Play(GameAudio.Sfx.Cast, 0.45f);
        }

        static string FirstTriggerOf(InternalLevelRunner runner)
        {
            var ids = runner.Level != null ? runner.Level.triggerIds : null;
            return ids != null && ids.Count > 0 ? ids[0] : "";
        }

        // ==================== 通关判据 ====================

        public bool CanSubmit(out string why)
        {
            if (CriticalFixed < CriticalToDone)
            {
                why = "还差 " + (CriticalToDone - CriticalFixed) +
                      " 个真正挡住交付的问题。其余那些只是「还能更好」，不拦着交付。";
                return false;
            }
            why = "";
            return true;
        }

        /// <summary>
        /// 交付那一刻的意义句：按**你主动留下了多少没改**分档。
        ///
        /// 留得越多，说明越早认出"再改下去也没用"。这一关想让玩家带走的
        /// 就是这个判断，所以奖励必须落在"停手"上，而不是落在"改得多"上。
        /// </summary>
        public string SubmitMeaning()
        {
            if (Remaining >= 4)
                return "你留下 " + Remaining + " 处没改就交了出去——这正是这一关要练的：" +
                       "认出「再改下去也不会更能交付」的那个点。";
            if (Remaining >= 1)
                return "你留下 " + Remaining + " 处没改。还可以更早停手——" +
                       "后面那几处每一处只涨一点点，时间却是一样的。";
            return "八处你全改完了。它确实更好看了，但能不能交付在第二处就已经定了——" +
                   "后面六处花掉的时间，是完美主义的账单。";
        }

        void OnDestroy()
        {
            if (Active == this) Active = null;
        }
    }

    /// <summary>一处可以改的地方：写了什么、改了能涨多少、是不是真的挡交付。</summary>
    public class ProofItem
    {
        public readonly string text;
        public readonly int gain;
        public readonly bool critical;

        public ProofItem(string text, int gain, bool critical)
        {
            this.text = text;
            this.gain = gain;
            this.critical = critical;
        }
    }

    /// <summary>
    /// 一张校稿卡。走近读内容，按【用】才动手。
    ///
    /// 和 9-1 的修改卡同一条规矩：**读不等于改**。
    /// 这一关全部的判断就是"读完之后决定改不改"，自动触发会把它替玩家做掉。
    /// </summary>
    public class ProofCard : MonoBehaviour
    {
        ProofItem _item;
        Level0902ProofRoom _owner;
        bool _done;
        bool _inRange;
        float _lastHint = -99f;

        public static ProofCard Create(Vector3 pos, ProofItem item,
            Level0902ProofRoom owner, Transform parent)
        {
            var size = new Vector3(1.1f, 1.4f, 0.14f);
            var go = InternalProps.LabeledBlock("ProofCard", pos, size,
                new Color(0.86f, 0.87f, 0.9f), 0.25f, Level0902ProofRoom.CardLabel);
            if (parent != null) go.transform.SetParent(parent, true);

            var c = go.AddComponent<ProofCard>();
            c._item = item;
            c._owner = owner;
            return c;
        }

        void Update()
        {
            if (_done || _owner == null) return;
            var runner = InternalLevelRunner.Active;
            if (runner == null || runner.Level == null ||
                runner.Level.levelId != Level0902ProofRoom.LevelId) return;

            var player = ActorRegistry.Player;
            if (player == null) return;
            float d = Vector3.Distance(transform.position, player.transform.position);
            if (d > InternalProp.InteractRange) { _inRange = false; return; }

            if (!_inRange || Time.time - _lastHint > 9f)
            {
                _inRange = true;
                _lastHint = Time.time;
                GameEvents.RaiseSubtitle("〔修改项〕" + _item.text +
                    "　——要改就按【用】/ R；不改就走开");
            }

            if (!(Input.GetKeyDown(KeyCode.R) || Mobile.MobileInput.GetDown("Interact"))) return;

            _done = true;
            var mr = GetComponentInChildren<MeshRenderer>();
            if (mr != null)
                mr.sharedMaterial =
                    Combat.CombatFeedback.EnergyMaterial(new Color(0.35f, 0.38f, 0.4f), 0.1f);
            _owner.Apply(runner, _item);
        }
    }
}
