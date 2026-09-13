using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.AI;
using AdversityRoad.Goals;
using AdversityRoad.Personalization;

namespace AdversityRoad.InternalOS
{
    /// <summary>
    /// 把一关的灰盒蓝图翻译成 SiteBlueprint，交给现成的 <c>SiteBuilder</c> 建出来。
    ///
    /// 【为什么不再写一个建造器】
    /// SiteBuilder 已经能把"语义描述"建成一个真的能走进去的地方：地面、边界、
    /// 标志物、高低差、天气、房间、道具、NPC、敌人落点、NavMesh 烘焙全都在里面，
    /// 并且按 assemblySeed 可复现。90 关需要的不是第二套几何代码，
    /// 而是一层**从 PRD 词汇到已批准词表的翻译**——那正是这个文件。
    ///
    /// 【翻译时坚持的两条】
    /// ① 只输出已批准 id：siteKind/layout/surface/boundary/landmark/prop 全部查表，
    ///    查不到就回退同类默认值，绝不凭空造词——和 AI 章节受同一套约束。
    /// ② 坐标一个都不给：PRD 里的"42m×18m"只用来决定 sizeHint 与高低差，
    ///    真正的落点仍由引擎在烘焙好的导航面上算。
    /// </summary>
    public static class InternalSiteComposer
    {
        /// <summary>World Kit → 已批准的场景类型。</summary>
        public static string SiteKindOf(string worldKitId)
        {
            switch (worldKitId)
            {
                case "WK01": return "apartment";
                case "WK02": return "stairwell";
                case "WK03": return "street_block";
                case "WK04": return "subway";
                case "WK05": return "office_floor";
                case "WK06": return "recruit_hall";
                // WK07 是"商场、广场、店铺、咖啡厅"——广场是户外的。
                // 原来一律映射成 mall（室内中庭），9 关全变成了封闭盒子。
                case "WK07": return "market";
                case "WK08": return "library_room";
                case "WK09": return "studio";
                case "WK10": return "waiting_area";
                case "WK11": return "park";
                // WK02 楼道确实是室内，但它的空间语言是"电梯、楼梯、长廊、消防通道"，
                // 不是一间屋子——布局那一层会把它走成 corridor。
                case "WK12": return "warehouse";
                case "WK13": return "alley";
                // WK14 InnerCore 是"可变几何、断桥、悬浮城市、镜面"。
                // 已批准词表里最接近的是废弃楼：空、结构外露、可以做断裂与错层。
                case "WK14": return "abandoned";
                default: return "office_floor";
            }
        }

        static string PaletteOf(string worldKitId)
        {
            switch (worldKitId)
            {
                case "WK01": case "WK02": return "warm_wood";
                case "WK03": case "WK04": return "concrete_gray";
                case "WK05": case "WK06": return "office_blue";
                case "WK07": return "neon_night";
                case "WK08": return "paper_archive";
                case "WK09": return "steel_cold";
                case "WK10": return "clinic_white";
                case "WK11": return "moss_green";
                case "WK12": return "rust_industrial";
                case "WK13": return "sunset_brick";
                case "WK14": return "deep_violet";
                default: return "concrete_gray";
            }
        }

        static string SurfaceOf(string worldKitId)
        {
            switch (worldKitId)
            {
                case "WK01": case "WK09": return "wood";
                case "WK02": case "WK05": case "WK06": case "WK08": case "WK10": return "carpet";
                case "WK03": return "asphalt";
                case "WK04": case "WK07": return "tile";
                case "WK11": return "grass";
                case "WK12": return "grate";
                case "WK13": return "gravel";
                case "WK14": return "concrete";
                default: return "concrete";
            }
        }

        static string BoundaryOf(string worldKitId)
        {
            switch (worldKitId)
            {
                case "WK03": case "WK07": return "buildings";
                case "WK11": return "hedge";
                case "WK12": return "containers";
                case "WK13": return "fence";
                case "WK14": return "curtain";
                default: return "wall";
            }
        }

