using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Goals;
using AdversityRoad.World;

namespace AdversityRoad.OpenWorld
{
    /// <summary>
    /// AI 生成关卡**深处那一端的出口门**。
    ///
    /// 【为什么非有这扇门不可】生成场景原本只在落点背后立一块写着"出口"的牌子——
    /// 那是"原路退出去"，不是出口。而外部心魔的关卡现在的通关方式是
    /// **从入口进来、朝出口方向走、从另一扇门出去**：少了对面那扇门，这条规则
    /// 在生成关卡里根本无处落地，玩家只能照旧去找 Boss。
    ///
    /// 所以每处生成场景都在与落点相对的那一端立一扇真的门，规则决定它怎么响应：
    ///   · 外部心魔的关卡（<see cref="LevelClearRule.Escape"/>）：走进去 → 通关。
    ///     一路上被追、被打、被骂都不必理会；想打赢 Boss 也照样算通关（那条路没拆）。
    ///   · 内心心魔的关卡（<see cref="LevelClearRule.Defeat"/>）：门照样立在那儿（"一进一出"
    ///     这个结构对两种关卡是一样的），但走进去**不通关**，只说清楚为什么——
    ///     它跟着你走，换个地方没有用。这扇门也不把人送走：正打着架被门一吸就出关，
    ///     比没有门更糟；真想离开走"传送"面板那条既有的路。
    ///
    /// 判定用停留而不是碰到就走：与传送门同一套理由（翻滚/突进/被击退都可能擦过门框）。
    /// </summary>
    public class SiteExitDoor : MonoBehaviour
    {
        public string chapterId = "";
        public float range = 3.2f;

        const float DwellTime = 0.35f;
        const float WalkThroughSpeed = 7.5f;

        Player.PlayerController _player;
        Vector3 _lastPos;
        bool _tracking;
        float _dwell;
        float _lastHint = -99f;
        bool _fired;

        public static SiteExitDoor Attach(GameObject host, string chapterId)
        {
            var d = host.AddComponent<SiteExitDoor>();
            d.chapterId = chapterId;
            return d;
        }

        /// <summary>这处生成关卡的通关规则（章节丢了就按旧规则：必须打倒）。</summary>
        LevelClearRule Rule()
        {
            var goal = GoalOS.Active;
            var ch = goal != null ? goal.FindChapter(chapterId) : null;
            return LevelRules.Of(ch);
        }

        void Update()
        {
            if (_fired) return;
            // 只在玩家真的站在这处场景里时才响应：场景是挂在城外二十公里处的，
            // 人还在城里的时候这扇门不该有任何反应
            if (!SiteGate.InsideSite || SiteGate.InsideChapterId != chapterId) return;

            if (_player == null)
            {
                _player = ActorRegistry.Player;
                if (_player == null) return;
            }

            Vector3 pos = _player.transform.position;
            float dist = Vector3.Distance(transform.position, pos);
            if (dist > range) { _dwell = 0f; _tracking = false; return; }

            // 高速穿过不算停留（翻滚/突进/被击飞）
            if (!_tracking) { _lastPos = pos; _tracking = true; }
            float speed = Time.deltaTime > 1e-4f
                ? Vector3.Distance(pos, _lastPos) / Time.deltaTime : 0f;
            _lastPos = pos;
            if (speed > WalkThroughSpeed || _player.IsDodging) { _dwell = 0f; return; }

            _dwell += Time.deltaTime;
            if (_dwell < DwellTime) return;

            // 第 9-26 章：交付完了，走出这扇门才算把这一关走完。
            // 这一批的规则既不是"打倒"也不是"逃走"，是**先交付、再离场**，
            // 所以在旧的两条规则之前单独判。
            var runner = InternalOS.InternalLevelRunner.Active;
            if (runner != null && runner.Level != null &&
                InternalOS.InternalChapterBridge.ChapterIdOfLevel(runner.Level.levelId) == chapterId)
            {
                if (!runner.Cleared)
                {
                    Hint("出口在这儿，但这一关还没交付——" + runner.Level.Objective, 4f);
                    _dwell = 0f;
                    return;
                }
                // 回访：这一关的账早就结了。再弹一次通关卡、再报一次"章节完成"
                // 都是假的——玩家原话"通关之后再回来，不要再重新玩一遍"。
                // 安静地送他回城就好。
                if (runner.Replay) { LeaveAfterRevisit(runner); return; }
                ClearInternalLevel(runner);
                return;
            }

            if (Rule() == LevelClearRule.Escape) ClearByEscape();
            else LeaveWithoutClearing();
        }

