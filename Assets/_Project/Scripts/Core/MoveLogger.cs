using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using AdversityRoad.Player;

namespace AdversityRoad.Core
{
    /// <summary>
    /// 移动/动画/镜头/输入的**逐帧**调试日志。
    ///
    /// 【为什么要有它】
    /// 这一路定位问题全靠录屏截帧读 HUD：一段 20 秒的视频我只能抠出十来个采样点，
    /// 而"起步先滑一下""转向那一瞬间漂了"这类现象只存在于零点几秒里，
    /// 十来个采样点大概率整个错过。更要命的是 HUD 是给人眼看的拼接字符串，
    /// 每次都要从中文里用正则抠数字，抠错了就得出错误结论（步幅那一行就是这么
    /// 让我误判成"脚滑 15%"的）。
    ///
    /// 日志把这两件事一起解决：**每一帧都有一行，每一列都是数**。
    /// 一段 20 秒的测试就是 1200 行完整状态，任何一帧都能回溯。
    ///
    /// 【格式】CSV，第一行是表头。两类行：
    ///   · S 行（state）—— 每帧一行，全量状态；
    ///   · E 行（event）—— 按键按下/松开、姿态切换、招式触发、敌人登记等离散事件，
    ///     时间戳与 S 行同轴，可以直接对齐"我按了闪 → 那一帧发生了什么"。
    ///
    /// 【文件位置】写入始终在 Application.persistentDataPath（永远可写、快、不会失败），
    /// 但安卓上那是 /storage/emulated/0/Android/data/&lt;包名&gt;/files/ ——
    /// **Android 11 起 scoped storage 把它对文件管理器藏起来了，玩家根本进不去**。
    /// 我一开始选错了地方。
    ///
    /// 所以再加一层【导出】：玩家用系统目录选择器挑任意一个文件夹（下载、文档、
    /// U 盘、网盘挂载点都行），应用拿到一份**可持久化的写授权**，导出即把日志复制
    /// 过去。走的是 Storage Access Framework，全程不需要任何运行时权限，
    /// 也不用改 AndroidManifest（见 LogExport.java）。
    /// 选一次记住，之后每次退到后台自动导出一份。
    /// </summary>
    public class MoveLogger : MonoBehaviour
    {
        /// <summary>总开关（设置面板可切）。关掉即停止采样，已写的内容保留。</summary>
        public static bool Enabled = true;

        /// <summary>当前日志文件的完整路径（设置面板显示用）。</summary>
        public static string CurrentPath { get; private set; } = "";
        /// <summary>已写入的状态行数（设置面板显示用，确认它真的在跑）。</summary>
        public static int Rows { get; private set; }

        // ===== 导出目标（玩家自选的目录）=====
        const string PrefTree = "movelog_tree_uri";   // 安卓 SAF 的目录授权 URI
        const string PrefDir = "movelog_dir";         // 桌面/自填的普通路径

        /// <summary>玩家选定的安卓目录授权（空=没选过）。</summary>
        public static string TreeUri
        {
            get => PlayerPrefs.GetString(PrefTree, "");
            set { PlayerPrefs.SetString(PrefTree, value ?? ""); PlayerPrefs.Save(); }
        }
        /// <summary>玩家自填的普通目录（桌面平台，或安卓上确实可写的路径）。</summary>
        public static string CustomDir
        {
            get => PlayerPrefs.GetString(PrefDir, "");
            set { PlayerPrefs.SetString(PrefDir, value ?? ""); PlayerPrefs.Save(); }
        }
        /// <summary>上一次导出的结果（设置面板显示用）。</summary>
        public static string LastExport { get; private set; } = "";

        /// <summary>导出目标的可读名字：没选过就返回空。</summary>
        public static string TargetLabel()
        {
            if (!string.IsNullOrEmpty(CustomDir)) return CustomDir;
            string t = TreeUri;
            if (string.IsNullOrEmpty(t)) return "";
            return Platform.LogExport.FolderLabel(t);
        }

