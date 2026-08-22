using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Player;
using AdversityRoad.World;

namespace AdversityRoad.Shame
{
    /// <summary>
    /// 第八章总控（方案 8.13.1 ShameLineController）：章节状态机、关卡衔接、结算判定，
    /// 以及本章比别处更严格的那一套安全条款（8.12）。
    ///
    /// 【本章不提供"洗清"结算】
    /// 没有 NPC 宣布玩家清白，没有道歉，没有误会解除。
    /// 玩家在本章拿到的不是清白，是行动能力：事实成立、注视仍在，依然能把事做完并走出去。
    /// </summary>
    public class ShameLineController : MonoBehaviour
    {
        public static ShameLineController Instance { get; private set; }

        /// <summary>连续失败保护：本章 2 次即触发（其他章节为 3 次，见 8.12.1）。</summary>
        public const int FailureCeiling = 2;

        /// <summary>连续失败保护已生效——降低 Exposure 增速与指认频率。</summary>
        public static bool ChallengeCeilingActive { get; private set; }

        string _levelId = "";
        int _consecutiveFailures;
        bool _selfWorthZeroHandled;
        Vector3 _levelEntry;
        Vector3 _lastRecoveryPoint;

        // ---- 8-2 的三个目标动作 ----
        readonly HashSet<string> _objectivesDone = new HashSet<string>();
        public const string ObjReturn = "归还";
        public const string ObjOwnWork = "完成本职";
        public const string ObjWalkOut = "步行离场";

        public string LevelId => _levelId;
        public int ObjectivesDone => _objectivesDone.Count;

        public static ShameLineController Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("ShameLineController");
            Instance = go.AddComponent<ShameLineController>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnEnable() => GameEvents.OnPlayerDied += HandleDeath;
        void OnDisable() => GameEvents.OnPlayerDied -= HandleDeath;
        void OnDestroy() { if (Instance == this) Instance = null; }

        // ================= 章节状态机 =================

        void Update()
        {
            string now = ShameLine.CurrentLevelId;
            if (now != _levelId)
            {
                if (!string.IsNullOrEmpty(_levelId)) ExitLevel(_levelId);
                if (!string.IsNullOrEmpty(now)) EnterLevel(now);
                _levelId = now;
            }
            if (string.IsNullOrEmpty(_levelId)) return;

            TickSelfWorthZero();
        }

        void EnterLevel(string levelId)
        {
            ShameLine.Data.currentLevelId = levelId;
            ShameLine.Persist();
            _objectivesDone.Clear();
            _selfWorthZeroHandled = false;
            ShameBreakdown.Reset();

            var player = FindObjectOfType<PlayerController>();
            if (player != null) _levelEntry = _lastRecoveryPoint = player.transform.position;

            ExposureSystem.Ensure();
            GazeConeSystem.Ensure();
            IdentityNailSystem.Ensure();
            OwnNotFinalSystem.Ensure();
            StatementSystem.Ensure();
            ShameSkills.Ensure();
            ShameHudOverlay.Ensure();

            // 强度上限：本章默认「标准」。「高压」要在章节入口二次确认（8.12.1）
            ConfirmIntensity();

            if (levelId == ShameLine.LevelDebtCorridor)
            {
                AppeasementSystem.Ensure().ResetForLevel();
                PendingCaseTimer.Ensure().StartCase();
                WhisperChainSystem.Ensure().EnableForLevel(false);
                GameEvents.RaiseSubtitle("【8-1 欠条长廊 / 未播出的广播室】" +
                    "偿还需要钱，说明真相需要暴露，隐瞒可以解决当下——代价是长廊变长。");
            }
            else if (levelId == ShameLine.LevelEchoClassroom)
            {
                var timer = PendingCaseTimer.Instance;
                if (timer != null) timer.StopCase();
                WhisperChainSystem.Ensure().EnableForLevel(true);
                GameEvents.RaiseSubtitle("【8-2 二十元回声教室】" +
                    "指控是成立的。不能靠否认通关，也不能靠让所有人闭嘴通关——" +
                    "在低语活着的时候，把三件事做完，然后正常走出去。");
            }
        }

