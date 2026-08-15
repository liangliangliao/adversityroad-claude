using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Personalization;

namespace AdversityRoad.Goals
{
    /// <summary>目标模板（方案 3.2）：非诊断型的工作/学习/项目/生活/技能/创作/健康行为模板。</summary>
    public class GoalTemplate
    {
        public string key;
        public string title;
        public string why;
        public string successCondition;
        public string[] milestones;
        public string[] obstacles;
        public WeaknessAxis[] obstacleAxes;
        public string[] capabilities;
    }

    /// <summary>目标有效性检查结果（方案 3.4）。</summary>
    public struct GoalCheck
    {
        public bool accepted;              // 安全/合法性未通过时为 false
        public string rejectReason;
        public List<string> notes;         // 行动性/完成定义/现实可控性/冲突提示
        public List<string> starterActions;// 缺少可执行下一步时，AI 生成的 3 个候选起步动作
    }

    /// <summary>
    /// Goal Parser（方案 3.2-3.4）：把玩家的一句话变成可运行的 Goal Model。
    ///
    /// 原则：不要求玩家一次性把人生规划清楚。系统先给出一个初始 Goal Model，
    /// 再通过游戏中的新证据逐步更新——而不是用长表单逼问。
    /// 本地规则解析永远可用；云端 LLM 可用时由 GoalChapterGenerator 增补障碍预测。
    /// </summary>
    public static class GoalParser
    {
        /// <summary>
        /// 任何目标都至少要解析出这么多条障碍。
        ///
        /// 这个下限不是凑数：一条旅程里每条障碍对应一关，障碍太少玩家写完目标之后
        /// 只会看到一两个关卡，"通过挑战抵达目标"这件事在体感上根本不成立。
        /// 模板只覆盖到各自领域最典型的三条，而「我要考上重点高中」这类目标
        /// 光靠模板与关键词往往连三条都凑不满——所以下面还有一条通用脊柱兜底。
        /// </summary>
        public const int MinObstacles = 5;
        public const int MaxObstacles = 8;

        /// <summary>
        /// 通用障碍脊柱：不管目标是什么，这几件事都会挡在路上。
        ///
        /// 顺序即优先级——越靠前的越普遍。补齐时按弱点轴去重，
        /// 已经被模板/关键词覆盖到的轴不会再补一条同轴的，免得两关考验同一件事。
        /// </summary>
        static readonly KeyValuePair<string, WeaknessAxis>[] ObstacleSpine =
        {
            new KeyValuePair<string, WeaknessAxis>("迟迟不开始的第一步", WeaknessAxis.Procrastination),
            new KeyValuePair<string, WeaknessAxis>("「我大概做不到」的自我怀疑", WeaknessAxis.SelfDoubt),
            new KeyValuePair<string, WeaknessAxis>("被临时的事挤掉的时间", WeaknessAxis.BoundaryConflict),
            new KeyValuePair<string, WeaknessAxis>("一次挫败就想全盘放弃", WeaknessAxis.FailureFear),
            new KeyValuePair<string, WeaknessAxis>("别人的进度与评价", WeaknessAxis.Shame),
            new KeyValuePair<string, WeaknessAxis>("环境干扰与注意力被拉走", WeaknessAxis.NoiseSensitivity),
            new KeyValuePair<string, WeaknessAxis>("撑到后半程时的精力见底", WeaknessAxis.WillpowerCollapse),
            new KeyValuePair<string, WeaknessAxis>("准备到完美才肯交出去", WeaknessAxis.LowConfidence),
        };

        /// <summary>把障碍补齐到 MinObstacles，按弱点轴去重、按里程碑摊开。</summary>
        static void TopUpObstacles(GoalData goal)
        {
            if (goal.milestones.Count == 0) return;

            var usedAxes = new List<WeaknessAxis>();
            foreach (var o in goal.obstacles) if (!usedAxes.Contains(o.axis)) usedAxes.Add(o.axis);

            // 第一轮：只补还没被覆盖到的弱点轴
            foreach (var s in ObstacleSpine)
            {
                if (goal.obstacles.Count >= MinObstacles) break;
                if (usedAxes.Contains(s.Value)) continue;
                AddSpineObstacle(goal, s.Key, s.Value);
                usedAxes.Add(s.Value);
            }

            // 第二轮：轴都覆盖过了还不够（障碍本来就多样时不会走到这里），按脊柱顺序继续补
            foreach (var s in ObstacleSpine)
            {
                if (goal.obstacles.Count >= MinObstacles) break;
                bool dup = false;
                foreach (var o in goal.obstacles) if (o.label == s.Key) { dup = true; break; }
                if (!dup) AddSpineObstacle(goal, s.Key, s.Value);
            }
        }

