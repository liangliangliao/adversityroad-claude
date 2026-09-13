using System;
using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.Core;

namespace AdversityRoad.InternalOS
{
    /// <summary>
    /// 一关的运行时规则驱动（第 9-26 章通用）。
    ///
    /// 【它负责什么】
    /// 把关卡表里那几栏静态文字，变成运行时真的会发生的事：
    /// 触发点 → 内部语言攻击 → 三选一 → Follow-up → Execution Gate → 胜利/撤退。
    ///
    /// 【它刻意不负责什么】
    /// 不建几何（那是 SiteBuilder 的事，见 InternalSiteComposer）、
    /// 不判 Boss 血量（见 InternalChapterBridge.BossDefeated）、
    /// 不替玩家宣布现实胜利（那必须由玩家自己回来确认，见 RealityVictorySystem）。
    ///
    /// 【一条硬规则】
    /// <see cref="ExecutionGate"/> 没过，这一关就不算通关——哪怕场上敌人全部清空。
    /// 第 3.4 节写得很直接："不以清怪定义胜利"。
    /// </summary>
    public class InternalLevelRunner : MonoBehaviour
    {
        public static InternalLevelRunner Active { get; private set; }

        public InternalLevelData Level { get; private set; }
        public InternalChapterInfo Chapter { get; private set; }
        public bool GatePassed { get; private set; }
        public bool Cleared { get; private set; }

        /// <summary>已经触发过的 Trigger（同一个触发点不重复弹同一条攻击）。</summary>
        readonly HashSet<string> _fired = new HashSet<string>();
        float _enteredAt;
        float _lastGoalActionAt;
        bool _goalOffline;
        float _goalOfflineSince;

        public static event Action<InternalLevelData> OnLevelEntered;
        public static event Action<InternalLevelData, bool> OnLevelEnded;   // bool: 是否通关

        // ==================== 进出 ====================

        public static InternalLevelRunner Enter(string levelId)
        {
            var lv = InternalChapterCatalog.Level(levelId);
            if (lv == null)
            {
                Debug.LogError("[InternalOS] 没有这一关：" + levelId);
                return null;
            }

            if (Active != null) Active.Leave(false);

            var go = new GameObject("InternalLevelRunner_" + levelId);
            var runner = go.AddComponent<InternalLevelRunner>();
            runner.Setup(lv);
            Active = runner;
            return runner;
        }

        void Setup(InternalLevelData lv)
        {
            Level = lv;
            Chapter = InternalChapterCatalog.Chapter(lv.chapterId);
            _enteredAt = Time.unscaledTime;
            _lastGoalActionAt = _enteredAt;

            // 机关的"这一趟做过了没有"必须每次进关清零。
            // 它原来一个调用点都没有：Done 锁和目标箱都按 levelId 记在静态表里，
            // 于是**重进同一关时，上一趟的进度还在**——第二次走到月台就直接过，
            // 不用再把箱子搬过来。这类关卡最怕的就是"第二遍不用玩了"。
            InternalProps.ResetSession();

            ControlChainRecorder.Begin(Chapter != null ? Chapter.chapterId : lv.chapterId,
                lv.realityVictory);

            GameEvents.RaiseSubtitle(lv.levelId + "《" + lv.name + "》—— " + lv.Objective);
            OnLevelEntered?.Invoke(lv);
        }

        /// <summary>离开这一关。cleared=false 时也不算"失败告终"——撤退是合法结局。</summary>
        public void Leave(bool cleared)
        {
            if (Level == null) return;

            SettleGoalOffline();
            RealityVictorySystem.Flush();
            ControlChainRecorder.End(cleared ? "Completed"
                : (GatePassed ? "Partial" : "Withdrawn"));

            OnLevelEnded?.Invoke(Level, cleared);
            if (Active == this) Active = null;
            Level = null;
            Destroy(gameObject);
        }

        // ==================== 触发点 ====================

        /// <summary>
        /// 关卡里的一个触发点被踩到了。
        ///
        /// 每个触发点最多引一次内部语言攻击：同一段路来回走不该被反复弹框。
        /// mastery 越高越少弹（见 MentalAttackSystem.SuppressedByMastery）。
        /// </summary>
        public bool FireTrigger(string triggerId)
        {
            if (Level == null) return false;
            if (!Level.triggerIds.Contains(triggerId))
            {
                // 命名契约里没有的触发点：多半是拼错了，别静默吞掉。
                Debug.LogWarning("[InternalOS] " + Level.levelId + " 没有声明触发点 " + triggerId);
                return false;
            }
            if (!_fired.Add(triggerId)) return false;

            int mastery = Chapter != null ? RealityVictorySystem.Mastery(Chapter.chapterId) : 0;
            bool boss = Level.isBossLevel;
            return MentalAttackSystem.TryFire(Level.levelId, mastery, boss);
        }

        /// <summary>玩家做了一个真正推进目标的动作（不是走路、不是打怪）。</summary>
        public void MarkGoalAction(string what)
        {
            if (Level == null) return;

            float now = Time.unscaledTime;
            // 中断到重新进入目标行动之间的时间：这条趋势比"连续多少天不中断"重要得多。
            RealityVictorySystem.LogRecoveryLatency(now - _lastGoalActionAt);
            _lastGoalActionAt = now;

            if (_goalOffline)
            {
                RealityVictorySystem.LogControlRecoveryTime(now - _goalOfflineSince);
                SettleGoalOffline();
            }

            // 答对之后那件必须做的事，就是在这里被认掉的。
            if (MentalAttackSystem.IsFollowUpPending) MentalAttackSystem.CompleteFollowUp();

            ControlChainRecorder.Log(0, what, "", what, 0f, what);
        }