        /// <summary>
        /// 第 9-26 章的收尾：交付过了，从出口走出去。
        ///
        /// 【为什么出口必须真的有用】
        /// 这一批关卡原来靠 LevelRules.Of 判规则，而它按敌人来源判：
        /// 内部敌人 → Defeat，于是门上写着"打倒关底心魔后才算通关"，
        /// 走过去还回一句"换个地方没有用"。玩家的原话是
        /// "没有真正的所谓出口"——因为这扇门当时确实是条死路，
        /// 而且还在对玩家说错话（这一批本来就不靠清怪通关）。
        ///
        /// 现在的结构和经典关卡一致：入口进 → 必经区做完事 → 出口走出去。
        /// 交付（Execution Gate）是"事情做完了"，走出这扇门是"这一关结束了"。
        /// </summary>
        void ClearInternalLevel(InternalOS.InternalLevelRunner runner)
        {
            _fired = true;
            var lv = runner.Level;
            GoalOS.ChapterCleared(chapterId);
            UI.InternalClearPanel.Show(lv);

            var host = new GameObject("SiteExitDelay");
            host.AddComponent<SiteExitDelay>().Setup(chapterId, 3.5f);
        }

        /// <summary>回访结束：不弹通关卡、不重记通关，按回执有没有交说一句就走。</summary>
        void LeaveAfterRevisit(InternalOS.InternalLevelRunner runner)
        {
            _fired = true;
            var lv = runner.Level;
            var ch = lv != null ? InternalOS.InternalChapterCatalog.Chapter(lv.chapterId) : null;
            string chapterKey = ch != null ? ch.chapterId : (lv != null ? lv.chapterId : "");

            bool reality = lv != null && InternalOS.RealityVictorySystem.HasLayer(
                lv.levelId, InternalOS.VictoryLayer.Reality);
            int contexts = InternalOS.RealityVictorySystem.DistinctTransferContexts(chapterKey);
            GameEvents.RaiseSubtitle(reality
                ? "回执交过了（现实 ✔，迁移 " + contexts + " 个场合）。这一关真正的胜利在外面，不在这儿。"
                : "这一趟没交回执也没关系。现实里做到那一次，随时回来记一笔。");

            var host = new GameObject("SiteExitDelay");
            host.AddComponent<SiteExitDelay>().Setup(chapterId, 2.5f);
        }

        void ClearByEscape()
        {
            // 过程不能省：必须真的从入口横穿到这一端（见 LevelTraverse）。
            // 生成场景注册成动态区域之后，它和经典关卡走的是同一套入口记录。
            int zone = ZoneBuilder.IndexOfZone(ZoneBuilder.CurrentZoneId);
            if (!LevelTraverse.Crossed(zone, transform.position, out float crossed))
            {
                _dwell = 0f;
                Hint("不必应战，但这一关得真的穿过去——从入口那头一路走到这扇门（已走 " +
                     Mathf.RoundToInt(crossed) + "/" + Mathf.RoundToInt(LevelTraverse.MinDistance) + " 米）。", 3f);
                return;
            }

            _fired = true;
            var goal = GoalOS.Active;
            var ch = goal != null ? goal.FindChapter(chapterId) : null;
            GoalOS.ChapterCleared(chapterId);
            if (ch != null)
            {
                WorldState.OnMilestoneReached(ch.worldDistrictId);
                GameEvents.RaiseSubtitle("〔章节完成〕" + ch.chapterName +
                    " —— 你没有打倒它，你只是从这里走了出去。对外面的东西，这就够了。" +
                    GoalJourneyGraph.ProgressLine(goal));
            }
            else GameEvents.RaiseSubtitle("〔章节完成〕你从这扇门走了出去。");

            // 与"打倒守门人通关"同一条收尾：先把人送回城，再卸载这处场景
            var host = new GameObject("SiteExitDelay");
            host.AddComponent<SiteExitDelay>().Setup(chapterId, 3.5f);
        }

        void LeaveWithoutClearing()
        {
            Hint("门在这儿，但这一关的心魔是内心的东西——它跟着你走，换个地方没有用。" +
                 "要通关，得回去把它打倒。（只是想离开的话，用右上角「传送」面板。）", 8f);
        }

        void Hint(string msg, float interval)
        {
            if (Time.time - _lastHint < interval) return;
            _lastHint = Time.time;
            GameEvents.RaiseSubtitle(msg);
        }
    }
}