        /// <summary>把当前日志复制到玩家选定的目录。返回是否成功。</summary>
        public static bool ExportNow()
        {
            if (_inst != null) _inst.Flush();
            if (string.IsNullOrEmpty(CurrentPath) || !File.Exists(CurrentPath))
            { LastExport = "还没有日志文件"; return false; }
            string fileName = Path.GetFileName(CurrentPath);

            // ① 自填的普通目录：桌面平台，以及安卓上真的可写的那些路径
            string dir = CustomDir;
            if (!string.IsNullOrEmpty(dir))
            {
                try
                {
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    string dst = Path.Combine(dir, fileName);
                    File.Copy(CurrentPath, dst, true);
                    LastExport = "已导出：" + dst;
                    return true;
                }
                catch (System.Exception e)
                {
                    LastExport = "写不进 " + dir + "：" + e.Message;
                    // 不 return：还有 SAF 这条路可以试
                }
            }

            // ② 安卓 SAF：玩家选过的目录
            string tree = TreeUri;
            if (!string.IsNullOrEmpty(tree))
            {
                string doc = Platform.LogExport.Export(tree, CurrentPath, fileName, "text/csv");
                if (!string.IsNullOrEmpty(doc))
                {
                    LastExport = "已导出到 " + Platform.LogExport.FolderLabel(tree) +
                                 "/" + fileName;
                    return true;
                }
                LastExport = "导出失败——授权可能已失效，请重新选目录";
                return false;
            }

            if (string.IsNullOrEmpty(LastExport)) LastExport = "还没选导出目录";
            return false;
        }

        static MoveLogger _inst;

        // 每帧一行 × 60fps，直接写盘会卡；先攒在内存里，按行数/时间批量落盘。
        readonly StringBuilder _buf = new StringBuilder(1 << 16);
        int _pending;
        float _nextFlush;
        const int FlushRows = 120;        // 约两秒
        const float FlushSeconds = 2f;

        PlayerController _pc;
        Combat.HumanoidAnimator _anim;
        float _t0;

        // 上一帧的状态，用来发"变化了"的事件行（只在变的那一帧写，不刷屏）
        readonly HashSet<string> _wasHeld = new HashSet<string>();
        string _lastPose = "", _lastCombat = "", _lastAction = "";
        int _lastSpawnCount = -1;
        bool _lastGrounded = true, _lastStrafe, _lastCrouch, _lastSeeSelf = true;

        /// <summary>所有会被记录的按键（物理键名，与 MobileControls / MobileInput 一致）。</summary>
        static readonly string[] Buttons =
        {
            "Light", "Kick", "Heavy", "Dodge", "Guard", "Jump", "Interact",
            "Art", "Lock", "Crouch", "Sheathe",
            "Skill1", "Skill2", "Skill3", "Skill4", "Skill5", "Skill6",
        };

        public static void Create()
        {
            if (_inst != null) return;
            var go = new GameObject("MoveLogger");
            DontDestroyOnLoad(go);
            _inst = go.AddComponent<MoveLogger>();
        }

        /// <summary>另起一个新文件（设置面板"新建日志"按钮）。旧文件留在原地。</summary>
        public static void StartNewFile()
        {
            if (_inst == null) { Create(); return; }
            _inst.Flush();
            _inst.Open();
        }

        void Awake()
        {
            _t0 = Time.unscaledTime;
            Open();
        }

        void Open()
        {
            Rows = 0;
            _buf.Length = 0;
            _pending = 0;
            string name = "movelog_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv";
            CurrentPath = Path.Combine(Application.persistentDataPath, name);
            try
            {
                // 表头列名用英文：中文列名在某些表格工具里编码一乱就全废，
                // 而这份文件的第一读者是解析器，不是人。
                File.WriteAllText(CurrentPath, Header, new UTF8Encoding(false));
                _cols = Fields(Header);
                _eventPad = new string(',', Mathf.Max(1, _cols - 2));
                _checked = false;
                Debug.Log("[MoveLogger] 日志文件：" + CurrentPath + "（" + _cols + " 列）");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[MoveLogger] 无法创建日志文件：" + e.Message);
                CurrentPath = "";
            }
        }

