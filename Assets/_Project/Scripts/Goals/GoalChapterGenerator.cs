using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using AdversityRoad.Core;

namespace AdversityRoad.Goals
{
    /// <summary>LLM 返回的一批章节蓝图（JsonUtility 需要一个容器类）。</summary>
    [System.Serializable]
    class BlueprintBatch
    {
        public GoalChapterData[] items;
    }

    /// <summary>
    /// Goal Chapter Generator（方案第 5 章 · AIRequired 章节强制生成机制）。
    ///
    /// 为什么必须强制存在：如果所有目标都只能匹配"拖延沼泽、责任法院、刺激街道"等固定模块，
    /// 系统只做到了心理标签推荐，做不到目标个性化。AIRequired 章节的职责，是捕捉
    /// **这个目标特有的行动链、资源瓶颈、现实环境和失败方式**。
    ///
    /// 三层 Prompt（方案 18.2）：Global Value / Goal-Chapter / Output Contract。
    /// AI 只输出 Structured Blueprint；校验通过后由 ProceduralQuestAssembler 确定性组装。
    /// 云端不可用 → 本地 Fallback Library，旅程一样能进入"正式生成完成"。
    /// </summary>
    public class GoalChapterGenerator : MonoBehaviour
    {
        public static GoalChapterGenerator Instance { get; private set; }

        /// <summary>
        /// 一条旅程至少要有这么多个 AI 专属关卡（强制下限）。
        ///
        /// 定成 5 不是拍脑袋：玩家写下一个目标，期待的是"通往它的路上有一连串要打的东西"。
        /// 只给一两关的话，屏幕上写着「AI 专属 2」，玩家的实际体感是"我写了目标，
        /// 然后什么也没发生"——目标驱动这件事就没成立。经典 24 关只是**附加可选**，
        /// 不能拿来充数：它们不是为这个目标造的。
        /// </summary>
        public const int MinAiChapters = 5;
        public const int MaxAiChapters = 8;

        /// <summary>每次云端请求生成几章（分批，避免长返回被截断或超时）。</summary>
        const int BatchSize = 2;

        bool _busy;

        public static void Ensure()
        {
            if (Instance != null) return;
            var go = new GameObject("GoalChapterGenerator");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<GoalChapterGenerator>();
        }

        static bool CloudReady
        {
            get
            {
                var cfg = AIPromptConfig.Load();
                return cfg.useCloud && !string.IsNullOrEmpty(cfg.apiKey);
            }
        }

        /// <summary>
        /// 为一条目标补齐旅程章节：Legacy（按相关性插入）+ UserPreset（玩家自己加）+ AIRequired（强制）。
        /// 云端可用时先请求 LLM；不可用或校验失败则用 Fallback——无论哪条路，都必须产出 ≥1 个合法 AI 章节。
        /// </summary>
        public void BuildJourneyChapters(GoalData goal)
        {
            if (goal == null) return;
            EnsureLegacyChapters(goal);
            // 障碍多就多给几关，但无论如何不低于强制下限
            int want = Mathf.Clamp(CountOpenObstacles(goal), MinAiChapters, MaxAiChapters);

            if (CloudReady && !_busy) StartCoroutine(GenerateRemote(goal, want));
            else FillWithFallback(goal, want, CloudReady ? "生成进行中" : "云端未配置");
        }

        /// <summary>手动重新生成（Goal 面板按钮）：清掉未通关的 AI 章节后重来。</summary>
        public void Regenerate(GoalData goal)
        {
            if (goal == null) return;
            for (int i = goal.chapters.Count - 1; i >= 0; i--)
                if (goal.chapters[i].source == ChapterSource.AIRequired && !goal.chapters[i].cleared)
                    goal.chapters.RemoveAt(i);
            goal.aiGeneratedChapterIds.Clear();
            foreach (var c in goal.chapters)
                if (c.source == ChapterSource.AIRequired) goal.aiGeneratedChapterIds.Add(c.chapterId);
            BuildJourneyChapters(goal);
        }

        // ================= Legacy 插入 =================

