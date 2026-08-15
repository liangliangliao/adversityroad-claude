using UnityEngine;
using AdversityRoad.Personalization;

namespace AdversityRoad.Goals
{
    /// <summary>
    /// Fallback Blueprint Library（方案 18.3 最后一条 / 23.3 第 25 条）：
    /// **AI 不可用时游戏仍必须能正常运行**——没有网络、没有 API Key、生成失败或被校验拒绝时，
    /// 这里用本地规则从同一个模块库里组装出一个合法的 AIRequired 章节。
    ///
    /// 它不是"占位符"：产出的蓝图同样绑定里程碑、同样有物理机制、
    /// 同样声明成功条件与失败后果，同样要过 Validator。
    /// </summary>
    public static class FallbackBlueprintLibrary
    {
        /// <summary>每条弱点轴一套本地专属章节骨架（名字/机制/成败条件都不同，保证差异性）。</summary>
        struct Skeleton
        {
            public string name;
            public string[] physical;
            public string success;
            public string failure;
            // —— 场景骨架：本地也要造出一个"这个目标专属的地方"，而不是退回固定关卡 ——
            public string siteKind;
            public string layout;
            public string ambience;
            public string[] roomNames;
            public string[][] roomProps;
            public string[] npcRoles;
            public string[] externalLines;
            public string[] internalLines;
        }

