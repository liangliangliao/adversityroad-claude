using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using Unity.AI.Navigation;
using AdversityRoad.AI;
using AdversityRoad.Combat;
using AdversityRoad.Mobile;
using AdversityRoad.Player;
using AdversityRoad.Quest;
using AdversityRoad.UI;

namespace AdversityRoad.Core
{
    /// <summary>
    /// 运行时一键搭建训练武馆：地面、围墙、NavMesh、玩家、敌人、Boss、
    /// 机制物件（目标板/拖延泥潭）、HUD、触屏操控、任务与胜负流程。
    /// 挂在构建列表内场景（SampleScene）的空物体上即可。
    /// CI 无头打包不经过编辑器手工建场，因此所有内容必须运行时生成。
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        [Tooltip("URP Lit 基础材质：保证 Shader 打进包体，运行时按颜色实例化")]
        public Material baseMaterial;

        [Tooltip("心理安全设置资产；为空时运行时创建默认值")]
        public SafetySettings safetySettings;

        public const string BossId = "boss_procrastination_shadow";

        PlayerController _player;

        void Start()
        {
            // 场景重载时系统单例仍在，但世界内容需要重建
            if (Object.FindFirstObjectByType<PlayerController>() != null) return;

            EnsureSystems();
            BuildArena();
            BakeNavMesh();
            BuildMechanics();
            BuildPlayer();
            BuildCamera();
            SpawnEnemies();
            BuildHUD();
            SetupQuests();
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
            }
            if (gm.safety == null)
                gm.safety = safetySettings != null
                    ? safetySettings
                    : ScriptableObject.CreateInstance<SafetySettings>();
        }

        // ================= 场地 =================

        void BuildArena()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0, -0.5f, 0);
            ground.transform.localScale = new Vector3(50, 1, 50);
            Paint(ground, new Color(0.35f, 0.35f, 0.38f));

            CreateWall(new Vector3(0, 1.5f, 25), new Vector3(50, 3, 1));
            CreateWall(new Vector3(0, 1.5f, -25), new Vector3(50, 3, 1));
            CreateWall(new Vector3(25, 1.5f, 0), new Vector3(1, 3, 50));
            CreateWall(new Vector3(-25, 1.5f, 0), new Vector3(1, 3, 50));

            // 训练柱：提供掩体与走位参照
            CreatePillar(new Vector3(9, 1.5f, 9));
            CreatePillar(new Vector3(-9, 1.5f, 9));
            CreatePillar(new Vector3(9, 1.5f, -9));
            CreatePillar(new Vector3(-9, 1.5f, -9));
        }

        void CreateWall(Vector3 pos, Vector3 scale)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Wall";
            wall.transform.position = pos;
            wall.transform.localScale = scale;
            Paint(wall, new Color(0.25f, 0.25f, 0.28f));
        }

        void CreatePillar(Vector3 pos)
        {
            var pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pillar.name = "Pillar";
            pillar.transform.position = pos;
            pillar.transform.localScale = new Vector3(1.5f, 3, 1.5f);
            Paint(pillar, new Color(0.45f, 0.38f, 0.3f));
        }

        void BakeNavMesh()
        {
            var navGo = new GameObject("Navigation");
            var surface = navGo.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
        }

        void BuildMechanics()
        {
            // 目标板：靠近按 E / 触屏"互"键蓄力，恢复意志与决断
            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "GoalBoard";
            board.transform.position = new Vector3(5, 1.2f, -12);
            board.transform.localScale = new Vector3(3, 1.8f, 0.2f);
            Paint(board, new Color(0.95f, 0.8f, 0.25f));
            board.AddComponent<GoalBoard>();

            // 拖延泥潭：通往 Boss 的必经区域，减速 + 决断流失
            var mireVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mireVisual.name = "MireVisual";
            mireVisual.transform.position = new Vector3(0, 0.03f, 11);
            mireVisual.transform.localScale = new Vector3(10, 0.06f, 8);
            Paint(mireVisual, new Color(0.12f, 0.08f, 0.18f));
            Destroy(mireVisual.GetComponent<Collider>());

            var mire = new GameObject("MireZone");
            mire.transform.position = new Vector3(0, 1, 11);
            var mireCol = mire.AddComponent<BoxCollider>();
            mireCol.size = new Vector3(10, 2, 8);
            mire.AddComponent<ProcrastinationMire>();
        }

        // ================= 玩家与镜头 =================

        void BuildPlayer()
        {
            var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Player";
            player.transform.position = new Vector3(0, 1.1f, -10);
            Destroy(player.GetComponent<CapsuleCollider>());
            Paint(player, new Color(0.2f, 0.5f, 1f));

            var cc = player.AddComponent<CharacterController>();
            cc.height = 2f;
            cc.center = Vector3.zero;
            cc.radius = 0.4f;

            _player = player.AddComponent<PlayerController>();
            player.AddComponent<CombatStateMachine>();
            var combat = player.AddComponent<PlayerCombatController>();
            player.AddComponent<LockOnSystem>();
            var skillExec = player.AddComponent<SkillExecutor>();

            var hurt = new GameObject("PlayerHurtbox");
            hurt.transform.SetParent(player.transform, false);
            var hurtCol = hurt.AddComponent<CapsuleCollider>();
            hurtCol.isTrigger = true;
            hurtCol.height = 2f;
            hurtCol.radius = 0.45f;
            hurt.AddComponent<Hurtbox>();

            var hitbox = CreateAttackHitbox(player.transform);
            combat.weaponHitbox = hitbox;
            skillExec.weaponHitbox = hitbox;

            EquipSkills(skillExec);
        }

        void EquipSkills(SkillExecutor exec)
        {
            var qibu = ScriptableObject.CreateInstance<Data.SkillDefinition>();
            qibu.skillId = "qibu_zhan";
            qibu.displayName = "起步斩";
            qibu.description = "对抗拖延的第一击：高伤害突进斩，打断敌人节奏。";
            qibu.staminaCost = 20;
            qibu.physicalDamage = 40;
            qibu.postureDamage = 30;
            qibu.knockback = 3;
            qibu.cooldown = 6;
            qibu.castLockTime = 0.5f;
            qibu.hitboxOpenTime = 0.35f;
            exec.equippedSkills.Add(qibu);

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
            _player.cameraTransform = camGo.transform;
        }

        // ================= 敌人 =================

        void SpawnEnemies()
        {
            SpawnEnemy("Enemy_TomorrowPhantom", new Vector3(6, 1.1f, 2), 1f,
                new Color(0.5f, 0.3f, 0.7f),
                new EnemyProfile
                {
                    enemyId = "enemy_tomorrow_phantom",
                    displayName = "明日幻影",
                    category = EnemyCategory.Internal,
                    targetWeakness = Personalization.WeaknessAxis.Procrastination,
                    maxHealth = 80, posture = 30,
                    physicalDamage = 6, mentalDamage = 10,
                    aggression = 0.4f, defense = 5, moveSpeed = 2.5f,
                    attackRange = 1.8f, detectRange = 15
                });

            SpawnEnemy("Enemy_CoughAssassin", new Vector3(-7, 1.1f, 4), 1f,
                new Color(0.9f, 0.4f, 0.2f),
                new EnemyProfile
                {
                    enemyId = "enemy_cough_assassin",
                    displayName = "咳声刺客",
                    category = EnemyCategory.Hybrid,
                    targetWeakness = Personalization.WeaknessAxis.NoiseSensitivity,
                    maxHealth = 100, posture = 40,
                    physicalDamage = 12, mentalDamage = 12,
                    aggression = 0.7f, defense = 8, moveSpeed = 4.5f,
                    attackRange = 1.8f, detectRange = 14
                });

            SpawnEnemy("Enemy_SelfDoubtWhisper", new Vector3(-3, 1.1f, 7), 1f,
                new Color(0.35f, 0.55f, 0.6f),
                new EnemyProfile
                {
                    enemyId = "enemy_selfdoubt_whisper",
                    displayName = "自我怀疑低语",
                    category = EnemyCategory.Internal,
                    targetWeakness = Personalization.WeaknessAxis.SelfDoubt,
                    maxHealth = 70, posture = 25,
                    physicalDamage = 4, mentalDamage = 14,
                    aggression = 0.5f, defense = 4, moveSpeed = 3f,
                    attackRange = 1.8f, detectRange = 13
                });

            SpawnEnemy("Boss_ProcrastinationShadow", new Vector3(0, 1.6f, 18), 1.5f,
                new Color(0.2f, 0.1f, 0.3f),
                new EnemyProfile
                {
                    enemyId = BossId,
                    displayName = "拖延影魔",
                    category = EnemyCategory.Boss,
                    targetWeakness = Personalization.WeaknessAxis.Procrastination,
                    maxHealth = 300, posture = 80,
                    physicalDamage = 15, mentalDamage = 18,
                    aggression = 0.6f, defense = 15, moveSpeed = 3.2f,
                    attackRange = 2.2f, detectRange = 13
                });
        }

        void SpawnEnemy(string name, Vector3 pos, float scale, Color color, EnemyProfile profile)
        {
            var enemy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            enemy.name = name;
            enemy.transform.position = pos;
            enemy.transform.localScale = Vector3.one * scale;
            Paint(enemy, color);

            var agent = enemy.AddComponent<NavMeshAgent>();
            agent.speed = profile.moveSpeed;
            agent.stoppingDistance = profile.attackRange * 0.8f;

            var ec = enemy.AddComponent<EnemyController>();
            ec.profile = profile;

            var hurt = new GameObject("Hurtbox");
            hurt.transform.SetParent(enemy.transform, false);
            var hurtCol = hurt.AddComponent<CapsuleCollider>();
            hurtCol.isTrigger = true;
            hurtCol.height = 2f;
            hurtCol.radius = 0.5f;
            hurt.AddComponent<Hurtbox>();

            ec.attackHitbox = CreateAttackHitbox(enemy.transform);
        }

        Hitbox CreateAttackHitbox(Transform parent)
        {
            var atk = GameObject.CreatePrimitive(PrimitiveType.Cube);
            atk.name = "AttackHitbox";
            atk.transform.SetParent(parent, false);
            atk.transform.localPosition = new Vector3(0, 0, 1f);
            atk.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);
            atk.GetComponent<BoxCollider>().isTrigger = true;
            atk.GetComponent<MeshRenderer>().enabled = false;
            var rb = atk.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            return atk.AddComponent<Hitbox>();
        }

        // ================= HUD 与流程 =================

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

            hud.hpBar        = CreateBar(canvasGo.transform, "HP",   0, new Color(0.85f, 0.2f, 0.2f));
            hud.willBar      = CreateBar(canvasGo.transform, "意志", 1, new Color(0.95f, 0.75f, 0.2f));
            hud.focusBar     = CreateBar(canvasGo.transform, "专注", 2, new Color(0.2f, 0.7f, 0.95f));
            hud.selfWorthBar = CreateBar(canvasGo.transform, "自尊", 3, new Color(0.6f, 0.4f, 0.9f));
            hud.boundaryBar  = CreateBar(canvasGo.transform, "边界", 4, new Color(0.3f, 0.8f, 0.5f));
            hud.resolveBar   = CreateBar(canvasGo.transform, "决断", 5, new Color(0.95f, 0.5f, 0.3f));

            var questGo = new GameObject("QuestText", typeof(Text));
            questGo.transform.SetParent(canvasGo.transform, false);
            var qrt = questGo.GetComponent<RectTransform>();
            qrt.anchorMin = new Vector2(0.5f, 1);
            qrt.anchorMax = new Vector2(0.5f, 1);
            qrt.pivot = new Vector2(0.5f, 1);
            qrt.anchoredPosition = new Vector2(0, -20);
            qrt.sizeDelta = new Vector2(900, 40);
            var qText = questGo.GetComponent<Text>();
            qText.font = DefaultFont();
            qText.fontSize = 26;
            qText.alignment = TextAnchor.MiddleCenter;
            qText.color = Color.white;
            hud.questText = qText;

            BuildBattleFlowPanel(canvasGo.transform);
        }

        void BuildBattleFlowPanel(Transform canvas)
        {
            var flow = canvas.gameObject.AddComponent<BattleFlowController>();
            flow.bossEnemyId = BossId;

            var panel = new GameObject("BattlePanel", typeof(Image));
            panel.transform.SetParent(canvas, false);
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0, 0, 0, 0.75f);

            var title = new GameObject("Title", typeof(Text));
            title.transform.SetParent(panel.transform, false);
            var trt = title.GetComponent<RectTransform>();
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.65f);
            trt.sizeDelta = new Vector2(1200, 90);
            var titleText = title.GetComponent<Text>();
            titleText.font = DefaultFont();
            titleText.fontSize = 60;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.color = new Color(0.95f, 0.85f, 0.4f);

            var detail = new GameObject("Detail", typeof(Text));
            detail.transform.SetParent(panel.transform, false);
            var drt = detail.GetComponent<RectTransform>();
            drt.anchorMin = drt.anchorMax = new Vector2(0.5f, 0.48f);
            drt.sizeDelta = new Vector2(1300, 160);
            var detailText = detail.GetComponent<Text>();
            detailText.font = DefaultFont();
            detailText.fontSize = 30;
            detailText.alignment = TextAnchor.MiddleCenter;
            detailText.color = Color.white;

            var btnGo = new GameObject("ConfirmButton", typeof(Image), typeof(Button));
            btnGo.transform.SetParent(panel.transform, false);
            var brt = btnGo.GetComponent<RectTransform>();
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.28f);
            brt.sizeDelta = new Vector2(360, 90);
            btnGo.GetComponent<Image>().color = new Color(0.85f, 0.45f, 0.2f, 0.95f);

            var btnLabel = new GameObject("Label", typeof(Text));
            btnLabel.transform.SetParent(btnGo.transform, false);
            var blrt = btnLabel.GetComponent<RectTransform>();
            blrt.anchorMin = Vector2.zero;
            blrt.anchorMax = Vector2.one;
            blrt.offsetMin = Vector2.zero;
            blrt.offsetMax = Vector2.zero;
            var btnText = btnLabel.GetComponent<Text>();
            btnText.font = DefaultFont();
            btnText.fontSize = 34;
            btnText.alignment = TextAnchor.MiddleCenter;
            btnText.color = Color.white;

            btnGo.GetComponent<Button>().onClick.AddListener(flow.OnConfirm);

            flow.panel = panel;
            flow.titleText = titleText;
            flow.detailText = detailText;
            flow.buttonText = btnText;
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

            var labelGo = new GameObject("Label", typeof(Text));
            labelGo.transform.SetParent(root.transform, false);
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = new Vector2(0, 1);
            lrt.pivot = new Vector2(0, 0.5f);
            lrt.anchoredPosition = Vector2.zero;
            lrt.sizeDelta = new Vector2(60, 0);
            var lt = labelGo.GetComponent<Text>();
            lt.font = DefaultFont();
            lt.fontSize = 18;
            lt.alignment = TextAnchor.MiddleLeft;
            lt.color = Color.white;
            lt.text = label;

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

        // ================= 任务 =================

        void SetupQuests()
        {
            var qm = QuestManager.Instance;
            if (qm == null) return;

            AddQuestOnce(qm, new QuestData
            {
                questId = "q_dojo_phantom",
                title = "训练试炼：击败 明日幻影",
                type = QuestType.Training,
                sceneId = "SC_TrainingDojo",
                relatedWeakness = Personalization.WeaknessAxis.Procrastination,
                objectives = new System.Collections.Generic.List<QuestObjective>
                {
                    new QuestObjective { description = "击败明日幻影", targetEnemyId = "enemy_tomorrow_phantom" }
                }
            });

            AddQuestOnce(qm, new QuestData
            {
                questId = "q_dojo_assassin",
                title = "训练试炼：击败 咳声刺客 与 自我怀疑低语",
                type = QuestType.Training,
                sceneId = "SC_TrainingDojo",
                relatedWeakness = Personalization.WeaknessAxis.NoiseSensitivity,
                objectives = new System.Collections.Generic.List<QuestObjective>
                {
                    new QuestObjective { description = "击败咳声刺客", targetEnemyId = "enemy_cough_assassin" },
                    new QuestObjective { description = "击败自我怀疑低语", targetEnemyId = "enemy_selfdoubt_whisper" }
                }
            });

            AddQuestOnce(qm, new QuestData
            {
                questId = "q_dojo_boss",
                title = "主线试炼：穿过拖延泥潭，击败 拖延影魔",
                type = QuestType.Main,
                sceneId = "SC_TrainingDojo",
                relatedWeakness = Personalization.WeaknessAxis.Procrastination,
                objectives = new System.Collections.Generic.List<QuestObjective>
                {
                    new QuestObjective { description = "击败拖延影魔", targetEnemyId = BossId }
                },
                rewardSkillIds = new System.Collections.Generic.List<string> { "qibu_zhan" }
            });
        }

        static void AddQuestOnce(QuestManager qm, QuestData quest)
        {
            foreach (var q in qm.activeQuests)
                if (q.questId == quest.questId)
                {
                    GameEvents.RaiseQuestUpdated(q.questId); // 刷新 HUD 任务提示
                    return;
                }
            qm.AddQuest(quest);
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

        static Font DefaultFont() =>
            Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }
}
