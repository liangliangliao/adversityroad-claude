using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.AI.Navigation;
using AdversityRoad.Core;
using AdversityRoad.Player;
using AdversityRoad.Combat;
using AdversityRoad.AI;
using AdversityRoad.Quest;
using AdversityRoad.UI;
using AdversityRoad.Personalization;

namespace AdversityRoad.EditorTools
{
    /// <summary>
    /// 一键搭建训练武馆：菜单栏 AdversityRoad → 一键搭建训练武馆。
    /// 自动创建：地面+NavMesh、玩家(全组件)、敌人、镜头、GameSystems、SafetySettings 资产、HUD。
    /// 本文件必须放在名为 Editor 的文件夹下（如 Assets/_Project/Editor/）。
    /// </summary>
    public static class TrainingDojoBuilder
    {
        [MenuItem("AdversityRoad/一键搭建训练武馆")]
        public static void Build()
        {
            // 新建空场景
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // ---------- 地面 + 导航 ----------
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(5, 1, 5);
            GameObjectUtility.SetStaticEditorFlags(ground, StaticEditorFlags.NavigationStatic);
            SetColor(ground, new Color(0.35f, 0.35f, 0.38f));

            // 四周围墙防掉落
            CreateWall(new Vector3(0, 1, 25), new Vector3(50, 2, 1));
            CreateWall(new Vector3(0, 1, -25), new Vector3(50, 2, 1));
            CreateWall(new Vector3(25, 1, 0), new Vector3(1, 2, 50));
            CreateWall(new Vector3(-25, 1, 0), new Vector3(1, 2, 50));

            // ---------- 玩家 ----------
            var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Player";
            player.transform.position = new Vector3(0, 1.1f, 0);
            Object.DestroyImmediate(player.GetComponent<CapsuleCollider>());
            SetColor(player, new Color(0.2f, 0.5f, 1f));

            var cc = player.AddComponent<CharacterController>();
            cc.height = 2f; cc.center = new Vector3(0, 0, 0); cc.radius = 0.4f;

            var pCtrl = player.AddComponent<PlayerController>();
            player.AddComponent<CombatStateMachine>();
            var pCombat = player.AddComponent<PlayerCombatController>();
            player.AddComponent<LockOnSystem>();
            player.AddComponent<SkillExecutor>();

            // 玩家受击框（子物体）
            var pHurt = new GameObject("PlayerHurtbox");
            pHurt.transform.SetParent(player.transform, false);
            var pHurtCol = pHurt.AddComponent<CapsuleCollider>();
            pHurtCol.isTrigger = true; pHurtCol.height = 2f; pHurtCol.radius = 0.45f;
            pHurt.AddComponent<Hurtbox>();

            // 玩家武器攻击框（子物体，前方）
            var pWeapon = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pWeapon.name = "WeaponHitbox";
            pWeapon.transform.SetParent(player.transform, false);
            pWeapon.transform.localPosition = new Vector3(0, 0, 1f);
            pWeapon.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);
            pWeapon.GetComponent<BoxCollider>().isTrigger = true;
            pWeapon.GetComponent<MeshRenderer>().enabled = false; // 隐藏可视网格
            var pWeaponRb = pWeapon.AddComponent<Rigidbody>();
            pWeaponRb.isKinematic = true; pWeaponRb.useGravity = false;
            var pHitbox = pWeapon.AddComponent<Hitbox>();

            pCombat.weaponHitbox = pHitbox;
            var skillExec = player.GetComponent<SkillExecutor>();
            skillExec.weaponHitbox = pHitbox;

            // ---------- 镜头 ----------
            var cam = Camera.main != null ? Camera.main.gameObject : new GameObject("Main Camera", typeof(Camera));
            cam.tag = "MainCamera";
            var tpc = cam.AddComponent<ThirdPersonCamera>();
            tpc.target = player.transform;
            tpc.player = pCtrl;
            pCtrl.cameraTransform = cam.transform;

            // ---------- 敌人 ----------
            CreateEnemy("Enemy_TomorrowPhantom", new Vector3(6, 1.1f, 6),
                new EnemyProfile
                {
                    enemyId = "enemy_tomorrow_phantom",
                    displayName = "明日幻影",
                    category = EnemyCategory.Internal,
                    targetWeakness = WeaknessAxis.Procrastination,
                    maxHealth = 80, posture = 30,
                    physicalDamage = 6, mentalDamage = 10,
                    aggression = 0.4f, defense = 5, moveSpeed = 2.5f,
                    attackRange = 1.8f, detectRange = 15
                }, new Color(0.5f, 0.3f, 0.7f));