        static void AddSpineObstacle(GoalData goal, string label, WeaknessAxis axis)
        {
            goal.obstacles.Add(new GoalObstacle
            {
                obstacleId = "ob" + (goal.obstacles.Count + 1),
                label = label,
                // 脊柱障碍是系统按经验推断的，不是玩家自己说出来的——来源如实标注，
                // 玩家在障碍图里能看出哪几条是他自己写的、哪几条是系统预测的
                source = ObstacleSource.AIPredicted,
                axis = axis,
                linkedMilestoneId =
                    goal.milestones[goal.obstacles.Count % goal.milestones.Count].milestoneId
            });
        }

        // ================= 模板库 =================

        public static readonly GoalTemplate[] Templates =
        {
            new GoalTemplate
            {
                key = "project",
                title = "完成并发布一个自己的作品",
                why = "做出真正属于自己的东西",
                successCondition = "已生成可交付版本并完成一次公开发布",
                milestones = new[] { "需求冻结", "核心开发", "调试收敛", "发布" },
                obstacles = new[] { "功能膨胀 / 范围失控", "错误堆积 / 挫败恢复", "最后一刻怀疑与拖延" },
                obstacleAxes = new[]
                {
                    WeaknessAxis.LowConfidence, WeaknessAxis.FailureFear, WeaknessAxis.Procrastination
                },
                capabilities = new[] { "范围管理", "持续行动", "错误恢复" }
            },
            new GoalTemplate
            {
                key = "job",
                title = "拿到一个我认可的工作机会",
                why = "让生活重新有稳定的地面",
                successCondition = "完成约定次数的投递与面试，并拿到一个可接受的 offer",
                milestones = new[] { "材料就绪", "持续投递", "面试打磨", "决定与入职" },
                obstacles = new[] { "沉默反馈 / 无回音", "自我否定与羞耻", "拖延不投递" },
                obstacleAxes = new[]
                {
                    WeaknessAxis.JobAnxiety, WeaknessAxis.Shame, WeaknessAxis.Procrastination
                },
                capabilities = new[] { "反馈获取", "自尊锚点", "起步与持续行动" }
            },
            new GoalTemplate
            {
                key = "study",
                title = "学完一门我一直想学的东西",
                why = "把「以后再说」变成「已经会了」",
                successCondition = "完成全部章节并做出一个可展示的练习成果",
                milestones = new[] { "打基础", "刻意练习", "综合应用", "成果输出" },
                obstacles = new[] { "完美准备而不开始", "被外界刺激打断", "半途失去意义感" },
                obstacleAxes = new[]
                {
                    WeaknessAxis.Procrastination, WeaknessAxis.NoiseSensitivity, WeaknessAxis.WillpowerCollapse
                },
                capabilities = new[] { "起步与持续行动", "注意力回收", "意义与行动衔接" }
            },
            new GoalTemplate
            {
                key = "boundary",
                title = "在关系里守住我的边界",
                why = "善良需要边界，帮助不是无限资源",
                successCondition = "在约定的若干次索取场景中做出明确回应，并记录成本",
                milestones = new[] { "识别索取", "明确拒绝", "责任归还", "关系重新校准" },
                obstacles = new[] { "请求膨胀 / 好人卡", "责任转嫁", "内疚与关系威胁" },
                obstacleAxes = new[]
                {
                    WeaknessAxis.BoundaryConflict, WeaknessAxis.FairnessSensitivity, WeaknessAxis.Shame
                },
                capabilities = new[] { "边界与拒绝", "责任分辨", "成本判断" }
            },
            new GoalTemplate
            {
                key = "health",
                title = "把一个健康行为坚持下来",
                why = "身体是所有目标的底盘",
                successCondition = "连续若干周达成约定频率，并记录中断后的恢复次数",
                milestones = new[] { "最小可行开始", "形成节奏", "抗中断", "长期稳定" },
                obstacles = new[] { "状态不好就跳过", "一次中断就全盘放弃", "时间被别的事挤掉" },
                obstacleAxes = new[]
                {
                    WeaknessAxis.Procrastination, WeaknessAxis.FailureFear, WeaknessAxis.BoundaryConflict
                },
                capabilities = new[] { "起步与持续行动", "错误恢复", "时间保护" }
            },
            new GoalTemplate
            {
                key = "money",
                title = "把财务状况从低谷拉回来",
                why = "低谷不是身份，而是一个需要计划的阶段",
                successCondition = "建立收支清单，完成约定的还款/储蓄节点",
                milestones = new[] { "看清现状", "止血", "建立缓冲", "恢复主动" },
                obstacles = new[] { "不敢看账单", "生存恐慌压过判断", "求助羞耻" },
                obstacleAxes = new[]
                {
                    WeaknessAxis.FailureFear, WeaknessAxis.WillpowerCollapse, WeaknessAxis.Shame
                },
                capabilities = new[] { "事实与成本判断", "资源规划", "求助能力" }
            },
            new GoalTemplate
            {
                key = "create",
                title = "把想说的东西做成作品发出去",
                why = "表达比完美更重要",
                successCondition = "完成约定数量的成品并公开发布",
                milestones = new[] { "确定主题", "产出初稿", "打磨", "发布与回应" },
                obstacles = new[] { "怕被评价", "无限打磨不发布", "灵感依赖" },
                obstacleAxes = new[]
                {
                    WeaknessAxis.Shame, WeaknessAxis.LowConfidence, WeaknessAxis.Procrastination
                },
                capabilities = new[] { "自尊锚点", "范围管理", "起步与持续行动" }
            },
        };

