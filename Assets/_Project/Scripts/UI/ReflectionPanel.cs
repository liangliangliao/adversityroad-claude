using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 战后复盘（安全屋核心）：把这一战整理成四栏——事实 / 感受 / 边界 / 行动。
    /// 四栏可编辑（默认按当前章节主题生成，抽象化不复刻真实人物/事件；也可手动改写成自己的话）。
    /// 「归档此战」：清空反刍、回补心理属性、写入旧事档案、并获得 1 复盘点（技能树货币）。
    /// 用复盘代替反刍，把刺痛转成经验。
    /// </summary>
    public class ReflectionPanel : MonoBehaviour
    {
        GameObject _panel;
        InputField _factInput, _feelInput, _boundInput, _actInput;
        Text _ruminationText;

        // 四栏默认文案：事实 / 感受 / 边界 / 行动，按子章顺序一一对应（大章-子章结构）+ 自由模式兜底。
        static readonly string[][] ChapterReflections =
        {
            new[] {   // 序章 其一 · 独居小屋
                "桌上的计划落灰，你在「我行不行」里反复打转，一直没有开始。",
                "不是懒——是怕自己不够好，于是抢先否定了自己。",
                "怀疑可以存在，但它没有资格替我做决定。",
                "现实里：为一个目标写下最小的第一步，今天就做五分钟。" },
            new[] {   // 序章 其二 · 训练武馆
                "你一次次把「开始」推给明天，明天却从不到来。",
                "总想等状态好了、准备足了再动，可那一刻不会自己来。",
                "不等动力，先开始；动力是被行动召回的，不是等来的。",
                "现实里：挑一件拖了很久的小事，只做五分钟，不求做完。" },
            new[] {   // 公平线 其一 · 两元赌桌
                "对方赖掉了两块钱，还笑你计较；旁观的人跟着起哄。",
                "刺痛你的不是金额，是承诺被当成笑话、你被当成小气鬼。",
                "核心不是金额，而是承诺；但追究也要看成本。",
                "现实里：下次先把事实说清楚（谁、何时、约定了什么），再决定追不追。" },
            new[] {   // 公平线 其二 · 债务车影
                "一笔没结清的账挂了很久，对方一直说「过段时间再说」。",
                "每次想起它都刺一下——未结清的故事在偷偷收利息。",
                "我可以坚持事实，但不让一个未结清的故事占据生命中心。",
                "现实里：给这件事定一个追讨的期限与方式，然后把它从脑子里放下。" },
            new[] {   // 公平线 其三 · 小题大做审判庭
                "你面对了否定、标签和「太敏感」「小题大做」的审判。",
                "被说「小题大做」时，你差点跟着否定自己的感受。",
                "我可以感受强烈，但这不等于没有理由——事实先于评价。",
                "现实里：下一次先说清事实，再决定是否继续解释。" },
            new[] {   // 刺激线 其一 · 一声咳嗽的街道
                "一声咳嗽、一个眼神，就把你的注意力整个拽走了。",
                "你把每个刺激都解释成「针对我」，越想越乱、越乱越耗。",
                "我听见了，但我不跟随；不必回应每一个声音。",
                "现实里：练习一次「听见但不跟随」，把注意力放回手上的事。" },
            new[] {   // 刺激线 其二 · 眼神审判走廊
                "一路走来全是注视：真实的、想象的、镜子里的。",
                "被看的感觉让你缩起来，好像每道目光都在打分。",
                "被看见不等于被否定——我不需要向每道目光交代。",
                "现实里：今天在人多的场合抬头走路一次，不检查任何人的表情。" },
            new[] {   // 刺激线 其三 · 陌生挑衅路口
                "有人故意挑衅，等你上钩；你差点追进车流里。",
                "那股「必须赢回来」的火，其实是被它牵着走。",
                "不是所有挑衅都值得回应——不接招，它就落空。",
                "现实里：下次被激时先数五个呼吸，再决定要不要回应。" },
            new[] {   // 刺激线 其四 · 噪声放大（终战）
                "熟悉的街道被放大成针对你的证据：咳嗽、转头、低语。",
                "你想追上每一个刺激，证明它是不是冲你来的。",
                "无法确认的事情，不直接当事实——注意力先拿回来。",
                "现实里：练习一次「听见但不跟随」，五分钟不查证任何猜测。" },
            new[] {   // 拖延线 其一 · 目标遗忘房
                "你在杂乱的房间里绕了很久，差点忘了进来是要做什么的。",
                "干扰太多，目标太旧——好像连「想要什么」都记不清了。",
                "目标不需要宏大，需要被擦亮：一天只钉一个。",
                "现实里：打开目标板，写下今天只做的一件事。" },
            new[] {   // 拖延线 其二 · 拖延沼泽
                "泥壳、深泥、手机光点和「再准备一下」把你困在原地。",
                "你等状态好、等准备足——等待本身成了最深的泥。",
                "不让准备代替行动；火种不大，五分钟就够点燃。",
                "现实里：现在就做五分钟——做完再决定要不要继续。" },
            new[] {   // 拖延线 其二 · 求职沉默荒原
                "投出去的简历石沉大海，你开始怀疑自己的价值。",
                "没有回音，被你读成了「我不够好」。",
                "没有回应不代表没有价值，它只说明我要继续调整、继续投。",
                "现实里：今天再投递或改进一次，把「下一次」握在自己手里。" },
            new[] {   // 拖延线 其三 · 城市广场（终战）
                "无数个「明天再说」，堆成了一个有你轮廓的巨大影子。",
                "你害怕成为过去的自己，却又被它拖住脚步。",
                "影子不是身份——行动一开始，它就开始碎。",
                "现实里：把今天的「一件事」写在目标板上，现在就动第一步。" },
            new[] {   // 边界线 其一 · 老实人消耗局
                "请求从四面八方涌来，每一个都说「就这一次」。",
                "拒绝的话到了嘴边，又被「你人最好了」堵了回去。",
                "帮助有边界，不是无限资源——好人卡不是卖身契。",
                "现实里：练习一次明确拒绝，不解释超过一句。" },
            new[] {   // 边界线 其二 · 责任转嫁法院
                "法院里，一个个「责任」被推到你面前，你差点全都接了下来。",
                "内疚让你习惯默认承担——好像不背，就是你不够好。",
                "我只承担属于我的那部分：多一分是内耗，少一分是逃避。",
                "现实里：下一次先分清「这是谁的责任」，再决定要不要帮。" },
            new[] {   // 边界线 其三 · 无限代付走廊
                "一条走不完的走廊：时间、金钱、精力、情绪，扇扇门都在收费。",
                "每次默认代付，走廊就变长一截——你在替别人走人生。",
                "我可以帮，但由我决定帮什么、帮多少——明确拒绝走廊就变短。",
                "现实里：找出一件长期默认代付的事，这周把费用还给它的主人。" },
            new[] {   // 低谷线 其一 · 饥饿荒巷
                "深夜的巷子里，你先找到了水和食物，然后才谈别的。",
                "饥饿和窘迫让人觉得低人一等——好像落魄是种罪。",
                "低谷不是身份，只是阶段；先照顾好基本生存，不丢人。",
                "现实里：把这周的吃饭睡觉排进日程，像照顾朋友那样照顾自己。" },
            new[] {   // 低谷线 其二 · 车库寒夜
                "寒夜里硬扛只会更冷——你学会了从一堆火走向下一堆火。",
                "「必须自己扛住」的念头，比冷风更早耗尽你的意志。",
                "坚持需要计划：知道去哪里取暖，才是真正的撑住。",
                "现实里：为最难的那件事列一个'补给点清单'（人/地方/方法）。" },
            new[] {   // 低谷线 其三 · 病房回廊（终战）
                "白色走廊里，账单和无力感压成了一座巨像。",
                "你差点相信「求助=没用」，想一个人把一切扛完。",
                "承认需要帮助，不等于失去尊严——开口正是巨像最怕的事。",
                "现实里：向一个可信的人说一句「这件事我需要帮忙」。" },
            new[] {   // 哲学线 其一 · 哲学虚无图书馆
                "书架间的雾里，你点亮了三座行动灯台，护体应声而碎。",
                "你差点用「还没读懂、还没想通」当作「还不能开始」的借口。",
                "想明白不是行动的前提——有些问题，做一步才答得上来。",
                "现实里：挑一件你一直在'研究'却没动手的事，今天先做五分钟。" },
            new[] {   // 哲学线 其二 · 无限追问大厅
                "满墙的问题之门是诱饵，你只穿过了发亮的行动之门。",
                "「为什么偏偏是我 / 如果又失败了怎么办」把你越钻越深。",
                "问题不必全部回答——有些问题可以带着走，边走边答。",
                "现实里：把一个反复纠结的问题写下来，先做它对应的下一个小动作。" },
            new[] {   // 哲学线 其三 · 意志断桥（终战）
                "断桥上你摔下去又爬起来，最后用「行动答台」让追问者语塞。",
                "无限追问差点把你钉在桥头，一步也不敢迈。",
                "用行动回答，而不是用答案行动——先迈出去，桥会在脚下续上。",
                "现实里：遇到「想清楚再做」的念头时，把顺序反过来一次。" },
            new[] {   // 旧我线 其一 · 失败展览馆
                "展览馆里，你的失败被裱起来打上射灯，中央的展台却是空的。",
                "旧审判官逼你在每件失败前停留，仿佛那就是你的名字。",
                "失败是发生过的事实，不是身份——它没资格坐上定义你的位置。",
                "现实里：选一件旧失败，写下它教会你的一件具体的事。" },
            new[] {   // 旧我线 其二 · 意志塔
                "你沿坡道登塔，每上一层，塔壁上的旧话就淡一分。",
                "「你不行 / 你总是这样」被复读太多年，几乎像是你自己的声音。",
                "旧话可以听见，但不必再照办——现在轮到你自己说话了。",
                "现实里：把一句常在脑中响起的旧话，改写成一句你想对自己说的新话。" },
            new[] {   // 旧我线 终局 · 旧事回声馆
                "旧我反复播放过去的失败、旧标签和未说出口的话。",
                "你差点把失败当成身份，把过去当成全部的自己。",
                "过去发生过，但不是我的全部；旧我不必杀死，只需更新。",
                "现实里：把一个旧标签改写成一个新的行动句。" },
        };

        static readonly string[] FreeMode =
        {
            "这一战里，对方用言语反复消耗你、试图接管你的方向。",
            "被刺痛是真的，但刺痛不等于事实，也不等于你不行。",
            "我守住我自己的注意力、边界与人生主线。",
            "现实里：为自己做一件五分钟的小事，把主动权拿回来。",
        };

        public static ReflectionPanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<ReflectionPanel>();
            comp.Build(canvas);
            return comp;
        }

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "ReflectionPanel", new Vector2(1200, 960),
                new Color(0.07f, 0.08f, 0.11f, 0.98f));

            var title = UiUtil.MakeText(_panel.transform, "Title", "战 后 复 盘", 40,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -44), new Vector2(700, 54));

            var hint = UiUtil.MakeText(_panel.transform, "Hint",
                "四栏可直接改写成自己的话；归档 = 反刍清零 + 写入档案 + 复盘点 +1", 22,
                TextAnchor.MiddleCenter, new Color(0.75f, 0.78f, 0.82f));
            UiUtil.SetRect(hint, new Vector2(0.5f, 1f), new Vector2(0, -86), new Vector2(1000, 30));

            _factInput  = MakeColumn("事实 · 发生了什么", new Color(0.85f, 0.9f, 1f), -120);
            _feelInput  = MakeColumn("感受 · 我为什么被刺痛", new Color(1f, 0.8f, 0.8f), -300);
            _boundInput = MakeColumn("边界 · 下一次更早守住什么", new Color(0.6f, 0.9f, 0.7f), -480);
            _actInput   = MakeColumn("行动 · 下一步现实小任务", new Color(1f, 0.85f, 0.5f), -660);

            _ruminationText = UiUtil.MakeText(_panel.transform, "Rum", "", 22,
                TextAnchor.MiddleCenter, new Color(0.8f, 0.6f, 0.85f));
            UiUtil.SetRect(_ruminationText, new Vector2(0.5f, 1f), new Vector2(0, -830), new Vector2(1000, 30));

            UiUtil.MakeButton(_panel.transform, "归档此战（清反刍·入档案·+1复盘点）", new Vector2(0.5f, 0f),
                new Vector2(-180, 52), new Vector2(640, 72),
                new Color(0.25f, 0.5f, 0.35f, 0.95f), OnArchive, 24);
            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(0.5f, 0f),
                new Vector2(330, 52), new Vector2(260, 72),
                new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 26);

            _panel.SetActive(false);
        }

        InputField MakeColumn(string header, Color headColor, float y)
        {
            var h = UiUtil.MakeText(_panel.transform, "H", header, 24,
                TextAnchor.MiddleLeft, headColor);
            UiUtil.SetRect(h, new Vector2(0.5f, 1f), new Vector2(0, y), new Vector2(1080, 32));
            h.fontStyle = FontStyle.Bold;

            var input = UiUtil.MakeInput(_panel.transform, "写下你的" + header.Substring(0, 2) + "……",
                new Vector2(0.5f, 1f), new Vector2(0, y - 88), new Vector2(1080, 128), true);
            var t = input.textComponent;
            t.fontSize = 23;
            return input;
        }

        void Refresh()
        {
            string[] r = FreeMode;
            var story = StoryManager.Instance;
            if (story != null && !story.AllCleared)
            {
                int idx = Mathf.Clamp(story.Chapter, 0, ChapterReflections.Length - 1);
                r = ChapterReflections[idx];
            }
            _factInput.text = r[0];
            _feelInput.text = r[1];
            _boundInput.text = r[2];
            _actInput.text = r[3];

            // 死亡诊断种子：刚倒下再战后首次打开复盘时，用诊断预填「感受/行动」两栏
            if (FailureLog.HasSeed)
            {
                var seed = FailureLog.ConsumeSeed();
                if (!string.IsNullOrEmpty(seed.feeling)) _feelInput.text = seed.feeling;
                if (!string.IsNullOrEmpty(seed.action)) _actInput.text = seed.action;
            }

            var stats = Stats();
            _ruminationText.text = stats != null
                ? $"当前反刍值：{Mathf.RoundToInt(stats.rumination)} / {Mathf.RoundToInt(stats.maxRumination)}"
                : "";
        }

        static PlayerStats Stats()
        {
            var pc = FindObjectOfType<PlayerController>();
            return pc != null ? pc.Stats : null;
        }

        void OnArchive()
        {
            var stats = Stats();
            // 复盘点只在真的有反刍要处理时发放（防止反复点归档刷点）
            bool earned = stats != null && stats.rumination >= 10f;
            if (stats != null)
            {
                stats.ReduceRumination(999f);
                stats.RestoreMental(25f);
            }

            // 写入旧事档案（安全屋「档案」面板可回看）
            var story = StoryManager.Instance;
            string chapterTitle = story != null && !story.AllCleared && story.Current != null
                ? story.Current.title : "自由修炼";
            GrowthSystem.SaveReflection(new ReflectionEntry
            {
                chapterTitle = chapterTitle,
                fact = _factInput.text,
                feeling = _feelInput.text,
                boundary = _boundInput.text,
                action = _actInput.text,
            });
            if (earned) GrowthSystem.AddPoints(1);

            // 「行动」栏 → 现实行动承诺：下次回安全屋「行动」面板确认是否做到
            ActionSystem.AddCommitment(_actInput.text, chapterTitle);

            GameAudio.Play(GameAudio.Sfx.Parry, 0.7f);
            GameEvents.RaiseSubtitle(earned
                ? "已归档：反刍清零，复盘点 +1。行动栏已记为现实承诺——下次回「行动」面板确认。"
                : "已归档入档案。行动栏已记为现实承诺——去做，然后回「行动」面板确认。");
            Refresh();
        }

        public void Toggle()
        {
            if (_panel.activeSelf) { Hide(); return; }
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