        int _cols;
        bool _checked;

        const string Header =
                    "kind,t,dt,dtSim,fps," +
                    "stickX,stickY,stickMag,stickHeld,stickYaw," +
                    "bodyYaw,camYaw,camPitch,turnNeed,dirTrust,lateralG,turnRadius,bodyYawRate," +
                    "rawSpeed,apMult,slowMult,attackMult,finalSpeed,strafeCap,tier,sprint," +
                    "targetVel,hVel,actual,speed01,moveAngle," +
                    "grounded,hitSides,vy,startGate,strafe,crouch,indoorPace,walkOnly," +
                    "posX,posY,posZ,stepLen,maxStep,stepAge," +
                    "pose,combat,hardLock,actionClip,actionW," +
                    "dir1,dir1W,dir2,dir2W,phaseRate,blendAngle," +
                    "strideActual,strideWant,strideRatio,slipClip,slipWant,slipGot,footFix," +
                    "camBoom,camBoomWant,camLift,camStuck,seeSelf,camTight,upperOnly," +
                    "extMove,extSrc,faceSnap,legsWalking,blendMix,depen,deep,rollback," +
                    "visStep,hipLeak,hipRaw,pinOn,bodyLX,bodyLZ,bindX,bindZ," +
                    "enemies,spawnCount,held,event\n";

        static string F(float v) => v.ToString("F3", CultureInfo.InvariantCulture);
        static string B(bool v) => v ? "1" : "0";
        /// <summary>CSV 字段消毒：片段名里有逗号/引号会把整行的列错开。</summary>
        static string Q(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            if (v.IndexOf(',') < 0 && v.IndexOf('"') < 0 && v.IndexOf('\n') < 0) return v;
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        }

        // 事件行要把 event 放在最后一列，中间全空。占位逗号**从表头算**，
        // 手数一串逗号是一定会数错的，而列一旦错位，整份日志的每一列都对不上。
        static string _eventPad;

        /// <summary>写一条事件行。时间轴与状态行同源，可直接对齐。</summary>
        public static void Event(string what)
        {
            if (_inst == null || !Enabled || string.IsNullOrEmpty(what)) return;
            _inst._buf.Append("E,").Append(F(Time.unscaledTime - _inst._t0))
                      .Append(_eventPad).Append(Q(what)).Append('\n');
            _inst._pending++;
        }

        /// <summary>数一行里的字段数（引号内的逗号不算）。</summary>
        static int Fields(string line)
        {
            int n = 1; bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"') q = !q;
                else if (c == ',' && !q) n++;
                else if (c == '\n') break;
            }
            return n;
        }

        void LateUpdate()
        {
            if (!Enabled || string.IsNullOrEmpty(CurrentPath)) return;
            if (_pc == null) _pc = ActorRegistry.Player;
            if (_pc == null) return;
            if (_anim == null) _anim = _pc.GetComponent<Combat.HumanoidAnimator>();

            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-5f);
            float t = Time.unscaledTime - _t0;

