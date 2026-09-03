using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.AI;
using AdversityRoad.Combat;
using AdversityRoad.Core;
using AdversityRoad.Mobile;
using AdversityRoad.Player;

namespace AdversityRoad.Shame
{
    /// <summary>
    /// 本章交互物的小选择面板。
    ///
    /// 【为什么不用「蹲」键做第二个选项】
    /// MobileInput.GetDown 是消费式读取，而「蹲」同时被 PlayerController 读着——
    /// 在交互物上再读一次，等于把玩家的蹲伏抢走，或者反过来被抢走。
    /// 一块面板就没有这个问题，而且触屏与桌面是同一套操作。
    /// </summary>
    public class ShameChoicePanel : MonoBehaviour
    {
        static ShameChoicePanel _open;

        GameObject _panel;

        public static bool AnyOpen => _open != null;

        public static void Show(string title, string line,
            string[] options, System.Action<int> onPick)
        {
            if (_open != null) return;
            var canvas = FindObjectOfType<Canvas>();
            if (canvas == null) { if (onPick != null) onPick(-1); return; }

            var go = new GameObject("ShameChoicePanel");
            _open = go.AddComponent<ShameChoicePanel>();
            _open.Build(canvas.transform, title, line, options, onPick);
        }

        void Build(Transform canvas, string title, string line,
            string[] options, System.Action<int> onPick)
        {
            int n = Mathf.Clamp(options != null ? options.Length : 0, 1, 4);
            float w = Mathf.Max(560f, 40f + n * 210f);
            _panel = UiUtil.MakePanel(canvas, "ShameChoice", new Vector2(w, 200),
                new Color(0.07f, 0.07f, 0.09f, 0.95f));
            UiUtil.SetRect(_panel.GetComponent<Image>(), new Vector2(0.5f, 0f),
                new Vector2(0, 400), new Vector2(w, 200));

            var t = UiUtil.MakeText(_panel.transform, "Title", title, 22,
                TextAnchor.MiddleCenter, new Color(0.92f, 0.85f, 0.62f));
            UiUtil.SetRect(t, new Vector2(0.5f, 1f), new Vector2(0, -22), new Vector2(w - 40, 28));

            var l = UiUtil.MakeText(_panel.transform, "Line", line, 20,
                TextAnchor.MiddleCenter, new Color(0.9f, 0.9f, 0.94f));
            UiUtil.SetRect(l, new Vector2(0.5f, 1f), new Vector2(0, -58), new Vector2(w - 40, 30));

            float step = 210f;
            float x0 = -(n - 1) * step * 0.5f;
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                UiUtil.MakeButton(_panel.transform, options[i], new Vector2(0.5f, 0f),
                    new Vector2(x0 + i * step, 44), new Vector2(190, 66),
                    new Color(0.28f, 0.31f, 0.38f, 0.95f),
                    () => { Close(); if (onPick != null) onPick(idx); }, 21);
            }
        }