        static Skeleton SkeletonFor(WeaknessAxis axis)
        {
            switch (axis)
            {
                case WeaknessAxis.LowConfidence:
                    return new Skeleton
                    {
                        name = "需求迷宫",
                        physical = new[] { "explore_branching", "timed_actions" },
                        success = "在分叉的需求街区里选出必须做的那条路，并把暂缓项明确挂起",
                        failure = "时间债务增加，下一阶段的可用窗口变窄，但会出现一次重新排期的机会",
                        siteKind = "office_floor", layout = "maze", ambience = "indoor_cold",
                        roomNames = new[] { "需求墙", "暂缓区", "评审角" },
                        roomProps = new[]
                        {
                            new[] { "whiteboard", "desk", "papers" },
                            new[] { "shelf", "crate", "cabinet" },
                            new[] { "table", "chair", "monitor" }
                        },
                        npcRoles = new[] { "colleague", "manager" },
                        externalLines = new[]
                        {
                            "这个也顺手加一下呗。", "范围又不是不能改。",
                            "先做完再想清楚不行吗？", "别人都能同时做三件。"
                        },
                        internalLines = new[]
                        {
                            "是不是我要求太多了。", "再加一点就完美了。",
                            "现在砍掉会不会前功尽弃。"
                        }
                    };
                case WeaknessAxis.FailureFear:
                    return new Skeleton
                    {
                        name = "错误之城",
                        physical = new[] { "source_hunt", "protect_zone" },
                        success = "找到连锁故障的真正源头，并保住已经跑通的稳定区",
                        failure = "连锁故障扩散一格，路线关闭，但解锁一条恢复与复盘路线",
                        siteKind = "server_room", layout = "corridor", ambience = "flicker",
                        roomNames = new[] { "故障区", "稳定区", "回滚台" },
                        roomProps = new[]
                        {
                            new[] { "server_rack", "monitor", "pipe" },
                            new[] { "server_rack", "barrier", "cabinet" },
                            new[] { "desk", "printer", "papers" }
                        },
                        npcRoles = new[] { "colleague", "cleaner" },
                        externalLines = new[]
                        {
                            "又崩了，你到底改了什么。", "这次是不是修不好了。",
                            "一个接一个，看不到头。", "上次也是你说没问题。"
                        },
                        internalLines = new[]
                        {
                            "又来了，我永远弄不好。", "干脆全部重写算了。",
                            "别人不会犯这种错。"
                        }
                    };
                case WeaknessAxis.Procrastination:
                    return new Skeleton
                    {
                        name = "发布前夜",
                        physical = new[] { "timed_actions", "beacon_ignite" },
                        success = "在有限时间内完成关键动作，而不是把时间花在低价值打磨上",
                        failure = "机会窗口滑走，时间推进，但训练与补给节点开放",
                        siteKind = "studio", layout = "rooms", ambience = "night",
                        roomNames = new[] { "工位", "打磨区", "发布台" },
                        roomProps = new[]
                        {
                            new[] { "desk", "monitor", "chair" },
                            new[] { "shelf", "papers", "plant" },
                            new[] { "counter", "sign", "lamp" }
                        },
                        npcRoles = new[] { "neighbor" },
                        externalLines = new[]
                        {
                            "明天再发也一样。", "现在这个状态你敢发？",
                            "再改一版会更好。", "反正也没人等着看。"
                        },
                        internalLines = new[]
                        {
                            "还不够好。", "发出去会被笑吧。", "再等等，等我准备好。"
                        }
                    };
                case WeaknessAxis.BoundaryConflict:
                    return new Skeleton
                    {
                        name = "代价清算所",
                        physical = new[] { "route_cost", "protect_zone" },
                        success = "对每一次索取给出明确回应，并守住自己的资源区",
                        failure = "资源被持续扣款，关系状态变化，但会出现一次重新谈判的机会",
                        siteKind = "recruit_hall", layout = "hall", ambience = "indoor_cold",
                        roomNames = new[] { "请求窗口", "我的资源区", "清算台" },
                        roomProps = new[]
                        {
                            new[] { "counter", "chair", "sign" },
                            new[] { "locker", "barrier", "crate" },
                            new[] { "table", "papers", "monitor" }
                        },
                        npcRoles = new[] { "colleague", "customer", "manager" },
                        externalLines = new[]
                        {
                            "就这一次，帮个忙。", "你人最好了。",
                            "不帮就是不给面子。", "这点小事都不肯？"
                        },
                        internalLines = new[]
                        {
                            "拒绝了会不会太冷漠。", "算了，我扛一下就过去了。",
                            "他们会怎么看我。"
                        }
                    };
                case WeaknessAxis.NoiseSensitivity:
                    return new Skeleton
                    {
                        name = "信号噪声区",
                        physical = new[] { "stealth_observe", "route_cost" },
                        success = "在持续干扰下把注意力收回到真正的目标上并完成推进",
                        failure = "专注被拿走，误锁定低价值目标，但注意力回收点在附近生成",
                        siteKind = "subway", layout = "corridor", ambience = "flicker",
                        roomNames = new[] { "闸机口", "换乘通道", "站台" },
                        roomProps = new[]
                        {
                            new[] { "barrier", "sign", "vending" },
                            new[] { "billboard", "bench", "pipe" },
                            new[] { "bench", "trashbin", "lamp" }
                        },
                        npcRoles = new[] { "passerby", "driver", "guard" },
                        externalLines = new[]
                        {
                            "（有人在看你）", "（身后一阵低语）",
                            "你听见了吧？", "（一声很响的咳嗽）"
                        },
                        internalLines = new[]
                        {
                            "他们是不是在说我。", "我是不是又走错了。",
                            "回头看一眼就好。"
                        }
                    };
                case WeaknessAxis.FairnessSensitivity:
                    return new Skeleton
                    {
                        name = "事实对质台",
                        physical = new[] { "evidence_collect", "boss_phase" },
                        success = "集齐事实碎片并当场对质，把模糊叙事钉在事实上",
                        failure = "争议悬置，公平刺痛升高，但会开放一条成本判断路线",
                        siteKind = "archive", layout = "rooms", ambience = "indoor_cold",
                        roomNames = new[] { "证据柜", "对质台", "旁听席" },
                        roomProps = new[]
                        {
                            new[] { "cabinet", "shelf", "papers" },
                            new[] { "table", "monitor", "sign" },
                            new[] { "bench", "chair", "plant" }
                        },
                        npcRoles = new[] { "clerk", "colleague" },
                        externalLines = new[]
                        {
                            "谁记得当时怎么说的。", "你这是小题大做。",
                            "证据呢？拿出来啊。", "都过去了，还提。"
                        },
                        internalLines = new[]
                        {
                            "是不是我记错了。", "计较这个显得我很小气。",
                            "算了，不值得。"
                        }
                    };
                case WeaknessAxis.Shame:
                    return new Skeleton
                    {
                        name = "评价长廊",
                        physical = new[] { "terrain_climb", "escort_protect" },
                        success = "在被注视的全程里完成动作，不为躲避目光而改变路线",
                        failure = "自尊受损，动作反馈变沉，但镜前恢复点开启",
                        siteKind = "mall", layout = "courtyard", ambience = "day",
                        roomNames = new[] { "中庭", "橱窗前", "扶梯口" },
                        roomProps = new[]
                        {
                            new[] { "bench", "plant", "sign" },
                            new[] { "billboard", "stall", "counter" },
                            new[] { "stairs", "barrier", "trashbin" }
                        },
                        npcRoles = new[] { "customer", "clerk", "passerby" },
                        externalLines = new[]
                        {
                            "（几个人同时看过来）", "就这水平也敢出来。",
                            "你脸红了。", "（有人笑了一声）"
                        },
                        internalLines = new[]
                        {
                            "他们都在看我。", "我是不是很可笑。", "快点走过去就好了。"
                        }
                    };
                case WeaknessAxis.JobAnxiety:
                    return new Skeleton
                    {
                        name = "回音收发室",
                        physical = new[] { "resource_scavenge", "timed_actions" },
                        success = "在沉默反馈中完成约定次数的投递动作，并至少拿到一次真实反馈",
                        failure = "沉默继续，但会解锁新的情报渠道与训练机会",
                        siteKind = "recruit_hall", layout = "rooms", ambience = "indoor_cold",
                        roomNames = new[] { "投递窗口", "等候区", "面试间" },
                        roomProps = new[]
                        {
                            new[] { "counter", "printer", "papers" },
                            new[] { "bench", "vending", "plant" },
                            new[] { "table", "chair", "whiteboard" }
                        },
                        npcRoles = new[] { "applicant", "clerk", "manager" },
                        externalLines = new[]
                        {
                            "简历我们会再看看。", "有消息会通知你。",
                            "今天面完的人很多。", "你这个经历……不太匹配。"
                        },
                        internalLines = new[]
                        {
                            "又是没有回音。", "是不是我根本不行。", "再投也是一样。"
                        }
                    };
                case WeaknessAxis.WillpowerCollapse:
                    return new Skeleton
                    {
                        name = "断电街区",
                        physical = new[] { "resource_scavenge", "route_cost" },
                        success = "从一个取暖点规划到下一个，找到资源与支持并抵达出口",
                        failure = "意志见底被送回最近安全点，恢复变慢但支持节点常驻开放",
                        siteKind = "street_block", layout = "openblock", ambience = "night",
                        roomNames = new[] { "便利店", "取暖点", "公用电话" },
                        roomProps = new[]
                        {
                            new[] { "counter", "vending", "crate" },
                            new[] { "bench", "lamp", "barrier" },
                            new[] { "sign", "trashbin", "papers" }
                        },
                        npcRoles = new[] { "passerby", "clerk" },
                        externalLines = new[]
                        {
                            "撑不下去就别撑了。", "求人有什么用。",
                            "这条路没人会来接你。", "天亮还早着呢。"
                        },
                        internalLines = new[]
                        {
                            "我是不是走不出去了。", "开口求助太丢人。", "再走一段就好。"
                        }
                    };
                default:
                    return new Skeleton
                    {
                        name = "起步空场",
                        physical = new[] { "beacon_ignite", "explore_branching" },
                        success = "点燃分散的行动灯台，把「还没准备好」变成「已经开始」",
                        failure = "行动力流失，路线变长，但五分钟火种台常驻",
                        siteKind = "studio", layout = "hall", ambience = "indoor_warm",
                        roomNames = new[] { "起步台", "拖延角", "计划墙" },
                        roomProps = new[]
                        {
                            new[] { "table", "lamp", "sign" },
                            new[] { "sofa", "trashbin", "papers" },
                            new[] { "whiteboard", "shelf", "chair" }
                        },
                        npcRoles = new[] { "neighbor" },
                        externalLines = new[]
                        {
                            "明天再开始也不迟。", "你现在状态不好。",
                            "先准备充分一点。", "反正也做不完。"
                        },
                        internalLines = new[]
                        {
                            "等我有动力了就开始。", "现在开始已经晚了。", "先歇五分钟。"
                        }
                    };
            }
        }