            // ---- 离散事件：只在变化的那一帧写 ----
            for (int i = 0; i < Buttons.Length; i++)
            {
                string b = Buttons[i];
                bool held = Mobile.MobileInput.GetHeld(b) ||
                            (b == "Art" && Mobile.MobileInput.ModifierHeld);
                bool was = _wasHeld.Contains(b);
                if (held == was) continue;
                if (held) _wasHeld.Add(b); else _wasHeld.Remove(b);
                Event((held ? "按下 " : "松开 ") + b);
            }
            string pose = _anim != null ? _anim.DbgPose : "";
            if (pose != _lastPose) { Event("姿态 " + _lastPose + " → " + pose); _lastPose = pose; }
            string cs = _pc.DbgCombatState;
            if (cs != _lastCombat) { Event("战斗状态 " + _lastCombat + " → " + cs); _lastCombat = cs; }
            string act = _anim != null ? _anim.DbgActionName : "";
            if (act != _lastAction)
            {
                if (!string.IsNullOrEmpty(act)) Event("起播动作 " + act);
                _lastAction = act;
            }
            bool grounded = _pc.IsGroundedNow;
            if (grounded != _lastGrounded) { Event(grounded ? "落地" : "离地"); _lastGrounded = grounded; }
            if (_pc.StrafeActive != _lastStrafe)
            { Event(_pc.StrafeActive ? "进入横移（面向目标）" : "退出横移"); _lastStrafe = _pc.StrafeActive; }
            if (_pc.IsCrouched != _lastCrouch)
            { Event(_pc.IsCrouched ? "蹲下" : "起身"); _lastCrouch = _pc.IsCrouched; }
            if (ThirdPersonCamera.DbgSeeSelf != _lastSeeSelf)
            {
                Event(ThirdPersonCamera.DbgSeeSelf ? "镜头重新看见角色" : "镜头看不见角色（盲区开始）");
                _lastSeeSelf = ThirdPersonCamera.DbgSeeSelf;
            }
            if (ActorRegistry.SpawnCount != _lastSpawnCount)
            {
                if (_lastSpawnCount >= 0)
                    Event("敌人登记 +" + (ActorRegistry.SpawnCount - _lastSpawnCount) +
                          " ← " + ActorRegistry.LastSpawn);
                _lastSpawnCount = ActorRegistry.SpawnCount;
            }

            // ---- 状态行 ----
            Vector3 sw = _pc.StickWorldDir;
            float stickYaw = sw.sqrMagnitude > 1e-4f
                ? Quaternion.LookRotation(sw.normalized).eulerAngles.y : 0f;
            float bodyYaw = _pc.transform.eulerAngles.y;
            float yawRate = Mathf.DeltaAngle(_prevBodyYaw, bodyYaw) / dt;
            _prevBodyYaw = bodyYaw;
            var cam = Camera.main;
            float camYaw = cam != null ? cam.transform.eulerAngles.y : 0f;
            float actual = _pc.DbgActual;
            // 转弯半径 = v / ω（直线跑时 ω→0，半径给一个封顶值而不是无穷）
            float radius = Mathf.Abs(yawRate) > 1f
                ? Mathf.Min(999f, actual / (Mathf.Abs(yawRate) * Mathf.Deg2Rad)) : 999f;

            string d1, d2; float w1, w2;
            if (_anim != null) _anim.DbgTopDirs(out d1, out w1, out d2, out w2);
            else { d1 = ""; w1 = 0f; d2 = ""; w2 = 0f; }
            float strideA = _anim != null ? _anim.DbgStrideActual(actual) : 0f;
            float strideW = _anim != null ? _anim.DbgStrideWant() : 0f;

            var held2 = new StringBuilder(24);
            for (int i = 0; i < Buttons.Length; i++)
                if (_wasHeld.Contains(Buttons[i]))
                { if (held2.Length > 0) held2.Append('|'); held2.Append(Buttons[i]); }