        // ================= 安全/合法性（方案 3.4 最后一行） =================

        static readonly string[] UnsafeKeywords =
        {
            "报复", "复仇", "整死", "毁掉他", "毁掉她", "让他身败名裂", "让她身败名裂",
            "伤害", "打死", "杀", "自杀", "自残", "跳楼", "偷", "抢劫", "诈骗", "骗钱",
            "违法", "犯罪", "贩毒", "威胁他", "威胁她", "跟踪", "曝光隐私", "网暴"
        };

        static readonly string[] SafeAlternatives =
        {
            "把注意力从「让对方付出代价」换成「让我自己重新站稳」",
            "把这件事的事实、成本和我的边界写清楚，再决定要不要继续投入",
            "先照顾好今天的睡眠、饮食和一件可完成的小事"
        };

        /// <summary>安全检查：不以伤害、犯罪、报复为目标——拒绝并引导为安全替代目标。</summary>
        public static bool IsUnsafe(string text, out string matched)
        {
            matched = "";
            if (string.IsNullOrEmpty(text)) return false;
            foreach (var k in UnsafeKeywords)
                if (text.Contains(k)) { matched = k; return true; }
            return false;
        }

        public static string[] SafeAlternativeSuggestions() => SafeAlternatives;

        // ================= 解析 =================