        void Close()
        {
            if (_panel != null) Destroy(_panel);
            _open = null;
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 第八章交互物的共用基类：站得够近 + 按【用】（触屏）/ R（桌面）。
    ///
    /// 【桌面为什么不用 E】
    /// E 在战斗控制器里是踢击（`Input.GetKeyDown(KeyCode.E)`）。家里那些物件用 E 没问题，
    /// 因为安全屋里没有敌人；而本章两关**遍地是敌人**，每按一次交互就先踢一脚，
    /// 长按目标动作更是一按就出腿。所以桌面端换到没人用的 R。
    /// 触屏那边「用」和「踢」本来就是两个不同的按钮，不受影响。
    /// </summary>
    public abstract class ShameInteractable : MonoBehaviour
    {
        public float interactRange = 3.2f;
        public string prompt = "按【用】/ R";

        float _lastHint = -99f;
        protected PlayerController player;

        protected virtual void Update()
        {
            if (player == null) player = AdversityRoad.Core.ActorRegistry.Player;
            if (player == null) return;
            float dist = Vector3.Distance(transform.position, player.transform.position);
            if (dist > interactRange) { OnOutOfRange(); return; }

            if (Time.time - _lastHint > 2.5f)
            {
                _lastHint = Time.time;
                string p = Prompt();
                if (!string.IsNullOrEmpty(p)) GameEvents.RaiseSubtitle(p);
            }
            if (Input.GetKeyDown(KeyCode.R) || MobileInput.GetDown("Interact")) Interact();
            OnInRange(dist);
        }

        protected virtual string Prompt() => prompt;
        protected virtual void OnInRange(float dist) { }
        protected virtual void OnOutOfRange() { }
        protected abstract void Interact();
    }

    /// <summary>
    /// 欠条台（8.5.1）：分期偿还入口。
    ///
    /// 总额固定，每期金额由玩家自选：高额推进快但资源压力大，低额安全但追问次数变多。
    /// 这是把压力做成**可调度的资源问题**，而不是一场道德测验——
    /// 系统不评价你选哪一档。
    /// </summary>
    public class DebtDesk : ShameInteractable
    {
        public const float TotalDebt = 100f;

        /// <summary>已偿还比例 0-1。</summary>
        public float Repaid { get; private set; }

        int _tier = 1;   // 0 低 / 1 中 / 2 高
        static readonly float[] Amounts = { 8f, 16f, 28f };
        static readonly string[] TierNames = { "低额", "中额", "高额" };

        protected override string Prompt() =>
            Repaid >= 1f ? "欠条台：已经还完了。剩下的不是钱的事。"
            : "欠条台：按【用】/ R 选本期金额（已还 " + Mathf.RoundToInt(Repaid * 100f) + "%）";

        protected override void Interact()
        {
            if (Repaid >= 1f || player == null || ShameChoicePanel.AnyOpen) return;
            ShameChoicePanel.Show("欠条台 · 分期偿还",
                "高额推进快但资源压力大；低额安全，但要多被问几次。",
                new[] { "低额", "中额", "高额" }, Repay);
        }

        void Repay(int tier)
        {
            if (tier < 0 || player == null) return;
            _tier = Mathf.Clamp(tier, 0, Amounts.Length - 1);

            // 资源压力：偿还花的是时间与精力（可恢复），不是永久不可恢复的关键资源（8.7）
            float cost = Amounts[_tier];
            player.Stats.actionPower = Mathf.Max(0f, player.Stats.actionPower - cost * 0.55f);
            GameEvents.RaiseMentalStatChanged("actionPower",
                player.Stats.actionPower, player.Stats.maxActionPower);

            Repaid = Mathf.Clamp01(Repaid + cost / TotalDebt);
            GameAudio.Play(GameAudio.Sfx.Cast, 0.6f);
            GameEvents.RaiseSubtitle("偿还了一期（" + TierNames[_tier] + "）——已还 " +
                Mathf.RoundToInt(Repaid * 100f) + "%。每还一次，就要再被问一次。");

            // 每次偿还触发一次「每周追问」：追问本身就是攻击，全程不发生正面战斗
            WeeklyInquiry.Trigger(transform.position, _tier == 0 ? 2 : 1);

            var ctl = ShameLineController.Instance;
            if (ctl != null) ctl.NoteProgress();
            if (Repaid >= 1f)
                GameEvents.RaiseSubtitle("账还清了。可案子还挂着——钱从来不是这一关的门槛。");
        }
    }

    /// <summary>
    /// 家中抽屉（8.5.1）：隐瞒类交互物。
    ///
    /// 【当下压力立即下降——这是真实的收益，必须给到】
    /// 代价同样是真实的：长廊 +1 段、Exposure 上限 +10、生成 1 个「新的把柄」。
    /// 这里不弹说教文案，不降低玩家操作权限，也不做惩罚性动画（8.3 禁止做法）。
    /// 系统只诚实展示账单。
    /// </summary>
    public class ConcealmentDrawer : ShameInteractable
    {
        public string coverStory = "先借一笔，回头再说";

        protected override string Prompt() => "抽屉：" + coverStory + "（按【用】/ R）";

        protected override void Interact()
        {
            if (player == null) return;

            // 真实的短期收益：当下的压力确实小了
            player.Stats.RestoreAxis(Personalization.WeaknessAxis.Shame, 18f);
            player.Stats.actionPower = Mathf.Min(player.Stats.maxActionPower,
                player.Stats.actionPower + 20f);
            GameEvents.RaiseMentalStatChanged("actionPower",
                player.Stats.actionPower, player.Stats.maxActionPower);
            GameAudio.Play(GameAudio.Sfx.Cast, 0.5f);

            var corridor = CorridorGrowthSystem.Instance;
            if (corridor != null) corridor.NoteConcealment(coverStory);

            Adversity.AdversityProfile.Observe("无法辩解", "隐瞒类交互使用", true,
                ShameLine.LevelDebtCorridor, "自行陈述");
        }
    }

    /// <summary>
    /// 每周门（8.5.1）：沿长廊排列，每扇代表一次追问。必须逐一通过，不可跳过。
    /// </summary>
    public class WeeklyDoor : MonoBehaviour
    {
        public int doorIndex = 1;
        bool _passed;

        void OnTriggerEnter(Collider other)
        {
            if (_passed) return;
            if (other.transform.root.GetComponentInChildren<PlayerController>() == null) return;
            _passed = true;
            GameEvents.RaiseSubtitle("第 " + doorIndex + " 扇门。又一周，又一次要解释同一件事。");
            WeeklyInquiry.Trigger(transform.position, 1);
        }
    }

    /// <summary>
    /// 每周追问（8.5.2）：定时遭遇。悬案法官出现、追问进度、提出新条件，
    /// **全程不发生正面战斗**——追问本身就是攻击，战斗不是唯一的压力形态。
    ///
    /// 遭遇中会浮现「顺从应答」：按下即降低本次压力并提升讨好度（8.3.1）。
    /// 它是真收益，所以按钮就摆在那儿，不劝阻、不隐藏。
    /// </summary>
    public class WeeklyInquiry : MonoBehaviour
    {
        static WeeklyInquiry _active;

        GameObject _panel;
        Text _line;
        float _until;
        int _rounds;

        static readonly string[] Questions =
        {
            "这周还上了吗？",
            "上次说的下周，是哪个下周？",
            "再加一条：这次要有个准话。",
            "你打算怎么跟别人解释？",
        };

        public static void Trigger(Vector3 near, int rounds)
        {
            if (_active != null) return;
            var go = new GameObject("WeeklyInquiry");
            go.transform.position = near;
            _active = go.AddComponent<WeeklyInquiry>();
            _active._rounds = Mathf.Clamp(rounds, 1, 3);
            _active.Begin();
        }

        void Begin()
        {
            var timer = PendingCaseTimer.Instance;
            if (timer != null) timer.NoteInquiry();

            float duration = 9f;
            var appease = AppeasementSystem.Instance;
            if (appease != null && appease.Value > 0f) duration *= 0.7f;   // 顺从确实让追问更快结束
            _until = Time.unscaledTime + duration * _rounds;

            Build();
            if (_line != null)
                _line.text = Questions[Random.Range(0, Questions.Length)];
            GameEvents.RaiseSubtitle("【每周追问】不是打架，是问话。它照样在扣你的东西。");
        }

        void Update()
        {
            if (Time.unscaledTime < _until) return;
            Finish();
        }

        void Submissive()
        {
            var appease = AppeasementSystem.Instance;
            if (appease != null) appease.Appease(14f, "你先应下来了");
            var player = AdversityRoad.Core.ActorRegistry.Player;
            if (player != null)
                GameAudio.Play(GameAudio.Sfx.Cast, appease != null ? appease.VoiceVolume : 0.6f);
            Finish();
        }

        void StateFact()
        {
            // 「不上庭」举起时，判词类交互直接被拒绝
            var skills = ShameSkills.Instance;
            if (skills != null && skills.TryRefuse(null, "这次追问")) { Finish(); return; }

            if (IdentityNailSystem.FactBladeLocked)
            {
                GameEvents.RaiseSubtitle("话到嘴边说不出来——事实之刃此刻不可用。");
                Finish();
                return;
            }
            var player = AdversityRoad.Core.ActorRegistry.Player;
            if (player != null)
            {
                player.Stats.TakeMentalDamage(Personalization.WeaknessAxis.Shame, 9f);
                player.Stats.ReduceRumination(6f);
            }
            GameEvents.RaiseSubtitle("你把时间、金额和下一步说清楚了。压力没少，但话是你自己的。");
            Adversity.AdversityProfile.ObserveStrength("事实判断", ShameLine.LevelDebtCorridor);
            Finish();
        }

        void Leave()
        {
            GameEvents.RaiseSubtitle("你退出了这次追问。可以随时退出——这一条在本章里优先级最高。");
            Finish();
        }

        void Finish()
        {
            if (_panel != null) Destroy(_panel);
            _active = null;
            Destroy(gameObject);
        }

        void Build()
        {
            var canvas = FindObjectOfType<Canvas>();
            if (canvas == null) return;
            _panel = UiUtil.MakePanel(canvas.transform, "WeeklyInquiryPanel",
                new Vector2(700, 210), new Color(0.07f, 0.07f, 0.09f, 0.94f));
            UiUtil.SetRect(_panel.GetComponent<Image>(), new Vector2(0.5f, 0f),
                new Vector2(0, 400), new Vector2(700, 210));

            var tag = UiUtil.MakeText(_panel.transform, "Tag", "每 周 追 问", 20,
                TextAnchor.MiddleCenter, new Color(0.9f, 0.78f, 0.6f));
            UiUtil.SetRect(tag, new Vector2(0.5f, 1f), new Vector2(0, -18), new Vector2(660, 24));

            _line = UiUtil.MakeText(_panel.transform, "Line", "", 22,
                TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.88f));
            UiUtil.SetRect(_line, new Vector2(0.5f, 1f), new Vector2(0, -54), new Vector2(660, 30));

            UiUtil.MakeButton(_panel.transform, "顺从应答", new Vector2(0.5f, 0f),
                new Vector2(-220, 46), new Vector2(200, 62),
                new Color(0.32f, 0.3f, 0.24f, 0.95f), Submissive, 22);
            UiUtil.MakeButton(_panel.transform, "说清事实", new Vector2(0.5f, 0f),
                new Vector2(0, 46), new Vector2(200, 62),
                new Color(0.26f, 0.34f, 0.3f, 0.95f), StateFact, 22);
            UiUtil.MakeButton(_panel.transform, "退出", new Vector2(0.5f, 0f),
                new Vector2(220, 46), new Vector2(160, 62),
                new Color(0.3f, 0.3f, 0.34f, 0.95f), Leave, 22);
        }
    }