        /// <summary>V1 七大章节按目标相关性动态插入（资产全部保留，不废弃）。</summary>
        public static void EnsureLegacyChapters(GoalData goal)
        {
            var arcs = LegacyChapterCatalog.SelectFor(goal, 4);
            foreach (var arc in arcs)
            {
                GoalObstacle match = null;
                foreach (var ob in goal.obstacles)
                    if (ob.axis == arc.axis) { match = ob; break; }
                var bp = LegacyChapterCatalog.ToBlueprint(arc, goal, match);
                if (goal.FindChapter(bp.chapterId) != null) continue;
                goal.chapters.Add(bp);
                goal.legacyChapterIds.Add(bp.chapterId);
            }
            GoalOS.Save();
        }

        /// <summary>玩家预设章节（方案 5.1 UserPreset / 验收标准 12）：可编辑、可关闭、可调强度。</summary>
        public static GoalChapterData AddUserPresetChapter(GoalData goal, string name,
            string obstacleLabel, string districtId)
        {
            if (goal == null) return null;
            name = (name ?? "").Trim();
            if (name.Length == 0) return null;
            if (name.Length > 24) name = name.Substring(0, 24);

            var ob = new GoalObstacle
            {
                obstacleId = "ubo" + (goal.obstacles.Count + 1),
                label = string.IsNullOrEmpty(obstacleLabel) ? name : obstacleLabel,
                source = ObstacleSource.UserPreset,
                axis = Personalization.WeaknessAxis.Procrastination,
                linkedMilestoneId = goal.CurrentMilestone() != null
                    ? goal.CurrentMilestone().milestoneId
                    : (goal.milestones.Count > 0 ? goal.milestones[0].milestoneId : "")
            };
            goal.obstacles.Add(ob);

            var bp = FallbackBlueprintLibrary.Build(goal, ob, 7717);
            if (bp == null) return null;
            bp.chapterId = "user_" + goal.goalId + "_" + ob.obstacleId;
            bp.source = ChapterSource.UserPreset;
            bp.chapterName = name;
            if (ChapterModuleLibrary.IsDistrict(districtId)) bp.worldDistrictId = districtId;

            if (ChapterBlueprintValidator.Validate(bp, goal, out string reason))
            {
                goal.chapters.Add(bp);
                goal.userChapterIds.Add(bp.chapterId);
                GoalJourneyGraph.Build(goal);
                GoalOS.Save();
                GameEvents.RaiseSubtitle("玩家预设章节已加入旅程：「" + bp.chapterName + "」");
                return bp;
            }
            goal.obstacles.Remove(ob);
            GameEvents.RaiseSubtitle("预设章节未通过校验：" + reason);
            return null;
        }

        // ================= Fallback =================

        void FillWithFallback(GoalData goal, int want, string why)
        {
            // 云端还在路上时铺的这几关是**占位**：等真正的 AI 章节回来要让位。
            bool asPlaceholder = why == "生成进行中";
            want = Mathf.Clamp(want, MinAiChapters, MaxAiChapters);
            int made = 0;
            int salt = 0;

            // 第一轮：一条障碍一关，这是最贴合玩家目标的排布
            foreach (var ob in goal.obstacles)
            {
                if (CountAiChapters(goal) >= want) break;
                if (ob.removed) continue;
                if (HasChapterFor(goal, ob.obstacleId)) continue;
                if (TryAddFallback(goal, ob, salt++, asPlaceholder)) made++;
            }

            // 第二轮：障碍用完了但还没凑够下限——给已有障碍再造一个不同阶段的关卡。
            // 「障碍不够」不能成为"玩家写了目标却只拿到两关"的理由：
            // 同一条障碍在旅程前段和后段的形态本来就不一样（刚开始的拖延和
            // 快交付时的拖延不是同一件事），拆成两关是合理的，不是灌水。
            for (int pass = 0; pass < 3 && CountAiChapters(goal) < want; pass++)
                foreach (var ob in goal.obstacles)
                {
                    if (CountAiChapters(goal) >= want) break;
                    if (ob.removed) continue;
                    if (TryAddFallback(goal, ob, salt++, asPlaceholder)) made++;
                }

            FinishJourney(goal, made, "本地 Fallback（" + why + "）");
        }