        /// <summary>
        /// 为一个障碍生成本地专属章节蓝图。
        /// 章节名会带上目标关键词，让同一条轴在不同目标下也长得不一样（差异性要求）。
        /// </summary>
        public static GoalChapterData Build(GoalData goal, GoalObstacle ob, int seedSalt)
        {
            if (goal == null || ob == null) return null;
            var sk = SkeletonFor(ob.axis);
            ChapterModuleLibrary.SuggestEnemies(ob.axis, out var ext, out var inn, out var boss);

            int seed = Mathf.Abs((goal.goalId + ob.obstacleId + seedSalt).GetHashCode());
            var rng = new System.Random(seed);

            string keyword = GoalKeyword(goal.title);
            var bp = new GoalChapterData
            {
                chapterId = "ai_" + goal.goalId + "_" + ob.obstacleId,
                source = ChapterSource.AIRequired,
                linkedGoalId = goal.goalId,
                linkedMilestoneId = ob.linkedMilestoneId,
                chapterName = string.IsNullOrEmpty(keyword) ? sk.name : keyword + "·" + sk.name,
                worldDistrictId = ChapterModuleLibrary.DistrictFor(ob.axis),
                primaryObstacle = ob.obstacleId,
                secondaryObstacle = SecondObstacle(goal, ob),
                bossArchetype = boss.ToString(),
                successCondition = sk.success,
                failureConsequence = sk.failure,
                assemblySeed = seed
            };
            foreach (var m in sk.physical) bp.physicalMechanics.Add(m);
            // 第三个物理机制按种子轮换，保证同一条轴在不同目标下机制组合不同
            var pool = ChapterModuleLibrary.AllMechanicIds(true);
            string extra = pool[rng.Next(pool.Count)];
            if (!bp.physicalMechanics.Contains(extra)) bp.physicalMechanics.Add(extra);

            bp.mentalMechanics.Add(ChapterModuleLibrary.MentalMechanicFor(ob.axis));
            bp.externalEnemies.Add(ext.ToString());
            bp.internalEnemies.Add(inn.ToString());
            bp.site = BuildSite(sk, goal, ob, keyword, rng);
            return bp;
        }

