using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 招式表面板：完整的动作规则与武术技能规则（KOF × 黑神话悟空体系）。
    /// 武术技能不是一键释放，而是玩家用拳/腿/跳/蹲/重按规则打出来的组合。
    /// </summary>
    public class MovesPanel : MonoBehaviour
    {
        GameObject _panel;

        const string MovesText =
            "◤ 基本动作 ◢\n" +
            "  移动＝摇杆（半推走·全推跑）   跳＝跳键   蹲＝蹲键   闪＝翻滚（无敌帧）\n" +
            "  挡＝格挡（按下瞬间为定心格挡，化解心理攻击）\n" +
            "\n" +
            "◤ 拳脚连段（可自由混接，最多四段） ◢\n" +
            "  拳：直拳 → 交替直拳 → 上挑 → 跃劈       （伤害高）\n" +
            "  腿：前蹬 → 侧踢 → 后旋踢 → 强旋踢       （削韧强）\n" +
            "  跳+拳＝下劈坠击      跳+腿＝飞踢      蹲+攻＝扫堂腿\n" +
            "\n" +
            "◤ 组合招式（按序打出自动触发，金光增伤） ◢\n" +
            "  拳拳拳＝三连崩拳         腿腿腿＝连环三腿\n" +
            "  拳拳腿＝崩拳扫腿         腿腿拳＝连腿贯拳\n" +
            "  拳腿拳腿＝拳腿相济       拳拳腿腿＝双龙出海\n" +
            "  腿腿拳拳＝踏山贯拳\n" +
            "\n" +
            "◤ 重击体系（意势：命中/完美闪避/蓄力积攒，最多3点） ◢\n" +
            "  重（按住）＝蓄力重劈（越久越痛，蓄力积势）\n" +
            "  重（轻点，连段中）＝切手技快速反击\n" +
            "  前+重（轻点）＝突进肘击      后+重（轻点）＝吹飞踢（大击退）\n" +
            "  满3势 放开蓄力＝超必杀「觉醒·乱舞」六连击\n" +
            "\n" +
            "◤ 防守反击 ◢\n" +
            "  敌人出手瞬间翻滚＝完美闪避（时缓+意势+1+下一击必暴击）\n" +
            "  被击倒瞬间按闪＝受身快速起身（无敌帧）\n" +
            "  技能键：定＝定心护体（回复心理属性）   气＝斩念气刃（远程）";

        public static MovesPanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<MovesPanel>();
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "MovesPanel", new Vector2(1420, 960),
                new Color(0.07f, 0.07f, 0.11f, 0.97f));

            var title = UiUtil.MakeText(_panel.transform, "Title", "招 式 表", 40,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -44), new Vector2(800, 54));

            var body = UiUtil.MakeText(_panel.transform, "Body", MovesText, 25,
                TextAnchor.UpperLeft, new Color(0.9f, 0.94f, 0.95f));
            UiUtil.SetRect(body, new Vector2(0.5f, 0.5f), new Vector2(0, -14), new Vector2(1320, 780));
            body.lineSpacing = 1.12f;

            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f), new Vector2(0, 56),
                new Vector2(260, 74), new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 28);

            _panel.SetActive(false);
        }

        public void Toggle()
        {
            if (_panel.activeSelf) { Hide(); return; }
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