        /// <summary>造一个 Fallback 章节并入库；被拒时记日志并返回 false。</summary>
        bool TryAddFallback(GoalData goal, GoalObstacle ob, int salt, bool placeholder = false)
        {
            var bp = FallbackBlueprintLibrary.Build(goal, ob, salt);
            if (bp == null) return false;

            // 同一条障碍可能出多关，章节 id 必须带 salt 才不会互相覆盖
            bp.chapterId = "ai_" + goal.goalId + "_" + ob.obstacleId + "_" + salt;
            if (goal.FindChapter(bp.chapterId) != null) return false;

            if (!ChapterBlueprintValidator.Validate(bp, goal, out string reason))
            {
                CloudDialogueService.AddLog("Fallback 章节被拒：" + bp.chapterName + " —— " + reason);
                return false;
            }
            bp.placeholder = placeholder;
            goal.chapters.Add(bp);
            goal.aiGeneratedChapterIds.Add(bp.chapterId);
            return true;
        }

        void FinishJourney(GoalData goal, int made, string sourceLabel)
        {
            goal.journeyReady = goal.HasAiChapter();
            GoalJourneyGraph.Build(goal);
            GoalOS.Save();

            CloudDialogueService.AddLog("旅程章节装配完成：" + sourceLabel + " 新增 " + made +
                " 个 AI 专属章节；Legacy " + goal.legacyChapterIds.Count +
                "、玩家预设 " + goal.userChapterIds.Count +
                "；journeyReady=" + goal.journeyReady);

            if (!goal.journeyReady)
                GameEvents.RaiseSubtitle("⚠ 这条旅程还没有 AI 专属章节，无法进入「正式生成完成」状态。");
            else if (made > 0)
                GameEvents.RaiseSubtitle("旅程已生成：" + made + " 个只属于这个目标的专属章节已落进开放世界。");
        }

        // ================= 云端生成 =================

