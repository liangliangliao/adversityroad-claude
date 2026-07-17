using UnityEngine;
using AdversityRoad.AI;

namespace AdversityRoad.Core
{
    /// <summary>大章（成长线）：方案的七大章节 + 序章。每条线下有多个子章场景。</summary>
    [System.Serializable]
    public class ActInfo
    {
        public string title;   // 如「第一章 · 公平与承诺线」
        public string theme;   // 该线的核心主题句
    }

    [System.Serializable]
    public class ChapterInfo
    {
        public int actIndex;    // 所属大章（成长线）
        public int zoneIndex;
        public EnemyType enemyType;
        public EnemyTier enemyTier;
        public string enemyId;
        public string title;    // 子章完整标题，如「公平线 其一 · 两元赌桌」
        public string intro;
        public string victory;
        /// <summary>Boss 出生点覆盖（相对区域原点）。零向量 = 使用区域默认敌人出生点。</summary>
        public UnityEngine.Vector3 spawnOffset;
    }

    /// <summary>
    /// 主线剧情管理（大章-子章双层结构，对齐方案七大章节）：
    /// 序章·觉醒 → 公平与承诺线 → 外界刺激线 → 拖延与目标线 → 边界与责任线 → 旧我与新我线。
    /// （低谷与生存线 / 哲学与行动线 及部分子章后续版本补充。）
    /// 击败子章心魔推进剧情并解锁对应区域。进度本地持久化，死亡重开不丢。
    /// </summary>
    public class StoryManager : MonoBehaviour
    {
        public static StoryManager Instance { get; private set; }

        // v2：大章-子章结构重排后章节序号含义改变，换存档键重开主线
        // （技能树/装备/图鉴/档案/影子护卫等成长数据全部保留；可用设置面板「跳过当前章节」快进）
        const string SaveKey = "adversity_chapter_v2";

        public static readonly ActInfo[] Acts =
        {
            new ActInfo { title = "序章 · 觉醒",
                theme = "从床上坐起来，是一切的开始。" },
            new ActInfo { title = "第一章 · 公平与承诺线",
                theme = "维护公平，但不被公平伤口困住。" },
            new ActInfo { title = "第二章 · 外界刺激线",
                theme = "夺回注意力，不让外界刺激接管自己的方向。" },
            new ActInfo { title = "第三章 · 拖延与目标线",
                theme = "没有信心也可以行动，行动会反过来制造信心。" },
            new ActInfo { title = "第四章 · 边界与责任线",
                theme = "善良需要边界，负责需要归属，拒绝不等于冷血。" },
            new ActInfo { title = "第五章 · 低谷与生存线",
                theme = "低谷不是身份，而是需要资源、计划和坚持的阶段。" },
            new ActInfo { title = "第六章 · 哲学与行动线（后续版本开放）",
                theme = "思想不是为了困住你，而是为了帮助你更清醒地行动。" },
            new ActInfo { title = "终章 · 旧我与新我线",
                theme = "不是杀死旧我，而是整合旧我。" },
        };

