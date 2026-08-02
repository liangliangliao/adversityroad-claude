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
            "◤ 基本键（六核心键 + 一修饰键，全部落在拇指可达区） ◢\n" +
            "  移动＝摇杆（半推走·全推跑）   跳＝跳键   蹲＝蹲键   闪＝翻滚（完整滚翻·无敌帧）\n" +
            "  拳＝轻连段·拳脚：前手直拳 → 交叉重拳 → 正踢 → 侧踹   （出手最快·削韧·积势快）\n" +
            "  剑＝重连段·巨剑：横斩 → 撩斩 → 突刺 → 旋风斩   （伤害高·击退大）\n" +
            "  挡＝格挡（按下瞬间为定心格挡，化解心理攻击）\n" +
            "  术＝技能修饰键（点一下亮起3秒）：亮起时六个核心键临时变成六个技能——\n" +
            "      术→拳＝定  术→剑＝气  术→重＝还  术→跳＝火  术→闪＝盾  术→挡＝收\n" +
            "      （亮起时按钮字面自动换成技能名；放完自动落回普通模式；再点「术」可取消）\n" +
            "      桌面端仍可直接用数字键 1–6 释放技能\n" +
            "  ※ 技能不再独占按钮位——手柄靠 L1+面键把 4 键变 16 招，触屏同理：\n" +
            "    六个按钮承载十二种功能，且全部在拇指够得着的地方\n" +
            "  镜头回正＝双击右半屏（桌面 V 键）：一次性把镜头拉到角色行进方向背后\n" +
            "      ——摇杆是镜头相对的，推在非正前方向时镜头【几何上】无法对着角色正前方，\n" +
            "      想立刻看清前方就双击一下（0.35 秒到位，期间角色不会被镜头带偏）\n" +
            "  换角色/换武器＝右上「角色」面板（角色与武器分离，重选即替换手中武器）\n" +
            "  ※ 连点任意攻击键即无缝连段，拳剑可自由混接；出招自动咬住身边敌人\n" +
            "    （摇杆在多个敌人间选目标）；无敌人时只小步前移，原地连打不会一路平移";

        const string DerivedText =
            "◤ 派生动作（跳/蹲 + 攻击键） ◢\n" +
            "  跳+拳＝飞踢   蹲+拳＝扫堂腿   蹲+剑＝低位突刺\n" +
            "  （跳+剑 / 跳+重 / 蹲+重 已升格为绝招，见「绝招·必杀」页）\n" +
            "  空中连段：一次滞空可打两段——第二段剑向＝空中回旋绝斩（滞空续航·横扫），\n" +
            "            拳向＝空中连环踢（压地收招）\n" +
            "  ※ 每招轨迹与范围各不相同（点/线/横弧/纵弧/环/面/空），详见「数据表」页\n" +
            "\n" +
            "◤ 连段（连点拳/剑） ◢\n" +
            "  拳＝直拳 → 交叉拳 → 正踢 → 侧踹     剑＝横斩 → 撩斩 → 突刺 → 旋风斩\n" +
            "  拳剑可以任意混接，节奏不断即为一条连段；第 7 击起伤害递减至 ×0.75\n" +
            "  ※ 连点【不再】凑绝招（旧版 拳拳剑剑 / 剑剑拳拳 这类序列已全部取消）——\n" +
            "    那套规则的问题是两头不讨好：乱按也能中，想放又记不住、还必须卡在连段窗口内。\n" +
            "    现在连段只负责「打得顺」，绝招一律走「方向/蹲/跳 + 键」，两条线互不干扰。\n" +
            "\n" +
            "◤ 重键＝蓄力气场（意势：命中/完美闪避/蓄力积攒，最多3点） ◢\n" +
            "  重（按住）＝蓄力气场：狂风护体外推敌人无法近身 + 防御姿态大减伤，\n" +
            "    持续消耗少量生命能量；松开＝巨剑跳劈→旋风斩二连【必中·无法格挡闪避】\n" +
            "    （越久越痛，合计约 ×6；2势＝旋风终结 ×8；满3势＝必杀「觉醒·乱舞」）\n" +
            "  重（轻点，连段中）＝切手·撩斩\n" +
            "  指令技（站立时轻点重+方向，免费即时，约 ×3，与绝招共享冷却）：\n" +
            "    前+重＝疾影突刺   后+重＝旋身空翻踢   左/右+重＝左/右旋风斩\n" +
            "  ※ 蹲+重 与 跳+重 不是指令技，是绝招（镇岳·裂地踏 / 天坠·陨星踏）";

        /// <summary>
        /// 绝招页：直接从 PlayerCombatController 的绝招表生成，
        /// 说明书与战斗判定同源——不会出现「面板写 ×8、实际打 ×5」。
        /// 玩家提出的「招很多，不知道哪个伤害最大、哪个什么时候用」，
        /// 靠的就是这一页：七招按伤害从高到低排，每招标明触发、意势、倍率、职责。
        /// </summary>
        static string BuildSpecialText()
        {
            var pc = Object.FindObjectOfType<Combat.PlayerCombatController>();
            float bas = pc != null ? pc.baseDamage : 20f;
            float hvy = pc != null ? pc.heavyDamage : 42f;

            var sb = new System.Text.StringBuilder();
            sb.Append("◤ 绝招（7 招）· 必杀（1 招）——一条语法全部覆盖 ◢\n");
            sb.Append("  【方向 / 蹲 / 跳  +  剑或重】，不需要背按键序列，也没有连点凑招。\n");
            sb.Append("  ★ 方向要「推一下」而不是「一直按着」：摇杆回中（或换个方向）→ 朝目标推出去\n");
            sb.Append("    → 0.4 秒内按剑。所以追着敌人跑着砍仍然是普通连段，不会误触绝招。\n");
            sb.Append("  意势 0–3 点：命中、完美闪避、蓄力都能积；绝招消耗，攒满 3 点开必杀。\n\n");
            sb.Append("  触发       名称              意势  倍率   伤害   职责（各不重叠）\n");
            sb.Append("  ──────────────────────────────────────────────────────────────\n");
            foreach (var sp in Combat.PlayerCombatController.SpecialTable())
            {
                sb.Append("  ").Append(Pad(sp.input, 11))
                  .Append(Pad(sp.name, 18))
                  .Append(Pad(sp.cost > 0 ? sp.cost + " 势" : "免费", 6))
                  .Append(Pad("×" + sp.mult.ToString("0.#"), 7))
                  .Append(Pad((bas * sp.mult).ToString("0"), 7))
                  .Append(sp.role).Append('\n');
            }
            sb.Append("  ──────────────────────────────────────────────────────────────\n");
            sb.Append("  ").Append(Pad("满3势·长按重松开", 11)).Append(Pad("觉醒·乱舞（必杀）", 18))
              .Append(Pad("3 势", 6)).Append(Pad("×16–21", 7))
              .Append(Pad((hvy * 1.65f * 6.1f).ToString("0"), 7))
              .Append("全表最高·四段连斩，期间无敌\n\n");
            sb.Append("◤ 怎么选 ◢\n");
            sb.Append("  · 敌人破防倒地（出现破绽）→ 前+剑「裂地跳劈」，这是单体伤害天花板\n");
            sb.Append("  · 被三个以上敌人围住      → 左/右+剑「旋风绝斩」，360° 一次全打到\n");
            sb.Append("  · 敌人举盾/一直架住       → 蹲+剑「扫堂连环」，削韧最高，两下就破\n");
            sb.Append("  · 一群小怪站成一片        → 蹲+重「裂地踏」，范围最大且同时打断\n");
            sb.Append("  · 敌人冲过来 / 想拉开距离 → 后+剑「惊鸿飞踢」，免费，击退最大\n");
            sb.Append("  · 跳起来之后             → 跳+重「陨星踏」砸落地，或 跳+剑「凌空斩」追浮空\n");
            sb.Append("  · 攒满 3 势              → 别留着，必杀期间无敌，可以当「保命 + 清场」用\n\n");
            sb.Append("◤ 规则说明 ◢\n");
            sb.Append("  · 绝招之间共享冷却：消耗意势的 1.6 秒，免费的 0.8 秒——放得出，但不能刷\n");
            sb.Append("  · 意势不够会明确弹出「放不出来」的提示，不会静默变成一次普通攻击\n");
            sb.Append("  · 每招判定窗一走完立刻开放收招取消，接下一手不必等动作播完\n");
            sb.Append("  · 不推方向按剑＝普通连段，绝招不会误触；连段与绝招是两条互不干扰的线\n");
            sb.Append("  · 突刺已从绝招表移除（起手与收招都偏长，放在绝招位必然读作慢半拍），\n");
            sb.Append("    它回到轻连段第三下与「前+重」指令技上");
            return sb.ToString();
        }

        const string FusionText =
            "◤ 自由融合：一切动作都是连招元素（放开限制） ◢\n" +
            "  元素：拳P · 剑K · 重H · 跃J · 闪D · 术S（技能/绝招）· 架G（招架成功）\n" +
            "  ① 融合增伤——不看你按了什么顺序，只看【用到几种不同元素】：\n" +
            "     2种×1.15  3种×1.35  4种×1.60  5种×1.90  6种以上×2.25（全能融合）\n" +
            "     单一元素连打（只会拳拳拳拳）没有加成——想打高伤就得真的把各种手段串起来\n" +
            "  ② 跨元素融招＝【纯伤害加成】，不改招式、不改帧数：\n" +
            "     跃→剑 · 跃→拳 · 闪→剑 · 闪→拳 · 术→剑 · 术→拳 · 架→剑 …\n" +
            "     串到就报一次名并额外增伤，但打出来的仍然是你按的那一下。\n" +
            "     ※ 旧版融招会把你按的招【换成另一个动作】（比如 重→剑 换成崩势），\n" +
            "       动作长、起手慢、还会打断你正在走的节奏——已全部取消。\n" +
            "       融合的价值是奖励你换着手段串，不该是藏在连段里的第二套招式表。\n" +
            "  ③ 连段收招【不清空融合链】——跳跃、闪避、技能正是用来在两段连招之间搭桥的；\n" +
            "     链在 2.2 秒内有效，超时才断。屏幕左侧连段条实时显示当前链与融合倍率\n" +
            "\n" +
            "◤ 能量远攻 / 防守反击 ◢\n" +
            "  术→剑＝能量斩·斩念气刃（需2势）  术→拳＝定心护体  术→重＝责任归还（法院关卡）\n" +
            "  敌人出手瞬间翻滚＝完美闪避（时缓+意势+1+下一击必暴击，并强制打断攻击者）\n" +
            "  敌人出手瞬间格挡＝完美招架（零伤害+强制打断攻击者）\n" +
            "  被击倒瞬间按闪＝受身快速起身（无敌帧）";

        /// <summary>
        /// 读招与闪避页：玩家明确提出"为什么会毫无征兆受到伤害？如何提前预测并闪避？
        /// 能不能设计某些规则？"——规则写在这一页，并且是**系统层面保证的硬承诺**，
        /// 不是玩法建议：EnemyController.MinWindup / ComboWindup 就是下面这些数字。
        /// </summary>
        const string ReadAttackText =
            "◤ 敌人出手的固定规则（可以背下来的四条） ◢\n" +
            "  ① 任何一次会造成伤害的攻击，出手前【必定】先亮警示——没有例外，\n" +
            "     连招的第二、三下也一样（这一条以前是漏的，所以会挨莫名其妙的连击）。\n" +
            "  ② 警示分两种，含义固定：\n" +
            "       头顶「！」+ 脚下红圈  → 普通攻击，可格挡、可闪避\n" +
            "       头顶「危」+ 更大的橙圈 → 危险攻击，【不可格挡】，只能闪避\n" +
            "  ③ 警示到出手的时间是有下限的：起手 ≥0.50 秒（危险攻击 ≥0.65 秒），\n" +
            "     连招的追加段 ≥0.34 秒。看见就动，时间一定够。\n" +
            "  ④ 看不见的敌人不会偷袭你：画面外或背后的敌人一进入前摇，\n" +
            "     屏幕边缘就会在那个方向出现同色箭头（红＝可挡，橙＝只能闪）。\n" +
            "     真的挨了打，来袭方向也会亮一下——至少知道该往哪转身。\n" +
            "\n" +
            "◤ 往哪躲：轨迹决定破解方式（与「敌人数据」页同源） ◢\n" +
            "  突刺/侧踹（线）：细长直线 → 【侧移】一步就让开，正后退最吃亏\n" +
            "  横斩/旋踢（横弧）：横向铺开 → 【后撤】，或抢在起手前贴身进内圈\n" +
            "  撩斩（纵弧）：纵向挑起 → 【后撤】，蹲伏无效\n" +
            "  回旋斩（环）：环身 360° → 侧移无用，只能【拉开距离】或跳起\n" +
            "  重砸（面）：罩住前方一片 → 前摇最长，【提前翻滚】穿到侧后方\n" +
            "  飞踢（空）：带位移压过来 → 横向【翻滚】让开落点\n" +
            "\n" +
            "◤ 三种防御分别管什么（分工明确，不要指望一招通吃） ◢\n" +
            "  闪（翻滚）：唯一能躲开【所有】攻击的手段，包括「危」。\n" +
            "     全程 0.30~0.42 秒，前 72% 无敌——滚动主体打不到你，只有收势尾巴能被打中。\n" +
            "     收势可被【攻击 / 跳 / 再次翻滚】立刻打断，闪完马上还手，不用干等。\n" +
            "  挡（格挡）：管【正面 + 两侧】的普通攻击（「！」），减伤 80%。\n" +
            "     对「危」无效（会破防），对真背后无效（身后约 106° 才算被绕后）。\n" +
            "     体力不够只能挡下 45%，会有明确提示——不会静默失效。\n" +
            "  跳：管【贴地招】——正踢/侧踹/扫堂这类判定框压得很低的招，跳起来就让开了。\n" +
            "     对重砸（罩住一片，判定高 3 米）和回旋斩没用，那两个要闪。\n" +
            "\n" +
            "◤ 两条时机技（学会它们，难度会明显下降） ◢\n" +
            "  · 敌人判定框开启的瞬间翻滚＝完美闪避：时缓 + 意势+1 + 下一击必暴击，\n" +
            "    并且【强制打断攻击者】。\n" +
            "  · 按下「挡」后 0.2 秒内接住＝精准格挡：零伤害 + 强制打断对方 + 意势+1。\n" +
            "    【不消耗体力】——它是纯时机技，体力见底照样成立。\n" +
            "  两条都是**固定规则、每次都成立**，不是概率。\n" +
            "\n" +
            "◤ 抢攻的规则（读招的正收益） ◢\n" +
            "  · 在敌人前摇里打中它＝【打断】它的出招，这就是读招的奖励。\n" +
            "  · 精英/首领有霸体：轻击打不断（会显示「霸体·未打断」），要用重击或绝招。\n" +
            "    打不断时它的招仍然带着完整前摇打出来，所以照样闪得掉、挡得住。\n" +
            "  · 抢攻【不会】反弹伤害给你。挨打只可能来自敌人真正打到你的判定框。\n" +
            "\n" +
            "◤ 玩家侧的保护（写死在系统里，不是运气） ◢\n" +
            "  · 单次伤害上限＝最大生命的 25%：任何一击都不可能秒掉你，包括 Boss。\n" +
            "  · 受击后有 0.45 秒无敌（挡住时 0.2 秒）：被围住时也总有一次做出反应的机会，\n" +
            "    不会被两三个敌人接力锁死到死。\n" +
            "  · 同一时刻只有一个敌人拿到攻击令牌，其余的会等——围攻不等于群殴。\n" +
            "  · 生命低于 30% 才会弹休整答题（其余情况一律不弹，不打断战斗）。";

        const string ArbitrationText =
            "◤ 连按不同键会怎样（输入仲裁规则） ◢\n" +
            "  · 先按的先出，后按的排队——两个都会发动，绝不会互相覆盖或凭空消失\n" +
            "  · 动作进行中按下的键【不会过期】，一直等到那个动作能被接续为止\n" +
            "  · 长动作（技能/蓄力二连/超必杀）打完主要判定后进入【收招取消窗】：\n" +
            "    此时攻击、技能、闪避都能立刻打断收招接上，不必等动作播完\n" +
            "    ——但终结一击的判定一定会打完，连打不会把自己的终结段取消掉\n" +
            "  · 站着不动时按下、却因为冷却/资源不够没打出来的键，0.6 秒后作废\n" +
            "    （避免松手几秒后角色自己突然动起来）\n" +
            "\n" +
            "  ◇ 实例：点「重」，0.1 秒后立刻点「拳」\n" +
            "     重＝蓄力二连（跳劈→旋风斩），整套锁定 0.88 秒；\n" +
            "     拳在按下时被标记为【排队】，不会过期；\n" +
            "     二连的第二击判定走完（0.78 秒）即开放收招取消，拳当场接上。\n" +
            "     → 两招都完整打出，衔接点在 0.78 秒，而不是干等 0.88 秒、更不会丢。\n" +
            "\n" +
            "  ◇ 想只出后按的那一招？松开重键前别按，或用闪避取消掉前一招再出手。";

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
            sb.Append("  ※ 融合倍率在上表系数之上再乘（见「自由融合」页）；绝招倍率见「绝招与必杀」页。");
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
        int _page;

        static readonly string[] PageTitles =
        {
            "招 式 表 · 基本键", "招 式 表 · 绝招与必杀", "招 式 表 · 派生与连段",
            "招 式 表 · 读招与闪避", "招 式 表 · 自由融合", "招 式 表 · 输入仲裁",
            "招 式 表 · 玩家数据", "招 式 表 · 敌人数据",
        };

        static string PageText(int page)
        {
            switch (page)
            {
                case 0: return MovesText;
                case 1: return BuildSpecialText();
                case 2: return DerivedText;
                case 3: return ReadAttackText;
                case 4: return FusionText;
                case 5: return ArbitrationText;
                case 6: return BuildPlayerSpecText();
                default: return BuildEnemySpecText();
            }
        }

        void SwitchPage()
        {
            _page = (_page + 1) % PageTitles.Length;
            ApplyPage();
        }

        // 正文区高度与行高换算：Unity 内置字体行高 ≈ fontSize × 1.28，再乘 lineSpacing 1.12。
        const float BodyHeight = 860f;
        const float LineHeightFactor = 1.12f * 1.28f;

        /// <summary>
        /// 字号按实际行数自动定，而不是每页手写一个魔数——
        /// Unity Text 的垂直溢出默认是【截断】，页面一旦写长就静默吃掉末尾几行，
        /// 而这种缺失在编辑器里不报错、只在真机上看不见。自动定档后，
        /// 以后往任何一页加内容都不会再溢出（代价只是字略小）。
        /// </summary>
        void ApplyPage()
        {
            string txt = PageText(_page);
            int lines = 1;
            foreach (char c in txt) if (c == '\n') lines++;
            _body.text = txt;
            _body.fontSize = Mathf.Clamp(
                Mathf.FloorToInt(BodyHeight / (lines * LineHeightFactor)), 15, 25);
            if (_title != null)
                _title.text = PageTitles[_page] + "  (" + (_page + 1) + "/" + PageTitles.Length + ")";
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
