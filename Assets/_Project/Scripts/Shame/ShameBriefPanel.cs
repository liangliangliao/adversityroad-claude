using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;

namespace AdversityRoad.Shame
{
    /// <summary>
    /// 本关规则卡（方案 8.6「必须在关卡设计、UI 提示与复盘文案中保持一致」）。
    ///
    /// 【为什么必须有这一块】
    /// 上一版进关只有一行字幕。字幕几秒就滚走，玩家的原话是
    /// "不知道这两个关卡是什么、不知道怎么通关、游戏规则是什么"——
    /// 那不是玩家没看，是这一关**从来没有把规则完整说出来过**。
    ///
    /// 【它和 8.5 那条禁令不冲突】
    /// 方案 8.5 禁的是"用箭头、任务提示或强制引导把玩家**推进广播室那扇门**"，
    /// 那是本章的收束动作，必须由玩家自己决定何时进入。
    /// 但方案同时反复要求可读：8.2 写"禁止把心理机制做成纯数值暗箱，计时器必须在
    /// HUD 上可读"，8.6.1 写"把被看见做成可读的空间"，8.6 写通关条件
    /// "必须在关卡设计、UI 提示与复盘文案中保持一致，不得暗示存在某种彻底消音的隐藏解法"。
    /// 说明规则是方案的**要求**，不是方案的禁令。
    /// 所以这张卡只陈述规则与事实，不给方向指引、不给门的坐标、不催促。
    ///
    /// 【为什么要专门写"这些不是通关条件"】
    /// 这两关最容易被玩成错的那一种：把注视清空、把低语打断干净、把 Boss 打死。
    /// 三条都做得到，三条都不通关。不写出来，玩家只会以为自己没打够。
    /// </summary>
    public class ShameBriefPanel : MonoBehaviour
    {
        static ShameBriefPanel _open;
        const string SeenKeyPrefix = "shame_brief_seen_";

        GameObject _panel;

        public static bool AnyOpen => _open != null;

        /// <summary>进关时自动弹一次（同一关看过就不再自动弹，但随时可以手动重开）。</summary>
        public static void ShowOnEnter(string levelId)
        {
            if (PlayerPrefs.GetInt(SeenKeyPrefix + levelId, 0) == 1) return;
            PlayerPrefs.SetInt(SeenKeyPrefix + levelId, 1);
            PlayerPrefs.Save();
            Show(levelId);
        }

        /// <summary>手动打开（HUD 上的「本关规则」按钮）。</summary>
        public static void Show(string levelId)
        {
            if (_open != null) { _open.Close(); return; }
            var canvas = UiUtil.MainCanvas();
            if (canvas == null) return;
            var go = new GameObject("ShameBriefPanel");
            _open = go.AddComponent<ShameBriefPanel>();
            _open.Build(canvas.transform, levelId);
        }

        void Build(Transform canvas, string levelId)
        {
            bool corridor = levelId == ShameLine.LevelDebtCorridor;
            // 画布参考分辨率是 1920×1080，正文最长的一关有 30 行——
            // 面板必须按正文的实际行数给高度，否则 Text 会静默截断，
            // 玩家看到的又是一段"说到一半"的规则。
            const float W = 1160f, H = 880f;

            _panel = UiUtil.MakePanel(canvas, "ShameBrief", new Vector2(W, H),
                new Color(0.06f, 0.06f, 0.08f, 0.96f));
            UiUtil.SetRect(_panel.GetComponent<Image>(), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(W, H));

            var title = UiUtil.MakeText(_panel.transform, "Title",
                corridor ? "8-1　欠条长廊 / 未播出的广播室"
                         : "8-2　二十元回声教室", 26,
                TextAnchor.MiddleCenter, new Color(0.94f, 0.86f, 0.6f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -28), new Vector2(W - 60, 32));

            var theme = UiUtil.MakeText(_panel.transform, "Theme",
                corridor ? "隐瞒复利 × 待审悬置" : "标签固化 × 目光与低语 × 指控成立时的防御", 18,
                TextAnchor.MiddleCenter, new Color(0.72f, 0.72f, 0.8f));
            UiUtil.SetRect(theme, new Vector2(0.5f, 1f), new Vector2(0, -60), new Vector2(W - 60, 24));

            // 章节核心命题（方案 8.1「通关定义」原文）。
            // 少了这一句，"敌人打不死""低语清不干净"读起来只像做得烂；
            // 有了它，玩家才知道那不是缺陷，是这一章要说的那件事。
            var thesis = UiUtil.MakeText(_panel.transform, "Thesis",
                "第八章·通关定义：不是洗清指控，也不是打败注视，而是收回裁判权——" +
                "事实可以成立，判词不能终审。", 17,
                TextAnchor.MiddleCenter, new Color(0.86f, 0.78f, 0.6f));
            UiUtil.SetRect(thesis, new Vector2(0.5f, 1f), new Vector2(0, -88), new Vector2(W - 70, 22));

            // 正文用一整块左对齐文本：行与行之间的层级靠符号区分，不再拆成十几个控件
            var body = UiUtil.MakeText(_panel.transform, "Body",
                corridor ? CorridorBody() : ClassroomBody(), 18,
                TextAnchor.UpperLeft, new Color(0.9f, 0.9f, 0.94f));
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Overflow;   // 宁可溢出，也不许悄悄截断
            UiUtil.SetRect(body, new Vector2(0.5f, 1f), new Vector2(0, -444),
                new Vector2(W - 80, 640));

            var tip = UiUtil.MakeText(_panel.transform, "Tip",
                "（关掉之后，随时可以按 HUD 左侧的「本关规则」重新打开）", 16,
                TextAnchor.MiddleCenter, new Color(0.62f, 0.62f, 0.7f));
            UiUtil.SetRect(tip, new Vector2(0.5f, 0f), new Vector2(0, 96), new Vector2(W - 80, 22));

            UiUtil.MakeButton(_panel.transform, "知道了", new Vector2(0.5f, 0f),
                new Vector2(0, 44), new Vector2(220, 56),
                new Color(0.28f, 0.31f, 0.38f, 0.95f), Close, 21);
        }