            CreateEnemy("Enemy_CoughAssassin", new Vector3(-7, 1.1f, 8),
                new EnemyProfile
                {
                    enemyId = "enemy_cough_assassin",
                    displayName = "咳声刺客",
                    category = EnemyCategory.Hybrid,
                    targetWeakness = WeaknessAxis.NoiseSensitivity,
                    maxHealth = 100, posture = 40,
                    physicalDamage = 12, mentalDamage = 12,
                    aggression = 0.7f, defense = 8, moveSpeed = 4.5f,
                    attackRange = 1.8f, detectRange = 14
                }, new Color(0.9f, 0.4f, 0.2f));

            // ---------- 管理器 + 安全设置资产 ----------
            var systems = new GameObject("GameSystems");
            var gm = systems.AddComponent<GameManager>();
            systems.AddComponent<QuestManager>();

            if (!AssetDatabase.IsValidFolder("Assets/_Project"))
                AssetDatabase.CreateFolder("Assets", "_Project");
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Settings"))
                AssetDatabase.CreateFolder("Assets/_Project", "Settings");

            var safety = AssetDatabase.LoadAssetAtPath<SafetySettings>(
                "Assets/_Project/Settings/SafetySettings.asset");
            if (safety == null)
            {
                safety = ScriptableObject.CreateInstance<SafetySettings>();
                AssetDatabase.CreateAsset(safety, "Assets/_Project/Settings/SafetySettings.asset");
            }
            gm.safety = safety;

            // ---------- HUD ----------
            BuildHUD(pCtrl);

            // ---------- 烘焙 NavMesh ----------
            var surface = ground.AddComponent<NavMeshSurface>();
            surface.BuildNavMesh();

            // ---------- 保存场景 ----------
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Scenes"))
                AssetDatabase.CreateFolder("Assets/_Project", "Scenes");
            EditorSceneManager.SaveScene(scene, "Assets/_Project/Scenes/SC_TrainingDojo.unity");
            AssetDatabase.SaveAssets();

            EditorUtility.DisplayDialog("逆境之路",
                "训练武馆搭建完成！\n\n操作：WASD 移动 | Space 跳 | Shift 闪避\n鼠标左/右键 轻/重攻击 | Ctrl 格挡 | Q 锁定 | R 内功\n\n点击 ▶ 开始测试。", "开始");
        }

        // ================= 辅助方法 =================

