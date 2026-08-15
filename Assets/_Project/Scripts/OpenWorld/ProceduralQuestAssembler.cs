using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.AI;
using AdversityRoad.Core;
using AdversityRoad.Goals;

namespace AdversityRoad.OpenWorld
{
    /// <summary>一个已经落进世界的章节遭遇（运行时对象，不入存档）。</summary>
    public class AssembledEncounter
    {
        public string chapterId;
        public string districtId;
        public Vector3 anchor;
        public readonly List<GameObject> enemies = new List<GameObject>();
        public string bossEnemyId = "";
        public bool live;
        /// <summary>本章节现场生成的场景（AI 章节必有；Legacy 走裂隙复用 V1 关卡）。</summary>
        public SiteInstance site;
        public SiteGate gate;
    }

    /// <summary>
    /// Procedural Quest Assembler（方案 5.4 / 19.5 / 19.6）：
    ///
    /// 蓝图过了 Validator 之后，由这里按 assemblySeed 确定性地挑选
    /// World Cell → Encounter Slot → Enemy Spawn Profile → Mechanic Package → Reward Package。
    /// **相同 Seed + 相同版本必须组装出相同的关卡**，这样 Bug 才能定位、验收才能复现。
    ///
    /// AI 永远拿不到坐标，也永远不能生成几何：它只说"在工作区、用这几种机制、
    /// 打这几种敌人"，落地的位置全部来自已烘焙、已导航验证的 EncounterSlot。
    /// </summary>
    public static class ProceduralQuestAssembler
    {
        /// <summary>由 GameBootstrap 注入的敌人生成器（type, tier, pos, uniqueId → GameObject）。</summary>
        public static System.Func<EnemyType, EnemyTier, Vector3, bool, GameObject> Spawner;

        /// <summary>世界构建上下文（贴图/材质缓存），由 GameBootstrap 注入给场景生成器。</summary>
        public static World.WorldContext WorldCtx;

        static readonly Dictionary<string, AssembledEncounter> _live =
            new Dictionary<string, AssembledEncounter>();

        public static IEnumerable<AssembledEncounter> Live => _live.Values;

        public static void ClearAll()
        {
            foreach (var e in _live.Values) Despawn(e);
            _live.Clear();
        }

        public static bool IsAssembled(string chapterId) =>
            !string.IsNullOrEmpty(chapterId) && _live.ContainsKey(chapterId);

        /// <summary>
        /// 把一个章节蓝图组装进开放世界。Legacy 章节不在这里组装——
        /// 它们通过心理裂隙进入 V1 原关卡（资产复用，不重复造）。
        /// </summary>
        public static AssembledEncounter Assemble(GoalChapterData bp, GoalData goal)
        {
            if (bp == null || Spawner == null || !DistrictCatalog.Ready) return null;
            if (!bp.validated) return null;
            if (_live.ContainsKey(bp.chapterId)) return _live[bp.chapterId];

            if (bp.source == ChapterSource.Legacy)
            {
                // Legacy：在它对应的区域打开心理裂隙，走进去就是原汁原味的 V1 关卡
                WorldState.OpenRift(bp.worldDistrictId, bp.legacyZoneIndex, bp.chapterName);
                return null;
            }

            var district = DistrictCatalog.Get(bp.worldDistrictId);
            if (district == null) return null;

            var enc = new AssembledEncounter
            {
                chapterId = bp.chapterId,
                districtId = bp.worldDistrictId,
                anchor = DistrictCatalog.EncounterSlot(bp.worldDistrictId, bp.assemblySeed, 0),
                live = true
            };

            // ---------- 现场把这一章自己的场景建出来（V2.0 的关键一步） ----------
            // 不再是"在固定关卡里换几个敌人"：AI 描述的房间、道具、NPC、灯光、规则
            // 会在这里被真的建成一处可以走进去的地方，并按 seed 完全可复现。
            enc.site = SiteBuilder.Build(bp, WorldCtx);

            var rng = new System.Random(bp.assemblySeed);

            if (enc.site != null)
            {
                // 入口开在所属区域的遭遇位上：从城里走过去，推门进入这个新场景
                enc.gate = SiteGate.Create(GroundAt(enc.anchor), bp.chapterId,
                    bp.site.siteName, new Color(0.55f, 0.8f, 1f));
                SpawnIntoSite(bp, goal, enc, rng);
            }
            else
            {
                // 极端情况（场景蓝图缺失）：退回在城区遭遇位上就地布置，保证章节仍可玩
                SpawnIntoDistrict(bp, goal, enc, rng);
                BuildMechanicProps(bp, enc.anchor);
            }

            _live[bp.chapterId] = enc;
            WorldState.Get(bp.worldDistrictId).hasChapter = true;
            GoalOS.NoteChapterAttempt(bp.chapterId);

            CloudDialogueService.AddLog("章节已组装：" + bp.chapterName + " @" +
                DistrictCatalog.NameOf(bp.worldDistrictId) + " seed=" + bp.assemblySeed +
                " 敌人×" + enc.enemies.Count);
            GameEvents.RaiseSubtitle("〔章节展开〕" + bp.chapterName + " —— " + bp.successCondition);
            return enc;
        }

