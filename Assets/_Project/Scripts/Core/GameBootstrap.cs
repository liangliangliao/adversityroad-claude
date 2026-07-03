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
    /// 四大区域（独居小屋/训练武馆/噪声街区/城市广场）+ 昼夜循环 + 行人车辆 +
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
            SpawnChapterEnemy();
            ZoneBuilder.SpawnLife(_world);
            BuildHUD();
            SetupChapterQuest();
            ShowChapterIntro();

            // 云端台词池预热：进区域即后台预取，喊话零延迟
            if (CloudDialogueService.Instance != null)
                CloudDialogueService.Instance.WarmUp(ZoneBuilder.CurrentZoneId,
                    Personalization.WeaknessAxis.Procrastination,
                    Personalization.WeaknessAxis.SelfDoubt,
                    Personalization.WeaknessAxis.NoiseSensitivity,
                    Personalization.WeaknessAxis.Shame);
        }

        void OnEnable() => GameEvents.OnChapterAdvanced += HandleChapterAdvanced;
        void OnDisable() => GameEvents.OnChapterAdvanced -= HandleChapterAdvanced;

        void HandleChapterAdvanced(int newChapter)
        {
            // 上一章敌人正在播放死亡演出，引用置空后生成下一章心魔
            _currentChapterEnemy = null;
            SpawnChapterEnemy();
            SetupChapterQuest();
        }

        int CurrentChapterZone()
        {
            var story = StoryManager.Instance;
            if (story == null || story.AllCleared)
                return StoryManager.Chapters[StoryManager.Chapters.Length - 1].zoneIndex;
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
            hurtCol.height = 2f;
            hurtCol.radius = 0.45f;
            hurt.AddComponent<Hurtbox>();

            var hitbox = CreateAttackHitbox(root.transform, 1.1f);
            combat.weaponHitbox = hitbox;
            skillExec.weaponHitbox = hitbox;

            // 边界盾可视化
            var shield = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shield.name = "GuardShield";
            Object.DestroyImmediate(shield.GetComponent<Collider>());
            shield.transform.SetParent(root.transform, false);
            shield.transform.localPosition = new Vector3(0, 0.2f, 0.7f);
            shield.transform.localScale = new Vector3(1.4f, 1.4f, 0.12f);
            Paint(shield, new Color(0.35f, 0.85f, 0.55f));
            shield.SetActive(false);
            combat.guardShield = shield;

            // 内功光环可视化
            var aura = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            aura.name = "InnerAura";
            Object.DestroyImmediate(aura.GetComponent<Collider>());
            aura.transform.SetParent(root.transform, false);
            aura.transform.localPosition = new Vector3(0, -0.95f, 0);
            aura.transform.localScale = new Vector3(1.7f, 0.05f, 1.7f);
            Paint(aura, new Color(1f, 0.85f, 0.35f));
            aura.SetActive(false);
            combat.innerAura = aura;

            EquipSkills(skillExec);
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

            var qiren = ScriptableObject.CreateInstance<Data.SkillDefinition>();
            qiren.skillId = "zhannian_qiren";
            qiren.displayName = "斩念气刃";
            qiren.description = "凝念成刃远程斩出，在心魔靠近前先发制人。";
            qiren.staminaCost = 15;
            qiren.willCost = 10;
            qiren.physicalDamage = 26;
            qiren.postureDamage = 12;
            qiren.knockback = 2;
            qiren.cooldown = 5;
            qiren.castLockTime = 0.45f;
            qiren.isRanged = true;
            qiren.projectileSpeed = 18f;
            exec.equippedSkills.Add(qiren);
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
            _currentChapterEnemy = SpawnEnemy(ch.enemyType, ch.enemyTier,
                _world.enemySpawns[ch.zoneIndex], false);
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
            var rig = HumanoidRig.Build(visualRoot.transform, new HumanoidRig.Config
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

            var body = root.AddComponent<CapsuleCollider>();
            body.height = 2f;
            body.radius = 0.5f;

            var agent = root.AddComponent<NavMeshAgent>();
            agent.speed = profile.moveSpeed;
            agent.stoppingDistance = profile.attackRange * 0.8f;

            var ec = root.AddComponent<EnemyController>();
            ec.profile = profile;

            var poser = root.AddComponent<HumanoidAnimator>();
            poser.visual = visualRoot.transform;
            poser.rig = rig;
            ec.poser = poser;

            ec.statusBar = EnemyStatusBar.Create(root.transform, profile.displayName, 2.5f);

            var dialogue = root.AddComponent<EnemyDialogue>();
            dialogue.displayName = profile.displayName;
            ec.dialogue = dialogue;

            // 兵器：不同敌方持不同兵器（棍/爪/剑/刀），挂右手随臂挥舞带刀光
            var weaponRig = WeaponFactory.Build(EnemyCatalog.WeaponOf(type), rig.handR,
                baseMaterial, new Vector3(0, -0.06f, 0.03f), new Vector3(112f, 0, 0));
            if (weaponRig != null)
            {
                poser.weaponPivot = weaponRig.pivot;
                poser.weaponTrail = weaponRig.trail;
            }
            ec.themeColor = tc;
            ec.baseMaterial = baseMaterial;

            var hurt = new GameObject("Hurtbox");
            hurt.transform.SetParent(root.transform, false);
            var hurtCol = hurt.AddComponent<CapsuleCollider>();
            hurtCol.isTrigger = true;
            hurtCol.height = 2f;
            hurtCol.radius = 0.55f;
            hurt.AddComponent<Hurtbox>();

            ec.attackHitbox = CreateAttackHitbox(root.transform, 1f);
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
            hud.resolveBar   = CreateBar(canvasGo.transform, "决断", 5, new Color(0.95f, 0.5f, 0.3f));

            // 意势点（黑神话棍势式资源）：属性条下方三枚圆点
            hud.momentumPips = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                var pip = new GameObject("MomentumPip" + i, typeof(Image));
                pip.transform.SetParent(canvasGo.transform, false);
                UiUtil.SetRect(pip.GetComponent<Image>(), new Vector2(0, 1),
                    new Vector2(40 + i * 46, -252), new Vector2(34, 34));
                var img = pip.GetComponent<Image>();
                img.color = new Color(1f, 1f, 1f, 0.18f);
                img.raycastTarget = false;
                hud.momentumPips[i] = img;
            }

            var qText = UiUtil.MakeText(canvasGo.transform, "QuestText", "", 26,
                TextAnchor.MiddleCenter, Color.white);
            UiUtil.SetRect(qText, new Vector2(0.5f, 1f), new Vector2(0, -24), new Vector2(1000, 40));
            hud.questText = qText;

            var subText = UiUtil.MakeText(canvasGo.transform, "Subtitle", "", 28,
                TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.75f));
            UiUtil.SetRect(subText, new Vector2(0.5f, 0f), new Vector2(0, 130), new Vector2(1300, 44));
            hud.subtitleText = subText;

            // 右上角功能按钮 + 面板
            var spawnerPanel = EnemySpawnerPanel.Create(canvasGo.transform, SpawnEnemyNearPlayer);
            var promptPanel = PromptConfigPanel.Create(canvasGo.transform);
            UiUtil.MakeButton(canvasGo.transform, "敌人+", new Vector2(1, 1), new Vector2(-95, -42),
                new Vector2(150, 64), new Color(0.6f, 0.25f, 0.2f, 0.8f), spawnerPanel.Toggle, 26);
            UiUtil.MakeButton(canvasGo.transform, "AI台词", new Vector2(1, 1), new Vector2(-265, -42),
                new Vector2(150, 64), new Color(0.25f, 0.35f, 0.6f, 0.8f), promptPanel.Toggle, 26);
            UiUtil.MakeButton(canvasGo.transform, "角色", new Vector2(1, 1), new Vector2(-435, -42),
                new Vector2(150, 64), new Color(0.3f, 0.5f, 0.4f, 0.8f),
                () => { if (_appearance != null) _appearance.TogglePreset(); }, 26);
            var aiLogPanel = AiLogPanel.Create(canvasGo.transform);
            UiUtil.MakeButton(canvasGo.transform, "日志", new Vector2(1, 1), new Vector2(-605, -42),
                new Vector2(150, 64), new Color(0.4f, 0.4f, 0.3f, 0.8f), aiLogPanel.Toggle, 26);
            var profilePanel = ProfilePanel.Create(canvasGo.transform);
            UiUtil.MakeButton(canvasGo.transform, "画像", new Vector2(1, 1), new Vector2(-775, -42),
                new Vector2(150, 64), new Color(0.5f, 0.35f, 0.55f, 0.8f), profilePanel.Toggle, 26);

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
            _battleFlow.ShowStory(ch.title, ch.intro, "出发");
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
