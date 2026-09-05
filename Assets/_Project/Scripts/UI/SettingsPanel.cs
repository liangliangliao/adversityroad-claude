using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;
using AdversityRoad.Player;
using AdversityRoad.Save;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 设置菜单（第六阶段）：心理安全系统 UI 化——
    /// 心理强度分级 / 台词柔化 / 恢复模式 / 镜头自动跟随 / 数据删除（二次确认）。
    /// 所有心理攻击与台词生成都读取这些开关。
    /// </summary>
    public class SettingsPanel : MonoBehaviour
    {
        GameObject _panel;
        readonly List<(Button btn, MentalIntensity val)> _intensityBtns =
            new List<(Button, MentalIntensity)>();
        Button _softenBtn, _recoveryBtn, _followBtn, _debugBtn, _deleteBtn, _perfBtn;
        Button _lockModeBtn, _aimAssistBtn;
        Button _footLockBtn, _leanBtn, _headFollowBtn, _upperBtn, _magnetBtn, _gradingBtn, _neutralBtn;
        Button _postFxBtn, _singleClipBtn;
        Button _logBtn;
        Text _logPath, _logTarget;
        readonly System.Collections.Generic.List<(Button, float)> _turnBtns =
            new System.Collections.Generic.List<(Button, float)>();
        bool _deleteArmed;

        static readonly Color Off = new Color(0.25f, 0.25f, 0.3f, 0.95f);
        static readonly Color On = new Color(0.2f, 0.55f, 0.35f, 0.95f);

        public static SettingsPanel Create(Transform canvas)
        {
            var comp = canvas.gameObject.AddComponent<SettingsPanel>();
            comp.Build(canvas);
            return comp;
        }

        SafetySettings Safety =>
            GameManager.Instance != null ? GameManager.Instance.safety : null;

        void Build(Transform canvas)
        {
            _panel = UiUtil.MakePanel(canvas, "SettingsPanel", new Vector2(1100, 1720),
                new Color(0.08f, 0.08f, 0.12f, 0.97f));

            var title = UiUtil.MakeText(_panel.transform, "Title", "设 置 · 心理安全", 38,
                TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.4f));
            UiUtil.SetRect(title, new Vector2(0.5f, 1f), new Vector2(0, -44), new Vector2(700, 52));

            var l1 = UiUtil.MakeText(_panel.transform, "L1", "心理攻击强度", 26,
                TextAnchor.MiddleLeft, Color.white);
            UiUtil.SetRect(l1, new Vector2(0.5f, 1f), new Vector2(-300, -120), new Vector2(400, 40));
            (string, MentalIntensity)[] levels =
            {
                ("轻度", MentalIntensity.Light),
                ("标准", MentalIntensity.Standard),
                ("高压", MentalIntensity.HighPressure)
            };
            for (int i = 0; i < levels.Length; i++)
            {
                var lv = levels[i];
                var btn = UiUtil.MakeButton(_panel.transform, lv.Item1, new Vector2(0.5f, 1f),
                    new Vector2(-220 + i * 240, -190), new Vector2(220, 70), Off, () =>
                    {
                        if (Safety != null) Safety.intensity = lv.Item2;
                        Refresh();
                    }, 26);
                _intensityBtns.Add((btn, lv.Item2));
            }

            _softenBtn = MakeToggle("台词柔化（降低攻击性表达）", -290, () =>
            {
                if (Safety != null) Safety.softenDialogue = !Safety.softenDialogue;
                Refresh();
            });
            _recoveryBtn = MakeToggle("恢复模式（停止一切心理攻击）", -380, () =>
            {
                if (Safety != null)
                {
                    Safety.recoveryMode = !Safety.recoveryMode;
                    if (Safety.recoveryMode) GameEvents.RaiseRecoveryMode();
                }
                Refresh();
            });
            _followBtn = MakeToggle("镜头自动跟随", -470, () =>
            {
                var cam = FindObjectOfType<ThirdPersonCamera>();
                if (cam != null) cam.autoFollow = !cam.autoFollow;
                Refresh();
            });
            // 【-560 这一行本来是三个控件叠在一起】骨骼后处理/单片段两颗 545 宽的
            // 按钮在 -564，把这颗 760 宽的整个盖住了——"调试模式"一直点不到。
            // 挪到 -650 与 -822 之间那段空档里。
            // -560 这一档是空的：上一颗在 -470（占到 -505），下一对在 -650（从 -615 起）。
            // 性能读数原来根本没有开关，只能靠改代码关掉——它盖住右半个屏幕。
            _perfBtn = MakeToggle("性能读数（FPS / 帧时 / 移动诊断，默认关）", -560, () =>
            {
                PerfHud.Enabled = !PerfHud.Enabled;
                Refresh();
            });
            _debugBtn = MakeToggle("调试模式（敌人耐揍，不易被打死）", -736, () =>
            {
                GameDebug.TankyEnemies = !GameDebug.TankyEnemies;
                Refresh();
            });

            // 战斗操作偏好（对齐大型动作游戏惯例）：锁定模式 + 攻击吸附
            _lockModeBtn = UiUtil.MakeButton(_panel.transform, "", new Vector2(0.5f, 1f),
                new Vector2(-195, -650), new Vector2(370, 70), Off, () =>
                {
                    LockOnSystem.AutoAcquire = !LockOnSystem.AutoAcquire;
                    Refresh();
                }, 22);
            _aimAssistBtn = UiUtil.MakeButton(_panel.transform, "", new Vector2(0.5f, 1f),
                new Vector2(195, -650), new Vector2(370, 70), Off, () =>
                {
                    LockOnSystem.AimAssist = !LockOnSystem.AimAssist;
                    Refresh();
                }, 22);

            // ===== 移动手感调试开关（本轮新增）=====
            //
            // 上一轮我一次性上了两个全新机制（支撑脚锁定、转向倾身）又把转向速率
            // 砍掉六成——三件事一起改，实机一说"更烂了"就根本分不清是哪一个，
            // 只能靠再推四个构建去二分。那是方法错误，代价由玩家承担。
            // 全部挂到这里：一个包就能自己定位，不必等我一轮轮试。
            for (int ti = 0; ti < 3; ti++)
            {
                var lv = ti == 0 ? ("转向轻 12", 12f)
                       : ti == 1 ? ("转向中 9", 9f)
                                 : ("转向重 6", 6f);
                float accel = lv.Item2;
                var b = UiUtil.MakeButton(_panel.transform, lv.Item1, new Vector2(0.5f, 1f),
                    new Vector2(-256 + ti * 256, -736), new Vector2(246, 70), Off, () =>
                    {
                        PlayerController.TurnAccelOverride = accel;
                        Refresh();
                    }, 22);
                _turnBtns.Add((b, accel));
            }
            _footLockBtn = UiUtil.MakeButton(_panel.transform, "", new Vector2(0.5f, 1f),
                new Vector2(-195, -822), new Vector2(370, 70), Off, () =>
                {
                    Combat.HumanoidAnimator.FootLockOn = !Combat.HumanoidAnimator.FootLockOn;
                    Refresh();
                }, 22);
            _leanBtn = UiUtil.MakeButton(_panel.transform, "", new Vector2(0.5f, 1f),
                new Vector2(195, -822), new Vector2(370, 70), Off, () =>
                {
                    Combat.HumanoidAnimator.TurnLeanOn = !Combat.HumanoidAnimator.TurnLeanOn;
                    Refresh();
                }, 22);

            // ===== 二分定位：两个开关把"漂移"锁进一半 =====
            // 找了八轮都是"找到一个机制→修掉→照旧"，再猜第九个没有意义。
            // 这两个开关各切掉一整条链路，玩家半分钟就能告诉我在哪一半：
            //   · 关「骨骼后处理」不漂 ⇒ 原因在动画图**下游**（钉髋/倾身/拧腰/
            //     前摇/贴地校准/锁脚）；
            //   · 开「单片段」不漂     ⇒ 原因在**混合**（相位、跨片段权重）；
            //   · 两个都试了还漂       ⇒ 与动画无关，去查角色位置与镜头。
            _postFxBtn = UiUtil.MakeButton(_panel.transform, "", new Vector2(0.5f, 1f),
                new Vector2(-277, -564), new Vector2(545, 70), Off, () =>
                {
                    Combat.HumanoidAnimator.BonePostFxOn =
                        !Combat.HumanoidAnimator.BonePostFxOn;
                    Refresh();
                }, 22);
            _singleClipBtn = UiUtil.MakeButton(_panel.transform, "", new Vector2(0.5f, 1f),
                new Vector2(277, -564), new Vector2(545, 70), Off, () =>
                {
                    Combat.PlayableAnimator.SingleClipLoco =
                        !Combat.PlayableAnimator.SingleClipLoco;
                    Refresh();
                }, 22);

            // ===== 出招磁吸（默认开）=====
            // 关掉＝出招不再自动贴身、不再自动转向，站位与朝向完全由玩家决定。
            // 触屏上瞄准本来就难，所以默认留着；但它是"补最后一小段"，
            // 不该替玩家走位——玩家觉得不听使唤时，这是第一个该关掉试试的东西。
            _magnetBtn = UiUtil.MakeButton(_panel.transform, "", new Vector2(0.5f, 1f),
                new Vector2(-277, -894), new Vector2(545, 70), Off, () =>
                {
                    Combat.PlayerCombatController.AttackMagnetOn =
                        !Combat.PlayerCombatController.AttackMagnetOn;
                    Refresh();
                }, 22);

            // ===== 跑动中出招只写上半身（默认开）=====
            // 关掉＝回到"招式接管整个身体"的旧行为，用于对照。
            // 【它只管玩家自己的招式】敌人、路人，以及按名字播的片段
            //（拔刀/收刀/Boss 说话）一律不受它影响——那些边走边播时腿被钉死
            // 纯粹是缺陷，没有"关掉做对照"的意义。
            _upperBtn = UiUtil.MakeButton(_panel.transform, "", new Vector2(0.5f, 1f),
                new Vector2(277, -894), new Vector2(545, 70), Off, () =>
                {
                    Combat.HumanoidAnimator.UpperBodyAttacksOn =
                        !Combat.HumanoidAnimator.UpperBodyAttacksOn;
                    Refresh();
                }, 22);

            // ===== 推杆时的镜头自动跟随（默认关）=====
            // 开着它就是"推着直杆却走弧线"的那个闭环（推导见 ThirdPersonCamera）。
            // 留这个开关只为当场对照：开 → 走两步就偏、最后蹭墙；关 → 走直线。
            _headFollowBtn = UiUtil.MakeButton(_panel.transform, "", new Vector2(0.5f, 1f),
                new Vector2(0, -980), new Vector2(560, 70), Off, () =>
                {
                    Player.ThirdPersonCamera.HeadingFollowWhileSteering =
                        !Player.ThirdPersonCamera.HeadingFollowWhileSteering;
                    Refresh();
                }, 22);

            // ===== 逐帧调试日志 =====
            // 每次测试点一下"新建日志"，跑完把文件发出来即可。
            _logBtn = UiUtil.MakeButton(_panel.transform, "", new Vector2(0.5f, 1f),
                new Vector2(-195, -1080), new Vector2(370, 70), Off, () =>
                {
                    Core.MoveLogger.Enabled = !Core.MoveLogger.Enabled;
                    Refresh();
                }, 22);
            UiUtil.MakeButton(_panel.transform, "新建日志（每次测试点一下）",
                new Vector2(0.5f, 1f), new Vector2(195, -1080), new Vector2(370, 70),
                new Color(0.25f, 0.4f, 0.55f, 0.95f), () =>
                {
                    Core.MoveLogger.StartNewFile();
                    Core.MoveLogger.Enabled = true;
                    GameEvents.RaiseSubtitle("已新建日志：" + Core.MoveLogger.CurrentPath);
                    Refresh();
                }, 22);
            // 日志放哪儿：persistentDataPath 在 Android 11 之后被 scoped storage
            // 对文件管理器藏了起来，玩家进不去。用系统目录选择器挑一个自己能进的
            // 文件夹（下载/文档/U 盘都行），拿到可持久化写授权后导出即可。
            // 选一次记住，之后每次切后台自动导出一份。
            UiUtil.MakeButton(_panel.transform, "选择日志目录（下载/文档…）",
                new Vector2(0.5f, 1f), new Vector2(-195, -1166), new Vector2(370, 70),
                new Color(0.3f, 0.45f, 0.3f, 0.95f), () =>
                {
                    if (!Platform.LogExport.Supported)
                    {
                        GameEvents.RaiseSubtitle("这个平台不支持系统目录选择器——" +
                            "日志仍在：" + Core.MoveLogger.CurrentPath);
                        return;
                    }
                    Platform.LogExport.PickFolder("选择存放调试日志的文件夹");
                }, 22);
            UiUtil.MakeButton(_panel.transform, "导出日志到该目录",
                new Vector2(0.5f, 1f), new Vector2(195, -1166), new Vector2(370, 70),
                new Color(0.3f, 0.45f, 0.55f, 0.95f), () =>
                {
                    Core.MoveLogger.ExportNow();
                    GameEvents.RaiseSubtitle(Core.MoveLogger.LastExport);
                    Refresh();
                }, 22);
            _logTarget = UiUtil.MakeText(_panel.transform, "LogTarget", "", 18,
                TextAnchor.MiddleCenter, new Color(0.75f, 1f, 0.75f, 0.75f));
            // 【挪到面板最底下】这两行日志说明本来压在色彩分级那一行的按钮底下
            //（截图里按钮背后那串灰色的 /storage/… 就是它），互相盖着都看不清。
            UiUtil.SetRect(_logTarget, new Vector2(0.5f, 1f), new Vector2(0, -1662),
                new Vector2(1000, 34));

            _logPath = UiUtil.MakeText(_panel.transform, "LogPath", "", 18,
                TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.55f));
            UiUtil.SetRect(_logPath, new Vector2(0.5f, 1f), new Vector2(0, -1698),
                new Vector2(1000, 34));

            // 真实色彩：关掉"改颜色"的那一层分级，看模型的本色。
            // 玩家说"模型放进游戏颜色跟原来不一样"——查下来贴图全对，
            // 改颜色的是全局 + 分区两层 ColorAdjustments（见 PostGrading）。
            // 这是美术设计，不该我替他删，给开关让他自己比。
            // 【并排一行，不要另起一行】面板高 1720，底部到"删除全部数据"只剩
            // 84 像素余量；再往下加一行放不下，硬加就会像上一版那样：
            // 本色对照放在 -1396，而"跳过当前子章"在 -1388，两个 70 高的按钮
            // 只差 8 像素完全重叠，而且它比我后创建，直接把我这颗盖没了——
            // 玩家找不到开关，不是没加，是压在别人底下。
            _gradingBtn = UiUtil.MakeButton(_panel.transform, "", new Vector2(0.5f, 1f),
                new Vector2(-195, -1244), new Vector2(370, 70),
                new Color(0.3f, 0.4f, 0.5f, 0.95f), () =>
                {
                    Core.PostGrading.Enabled = !Core.PostGrading.Enabled;
                    Refresh();
                }, 22);

            // 模型本色对照（中性白光）：主光/补光/雾/分级全关，角色身上剩下的
            // 就是底色贴图本身。这不是画面模式，是一次判定——见 PostGrading.NeutralLight。
            _neutralBtn = UiUtil.MakeButton(_panel.transform, "", new Vector2(0.5f, 1f),
                new Vector2(195, -1244), new Vector2(370, 70),
                new Color(0.42f, 0.42f, 0.3f, 0.95f), () =>
                {
                    Core.PostGrading.NeutralLight = !Core.PostGrading.NeutralLight;
                    Refresh();
                }, 22);

            // 漂移自检：不用再"玩一局导出 CSV"，点一下、看屏幕上的四个数就行。
            // 脚本化的摇杆保证每次输入完全一样，两次结果才有可比性。
            UiUtil.MakeButton(_panel.transform, "漂移自检（约 18 秒，全程别碰摇杆）",
                new Vector2(0.5f, 1f), new Vector2(0, -1320), new Vector2(760, 70),
                new Color(0.5f, 0.3f, 0.45f, 0.95f), () =>
                {
                    Hide();                       // 面板挡着看不到角色，也看不到结论
                    Core.MoveLogger.Enabled = true;
                    Core.DriftProbe.Run();
                }, 22);

            // 跳章快进：主线结构重排后老玩家可快速回到原进度（视为完成，不发奖励）
            UiUtil.MakeButton(_panel.transform, "跳过当前子章（调试/老玩家快进）",
                new Vector2(0.5f, 1f), new Vector2(0, -1396), new Vector2(760, 70),
                new Color(0.45f, 0.4f, 0.25f, 0.95f), () =>
                {
                    var story = StoryManager.Instance;
                    if (story == null || story.AllCleared)
                    {
                        GameEvents.RaiseSubtitle("主线已完结，没有可跳过的子章。");
                        return;
                    }
                    string skipped = story.Current.title;
                    story.SkipChapter();
                    GameEvents.RaiseSubtitle("已跳过【" + skipped + "】——主线推进到下一子章。");
                }, 24);

            // 心理安全系统：快速退出战斗——任何时刻一键传送回安全屋（独居小屋）
            UiUtil.MakeButton(_panel.transform, "一键返回安全屋（立刻脱离当前战斗）",
                new Vector2(0.5f, 1f), new Vector2(0, -1474), new Vector2(760, 70),
                new Color(0.25f, 0.4f, 0.55f, 0.95f), ReturnToSafeHouse, 24);

            _deleteBtn = UiUtil.MakeButton(_panel.transform, "删除全部数据（存档/画像/提示词/进度）",
                new Vector2(0.5f, 1f), new Vector2(0, -1560), new Vector2(760, 74),
                new Color(0.5f, 0.2f, 0.18f, 0.95f), OnDelete, 24);

            var note = UiUtil.MakeText(_panel.transform, "Note",
                "个人材料仅保存在本机；删除后自新的第一章重新开始。",
                20, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.45f));
            UiUtil.SetRect(note, new Vector2(0.5f, 1f), new Vector2(0, -1620), new Vector2(900, 32));

            // 关闭移到右上角：底部空间让给新增的操作偏好行
            UiUtil.MakeButton(_panel.transform, "关闭", new Vector2(1f, 1f), new Vector2(-90, -46),
                new Vector2(140, 60), new Color(0.3f, 0.3f, 0.38f, 0.95f), Hide, 24);

            _panel.SetActive(false);
        }

        Button MakeToggle(string label, float y, UnityEngine.Events.UnityAction onClick) =>
            UiUtil.MakeButton(_panel.transform, label, new Vector2(0.5f, 1f),
                new Vector2(0, y), new Vector2(760, 70), Off, onClick, 24);

        /// <summary>一键返回安全屋：不论身处哪个区域/是否交战，立即传送回独居小屋。</summary>
        void ReturnToSafeHouse()
        {
            var player = AdversityRoad.Core.ActorRegistry.Player;
            if (player == null) return;
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.position = new Vector3(0, 1.1f, -5);   // 独居小屋出生点
            if (cc != null) cc.enabled = true;
            player.NotifyTeleported();   // 同上：跨场景传送后必须清掉旧的安全点
            player.MoveSpeedMultiplier = 1f;
            World.ZoneBuilder.CurrentZoneId = "home";
            Hide();
            GameEvents.RaiseSubtitle("—— 已返回安全屋。喘口气，需要时再出发。——");
        }

        void OnDelete()
        {
            if (!_deleteArmed)
            {
                _deleteArmed = true;
                _deleteBtn.GetComponentInChildren<Text>().text = "再点一次确认删除！";
                return;
            }
            SaveSystem.DeleteAll();
            Core.GrowthSystem.DeleteAll();   // 清空成长/图鉴/档案的内存缓存
            Core.QuizSystem.DeleteAll();     // 清空答题记录的内存缓存
            Core.QuizAiBank.DeleteAll();     // 删除 AI 命题题库（含本地文件）
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
            try
            {
                string p = Application.persistentDataPath + "/aiprompts.json";
                if (System.IO.File.Exists(p)) System.IO.File.Delete(p);
            }
            catch { }
            GameEvents.RaiseSubtitle("数据已全部删除。重启游戏后从第一章重新开始。");
            _deleteArmed = false;
            _deleteBtn.GetComponentInChildren<Text>().text = "已删除（重启游戏生效）";
        }

        void Refresh()
        {
            var s = Safety;
            foreach (var (btn, val) in _intensityBtns)
                btn.GetComponent<Image>().color = s != null && s.intensity == val ? On : Off;
            if (_softenBtn != null)
                _softenBtn.GetComponent<Image>().color = s != null && s.softenDialogue ? On : Off;
            if (_recoveryBtn != null)
                _recoveryBtn.GetComponent<Image>().color = s != null && s.recoveryMode ? On : Off;
            var cam = FindObjectOfType<ThirdPersonCamera>();
            if (_followBtn != null)
                _followBtn.GetComponent<Image>().color = cam != null && cam.autoFollow ? On : Off;
            if (_debugBtn != null)
                _debugBtn.GetComponent<Image>().color = GameDebug.TankyEnemies ? On : Off;
            if (_perfBtn != null)
                _perfBtn.GetComponent<Image>().color = PerfHud.Enabled ? On : Off;
            if (_lockModeBtn != null)
            {
                _lockModeBtn.GetComponentInChildren<Text>().text =
                    LockOnSystem.AutoAcquire ? "锁定模式：自动" : "锁定模式：手动（Q/锁键）";
                _lockModeBtn.GetComponent<Image>().color = LockOnSystem.AutoAcquire ? On : Off;
            }
            if (_aimAssistBtn != null)
            {
                _aimAssistBtn.GetComponentInChildren<Text>().text =
                    LockOnSystem.AimAssist ? "攻击吸附：开" : "攻击吸附：关（完全手操）";
                _aimAssistBtn.GetComponent<Image>().color = LockOnSystem.AimAssist ? On : Off;
            }
            foreach (var (btn, val) in _turnBtns)
                btn.GetComponent<Image>().color =
                    Mathf.Approximately(PlayerController.TurnAccelOverride, val) ? On : Off;
            if (_footLockBtn != null)
            {
                _footLockBtn.GetComponentInChildren<Text>().text =
                    Combat.HumanoidAnimator.FootLockOn ? "支撑脚锁定：开" : "支撑脚锁定：关";
                _footLockBtn.GetComponent<Image>().color =
                    Combat.HumanoidAnimator.FootLockOn ? On : Off;
            }
            if (_leanBtn != null)
            {
                _leanBtn.GetComponentInChildren<Text>().text =
                    Combat.HumanoidAnimator.TurnLeanOn ? "转向倾身：开" : "转向倾身：关";
                _leanBtn.GetComponent<Image>().color =
                    Combat.HumanoidAnimator.TurnLeanOn ? On : Off;
            }
            if (_gradingBtn != null)
            {
                _gradingBtn.GetComponentInChildren<Text>().text =
                    Core.PostGrading.Enabled ? "色彩分级：开（氛围）" : "色彩分级：关";
                _gradingBtn.GetComponent<Image>().color =
                    Core.PostGrading.Enabled ? On : Off;
            }
            if (_neutralBtn != null)
            {
                _neutralBtn.GetComponentInChildren<Text>().text =
                    Core.PostGrading.NeutralLight ? "本色对照：开（中性光）" : "本色对照：关";
                _neutralBtn.GetComponent<Image>().color =
                    Core.PostGrading.NeutralLight ? On : Off;
            }
            if (_postFxBtn != null)
            {
                _postFxBtn.GetComponentInChildren<Text>().text =
                    Combat.HumanoidAnimator.BonePostFxOn
                        ? "骨骼后处理：开（正常）"
                        : "骨骼后处理：关（诊断·会与碰撞体分家）";
                _postFxBtn.GetComponent<Image>().color =
                    Combat.HumanoidAnimator.BonePostFxOn ? On : Off;
            }
            if (_singleClipBtn != null)
            {
                _singleClipBtn.GetComponentInChildren<Text>().text =
                    Combat.PlayableAnimator.SingleClipLoco
                        ? "单片段模式：开（诊断·不混合）"
                        : "单片段模式：关（正常）";
                _singleClipBtn.GetComponent<Image>().color =
                    Combat.PlayableAnimator.SingleClipLoco ? On : Off;
            }
            if (_magnetBtn != null)
            {
                _magnetBtn.GetComponentInChildren<Text>().text =
                    Combat.PlayerCombatController.AttackMagnetOn
                        ? "出招自动贴身/转向：开"
                        : "出招自动贴身/转向：关";
                _magnetBtn.GetComponent<Image>().color =
                    Combat.PlayerCombatController.AttackMagnetOn ? On : Off;
            }
            if (_upperBtn != null)
            {
                _upperBtn.GetComponentInChildren<Text>().text =
                    Combat.HumanoidAnimator.UpperBodyAttacksOn
                        ? "跑动出招·只动上半身：开"
                        : "跑动出招·只动上半身：关";
                _upperBtn.GetComponent<Image>().color =
                    Combat.HumanoidAnimator.UpperBodyAttacksOn ? On : Off;
            }
            if (_headFollowBtn != null)
            {
                _headFollowBtn.GetComponentInChildren<Text>().text =
                    Player.ThirdPersonCamera.HeadingFollowWhileSteering
                        ? "推杆时镜头自动跟随：开（会走弧线）"
                        : "推杆时镜头自动跟随：关";
                _headFollowBtn.GetComponent<Image>().color =
                    Player.ThirdPersonCamera.HeadingFollowWhileSteering ? On : Off;
            }
            if (_logBtn != null)
            {
                _logBtn.GetComponentInChildren<Text>().text =
                    Core.MoveLogger.Enabled ? "调试日志：开" : "调试日志：关";
                _logBtn.GetComponent<Image>().color = Core.MoveLogger.Enabled ? On : Off;
            }
            if (_logPath != null)
                _logPath.text = string.IsNullOrEmpty(Core.MoveLogger.CurrentPath)
                    ? "日志未启用"
                    : Core.MoveLogger.CurrentPath + "　（已写 " + Core.MoveLogger.Rows + " 行）";
            if (_logTarget != null)
            {
                string tgt = Core.MoveLogger.TargetLabel();
                _logTarget.text = string.IsNullOrEmpty(tgt)
                    ? "导出目录：未选择　—— 点左边的按钮挑一个你进得去的文件夹"
                    : "导出目录：" + tgt +
                      (string.IsNullOrEmpty(Core.MoveLogger.LastExport)
                          ? "" : "　｜　" + Core.MoveLogger.LastExport);
            }
        }

        public void Toggle()
        {
            if (_panel.activeSelf) { Hide(); return; }
            _deleteArmed = false;
            Refresh();
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            Time.timeScale = 0f;
        }

        void Hide()
        {
            _panel.SetActive(false);
            Time.timeScale = 1f;
        }
    }
}
