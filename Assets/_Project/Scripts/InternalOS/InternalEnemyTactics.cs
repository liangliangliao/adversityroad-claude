using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.AI;
using AdversityRoad.Core;
using AdversityRoad.Goals;          // ChapterModuleLibrary
using AdversityRoad.Personalization;

namespace AdversityRoad.InternalOS
{
    /// <summary>
    /// 让这一关的敌人和这一关的机关**咬在一起**。
    ///
    /// 【为什么必须有这个文件】
    /// 玩家的原话是"和战斗完全脱节"——说得对，而且根因不是设计缺失，是**我没实现**。
    /// 关卡表的"敌人/干扰"栏本来写得很具体：
    ///
    ///   9-1 白纸哨兵×2，主要制造错误提示与打断；
    ///   9-2 校稿幽灵 / 反复修改节点；
    ///   9-3 最后检查者×3 **会把目标箱推回检查区**；
    ///   9-4 全或无刽子手会破坏部分路线但始终留可继续路径。
    ///
    /// 这些行为一条都没做。场上只是几只通用小怪站着，打不打都一样——
    /// 于是"不以清怪定义胜利"被做成了"战斗根本没有意义"。
    /// 这两件事差得很远：前者是**赢的方式不是清场**，
    /// 后者是**打架这件事被从关卡里摘了出去**。
    ///
    /// 正确的关系是：敌人**妨碍你完成那个动作**。
    /// 你打它们不是为了清场，是为了挣出把事情做完的那点空间和时间。
    /// 这样战斗系统才重新有用，而胜利条件仍然不是清怪。
    /// </summary>
    public static class InternalEnemyTactics
    {
        /// <summary>场景建好、敌人放完之后调一次。</summary>
        public static void Attach(InternalLevelData lv, string chapterId, List<GameObject> enemies)
        {
            if (lv == null || enemies == null) return;

            for (int i = 0; i < enemies.Count; i++)
            {
                var go = enemies[i];
                if (go == null) continue;
                var ec = go.GetComponent<EnemyController>();
                if (ec == null) continue;
                // Boss 有自己的一套，别覆盖
                if (go.GetComponent<OpenWorld.ChapterGateEnemy>() != null) continue;

                switch (lv.levelId)
                {
                    case Level0901BlankPage.LevelId:
                        go.AddComponent<BlankPageSentinel>().chapterId = chapterId;
                        break;
                    case Level0902ProofRoom.LevelId:
                        go.AddComponent<ProofGhost>().chapterId = chapterId;
                        break;
                    case "9-3":
                        go.AddComponent<FinalChecker>();
                        break;
                }
            }
        }

        /// <summary>9-2 用：多出来的那只幽灵放在玩家附近但不贴脸。</summary>
        public static void SpawnOneMoreGhost(string chapterId, InternalChapterInfo ch)
        {
            var player = ActorRegistry.Player;
            if (player == null || ch == null) return;

            EnemyType ext, inner, boss;
            ChapterModuleLibrary.SuggestEnemies(InternalChapterBridge.AxisOf(ch),
                out ext, out inner, out boss);

            float ang = Random.value * Mathf.PI * 2f;
            Vector3 at = player.transform.position
                       + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 11f;

            var go = OpenWorld.ProceduralQuestAssembler.SpawnExtra(
                chapterId, inner, EnemyTier.Standard, at);
            if (go != null) go.AddComponent<ProofGhost>().chapterId = chapterId;
        }

        /// <summary>
        /// 9-1 用：倒下的哨兵过一会儿回来一只（只在还没做出第一版时）。
        ///
        /// 用一个挂在场景上的小协程载体来等——敌人自己马上就要被销毁，
        /// 在它身上开协程等不到时间到。
        /// </summary>
        public static void ReturnSentinelLater(string chapterId, Vector3 near)
        {
            var host = new GameObject("SentinelReturn");
            host.AddComponent<SentinelReturn>().Begin(chapterId, near);
        }
    }

    /// <summary>等一会儿，把一只白纸哨兵放回场上。</summary>
    public class SentinelReturn : MonoBehaviour
    {
        string _chapterId;
        Vector3 _near;
        float _at;

        public void Begin(string chapterId, Vector3 near)
        {
            _chapterId = chapterId;
            _near = near;
            _at = Time.time + BlankPageSentinel.ReturnAfter;
        }