        /// <summary>目标掉出工作记忆了（玩家在做与目标无关的事）。</summary>
        public void MarkGoalOffline(int bossId, string automaticThought, string automaticBehavior,
            float controlShift)
        {
            if (!_goalOffline)
            {
                _goalOffline = true;
                _goalOfflineSince = Time.unscaledTime;
            }
            ControlChainRecorder.Log(bossId, "", automaticThought, automaticBehavior, controlShift);
        }

        void SettleGoalOffline()
        {
            if (!_goalOffline) return;
            RealityVictorySystem.AddGoalOfflineTime(Time.unscaledTime - _goalOfflineSince);
            _goalOffline = false;
        }

        // ==================== 通关 ====================

        /// <summary>
        /// Execution Gate：这一关真正的通关动作发生了（提交、跨过门槛、装车、归档……）。
        ///
        /// hpCleared 只在 DeathType=Defeat 的 Boss 上有意义；其余 Boss 它是什么都不影响结果。
        /// </summary>
        public bool ExecutionGate(bool hpCleared = false)
        {
            if (Level == null) return false;

            if (Level.isBossLevel &&
                !InternalChapterBridge.BossDefeated(Level.levelId, true, hpCleared))
            {
                return false;
            }

            GatePassed = true;
            Cleared = true;

            RealityVictorySystem.Record(Level.levelId, VictoryLayer.Simulation,
                Level.realityVictory, Level.levelId);
            MentalAttackSystem.LinkLaterOutcome(Level.levelId, true);

            var boss = InternalChapterCatalog.BossOfLevel(Level.levelId);
            GameEvents.RaiseSubtitle(boss != null
                ? boss.name + " 失去了这条路上的指挥权（" + boss.deathType + "）"
                : "这一关的条件达成了。");

            return true;
        }

        /// <summary>
        /// 战略撤退：符合条件时它是合法结局，显示 Strategic Withdrawal 而不是 FAILED（第 9 节）。
        /// 但 Goal OS 仍要识别"把撤退包装成无限拖延"——所以这里要求给出重返条件。
        /// </summary>
        public bool StrategicWithdraw(string reentryCondition)
        {
            if (Level == null) return false;
            if (string.IsNullOrEmpty(reentryCondition))
            {
                GameEvents.RaiseSubtitle("撤退需要一个重返条件——没有它，这是拖延，不是战略。");
                return false;
            }

            ControlChainRecorder.Log(0, "StrategicWithdraw", "", "撤离并设定重返条件", 0f,
                reentryCondition);
            GameEvents.RaiseSubtitle("战略撤退 —— 重返条件：" + reentryCondition);
            Leave(false);
            return true;
        }

        /// <summary>玩家回来确认他在现实里真的做了对应的行为。</summary>
        public static VictoryEvidence ConfirmRealityVictory(string levelId, string behavior,
            string context, float confidence = 1f)
        {
            return RealityVictorySystem.Record(levelId, VictoryLayer.Reality, behavior, context, confidence);
        }

        /// <summary>同一个能力在**另一个情境**里又用了一次。</summary>
        public static VictoryEvidence ConfirmTransferVictory(string levelId, string behavior,
            string context, float confidence = 1f)
        {
            return RealityVictorySystem.Record(levelId, VictoryLayer.Transfer, behavior, context, confidence);
        }

        void Update()
        {
            MentalAttackSystem.Tick();

            if (Level == null) return;
            // 在这一关里，只要还没通关，时间就都算"可行动时间"；
            // 真正被算成 Agency 的只有 MarkGoalAction 之后那一段。
            RealityVictorySystem.AddActionTime(_goalOffline ? 0f : Time.unscaledDeltaTime,
                Time.unscaledDeltaTime);
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) RealityVictorySystem.Flush();
        }

        void OnDestroy()
        {
            RealityVictorySystem.Flush();
            if (Active == this) Active = null;
        }

        /// <summary>
        /// HUD 目标行：**现在该做的那一件事**，随进度变。
        ///
        /// 【为什么这一行必须由关卡自己给】
        /// SiteGate 原来按"敌人还剩几个"写目标行，清完就打
        /// "这里清空了，从来路走出去"。可这 90 关的通关条件根本不是清怪——
        /// PRD 3.4 写着"不以清怪定义胜利"。那行字不只是没用，它在**教玩家错误的通关方式**，
        /// 还盖住了真正要做的事。玩家的原话就是"通关需要做什么？"
        ///
        /// 规则：永远只说下一步，不说全部流程。一行字读完就知道往哪走。
        /// </summary>
        public string ObjectiveLine()
        {
            if (Level == null) return "";
            string head = Level.levelId + "《" + Level.name + "》 ";

            if (Cleared) return head + "—— 这一关的条件达成了，可以离开";

            var loop = Level0901BlankPage.Active;
            if (loop != null && Level.levelId == Level0901BlankPage.LevelId)
            {
                if (!loop.DraftMade)
                    return head + "→ 去【工作台】做出第一版（站着不动，这地方会一直往外长）";
                if (!loop.DoneReached)
                    return head + "→ 找出真正挡住交付的问题并处理（" +
                           loop.CriticalFixed + "/" + Level0901BlankPage.CriticalToDone +
                           "，其余只是可优化）";
                return head + "→ 去【提交台】提交";
            }

            if (MentalAttackSystem.IsFollowUpPending)
                return head + "→ " + MentalAttackSystem.PendingFollowUp;

            // 玩家看的是 playerObjective 那句人话；realityVictory 是验收条件，
            // 里面有 "Reality Evidence""DeathType=Publish" 这类只有文档读得懂的词。
            return head + "→ " + Level.Objective;
        }

        /// <summary>这一关进来多久了（秒）。</summary>
        public float ElapsedSeconds => Level == null ? 0f : Time.unscaledTime - _enteredAt;
    }
}