        IEnumerator GenerateRemote(GoalData goal, int want)
        {
            _busy = true;
            var cfg = AIPromptConfig.Load();
            string url, model;
            switch (cfg.provider)
            {
                case "deepseek":
                    url = "https://api.deepseek.com/chat/completions";
                    model = string.IsNullOrEmpty(cfg.model) ? "deepseek-chat" : cfg.model;
                    break;
                case "edenai":
                    url = "https://api.edenai.run/v2/llm/chat";
                    model = string.IsNullOrEmpty(cfg.model) ? "openai/gpt-4o-mini" : cfg.model;
                    break;
                default:
                    url = "https://openrouter.ai/api/v1/chat/completions";
                    model = string.IsNullOrEmpty(cfg.model) ? "deepseek/deepseek-chat" : cfg.model;
                    break;
            }

            int accepted = 0, rejected = 0, replaced = 0;

            // 分批请求：一次只要 BatchSize 章。
            //
            // 一次要五章、每章还带整套 site，返回动辄一万字——DeepSeek 要二十五秒，
            // 换成 qwen 直接超时（实机日志：70015ms Request timeout，重试又跑了 173 秒）。
            // 而且越长越容易被截断。拆成小批之后每次返回只有两章，
            // 单次两三千字、十几秒回来，超时与截断这两类失败一起消失。
            for (int done = 0; done < want; done += BatchSize)
            {
                int askFor = Mathf.Min(BatchSize, want - done);
                string body = "{\"model\":\"" + Esc(model) + "\",\"messages\":[" +
                    "{\"role\":\"system\",\"content\":\"" + Esc(GlobalValuePrompt() + OutputContractPrompt()) + "\"}," +
                    "{\"role\":\"user\",\"content\":\"" + Esc(GoalChapterPrompt(goal, askFor)) + "\"}]," +
                    "\"max_tokens\":4000,\"temperature\":0.8}";

                CloudDialogueService.AddLog("章节生成请求 " + cfg.provider + "/" + model +
                    " → 目标「" + goal.title + "」第 " + (done / BatchSize + 1) + " 批 × " + askFor + " 章");
                float startAt = Time.realtimeSinceStartup;

                string content = null;
                // 连接层瞬时失败（HTTP0）与超时都值得重试一次——一失败就整批退回本地库，
                // 正是玩家看到"AI 没起作用"的直接原因。
                for (int attempt = 0; attempt < 2 && string.IsNullOrEmpty(content); attempt++)
                {
                    if (attempt > 0)
                    {
                        CloudDialogueService.AddLog("本批重试第 " + attempt + " 次…");
                        yield return new WaitForSecondsRealtime(1.5f);
                    }
                    using (var req = new UnityWebRequest(url, "POST"))
                    {
                        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                        req.downloadHandler = new DownloadHandlerBuffer();
                        req.SetRequestHeader("Content-Type", "application/json");
                        req.SetRequestHeader("Authorization", "Bearer " + cfg.apiKey.Trim());
                        // 70 秒对慢模型不够：qwen 那次正好卡在 70015ms 被判超时。
                        // 分批之后单次本该更快，但仍给足余量，宁可等也别白跑一趟。
                        req.timeout = 150;
                        yield return req.SendWebRequest();

                        float ms = (Time.realtimeSinceStartup - startAt) * 1000f;
                        if (req.result == UnityWebRequest.Result.Success)
                        {
                            content = CloudDialogueService.ExtractContent(req.downloadHandler.text);
                            string head = string.IsNullOrEmpty(content) ? "(空)"
                                : content.Substring(0, Mathf.Min(60, content.Length)).Replace("\n", " ");
                            CloudDialogueService.AddLog("本批返回 " + Mathf.RoundToInt(ms) + "ms · 长度 " +
                                (content == null ? 0 : content.Length) + " · 开头「" + head + "」");
                        }
                        else
                        {
                            CloudDialogueService.AddLog("本批失败 " + Mathf.RoundToInt(ms) + "ms HTTP" +
                                req.responseCode + " " + req.error + " · 请求体 " + body.Length + " 字节" +
                                (attempt == 0 ? " → 重试" : " → 放弃本批"));
                        }
                    }
                }

                foreach (var bp in Parse(content))
                {
                    // 名额满了不代表该丢——先看看占着位子的是不是本地占位关。
                    if (CountAiChapters(goal) >= want)
                    {
                        if (!DropOnePlaceholder(goal)) break;
                        replaced++;
                    }

                    bp.source = ChapterSource.AIRequired;
                    bp.chapterId = "ai_" + goal.goalId + "_" + (goal.aiGeneratedChapterIds.Count + accepted + 1);
                    // 模型很可能把好几章都挂到同一条障碍上：先换一条还没被覆盖的障碍，
                    // 真的覆盖满了才丢。
                    if (HasChapterFor(goal, bp.primaryObstacle)) Rebind(goal, bp);

                    if (ChapterBlueprintValidator.Validate(bp, goal, out string reason))
                    {
                        if (HasChapterFor(goal, bp.primaryObstacle)) { rejected++; continue; }
                        goal.chapters.Add(bp);
                        goal.aiGeneratedChapterIds.Add(bp.chapterId);
                        accepted++;
                    }
                    else
                    {
                        rejected++;
                        CloudDialogueService.AddLog("章节被校验拒绝：" +
                            (string.IsNullOrEmpty(bp.chapterName) ? "(无名)" : bp.chapterName) + " —— " + reason);
                    }
                }
            }

            CloudDialogueService.AddLog("章节校验：通过 " + accepted + "、拒绝 " + rejected +
                "（其中替换本地占位 " + replaced + " 关）");

            _busy = false;
            // 强制条款：无论 AI 给了什么，旅程都必须拿到至少一个合法 AI 专属章节
            if (CountAiChapters(goal) < Mathf.Max(MinAiChapters, want))
                FillWithFallback(goal, want, accepted > 0 ? "补足 AI 生成缺口" : "AI 返回不可用");
            else
                FinishJourney(goal, accepted, "云端 " + cfg.provider);
        }

        // ================= 三层 Prompt（方案 18.2） =================