        /// <summary>把敌人放进**生成出来的场景**里（不是放在城区街上）。</summary>
        static void SpawnIntoSite(GoalChapterData bp, GoalData goal,
            AssembledEncounter enc, System.Random rng)
        {
            var site = enc.site;
            int slot = 0;

            foreach (var name in bp.externalEnemies)
            {
                if (!ChapterModuleLibrary.TryEnemy(name, out var t)) continue;
                var go = Spawner(t, TierFor(goal, rng), SiteSpot(site, slot++, rng), true);
                if (go != null) { enc.enemies.Add(go); Reparent(go, site); }
            }
            foreach (var name in bp.internalEnemies)
            {
                if (!ChapterModuleLibrary.TryEnemy(name, out var t)) continue;
                var go = Spawner(t, EnemyTier.Standard, SiteSpot(site, slot++, rng), true);
                if (go != null) { enc.enemies.Add(go); Reparent(go, site); }
            }

            if (ChapterModuleLibrary.TryEnemy(bp.bossArchetype, out var bossType))
            {
                var bossGo = Spawner(bossType, EnemyTier.Chief, SiteSpot(site, 0, rng), true);
                if (bossGo != null)
                {
                    enc.enemies.Add(bossGo);
                    Reparent(bossGo, site);
                    var ec = bossGo.GetComponent<EnemyController>();
                    if (ec != null && ec.profile != null) enc.bossEnemyId = ec.profile.enemyId;
                    bossGo.AddComponent<ChapterGateEnemy>().chapterId = bp.chapterId;
                }
            }

            // 机制物件也放进场景内部，让"规则"在这个地方看得见摸得着
            BuildMechanicProps(bp, site.origin, site);
        }

        static void SpawnIntoDistrict(GoalChapterData bp, GoalData goal,
            AssembledEncounter enc, System.Random rng)
        {
            int slot = 0;
            foreach (var name in bp.externalEnemies)
            {
                if (!ChapterModuleLibrary.TryEnemy(name, out var t)) continue;
                var pos = Offset(DistrictCatalog.EncounterSlot(bp.worldDistrictId, bp.assemblySeed, slot++), rng);
                var go = Spawner(t, TierFor(goal, rng), pos, true);
                if (go != null) enc.enemies.Add(go);
            }
            foreach (var name in bp.internalEnemies)
            {
                if (!ChapterModuleLibrary.TryEnemy(name, out var t)) continue;
                var pos = Offset(DistrictCatalog.EncounterSlot(bp.worldDistrictId, bp.assemblySeed, slot++), rng);
                var go = Spawner(t, EnemyTier.Standard, pos, true);
                if (go != null) enc.enemies.Add(go);
            }
            if (ChapterModuleLibrary.TryEnemy(bp.bossArchetype, out var bossType))
            {
                var bossGo = Spawner(bossType, EnemyTier.Chief, Offset(enc.anchor, rng), true);
                if (bossGo != null)
                {
                    enc.enemies.Add(bossGo);
                    var ec = bossGo.GetComponent<EnemyController>();
                    if (ec != null && ec.profile != null) enc.bossEnemyId = ec.profile.enemyId;
                    bossGo.AddComponent<ChapterGateEnemy>().chapterId = bp.chapterId;
                }
            }
        }