            var sb = _buf;
            int rowStart = sb.Length;
            sb.Append("S,").Append(F(t)).Append(',').Append(F(dt)).Append(',')
              // dt 是墙钟时间（unscaled，不受 maximumDeltaTime 钳制），
              // dtSim 才是这一帧**真正用来推进游戏**的步长。两个都要记：
              // 上一轮我拿 dt 去判断"单帧步长上限有没有生效"，结论必然是错的。
              .Append(F(Time.deltaTime)).Append(',')
              .Append(F(1f / dt)).Append(',')
              .Append(F(Mobile.MobileInput.Move.x)).Append(',')
              .Append(F(Mobile.MobileInput.Move.y)).Append(',')
              .Append(F(_pc.DbgInputMag)).Append(',').Append(B(_pc.StickHeld)).Append(',')
              .Append(F(stickYaw)).Append(',')
              .Append(F(bodyYaw)).Append(',').Append(F(camYaw)).Append(',')
              .Append(F(ThirdPersonCamera.DbgPitch)).Append(',')
              .Append(F(_pc.DbgTurnNeed)).Append(',').Append(F(_pc.DbgDirTrust)).Append(',')
              .Append(F(_pc.DbgLateralG)).Append(',').Append(F(radius)).Append(',')
              .Append(F(yawRate)).Append(',')
              .Append(F(_pc.DbgRawSpeed)).Append(',').Append(F(_pc.DbgApMult)).Append(',')
              .Append(F(_pc.MoveSpeedMultiplier)).Append(',').Append(F(_pc.DbgAttackFactor)).Append(',')
              .Append(F(_pc.DbgFinalSpeed)).Append(',').Append(F(_pc.DbgStrafeCap)).Append(',')
              // 三档移速：tier 0 走 / 1 跑 / 2 冲刺。玩家报"三档推不出来、冲刺
              // 跟跑一样快"，这两列直接看得到本帧到底落在哪一档。
              .Append(_pc.DbgSpeedTier).Append(',').Append(B(_pc.Sprinting)).Append(',')
              .Append(F(_pc.DbgTargetVel)).Append(',').Append(F(_pc.DbgVel)).Append(',')
              .Append(F(actual)).Append(',')
              .Append(F(Mathf.Clamp01(actual / Mathf.Max(0.1f, _pc.runSpeed)))).Append(',')
              .Append(F(_pc.DbgMoveAngle)).Append(',')
              .Append(B(grounded)).Append(',').Append(B(_pc.DbgHitSides)).Append(',')
              .Append(F(_pc.VerticalVelocity)).Append(',').Append(B(_pc.DbgStartGate)).Append(',')
              .Append(B(_pc.StrafeActive)).Append(',').Append(B(_pc.IsCrouched)).Append(',')
              .Append(B(_pc.IndoorPace)).Append(',').Append(B(_pc.WalkOnly)).Append(',')
              .Append(F(_pc.transform.position.x)).Append(',')
              .Append(F(_pc.transform.position.y)).Append(',')
              .Append(F(_pc.transform.position.z)).Append(',')
              .Append(F(_pc.DbgVel * dt)).Append(',')
              .Append(F(_pc.DbgMaxStep)).Append(',').Append(F(_pc.DbgStepAge)).Append(',')
              .Append(Q(pose)).Append(',').Append(Q(cs)).Append(',')
              .Append(B(_pc.DbgHardLocked)).Append(',')
              .Append(Q(act)).Append(',')
              .Append(F(_anim != null ? _anim.DbgActionW : 0f)).Append(',')
              .Append(Q(d1)).Append(',').Append(F(w1)).Append(',')
              .Append(Q(d2)).Append(',').Append(F(w2)).Append(',')
              .Append(F(_anim != null ? _anim.DbgPhaseRate : 0f)).Append(',')
              .Append(F(_anim != null ? _anim.DbgBlendAngle : 0f)).Append(',')
              .Append(F(strideA)).Append(',').Append(F(strideW)).Append(',')
              .Append(F(strideW > 0.01f ? strideA / strideW : 0f)).Append(',')
              .Append(Q(_anim != null ? _anim.DbgSlipClip : "")).Append(',')
              .Append(F(_anim != null ? _anim.DbgSlipWant : 0f)).Append(',')
              .Append(F(_anim != null ? _anim.DbgSlipGot : 0f)).Append(',')
              .Append(F(_anim != null ? _anim.DbgFootFix : 0f)).Append(',')
              .Append(F(ThirdPersonCamera.DbgBoom)).Append(',')
              .Append(F(ThirdPersonCamera.DbgBoomWant)).Append(',')
              .Append(F(ThirdPersonCamera.DbgLift)).Append(',')
              .Append(B(ThirdPersonCamera.DbgStuck)).Append(',')
              .Append(B(ThirdPersonCamera.DbgSeeSelf)).Append(',')
              .Append(F(ThirdPersonCamera.DbgTight)).Append(',')
              // 跑动中出招时招式是否只写上半身：⑥ 那一类"腿在演招式、人还在跑"
              // 修没修掉，只能靠这一列判定——光看动作名分不出全身还是半身。
              .Append(B(_anim != null && _anim.ActionUpperBodyOnly)).Append(',')
              // ===== 谁在推角色 =====
              // 角色的位置以前有七个写入方，实测速度可以远超指令速度而无从追查。
              // 现在外部位移统一走 PlayerController 的一条通道，这两列直接回答
              // "这一帧除了玩家的输入，还有谁在挪人、挪了多少"。
              .Append(F(_pc != null ? _pc.DbgExtMove : 0f)).Append(',')
              .Append(Q(_pc != null ? _pc.DbgExtSrc : "")).Append(',')
              // 出招把朝向掰了多少度：玩家说"很难精准控制、像被动画控制"，
              // 自动瞄准的强制转向是最直接的一条，必须能量出来。
              .Append(F(Combat.PlayerCombatController.DbgFaceSnap)).Append(',')
              // ===== 腿到底有没有在演走路 =====
              // 玩家的原话："偶尔突然出现一会脚不在地上走路的动画，而是直接从 a
              // 漂移到 b"。前面几轮我一直在量"脚打不打滑"（步幅比），那是另一回事
              // ——步幅比漂亮的同时，腿可以整个定住不动。
              // 这一列直接回答：本帧移动层有没有真的在驱动腿。三个条件缺一不可：
              //   · 有方向片段拿到权重（不然移动层等于没输出）；
              //   · 步态相位在推进（相位不动＝腿定格）；
              //   · 动作层没有整体接管（开了上半身遮罩就不算接管，腿仍归移动层）。
              .Append(B(_anim != null && _anim.LegsWalking)).Append(',')
              // 两条方向片段共混的程度：相位不对齐造成的"腿互相抵消"只在它高时发生。
              // 转向/转圈/起步变速正是它高的时刻——玩家报的三个场景一一对应。
              .Append(F(_anim != null ? _anim.DbgBlendMix : 0f)).Append(',')
              // 嵌墙兜底：depen = 本帧被推出来多少米；deep = 本帧最深的横向嵌入量；
              // rollback = 累计判定"已经在墙里"而回滚的次数。
              // 玩家报的"360 转圈会穿墙"修没修掉、以及这张网自己有没有变成新的
              // 瞬移源，都看这三列：deep 一直是 0 说明根本没嵌进去，问题在别处。
              .Append(F(Combat.CharacterMotion.DbgDepenetrate)).Append(',')
              .Append(F(Combat.CharacterMotion.DbgDeepest)).Append(',')
              .Append(Combat.CharacterMotion.DbgRollbacks).Append(',')
              // ===== 画面上的身体，而不是胶囊 =====
              // 之前所有列量的都是胶囊，而胶囊已被证明是干净的（逐帧全覆盖，
              // 单帧位移从没超过 0.13m），所以玩家看见的漂移只能在这两列里：
              //   visStep = 渲染出来的髋骨这一帧在世界里走了多远。它减去 stepLen
              //             就是身体相对胶囊的滑动量——漂移本体。
              //   hipLeak = 钉髋之后髋骨仍偏离绑定位多少（应恒为 0）。不为 0
              //             就说明片段自带位移漏进了画面，人会往前爬再弹回去。
              .Append(F(_anim != null ? _anim.DbgVisStep : 0f)).Append(',')
              .Append(F(_anim != null ? _anim.DbgHipLeak : 0f)).Append(',')
              // hipLeak 是钉**之后**量的，钉髋干的就是把它清零，所以它恒为 0，
              // 而且 _hipsPin 为 false（根本没钉）时同样是 0——两种情况读数一样，
              // 拿它当"没漏"的证据等于什么都没证明。下面三列才是能分辨的：
              //   hipRaw = 钉**之前**的偏离量，片段这一帧想要的根位移；
              //   pinOn  = 这一帧到底钉没钉；
              //   bodyLX/LZ = 髋骨在角色自身坐标系里的水平位置。身体相对胶囊
              //               有没有滑、往哪滑，只有它说了算——世界位移里混着
              //               胶囊平移和转身，拆不开。它一跳，就是漂移那一帧。
              .Append(F(_anim != null ? _anim.DbgHipRaw : 0f)).Append(',')
              .Append(B(_anim != null && _anim.DbgPinOn)).Append(',')
              .Append(F(_anim != null ? _anim.DbgBodyLocal.x : 0f)).Append(',')
              .Append(F(_anim != null ? _anim.DbgBodyLocal.y : 0f)).Append(',')
              // 钉髋锚点本身（米）。bodyLX 一直是 1.34 而 bindX 也是 1.34，
              // 就说明身体不是"没被拦住"，而是被钉髋按在了错的地方。
              .Append(F(_anim != null ? _anim.DbgHipBind.x : 0f)).Append(',')
              .Append(F(_anim != null ? _anim.DbgHipBind.y : 0f)).Append(',')
              .Append(ActorRegistry.Enemies.Length).Append(',')
              .Append(ActorRegistry.SpawnCount).Append(',')
              .Append(Q(held2.ToString())).Append(",\n");

