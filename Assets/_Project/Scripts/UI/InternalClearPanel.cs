using System.Text;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;
using AdversityRoad.InternalOS;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 第 9-26 章的通关卡。
    ///
    /// 【为什么要有它】
    /// 经典关卡打完会有一张复盘卡（<see cref="ReflectionPanel"/> 的事实／感受／边界／行动
    /// 四栏）。这一批关卡通关之后什么都没有——玩家原话
    /// "通关之后没有像经典关卡那样的提示卡及其相关内容"，一场关卡就这么无声无息地结束了。
    ///
    /// 【这张卡该写什么，PRD 已经规定了】
    /// V2.2 的胜利分三层：Simulation（游戏里做到了）→ Reality（现实里真的做了）
    /// → Transfer（换个情境又做了一次）。按下提交台只完成第一层。
    /// 所以这张卡的正事不是发奖，而是**把后两层交回给玩家**：
    ///   · 你刚才在游戏里做成的那个动作是什么
    ///   · 它在现实里对应哪一件事（Reality Victory 的条件）
    ///   · 现在的 Mastery 走到哪一级
    /// 这正是这一批关卡和普通关卡的根本区别：关卡里的胜利只是排练。
    /// </summary>
    public class InternalClearPanel : MonoBehaviour
    {
        static InternalClearPanel _open;

        GameObject _panel;
        float _restoreTimeScale = 1f;

        public static void Show(InternalLevelData lv)
        {
            if (lv == null || _open != null) return;
            var canvas = UiUtil.MainCanvas();
            if (canvas == null) return;

            var go = new GameObject("InternalClearPanel");
            _open = go.AddComponent<InternalClearPanel>();
            _open.Build(canvas.transform, lv);
        }

        void Build(Transform canvas, InternalLevelData lv)
        {
            var frame = UiUtil.MakePanel(canvas, "InternalClearFrame", new Vector2(1180, 780),
                new Color(0.07f, 0.10f, 0.09f, 0.98f));

            var title = UiUtil.MakeText(frame.transform, "Title",
                "✔ " + lv.levelId + "《" + lv.name + "》 完成", 40,
                TextAnchor.MiddleCenter, new Color(0.55f, 0.92f, 0.62f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -54), new Vector2(1080, 56));

            var ch = InternalChapterCatalog.Chapter(lv.chapterId);
            int mastery = ch != null ? RealityVictorySystem.Mastery(ch.chapterId) : 0;

            var sb = new StringBuilder();
            sb.Append("【你刚才做成的那件事】\n").Append(lv.Objective).Append("\n\n");

            // 这一关自己的意义句（9-1/9-2 有专门写的；其余回落到章节核心）
            var run = InternalLevelRunner.ActiveHere;
            var gate = run != null ? run.Gate : null;
            string meaning = gate != null ? gate.SubmitMeaning() : "";
            if (string.IsNullOrEmpty(meaning) && ch != null) meaning = ch.core;
            if (!string.IsNullOrEmpty(meaning))
                sb.Append("【这一关想让你带走的】\n").Append(meaning).Append("\n\n");

            // PRD 的三层胜利：游戏里做到只是第一层
            sb.Append("【但这只是第一层】\n")
              .Append("游戏里的胜利叫 Simulation——排练。这一关真正的通关条件写在现实里：\n")
              .Append("· 现实胜利：").Append(lv.realityVictory).Append('\n')
              .Append("· 迁移胜利：换一个情境，再做一次同样的判断。\n\n")
              .Append("【当前熟练度】M").Append(mastery)
              .Append("　（现实里真的做过，才会往上走）");

            var body = UiUtil.MakeText(frame.transform, "Body", sb.ToString(), 24,
                TextAnchor.UpperLeft, new Color(0.90f, 0.93f, 0.90f));
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Truncate;
            body.lineSpacing = 1.2f;
            UiUtil.SetRect(body, new Vector2(0.5f, 1f), new Vector2(0, -400), new Vector2(1040, 520));

            UiUtil.MakeButton(frame.transform, "我记下了", new Vector2(0.5f, 0f),
                new Vector2(0, 52), new Vector2(360, 72),
                new Color(0.20f, 0.44f, 0.30f, 0.98f), Close, 28);

            _panel = frame;
            _panel.transform.SetAsLastSibling();
            _restoreTimeScale = Time.timeScale > 0.01f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
        }

        void Close()
        {
            Time.timeScale = _restoreTimeScale;
            if (_panel != null) Destroy(_panel);
            if (_open == this) _open = null;
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (_open == this)
            {
                _open = null;
                if (Mathf.Approximately(Time.timeScale, 0f)) Time.timeScale = _restoreTimeScale;
            }
        }
    }
}
