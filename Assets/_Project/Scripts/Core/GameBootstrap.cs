using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Unity.AI.Navigation;
using AdversityRoad.AI;
using AdversityRoad.Combat;
using AdversityRoad.Mobile;
using AdversityRoad.Player;
using AdversityRoad.Quest;
using AdversityRoad.UI;
using AdversityRoad.World;

namespace AdversityRoad.Core
{
    /// <summary>
    /// 运行时一键搭建整个游戏世界（CI 无头打包不依赖编辑器建场）：
    /// 六大区域（独居小屋/训练武馆/噪声街区/求职荒原/城市广场/责任转嫁法院）+ 昼夜循环 + 行人车辆 +
    /// 章节剧情 + 每区域一个章节心魔 + 玩家自由添加敌人 + HUD/触屏操控/配置面板。
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        [Tooltip("URP Lit 基础材质：保证 Shader 打进包体，运行时按颜色实例化")]
        public Material baseMaterial;

        [Tooltip("心理安全设置资产；为空时运行时创建默认值")]
        public SafetySettings safetySettings;

        WorldContext _world;
        PlayerController _player;
        PlayerAppearance _appearance;
        BattleFlowController _battleFlow;
        GameObject _currentChapterEnemy;

        void Start()
        {
            // 场景重载时系统单例仍在，但世界内容需要重建
            if (Object.FindFirstObjectByType<PlayerController>() != null) return;

            ApplyComfortAndPerformance();
            EnsureSystems();
            CombatFeedback.Init(baseMaterial);

            _world = new WorldContext { mat = baseMaterial };
            SetupDayNight();
            ZoneBuilder.BuildAll(_world);
            BakeNavMesh();

            int zone = CurrentChapterZone();
            ZoneBuilder.CurrentZoneId = ZoneBuilder.ZoneIdOf(zone);
            BuildPlayer(_world.playerSpawns[zone]);
            BuildCamera();
            EnemySpawnHook.Spawn = SpawnEnemy;   // Boss 战中召唤援军（明天之王/旧我）
            SpawnChapterEnemy();
            SpawnZoneMinions();
            ZoneBuilder.SpawnLife(_world);
            SpawnShadowGuardianIfEarned();
            BuildHUD();
            SetupChapterQuest();
            ShowChapterIntro();

            // 云端台词池预热：进区域即后台预取，喊话零延迟
            if (CloudDialogueService.Instance != null)
                CloudDialogueService.Instance.WarmUp(ZoneBuilder.CurrentZoneId,
                    Personalization.WeaknessAxis.Procrastination,
                    Personalization.WeaknessAxis.SelfDoubt,
                    Personalization.WeaknessAxis.NoiseSensitivity,
                    Personalization.WeaknessAxis.Shame,
                    Personalization.WeaknessAxis.BoundaryConflict,
                    Personalization.WeaknessAxis.FairnessSensitivity,
                    Personalization.WeaknessAxis.FailureFear);
        }