        /// <summary>第一层 Global Value Prompt：产品价值、目标优先、主动性、安全、非诊断、非报复。</summary>
        static string GlobalValuePrompt()
        {
            return
                "你是《逆境之路》的目标专属章节设计器。产品价值观（必须遵守）：" +
                "1.目标优先——每个章节都必须真的在阻挡玩家自己写下的某个里程碑，不能随便套一个心理主题；" +
                "2.主动性——玩家可以攻击、撤退、绕路、训练、协商、保护资源，章节不能只有一条唯一解；" +
                "3.非诊断——绝不推断或标注玩家的人格、精神状态、疾病，只描述可玩的情境与可观察行为；" +
                "4.非报复——不得以伤害、犯罪、报复、羞辱真实第三方为内容；" +
                "5.不复刻现实隐私——不得出现真实姓名、住址、联系方式、单位名或可识别的第三方信息；" +
                "6.不生成现实操控话术、创伤细节或自伤内容；台词短、可玩、能在动作战斗中即时读完；" +
                "7.困难要足够暴露策略问题、足够公平以允许学习、足够可控以允许退出。";
        }

        /// <summary>第三层 Output Contract：严格 JSON / Schema，只允许引用已批准模块库。</summary>
        static string OutputContractPrompt()
        {
            var sb = new StringBuilder();
            sb.Append("输出契约（违反即被系统拒绝）：只输出一个 JSON 数组，不要任何解释文字或代码块标记。");
            sb.Append("每个元素字段：chapterName(不超过12个汉字)、linkedMilestoneId、primaryObstacle、");
            sb.Append("secondaryObstacle、worldDistrictId、externalEnemies(数组)、internalEnemies(数组)、");
            sb.Append("bossArchetype、physicalMechanics(数组)、mentalMechanics(数组)、successCondition、");
            sb.Append("failureConsequence、safetyTags(数组)、assemblySeed(整数)、enemyPlan(数组)、site(对象)。");
            // 说清楚 safetyTags 要填什么：模型默认会填「无报复」「不含自伤」这种**声明**，
            // 那是在回答"我避开了什么"，而这个字段问的是"这一章涉及什么主题"。
            sb.Append("safetyTags 填这一章**实际涉及**的主题词（例：求职压力、自我怀疑、时间紧迫），");
            sb.Append("用来和玩家的禁用主题比对；不要填「无XX」「不含XX」这类声明式内容。");

            // —— site：这一章自己的那个地方（V2.0 的关键，不能省） ——
            sb.Append("site 字段（必填，决定这一章会被现场建成什么样的场景）：");
            sb.Append("siteName(场景名,不超过8个汉字)、siteKind、layout、ambience、sizeHint(small/medium/large)、");
            // 篇幅直接决定这次返回会不会被截断——五章各写满五个房间、六句台词，
            // 很容易超出额度，然后整批解析不出来（日志上表现为"通过 0、拒绝 0"）。
            // 房间与台词各砍一半，信息密度不变，返回长度减一大截。
            sb.Append("rooms(数组,2-3个,每个含 name(不超过6个汉字)/purpose(不超过12字)/sizeHint/props(数组,2-4个))、");
            sb.Append("npcs(数组,1-3个,每个含 roleType/count(1-3)/behavior(wander|station|patrol)/line(一句非攻击性的环境台词))、");
            sb.Append("rules(数组,2-3条,用玩家能读懂的一句话说清这一关怎么玩)、");
            sb.Append("externalLines(数组,3句,敌人会喊的压力台词,每句不超过18字)、");
            sb.Append("internalLines(数组,3句,玩家脑内回声,每句不超过18字)、");
            sb.Append("interactables(数组,场景里的关键可交互物名称)。");
            sb.Append("siteKind 只能取：")
              .Append(string.Join("/", SiteKitCatalog.AllKindIds().ToArray())).Append("。");
            sb.Append("layout 只能取：").Append(string.Join("/", SiteKitCatalog.Layouts)).Append("。");
            sb.Append("ambience 只能取：").Append(string.Join("/", SiteKitCatalog.Ambiences)).Append("。");
            sb.Append("props 只能取：").Append(string.Join("/", SiteKitCatalog.Props)).Append("。");
            sb.Append("roleType 只能取：").Append(string.Join("/", SiteKitCatalog.NpcRoles)).Append("。");
            sb.Append("台词必须是虚构的压力表达，不得包含现实操控教程、真实姓名、住址、联系方式，");
            sb.Append("不得出现报复、羞辱特定真实个体、自伤或仇恨内容。");
            sb.Append("worldDistrictId 只能取：").Append(string.Join("/", ChapterModuleLibrary.AllDistrictIds().ToArray())).Append("。");
            sb.Append("physicalMechanics 只能取：")
              .Append(string.Join("/", ChapterModuleLibrary.AllMechanicIds(true).ToArray())).Append("，且至少一个。");
            sb.Append("mentalMechanics 只能取：")
              .Append(string.Join("/", ChapterModuleLibrary.AllMechanicIds(false).ToArray())).Append("。");
            // 这份清单以前只在契约里写了一句"只能使用给定的敌人类型英文名"，
            // 却从来没有把清单**给**出去——模型只能自己编（「截止倒计时」「便签风暴」），
            // 然后在校验时被判"不在已批准敌人库中"整章丢弃。
            // 说了"给定"就必须真的给。
            sb.Append("externalEnemies / internalEnemies / bossArchetype 只能从下面这份清单里挑，");
            sb.Append("必须逐字使用英文名（括号内是它的中文含义，仅供你理解，不要填中文）：");
            sb.Append(string.Join("、", ChapterModuleLibrary.AllEnemyNames().ToArray())).Append("。");
            sb.Append("externalEnemies 填 1-3 个，internalEnemies 填 1-2 个，bossArchetype 填 1 个。");
            // 敌人编成也交给模型：谁、几个、放门口还是深处、守点还是巡逻。
            // 只给三个扁平字段的话，每一关的战斗排布都是引擎按同一套规则摆的，
            // "这一关有它自己的打法"就无从谈起。坐标仍然不给——placement 只说层次。
            sb.Append("enemyPlan(数组,3-4 条)：每条含 enemyType(同上清单里的英文名)、");
            sb.Append("tier(minion|standard|elite|chief)、count(1-3)、");
            sb.Append("placement(entrance 门口|middle 中段|deep 深处)、role(guard 守点|patrol 巡逻|ambush 伏击)。");
            sb.Append("必须**恰好有一条 tier=chief 且 placement=deep**（它就是这一关的首领与通关条件），");
            sb.Append("并且**至少有一条 placement=entrance**——玩家一进场就该看得见要打的东西。");
            // 公平边界：人数上限是引擎的规矩，不是模型的创作空间。写进提示词是为了让它
            // 一开始就把强度做进"谁"和"怎么摆"里，而不是靠堆人头——堆出来也会被削掉。
            sb.Append("全场敌人总数(所有 count 相加，含首领)**不得超过 6**；");
            sb.Append("难度靠 tier 与站位层次体现，不靠人多——一次围上来太多个是不公平的设计。");
            sb.Append("编成要贴着这一关的阻力：被围观就多而弱、被一个人挡死就少而强。");
            sb.Append("不得输出任何代码、Prefab 名、资源路径或坐标——世界组装由引擎按 assemblySeed 确定性完成。");
            return sb.ToString();
        }