    /// <summary>欠条残片（8.5.1）：环境叙事物。收集可解锁本关情报，用于缩短 Boss 阶段。</summary>
    public class DebtFragment : ShameInteractable
    {
        public static int Collected { get; private set; }

        public static void ResetAll() => Collected = 0;

        protected override string Prompt() => "一片欠条的残角（按【用】/ R 捡起）";

        protected override void Interact()
        {
            Collected++;
            CombatFeedback.Debris(transform.position, new Color(0.9f, 0.85f, 0.7f), 6);
            GameEvents.RaiseSubtitle("欠条残片 " + Collected + "/3——" +
                (Collected >= 3
                    ? "凑齐了。条款、日期、金额都对得上，悬案段会短一截。"
                    : "上面的日期和你记的不一样。"));
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 广播室的门（8.5.1）——本关的收束点。
    ///
    /// 【从关卡开始的第一秒起，这扇门就是开着的】
    /// 难度不在于找到出口，也不在于击败守门人。门槛是：在 Exposure 高、SelfWorth 低、
    /// 身上挂着钉子的状态下，主动走进去并完成自行陈述。
    /// 系统不得用箭头、任务提示或强制引导把玩家推进门内（验收第 37 条）。
    /// </summary>
    public class BroadcastDoor : MonoBehaviour
    {
        bool _entered;

        void OnTriggerEnter(Collider other)
        {
            if (_entered) return;
            if (other.transform.root.GetComponentInChildren<PlayerController>() == null) return;
            _entered = true;
            var statement = StatementSystem.Ensure();
            if (statement != null) statement.Open();
        }

        void OnTriggerExit(Collider other)
        {
            if (other.transform.root.GetComponentInChildren<PlayerController>() == null) return;
            _entered = false;
        }
    }

    /// <summary>
    /// ON AIR 红灯（8.5.1）：门上永不亮起的红灯——「待审悬置」的可视化象征。
    /// 它是悬案计时器的载体：灯不亮，但灯座上写着案子还剩多少。
    /// </summary>
    public class OnAirLight : MonoBehaviour
    {
        Renderer _bulb;
        float _next;

        void Start() => _bulb = GetComponentInChildren<Renderer>();

        void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + 0.5f;
            if (_bulb == null || _bulb.sharedMaterial == null) return;
            // 它**不会**亮。偶尔的一点点起伏只是电流，不是"轮到你了"
            float flicker = 0.12f + Mathf.PingPong(Time.time * 0.08f, 0.05f);
            var c = new Color(0.35f * flicker * 6f, 0.06f, 0.06f);
            _bulb.material.color = c;
            if (_bulb.material.HasProperty("_BaseColor")) _bulb.material.SetColor("_BaseColor", c);
        }
    }