        /// <summary>
        /// 8-1 的规则。
        /// 注意措辞：广播室那扇门只作为**事实**陈述（它一直开着，何时进由你定），
        /// 不写成任务、不给方向、不催。这是 8.5 那条禁令的边界。
        /// </summary>
        static string CorridorBody() =>
            "▍你在哪\n" +
            "    住宅区的小商店 → 一条走廊 → 走廊尽头的广播室。欠了一笔钱，正在分期还。\n\n" +
            "▍这一关考的是什么\n" +
            "    偿还需要钱；说明真相需要暴露；隐瞒可以解决当下——代价是长廊真的会变长。\n" +
            "    每用一次抽屉（隐瞒），走廊多长出一段，并多出一个「新的把柄」盯着你。\n\n" +
            "▍规则\n" +
            "    · 欠条台：选本期金额。高额推进快但耗行动力，低额安全但要多被问几次。\n" +
            "    · 每还一期，触发一次「每周追问」。追问全程不发生正面战斗——追问本身就是攻击。\n" +
            "    · 每周门要逐一走过，不能跳过。\n" +
            "    · 悬案计时器（HUD 左侧）替代 Boss 血条。它不因你受伤而变化，\n" +
            "      只随追问次数与讨好度缩短。\n" +
            "    · 广播室的门从第一秒起就是开着的，没有锁，也没有守门人。\n" +
            "      走进去做一次「自行陈述」——时机、对象、措辞三项全部由你自己选。\n" +
            "      什么时候进去，是你的决定，这一关不会催你。\n\n" +
            "▍这些不是通关条件\n" +
            "    把欠款还清不通关（钱从来不是这一关的门槛）。把追问者打死不通关。\n\n" +
            "▍结算\n" +
            "    在案子还有余地的时候自己走进去说明，与被拖到最后才说明，是两种结算。\n" +
            "    差别在于「是你选的时机」还是「时机替你选了」——这一关不会告诉你还剩多久算早，\n" +
            "    那个判断本来就该是你的。\n\n" +
            "▍失败会怎样\n" +
            "    计时器耗尽：长廊闭环回到起点，欠条、长廊长度、技能与情报全部保留。\n" +
            "    自尊归零：进入羞耻状态，回到最近的恢复点，关卡进度不回退。\n" +
            "    本关的失败不会有任何围观、嘲笑或当众羞辱的演出。\n\n" +
            "▍随时可退出\n" +
            "    任何公开场景都可以中途「先出去」，也可以用一次文字复盘替代，不阻断主线。";

        /// <summary>
        /// 8-2 的规则。
        /// 方案 8.6 明写：不得暗示存在"彻底消音"的隐藏解法——所以"这些不是通关条件"
        /// 这一栏在这一关是硬要求，不是补充说明。
        /// </summary>
        static string ClassroomBody() =>
            "▍你在哪\n" +
            "    夜间的自习教室，灯只开了一半。一件二十元的东西不见了，指认落到你头上。\n\n" +
            "▍这一关考的是什么\n" +
            "    指控是成立的。你不能靠否认通关，也不能靠让所有人闭嘴通关。\n" +
            "    目标不是「不被看见」，是「被看见的同时仍然能把事做完」。\n\n" +
            "▍通关条件（就这一条）\n" +
            "    在低语链**保持活跃**的状态下，完成三个目标动作，然后正常**步行**走出教室：\n" +
            "      1. 归还——教室前排，主视线锥正中。长按完成，中途松手算失败。\n" +
            "      2. 完成本职——自习座位，交叉视线区。长按期间会一直挨话。\n" +
            "      3. 步行离场——从教室门走出去。全程不得冲刺、不得闪避。\n" +
            "    三个交互物**全部在视线锥内，绕不开**。这是主题设计，不是难度设计。\n\n" +
            "▍规则\n" +
            "    · 视线锥（地上那片扇形）随 NPC 头部实时转动，锥内暴露度快速上升。\n" +
            "    · 暴露度越高，自尊伤害倍率越高（60 起 ×1.5，85 起 ×2.0）。\n" +
            "    · 低语链有三节。打断任意一节，8 秒后会从别处重建。\n" +
            "    · 打倒持锥的人，45 秒后会有别人从别处补上位置。\n" +
            "    · 被指认时用「认领不终审」认下事实——认的是事实，不是身份。\n" +
            "      对虚假指控，正解是「事实之刃」，不是认领。\n\n" +
            "▍这些不是通关条件\n" +
            "    让低语停止——**不存在**这个胜利条件，也没有隐藏的彻底消音解法。\n" +
            "    把围观的人全部打倒——他们会补位。把 Boss 打死——打赢不会让关卡结束。\n" +
            "    奔跑离场——判定为回避，本关记为普通结算。\n\n" +
            "▍随时可退出\n" +
            "    任何公开场景都可以中途「先出去」，也可以用一次文字复盘替代，不阻断主线。";

        void Close()
        {
            if (_panel != null) Destroy(_panel);
            _open = null;
            Destroy(gameObject);
        }
    }
}
