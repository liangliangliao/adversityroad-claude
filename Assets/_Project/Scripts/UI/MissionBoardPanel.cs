using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 目标板面板（安全屋·今日唯一目标）：
    /// 从方案的成长挑战模板里选一条，或自己写一条（游戏内挑战 / 现实行动小任务均可），
    /// 「钉下目标」后 HUD 常驻显示；「完成打卡」每日一次奖励复盘点并恢复行动力。
    /// 这让每次进入游戏都有一个明确的小目标——游戏不只是娱乐，也有成长反馈。
    /// </summary>
    public class MissionBoardPanel : MonoBehaviour
    {
        // 方案「玩家每次进入游戏的目标」模板
        static readonly string[] Suggestions =
        {
            "今天练习一次明确拒绝",
            "今天完成一次注意力回收",
            "今天击败一个拖延敌人",
            "今天完成一个五分钟行动任务",
            "今天整理一个旧事档案（归档一座展柜）",
            "今天通过一次责任归还战斗",
            "今天在现实里先做五分钟，不求做完",
        };

        GameObject _panel;
        InputField _input;
        Text _statusText;

        public static MissionBoardPanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<MissionBoardPanel>();
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "MissionBoardPanel", new Vector2(1150, 1000),
                new Color(0.07f, 0.08f, 0.11f, 0.98f));

            var title = UiUtil.MakeText(_panel.transform, "Title", "目 标 板 · 今 日 唯 一 目 标", 38,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -46), new Vector2(800, 54));

            var hint = UiUtil.MakeText(_panel.transform, "Hint",
                "一天只钉一个目标：可以是游戏内挑战，也可以是现实行动小任务。\n点模板填入，或直接改写成自己的话。", 22,
                TextAnchor.MiddleCenter, new Color(0.8f, 0.8f, 0.85f));
            UiUtil.SetRect(hint, new Vector2(0.5f, 1f), new Vector2(0, -110), new Vector2(1000, 60));

            for (int i = 0; i < Suggestions.Length; i++)
            {
                string s = Suggestions[i];
                UiUtil.MakeButton(_panel.transform, s, new Vector2(0.5f, 1f),
                    new Vector2(-260 + (i % 2) * 520, -190 - (i / 2) * 78),
                    new Vector2(500, 64), new Color(0.2f, 0.24f, 0.32f, 0.95f),
                    () => { if (_input != null) _input.text = s; }, 21);
            }

            _input = UiUtil.MakeInput(_panel.transform, "写下今天只做的这一件事……",
                new Vector2(0.5f, 1f), new Vector2(0, -570), new Vector2(1020, 90), false);
            _input.textComponent.fontSize = 26;

            _statusText = UiUtil.MakeText(_panel.transform, "Status", "", 24,
                TextAnchor.MiddleCenter, new Color(0.85f, 0.95f, 0.85f));
            UiUtil.SetRect(_statusText, new Vector2(0.5f, 1f), new Vector2(0, -660), new Vector2(1000, 60));

            UiUtil.MakeButton(_panel.transform, "钉下目标（目标钉）", new Vector2(0.5f, 0f),
                new Vector2(-330, 130), new Vector2(400, 76),
                new Color(0.6f, 0.45f, 0.15f, 0.95f), OnPin, 25);
            UiUtil.MakeButton(_panel.transform, "完成打卡（+1 复盘点）", new Vector2(0.5f, 0f),
                new Vector2(120, 130), new Vector2(440, 76),
                new Color(0.25f, 0.5f, 0.35f, 0.95f), OnComplete, 25);
            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f),
                new Vector2(0, 44), new Vector2(260, 70),
                new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 26);

            _panel.SetActive(false);
        }

        void OnPin()
        {
            if (_input == null || _input.text.Trim().Length == 0)
            {
                GameEvents.RaiseSubtitle("先写下（或点选）一个目标，再钉。");
                return;
            }
            GoalSystem.Pin(_input.text);
            GameAudio.Play(GameAudio.Sfx.Hit, 0.6f);
            Refresh();
        }

        void OnComplete()
        {
            var pc = FindObjectOfType<PlayerController>();
            if (!GoalSystem.CompleteToday(pc != null ? pc.Stats : null))
            {
                GameEvents.RaiseSubtitle(GoalSystem.DoneToday
                    ? "今天已经打过卡了——明天再钉一个新目标。"
                    : "还没有钉下今天的目标。");
                Refresh();
                return;
            }
            GameAudio.Play(GameAudio.Sfx.Parry, 0.8f);
            Refresh();
        }

        void Refresh()
        {
            if (GoalSystem.DoneToday)
                _statusText.text = "✓ 今日目标已完成：「" + GoalSystem.CurrentGoal + "」\n明天再来钉一个新的。";
            else if (GoalSystem.PinnedToday)
                _statusText.text = "◎ 已钉下：「" + GoalSystem.CurrentGoal + "」\n完成后回来打卡。";
            else
                _statusText.text = "今天还没有钉目标——不用等状态完美，先钉一个最小的。";
        }

        public void Toggle()
        {
            if (_panel.activeSelf) { Hide(); return; }
            if (GoalSystem.PinnedToday && _input != null && _input.text.Length == 0)
                _input.text = GoalSystem.CurrentGoal;
            Refresh();
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            Time.timeScale = 0f;
        }

        void Hide()
        {
            _panel.SetActive(false);
            Time.timeScale = 1f;
        }
    }
}