            // 一次性列数自检：状态行的字段数必须等于表头。少一列多一列都会让
            // 后面**每一列都错位**，而错位的日志比没有日志更坏——它会让我信心十足地
            // 读出错误的结论（步幅那一行就是这么把我带偏过一次）。
            if (!_checked)
            {
                _checked = true;
                int got = Fields(sb.ToString(rowStart, sb.Length - rowStart));
                if (got != _cols)
                    Debug.LogError("[MoveLogger] 列数不匹配：表头 " + _cols +
                                   " 列，状态行 " + got + " 列——日志不可信，请修列表。");
            }

            Rows++;
            _pending++;
            if (_pending >= FlushRows || Time.unscaledTime >= _nextFlush) Flush();
        }

        float _prevBodyYaw;

        void Flush()
        {
            _nextFlush = Time.unscaledTime + FlushSeconds;
            if (_buf.Length == 0 || string.IsNullOrEmpty(CurrentPath)) return;
            try { File.AppendAllText(CurrentPath, _buf.ToString(), new UTF8Encoding(false)); }
            catch (System.Exception e)
            {
                Debug.LogWarning("[MoveLogger] 写盘失败：" + e.Message);
                CurrentPath = "";
            }
            _buf.Length = 0;
            _pending = 0;
        }

        // 切后台/退出都要落盘：手机上玩家直接切走是常态，
        // 攒在内存里的最后两秒往往正是他想给我看的那两秒。
        void OnApplicationPause(bool paused) { if (paused) { Flush(); AutoExport(); } }
        void OnApplicationFocus(bool focus) { if (!focus) { Flush(); AutoExport(); } }
        void OnApplicationQuit() { Flush(); AutoExport(); }
        void OnDestroy() { Flush(); }

        /// <summary>选过目录就在切后台时顺手导出一份——玩家跑完一段直接切走是常态，
        /// 让他再回来点一次"导出"是多余的一步，而漏掉的那一次往往正是要看的那一次。</summary>
        static void AutoExport()
        {
            if (string.IsNullOrEmpty(TreeUri) && string.IsNullOrEmpty(CustomDir)) return;
            ExportNow();
        }
    }
}