        public static readonly ChapterInfo[] Chapters =
        {
            // ================= 序章 · 觉醒 =================
            new ChapterInfo
            {
                actIndex = 0, zoneIndex = 0,
                enemyType = EnemyType.SelfDoubtWhisper, enemyTier = EnemyTier.Novice,
                enemyId = "enemy_selfdoubt_whisper",
                title = "序章 其一 · 独居小屋",
                intro = "深夜，独居的房间。桌上的计划落满灰尘，角落里传来熟悉的低语：\n「你真的觉得自己可以？」\n\n这一次，你决定不再躺回床上。\n击败【见习·自我怀疑低语】，推开那扇门。",
                victory = "低语散去，房间安静下来。\n你推开门，门外是清晨的街道。\n下一站：训练武馆——把决心练成实力。"
            },
            new ChapterInfo
            {
                actIndex = 0, zoneIndex = 1,
                enemyType = EnemyType.TomorrowPhantom, enemyTier = EnemyTier.Standard,
                enemyId = "enemy_tomorrow_phantom",
                title = "序章 其二 · 训练武馆",
                intro = "武馆的木地板吱呀作响。\n一个熟悉的身影挡在训练柱之间——它总是劝你「明天再来」。\n\n击败【标准·明日幻影】，证明你今天就能开始。",
                victory = "幻影消散。你的拳脚第一次有了重量。\n\n武馆后门通向一间烟雾缭绕的棋牌室——\n有人在那里，用两块钱试探你的底线。\n【第一章 · 公平与承诺线】开启：维护公平，但不被公平伤口困住。"
            },

            // ================= 第一章 · 公平与承诺线 =================
            new ChapterInfo
            {
                actIndex = 1, zoneIndex = 9,
                enemyType = EnemyType.GambleKing, enemyTier = EnemyTier.Chief,
                enemyId = "boss_gamble_king",
                title = "公平线 其一 · 两元赌桌",
                intro = "狭小的棋牌室，一张旧桌子，几枚硬币。\n【两元赖账王】输了两块钱，却笑着说：「才这点钱，你也计较？」\n四周的旁观者跟着笑。\n\n提示：桌上的账本记着事实——走近「账本对质」可令它当场语塞破绽；\n绕桌走位躲开硬币弹幕。\n击败【首领·两元赖账王】：核心不是金额，而是承诺。",
                victory = "硬币在桌上发光，账本自动合上。\n核心不是金额，而是承诺；但追究也要看成本。\n\n棋牌室后门通向一座夜晚的停车场——\n那里停着一辆没结清的车。\n下一站：债务车影。"
            },
            new ChapterInfo
            {
                actIndex = 1, zoneIndex = 10,
                enemyType = EnemyType.DebtCarKing, enemyTier = EnemyTier.Chief,
                enemyId = "boss_debt_car_king",
                title = "公平线 其二 · 债务车影",
                intro = "夜晚的停车场，一辆被强光照亮的幻影车。\n【新车债王】坐在车里：「我又不是不还，过段时间再说。」\n\n提示：它被「未结清的故事」护体——收集场中三张发光的欠条残片，\n护体即碎；小心车灯眩光（预警后闪避），欠款残影会从车后爬出。\n击败【首领·新车债王】：事实是，这件事还没有结清。",
                victory = "幻影车灯熄灭，停车场安静下来。\n我可以坚持事实，但不能让一个未结清的故事长期占据生命中心。\n\n停车场深处的门通向一座畸形审判庭——\n在那里，被审判的不是账，而是你的感受。\n下一站：小题大做审判庭。"
            },
            new ChapterInfo
            {
                actIndex = 1, zoneIndex = 6,
                enemyType = EnemyType.SelfDenialGavel, enemyTier = EnemyTier.Chief,
                enemyId = "boss_self_denial_gavel",
                title = "公平线 其三 · 小题大做审判庭",
                intro = "倾斜的法官席上悬着一柄巨大的法槌。\n墙上漂浮着一个个标签：「太敏感」「小题大做」「你也有问题」。\n每一次你想说出事实，就有人敲响法槌：「这么小的事，你也要说？」\n\n提示：先去证据桌前看清事实——此后浮动标签一击即碎；\n击碎标签回补自尊，破碎的镜子能让你看清被扭曲的倒影。\n击败【首领·自我否定法槌】，守住自己的判断。",
                victory = "法槌落地，标签散尽。\n我可以感受强烈，但这不等于没有理由——事实先于评价。\n【公平与承诺线 · 完成】你学会了区分「维护原则」和「被原则伤口困住」。\n\n远处的街道传来一声咳嗽。\n【第二章 · 外界刺激线】开启：先夺回注意力。\n下一站：一声咳嗽的街道（「传送」面板可直达）。"
            },

            // ================= 第二章 · 外界刺激线 =================
            new ChapterInfo
            {
                actIndex = 2, zoneIndex = 2,
                enemyType = EnemyType.CoughAssassin, enemyTier = EnemyTier.Elite,
                enemyId = "enemy_cough_assassin",
                title = "刺激线 其一 · 一声咳嗽的街道",
                intro = "行人、车辆、议论声、咳嗽声……\n每一个声音都在拉扯你的注意力。\n\n在干扰中保持专注，击败【精英·咳声刺客】。\n提示：专注值被打空时锁定会失灵，用「定心格挡」反制心理攻击；\n广告牌下与公交站旁是噪声区——切「定心姿态」可减免。",
                victory = "街道依旧喧嚣，但那些声音再也钻不进你的心里。\n\n街区西侧出现一条挂满眼睛的走廊——\n每一道目光都像在审判你。\n下一站：眼神审判走廊。"
            },
            new ChapterInfo
            {
                actIndex = 2, zoneIndex = 11,
                enemyType = EnemyType.ThousandEyeJudge, enemyTier = EnemyTier.Chief,
                enemyId = "boss_thousand_eye_judge",
                title = "刺激线 其二 · 眼神审判走廊",
                intro = "狭长的走廊，两壁布满眼睛状的灯与镜面。\n每走一步，都有目光转过来。\n尽头的圆形镜厅里，【万眼审判者】睁开了一千只眼睛。\n\n提示：它会制造「虚假凝视点」幻影——攻击幻影只会消耗你自己；\n用「不读心盾」（键5/盾）让幻影显形、「注意力回收」（键6/收）清场后打真身。\n击败【首领·万眼审判者】：被看见，不等于被否定。",
                victory = "千眼闭合，走廊的灯一盏盏暗下来。\n被看见不等于被否定——你不需要向每一道目光交代。\n\n走廊出口连着一个喧闹的十字路口，\n有人故意撞了你一下，等你回头。\n下一站：陌生挑衅路口。"
            },
            new ChapterInfo
            {
                actIndex = 2, zoneIndex = 12,
                enemyType = EnemyType.TauntMirror, enemyTier = EnemyTier.Chief,
                enemyId = "boss_taunt_mirror",
                title = "刺激线 其三 · 陌生挑衅路口",
                intro = "城市十字路口：红绿灯、车流幻影、围观的人群。\n【挑衅镜像】站在路口中央——它长得像你，专门模仿你最冲动的样子。\n\n本关核心是「不被拖入战场」：\n它举起双手挑衅时（头顶亮起挑衅标记），打它=它变强并吸血；\n忍住不打，挑衅落空，它会自己露出大破绽。\n别追进车流幻影区（马路上会持续掉血）。\n击败【首领·挑衅镜像】：不是所有挑衅都值得回应。",
                victory = "镜像碎裂——它模仿不了一个不接招的人。\n哪些敌人值得战斗、什么时候撤离、什么时候反击，你已经会判断了。\n\n可街心广场的方向，所有噪声正在被什么东西放大十倍。\n下一站：回到街心广场，终结【刺激放大器】。"
            },
            new ChapterInfo
            {
                actIndex = 2, zoneIndex = 2,
                enemyType = EnemyType.StimulusAmplifier, enemyTier = EnemyTier.Chief,
                enemyId = "boss_stimulus_amplifier",
                title = "刺激线 其四 · 噪声放大（终战）",
                intro = "熟悉的街道，陌生的震动。\n【刺激放大器】把每一声咳嗽、每一次转头、每一句低语都放大了十倍——\n整条街都成了针对你的证据。\n\n提示：幻影假目标用「不读心盾」显形、「注意力回收」清场；\n噪声放大可用定心格挡整个化解。\n击败【首领·刺激放大器】，夺回注意力的主权。",
                victory = "放大器碎裂，街道恢复成普通的街道。\n咳嗽只是咳嗽，眼神只是眼神。\n【外界刺激线 · 完成】外界可以存在，但不能接管你。\n\n街道尽头东南侧出现一扇门——门里是一个你很熟悉的房间：\n杂乱、落灰，目标板被埋在最深处。\n【第三章 · 拖延与目标线】开启：行动会反过来制造信心。\n下一站：目标遗忘房。",
                spawnOffset = new UnityEngine.Vector3(0, 1.1f, 9)
            },

            // ================= 第三章 · 拖延与目标线 =================
            new ChapterInfo
            {
                actIndex = 3, zoneIndex = 13,
                enemyType = EnemyType.GoalForgetter, enemyTier = EnemyTier.Chief,
                enemyId = "boss_goal_forgetter",
                title = "拖延线 其一 · 目标遗忘房",
                intro = "一个杂乱的房间：床被藤蔓缠住、手机在角落发光、\n满地便利贴写着「想做但没做」，墙壁展开成一座小迷宫。\n最深处，落灰的目标板前站着【目标遗忘者】——\n它不打算杀你，它只想让你忘记进来是要做什么的。\n\n提示：手机光点和床铺藤蔓都会拖住你——别停留；\n它的「遗忘之雾」（预警圈）流失行动力，被冻慢就按「五分钟火种」（键4/火）。\n击败【首领·目标遗忘者】，走到目标板前。",
                victory = "遗忘者散去，你擦掉目标板上的灰。\n上面写着你自己当年的字：想做的事、想成为的人。\n\n提示：打开安全屋「目标」面板，钉下今日唯一目标——\n每次进入游戏，都完成一个明确的小目标。\n\n房间东北的门通向一片湿地。\n下一站：拖延沼泽。"
            },
            new ChapterInfo
            {
                actIndex = 3, zoneIndex = 7,
                enemyType = EnemyType.TomorrowKing, enemyTier = EnemyTier.Chief,
                enemyId = "boss_tomorrow_king",
                title = "拖延线 其二 · 拖延沼泽",
                intro = "泥沼中漂着床垫、手机屏幕和写满计划的纸。\n巨大的泥台上坐着【明天之王】——它的泥壳由无数个「明天再说」凝成，寻常刀剑打不动。\n\n提示：泥壳只有一个弱点——已经开始的行动。\n点燃场边三座「五分钟火种台」（走近即点燃），火种齐燃时泥壳崩裂；\n陷进深泥或被冻住时，按「五分钟火种」（键4/火）脱身。\n击败【首领·明天之王】，把今天夺回来。",
                victory = "泥壳崩裂，王座沉入沼底。\n不是等有动力才行动，而是行动召回动力——五分钟就够点燃开始。\n\n沼泽的西边，是一片漫天飘着简历的荒原——\n你投出去的每一份努力，都石沉大海。\n下一站：求职沉默荒原。"
            },
            new ChapterInfo
            {
                actIndex = 3, zoneIndex = 3,
                enemyType = EnemyType.NoReplyKing, enemyTier = EnemyTier.Chief,
                enemyId = "boss_no_reply_king",
                title = "拖延线 其三 · 求职沉默荒原",
                intro = "荒原上漫天飘着简历纸片，五扇面试之门紧闭，只有一扇透着光。\n审判台上坐着一个不说话的王——它的武器是沉默，它的刀刃是拒信。\n\n击败【首领·无回应之王】，夺回「下一次投递」的勇气。\n提示：它会远程掷出拒信飞刃，靠近它、别停下脚步。",
                victory = "王座崩塌，沉默被打破。\n没有回应不代表没有价值——你还能再投一次。\n\n最后一站：城市广场。\n你所有的拖延，正在那里凝成一个有你轮廓的影子。"
            },
            new ChapterInfo
            {
                actIndex = 3, zoneIndex = 4,
                enemyType = EnemyType.ProcrastinationShadow, enemyTier = EnemyTier.Chief,
                enemyId = "boss_procrastination_shadow",
                title = "拖延线 其四 · 城市广场（终战）",
                intro = "华灯初上的广场中央，站着一个巨大的影子。\n它有你的轮廓——那是无数个「明天再说」堆成的旧我。\n\n击败【首领·拖延影魔】，把现在夺回来。",
                victory = "影子碎裂成漫天光点。\n【拖延与目标线 · 完成】不是等有信心才行动，是行动制造信心。\n\n可广场东门里传来此起彼伏的请求声：\n「就这一次」「你人最好了」「帮个忙不过分吧」——\n【第四章 · 边界与责任线】开启：善良需要边界。\n下一站：老实人消耗局。"
            },

            // ================= 第四章 · 边界与责任线 =================
            new ChapterInfo
            {
                actIndex = 4, zoneIndex = 14,
                enemyType = EnemyType.GoodPersonCage, enemyTier = EnemyTier.Chief,
                enemyId = "boss_good_person_cage",
                title = "边界线 其一 · 老实人消耗局",
                intro = "一座不断扩张的大厅：四面是「请求入口」，写着时间、金钱、精力、情绪。\n请求膨胀者从入口涌来，内疚投手在远处掷来内疚。\n大厅中央的【好人牢笼】笑着说：「你人最好了。」\n\n提示：中央绿圈是你的边界圈——站入恢复边界、清除过度负责；\n被好人卡糊脸或被牢笼困住时，按「责任归还」（键3/还）清除与打破。\n击败【首领·好人牢笼】：帮助有边界，不是无限资源。",
                victory = "牢笼散架成一地好人卡。\n善良不需要证明——它需要边界。\n\n大厅东北的门通向一座高大的法院：\n那里，有人正把不属于你的责任一件件推给你。\n下一站：责任转嫁法院。"
            },
            new ChapterInfo
            {
                actIndex = 4, zoneIndex = 5,
                enemyType = EnemyType.TotalResponsibilityJudge, enemyTier = EnemyTier.Chief,
                enemyId = "boss_total_responsibility_judge",
                title = "边界线 其二 · 责任转嫁法院",
                intro = "高大的法院里，责任天平永远倾向你这一侧。\n审判席上的【全责法官】不停宣判：「这也是你的责任。」\n它会把一个个「责任球」抛向你——红色的不属于你，绿色的才是你的本分。\n\n提示：举起边界盾（格挡）把红球挡回去，就是「责任归还」；绿球别推开，接下它。\n击败【首领·全责法官】，学会准确承担属于自己的部分。",
                victory = "责任天平终于回正。\n真正的负责不是什么都背，而是准确承担属于自己的那部分。\n\n可法院深处还有一条走不完的走廊——\n两侧的门后，全是等着你代付的账单。\n下一站：无限代付走廊（法院审判席西侧的门）。"
            },
            new ChapterInfo
            {
                actIndex = 4, zoneIndex = 15,
                enemyType = EnemyType.InfinitePayer, enemyTier = EnemyTier.Chief,
                enemyId = "boss_infinite_payer",
                title = "边界线 其三 · 无限代付走廊（终战）",
                intro = "一条望不到头的走廊，两侧的门上写着：\n时间、金钱、精力、情绪、注意力、责任、同情、解释。\n走廊上一道道「请求区」——**举着盾（格挡）通过 = 明确拒绝**；\n空手走过 = 默认代付，边界与关系被悄悄扣款。\n\n尽头的圆厅里，【无限代付者】等着你。\n它发动【索取冲击】（绿圈预警）时**举盾格挡** = 明确拒绝成功，它当场大破绽；\n没挡住就会被大量掏空。小心脚下的「代付之门」吸取区。\n击败【首领·无限代付者】：我不再无限代付。",
                victory = "走廊塌缩成一扇普通的门。\n【边界与责任线 · 完成】我不是你的钱包，也不是你的替身人生——\n我可以帮，但由我决定帮什么、帮多少。\n\n可门外的城市忽然安静：路灯稀疏，胃里发空。\n【第五章 · 低谷与生存线】开启：低谷不是身份，而是一个阶段。\n下一站：饥饿荒巷（圆厅东北的门）。"
            },
            // ================= 第五章 · 低谷与生存线 =================
            new ChapterInfo
            {
                actIndex = 5, zoneIndex = 16,
                enemyType = EnemyType.HungerHound, enemyTier = EnemyTier.Chief,
                enemyId = "enemy_hunger_hound",
                title = "低谷线 其一 · 饥饿荒巷",
                intro = "夜晚的小巷：垃圾桶、空纸箱、雨水洼。\n这一关不靠蛮力——**先找资源**：巷子里散落着水瓶与食物包，\n路灯下是安全区，尽头有一座「求助电话亭」（走近使用，大幅恢复）。\n\n【饥饿犬影】在暗处游荡：它们快、狠、成群，别在黑暗里恋战。\n击败【首领·饥饿犬影】：先照顾好基本生存，再谈别的。",
                victory = "犬影散去，尽头餐馆的灯牌暖得晃眼。\n低谷里的第一课：先解决今晚的水和饭，天塌不下来。\n\n荒巷西侧的坡道通向地下车库——那里比外面更冷。\n下一站：车库寒夜。"
            },
            new ChapterInfo
            {
                actIndex = 5, zoneIndex = 17,
                enemyType = EnemyType.ColdWindBlade, enemyTier = EnemyTier.Chief,
                enemyId = "enemy_cold_wind_blade",
                title = "低谷线 其二 · 车库寒夜",
                intro = "寒冷的地下车库：整片区域都在吹走你的意志（意志条持续流失）。\n三座火盆是生命线——**烤火获得「暖意」**，离开后短时间内不怕冷。\n\n【寒风刃】在空旷处成形，专挑你离开火堆的时候动手。\n规划路线：从一个取暖点冲向下一个，中途别贪战。\n击败【首领·寒风刃】：低谷需要的是计划，不是硬扛。",
                victory = "风停了。你在最后一堆火边坐了一会儿。\n硬扛不是坚强——知道去哪里取暖，才是。\n\n车库出口连着一条彻夜亮灯的走廊，消毒水味扑面而来。\n下一站：病房回廊。"
            },
            new ChapterInfo
            {
                actIndex = 5, zoneIndex = 18,
                enemyType = EnemyType.ValleyColossus, enemyTier = EnemyTier.Chief,
                enemyId = "boss_valley_colossus",
                title = "低谷线 其三 · 病房回廊（终战）",
                intro = "白色的医院走廊：病房门、安静提示牌、漂浮的医药账单。\n这里战斗强度不高——真正沉重的是气氛本身。\n\n尽头大厅里，【低谷巨像】由无力感、内疚重石和生存恐慌凝成。\n它的「无力威压」流失意志、「内疚重石」从天而降（看红圈躲）。\n\n破局关键（本线的答案）：大厅两侧有**求助电话亭**——\n拨通求助电话，它的「无力感」当场松动【大破绽】。\n承认需要帮助，不等于失去尊严。",
                victory = "巨像轰然散落成一地碎石，走廊的灯一盏盏亮起来。\n【低谷与生存线 · 完成】低谷不是身份，而是阶段——\n需要资源就去找，需要帮助就开口。\n\n最后的路通向一座安静的建筑：旧事回声馆。\n【终章 · 旧我与新我线】开启：不是杀死旧我，而是整合旧我。\n下一站：旧事回声馆（大厅东北的门，或「传送」面板直达）。\n（第六章·哲学与行动线将在后续版本开放。）"
            },

            // ================= 终章 · 旧我与新我线 =================
            new ChapterInfo
            {
                actIndex = 7, zoneIndex = 8,
                enemyType = EnemyType.OldSelf, enemyTier = EnemyTier.Chief,
                enemyId = "boss_old_self",
                title = "旧我线 终局 · 旧事回声馆",
                intro = "博物馆里陈列着你的失败记录、旧标签和未说出口的话。\n靠近展柜，旧事就开始循环播放——站定完成「归档」，让它们安静下来（归档 3 座开启终局大门）。\n\n镜面平台中央站着【旧我】。它有你的轮廓——那是过去为了保护你而形成的旧模式。\n它会复读旧话、冻结你的脚步、召回你打败过的旧敌。\n\n记住：旧我不能也不需要被杀死。\n打到它停手时，走进整合圆环，完成「旧我整合式」。",
                victory = "旧我化为影子护卫，站到了你身后。\n「你曾经保护过我，但现在我要继续往前。」\n\n过去发生过，但不是你的全部；失败是事实，不是身份。\n\n【主线完结】自由修炼模式已开启：\n可用「敌人+」在任意区域添加不同类型与难度的心魔挑战；\n安全屋各面板（复盘/成长/装备/图鉴/档案/目标/传送）持续可用。\n（低谷与生存线、哲学与行动线及剩余子章将在后续版本开放。）",
                spawnOffset = new UnityEngine.Vector3(0, 1.1f, 30)
            }
        };