        /// <summary>第二层 Goal/Chapter Prompt：Goal Model、里程碑、障碍、区域、强度、禁用主题。</summary>
        static string GoalChapterPrompt(GoalData goal, int want)
        {
            var sb = new StringBuilder();
            sb.Append("为下面这个目标生成 ").Append(want).Append(" 个只属于它的专属章节。");
            sb.Append("目标：").Append(goal.title).Append("。");
            if (!string.IsNullOrEmpty(goal.why)) sb.Append("动机：").Append(goal.why).Append("。");
            if (!string.IsNullOrEmpty(goal.successCondition))
                sb.Append("完成条件：").Append(goal.successCondition).Append("。");
            sb.Append("当前状态：").Append(goal.currentState).Append("；目标状态：").Append(goal.targetState).Append("。");

            sb.Append("里程碑(id:标题)：");
            foreach (var m in goal.milestones)
                sb.Append(m.milestoneId).Append(':').Append(m.title).Append(m.done ? "(已完成) " : " ");

            sb.Append("。尚未消除的障碍(id:描述)：");
            foreach (var o in goal.obstacles)
                if (!o.removed)
                    sb.Append(o.obstacleId).Append(':').Append(o.label).Append(' ');

            sb.Append("。已经存在的章节（不要重复它们的主障碍与机制组合）：");
            foreach (var c in goal.chapters)
                sb.Append(c.chapterName).Append('(').Append(c.primaryObstacle).Append(") ");

            sb.Append("。强度设置：").Append(IntensityLabel(goal.intensity));
            sb.Append("；现实贴近度：").Append(RealismLabel(goal.realism));
            if (goal.safetyTags.Count > 0)
                sb.Append("；玩家禁用主题（绝对不能出现）：").Append(string.Join("、", goal.safetyTags.ToArray()));

            sb.Append("。每个章节必须捕捉这个目标特有的行动链、资源瓶颈、现实环境和失败方式，");
            sb.Append("而不是通用的心理标签；linkedMilestoneId 与 primaryObstacle 必须使用上面给出的 id。");
            sb.Append("site 要描述一个**这个目标专属的真实生活场景**：");
            sb.Append("如果目标是发布软件，那可能是堆满需求便签的工位区或凌晨的机房；");
            sb.Append("如果目标是找工作，那可能是招聘大厅或一间只有一张桌子的面试室；");
            sb.Append("房间名、道具、NPC 角色类型、环境台词都要贴着这个目标写，");
            sb.Append("让玩家一走进去就认得出「这是我那件事」。");

            // 场所多样性：不给这条约束，模型会把五关全写成办公层/会议室，
            // 玩家看到的就是"换了名字的同一个房间"。阻力发生在哪里本身就是内容的一部分。
            sb.Append("【场所必须各不相同】这 ").Append(want).Append(" 关的 siteKind 不许重复，");
            sb.Append("且**至少两关发生在户外开阔地带**（street_block/market/rooftop/park/crossroad/alley）。");
            sb.Append("按'这条阻力最可信地发生在哪里'来选：面试与评价多在室内，");
            sb.Append("而等待、奔波、被围观、下决心这类更适合街道、天台、路口、公园。");
            sb.Append("户外场景请把 sizeHint 填 large、layout 用 openblock 或 courtyard，");
            sb.Append("ambience 用 day/dusk/night/rain 而不是 indoor_ 开头的那两个。");
            return sb.ToString();
        }