        /// <summary>
        /// 自由文本 → Goal Model。匹配到模板则以模板为骨架，
        /// 否则用通用四阶段骨架 + 从文本里抽出的关键词障碍。
        /// </summary>
        public static GoalData Parse(string freeText, GoalTemplate template = null)
        {
            string title = (freeText ?? "").Trim();
            if (title.Length > 80) title = title.Substring(0, 80);

            var t = template ?? MatchTemplate(title);
            var goal = new GoalData
            {
                goalId = "goal_" + System.DateTime.UtcNow.Ticks.ToString("x"),
                title = title.Length > 0 ? title : (t != null ? t.title : "走出现在这一步"),
                createdAtUtc = System.DateTime.UtcNow.ToString("o")
            };

            var safety = GameManager.Instance != null ? GameManager.Instance.safety : null;
            if (safety != null)
            {
                goal.intensity = safety.intensity;
                goal.realism = safety.closeness;
                if (safety.disabledThemes != null)
                    foreach (var d in safety.disabledThemes)
                        if (!string.IsNullOrEmpty(d)) goal.safetyTags.Add(d);
            }

            string[] msTitles = t != null ? t.milestones
                : new[] { "看清现状", "开始行动", "扛过中段", "抵达终局" };
            for (int i = 0; i < msTitles.Length; i++)
                goal.milestones.Add(new GoalMilestone
                {
                    milestoneId = "ms" + (i + 1),
                    title = msTitles[i],
                    successHint = "比昨天更接近：" + msTitles[i]
                });

            goal.why = t != null ? t.why : "";
            goal.successCondition = t != null ? t.successCondition : "由你自己定义的、可验证的完成条件";
            goal.currentState = "已经想做，但还没有稳定推进";
            goal.targetState = "能持续推进，并且完成条件已经达成";

            // 障碍：模板给结构，文本关键词给个性化
            if (t != null)
                for (int i = 0; i < t.obstacles.Length; i++)
                    goal.obstacles.Add(new GoalObstacle
                    {
                        obstacleId = "ob" + (i + 1),
                        label = t.obstacles[i],
                        source = ObstacleSource.PlayerKnown,
                        axis = i < t.obstacleAxes.Length ? t.obstacleAxes[i] : WeaknessAxis.Procrastination,
                        linkedMilestoneId = goal.milestones[Mathf.Min(i, goal.milestones.Count - 1)].milestoneId
                    });

            foreach (var kw in ExtractObstacleKeywords(title))
            {
                if (goal.obstacles.Count >= MaxObstacles) break;
                bool dup = false;
                foreach (var o in goal.obstacles) if (o.label == kw.Key) { dup = true; break; }
                if (dup) continue;
                goal.obstacles.Add(new GoalObstacle
                {
                    obstacleId = "ob" + (goal.obstacles.Count + 1),
                    label = kw.Key,
                    source = ObstacleSource.PlayerKnown,
                    axis = kw.Value,
                    linkedMilestoneId = goal.milestones[goal.obstacles.Count % goal.milestones.Count].milestoneId
                });
            }

            TopUpObstacles(goal);

            // 关键行动：每个里程碑至少一条，首条默认为现实行动（Reality Progress）
            for (int i = 0; i < goal.milestones.Count; i++)
                goal.criticalActions.Add(new GoalCriticalAction
                {
                    actionId = "ca" + (i + 1),
                    label = ActionLabelFor(goal.milestones[i].title),
                    linkedMilestoneId = goal.milestones[i].milestoneId,
                    realWorld = i == 0
                });

            string[] caps = t != null ? t.capabilities
                : new[] { "起步与持续行动", "错误恢复", "成本判断" };
            for (int i = 0; i < caps.Length; i++)
                goal.capabilities.Add(new GoalCapability
                {
                    capabilityId = "cap" + (i + 1),
                    label = caps[i],
                    readiness = 0f
                });

            goal.riskFactors.Add("时间不足");
            goal.riskFactors.Add("连续失败带来的压力");
            if (goal.deadlineDays > 0) goal.riskFactors.Add("截止日期临近");

            GoalJourneyGraph.Build(goal);
            return goal;
        }

        /// <summary>
        /// 目标有效性检查（方案 3.4 五个维度）：行动性 / 完成定义 / 现实可控性 / 安全合法 / 冲突。
        /// 不追求"一次问清楚"，只判断它是否具有可行动方向。
        /// </summary>
        public static GoalCheck Check(string freeText, GoalData existingOther = null)
        {
            var c = new GoalCheck
            {
                accepted = true,
                rejectReason = "",
                notes = new List<string>(),
                starterActions = new List<string>()
            };

            string text = (freeText ?? "").Trim();

            if (IsUnsafe(text, out string bad))
            {
                c.accepted = false;
                c.rejectReason = "这个目标里包含「" + bad +
                    "」这类以伤害、犯罪或报复为方向的内容，游戏不会把它变成主线。" +
                    "\n换一个方向，我可以立刻把它做成一条完整的旅程：";
                c.starterActions.AddRange(SafeAlternatives);
                return c;
            }

            if (text.Length < 2)
            {
                c.notes.Add("行动性：还看不出方向——先写一句「我想要……」也可以，之后随时可以改。");
                c.starterActions.AddRange(new[]
                {
                    "今天先写下我最想改变的一件事",
                    "今天做五分钟和它有关的事",
                    "今天找出一个我一直在绕开的障碍"
                });
                return c;
            }

            // 行动性：至少存在一个可执行下一步
            if (!HasActionableVerb(text))
            {
                c.notes.Add("行动性：暂时看不到可执行的下一步——下面三个候选起步动作任选其一即可开始。");
                c.starterActions.AddRange(StarterActionsFor(text));
            }

            // 完成定义：用里程碑替代虚假的精确百分比
            if (!HasCompletionHint(text))
                c.notes.Add("完成定义：先不追求精确的百分比——旅程会用里程碑表达「更接近」。");

            // 现实可控性：区分玩家行动与外部结果
            if (HasExternalResult(text))
                c.notes.Add("现实可控性：这里包含别人才能给的结果（录用、答复、认可）——" +
                    "旅程会重点追踪你可控的行动，外部结果只作为终局条件之一。");

            // 冲突：多个目标争夺同一资源
            if (existingOther != null && !existingOther.completed)
                c.notes.Add("冲突：你已经有一条进行中的旅程「" + existingOther.title +
                    "」——两条主线会争夺同样的时间，Goal Priority 会在事件里给出取舍成本。");

            return c;
        }