        /// <summary>
        /// 新章节区域的常驻小敌人：让审判庭/沼泽/回声馆不只有 Boss——
        /// 探索路上会遭遇主题杂兵（uniqueId，不推进章节任务）。
        /// </summary>
        void SpawnZoneMinions()
        {
            var o = _world.zoneOrigins;
            // 小题大做审判庭：旁观席前的小题大做鬼与旁观嘲笑者
            SpawnEnemy(EnemyType.OverreactGhost, EnemyTier.Standard, o[6] + new Vector3(-6, 1.1f, -16), true);
            SpawnEnemy(EnemyType.OverreactGhost, EnemyTier.Novice, o[6] + new Vector3(7, 1.1f, -4), true);
            SpawnEnemy(EnemyType.MockingBystander, EnemyTier.Standard, o[6] + new Vector3(10, 1.1f, 10), true);
            // 拖延沼泽：泥里的明日泥怪与完美准备者
            SpawnEnemy(EnemyType.TomorrowMud, EnemyTier.Standard, o[7] + new Vector3(-8, 1.1f, -18), true);
            SpawnEnemy(EnemyType.TomorrowMud, EnemyTier.Novice, o[7] + new Vector3(12, 1.1f, -2), true);
            SpawnEnemy(EnemyType.PerfectPreparer, EnemyTier.Standard, o[7] + new Vector3(-4, 1.1f, 10), true);
            // 旧事回声馆：展柜长廊里的旧话复读者/过去判官/反刍虫群
            SpawnEnemy(EnemyType.OldVoiceRepeater, EnemyTier.Standard, o[8] + new Vector3(-7, 1.1f, -18), true);
            SpawnEnemy(EnemyType.PastJudge, EnemyTier.Standard, o[8] + new Vector3(8, 1.1f, -10), true);
            SpawnEnemy(EnemyType.RuminationSwarm, EnemyTier.Novice, o[8] + new Vector3(0, 1.1f, 2), true);
            // 两元赌桌：桌边的赖账牌手与规则篡改者
            SpawnEnemy(EnemyType.DebtDodger, EnemyTier.Novice, o[9] + new Vector3(-5, 1.1f, -3), true);
            SpawnEnemy(EnemyType.RuleTwister, EnemyTier.Novice, o[9] + new Vector3(6, 1.1f, -1), true);
            // 债务车影：车阵间的欠款残影
            SpawnEnemy(EnemyType.DebtShadow, EnemyTier.Standard, o[10] + new Vector3(-14, 1.1f, -6), true);
            SpawnEnemy(EnemyType.DebtShadow, EnemyTier.Novice, o[10] + new Vector3(12, 1.1f, 2), true);
            // 眼神审判走廊：凝视眼球与表情面具
            SpawnEnemy(EnemyType.GazeEye, EnemyTier.Standard, o[11] + new Vector3(-4, 1.1f, -18), true);
            SpawnEnemy(EnemyType.GazeEye, EnemyTier.Novice, o[11] + new Vector3(4, 1.1f, -4), true);
            SpawnEnemy(EnemyType.MaskFace, EnemyTier.Standard, o[11] + new Vector3(0, 1.1f, 8), true);
            // 陌生挑衅路口：街角的挑衅路人
            SpawnEnemy(EnemyType.ProvokerPasserby, EnemyTier.Standard, o[12] + new Vector3(-13, 1.1f, 13), true);
            SpawnEnemy(EnemyType.ProvokerPasserby, EnemyTier.Novice, o[12] + new Vector3(13, 1.1f, -13), true);
            // 目标遗忘房：迷宫里的明日幻影与手机诱惑
            SpawnEnemy(EnemyType.TomorrowPhantom, EnemyTier.Novice, o[13] + new Vector3(10, 1.1f, -8), true);
            SpawnEnemy(EnemyType.PerfectPreparer, EnemyTier.Novice, o[13] + new Vector3(-8, 1.1f, 3), true);
            // 老实人消耗局：请求膨胀者与内疚投手
            SpawnEnemy(EnemyType.RequestExpander, EnemyTier.Standard, o[14] + new Vector3(-16, 1.1f, 2), true);
            SpawnEnemy(EnemyType.RequestExpander, EnemyTier.Novice, o[14] + new Vector3(16, 1.1f, -4), true);
            SpawnEnemy(EnemyType.GuiltThrower, EnemyTier.Standard, o[14] + new Vector3(0, 1.1f, 16), true);
            // 无限代付走廊：走廊里的内疚投手与请求膨胀者
            SpawnEnemy(EnemyType.GuiltThrower, EnemyTier.Standard, o[15] + new Vector3(-3, 1.1f, -16), true);
            SpawnEnemy(EnemyType.RequestExpander, EnemyTier.Standard, o[15] + new Vector3(3, 1.1f, 0), true);
            // 饥饿荒巷：暗处游荡的饥饿犬影
            SpawnEnemy(EnemyType.HungerHound, EnemyTier.Novice, o[16] + new Vector3(-5, 1.1f, -18), true);
            SpawnEnemy(EnemyType.HungerHound, EnemyTier.Novice, o[16] + new Vector3(6, 1.1f, -2), true);
            // 车库寒夜：空旷处成形的寒风刃与饿犬
            SpawnEnemy(EnemyType.ColdWindBlade, EnemyTier.Novice, o[17] + new Vector3(-10, 1.1f, -12), true);
            SpawnEnemy(EnemyType.HungerHound, EnemyTier.Novice, o[17] + new Vector3(10, 1.1f, 2), true);
            // 病房回廊：低强度——只有两道医药债影的低语
            SpawnEnemy(EnemyType.MedDebtShadow, EnemyTier.Novice, o[18] + new Vector3(-4, 1.1f, -20), true);
            SpawnEnemy(EnemyType.MedDebtShadow, EnemyTier.Novice, o[18] + new Vector3(4, 1.1f, -2), true);
            // 哲学虚无图书馆：书架间的引文幽灵与怀疑学者
            SpawnEnemy(EnemyType.QuoteGhost, EnemyTier.Novice, o[19] + new Vector3(-11, 1.1f, -16), true);
            SpawnEnemy(EnemyType.QuoteGhost, EnemyTier.Standard, o[19] + new Vector3(11, 1.1f, -4), true);
            SpawnEnemy(EnemyType.DoubtScholar, EnemyTier.Standard, o[19] + new Vector3(0, 1.1f, -10), true);
            // 无限追问大厅：门与门之间的怀疑学者与反刍虫群
            SpawnEnemy(EnemyType.DoubtScholar, EnemyTier.Standard, o[20] + new Vector3(-10, 1.1f, -24), true);
            SpawnEnemy(EnemyType.RuminationSwarm, EnemyTier.Novice, o[20] + new Vector3(8, 1.1f, -8), true);
            SpawnEnemy(EnemyType.QuoteGhost, EnemyTier.Novice, o[20] + new Vector3(-4, 1.1f, 8), true);
            // 意志断桥：桥头的引文幽灵（桥面留给走位，不设杂兵）
            SpawnEnemy(EnemyType.QuoteGhost, EnemyTier.Standard, o[21] + new Vector3(-5, 1.1f, -42), true);
            // 失败展览馆：展品间的旧话复读者与过去判官
            SpawnEnemy(EnemyType.OldVoiceRepeater, EnemyTier.Standard, o[22] + new Vector3(-8, 1.1f, -14), true);
            SpawnEnemy(EnemyType.PastJudge, EnemyTier.Standard, o[22] + new Vector3(8, 1.1f, 0), true);
            // 意志塔：广场石柱间的旧话复读者与反刍虫群（塔台留给登塔）
            SpawnEnemy(EnemyType.OldVoiceRepeater, EnemyTier.Standard, o[23] + new Vector3(-9, 1.1f, -14), true);
            SpawnEnemy(EnemyType.RuminationSwarm, EnemyTier.Novice, o[23] + new Vector3(9, 1.1f, -8), true);
        }

        /// <summary>终局达成过（旧我已整合）：影子护卫随行出生。</summary>
        void SpawnShadowGuardianIfEarned()
        {
            if (PlayerPrefs.GetInt("adversity_shadow_guardian", 0) != 1 || _player == null) return;
            Combat.ShadowGuardian.Spawn(_player.transform.position - _player.transform.forward * 2.5f);
        }

        void OnEnable() => GameEvents.OnChapterAdvanced += HandleChapterAdvanced;
        void OnDisable() => GameEvents.OnChapterAdvanced -= HandleChapterAdvanced;

        void HandleChapterAdvanced(int newChapter)
        {
            // 上一章敌人正在播放死亡演出，引用置空后生成下一章心魔
            _currentChapterEnemy = null;
            SpawnChapterEnemy();
            SetupChapterQuest();

            // 下一战场指引：明确说出目标区域 + 快速传送入口（跨区域章节不再迷路）
            var story = StoryManager.Instance;
            if (story != null && !story.AllCleared && story.Current != null)
                GameEvents.RaiseSubtitle("下一战场：【" +
                    ZoneBuilder.ZoneNameOf(story.Current.zoneIndex) +
                    "】——右上角「传送」面板可直达。");
        }

        int CurrentChapterZone()
        {
            var story = StoryManager.Instance;
            if (story == null) return 0;
            // 主线完结后回到安全屋（独居小屋）：自由修炼从家出发
            if (story.AllCleared) return 0;
            return story.Current.zoneIndex;
        }

        // ================= 舒适度与性能（防晕核心） =================

        void ApplyComfortAndPerformance()
        {
            // 安卓默认锁 30 帧，低帧率是眩晕的最大来源之一：拉到刷新率（60-120）
            QualitySettings.vSyncCount = 0;
            int hz = Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value);
            Application.targetFrameRate = Mathf.Clamp(hz, 60, 120);