        public int Chapter { get; private set; }

        public bool AllCleared => Chapter >= Chapters.Length;

        public ChapterInfo Current =>
            Chapter < Chapters.Length ? Chapters[Chapter] : null;

        /// <summary>当前大章信息（主线完结返回最后一条线）。</summary>
        public ActInfo CurrentAct =>
            Acts[Mathf.Clamp(Current != null ? Current.actIndex : Acts.Length - 1, 0, Acts.Length - 1)];

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            Chapter = PlayerPrefs.GetInt(SaveKey, 0);
        }

        void OnEnable() => GameEvents.OnEnemyKilled += HandleEnemyKilled;
        void OnDisable() => GameEvents.OnEnemyKilled -= HandleEnemyKilled;

        void HandleEnemyKilled(string enemyId)
        {
            var cur = Current;
            if (cur == null || enemyId != cur.enemyId) return;
            Advance();
        }

        void Advance()
        {
            Chapter++;
            PlayerPrefs.SetInt(SaveKey, Chapter);
            PlayerPrefs.Save();
            GameEvents.RaiseChapterAdvanced(Chapter);
        }

        /// <summary>调试/老玩家快进：跳过当前子章（视为已完成，不发奖励）。</summary>
        public void SkipChapter()
        {
            if (AllCleared) return;
            // 清掉当前章节心魔（若在场），避免残留旧 Boss
            foreach (var e in FindObjectsOfType<AI.EnemyController>())
                if (e.profile != null && e.profile.enemyId == Current.enemyId)
                    Destroy(e.gameObject);
            Advance();
        }

        /// <summary>
        /// 区域是否解锁：主线到过的区域集合（子章顺序不与区域顺序单调对应——
        /// 例如刺激线终战回访噪声街区）。安全屋（0 区）永远可回。
        /// </summary>
        public bool ZoneUnlocked(int zoneIndex)
        {
            if (AllCleared || zoneIndex == 0) return true;
            for (int i = 0; i <= Chapter && i < Chapters.Length; i++)
                if (Chapters[i].zoneIndex == zoneIndex) return true;
            return false;
        }

        /// <summary>重置主线（调试/新周目用）。</summary>
        public void ResetStory()
        {
            Chapter = 0;
            PlayerPrefs.SetInt(SaveKey, 0);
            PlayerPrefs.Save();
        }
    }
}