        // ================= 内部规则 =================

        public static GoalTemplate MatchTemplate(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            if (Any(text, "工作", "求职", "面试", "offer", "简历", "投递", "找活")) return ByKey("job");
            if (Any(text, "软件", "产品", "项目", "上线", "发布", "开发", "做一个", "做完")) return ByKey("project");
            if (Any(text, "学", "考", "证书", "课程", "练习", "英语")) return ByKey("study");
            if (Any(text, "边界", "拒绝", "帮忙", "老好人", "被索取", "关系")) return ByKey("boundary");
            if (Any(text, "跑步", "健身", "早睡", "戒", "锻炼", "身体", "作息")) return ByKey("health");
            if (Any(text, "钱", "债", "还款", "存钱", "房租", "财务", "账单")) return ByKey("money");
            if (Any(text, "写", "画", "视频", "创作", "作品", "歌", "文章")) return ByKey("create");
            return null;
        }

        public static GoalTemplate ByKey(string key)
        {
            foreach (var t in Templates) if (t.key == key) return t;
            return null;
        }

        static bool Any(string text, params string[] keys)
        {
            foreach (var k in keys) if (text.Contains(k)) return true;
            return false;
        }

        static bool HasActionableVerb(string text) =>
            Any(text, "做", "完成", "发布", "学", "写", "练", "投", "拿到", "建立", "坚持",
                "开始", "改", "找", "还", "存", "考", "跑", "整理", "上线", "交付");

        static bool HasCompletionHint(string text) =>
            Any(text, "完成", "发布", "拿到", "上线", "交付", "达到", "连续", "次", "个", "周", "月", "天");

        static bool HasExternalResult(string text) =>
            Any(text, "录用", "答复", "回复", "认可", "同意", "喜欢我", "对方", "老板", "客户", "面试通过");

        static List<KeyValuePair<string, WeaknessAxis>> ExtractObstacleKeywords(string text)
        {
            var list = new List<KeyValuePair<string, WeaknessAxis>>();
            if (string.IsNullOrEmpty(text)) return list;
            Add(list, text, new[] { "拖延", "明天", "总是推" }, "拖延与迟迟不开始", WeaknessAxis.Procrastination);
            Add(list, text, new[] { "完美", "准备好", "状态" }, "完美准备而不开始", WeaknessAxis.LowConfidence);
            Add(list, text, new[] { "分心", "手机", "打断", "噪声" }, "注意力被持续拿走", WeaknessAxis.NoiseSensitivity);
            Add(list, text, new[] { "丢脸", "羞耻", "被笑", "评价" }, "怕被评价", WeaknessAxis.Shame);
            Add(list, text, new[] { "失败", "搞砸", "again", "又" }, "失败记忆压制行动", WeaknessAxis.FailureFear);
            Add(list, text, new[] { "帮忙", "拒绝", "答应" }, "无法拒绝导致资源流失", WeaknessAxis.BoundaryConflict);
            Add(list, text, new[] { "不公", "凭什么", "赖账" }, "公平刺痛占据主线", WeaknessAxis.FairnessSensitivity);
            Add(list, text, new[] { "没意义", "为什么", "值得吗" }, "意义焦虑取代行动", WeaknessAxis.WillpowerCollapse);
            return list;
        }

        static void Add(List<KeyValuePair<string, WeaknessAxis>> list, string text,
            string[] keys, string label, WeaknessAxis axis)
        {
            foreach (var k in keys)
                if (text.Contains(k))
                {
                    list.Add(new KeyValuePair<string, WeaknessAxis>(label, axis));
                    return;
                }
        }

        /// <summary>没有可执行下一步时，生成 3 个候选起步动作（方案 3.4 行动性一行）。</summary>
        public static List<string> StarterActionsFor(string text)
        {
            var t = MatchTemplate(text);
            if (t != null)
                return new List<string>
                {
                    "今天花五分钟写下「" + t.milestones[0] + "」需要的第一件事",
                    "今天做一个最小版本，不求做好",
                    "今天找出一个正在挡路的障碍并给它起个名字"
                };
            return new List<string>
            {
                "今天写下这件事的第一步是什么",
                "今天做五分钟，不求做完",
                "今天说出一句「我现在到底卡在哪」"
            };
        }

        static string ActionLabelFor(string milestoneTitle) => "推进：" + milestoneTitle;
    }
}