        void Update()
        {
            if (Time.time < _at) return;

            var loop = Level0901BlankPage.Active;
            var runner = InternalLevelRunner.Active;
            // 关卡已经离开、或者第一版已经做出来了 —— 都不再补人
            bool stillBlocked = loop != null && !loop.DraftMade &&
                                runner != null && runner.Level != null &&
                                runner.Level.levelId == Level0901BlankPage.LevelId;
            if (!stillBlocked) { Destroy(gameObject); return; }

            var ch = runner.Chapter;
            if (ch != null)
            {
                EnemyType ext, inner, boss;
                ChapterModuleLibrary.SuggestEnemies(InternalChapterBridge.AxisOf(ch),
                    out ext, out inner, out boss);
                var go = OpenWorld.ProceduralQuestAssembler.SpawnExtra(
                    _chapterId, inner, EnemyTier.Standard, _near);
                if (go != null)
                {
                    go.AddComponent<BlankPageSentinel>().chapterId = _chapterId;
                    GameEvents.RaiseSubtitle(
                        "又一个白纸哨兵站了过来——在做出第一版之前，它们会一直回来。" +
                        "去【工作台】按【用】。");
                }
            }
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 白纸哨兵（9-1）：**打得倒，但在你动笔之前它会一直回来**。
    ///
    /// 【上一版做错了：我把它设成了完全免伤】
    /// 当时是 externalDamageMult = 0，想表达"完美主义在你动笔之前不可战胜"。
    /// 实机上玩家看到的是：砍了很多轮，血条一点不掉，屏幕上只跳一个通用的
    /// "护体"——没有任何理由。玩家的原话是
    /// "第九章的第一个关卡的两个敌人的血量是无限的吗？为什么对战多轮次没有战胜它门。"
    ///
    /// 他问的是"这是不是个 bug"。一个打不动的敌人**读起来就是 bug**，
    /// 不管我在注释里把它的含义写得多好。这条教训比那个隐喻重要：
    /// **玩家先要能判断游戏有没有坏，才谈得上读懂它想说什么。**
    ///
    /// 现在改成：哨兵是**正常敌人**，打得动、打得死、有正常反馈。
    /// 只是在做出第一版之前，倒下的哨兵会在 10 秒后重新回来一只。
    /// "完美主义会一直回来，直到你动手"——意思一样，但战斗是真的、可赢的，
    /// 而且回来那一刻会明确说清原因，不会被当成血条坏了。
    /// 做出第一版之后不再补人，场上剩的打完就干净了。
    /// </summary>
    public class BlankPageSentinel : MonoBehaviour
    {
        /// <summary>倒下之后隔多久回来一只（仅在还没做出第一版时）。</summary>
        public const float ReturnAfter = 10f;

        public string chapterId = "";

        EnemyController _ec;
        bool _countedDown;

        void Awake() => _ec = GetComponent<EnemyController>();

        void Update()
        {
            if (_ec == null || _countedDown) return;
            if (_ec.State != EnemyState.Dead) return;

            _countedDown = true;
            var loop = Level0901BlankPage.Active;
            // 粗稿出来之后就不再补人了——路是通的，打完就干净
            if (loop == null || loop.DraftMade)
            {
                GameEvents.RaiseSubtitle("白纸哨兵倒下了。第一版已经有了，它不会再回来。");
                return;
            }
            InternalEnemyTactics.ReturnSentinelLater(chapterId, transform.position);
        }
    }

    /// <summary>
    /// 校稿幽灵（9-2）：**你每多打磨一处不挡交付的地方，屋里就多一只。**
    ///
    /// 这一关的核心是"边际收益递减，该停手了"。
    /// 我上一版把它做成了一个涨幅数字——那是文档语言，玩家读的是字，不是在玩。
    /// 换成这个之后，"再改下去代价更大"不需要任何一句说明：
    /// 你会亲眼看见屋里的人越来越多，路越来越难走。
    ///
    /// 它也不会把关卡做成打不过：幽灵不追到天涯海角，
    /// 而且两个阻断项处理完之后随时可以走去提交——**停手永远是一个选项**。
    /// </summary>
    public class ProofGhost : MonoBehaviour
    {
        public string chapterId = "";

        // 这里刻意**不**去调敌人的攻击力。
        // 压力曲线由"屋里的人变多"表达就够了，而且那是玩家自己一次次选出来的；
        // 再偷偷加一层伤害倍率，玩家只会觉得"越打越难受"却说不清为什么——
        // 看不见的难度曲线不是设计，是暗改。
        void Awake()
        {
            // 幽灵不比别的敌人硬：它们的压力来自数量，不来自单体强度
            var ec = GetComponent<EnemyController>();
            if (ec != null) ec.externalDamageMult = 1f;
        }
    }

    /// <summary>
    /// 最后检查者（9-3）：**它把你手上的箱子推回检查区**。
    ///
    /// 这一条直接来自关卡表的敌人栏，是 PRD 自己写的设计，我之前没实现。
    /// 它把这一关从"走过去按一下"变成"护着一件东西穿过一段有人拦你的路"：
    /// 手上有箱子的时候你走得慢，被它贴上就前功尽弃，
    /// 所以要么先把它引开、要么打退它、要么挑它转身的空档走。
    ///
    /// 这正是"战斗有用、但胜利不靠清怪"该有的样子：
    /// 打倒它们能让路好走，可通关的判定仍然是那一箱有没有上车。
    /// </summary>
    public class FinalChecker : MonoBehaviour
    {
        const float Reach = 2.6f;
        const float Cooldown = 6f;

        float _next;

        void Update()
        {
            var runner = InternalLevelRunner.Active;
            if (runner == null || runner.Level == null || runner.Level.levelId != "9-3") return;

            var cargo = InternalProp.Carried;
            if (cargo == null) return;
            if (Time.time < _next) return;

            var ec = GetComponent<EnemyController>();
            if (ec != null && ec.State == EnemyState.Dead) return;

            if (Vector3.Distance(transform.position, cargo.transform.position) > Reach) return;

            _next = Time.time + Cooldown;
            cargo.KnockCargoAway();
        }
    }
}
