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
            "◤ 基本键（大作标准布局：轻/重双连段 + 闪避 + 格挡 + 蓄力） ◢\n" +
            "  移动＝摇杆（半推走·全推跑）   跳＝跳键   蹲＝蹲键   闪＝翻滚（完整滚翻·无敌帧）\n" +
            "  拳＝轻连段·拳脚：前手直拳 → 交叉重拳 → 正踢 → 侧踹   （出手最快·削韧·积势快）\n" +
            "  剑＝重连段·巨剑：横斩 → 撩斩 → 突刺 → 旋风斩   （伤害高·击退大）\n" +
            "  挡＝格挡（按下瞬间为定心格挡，化解心理攻击）\n" +
            "  换角色/换武器＝右上「角色」面板（角色与武器分离，重选即替换手中武器）\n" +
            "  ※ 连点任意攻击键即无缝连段，拳剑可自由混接；出招自动咬住身边敌人\n" +
            "    （摇杆在多个敌人间选目标）；无敌人时只小步前移，原地连打不会一路平移\n" +
            "\n" +
            "◤ 派生动作（跳/蹲 + 攻击键） ◢\n" +
            "  跳+拳＝飞踢   跳+剑＝空袭跳劈   跳+重＝空袭·裂地跳劈   蹲+拳＝扫堂腿   蹲+剑＝低位突刺\n" +
            "  空中连段：一次滞空可打两段——第二段剑向＝空中回旋绝斩（滞空续航·横扫），\n" +
            "            拳向＝空中连环踢（压地收招）\n" +
            "  ※ 每招轨迹与范围各不相同（点/线/横弧/纵弧/环/面/空），详见「数据表」页\n" +
            "\n" +
            "◤ 组合招式（按顺序连点拳/剑，自动成招） ◢\n" +
            "  普通(无消耗)：拳拳拳＝连环拳脚·空翻踢 · 剑剑剑＝三连斩·大回旋\n" +
            "                拳拳剑＝拳影·裂地跳劈 · 剑剑拳＝双斩·惊鸿飞踢\n" +
            "  绝招(需2势·金光爆发)：拳拳剑剑＝龙卷·旋风绝斩 · 剑剑拳拳＝踏空·裂地跳劈\n" +
            "                        拳剑拳剑＝拳剑·惊鸿飞踢\n" +
            "  —— 招式越复杂伤害越高，绝招需积攒意势能量，不可无限使用\n" +
            "\n" +
            "◤ 重键＝蓄力气场（意势：命中/完美闪避/蓄力积攒，最多3点） ◢\n" +
            "  重（按住）＝蓄力气场：狂风护体外推敌人无法近身 + 防御姿态大减伤，\n" +
            "    持续消耗少量生命能量；松开＝巨剑跳劈【必中·无法格挡闪避】（越久越痛；\n" +
            "    2势＝旋风终结；满3势＝超必杀「觉醒·乱舞」）\n" +
            "  重（轻点，连段中）＝切手·撩斩    指令技（轻点重+方向，共享冷却）：\n" +
            "    前+重＝疾影突刺   后+重＝旋身空翻踢   左/右+重＝左/右旋风斩";

        const string FusionText =
            "◤ 自由融合：一切动作都是连招元素（放开限制） ◢\n" +
            "  元素：拳P · 剑K · 重H · 跃J · 闪D · 术S（技能/绝招）· 架G（招架成功）\n" +
            "  ① 融合增伤——不看你按了什么顺序，只看【用到几种不同元素】：\n" +
            "     2种×1.15  3种×1.35  4种×1.60  5种×1.90  6种以上×2.25（全能融合）\n" +
            "     单一元素连打（只会拳拳拳拳）没有加成——想打高伤就得真的把各种手段串起来\n" +
            "  ② 跨元素融招（跳/闪/术/架 与拳剑重互相接续，自动成招）：\n" +
            "     跃→剑＝踏空斩·凌云一式   跃→拳＝惊鸿飞踢·踏虚而至\n" +
            "     闪→剑＝闪身突刺·后发制人 闪→拳＝闪身重拳·借势反打\n" +
            "     术→剑＝术后追斩·势不可挡 术→拳＝术后贯拳·气随身走\n" +
            "     架→剑＝架后反斩         重→剑＝重斩接锋·崩势\n" +
            "     跃→重→剑＝踏空三叠·裂地崩斩(1势)  架→拳→剑＝架打连环·后发先至(1势)\n" +
            "     跃→术→剑＝踏云术斩·天倾一击(2势)  闪→术→拳＝影遁术拳·无相连环(2势)\n" +
            "  ③ 连段收招【不清空融合链】——跳跃、闪避、技能正是用来在两段连招之间搭桥的；\n" +
            "     链在 2.2 秒内有效，超时才断。屏幕左侧连段条实时显示当前链与融合倍率\n" +
            "  —— 没有预设过的串法一样成招：奖励的是临场把各种手段串起来，而不是背招表\n" +
            "\n" +
            "◤ 能量远攻 / 防守反击 ◢\n" +
            "  气＝能量斩·斩念气刃（需2势）    定＝定心护体    还＝责任归还（法院关卡）\n" +
            "  敌人出手瞬间翻滚＝完美闪避（时缓+意势+1+下一击必暴击）\n" +
            "  被击倒瞬间按闪＝受身快速起身（无敌帧）";

        /// <summary>
        /// 招式数据表：直接从 MoveTable 生成——面板与战斗判定共用同一份规格，
        /// 不会出现"说明书写 2 米、实际打 1.5 米"的对不上。
        /// 大型动作游戏对每一招的轨迹/范围/伤害都有明确数据，这里向玩家全部公开。
        /// </summary>
        const string SpecHeader =
            "  招式            轨迹        横宽×纵高×纵深(米)    伤害   削韧  击退\n" +
            "  ──────────────────────────────────────────────────────────────\n";

        static void AppendRow(System.Text.StringBuilder sb, Combat.MoveSpec m)
        {
            sb.Append("  ").Append(Pad(m.label, 14))
              .Append(Pad(m.TrajLabel, 11))
              .Append(Pad(m.width.ToString("0.0") + "×" + m.height.ToString("0.0") +
                          "×" + m.reach.ToString("0.0"), 21))
              .Append(Pad("×" + m.damageMult.ToString("0.00"), 7))
              .Append(Pad(m.postureMult.ToString("0"), 6))
              .Append(m.knockback.ToString("0.0")).Append('\n');
        }

        /// <summary>玩家招式规格页。</summary>
        static string BuildPlayerSpecText()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("◤ 玩家招式规格表（轨迹 · 范围 · 伤害）——与战斗判定同源 ◢\n");
            sb.Append("  轨迹：点=短促直击(最精准) 线=直线穿透(远而窄) 横弧=横扫一片(多目标)\n");
            sb.Append("        纵弧=纵向挑击(打高低差) 环=环身360°(被围清场) 面=罩住前方一片 空=凌空罩落点\n");
            sb.Append("  纵深＝判定框前后长度(实际触及≈中心+纵深/2)  伤害＝基础×系数  削韧＝破防进度\n\n");
            sb.Append(SpecHeader);
            foreach (var kv in Combat.MoveTable.All) AppendRow(sb, kv.Value);
            sb.Append("  ——— 派生变体（与基础招共用姿态，判定另有一套）———\n");
            foreach (var kv in Combat.MoveTable.AllVariants) AppendRow(sb, kv.Value);
            sb.Append("\n  ※ 连段第 7 击起伤害递减至 ×0.75，防止原地站桩无脑连打。\n");
            sb.Append("  ※ 部位伤害：头 ×1.35 伤害；腿 ×0.9 伤害但 ×1.4 削韧（打腿更易破防）。\n");
            sb.Append("  ※ 融合倍率在上表系数之上再乘（见「自由融合」页）；绝招/融招另有专属倍率。");
            return sb.ToString();
        }

        /// <summary>敌人招式规格页。</summary>
        static string BuildEnemySpecText()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("◤ 敌人招式规格表——与玩家同一套结构，各自独立调值 ◢\n");
            sb.Append("  同构是为了「算得清楚」：敌我判定都由同一份规格派生，可查可校对。\n");
            sb.Append("  独立调值是因为玩家的大招要付蓄力与意势的代价，敌人不付。\n\n");
            sb.Append(SpecHeader);
            foreach (var kv in Combat.EnemyMoveTable.All) AppendRow(sb, kv.Value);
            sb.Append("\n  ※ 敌人伤害＝该敌人基础伤害 × 上表系数；头顶亮「危」的危险攻击\n");
            sb.Append("     再 ×1.5 且【不可格挡】，只能闪避。\n\n");
            sb.Append("◤ 读招 → 该往哪躲（轨迹决定破解方式） ◢\n");
            sb.Append("  突刺/侧踹（线）：细长直线——【侧移】一步即可让开，正后退最吃亏\n");
            sb.Append("  横斩/旋踢（横弧）：横向铺开——【后撤】或抢在起手前贴身进内圈\n");
            sb.Append("  撩斩（纵弧）：纵向挑起——【后撤】，蹲伏无效（判定偏高但纵深短）\n");
            sb.Append("  回旋斩（环）：环身 360°——侧移无用，只能【拉开距离】或跳起\n");
            sb.Append("  重砸（面）：罩住前方一片——前摇最长，【提前翻滚】穿到侧后方\n");
            sb.Append("  飞踢（空）：带位移压过来——横向【翻滚】让开落点");
            return sb.ToString();
        }

        /// <summary>等宽对齐：中文按两格宽计（Unity 内置字体下近似对齐即可）。</summary>
        static string Pad(string s, int width)
        {
            int w = 0;
            foreach (char c in s) w += c > 0x2000 ? 2 : 1;
            return s + new string(' ', Mathf.Max(1, width - w));
        }

        public static MovesPanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<MovesPanel>();
            comp.Build(canvas);
            return comp;
        }

        Text _body, _title;
        int _page;   // 0=基础规则 1=自由融合 2=玩家数据表 3=敌人数据表

        static readonly string[] PageTitles =
        {
            "招 式 表 · 基础规则  (1/4)", "招 式 表 · 自由融合  (2/4)",
            "招 式 表 · 玩家数据  (3/4)", "招 式 表 · 敌人数据  (4/4)",
        };

        void SwitchPage()
        {
            _page = (_page + 1) % PageTitles.Length;
            ApplyPage();
        }

        void ApplyPage()
        {
            // 字号按各页行数选定，确保整页都在 780px 的正文区内显示完整（不被截断）
            switch (_page)
            {
                case 0: _body.text = MovesText; _body.fontSize = 21; break;
                case 1: _body.text = FusionText; _body.fontSize = 24; break;
                case 2: _body.text = BuildPlayerSpecText(); _body.fontSize = 21; break;
                default: _body.text = BuildEnemySpecText(); _body.fontSize = 21; break;
            }
            if (_title != null) _title.text = PageTitles[_page];
        }

        void Build(Transform canvas)
        {
            // 1040 高（参考分辨率 1080）：数据表页行数多，正文区必须放得下整页——
            // Unity Text 默认垂直溢出是截断，放不下会静默吃掉末尾几行。
            _panel = UiUtil.MakePanel(canvas, "MovesPanel", new Vector2(1420, 1040),
                new Color(0.07f, 0.07f, 0.11f, 0.97f));

            _title = UiUtil.MakeText(_panel.transform, "Title", PageTitles[0], 40,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(_title, new Vector2(0.5f, 1f), new Vector2(0, -44), new Vector2(800, 54));

            var body = UiUtil.MakeText(_panel.transform, "Body", MovesText, 25,
                TextAnchor.UpperLeft, new Color(0.9f, 0.94f, 0.95f));
            UiUtil.SetRect(body, new Vector2(0.5f, 0.5f), new Vector2(0, -12), new Vector2(1340, 860));
            body.lineSpacing = 1.12f;
            body.verticalOverflow = VerticalWrapMode.Overflow;   // 宁可略出框，也不静默截断
            body.horizontalOverflow = HorizontalWrapMode.Overflow;
            _body = body;

            UiUtil.MakeButton(_panel.transform, "下一页 ▶", new Vector2(0.5f, 0f), new Vector2(-190, 56),
                new Vector2(300, 74), new Color(0.24f, 0.34f, 0.46f, 0.95f), SwitchPage, 26);
            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f), new Vector2(190, 56),
                new Vector2(260, 74), new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 28);

            ApplyPage();
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