        /// <summary>布局取自"主路径 / 拓扑"那一栏的形状词，而不是取自场景类型。</summary>
        public static string LayoutOf(InternalLevelData lv)
        {
            string name = lv.name ?? "";
            string scale = lv.greyboxScale ?? "";
            string path = lv.mainPath ?? "";

            // 【先看这一关整体是什么，再看路径里的词】
            //
            // 原来是把 mainPath 整串拿去匹配关键词，于是 9-1 的主路径
            // "空白工作区→草稿工位→**修改走廊**→提交区" 里一个"走廊"，
            // 就把整关判成了 corridor——一段路的名字决定了整关的形状。
            // 实机结果是玩家被夹在两排家具之间动不了，原话是"一点儿不方便移动"。
            // 路径里的段名描述的是**沿途经过什么**，不是这地方长什么样。

            // ① 关卡名/尺度里明写的形状最可信
            if (name.Contains("迷宫") || name.Contains("迷雾")) return "maze";
            if (name.Contains("走廊") || name.Contains("长廊") || name.Contains("隧道") ||
                name.Contains("巷") || name.Contains("通道")) return "corridor";
            if (name.Contains("大厅") || name.Contains("法庭") || name.Contains("审判") ||
                name.Contains("影院") || name.Contains("王座") || name.Contains("神殿")) return "hall";
            if (name.Contains("广场") || name.Contains("街") || name.Contains("市场") ||
                name.Contains("峡谷") || name.Contains("公园") || name.Contains("断崖")) return "openblock";

            // ② 路线型：尺度栏写的是"主路线 NNNm"而不是 W×D
            if (scale.Contains("路线") || scale.Contains("长度")) return "corridor";

            // ③ 路径里**过半**的段落都是通道类，才算通道关
            int corridorish = 0, segs = 0;
            var parts = path.Split('→');
            for (int i = 0; i < parts.Length; i++)
            {
                string seg = parts[i].Trim();
                if (seg.Length == 0) continue;
                segs++;
                if (seg.Contains("走廊") || seg.Contains("通道") || seg.Contains("隧道") ||
                    seg.Contains("巷") || seg.Contains("阶梯")) corridorish++;
            }
            if (segs > 0 && corridorish * 2 > segs) return "corridor";

            // ④ 其余：室内给 hall（一整块开阔地，道具沿着走），户外给 openblock。
            //    默认绝不能是 rooms——那是分隔最密的一种，玩家进去就是"拥挤狭小封闭"。
            string kit = FirstKit(lv);
            var info = SiteKitCatalog.Kind(SiteKindOf(kit));
            return (info != null && info.indoor) ? "hall" : "openblock";
        }

        /// <summary>高低差：断崖、平台、楼层、塔、矿井都在关卡表里明写着。</summary>
        public static string VerticalityOf(InternalLevelData lv)
        {
            string t = (lv.mainPath ?? "") + (lv.greyboxScale ?? "") + (lv.name ?? "") + (lv.coreMechanic ?? "");
            if (t.Contains("矿井") || t.Contains("下沉") || t.Contains("坑")) return "pit";
            if (t.Contains("断崖") || t.Contains("断桥") || t.Contains("峡谷")) return "split";
            if (t.Contains("平台") || t.Contains("阶梯") || t.Contains("楼层") ||
                t.Contains("塔") || t.Contains("山坡") || t.Contains("脚手架")) return "platform";
            if (t.Contains("看台") || t.Contains("二层") || t.Contains("阳台")) return "balcony";
            return "flat";
        }

        static string WeatherOf(InternalLevelData lv)
        {
            string t = (lv.realityToAdversity ?? "") + (lv.name ?? "");
            if (t.Contains("雾")) return "fog";
            if (t.Contains("雨")) return "rain";
            if (t.Contains("雪") || t.Contains("冰") || t.Contains("冻")) return "snow";
            if (t.Contains("尘") || t.Contains("沙")) return "dust";
            if (t.Contains("风")) return "wind";
            return "clear";
        }

        static string AmbienceOf(InternalLevelData lv, string kit)
        {
            if (kit == "WK14") return "flicker";
            if (kit == "WK03" || kit == "WK07" || kit == "WK11" || kit == "WK13") return "dusk";
            return "indoor_cold";
        }

        static string LandmarkOf(InternalLevelData lv)
        {
            string t = (lv.name ?? "") + (lv.mainPath ?? "") + (lv.coreMechanic ?? "");
            if (t.Contains("王座") || t.Contains("审判") || t.Contains("法庭") ||
                t.Contains("讲台") || t.Contains("神殿")) return "podium";
            if (t.Contains("排行") || t.Contains("银幕") || t.Contains("重播") ||
                t.Contains("大屏")) return "big_screen";
            if (t.Contains("竞技") || t.Contains("对决")) return "ring";
            if (t.Contains("货架") || t.Contains("仓库") || t.Contains("箱")) return "shelf_maze";
            if (t.Contains("脚手架") || t.Contains("工地")) return "scaffold";
            if (t.Contains("钟楼") || t.Contains("计时")) return "clock_tower";
            if (t.Contains("公交") || t.Contains("车站") || t.Contains("班车")) return "bus";
            if (t.Contains("营地") || t.Contains("港")) return "tent";
            if (t.Contains("偶像") || t.Contains("塔")) return "statue";
            return "none";
        }