        static void CreateWall(Vector3 pos, Vector3 scale)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Wall";
            wall.transform.position = pos;
            wall.transform.localScale = scale;
            SetColor(wall, new Color(0.25f, 0.25f, 0.28f));
        }

        static void CreateEnemy(string name, Vector3 pos, EnemyProfile profile, Color color)
        {
            var enemy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            enemy.name = name;
            enemy.transform.position = pos;
            SetColor(enemy, color);

            var agent = enemy.AddComponent<UnityEngine.AI.NavMeshAgent>();
            agent.speed = profile.moveSpeed;
            agent.stoppingDistance = profile.attackRange * 0.8f;

            var ec = enemy.AddComponent<EnemyController>();
            ec.profile = profile;

            // 受击框
            var hurt = new GameObject("Hurtbox");
            hurt.transform.SetParent(enemy.transform, false);
            var hurtCol = hurt.AddComponent<CapsuleCollider>();
            hurtCol.isTrigger = true; hurtCol.height = 2f; hurtCol.radius = 0.5f;
            hurt.AddComponent<Hurtbox>();

            // 攻击框
            var atk = GameObject.CreatePrimitive(PrimitiveType.Cube);
            atk.name = "AttackHitbox";
            atk.transform.SetParent(enemy.transform, false);
            atk.transform.localPosition = new Vector3(0, 0, 1f);
            atk.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);
            atk.GetComponent<BoxCollider>().isTrigger = true;
            atk.GetComponent<MeshRenderer>().enabled = false;
            var rb = atk.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false;
            ec.attackHitbox = atk.AddComponent<Hitbox>();
        }

        static void SetColor(GameObject go, Color c)
        {
            var r = go.GetComponent<MeshRenderer>();
            if (r == null) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader) { color = c };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            r.sharedMaterial = mat;
        }

        static void BuildHUD(PlayerController player)
        {
            var canvasGo = new GameObject("HUD_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGo.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);

            if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
                new GameObject("EventSystem",
                    typeof(UnityEngine.EventSystems.EventSystem),
                    typeof(UnityEngine.EventSystems.StandaloneInputModule));

            var hud = canvasGo.AddComponent<HUDController>();

            // 触屏操作层：真机自动显示；forceShow=true 时编辑器内也显示
            var mobile = canvasGo.AddComponent<AdversityRoad.Mobile.MobileControls>();
            mobile.forceShow = true;

            hud.hpBar        = CreateBar(canvasGo.transform, "HP",     0, new Color(0.85f, 0.2f, 0.2f));
            hud.willBar      = CreateBar(canvasGo.transform, "意志",   1, new Color(0.95f, 0.75f, 0.2f));
            hud.focusBar     = CreateBar(canvasGo.transform, "专注",   2, new Color(0.2f, 0.7f, 0.95f));
            hud.selfWorthBar = CreateBar(canvasGo.transform, "自尊",   3, new Color(0.6f, 0.4f, 0.9f));
            hud.boundaryBar  = CreateBar(canvasGo.transform, "边界",   4, new Color(0.3f, 0.8f, 0.5f));
            hud.actionPowerBar = CreateBar(canvasGo.transform, "行动",   5, new Color(0.95f, 0.5f, 0.3f));

            // 任务提示文字（顶部中央）
            var questGo = new GameObject("QuestText", typeof(Text));
            questGo.transform.SetParent(canvasGo.transform, false);
            var qrt = questGo.GetComponent<RectTransform>();
            qrt.anchorMin = new Vector2(0.5f, 1); qrt.anchorMax = new Vector2(0.5f, 1);
            qrt.pivot = new Vector2(0.5f, 1);
            qrt.anchoredPosition = new Vector2(0, -20);
            qrt.sizeDelta = new Vector2(800, 40);
            var qText = questGo.GetComponent<Text>();
            qText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            qText.fontSize = 24; qText.alignment = TextAnchor.MiddleCenter;
            qText.color = Color.white;
            hud.questText = qText;
        }

        static StatBar CreateBar(Transform parent, string label, int index, Color fillColor)
        {
            float y = -30 - index * 34;

            var root = new GameObject("Bar_" + label, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var rt = root.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(20, y);
            rt.sizeDelta = new Vector2(320, 26);

            // 标签
            var labelGo = new GameObject("Label", typeof(Text));
            labelGo.transform.SetParent(root.transform, false);
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = new Vector2(0, 1);
            lrt.pivot = new Vector2(0, 0.5f);
            lrt.anchoredPosition = new Vector2(0, 0);
            lrt.sizeDelta = new Vector2(60, 0);
            var lt = labelGo.GetComponent<Text>();
            lt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            lt.fontSize = 18; lt.alignment = TextAnchor.MiddleLeft; lt.color = Color.white;
            lt.text = label;

            // Slider
            var sliderGo = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            sliderGo.transform.SetParent(root.transform, false);
            var srt = sliderGo.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 1);
            srt.offsetMin = new Vector2(65, 4); srt.offsetMax = new Vector2(0, -4);

            // 背景
            var bg = new GameObject("Background", typeof(Image));
            bg.transform.SetParent(sliderGo.transform, false);
            var bgrt = bg.GetComponent<RectTransform>();
            bgrt.anchorMin = Vector2.zero; bgrt.anchorMax = Vector2.one;
            bgrt.offsetMin = Vector2.zero; bgrt.offsetMax = Vector2.zero;
            bg.GetComponent<Image>().color = new Color(0, 0, 0, 0.6f);

            // 填充区域
            var fillArea = new GameObject("FillArea", typeof(RectTransform));
            fillArea.transform.SetParent(sliderGo.transform, false);
            var fart = fillArea.GetComponent<RectTransform>();
            fart.anchorMin = Vector2.zero; fart.anchorMax = Vector2.one;
            fart.offsetMin = Vector2.zero; fart.offsetMax = Vector2.zero;

            var fill = new GameObject("Fill", typeof(Image));
            fill.transform.SetParent(fillArea.transform, false);
            var frt = fill.GetComponent<RectTransform>();
            frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
            frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
            fill.GetComponent<Image>().color = fillColor;

            var slider = sliderGo.GetComponent<Slider>();
            slider.fillRect = frt;
            slider.interactable = false;
            slider.transition = Selectable.Transition.None;
            slider.minValue = 0; slider.maxValue = 1; slider.value = 1;

            var bar = root.AddComponent<StatBar>();
            bar.slider = slider;
            return bar;
        }
    }
}
