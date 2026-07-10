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
            "◤ 基本键 ↔ 动作库（每个键都是真实动作） ◢\n" +
            "  移动＝摇杆（半推走·全推跑）   跳＝跳键   蹲＝蹲键   闪＝翻滚（无敌帧）\n" +
            "  拳＝巨剑连斩：巨剑横斩 → 巨剑撩斩 → 突刺 → 巨剑旋风斩   （伤害高）\n" +
            "  腿＝腿法连段：正踢 → 侧踹 → 旋身空翻踢 → 飞踢   （削韧强，击退大）\n" +
            "  重＝按住蓄力松开·巨剑跳劈    挡＝格挡（按下瞬间为定心格挡，化解心理攻击）\n" +
            "  换角色/换武器＝右上「角色」面板（角色与武器分离，武器库重选即替换手中武器）\n" +
            "  ※ 连点任意攻击键即无缝连段：每击命中后立刻可接下一击，拳腿可自由混接；\n" +
            "    出招自动咬住身边敌人（摇杆用来在多个敌人间选目标），差多远冲多远必中\n" +
            "\n" +
            "◤ 派生动作（跳/蹲 + 攻击键） ◢\n" +
            "  跳+拳＝空袭跳劈   跳+腿＝飞踢   跳+重＝空袭·裂地跳劈   蹲+腿＝扫堂腿   蹲+拳＝低位突刺\n" +
            "  ※ 每招范围独立：突刺长窄 · 横斩横宽 · 撩斩纵高 · 旋风斩/扫堂环身360°；蓄力/技能越高范围越大\n" +
            "\n" +
            "◤ 组合招式（按顺序连点拳/腿，自动成招） ◢\n" +
            "  普通(无消耗)：拳拳拳＝三连斩·大回旋(旋风斩收) · 腿腿腿＝三连腿·裂空飞踢\n" +
            "                拳拳腿＝双斩·空翻旋踢 · 腿腿拳＝双腿·贯心突刺\n" +
            "  绝招(需2势·金光爆发)：拳拳腿腿＝龙卷·旋风绝斩 · 腿腿拳拳＝踏空·裂地跳劈\n" +
            "                        拳腿拳腿＝剑影·惊鸿飞踢\n" +
            "  —— 招式越复杂伤害越高，绝招需积攒意势能量，不可无限使用\n" +
            "\n" +
            "◤ 重键 / 意势能量（命中/完美闪避/蓄力积攒，最多3点，HUD 圆点显示） ◢\n" +
            "  重（按住）＝蓄力，松开＝巨剑跳劈（越久越痛；2势＝旋风终结；满3势＝超必杀「觉醒·乱舞」）\n" +
            "  重（轻点，连段中）＝切手·撩斩快速反击\n" +
            "  指令技（轻点重+摇杆方向，共享冷却，不可连发）：\n" +
            "    前+重＝疾影突刺   后+重＝旋身空翻踢   左/右+重＝左/右旋风斩\n" +
            "\n" +
            "◤ 能量远攻 / 防守反击 ◢\n" +
            "  气＝能量斩·斩念气刃（需2势，巨型剑气，伤害极高，调息冷却）\n" +
            "  定＝定心护体（回复心理属性）    还＝责任归还（法院关卡核心技）\n" +
            "  敌人出手瞬间翻滚＝完美闪避（时缓+意势+1+下一击必暴击）\n" +
            "  被击倒瞬间按闪＝受身快速起身（无敌帧）";

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
