using System.Text;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;
using AdversityRoad.Goals;
using AdversityRoad.Player;

namespace AdversityRoad.UI
{
    /// <summary>
    /// Goal Board（方案 17.2）：目标前台化的主界面——
    /// 输入一个你真正想实现的目标，游戏把通往它的道路变成这座开放世界。
    ///
    /// 这里做四件事：创建目标（含有效性检查与安全拒绝）、查看目标与当前里程碑、
    /// 重铸目标（Goal Evolution，不判失败）、记录现实行动进度（Reality Progress）。
    /// </summary>
    public class GoalOSPanel : MonoBehaviour
    {
        GameObject _panel;
        InputField _input;
        Text _bodyText, _checkText;
        GoalTemplate _pickedTemplate;
        bool _reforgeMode;

        public static GoalOSPanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<GoalOSPanel>();
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "GoalOSPanel", new Vector2(1320, 1010),
                new Color(0.07f, 0.08f, 0.11f, 0.98f));

            var title = UiUtil.MakeText(_panel.transform, "Title", "目 标 · Goal OS", 40,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -44), new Vector2(700, 56));

            var hint = UiUtil.MakeText(_panel.transform, "Hint",
                "输入一个你真正想实现的目标——开放世界会围绕它重排道路、障碍与敌人。\n" +
                "目标是主线；拖延、边界、专注、旧我只有在真的挡住它时才会被叫出来。", 22,
                TextAnchor.MiddleCenter, new Color(0.8f, 0.8f, 0.85f));
            UiUtil.SetRect(hint, new Vector2(0.5f, 1f), new Vector2(0, -100), new Vector2(1180, 60));

            // 模板行（方案 3.2）：非诊断型模板，点一下填入输入框
            for (int i = 0; i < GoalParser.Templates.Length; i++)
            {
                var t = GoalParser.Templates[i];
                UiUtil.MakeButton(_panel.transform, t.title, new Vector2(0.5f, 1f),
                    new Vector2(-300 + (i % 2) * 600, -168 - (i / 2) * 62),
                    new Vector2(580, 52), new Color(0.2f, 0.24f, 0.32f, 0.95f),
                    () => { _pickedTemplate = t; if (_input != null) _input.text = t.title; Refresh(); }, 20);
            }

            _input = UiUtil.MakeInput(_panel.transform, "例如：我要完成一款软件并发布第一版",
                new Vector2(0.5f, 1f), new Vector2(0, -448), new Vector2(1180, 76), false);
            _input.textComponent.fontSize = 24;
            _input.onValueChanged.AddListener(_ => RunCheck());

            _checkText = UiUtil.MakeText(_panel.transform, "Check", "", 20,
                TextAnchor.UpperLeft, new Color(0.85f, 0.8f, 0.6f));
            UiUtil.SetRect(_checkText, new Vector2(0.5f, 1f), new Vector2(0, -556), new Vector2(1180, 120));
            _checkText.lineSpacing = 1.1f;

            _bodyText = UiUtil.MakeText(_panel.transform, "Body", "", 21,
                TextAnchor.UpperLeft, new Color(0.92f, 0.92f, 0.95f));
            UiUtil.SetRect(_bodyText, new Vector2(0.5f, 1f), new Vector2(0, -770), new Vector2(1180, 300));
            _bodyText.lineSpacing = 1.12f;

            UiUtil.MakeButton(_panel.transform, "开始这条旅程", new Vector2(0.5f, 0f),
                new Vector2(-430, 116), new Vector2(320, 74),
                new Color(0.6f, 0.45f, 0.15f, 0.95f), OnCreate, 24);
            UiUtil.MakeButton(_panel.transform, "重铸目标", new Vector2(0.5f, 0f),
                new Vector2(-90, 116), new Vector2(300, 74),
                new Color(0.35f, 0.4f, 0.55f, 0.95f), OnReforge, 24);
            UiUtil.MakeButton(_panel.transform, "现实行动打卡", new Vector2(0.5f, 0f),
                new Vector2(240, 116), new Vector2(320, 74),
                new Color(0.25f, 0.5f, 0.35f, 0.95f), OnRealityLog, 24);
            UiUtil.MakeButton(_panel.transform, "重新生成 AI 专属章节", new Vector2(0.5f, 0f),
                new Vector2(-260, 40), new Vector2(420, 66),
                new Color(0.4f, 0.32f, 0.5f, 0.95f), OnRegenerate, 22);
            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f),
                new Vector2(180, 40), new Vector2(240, 66),
                new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 24);

            _panel.SetActive(false);
        }

        public void Toggle()
        {
            if (_panel == null) return;
            bool show = !_panel.activeSelf;
            _panel.SetActive(show);
            if (show) Refresh();
        }

        public void Show()
        {
            if (_panel == null) return;
            _panel.SetActive(true);
            Refresh();
        }

        void Hide() => _panel.SetActive(false);

        // ================= 有效性检查（方案 3.4） =================

        void RunCheck()
        {
            if (_checkText == null) return;
            string text = _input != null ? _input.text : "";
            if (string.IsNullOrEmpty(text)) { _checkText.text = ""; return; }

            var other = GoalOS.Active;
            var c = GoalParser.Check(text, other != null && !other.completed ? other : null);
            var sb = new StringBuilder();
            if (!c.accepted)
            {
                sb.Append("✕ ").Append(c.rejectReason).Append('\n');
                foreach (var a in c.starterActions) sb.Append("   · ").Append(a).Append('\n');
                _checkText.color = new Color(1f, 0.6f, 0.55f);
            }
            else
            {
                _checkText.color = new Color(0.85f, 0.8f, 0.6f);
                if (c.notes.Count == 0) sb.Append("✓ 这是一个有可行动方向的目标——可以直接开始。");
                foreach (var n in c.notes) sb.Append("· ").Append(n).Append('\n');
                if (c.starterActions.Count > 0)
                {
                    sb.Append("  候选起步动作：");
                    foreach (var a in c.starterActions) sb.Append('\n').Append("   · ").Append(a);
                }
            }
            _checkText.text = sb.ToString();
        }

        // ================= 操作 =================

        void OnCreate()
        {
            string text = _input != null ? _input.text : "";
            var check = GoalParser.Check(text, null);
            if (!check.accepted)
            {
                GameEvents.RaiseSubtitle(check.rejectReason);
                return;
            }
            if (string.IsNullOrEmpty(text.Trim()) && _pickedTemplate == null)
            {
                GameEvents.RaiseSubtitle("先写一句你想去的方向——写得粗糙也没关系，之后随时可以改。");
                return;
            }

            var goal = GoalOS.CreateGoal(text, _pickedTemplate);
            GoalChapterGenerator.Ensure();
            if (GoalChapterGenerator.Instance != null)
                GoalChapterGenerator.Instance.BuildJourneyChapters(goal);
            if (OpenWorld.GoalWorldBinder.Instance != null)
                OpenWorld.GoalWorldBinder.Instance.ResetBindings();
            _reforgeMode = false;
            Refresh();
        }

        void OnReforge()
        {
            var goal = GoalOS.Active;
            if (goal == null)
            {
                GameEvents.RaiseSubtitle("还没有进行中的旅程——先开始一条。");
                return;
            }
            if (!_reforgeMode)
            {
                _reforgeMode = true;
                if (_input != null) _input.text = goal.title;
                GameEvents.RaiseSubtitle("重铸模式：改写上面的目标后再按一次「重铸目标」。" +
                    "改变方向不判失败——原目标与原因都会留在演化史里。");
                Refresh();
                return;
            }
            _reforgeMode = false;
            GoalOS.ReforgeGoal(_input != null ? _input.text : "", "获得了新的事实，判断改变了");
            var g = GoalOS.Active;
            if (g != null && GoalChapterGenerator.Instance != null)
                GoalChapterGenerator.Instance.Regenerate(g);
            Refresh();
        }

        void OnRealityLog()
        {
            string text = _input != null ? _input.text.Trim() : "";
            if (text.Length == 0)
            {
                GameEvents.RaiseSubtitle("在输入框里写下你在现实里做了什么，再按一次打卡。");
                return;
            }
            GoalOS.LogRealityProgress(text);
            var player = FindObjectOfType<PlayerController>();
            if (player != null)
            {
                player.Stats.RestoreAxis(Personalization.WeaknessAxis.Procrastination, 30f);
                player.Stats.ReduceRumination(12f);
            }
            Adversity.CourageSystem.NoteGoalAction("现实里推进了一步");
            GameEvents.RaiseSubtitle("现实行动已记录——游戏里的进度不能替代它，但可以陪着它。");
            if (_input != null) _input.text = "";
            Refresh();
        }

        void OnRegenerate()
        {
            var goal = GoalOS.Active;
            if (goal == null) { GameEvents.RaiseSubtitle("还没有进行中的旅程。"); return; }
            GoalChapterGenerator.Ensure();
            if (GoalChapterGenerator.Instance != null) GoalChapterGenerator.Instance.Regenerate(goal);
            if (OpenWorld.GoalWorldBinder.Instance != null)
                OpenWorld.GoalWorldBinder.Instance.ResetBindings();
            Refresh();
        }

        // ================= 展示 =================

        void Refresh()
        {
            RunCheck();
            if (_bodyText == null) return;

            var g = GoalOS.Active;
            if (g == null)
            {
                _bodyText.text =
                    "当前没有进行中的旅程。\n\n" +
                    "选一个模板或自己写一句，然后按「开始这条旅程」。\n" +
                    "系统会立刻做三件事：\n" +
                    "  1. 把目标拆成里程碑、关键行动、障碍与需要成长的能力；\n" +
                    "  2. 从 V1 七大经典章节里按相关性挑出会挡路的那几个；\n" +
                    "  3. 生成至少一个**只属于这个目标**的 AI 专属章节（没网也有本地兜底）。";
                return;
            }

            var sb = new StringBuilder();
            sb.Append("【").Append(g.title).Append('】');
            if (g.completed) sb.Append("  ✦ 已达成");
            sb.Append('\n');
            if (!string.IsNullOrEmpty(g.why)) sb.Append("为什么：").Append(g.why).Append('\n');
            sb.Append("完成条件：").Append(g.successCondition).Append('\n');

            var ms = g.CurrentMilestone();
            sb.Append("当前里程碑：").Append(ms != null ? ms.title : "全部完成，终局已开启").Append('\n');
            sb.Append("进度：").Append(GoalJourneyGraph.ProgressLine(g)).Append('\n');

            int legacy = 0, user = 0, ai = 0, cleared = 0;
            foreach (var c in g.chapters)
            {
                if (c.cleared) cleared++;
                if (c.source == ChapterSource.Legacy) legacy++;
                else if (c.source == ChapterSource.UserPreset) user++;
                else if (c.validated) ai++;
            }
            sb.Append("章节来源：经典 ").Append(legacy)
              .Append(" · 玩家预设 ").Append(user)
              .Append(" · AI 专属 ").Append(ai)
              .Append("（已通关 ").Append(cleared).Append("）\n");
            sb.Append(g.journeyReady
                ? "旅程状态：正式生成完成 ✓\n"
                : "旅程状态：⚠ 缺少 AI 专属章节，尚未进入「正式生成完成」\n");

            if (g.evolution.Count > 0)
            {
                sb.Append("\n目标演化史（").Append(g.evolution.Count).Append(" 次重铸）：\n");
                for (int i = g.evolution.Count - 1; i >= 0 && i >= g.evolution.Count - 3; i--)
                {
                    var e = g.evolution[i];
                    sb.Append("  · 「").Append(e.oldTitle).Append("」→「").Append(e.newTitle)
                      .Append("」  原因：").Append(e.reason).Append('\n');
                }
            }

            var log = GoalOS.RealityLog;
            if (log.Count > 0)
            {
                sb.Append("\n现实行动记录（最近 3 条）：\n");
                for (int i = 0; i < log.Count && i < 3; i++)
                    sb.Append("  · ").Append(log[i]).Append('\n');
            }

            _bodyText.text = sb.ToString();
        }
    }
}