        void ExitLevel(string levelId)
        {
            var exposure = ExposureSystem.Instance;
            if (exposure != null) exposure.CarryToNextLevel();
            var whisper = WhisperChainSystem.Instance;
            if (whisper != null) whisper.EnableForLevel(false);
            var nails = IdentityNailSystem.Instance;
            // 离开本章（不是章内换关）时把钉子清干净：钉子是章节内的东西
            if (nails != null && !ShameLine.InChapter) nails.ResetForExit();
            ShameBreakdown.Reset();
        }

        void ConfirmIntensity()
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.safety == null) return;
            if (gm.safety.intensity != MentalIntensity.HighPressure) return;
            // 高压需要玩家二次确认。这里不弹模态窗打断进场，而是先降到标准并说明——
            // 「随时可退出」与「默认上限为标准」这两条的优先级高于表现需求（8.12）。
            gm.safety.intensity = MentalIntensity.Standard;
            GameEvents.RaiseSubtitle("本章默认强度上限为「标准」。" +
                "需要「高压」请到设置面板再次开启；任何公开场景都可以随时退出。");
        }

        // ================= 失败态 =================

        void TickSelfWorthZero()
        {
            var player = FindObjectOfType<PlayerController>();
            if (player == null) return;
            bool zero = player.Stats.selfWorth <= 0.01f;
            if (zero && !_selfWorthZeroHandled)
            {
                _selfWorthZeroHandled = true;
                OnSelfWorthZero(player);
            }
            else if (!zero && _selfWorthZeroHandled && player.Stats.selfWorth > 8f)
                _selfWorthZeroHandled = false;
        }

        /// <summary>
        /// SelfWorth 归零：羞耻状态（8.10.2）。
        /// 事实之刃短时不可用，回到最近恢复点——**不回退关卡进度**。
        /// 失败只改变世界状态与路线，不制造额外羞辱（8.5.5 硬约束）。
        /// </summary>
        void OnSelfWorthZero(PlayerController player)
        {
            ShameBreakdown.Enter();
            NoteFailure();
            player.Stats.RestoreAxis(Personalization.WeaknessAxis.Shame, 26f);
            TeleportTo(_lastRecoveryPoint);
            GameEvents.RaiseSubtitle("你退到了一处没人看的地方。进度一点没丢——" +
                "完成任意一次与目标相关的行动，事实之刃就回来了。");
        }

        /// <summary>悬案计时器耗尽（8.5.5 失败）：长廊闭环回到起点，长度与欠条全部保留。</summary>
        public void OnCaseTimerExpired()
        {
            NoteFailure();
            GameEvents.RaiseSubtitle("计时器到头了。长廊在这里闭合，又把你送回起点——" +
                "欠条还在，长廊也还是那么长。学到的技能、情报与复盘资源一样都没少。");
            TeleportTo(_levelEntry);
            var timer = PendingCaseTimer.Instance;
            if (timer != null) timer.StartCase();
        }

        void HandleDeath(string reason)
        {
            if (!ShameLine.InChapter) return;
            NoteFailure();
        }

        /// <summary>连续失败保护：本章 2 次即降低 Exposure 增速与指认频率（8.12.1）。</summary>
        public void NoteFailure()
        {
            _consecutiveFailures++;
            if (_consecutiveFailures < FailureCeiling || ChallengeCeilingActive) return;
            ChallengeCeilingActive = true;
            GameEvents.RaiseSubtitle("【降压】注视的增速与指认的频率都降下来了，" +
                "认领窗口也放宽了。这不是施舍——连续两次撞墙之后，难度本来就该让路。");
        }

        /// <summary>任何一次有效推进都视为"没有卡住"：连续失败计数归零。</summary>
        public void NoteProgress()
        {
            _consecutiveFailures = 0;
        }

        void TeleportTo(Vector3 pos)
        {
            var player = FindObjectOfType<PlayerController>();
            if (player == null) return;
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.position = pos;
            if (cc != null) cc.enabled = true;
            player.NotifyTeleported();
        }

        /// <summary>路过恢复点时登记（羞耻状态回落点）。</summary>
        public void NoteRecoveryPoint(Vector3 pos) => _lastRecoveryPoint = pos;

        // ================= 8-1 结算 =================

        /// <summary>自行陈述完成：法官失去权限并消失，无战斗动画（8.5.4）。</summary>
        public void OnStatementCompleted(StatementRecord rec)
        {
            var timer = PendingCaseTimer.Instance;
            if (timer != null) timer.StopCase();
            NoteProgress();

            bool best = rec != null && rec.resultRank == "best";
            var d = ShameLine.Data;
            d.outcomeRank = best ? "best" : "normal";

            // 法官从场景中消失：不打，不演出，权限没了就是没了
            foreach (var e in FindObjectsOfType<AI.EnemyController>())
            {
                if (e == null || e.profile == null) continue;
                if (e.profile.enemyId != null && e.profile.enemyId.StartsWith("boss_pending_judge"))
                    Destroy(e.gameObject);
                // 讨好回声在最佳结算下永久移除
                if (best && e.profile.enemyId != null &&
                    e.profile.enemyId.StartsWith("enemy_appease_echo"))
                    Destroy(e.gameObject);
            }

            if (best)
            {
                d.selfStatementProof = true;
                GrantProofWeapon();
                GameEvents.RaiseSubtitle("【最佳结算 · 自选时机】没有人逼你，你自己走进去说的。" +
                    "获得武器「自述之证」；讨好回声永久移除。");
                Adversity.AdversityProfile.ObserveStrength("主动陈述提前量", ShameLine.LevelDebtCorridor);
            }
            else
            {
                var nails = IdentityNailSystem.Instance;
                if (nails != null) nails.KeepOneForNextLevel();
                GameEvents.RaiseSubtitle("【普通结算 · 被逼到最后】还是说出来了——" +
                    "只是这一次不是你挑的时间。带着 1 枚钉进入下一关。");
            }
            ShameLine.Persist();
            Goals.GoalOS.ChapterCleared(ShameLine.LevelDebtCorridor);
            var story = StoryManager.Instance;
            if (story != null) story.CompleteChapterByObjective("boss_pending_judge");
        }

        void GrantProofWeapon()
        {
            // 「自述之证」是象征能力：主动公开权——由自己决定何时、对谁、用什么措辞说出来。
            // 落到机制上就是三条被动，全部作用于本章语法，不改动别的章节。
            GrowthSystem.GrantNode("shame_proof");
        }

        // ================= 8-2 三个目标动作与终局 =================

        public bool ObjectiveDone(string id) => _objectivesDone.Contains(id);

        public void CompleteObjective(string id)
        {
            if (!_objectivesDone.Add(id)) return;
            NoteProgress();
            ShameBreakdown.ResolveByAction("完成了「" + id + "」");
            Adversity.AdversityProfile.ObserveStrength("锥内完成率", ShameLine.LevelEchoClassroom);
            Adversity.CourageSystem.NoteGoalAction("在注视里完成「" + id + "」");
            GameEvents.RaiseSubtitle("目标动作「" + id + "」完成（" + _objectivesDone.Count +
                "/3）——低语没有停，你也没有停。");

            var whisper = WhisperChainSystem.Instance;
            if (_objectivesDone.Count == 2 && whisper != null) whisper.FieldWideRebuild();
        }

        /// <summary>前两个目标动作都完成后，门那一段才算数。</summary>
        public bool WalkOutReady => _objectivesDone.Contains(ObjReturn) &&
                                    _objectivesDone.Contains(ObjOwnWork);

        bool _walkOutSettled;

        /// <summary>
        /// 步行离场判定：奔跑离场记为回避（本关按普通结算）。
        /// 回避与完成在**动作层面**被区分开——这一条不靠玩家自述，靠他怎么走出去。
        /// </summary>
        public void NoteWalkOut(bool walked)
        {
            if (_walkOutSettled) return;
            _walkOutSettled = true;
            var d = ShameLine.Data;

            // 阶段三里完成最后一个目标动作：他停止发声（不是被打死的）
            var boss = FindObjectOfType<AI.BackRowWhispererBoss>();
            if (boss != null) boss.Silence();

            if (walked)
            {
                CompleteObjective(ObjWalkOut);
                d.outcomeRank = "best";
                GameEvents.RaiseSubtitle("他停止发声了。没有击杀动画，没有道歉，也没有和解——" +
                    "他只是停下来了，而你继续走了出去。");
            }
            else
            {
                d.outcomeRank = "normal";
                GameEvents.RaiseSubtitle("你跑出去了。也算走出去了——只是这一次，是躲出去的。");
                Adversity.AdversityProfile.Observe("公开注视", "撤退 / 绕行概率上升", true,
                    ShameLine.LevelEchoClassroom, "锥内稳态");
            }
            ShameLine.Persist();
            EvaluateComeback();
            Goals.GoalOS.ChapterCleared(ShameLine.LevelEchoClassroom);
            var story = StoryManager.Instance;
            if (story != null) story.CompleteChapterByObjective("boss_back_row_whisperer");
        }

        // ================= 逆袭判定（8.11） =================

        /// <summary>
        /// 本章的逆袭判定不看伤害，也不看胜负：它看的是玩家相对于自己发生了什么变化。
        /// 五项里过三项即成立——每一项都是可观察的游戏行为，没有一项是主观评分。
        /// </summary>
        public string EvaluateComeback()
        {
            var d = ShameLine.Data;
            var passed = new List<string>();

            // ① 指控复用失效：认过的事，本章里再没能挂上钉
            if (d.ownCount > 0 && ClaimRegistry.SpentCount() >= d.ownCount)
                passed.Add("指控复用失效");

            // ② 陈述提前量提升：第二次比第一次更早
            if (d.statementHistory.Count >= 2)
            {
                var first = d.statementHistory[0];
                var last = d.statementHistory[d.statementHistory.Count - 1];
                if (last.timingRatio > first.timingRatio) passed.Add("陈述提前量提升");
            }

            // ③ 锥内行动稳定：三个目标动作全部在注视里完成
            if (_objectivesDone.Count >= 3) passed.Add("锥内行动稳定");

            // ④ 否认频率下降：认领次数已经压过否认次数
            if (d.ownCount > d.denialCount) passed.Add("否认频率下降");

            // ⑤ 宿敌降级：玩家在场时低语链无法完整成形
            var whisper = WhisperChainSystem.Instance;
            if (whisper != null && !whisper.FormsWithPlayerPresent()) passed.Add("宿敌降级");

            if (passed.Count >= 3)
            {
                var sb = new System.Text.StringBuilder("【逆袭成立】");
                for (int i = 0; i < passed.Count; i++)
                {
                    if (i > 0) sb.Append(" / ");
                    sb.Append(passed[i]);
                }
                GameEvents.RaiseSubtitle(sb.ToString() +
                    "——你拿到的不是清白，是行动能力。");
                Adversity.AdversityProfile.ObserveStrength("羞耻线逆袭", ShameLine.ChapterId);
            }

            var result = new System.Text.StringBuilder();
            foreach (var s in passed) { if (result.Length > 0) result.Append(" / "); result.Append(s); }
            return result.ToString();
        }

        /// <summary>复盘页要用的一段本章记录（8.11.1 Adversity History 记录项）。</summary>
        public static string HistorySummary()
        {
            var d = ShameLine.Data;
            var sb = new System.Text.StringBuilder();
            sb.Append("首次被钉：").Append(string.IsNullOrEmpty(d.firstNailTag) ? "无" : d.firstNailTag);
            sb.Append("　否认 ").Append(d.denialCount).Append(" 次 / 认领 ").Append(d.ownCount).Append(" 次");
            sb.Append("　暴露峰值 ").Append(Mathf.RoundToInt(d.exposurePeak));
            if (d.statementHistory.Count > 0)
            {
                var last = d.statementHistory[d.statementHistory.Count - 1];
                sb.Append("　最近一次陈述提前量 ").Append(Mathf.RoundToInt(last.timingRatio * 100f)).Append('%');
            }
            return sb.ToString();
        }
    }
}