        static Vector3 SiteSpot(SiteInstance site, int index, System.Random rng)
        {
            if (site.enemySpawns.Count == 0) return site.playerSpawn + Vector3.forward * 10f;
            var basePos = site.enemySpawns[Mathf.Abs(index) % site.enemySpawns.Count];
            return Offset(basePos, rng);
        }

        /// <summary>敌人挂到场景根下：场景卸载时一起收走，不会留在世界里游荡。</summary>
        static void Reparent(GameObject go, SiteInstance site)
        {
            if (go != null && site != null && site.root != null)
                go.transform.SetParent(site.root.transform, true);
        }

        static Vector3 GroundAt(Vector3 p)
        {
            if (UnityEngine.AI.NavMesh.SamplePosition(p, out var hit, 8f, UnityEngine.AI.NavMesh.AllAreas))
                return hit.position;
            return p;
        }

        /// <summary>章节通关或玩家离开时收起（不销毁世界，只收起这次遭遇的内容）。</summary>
        public static void Despawn(AssembledEncounter enc)
        {
            if (enc == null) return;
            foreach (var go in enc.enemies)
                if (go != null) Object.Destroy(go);
            enc.enemies.Clear();
            if (enc.gate != null) Object.Destroy(enc.gate.gameObject);
            if (enc.site != null)
            {
                World.ZoneBuilder.UnregisterDynamicZone(enc.site.siteId);
                SiteBuilder.Unload(enc.chapterId);
            }
            enc.live = false;
        }

        public static void DespawnChapter(string chapterId)
        {
            if (string.IsNullOrEmpty(chapterId)) return;
            if (!_live.TryGetValue(chapterId, out var enc)) return;
            Despawn(enc);
            _live.Remove(chapterId);
        }

        /// <summary>难度取自逆境预算：不由 AI 决定，也不在动画里偷偷改判定。</summary>
        static EnemyTier TierFor(GoalData goal, System.Random rng)
        {
            var intensity = goal != null ? goal.intensity : MentalIntensity.Standard;
            switch (intensity)
            {
                case MentalIntensity.Light: return EnemyTier.Novice;
                case MentalIntensity.HighPressure:
                    return rng.Next(100) < 55 ? EnemyTier.Elite : EnemyTier.Standard;
                default:
                    return rng.Next(100) < 35 ? EnemyTier.Standard : EnemyTier.Novice;
            }
        }

        static Vector3 Offset(Vector3 basePos, System.Random rng)
        {
            if (basePos == Vector3.zero) return basePos;
            float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
            float rad = 2.5f + (float)rng.NextDouble() * 5f;
            var p = basePos + new Vector3(Mathf.Cos(ang) * rad, 0, Mathf.Sin(ang) * rad);
            if (UnityEngine.AI.NavMesh.SamplePosition(p, out var hit, 8f, UnityEngine.AI.NavMesh.AllAreas))
                return hit.position + Vector3.up * 1.1f;
            return basePos;
        }

