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
                        go.AddComponent<BlankPageSentinel>();
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
    }

    /// <summary>
    /// 白纸哨兵（9-1）：**在你动笔之前，它打不倒**。
    ///
    /// 这一关的核心是"完美主义不让你开始"。把它做成一个数字（可交付度）是抽象的，
    /// 做成一个**打不动的敌人**就是具体的：你可以一直砍它，它一直不倒，
    /// 屏幕上一直跳"护体"——直到你走到工作台按下【用】做出第一版。
    /// 那一刻它们同时变得可以被打倒。
    ///
    /// 玩家学到的不是一句话，是一个身体经验：**先动手，路才打得开。**
    /// </summary>
    public class BlankPageSentinel : MonoBehaviour
    {
        EnemyController _ec;
        bool _released;
        static float _lastHintAt = -99f;

        void Awake() => _ec = GetComponent<EnemyController>();

        void Update()
        {
            if (_ec == null) return;
            var loop = Level0901BlankPage.Active;
            if (loop == null) return;

            if (!loop.DraftMade)
            {
                // 打得动、有打击感、会硬直，但血不掉——这是"还没开始"的手感
                _ec.externalDamageMult = 0f;
                if (Time.time - _lastHintAt > 14f)
                {
                    _lastHintAt = Time.time;
                    GameEvents.RaiseSubtitle(
                        "〔白纸哨兵〕打不倒它——在做出第一版之前，它不会让开。");
                }
                return;
            }

            if (_released) return;
            _released = true;
            _ec.externalDamageMult = 1f;
            GameEvents.RaiseSubtitle("第一版有了——白纸哨兵现在打得倒了。");
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