        static string IntensityLabel(MentalIntensity i)
        {
            switch (i)
            {
                case MentalIntensity.Light: return "轻度（压力低、恢复快、心理攻击稀疏）";
                case MentalIntensity.HighPressure: return "高压（多轴叠加，但仍必须公平可学）";
                default: return "标准";
            }
        }

        static string RealismLabel(RealityCloseness r)
        {
            switch (r)
            {
                case RealityCloseness.Abstract: return "低（象征化空间为主）";
                case RealityCloseness.Close: return "高（贴近真实城市生活场景）";
                default: return "中（现实场景 + 象征化覆盖）";
            }
        }

        // ================= 解析与工具 =================

        static List<GoalChapterData> Parse(string content)
        {
            var list = new List<GoalChapterData>();
            if (string.IsNullOrEmpty(content)) return list;
            int start = content.IndexOf('[');
            if (start < 0) return list;
            int end = content.LastIndexOf(']');

            // 截断得厉害时可能连数组的收尾括号都没有——那就直接进抢救分支，
            // 而不是像以前那样一句 return 把整批答复扔掉
            if (end > start)
            {
                try
                {
                    var batch = JsonUtility.FromJson<BlueprintBatch>(
                        "{\"items\":" + content.Substring(start, end - start + 1) + "}");
                    if (batch != null && batch.items != null)
                        foreach (var b in batch.items)
                            if (b != null) list.Add(Normalize(b));
                }
                catch
                {
                    CloudDialogueService.AddLog("章节解析失败：整段不是合法 JSON 数组，改为逐章抢救");
                }
            }

            // 整段解析失败最常见的原因是**被截断**：最后一章写到一半就没了，
            // 于是 LastIndexOf(']') 找到的其实是某个 props 数组的收尾括号。
            // 这时候前面几章往往是完整的——逐个抠出来能救回来的就救，
            // 总比"模型答了五章、玩家一章没拿到"强。
            if (list.Count == 0) list = Salvage(content, start);
            return list;
        }