        /// <summary>机制包：把蓝图里的机制标签变成场上看得见、摸得到的物件。</summary>
        static void BuildMechanicProps(GoalChapterData bp, Vector3 anchor, SiteInstance site = null)
        {
            if (anchor == Vector3.zero) return;
            int i = 0;
            foreach (var id in bp.physicalMechanics)
            {
                var info = ChapterModuleLibrary.Mechanic(id);
                if (info == null) continue;
                Vector3 p = anchor + Quaternion.Euler(0, 70f * i++, 0) * Vector3.forward * 9f;
                if (UnityEngine.AI.NavMesh.SamplePosition(p, out var mh, 10f,
                        UnityEngine.AI.NavMesh.AllAreas)) p = mh.position;

                ChapterProp made;
                switch (id)
                {
                    case "beacon_ignite":
                        made = ChapterProp.Create(p, "行动灯台", new Color(1f, 0.65f, 0.25f), info.description);
                        break;
                    case "evidence_collect":
                        made = ChapterProp.Create(p, "事实碎片", new Color(0.55f, 0.85f, 1f), info.description);
                        break;
                    case "resource_scavenge":
                        made = ChapterProp.Create(p, "补给点", new Color(0.45f, 0.9f, 0.6f), info.description);
                        break;
                    case "protect_zone":
                        made = ChapterProp.Create(p, "稳定区", new Color(0.5f, 0.75f, 1f), info.description);
                        break;
                    case "timed_actions":
                        made = ChapterProp.Create(p, "限时台", new Color(1f, 0.5f, 0.35f), info.description);
                        break;
                    default:
                        made = ChapterProp.Create(p, info.name, new Color(0.8f, 0.75f, 0.95f), info.description);
                        break;
                }
                if (made != null && site != null && site.root != null)
                    made.transform.SetParent(site.root.transform, true);
            }
        }
    }

    /// <summary>章节机制的世界化身：走近给出可读提示（机制必须看得见，而不是只写在蓝图里）。</summary>
    public class ChapterProp : MonoBehaviour
    {
        public string label = "";
        public string hint = "";
        float _lastHint = -99f;

        public static ChapterProp Create(Vector3 pos, string label, Color color, string hint)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "ChapterProp_" + label;
            go.transform.position = pos + Vector3.up * 0.7f;
            go.transform.localScale = new Vector3(1.2f, 1.4f, 1.2f);
            go.GetComponent<MeshRenderer>().sharedMaterial =
                Combat.CombatFeedback.EnergyMaterial(color, 0.5f);
            OpenWorldBuilder.HomeSign(pos + Vector3.up * 2.2f, label);

            var p = go.AddComponent<ChapterProp>();
            p.label = label;
            p.hint = hint;
            return p;
        }

        void Update()
        {
            if (Time.time - _lastHint < 10f) return;
            var player = FindObjectOfType<Player.PlayerController>();
            if (player == null) return;
            if (Vector3.Distance(transform.position, player.transform.position) > 3.5f) return;
            _lastHint = Time.time;
            GameEvents.RaiseSubtitle("【" + label + "】" + hint);
        }
    }

    /// <summary>章节守门人：击败它即判定该章节通关，并把结果回写给 Goal OS。</summary>
    public class ChapterGateEnemy : MonoBehaviour
    {
        public string chapterId = "";
        bool _reported;

        void OnEnable() => GameEvents.OnEnemyKilled += OnKilled;
        void OnDisable() => GameEvents.OnEnemyKilled -= OnKilled;

        void OnKilled(string enemyId)
        {
            if (_reported) return;
            var ec = GetComponent<EnemyController>();
            if (ec == null || ec.profile == null || ec.profile.enemyId != enemyId) return;
            _reported = true;

            GoalOS.ChapterCleared(chapterId);
            var goal = GoalOS.Active;
            var bp = goal != null ? goal.FindChapter(chapterId) : null;
            if (bp != null)
            {
                WorldState.OnMilestoneReached(bp.worldDistrictId);
                GameEvents.RaiseSubtitle("〔章节完成〕" + bp.chapterName + " —— " +
                    GoalJourneyGraph.ProgressLine(goal));
            }
            // 在生成场景里打完 → 先把玩家送回城里，再卸载这处场景（否则会被留在虚空里）
            if (SiteGate.InsideSite && SiteGate.InsideChapterId == chapterId)
            {
                var host = new GameObject("SiteExitDelay");
                host.AddComponent<SiteExitDelay>().Setup(chapterId, 3.5f);
            }
            else ProceduralQuestAssembler.DespawnChapter(chapterId);
        }
    }
}