        /// <summary>
        /// 本地场景蓝图：没有网络时照样造出一个"这个目标专属的地方"。
        /// 房间名带上目标关键词，同一条轴在不同目标下也长得不一样。
        /// </summary>
        static SiteBlueprint BuildSite(Skeleton sk, GoalData goal, GoalObstacle ob,
            string keyword, System.Random rng)
        {
            var site = new SiteBlueprint
            {
                siteName = string.IsNullOrEmpty(keyword) ? sk.name : keyword + "·" + sk.name,
                siteKind = sk.siteKind,
                layout = sk.layout,
                ambience = sk.ambience,
                sizeHint = rng.Next(100) < 25 ? "large" : "medium"
            };
            if (site.siteName.Length > 16) site.siteName = site.siteName.Substring(0, 16);

            for (int i = 0; i < sk.roomNames.Length; i++)
            {
                var room = new SiteRoom
                {
                    name = sk.roomNames[i],
                    purpose = i == 0 ? "这一关的核心：" + ob.label : "支线：资源、喘息或岔路",
                    sizeHint = i == 0 ? "large" : "medium"
                };
                if (sk.roomProps != null && i < sk.roomProps.Length)
                    room.props.AddRange(sk.roomProps[i]);
                site.rooms.Add(room);
            }

            if (sk.npcRoles != null)
                foreach (var role in sk.npcRoles)
                    site.npcs.Add(new SiteNpc
                    {
                        roleType = role,
                        count = 1 + rng.Next(2),
                        behavior = rng.Next(2) == 0 ? "wander" : "station",
                        line = ""
                    });

            site.rules.Add(sk.success);
            site.rules.Add("失败不是终点：" + sk.failure);
            site.rules.Add("与目标无关的挑衅可以不接——离开也是一种胜利。");

            if (sk.externalLines != null) site.externalLines.AddRange(sk.externalLines);
            if (sk.internalLines != null) site.internalLines.AddRange(sk.internalLines);

            site.interactables.Add(sk.name + "的关键物");
            return site;
        }

        /// <summary>从目标标题里抽一个短关键词做章节名前缀（不含真实姓名/地址等隐私内容）。</summary>
        static string GoalKeyword(string title)
        {
            if (string.IsNullOrEmpty(title)) return "";
            string t = title.Trim();
            foreach (var w in new[] { "我要", "我想", "希望", "打算", "准备" })
                if (t.StartsWith(w)) t = t.Substring(w.Length);
            if (t.Length > 6) t = t.Substring(0, 6);
            return t;
        }

        static string SecondObstacle(GoalData goal, GoalObstacle primary)
        {
            foreach (var o in goal.obstacles)
                if (o != primary && !o.removed) return o.obstacleId;
            return "";
        }
    }
}