    /// <summary>
    /// 8-2 的目标动作交互物（归还 / 完成本职）。
    ///
    /// 三个目标交互物**全部位于视线锥内**，且不可能全程回避（验收第 39 条）：
    /// 绕开无法通关。这条设计是本章防"回避成为最优解"的地基。
    /// </summary>
    public class ObjectiveStation : MonoBehaviour
    {
        public string objectiveId = ShameLineController.ObjReturn;
        /// <summary>需要长按多久（中途松手即失败）。</summary>
        public float holdSeconds = 3.2f;
        /// <summary>期间是否承受 Mental Attack（「完成本职」用）。</summary>
        public bool underMentalAttack;
        public float interactRange = 3.4f;

        float _held;
        bool _done;
        float _lastHint = -99f;
        float _nextTick;

        void Update()
        {
            if (_done) return;
            var ctl = ShameLineController.Instance;
            if (ctl != null && ctl.ObjectiveDone(objectiveId)) { _done = true; return; }

            var player = AdversityRoad.Core.ActorRegistry.Player;
            if (player == null) return;
            if (Vector3.Distance(transform.position, player.transform.position) > interactRange)
            {
                if (_held > 0f)
                {
                    _held = 0f;
                    GameEvents.RaiseSubtitle("手松开了，进度归零——" + objectiveId + "得一次做完。");
                }
                return;
            }

            if (Time.time - _lastHint > 3f)
            {
                _lastHint = Time.time;
                GameEvents.RaiseSubtitle("「" + objectiveId + "」：按住【用】/ R " +
                    holdSeconds.ToString("0.0") + " 秒。它就在视线里，绕不开。");
            }

            bool holding = Input.GetKey(KeyCode.R) || MobileInput.GetHeld("Interact");
            if (!holding)
            {
                if (_held > 0.2f)
                    GameEvents.RaiseSubtitle("松手了。被看着的时候，手最容易先松。");
                _held = 0f;
                return;
            }

            _held += Time.deltaTime;

            if (underMentalAttack && Time.time >= _nextTick)
            {
                _nextTick = Time.time + 1.1f;
                float dmg = 7f;
                var gm = GameManager.Instance;
                if (gm != null && gm.safety != null) dmg *= gm.safety.MentalDamageMultiplier();
                player.Stats.TakeMentalDamage(Personalization.WeaknessAxis.Shame, dmg);
            }

            if (_held >= holdSeconds)
            {
                _done = true;
                GameAudio.Play(GameAudio.Sfx.Parry, 0.8f);
                CombatFeedback.ShockRing(transform.position, new Color(0.9f, 0.88f, 0.6f), 2.6f);
                if (ctl != null) ctl.CompleteObjective(objectiveId);
            }
        }
    }