        /// <summary>
        /// 从关卡表的尺度栏里读出真实米数。
        ///
        /// PRD 写得很具体，但写法有三种，三种都得认：
        ///   ① 单组：  "42m×18m×4m"
        ///   ② 多组：  "Reality 35×25m；法庭55×45m；核心40×40m"
        ///   ③ 路线：  "主路线120m；平台宽8-15m"（根本不是一块场地的边长）
        ///
        /// 取哪一组有讲究：
        /// · Boss 关取**最大**的那一组——4.1 节要求 Boss 有效尺度 35-70m，
        ///   而 9-5 的第一组是 Reality 35×25，真正的战场是法庭 55×45。
        ///   取第一组会把 Boss 塞进一间比它自己还小的屋子。
        /// · 其余关取第一组：它是主场地，后面几组是支路或分区。
        /// · 路线型关卡（9-4 断崖）给一个**狭长**的占地：长度按路线、宽度按平台。
        ///   不这么做它会退回默认盒子，变成一间方屋子——而这一关的空间语言是"一条路"。
        ///
        /// 读不到就返回 0，建造器自己回退到 sizeHint。
        /// </summary>
        public static void MetersOf(InternalLevelData lv, out float w, out float d)
        {
            w = 0f; d = 0f;
            string s = lv != null ? (lv.greyboxScale ?? "") : "";
            if (s.Length == 0) return;

            bool wantLargest = lv.isBossLevel;
            for (int i = 0; i < s.Length; i++)
            {
                if (!char.IsDigit(s[i])) continue;
                int a = i;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
                float first;
                if (!float.TryParse(s.Substring(a, i - a), out first)) continue;

                int j = i;
                while (j < s.Length && (s[j] == 'm' || s[j] == 'M' || s[j] == ' ')) j++;
                if (j >= s.Length || (s[j] != '×' && s[j] != 'x' && s[j] != 'X' && s[j] != '*')) continue;
                j++;
                while (j < s.Length && s[j] == ' ') j++;
                int b = j;
                while (j < s.Length && (char.IsDigit(s[j]) || s[j] == '.')) j++;
                float second;
                if (b == j || !float.TryParse(s.Substring(b, j - b), out second)) continue;

                if (!wantLargest) { w = first; d = second; return; }
                if (first * second > w * d) { w = first; d = second; }
                i = j;
            }
            if (w > 0.1f) return;

            // 一组 W×D 都没有：看看是不是"主路线 NNN m"那种路线型关卡
            float route = RouteLengthOf(s);
            if (route <= 0.1f) return;
            w = route;
            d = Mathf.Max(12f, route * 0.18f);   // 狭长：宽度按路线的一小截，读起来是一条路
        }

        /// <summary>"主路线120m" / "长度110m" / "完整路线约150m" 里的那个长度。</summary>
        static float RouteLengthOf(string s)
        {
            string[] keys = { "路线", "长度", "主路" };
            for (int k = 0; k < keys.Length; k++)
            {
                int at = s.IndexOf(keys[k], System.StringComparison.Ordinal);
                if (at < 0) continue;
                for (int i = at; i < s.Length; i++)
                {
                    if (!char.IsDigit(s[i])) continue;
                    int a = i;
                    while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
                    float v;
                    if (float.TryParse(s.Substring(a, i - a), out v) && v >= 20f) return v;
                    break;
                }
            }
            return 0f;
        }