        /// <summary>从可能被截断的数组里逐个抠出**完整**的顶层对象。</summary>
        static List<GoalChapterData> Salvage(string content, int arrayStart)
        {
            var list = new List<GoalChapterData>();
            if (arrayStart < 0) return list;

            int depth = 0, objStart = -1;
            bool inString = false, escaped = false;

            for (int i = arrayStart; i < content.Length; i++)
            {
                char c = content[i];

                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') { inString = true; continue; }

                if (c == '{')
                {
                    if (depth == 0) objStart = i;
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth != 0 || objStart < 0) continue;
                    try
                    {
                        var bp = JsonUtility.FromJson<GoalChapterData>(
                            content.Substring(objStart, i - objStart + 1));
                        if (bp != null) list.Add(Normalize(bp));
                    }
                    catch { /* 这一章救不回来，继续看下一章 */ }
                    objStart = -1;
                }
            }

            if (list.Count > 0)
                CloudDialogueService.AddLog("逐章抢救成功：从截断的返回里救回 " + list.Count + " 章");
            return list;
        }

        /// <summary>JsonUtility 对缺失的 List 字段会给 null，这里统一补成空列表。</summary>
        static GoalChapterData Normalize(GoalChapterData b)
        {
            if (b.externalEnemies == null) b.externalEnemies = new List<string>();
            if (b.internalEnemies == null) b.internalEnemies = new List<string>();
            if (b.physicalMechanics == null) b.physicalMechanics = new List<string>();
            if (b.mentalMechanics == null) b.mentalMechanics = new List<string>();
            if (b.safetyTags == null) b.safetyTags = new List<string>();
            if (b.site == null) b.site = new SiteBlueprint();
            b.cleared = false;
            b.attempts = 0;
            b.legacyZoneIndex = -1;
            return b;
        }

        static int CountAiChapters(GoalData goal)
        {
            int n = 0;
            foreach (var c in goal.chapters)
                if (c.source == ChapterSource.AIRequired && c.validated) n++;
            return n;
        }

        static int CountOpenObstacles(GoalData goal)
        {
            int n = 0;
            foreach (var o in goal.obstacles) if (!o.removed) n++;
            return Mathf.Max(n, 1);
        }

        /// <summary>
        /// 腾一个名额出来：丢掉一关**还没通关的本地占位**。
        /// 找不到可丢的（都是玩家已经打过的、或都是真 AI 章节）就返回 false，此时才该停。
        /// </summary>
        static bool DropOnePlaceholder(GoalData goal)
        {
            for (int i = goal.chapters.Count - 1; i >= 0; i--)
            {
                var c = goal.chapters[i];
                if (!c.placeholder || c.cleared || c.source != ChapterSource.AIRequired) continue;
                // 玩家此刻正站在这一关里就不能丢：连章节数据带场景一起抽走，
                // 等于把地板从他脚下拆了——他会掉进虚空、再被兜底送去别的区。
                if (OpenWorld.SiteGate.InsideSite &&
                    OpenWorld.SiteGate.InsideChapterId == c.chapterId) continue;
                // 占位关可能已经在世界里建出来了，一并收走，免得留下一处没人认领的场景
                OpenWorld.ProceduralQuestAssembler.DespawnChapter(c.chapterId);
                goal.aiGeneratedChapterIds.Remove(c.chapterId);
                goal.chapters.RemoveAt(i);
                return true;
            }
            return false;
        }

        /// <summary>把这一章改挂到还没有关卡的那条障碍上（连里程碑一起改，免得两者对不上）。</summary>
        static void Rebind(GoalData goal, GoalChapterData bp)
        {
            foreach (var o in goal.obstacles)
            {
                if (o.removed || HasChapterFor(goal, o.obstacleId)) continue;
                bp.primaryObstacle = o.obstacleId;
                if (!string.IsNullOrEmpty(o.linkedMilestoneId))
                    bp.linkedMilestoneId = o.linkedMilestoneId;
                return;
            }
        }

        static bool HasChapterFor(GoalData goal, string obstacleId)
        {
            if (string.IsNullOrEmpty(obstacleId)) return false;
            foreach (var c in goal.chapters)
                if (c.source == ChapterSource.AIRequired && c.primaryObstacle == obstacleId) return true;
            return false;
        }

        static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "").Replace("\t", " ");
        }
    }
}