    /// <summary>
    /// 教室门（8.6.2 步行离场）：终局要求玩家**走**出去。
    /// 奔跑离场判定为回避，本关记为普通结算——回避与完成在动作层面被区分开。
    /// </summary>
    public class ClassroomExit : MonoBehaviour
    {
        void OnTriggerEnter(Collider other)
        {
            var player = other.transform.root.GetComponentInChildren<PlayerController>();
            if (player == null) return;
            var ctl = ShameLineController.Instance;
            if (ctl == null) return;
            if (!ctl.WalkOutReady)
            {
                GameEvents.RaiseSubtitle("还有事没做完。门在这里，但今天不是从这里跑掉的日子。");
                return;
            }
            // 用「有没有在冲刺/翻滚」判定：这是动作层面的事实，不是玩家的自述
            bool walked = !player.IsDodging &&
                          !(Input.GetKey(KeyCode.LeftShift) || MobileInput.GetHeld("Dodge"));
            ctl.NoteWalkOut(walked);
        }
    }

    /// <summary>
    /// 搜查回响（8.6.1 可选支线）：玩家可以选择搜查他人物品来找回失物。
    ///
    /// 系统允许执行，**不做道德弹窗**；执行后 Exposure 上限 +20，
    /// 并解锁 1 个新的宿敌候选。展示代价，不做说教。
    /// 主动放弃同样被记录——记为策略判断，不作为道德评分展示（8.9.2）。
    /// </summary>
    public class SearchEcho : ShameInteractable
    {
        bool _resolved;

