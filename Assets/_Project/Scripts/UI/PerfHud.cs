using UnityEngine;
using UnityEngine.UI;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 帧率 / 帧时读数（右上角一行小字）。
    ///
    /// 【为什么要有它】
    /// "穿墙""被拉着转""动作时快时慢""移动变慢"这一串现象，在**低且不稳的帧率**下
    /// 会全部同时出现，而且与代码逻辑无关：
    ///   · 单帧位移 = 速度 × dt，掉帧时 dt 变大 ⇒ 一次 Move 跨过半米甚至更多 ⇒ 穿墙；
    ///   · 搓杆时相邻两帧的角度差变大 ⇒ 读作"人被瞬移/被拉着甩"；
    ///   · 帧时忽长忽短 ⇒ 动画忽快忽慢。
    /// 也就是说：**同一批症状既可能来自逻辑 bug，也可能纯粹来自帧率**，
    /// 光靠现象描述分不开。分不开就只能反复猜，而每猜一轮就是一次真机往返。
    ///
    /// 这一行读数把它变成一个可以直接报出来的数字：
    ///   · FPS 平均值 —— 整体档次；
    ///   · 本秒最长帧（ms）—— 决定"最坏那一帧位移多大"，穿墙看的是它，不是平均值；
    ///   · 场上角色数 —— 动画开销随它线性增长，用来判断是不是人一多就掉。
    /// </summary>
    public class PerfHud : MonoBehaviour
    {
        public static bool Enabled = true;

        Text _text;
        Text _move;              // 第二行：移动诊断
        Player.PlayerController _pc;
        float _accum, _worst;
        int _frames;
        float _nextReport;

        void Awake()
        {
            var go = new GameObject("PerfHudCanvas");
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;          // 压在所有面板之上，任何时候都读得到
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            var t = new GameObject("PerfText");
            t.transform.SetParent(go.transform, false);
            _text = t.AddComponent<Text>();
            _text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _text.fontSize = 26;
            _text.alignment = TextAnchor.UpperRight;
            _text.color = new Color(1f, 0.95f, 0.4f);
            _text.raycastTarget = false;          // 绝不吃掉触摸事件
            _text.horizontalOverflow = HorizontalWrapMode.Overflow;

            var rt = _text.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-24f, -190f);   // 让开右上角的暂停/菜单/目标
            rt.sizeDelta = new Vector2(700f, 40f);

            // 第二行：移动诊断。速度是好几层倍率连乘出来的，"移动变慢"到底慢在
            // 哪一层——行动力低？减速 debuff？锁定封顶？出招定步卡住？——
            // 只看角色跑得快不快是分不出来的，必须把每一层原样摆出来。
            var m = new GameObject("MoveText");
            m.transform.SetParent(go.transform, false);
            _move = m.AddComponent<Text>();
            _move.font = _text.font;
            _move.fontSize = 24;
            _move.alignment = TextAnchor.UpperRight;
            _move.color = new Color(0.6f, 0.9f, 1f);
            _move.raycastTarget = false;
            _move.horizontalOverflow = HorizontalWrapMode.Overflow;
            var mrt = _move.rectTransform;
            mrt.anchorMin = mrt.anchorMax = new Vector2(1f, 1f);
            mrt.pivot = new Vector2(1f, 1f);
            mrt.anchoredPosition = new Vector2(-24f, -226f);
            mrt.sizeDelta = new Vector2(700f, 40f);

            // 第三行：**搓杆时的整条链**。"看不到角色自己的移动节奏"这句话，
            // 从摇杆到腿一共经过六个环节，光看画面分不出断在哪一环：
            //   杆角速度 → 身体角速度 → 镜头角速度 → 实际移速/转弯半径
            //   → 喂给动画的夹角/混合角 → 步态相位速率
            // 全打出来，一次搓杆截图就能定死是哪一环。峰值保持 1.2 秒，
            // 否则搓完手一松数字就掉回 0，截图永远抓不到。
            var s3 = new GameObject("SpinText");
            s3.transform.SetParent(go.transform, false);
            _spin = s3.AddComponent<Text>();
            _spin.font = _text.font;
            _spin.fontSize = 24;
            _spin.alignment = TextAnchor.UpperRight;
            _spin.color = new Color(1f, 0.75f, 0.95f);
            _spin.raycastTarget = false;
            _spin.horizontalOverflow = HorizontalWrapMode.Overflow;
            var srt = _spin.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(1f, 1f);
            srt.pivot = new Vector2(1f, 1f);
            srt.anchoredPosition = new Vector2(-24f, -262f);
            srt.sizeDelta = new Vector2(760f, 40f);

            // 第四行：**此刻画面上真正在播的动画**。
            // "横移/后退的片段放进去了却看不到效果"这类问题，光看代码说不清——
            // 片段可能没接上、可能接上了但权重恒为 0、也可能被动作层整个盖住。
            // 姿态（该播什么）与实际片段（在播什么）并排打出来，一眼分辨是哪一种。
            var a4 = new GameObject("AnimText");
            a4.transform.SetParent(go.transform, false);
            _anim4 = a4.AddComponent<Text>();
            _anim4.font = _text.font;
            _anim4.fontSize = 24;
            _anim4.alignment = TextAnchor.UpperRight;
            _anim4.color = new Color(0.7f, 1f, 0.7f);
            _anim4.raycastTarget = false;
            _anim4.horizontalOverflow = HorizontalWrapMode.Overflow;
            var art = _anim4.rectTransform;
            art.anchorMin = art.anchorMax = new Vector2(1f, 1f);
            art.pivot = new Vector2(1f, 1f);
            art.anchoredPosition = new Vector2(-24f, -298f);
            art.sizeDelta = new Vector2(900f, 40f);
        }

        Text _anim4;

        Text _spin;
        float _prevBodyYaw, _prevCamYaw;
        bool _yawInit;

        // 【上一版诊断自己的毛病】判据是"取丢速最厉害的那一帧"，
        // 而那保证抓到的是**离群单帧**（出招落地、碰撞解算的那一瞬），
        // 不是持续行为——三张截图各抓到一个不同的离群点，互相对不上。
        // 改为一秒内的统计：平均命令/平均实际、硬锁占比、撞墙占比、平均身体角速度。
        // 持续现象才会体现在平均值里，一次性的毛刺被摊平。
        int _n, _nLock, _nWall;
        float _sCmd, _sVel, _sAct, _sBody, _sAngle, _sCam, _sNeed, _sG, _sPhase, _sTrust;
        string _line = "";
        float _nextRoll;

        void SampleSpin(float dt)
        {
            if (_spin == null || dt <= 0.0001f) return;
            var pc = AdversityRoad.Core.ActorRegistry.Player;
            if (pc == null) { _spin.text = ""; return; }

            var cam = Camera.main;
            float bodyYaw = pc.transform.eulerAngles.y;
            float camYaw = cam != null ? cam.transform.eulerAngles.y : 0f;
            if (!_yawInit)
            {
                _yawInit = true; _prevBodyYaw = bodyYaw; _prevCamYaw = camYaw; return;
            }
            float bRate = Mathf.Abs(Mathf.DeltaAngle(_prevBodyYaw, bodyYaw)) / dt;
            // 镜头角速度上一版被我删掉了，结果无法验证 H = C + θ 那条反馈环
            //（摇杆是镜头相对的，镜头转 1° 行进方向就跟着转 1°）。加回来。
            float cRate = Mathf.Abs(Mathf.DeltaAngle(_prevCamYaw, camYaw)) / dt;
            _prevBodyYaw = bodyYaw; _prevCamYaw = camYaw;

            // 只统计【确实在推杆】的帧：松杆减速本来就该丢速，混进来会污染平均
            if (pc.DbgInputMag > 0.6f)
            {
                _n++;
                _sCmd += pc.DbgFinalSpeed;
                _sVel += pc.DbgVel;
                _sAct += pc.DbgActual;
                _sBody += bRate;
                _sCam += cRate;
                _sNeed += pc.DbgTurnNeed;
                _sG += pc.DbgLateralG;
                _sTrust += pc.DbgDirTrust;   // 方向意图置信度：搓杆趋0、稳定推杆趋1             // 转向的横向加速度（g）：>1 就不像人在跑
                var ha = pc.GetComponent<Combat.HumanoidAnimator>();
                if (ha != null) _sPhase += ha.DbgPhaseRate;   // 步频：腿有没有跟上地面速度
                _sAngle += Mathf.Abs(pc.DbgMoveAngle);
                if (pc.DbgHardLocked) _nLock++;
                if (pc.DbgHitSides) _nWall++;
            }

            // 【上一版这一行会冻住】录屏里连续 12 秒读数一字不差，而背景在动——
            // 因为 _n<3 时直接沿用旧串却不清账，推杆量偶尔掉到 0.6 以下就再也不更新。
            // 而且一秒均值本来就抹掉了搓杆这种瞬时动作。
            // 改为 0.35 秒一档、且无论如何都清账，宁可跳动也不要给出过期的数字。
            if (Time.unscaledTime < _nextRoll) { _spin.text = _line; return; }
            _nextRoll = Time.unscaledTime + 0.35f;
            if (_n < 2)
            {
                _n = _nLock = _nWall = 0;
                _sCmd = _sVel = _sAct = _sBody = _sAngle = _sCam = _sNeed = _sG = _sPhase = _sTrust = 0f;
                _spin.text = _line; return;
            }

            float inv = 1f / _n;
            _line = string.Format(
                "推杆0.35秒 命令{0:F1}→实际{1:F1}m/s 步频{2:F2} | 身{3:F0}°/s 横向{4:F2}g 半径{5:F1}m | 夹角{6:F0}° 待转{7:F0}° 意图{9:F2} | 撞墙{8:F0}%",
                _sCmd * inv, _sAct * inv, _sPhase * inv,
                _sBody * inv, _sG * inv,
                _sBody * inv > 1f
                    ? (_sAct * inv) / (_sBody * inv * Mathf.Deg2Rad) : 0f,
                _sAngle * inv, _sNeed * inv, 100f * _nWall / _n, _sTrust * inv);
            _n = _nLock = _nWall = 0;
            _sCmd = _sVel = _sAct = _sBody = _sAngle = _sCam = _sNeed = _sG = _sPhase = _sTrust = 0f;
            _spin.text = _line;
        }

        void Update()
        {
            if (_text == null) return;
            if (_text.enabled != Enabled) _text.enabled = Enabled;
            if (_move != null && _move.enabled != Enabled) _move.enabled = Enabled;
            if (_spin != null && _spin.enabled != Enabled) _spin.enabled = Enabled;
            if (_anim4 != null && _anim4.enabled != Enabled) _anim4.enabled = Enabled;
            if (!Enabled) return;

            // 用不缩放的真实帧时：顿帧/时缓会把 Time.deltaTime 改掉，
            // 而这里要量的是**设备每帧真的花了多久**。
            float dt = Time.unscaledDeltaTime;
            _accum += dt;
            _frames++;
            if (dt > _worst) _worst = dt;

            SampleSpin(dt);   // 搓杆链路要每帧采（角速度是差分出来的，不能只在汇报时算）

            // 动画叠层：每帧直读，**不做任何平滑或节流**——动画冲突就发生在
            // 零点几秒里，一平均就什么都看不见了（第三行冻住那次就是教训）。
            if (_anim4 != null)
            {
                var pcx = AdversityRoad.Core.ActorRegistry.Player;
                var hax = pcx != null ? pcx.GetComponent<Combat.HumanoidAnimator>() : null;
                // 待转角与起步闸门也放在这一行：它们同样是零点几秒的瞬态，
                // 放进 0.5 秒一次的汇报行里就永远看不见。
                // 「闸」亮起 = 静止起步正在先转身（这时不动是对的，不是卡住）。
                _anim4.text = hax != null
                    ? string.Format("姿态 {0} ｜ 待转{1:F0}° {2}｜ {3}",
                        hax.DbgPose, pcx.DbgTurnNeed,
                        pcx.DbgStartGate ? "[闸] " : "", hax.DbgNowPlaying)
                    : "";
            }

            if (Time.unscaledTime < _nextReport) return;
            _nextReport = Time.unscaledTime + 0.5f;

            float fps = _frames > 0 ? _frames / Mathf.Max(0.0001f, _accum) : 0f;
            float worstMs = _worst * 1000f;
            // 最坏那一帧对应的冲刺位移：直接告诉你"这一帧人挪了多远"，
            // 与胶囊半径（0.4m 上下）一比就知道会不会穿墙。
            float worstStep = _worst * 5.2f;
            CountCharacters();
            _text.text = string.Format(
                "FPS {0:F0} | 最长帧 {1:F0}ms（单帧位移 {2:F2}m）| 角色 {3}（动捕 {4}）| 敌 {5} | 新增{6} {7}",
                fps, worstMs, worstStep, _charCache, _mocapCache,
                AdversityRoad.Core.ActorRegistry.Enemies.Length,
                AdversityRoad.Core.ActorRegistry.SpawnCount,
                // 最近 6 秒内登记过就把名字亮出来：按下闪的那一下到底生成了什么，
                // 一眼可见，不必再靠搜代码猜
                Time.unscaledTime - AdversityRoad.Core.ActorRegistry.LastSpawnAt < 6f
                    ? "←" + AdversityRoad.Core.ActorRegistry.LastSpawn : "");
            // 最坏帧偏红：一眼能看出这半秒里有没有大顿
            _text.color = worstMs > 60f ? new Color(1f, 0.45f, 0.35f)
                                        : new Color(1f, 0.95f, 0.4f);
            _accum = 0f; _frames = 0; _worst = 0f;

            ReportMovement();
        }

        void ReportMovement()
        {
            if (_move == null) return;
            if (_pc == null)
                _pc = AdversityRoad.Core.ActorRegistry.Player;
            if (_pc == null) { _move.text = ""; return; }

            // 实测移速：由控制器每帧量出来的真实位移换算（不是"打算走多快"，
            // 而是"实际走了多快"）——两者对不上本身就是一条线索。
            float want = _pc.DbgFinalSpeed;
            string cap = _pc.DbgStrafeCap > 0.01f
                ? string.Format("锁定封顶{0:F1}", _pc.DbgStrafeCap)
                : _pc.WalkOnly ? "冲刺锁"
                : _pc.IndoorPace ? "室内步速" : "无封顶";
            _move.text = string.Format(
                "杆{0:F2} 目标{1:F1}/{2:F1}m/s | 行动力×{3:F2} 减速×{4:F2} 出招×{5:F2} | {6} | {7}",
                _pc.DbgInputMag, want, _pc.DbgRawSpeed,
                _pc.DbgApMult, _pc.MoveSpeedMultiplier, _pc.DbgAttackFactor,
                cap, _pc.StrafeActive ? "锁定中" : "自由");
            // 任何一层把速度压到七成以下就标红——那就是"变慢"的那一层
            bool slowed = _pc.DbgApMult < 0.95f || _pc.MoveSpeedMultiplier < 0.95f ||
                          _pc.DbgAttackFactor < 0.95f;
            _move.color = slowed ? new Color(1f, 0.6f, 0.35f) : new Color(0.6f, 0.9f, 1f);
        }

        float _nextCount;
        int _charCache;

        int _mocapCache;

        void CountCharacters()
        {
            // 每 2 秒数一次就够——FindObjects 本身不便宜，别让诊断自己变成卡顿源
            if (Time.unscaledTime < _nextCount) return;
            _nextCount = Time.unscaledTime + 2f;
            var all = Object.FindObjectsByType<Combat.HumanoidAnimator>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            _charCache = all.Length;
            int m = 0;
            for (int i = 0; i < all.Length; i++) if (all[i].IsMocap) m++;
            _mocapCache = m;
        }
    }
}