        /// <summary>尺度栏里的第一个数字决定 sizeHint（Boss 关按 35-70m 的有效尺度走）。</summary>
        public static string SizeHintOf(InternalLevelData lv)
        {
            if (lv.isBossLevel) return "large";
            if (lv.tier == "Elite") return "large";
            int biggest = 0, cur = 0;
            string s = lv.greyboxScale ?? "";
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] >= '0' && s[i] <= '9') { cur = cur * 10 + (s[i] - '0'); continue; }
                if (cur > biggest) biggest = cur;
                cur = 0;
            }
            if (cur > biggest) biggest = cur;
            if (biggest >= 60) return "large";
            if (biggest >= 25) return "medium";
            return "small";
        }

        /// <summary>
        /// World Kit → 这类地方该摆什么（只能用已批准道具库里的 id）。
        ///
        /// 【为什么必须填】不填的话 SiteBuilder 建出来的是一间只有地板和墙的空屋子：
        /// 房间名写着"草稿工位"，走进去什么都没有。道具不是装饰，
        /// 它是"这地方有人用过"的唯一证据。
        /// </summary>
        static string[] PropsOf(string worldKitId)
        {
            switch (worldKitId)
            {
                case "WK01": return new[] { "bed", "desk", "chair", "cabinet", "curtain", "lamp" };
                case "WK02": return new[] { "door_frame", "stairs", "locker", "lamp", "trashbin" };
                case "WK03": return new[] { "bench", "sign", "lamp", "car", "trashbin", "billboard" };
                case "WK04": return new[] { "bench", "sign", "pillar", "barrier", "lamp" };
                case "WK05": return new[] { "desk", "chair", "monitor", "whiteboard", "printer", "cabinet" };
                case "WK06": return new[] { "counter", "chair", "sign", "billboard", "bench" };
                case "WK07": return new[] { "stall", "shelf", "counter", "billboard", "bench", "plant" };
                case "WK08": return new[] { "shelf", "desk", "chair", "papers", "lamp" };
                case "WK09": return new[] { "desk", "monitor", "whiteboard", "shelf", "papers", "chair" };
                case "WK10": return new[] { "counter", "bench", "chair", "sign", "plant" };
                case "WK11": return new[] { "bench", "plant", "lamp", "fence", "sign" };
                case "WK12": return new[] { "shelf", "crate", "cart", "barrier", "pipe", "sign" };
                case "WK13": return new[] { "crate", "barrier", "trashbin", "pipe", "fence" };
                case "WK14": return new[] { "pillar", "papers", "lamp", "curtain" };
                default: return new[] { "desk", "chair", "shelf", "lamp" };
            }
        }

        /// <summary>主路径按 → 拆成房间：入口、阻力所在、深处，最多 4 段。</summary>
        static List<SiteRoom> RoomsOf(InternalLevelData lv)
        {
            var rooms = new List<SiteRoom>();
            // 房间数按面积收：42×18 = 756 平米切成四间，每间只剩十来平米，
            // 再摆上家具就是图里那样"人卡在两组柜子中间"。
            float aw, ad;
            MetersOf(lv, out aw, out ad);
            float area = (aw > 0.1f && ad > 0.1f) ? aw * ad : 900f;
            int maxRooms = area >= 1800f ? 4 : (area >= 900f ? 3 : 2);

            var segs = (lv.mainPath ?? "").Split('→');
            for (int i = 0; i < segs.Length && rooms.Count < maxRooms; i++)
            {
                string name = segs[i].Trim();
                if (string.IsNullOrEmpty(name)) continue;
                var room = new SiteRoom
                {
                    name = name,
                    purpose = i == 0 ? "进场：看清自己站在哪"
                            : (i == segs.Length - 1 ? "收束：这一关真正要发生的那个动作"
                                                    : "阻力所在：错误策略在这里显出代价"),
                    sizeHint = i == segs.Length - 1 ? "large" : "medium",
                };
                // 每间房各取一段，互不重样：同一处 Base 的几间房不该长得一模一样
                var pool = PropsOf(FirstKit(lv));
                for (int k = 0; k < 2; k++) room.props.Add(pool[(i * 2 + k) % pool.Length]);
                rooms.Add(room);
            }
            if (rooms.Count == 0)
                rooms.AddRange(SiteKitCatalog.DefaultRooms(SiteKindOf(FirstKit(lv)),
                    WeaknessAxis.Procrastination));
            return rooms;
        }

        /// <summary>复用资产栏里的 PF_xxx 就是这一关的可交互关键物。</summary>
        static List<string> InteractablesOf(InternalLevelData lv)
        {
            var list = new List<string>();
            string s = (lv.reusedAssets ?? "") + " " + (lv.triggerNote ?? "");
            int i = 0;
            while (i < s.Length)
            {
                int at = s.IndexOf("PF_", i, System.StringComparison.Ordinal);
                if (at < 0) break;
                int end = at + 3;
                while (end < s.Length && (char.IsLetterOrDigit(s[end]) || s[end] == '_')) end++;
                string id = s.Substring(at, end - at);
                if (!list.Contains(id)) list.Add(id);
                i = end;
            }
            return list;
        }

        public static string FirstKit(InternalLevelData lv) =>
            lv != null && lv.worldKitIds.Count > 0 ? lv.worldKitIds[0] : "WK05";

        /// <summary>
        /// 一关 → 一份 SiteBlueprint。
        /// 规则栏（rules）直接给玩家看：这一关考的是什么、怎样才算通关。
        /// </summary>
        public static SiteBlueprint Compose(InternalLevelData lv, InternalChapterInfo ch)
        {
            if (lv == null) return new SiteBlueprint();

            string kit = FirstKit(lv);
            var site = new SiteBlueprint
            {
                siteName = lv.name,
                siteKind = SiteKindOf(kit),
                layout = LayoutOf(lv),
                ambience = AmbienceOf(lv, kit),
                sizeHint = SizeHintOf(lv),
                palette = PaletteOf(kit),
                groundSurface = SurfaceOf(kit),
                boundary = BoundaryOf(kit),
                landmark = LandmarkOf(lv),
                verticality = VerticalityOf(lv),
                weather = WeatherOf(lv),
                // 家具给到 0-1：这一关的内容是关键物与修改卡，不是桌椅。
                // 42×18 的地方摆四间房的家具 + 散落道具 + 9 个交互物，
                // 玩家会被夹在中间走不动（实机原话："一点儿不方便移动"）。
                clutter = lv.isBossLevel ? 0 : 1,
                sceneDescription = lv.realityToAdversity,
            };

            // PRD 明写的尺度：给了就用真实米数，别再压成三档
            float mw, md;
            MetersOf(lv, out mw, out md);
            site.siteWidth = mw;
            site.siteDepth = md;

            site.rooms = RoomsOf(lv);
            site.interactables = InteractablesOf(lv);

            // 散落道具只给一件：够说明"有人在这儿做过事"，又不挡路。
            var scatter = PropsOf(kit);
            site.scatterProps.Add(scatter[0]);

            site.rules.Add("核心机制：" + lv.coreMechanic);
            site.rules.Add("通关：" + lv.realityVictory);
            if (lv.isBossLevel && ch != null && ch.boss != null)
            {
                site.rules.Add("Boss 失效条件：" + ch.boss.executionGate +
                               "（DeathType=" + ch.boss.deathType + "）");
                site.rules.Add("以下都不是命门：" + string.Join("、", ch.boss.fakeWeaknesses.ToArray()));
            }

            // 内部语言攻击：这一关的 Canonical 攻击句。
            // 它们不是氛围字幕——每一句在 MentalAttackCatalog 里都挂着三选一与状态效果。
            var events = MentalAttackCatalog.ForLevel(lv.levelId);
            for (int i = 0; i < events.Count; i++) site.internalLines.Add(events[i].attackLine);

            // externalLines 保持为空：第 9-26 章没有外部敌人，也就没有外部台词。

            return site;
        }

        /// <summary>
        /// 敌人编成。
        ///
        /// 关卡表里的单位名（白纸哨兵、链断者、反例书记员……）是这一关的**叫法**，
        /// 不是敌人库里的类型。类型按本章弱点轴取该轴的内心敌人——
        /// 这样既保证"只引用已批准 id"，又保证这 90 关一个外部敌人都不会出现。
        /// </summary>
        public static List<ChapterEnemySpec> ComposeEnemies(InternalLevelData lv, InternalChapterInfo ch)
        {
            var plan = new List<ChapterEnemySpec>();
            if (lv == null) return plan;

            var axis = InternalChapterBridge.AxisOf(ch);
            EnemyType ext, inner, bossArch;
            ChapterModuleLibrary.SuggestEnemies(axis, out ext, out inner, out bossArch);

            for (int i = 0; i < lv.internalUnits.Count; i++)
            {
                var u = lv.internalUnits[i];
                plan.Add(new ChapterEnemySpec
                {
                    enemyType = inner.ToString(),
                    // 不可击杀的投影按精英摆：它们该有存在感，但玩家不该指望打掉它们。
                    tier = !u.killable ? "elite" : (lv.tier == "Elite" && i == 0 ? "elite" : "standard"),
                    count = Mathf.Clamp(u.count, 1, 4),
                    placement = i == 0 ? "entrance" : (i == 1 ? "middle" : "deep"),
                    role = u.killable ? "guard" : "ambush",
                });
            }

            if (lv.isBossLevel && ch != null && ch.boss != null)
            {
                EnemyType bossType;
                if (!InternalChapterBridge.TryBossEnemyType(ch.boss, out bossType)) bossType = bossArch;
                plan.Add(new ChapterEnemySpec
                {
                    enemyType = bossType.ToString(),
                    tier = "chief",
                    count = 1,
                    placement = "deep",
                    role = "guard",
                });
            }

            return plan;
        }
    }
}