            // 后处理防晕：禁用运动模糊/色差/镜头畸变/噪点，压低暗角强度
            var vol = Object.FindFirstObjectByType<Volume>();
            if (vol != null && vol.profile != null)
            {
                if (vol.profile.TryGet(out MotionBlur mb)) mb.active = false;
                if (vol.profile.TryGet(out ChromaticAberration ca)) ca.active = false;
                if (vol.profile.TryGet(out LensDistortion ld)) ld.active = false;
                if (vol.profile.TryGet(out FilmGrain fg)) fg.active = false;
                if (vol.profile.TryGet(out Vignette vg) && vg.intensity.value > 0.25f)
                    vg.intensity.Override(0.25f);
            }
        }

        // ================= 系统 =================

        void EnsureSystems()
        {
            var gm = GameManager.Instance;
            if (gm == null)
            {
                var systems = new GameObject("GameSystems");
                gm = systems.AddComponent<GameManager>();
                systems.AddComponent<QuestManager>();
                systems.AddComponent<StoryManager>();
            }
            else if (StoryManager.Instance == null)
            {
                gm.gameObject.AddComponent<StoryManager>();
            }
            if (gm.safety == null)
                gm.safety = safetySettings != null
                    ? safetySettings
                    : ScriptableObject.CreateInstance<SafetySettings>();
            CloudDialogueService.Ensure();
            GrowthSystem.EnsureKillHook();   // 敌人图鉴击败计数
        }

        void SetupDayNight()
        {
            Light sun = null;
            foreach (var l in Object.FindObjectsOfType<Light>())
                if (l.type == LightType.Directional) { sun = l; break; }
            var go = new GameObject("DayNightCycle");
            var cycle = go.AddComponent<DayNightCycle>();
            cycle.sun = sun;
            _world.dayNight = cycle;
        }

        void BakeNavMesh()
        {
            var navGo = new GameObject("Navigation");
            var surface = navGo.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
        }

        // ================= 玩家与镜头 =================

        void BuildPlayer(Vector3 spawn)
        {
            var root = new GameObject("Player");
            // 出生即贴地（胶囊底=地面），避免出生后下落/滑行漂移
            if (Physics.Raycast(spawn + Vector3.up * 5f, Vector3.down, out RaycastHit gh, 40f))
                spawn = gh.point + Vector3.up * 1.02f;
            root.transform.position = spawn;

            // 外观容器：由 PlayerAppearance 按预设组装身体/头部/着装/兵器
            var visualRoot = new GameObject("Visual");
            visualRoot.transform.SetParent(root.transform, false);

            var cc = root.AddComponent<CharacterController>();
            cc.height = 2f;
            cc.center = Vector3.zero;
            cc.radius = 0.4f;

            _player = root.AddComponent<PlayerController>();
            var fsm = root.AddComponent<CombatStateMachine>();
            root.AddComponent<StanceSystem>();     // 五种战斗姿态（Awake 早于 PlayerCombatController.Awake 读取）
            var combat = root.AddComponent<PlayerCombatController>();
            root.AddComponent<LockOnSystem>();
            var skillExec = root.AddComponent<SkillExecutor>();

            var poser = root.AddComponent<HumanoidAnimator>();
            poser.visual = visualRoot.transform;
            poser.fsm = fsm;

            _appearance = root.AddComponent<PlayerAppearance>();
            _appearance.visualRoot = visualRoot.transform;
            _appearance.poser = poser;
            _appearance.baseMaterial = baseMaterial;
            _appearance.Init();

            var hurt = new GameObject("PlayerHurtbox");
            hurt.transform.SetParent(root.transform, false);
            var hurtCol = hurt.AddComponent<CapsuleCollider>();
            hurtCol.isTrigger = true;
            hurtCol.height = 4.0f;                       // 随视觉体型放大（受击判定罩全身）
            hurtCol.center = new Vector3(0, 0.9f, 0);
            hurtCol.radius = 0.55f;
            hurt.AddComponent<Hurtbox>();

            var hitbox = CreateAttackHitbox(root.transform, 1.1f);
            combat.weaponHitbox = hitbox;
            skillExec.weaponHitbox = hitbox;

            // 边界盾可视化：半透明能量薄膜（此前是实心绿方块，正面糊住视野）
            var shield = GameObject.CreatePrimitive(PrimitiveType.Quad);
            shield.name = "GuardShield";
            Object.DestroyImmediate(shield.GetComponent<Collider>());
            shield.transform.SetParent(root.transform, false);
            shield.transform.localPosition = new Vector3(0, 0.25f, 0.75f);
            shield.transform.localScale = new Vector3(1.1f, 1.3f, 1f);
            shield.GetComponent<MeshRenderer>().sharedMaterial =
                CombatFeedback.EnergyMaterial(new Color(0.35f, 0.85f, 0.55f), 0.28f);
            shield.SetActive(false);
            combat.guardShield = shield;

            // 满势不再用脚下光环圆盘表达（易糊在地面显脏）——改由 HUD 意势圆点显示，
            // 保持画面干净。combat.innerAura 留空即可，PlayerCombatController 已做空判断。

            EquipSkills(skillExec);

            // 技能树上限加成（边界/专注/自尊扩容节点）落到属性
            GrowthSystem.ApplyMaxBonuses(_player.Stats);
        }

        void EquipSkills(SkillExecutor exec)
        {
            var dingxin = ScriptableObject.CreateInstance<Data.SkillDefinition>();
            dingxin.skillId = "dingxin_huti";
            dingxin.displayName = "定心护体";
            dingxin.description = "消耗意志凝神定心，恢复专注、自尊与决断。";
            dingxin.staminaCost = 0;
            dingxin.willCost = 25;
            dingxin.mentalRestore = 45;
            dingxin.cooldown = 12;
            dingxin.castLockTime = 0.4f;
            exec.equippedSkills.Add(dingxin);

            // 能量斩：远程大能量攻击——需 2 点意势积累才能释放，伤害极高
            var qiren = ScriptableObject.CreateInstance<Data.SkillDefinition>();
            qiren.skillId = "zhannian_qiren";
            qiren.displayName = "能量斩·斩念气刃";
            qiren.description = "凝聚 2 点意势轰出巨型剑气，伤害极高但需能量积累与调息。";
            qiren.staminaCost = 12;
            qiren.willCost = 10;
            qiren.physicalDamage = 60;
            qiren.postureDamage = 35;
            qiren.knockback = 5;
            qiren.cooldown = 7;
            qiren.castLockTime = 0.5f;
            qiren.isRanged = true;
            qiren.projectileSpeed = 20f;
            qiren.projectileScale = 2.2f;
            qiren.momentumCost = 2;
            exec.equippedSkills.Add(qiren);

            // 责任归还：责任转嫁法院核心技能——清除过度负责、把虚假责任球打回法官
            var guihuan = ScriptableObject.CreateInstance<Data.SkillDefinition>();
            guihuan.skillId = "zeren_guihuan";
            guihuan.displayName = "责任归还";
            guihuan.description = "把不属于自己的责任准确还回去：清除过度负责减速，将飞来的虚假责任球打回法官，回补边界。";
            guihuan.staminaCost = 10;
            guihuan.cooldown = 6;
            guihuan.castLockTime = 0.35f;
            guihuan.isResponsibilityReturn = true;
            exec.equippedSkills.Add(guihuan);

            // 五分钟火种：拖延沼泽/旧我冻结阶段核心技能——不等动力，先开始
            var huozhong = ScriptableObject.CreateInstance<Data.SkillDefinition>();
            huozhong.skillId = "wufenzhong_huozhong";
            huozhong.displayName = "五分钟火种";
            huozhong.description = "先做五分钟：恢复行动力、清除减速与身份冻结、意势+1。动力是被行动召回的。";
            huozhong.staminaCost = 6;
            huozhong.cooldown = 10;
            huozhong.castLockTime = 0.3f;
            huozhong.isFiveMinuteSpark = true;
            exec.equippedSkills.Add(huozhong);

            // 不读心盾：外界刺激线核心技能——无法确认的事，不当成事实
            var budu = ScriptableObject.CreateInstance<Data.SkillDefinition>();
            budu.skillId = "buduxin_dun";
            budu.displayName = "不读心盾";
            budu.description = "十秒内抵消下一次心理攻击，并令幻影假目标显形消散。无法确认的事，我不把猜测当事实。";
            budu.staminaCost = 8;
            budu.cooldown = 14;
            budu.castLockTime = 0.3f;
            budu.isMindShield = true;
            exec.equippedSkills.Add(budu);

            // 注意力回收：刺激放大器 Boss 战核心技能——把注意力拿回来
            var huishou = ScriptableObject.CreateInstance<Data.SkillDefinition>();
            huishou.skillId = "zhuyili_huishou";
            huishou.displayName = "注意力回收";
            huishou.description = "清除全部幻影假目标、恢复专注、降低反刍。不是所有声音都要回应。";
            huishou.staminaCost = 8;
            huishou.cooldown = 9;
            huishou.castLockTime = 0.3f;
            huishou.isAttentionRecall = true;
            exec.equippedSkills.Add(huishou);
        }

        void BuildCamera()
        {
            GameObject camGo;
            if (Camera.main != null) camGo = Camera.main.gameObject;
            else
            {
                camGo = new GameObject("Main Camera", typeof(Camera));
                camGo.tag = "MainCamera";
            }
            var tpc = camGo.GetComponent<ThirdPersonCamera>();
            if (tpc == null) tpc = camGo.AddComponent<ThirdPersonCamera>();
            tpc.target = _player.transform;
            tpc.player = _player;
            tpc.lockOn = _player.GetComponent<LockOnSystem>();
            _player.cameraTransform = camGo.transform;

            // 遮挡淡出：树木等挡在镜头与玩家之间时自动透明
            var occ = camGo.GetComponent<CameraOcclusionFade>();
            if (occ == null) occ = camGo.AddComponent<CameraOcclusionFade>();
            occ.target = _player.transform;

            // 近镜角色淡出：镜头贴近玩家/敌人身体时把该角色淡为半透明——
            // 根治近身缠斗时"整屏被白色模型糊脸/镜头穿模"的问题
            var closeFade = camGo.GetComponent<CharacterCloseFade>();
            if (closeFade == null) closeFade = camGo.AddComponent<CharacterCloseFade>();
            closeFade.player = _player.transform;

            // 取景补光·镜头平行光(headlight)：方向 = 镜头前方，随镜头转动，
            // 无距离衰减——任何朝向镜头的表面(=玩家/敌人的脸)恒被照亮。之前用点光，
            // 点光在战斗距离(≈5m)已被距离衰减削弱，白天主光偏后时脸仍落在阴影里
            // （截图反馈的"脸上阴影"）。平行光按法向照亮迎镜面，彻底解决背光脸黑，
            // 强度控制在补光比例(0.7 < 主光 1.15)，不喧宾夺主、不过曝。
            if (camGo.transform.Find("CameraFillLight") == null)
            {
                var fillGo = new GameObject("CameraFillLight");
                fillGo.transform.SetParent(camGo.transform, false);
                fillGo.transform.localPosition = new Vector3(0, 0.4f, 0f);
                fillGo.transform.localRotation = Quaternion.identity;   // 沿镜头前方照射
                var fill = fillGo.AddComponent<Light>();
                fill.type = LightType.Directional;
                fill.intensity = 0.5f;                       // 补光比例：迎镜脸部去黑，不过曝/不冲淡本色
                fill.color = new Color(1f, 1f, 1f);          // 中性白：不给模型染暖色，保持本色
                fill.shadows = LightShadows.None;            // 补光不投影，避免二次脸部阴影
                fill.renderMode = LightRenderMode.ForcePixel;
                // 交给昼夜循环按白天/夜晚收放强度（夜晚收低保留暗调，不平光化场景）
                if (_world != null && _world.dayNight != null)
                    _world.dayNight.cameraFill = fill;
            }

            // 音效需要一个 AudioListener（运行时建的相机不会自带）
            if (camGo.GetComponent<AudioListener>() == null) camGo.AddComponent<AudioListener>();

            // 镜头立即就位，避免开场从原点飞过来
            camGo.transform.position = _player.transform.position + new Vector3(0, 2.5f, -5f);
        }

        // ================= 敌人 =================

        /// <summary>当前章节的心魔（一个场景默认只有这一个敌人）。</summary>
        void SpawnChapterEnemy()
        {
            var story = StoryManager.Instance;
            if (story == null || story.AllCleared) return;
            var ch = story.Current;
            if (_currentChapterEnemy != null) return;
            // 章节可覆盖出生点（如第八章刺激放大器出现在街心广场而非区域默认点）
            Vector3 pos = ch.spawnOffset != Vector3.zero
                ? _world.zoneOrigins[ch.zoneIndex] + ch.spawnOffset
                : _world.enemySpawns[ch.zoneIndex];
            _currentChapterEnemy = SpawnEnemy(ch.enemyType, ch.enemyTier, pos, false);
        }

        /// <summary>玩家在"敌人+"面板自由添加的挑战。</summary>
        void SpawnEnemyNearPlayer(EnemyType type, EnemyTier tier)
        {
            if (_player == null) return;
            Vector3 pos = _player.transform.position + _player.transform.forward * 5f;
            if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 6f, NavMesh.AllAreas))
                pos = hit.position + Vector3.up * 1.1f;
            SpawnEnemy(type, tier, pos, true);
        }

        GameObject SpawnEnemy(EnemyType type, EnemyTier tier, Vector3 pos, bool uniqueId)
        {
            var profile = EnemyCatalog.Create(type, tier, uniqueId);
            float scale = EnemyCatalog.TierScale(tier);

            var root = new GameObject(profile.enemyId);
            root.transform.position = pos;
            root.transform.localScale = Vector3.one * scale;

            // 心魔人形：完整人体骨骼，主题色着装、暗肤、赤瞳
            var visualRoot = new GameObject("Visual");
            visualRoot.transform.SetParent(root.transform, false);
            Color tc = EnemyCatalog.TypeColor(type);
            var weaponKind = EnemyCatalog.WeaponOf(type);

            var poser = root.AddComponent<HumanoidAnimator>();
            poser.visual = visualRoot.transform;
            poser.isEnemy = true;

            // 优先动捕模型；无资源则回退程序化方块骨骼。
            // 不再整体染色：保持模型原本材质/肤色（按用户要求恢复原色），
            // 敌我识别交给头顶状态条/前摇红圈/恶意台词。
            HumanoidRig rig = null;
            if (!MecanimCharacter.TryBuild(visualRoot.transform, poser, false, baseMaterial, weaponKind))
            {
                rig = HumanoidRig.Build(visualRoot.transform, new HumanoidRig.Config
                {
                    skin = new Color(tc.r * 0.5f + 0.15f, tc.g * 0.5f + 0.12f, tc.b * 0.5f + 0.18f),
                    top = tc,
                    bottom = new Color(tc.r * 0.55f, tc.g * 0.55f, tc.b * 0.55f),
                    shoes = new Color(0.12f, 0.1f, 0.14f),
                    hair = new Color(0.08f, 0.06f, 0.1f),
                    eye = new Color(0.9f, 0.15f, 0.15f),
                    hasHat = false,
                    bulk = tier == EnemyTier.Chief ? 1.22f : 1f
                }, baseMaterial);
                poser.rig = rig;
                // 兵器：挂右手随臂挥舞带刀光
                var weaponRig = WeaponFactory.Build(weaponKind, rig.handR,
                    baseMaterial, new Vector3(0, -0.06f, 0.03f), new Vector3(-32f, 0, 8f));
                if (weaponRig != null)
                {
                    poser.weaponPivot = weaponRig.pivot;
                    poser.weaponTrail = weaponRig.trail;
                }
            }

            var body = root.AddComponent<CapsuleCollider>();
            body.height = 2f;
            body.radius = 0.5f;

            var agent = root.AddComponent<NavMeshAgent>();
            agent.speed = profile.moveSpeed;
            agent.stoppingDistance = profile.attackRange * 0.8f;
            // NavMeshAgent 把根节点贴在导航面上，而胶囊体/模型脚底在根节点下方 1 单位
            // （height=2、center=0 → 底部 = root - 1）。抬高 baseOffset = 胶囊半高，让胶囊
            // 底部正好落在导航面，模型脚底再由 MecanimCharacter 对齐到胶囊底部。
            // baseOffset 与胶囊高度都随 Agent 缩放，任意体型都不会腾空/陷地。
            agent.baseOffset = 1f;
            // 出生即落位：Warp 直接落到导航面上，避免 Agent 出生后向最近导航点"漂移滑行"
            //（采样半径放宽：出生点偏离导航面较远时也能一步落位，而不是禁用兜底再滑过去）
            if (NavMesh.SamplePosition(pos, out NavMeshHit spawnHit, 12f, NavMesh.AllAreas))
                agent.Warp(spawnHit.position);
            else if (Physics.Raycast(pos + Vector3.up * 5f, Vector3.down, out RaycastHit eg, 40f))
                root.transform.position = eg.point + Vector3.up * 1.02f;

            var ec = root.AddComponent<EnemyController>();
            ec.profile = profile;
            ec.poser = poser;

            ec.statusBar = EnemyStatusBar.Create(root.transform, profile.displayName, 3.8f);

            var dialogue = root.AddComponent<EnemyDialogue>();
            dialogue.displayName = profile.displayName;
            ec.dialogue = dialogue;

            ec.themeColor = tc;
            ec.baseMaterial = baseMaterial;

            var hurt = new GameObject("Hurtbox");
            hurt.transform.SetParent(root.transform, false);
            var hurtCol = hurt.AddComponent<CapsuleCollider>();
            hurtCol.isTrigger = true;
            hurtCol.height = 4.0f;                       // 随视觉体型放大
            hurtCol.center = new Vector3(0, 0.9f, 0);
            hurtCol.radius = 0.65f;
            hurt.AddComponent<Hurtbox>();

            ec.attackHitbox = CreateAttackHitbox(root.transform, 1f);

            // Boss 专属行为组件：把方案里的关卡机制落到对应心魔身上
            switch (type)
            {
                case EnemyType.TotalResponsibilityJudge:  // 抛掷责任球（真假责任判断）
                    root.AddComponent<ResponsibilityJudge>();
                    break;
                case EnemyType.SelfDenialGavel:           // 标签弹幕/审判冲击波/否定重锤
                    root.AddComponent<GavelBoss>();
                    break;
                case EnemyType.StimulusAmplifier:         // 噪声放大/幻影假目标
                    root.AddComponent<StimulusAmplifierBoss>();
                    break;
                case EnemyType.TomorrowKing:              // 泥壳护体/召唤泥怪/深泥浇灌
                    root.AddComponent<TomorrowKingBoss>();
                    break;
                case EnemyType.OldSelf:                   // 四阶段：复读/冻结/召回/整合
                    root.AddComponent<OldSelfBoss>();
                    break;
                case EnemyType.GambleKing:                // 硬币弹幕/耍赖回血/账本对质破防
                    root.AddComponent<GambleKingBoss>();
                    break;
                case EnemyType.DebtCarKing:               // 欠条护体/车灯眩光/召唤残影
                    root.AddComponent<DebtCarKingBoss>();
                    break;
                case EnemyType.ThousandEyeJudge:          // 凝视光束/虚假凝视点/万目扫射
                    root.AddComponent<ThousandEyeJudgeBoss>();
                    break;
                case EnemyType.TauntMirror:               // 挑衅窗口：追打变强、不理破绽
                    root.AddComponent<TauntMirrorBoss>();
                    break;
                case EnemyType.GoalForgetter:             // 遗忘之雾/召唤完美准备者
                    root.AddComponent<GoalForgetterBoss>();
                    break;
                case EnemyType.GoodPersonCage:            // 好人卡附着/困人牢笼
                    root.AddComponent<GoodPersonCageBoss>();
                    break;
                case EnemyType.InfinitePayer:             // 索取冲击(格挡=破绽)/账单/代付之门
                    root.AddComponent<InfinitePayerBoss>();
                    break;
                case EnemyType.ValleyColossus:            // 无力威压/内疚重石/求助电话破防
                    root.AddComponent<ValleyColossusBoss>();
                    break;
                case EnemyType.ConceptMazeMaster:         // 引文护体(三灯台破)/引文弹幕/概念迷环
                    root.AddComponent<ConceptMazeMasterBoss>();
                    break;
                case EnemyType.QuestionBeast:             // 问题弹幕连射/召唤引文幽灵
                    root.AddComponent<QuestionBeastBoss>();
                    break;
                case EnemyType.InfiniteAsker:             // 追问弹幕/意义崩桥/行动答台破防
                    root.AddComponent<InfiniteAskerBoss>();
                    break;
            }

            return root;
        }

        Hitbox CreateAttackHitbox(Transform parent, float reach)
        {
            var atk = GameObject.CreatePrimitive(PrimitiveType.Cube);
            atk.name = "AttackHitbox";
            atk.transform.SetParent(parent, false);
            atk.transform.localPosition = new Vector3(0, 0, reach);
            atk.transform.localScale = new Vector3(1f, 1f, 1.2f);
            atk.GetComponent<BoxCollider>().isTrigger = true;
            atk.GetComponent<MeshRenderer>().enabled = false;
            var rb = atk.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            return atk.AddComponent<Hitbox>();
        }

        // ================= HUD 与面板 =================

        void BuildHUD()
        {
            var canvasGo = new GameObject("HUD_Canvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                new GameObject("EventSystem",
                    typeof(UnityEngine.EventSystems.EventSystem),
                    typeof(UnityEngine.EventSystems.StandaloneInputModule));

            var hud = canvasGo.AddComponent<HUDController>();
            canvasGo.AddComponent<MobileControls>(); // 真机自动显示触屏操控

            // 受击暗角（必须不拦截点击）
            var vignetteGo = new GameObject("Vignette", typeof(Image));
            vignetteGo.transform.SetParent(canvasGo.transform, false);
            var vrt = vignetteGo.GetComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.offsetMin = Vector2.zero;
            vrt.offsetMax = Vector2.zero;
            var vImg = vignetteGo.GetComponent<Image>();
            vImg.color = new Color(0, 0, 0, 0);
            vImg.raycastTarget = false;
            hud.vignette = vImg;

            hud.hpBar        = CreateBar(canvasGo.transform, "HP",   0, new Color(0.85f, 0.2f, 0.2f));
            hud.willBar      = CreateBar(canvasGo.transform, "意志", 1, new Color(0.95f, 0.75f, 0.2f));
            hud.focusBar     = CreateBar(canvasGo.transform, "专注", 2, new Color(0.2f, 0.7f, 0.95f));
            hud.selfWorthBar = CreateBar(canvasGo.transform, "自尊", 3, new Color(0.6f, 0.4f, 0.9f));
            hud.boundaryBar  = CreateBar(canvasGo.transform, "边界", 4, new Color(0.3f, 0.8f, 0.5f));
            hud.actionPowerBar = CreateBar(canvasGo.transform, "行动", 5, new Color(0.95f, 0.5f, 0.3f));
            hud.ruminationBar = CreateBar(canvasGo.transform, "反刍", 6, new Color(0.55f, 0.2f, 0.5f));
            hud.ruminationBar.SetValue(0, 100); // 反刍从空开始（越满越糟）
            hud.drainBar      = CreateBar(canvasGo.transform, "消耗", 7, new Color(0.8f, 0.45f, 0.2f));
            hud.drainBar.SetValue(0, 100);      // 关系消耗从空开始（越满越糟）

            // 意势点（黑神话棍势式资源）：属性条下方三枚圆点
            hud.momentumPips = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                var pip = new GameObject("MomentumPip" + i, typeof(Image));
                pip.transform.SetParent(canvasGo.transform, false);
                UiUtil.SetRect(pip.GetComponent<Image>(), new Vector2(0, 1),
                    new Vector2(40 + i * 46, -310), new Vector2(34, 34));
                var img = pip.GetComponent<Image>();
                img.color = new Color(1f, 1f, 1f, 0.18f);
                img.raycastTarget = false;
                hud.momentumPips[i] = img;
            }

            // 电影黑边（锁定战斗时滑入）
            hud.cineTop = MakeCineBar(canvasGo.transform, true);
            hud.cineBottom = MakeCineBar(canvasGo.transform, false);

            // 连段序列显示（拳·拳·腿 → 提示玩家配方进度）
            var comboText = UiUtil.MakeText(canvasGo.transform, "ComboText", "", 30,
                TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.4f));
            UiUtil.SetRect(comboText, new Vector2(0, 1), new Vector2(210, -310), new Vector2(400, 40));
            hud.comboText = comboText;

            // 姿态条（属性条下方一排五枚：起步/边界/定心/事实/意志，点选或 Tab/F 切换）
            StanceBar.Create(canvasGo.transform, _player.GetComponent<StanceSystem>());

            var qText = UiUtil.MakeText(canvasGo.transform, "QuestText", "", 26,
                TextAnchor.MiddleCenter, Color.white);
            UiUtil.SetRect(qText, new Vector2(0.5f, 1f), new Vector2(0, -24), new Vector2(1000, 40));
            hud.questText = qText;

            // 今日目标常驻行（目标板系统）：主线任务下方一行淡金色小字
            var gText = UiUtil.MakeText(canvasGo.transform, "GoalText", "", 22,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.55f));
            UiUtil.SetRect(gText, new Vector2(0.5f, 1f), new Vector2(0, -58), new Vector2(1000, 32));
            hud.goalText = gText;
            gText.text = GoalSystem.HudLine();   // HUD 建立时 OnEnable 已跑过，这里补一次初始显示

            var subText = UiUtil.MakeText(canvasGo.transform, "Subtitle", "", 28,
                TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.75f));
            UiUtil.SetRect(subText, new Vector2(0.5f, 0f), new Vector2(0, 130), new Vector2(1300, 44));
            hud.subtitleText = subText;

            // 招式大字横幅（屏幕中央偏上，弹入缩放淡出）
            var banner = UiUtil.MakeText(canvasGo.transform, "SkillBanner", "", 72,
                TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.35f));
            UiUtil.SetRect(banner, new Vector2(0.5f, 0.5f), new Vector2(0, 180), new Vector2(1400, 110));
            banner.fontStyle = FontStyle.Bold;
            var bannerColor = banner.color; bannerColor.a = 0f; banner.color = bannerColor;
            hud.skillBanner = banner;

            // 连击计数器（屏幕右侧固定位置，格斗游戏惯例）：
            // 命中弹缩、连击越高越红、断连淡出——不再跟着接触点满场乱飞
            var comboCount = UiUtil.MakeText(canvasGo.transform, "ComboCount", "", 54,
                TextAnchor.MiddleRight, new Color(1f, 0.85f, 0.3f));
            UiUtil.SetRect(comboCount, new Vector2(1f, 1f), new Vector2(-220, -250), new Vector2(360, 70));
            comboCount.fontStyle = FontStyle.Bold;
            var ccColor = comboCount.color; ccColor.a = 0f; comboCount.color = ccColor;
            hud.comboCountText = comboCount;

            // 右上角功能按钮 + 面板
            var spawnerPanel = EnemySpawnerPanel.Create(canvasGo.transform, SpawnEnemyNearPlayer);
            var promptPanel = PromptConfigPanel.Create(canvasGo.transform);
            UiUtil.MakeButton(canvasGo.transform, "敌人+", new Vector2(1, 1), new Vector2(-95, -42),
                new Vector2(150, 64), new Color(0.6f, 0.25f, 0.2f, 0.8f), spawnerPanel.Toggle, 26);
            UiUtil.MakeButton(canvasGo.transform, "AI台词", new Vector2(1, 1), new Vector2(-265, -42),
                new Vector2(150, 64), new Color(0.25f, 0.35f, 0.6f, 0.8f), promptPanel.Toggle, 26);
            var characterPanel = CharacterPanel.Create(canvasGo.transform, _appearance);
            UiUtil.MakeButton(canvasGo.transform, "角色", new Vector2(1, 1), new Vector2(-435, -42),
                new Vector2(150, 64), new Color(0.3f, 0.5f, 0.4f, 0.8f),
                characterPanel.Toggle, 26);
            var aiLogPanel = AiLogPanel.Create(canvasGo.transform);
            UiUtil.MakeButton(canvasGo.transform, "日志", new Vector2(1, 1), new Vector2(-605, -42),
                new Vector2(150, 64), new Color(0.4f, 0.4f, 0.3f, 0.8f), aiLogPanel.Toggle, 26);
            var profilePanel = ProfilePanel.Create(canvasGo.transform);
            UiUtil.MakeButton(canvasGo.transform, "画像", new Vector2(1, 1), new Vector2(-775, -42),
                new Vector2(150, 64), new Color(0.5f, 0.35f, 0.55f, 0.8f), profilePanel.Toggle, 26);
            var movesPanel = MovesPanel.Create(canvasGo.transform);
            UiUtil.MakeButton(canvasGo.transform, "招式", new Vector2(1, 1), new Vector2(-945, -42),
                new Vector2(150, 64), new Color(0.55f, 0.45f, 0.25f, 0.8f), movesPanel.Toggle, 26);
            UiUtil.MakeButton(canvasGo.transform, "视角", new Vector2(1, 1), new Vector2(-95, -116),
                new Vector2(150, 64), new Color(0.35f, 0.45f, 0.55f, 0.8f), () =>
                {
                    var cam = Object.FindFirstObjectByType<ThirdPersonCamera>();
                    if (cam != null) cam.CyclePreset();
                }, 26);
            var debugPanel = DebugMovesPanel.Create(canvasGo.transform);
            UiUtil.MakeButton(canvasGo.transform, "测试", new Vector2(1, 1), new Vector2(-265, -116),
                new Vector2(150, 64), new Color(0.45f, 0.35f, 0.5f, 0.8f), debugPanel.Toggle, 26);
            var settingsPanel = SettingsPanel.Create(canvasGo.transform);
            UiUtil.MakeButton(canvasGo.transform, "设置", new Vector2(1, 1), new Vector2(-435, -116),
                new Vector2(150, 64), new Color(0.4f, 0.4f, 0.45f, 0.8f), settingsPanel.Toggle, 26);
            var reflectionPanel = ReflectionPanel.Create(canvasGo.transform);
            UiUtil.MakeButton(canvasGo.transform, "复盘", new Vector2(1, 1), new Vector2(-605, -116),
                new Vector2(150, 64), new Color(0.35f, 0.45f, 0.4f, 0.8f), reflectionPanel.Toggle, 26);

            // 安全屋枢纽：复盘 / 技能树 / 装备套装 / 敌人图鉴 / 旧事档案 / 关卡传送
            var growthPanel = GrowthPanel.Create(canvasGo.transform);
            var equipmentPanel = EquipmentPanel.Create(canvasGo.transform);
            var codexPanel = CodexPanel.Create(canvasGo.transform);
            var archivePanel = ArchivePanel.Create(canvasGo.transform);
            var levelSelectPanel = LevelSelectPanel.Create(canvasGo.transform);
            var missionBoardPanel = MissionBoardPanel.Create(canvasGo.transform);
            var safeHousePanel = SafeHousePanel.Create(canvasGo.transform,
                reflectionPanel, growthPanel, equipmentPanel, codexPanel, archivePanel,
                levelSelectPanel, missionBoardPanel);
            UiUtil.MakeButton(canvasGo.transform, "安全屋", new Vector2(1, 1), new Vector2(-775, -116),
                new Vector2(150, 64), new Color(0.5f, 0.42f, 0.25f, 0.85f), safeHousePanel.Toggle, 26);
            // 传送直达按钮：跨区域章节（如第八章回噪声街区）不再靠走路找入口
            UiUtil.MakeButton(canvasGo.transform, "传送", new Vector2(1, 1), new Vector2(-945, -116),
                new Vector2(150, 64), new Color(0.3f, 0.45f, 0.6f, 0.85f), levelSelectPanel.Toggle, 26);

            // 言语攻防（快速选择式）：敌人心理攻击时弹出三选一回应面板
            canvasGo.AddComponent<VerbalDefenseController>();

            // 第五阶段：画像驱动的个性化遭遇战
            var director = new GameObject("EncounterDirector").AddComponent<EncounterDirector>();
            director.spawner = SpawnEnemy;

            BuildBattleFlowPanel(canvasGo.transform);
        }

        void BuildBattleFlowPanel(Transform canvas)
        {
            _battleFlow = canvas.gameObject.AddComponent<BattleFlowController>();

            var panel = new GameObject("BattlePanel", typeof(Image));
            panel.transform.SetParent(canvas, false);
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0, 0, 0, 0.8f);

            var titleText = UiUtil.MakeText(panel.transform, "Title", "", 56,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(titleText, new Vector2(0.5f, 0.78f), Vector2.zero, new Vector2(1400, 80));

            var detailText = UiUtil.MakeText(panel.transform, "Detail", "", 30,
                TextAnchor.MiddleCenter, Color.white);
            UiUtil.SetRect(detailText, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400, 380));

            var btnText = UiUtil.MakeButton(panel.transform, "继续", new Vector2(0.5f, 0.16f),
                Vector2.zero, new Vector2(380, 92), new Color(0.85f, 0.45f, 0.2f, 0.95f),
                () => _battleFlow.OnConfirm(), 34);

            _battleFlow.panel = panel;
            _battleFlow.titleText = titleText;
            _battleFlow.detailText = detailText;
            _battleFlow.buttonText = btnText.GetComponentInChildren<Text>();
            panel.SetActive(false);
        }

        RectTransform MakeCineBar(Transform canvas, bool top)
        {
            var go = new GameObject(top ? "CineTop" : "CineBottom", typeof(Image));
            go.transform.SetParent(canvas, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = top ? new Vector2(0, 1) : new Vector2(0, 0);
            rt.anchorMax = top ? new Vector2(1, 1) : new Vector2(1, 0);
            rt.pivot = top ? new Vector2(0.5f, 1) : new Vector2(0.5f, 0);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0, 0);
            var img = go.GetComponent<Image>();
            img.color = Color.black;
            img.raycastTarget = false;
            return rt;
        }

        StatBar CreateBar(Transform parent, string label, int index, Color fillColor)
        {
            float y = -30 - index * 34;

            var root = new GameObject("Bar_" + label, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var rt = root.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(20, y);
            rt.sizeDelta = new Vector2(320, 26);

            var lt = UiUtil.MakeText(root.transform, "Label", label, 18, TextAnchor.MiddleLeft, Color.white);
            var lrt = lt.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = new Vector2(0, 1);
            lrt.pivot = new Vector2(0, 0.5f);
            lrt.anchoredPosition = Vector2.zero;
            lrt.sizeDelta = new Vector2(60, 0);

            var sliderGo = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            sliderGo.transform.SetParent(root.transform, false);
            var srt = sliderGo.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0, 0);
            srt.anchorMax = new Vector2(1, 1);
            srt.offsetMin = new Vector2(65, 4);
            srt.offsetMax = new Vector2(0, -4);

            var bg = new GameObject("Background", typeof(Image));
            bg.transform.SetParent(sliderGo.transform, false);
            var bgrt = bg.GetComponent<RectTransform>();
            bgrt.anchorMin = Vector2.zero;
            bgrt.anchorMax = Vector2.one;
            bgrt.offsetMin = Vector2.zero;
            bgrt.offsetMax = Vector2.zero;
            bg.GetComponent<Image>().color = new Color(0, 0, 0, 0.6f);
            bg.GetComponent<Image>().raycastTarget = false;

            var fillArea = new GameObject("FillArea", typeof(RectTransform));
            fillArea.transform.SetParent(sliderGo.transform, false);
            var fart = fillArea.GetComponent<RectTransform>();
            fart.anchorMin = Vector2.zero;
            fart.anchorMax = Vector2.one;
            fart.offsetMin = Vector2.zero;
            fart.offsetMax = Vector2.zero;

            var fill = new GameObject("Fill", typeof(Image));
            fill.transform.SetParent(fillArea.transform, false);
            var frt = fill.GetComponent<RectTransform>();
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;
            fill.GetComponent<Image>().color = fillColor;
            fill.GetComponent<Image>().raycastTarget = false;

            var slider = sliderGo.GetComponent<Slider>();
            slider.fillRect = frt;
            slider.interactable = false;
            slider.transition = Selectable.Transition.None;
            slider.minValue = 0;
            slider.maxValue = 1;
            slider.value = 1;

            var bar = root.AddComponent<StatBar>();
            bar.slider = slider;
            return bar;
        }

        // ================= 任务与剧情 =================

        void SetupChapterQuest()
        {
            var qm = QuestManager.Instance;
            var story = StoryManager.Instance;
            if (qm == null || story == null || story.AllCleared) return;
            var ch = story.Current;
            string questId = "chapter_q_" + story.Chapter;
            foreach (var q in qm.activeQuests)
                if (q.questId == questId) { GameEvents.RaiseQuestUpdated(questId); return; }
            qm.AddQuest(new QuestData
            {
                questId = questId,
                title = ch.title + "：前往" + ZoneBuilder.ZoneNameOf(ch.zoneIndex) +
                        "，击败【" + EnemyCatalog.TierLabel(ch.enemyTier) + "·" +
                        EnemyCatalog.TypeLabel(ch.enemyType) + "】",
                type = QuestType.Main,
                sceneId = ZoneBuilder.ZoneIdOf(ch.zoneIndex),
                relatedWeakness = Personalization.WeaknessAxis.Procrastination,
                objectives = new System.Collections.Generic.List<QuestObjective>
                {
                    new QuestObjective
                    {
                        description = "击败章节心魔",
                        targetEnemyId = ch.enemyId
                    }
                }
            });
        }

        void ShowChapterIntro()
        {
            var story = StoryManager.Instance;
            if (_battleFlow == null || story == null) return;
            if (story.AllCleared)
            {
                _battleFlow.ShowStory("自由修炼",
                    "主线已完结。\n用右上角「敌人+」添加任意类型与难度的心魔，继续磨炼自己。",
                    "开始");
                return;
            }
            var ch = story.Current;
            // 大章-子章结构：标题显示所属成长线，正文首行点出子章与线主题
            _battleFlow.ShowStory(story.CurrentAct.title,
                "【" + ch.title + "】\n" + story.CurrentAct.theme + "\n\n" + ch.intro, "出发");
        }

        // ================= 工具 =================

        void Paint(GameObject go, Color c)
        {
            var r = go.GetComponent<MeshRenderer>();
            if (r == null) return;
            Material m;
            if (baseMaterial != null) m = new Material(baseMaterial);
            else
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                m = new Material(shader);
            }
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            r.sharedMaterial = m;
        }
    }
}
