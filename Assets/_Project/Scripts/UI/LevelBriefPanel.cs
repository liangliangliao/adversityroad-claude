using System.Text;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;
using AdversityRoad.InternalOS;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 进关时的"这一关怎么玩"卡片。
    ///
    /// 【为什么要有它】
    /// 玩家连着三轮反馈"游戏规则不清楚，不知道如何玩"。我前两轮的答复都是
    /// 把 HUD 那一行目标行写得更准——可那一行只回答"下一步去哪"，
    /// 它回答不了"这一关到底怎么玩、场上这几样东西各是干什么的"，
    /// 而且一行小字压在屏幕角上，很容易根本没被看见。
    ///
    /// 所以这里把规则做成**进关必看的一张卡**：三到五条，一条一句，
    /// 说清场上有什么、先做什么、什么算通关。读完按"开始"才进场。
    ///
    /// 【这张卡只说真的做出来了的东西】
    /// 文案来自关卡数据的 howToPlay，那一栏的硬规矩写在
    /// <see cref="InternalLevelData.howToPlay"/> 上：关卡表里写着、而代码还没做的机制
    /// 一个字都不许写。卡上说有六张修改卡而场上没有，比不给说明更糟。
    ///
    /// 【为什么第一条永远是"不靠清怪通关"】
    /// 这是 PRD 3.4 的硬规则，也是玩家最容易想反的一条：
    /// 这个游戏前 8 章都是打完就过，到这一批章节突然不是了。不写明，玩家会
    /// 在场上找敌人打，打完发现没过关，于是"不知道怎么玩"。
    /// </summary>
    public class LevelBriefPanel : MonoBehaviour
    {
        static LevelBriefPanel _open;

        GameObject _panel;
        float _restoreTimeScale = 1f;

        public static bool AnyOpen => _open != null;

        /// <summary>进关时弹一次。没有 howToPlay 的关卡（第 10 章以后）不弹。</summary>
        public static void Show(InternalLevelData lv)
        {
            if (lv == null || lv.howToPlay == null || lv.howToPlay.Count == 0) return;
            if (_open != null) return;

            var canvas = UiUtil.MainCanvas();
            if (canvas == null) return;

            var go = new GameObject("LevelBriefPanel");
            _open = go.AddComponent<LevelBriefPanel>();
            _open.Build(canvas.transform, lv);
        }

        void Build(Transform canvas, InternalLevelData lv)
        {
            var frame = UiUtil.MakePanel(canvas, "LevelBriefFrame", new Vector2(1180, 760),
                new Color(0.07f, 0.08f, 0.11f, 0.98f));

            var title = UiUtil.MakeText(frame.transform, "Title",
                lv.levelId + "《" + lv.name + "》", 40,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -52), new Vector2(1080, 56));

            var goal = UiUtil.MakeText(frame.transform, "Goal",
                "要做的事：" + lv.Objective, 25,
                TextAnchor.UpperLeft, new Color(0.72f, 0.92f, 0.78f));
            UiUtil.SetRect(goal, new Vector2(0.5f, 1f), new Vector2(0, -132), new Vector2(1040, 70));

            var sb = new StringBuilder();
            int step = 0;
            for (int i = 0; i < lv.howToPlay.Count; i++)
            {
                string line = lv.howToPlay[i];
                if (string.IsNullOrEmpty(line)) continue;
                // 括号开头的是那条通用提醒，不给它编号——它不是一个步骤。
                // 序号用自己的计数器，不用下标：否则提醒夹在中间时后面的步骤会跳号。
                if (line.StartsWith("（")) sb.Append('\n').Append(line).Append('\n');
                else { step++; sb.Append(step).Append(". ").Append(line).Append('\n'); }
            }

            var body = UiUtil.MakeText(frame.transform, "Steps", sb.ToString(), 25,
                TextAnchor.UpperLeft, new Color(0.88f, 0.9f, 0.94f));
            UiUtil.SetRect(body, new Vector2(0.5f, 1f), new Vector2(0, -400), new Vector2(1040, 440));

            UiUtil.MakeButton(frame.transform, "开始", new Vector2(0.5f, 0f),
                new Vector2(0, 54), new Vector2(320, 72),
                new Color(0.22f, 0.42f, 0.32f, 0.98f), Close, 28);

            _panel = frame;
            _panel.transform.SetAsLastSibling();

            // 读规则的时候世界该停下来——否则 9-1 的白墙正在往外推，
            // 玩家读完说明出来发现提交台已经退远了两阶。
            //
            // 记下来的原速若本身就是 0（另一个面板已经把世界停住了），
            // 关掉这张卡时照原样还回去会把游戏永远冻在那儿——所以兜底给 1。
            _restoreTimeScale = Time.timeScale > 0.01f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
        }

        void Close()
        {
            Time.timeScale = _restoreTimeScale;
            if (_panel != null) Destroy(_panel);
            if (_open == this) _open = null;
            // 连同承载它的空物体一起销毁：只销毁组件会在场里留一串空壳
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (_open == this)
            {
                _open = null;
                // 面板被外力销毁（切场景等）时别把游戏永远停在 0 倍速
                if (Mathf.Approximately(Time.timeScale, 0f)) Time.timeScale = _restoreTimeScale;
            }
        }
    }
}