        protected override string Prompt() =>
            _resolved ? "" : "别人的包就放在这里。按【用】/ R 决定";

        protected override void Interact()
        {
            if (_resolved || ShameChoicePanel.AnyOpen) return;
            // 系统允许执行，不做道德弹窗：两个选项并排，没有一个被标成"正确的"
            ShameChoicePanel.Show("搜查回响", "失物也许在这里，也许不在。",
                new[] { "搜查", "走开" }, i => { if (i == 0) DoSearch(); else if (i == 1) Decline(); });
        }

        void DoSearch()
        {
            if (_resolved) return;
            _resolved = true;
            ShameLine.Data.searchEchoTaken = true;
            ShameLine.Persist();

            var exposure = ExposureSystem.Instance;
            if (exposure != null) exposure.RaiseCap(20f, "你翻了别人的东西");
            GameEvents.RaiseSubtitle("东西不在这里。你翻过了——这件事本身现在也在场上了。");
            Adversity.NemesisSystem.AdjustTactic("低语传播", false);
        }

        void Decline()
        {
            if (_resolved) return;
            _resolved = true;
            GameEvents.RaiseSubtitle("你没有翻。记为一次策略判断——这里不打分。");
            Adversity.AdversityProfile.ObserveStrength("拒绝搜查回响", ShameLine.LevelEchoClassroom);
        }
    }

    /// <summary>恢复点：站上去即登记为羞耻状态的回落点（不回退关卡进度）。</summary>
    public class ShameRecoverySpot : MonoBehaviour
    {
        void OnTriggerStay(Collider other)
        {
            if (other.transform.root.GetComponentInChildren<PlayerController>() == null) return;
            var ctl = ShameLineController.Instance;
            if (ctl != null) ctl.NoteRecoveryPoint(transform.position);
        }
    }
}
