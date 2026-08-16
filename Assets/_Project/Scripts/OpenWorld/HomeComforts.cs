using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Player;
using AdversityRoad.World;

namespace AdversityRoad.OpenWorld
{
    /// <summary>
    /// 可以坐下/躺下的家具。
    ///
    /// 家里摆了一屋子椅子、沙发和床，但玩家只能站着看——这正是"像样板间而不像家"
    /// 的地方。坐下不是装饰性动作：住处是这个游戏里唯一没有敌人也没有倒计时的空间，
    /// 坐一会儿、在床上躺一会儿本身就是它要提供的东西（躺下会慢慢回体力与意志）。
    ///
    /// 实现上不去改角色控制器：坐下时把 PlayerController 停掉、把人放到座位点、
    /// 把外观压低（或放平）；起身时原样还回去。这样不必新增任何动画资源，
    /// 也不会和战斗、移动、镜头的既有逻辑纠缠。
    /// </summary>
    public class Sittable : MonoBehaviour
    {
        public Vector3 seatOffset = new Vector3(0, 0.55f, 0);
        public Vector3 faceDir = Vector3.forward;
        public bool lieDown;
        public string label = "椅子";
        public float range = 2.6f;

        PlayerController _player;
        float _lastHint = -99f;

        public static Sittable Attach(GameObject go, Vector3 seatOffset, Vector3 faceDir,
            bool lieDown, string label)
        {
            if (go == null) return null;
            var s = go.AddComponent<Sittable>();
            s.seatOffset = seatOffset;
            s.faceDir = faceDir.sqrMagnitude < 0.01f ? Vector3.forward : faceDir.normalized;
            s.lieDown = lieDown;
            s.label = label;
            s.range = lieDown ? 3.4f : 2.6f;
            return s;
        }

        public Vector3 SeatPoint => transform.position + seatOffset;

        void Update()
        {
            if (SitController.Seated) return;
            if (_player == null)
            {
                _player = FindObjectOfType<PlayerController>();
                if (_player == null) return;
            }
            if (Vector3.Distance(SeatPoint, _player.transform.position) > range) return;

            if (Time.time - _lastHint > 8f)
            {
                _lastHint = Time.time;
                GameEvents.RaiseSubtitle("【" + label + "】" +
                    (lieDown ? "躺下休息" : "坐下歇一会儿") + "——" + Mobile.MobileInput.UseHint + "。");
            }
            if (Input.GetKeyDown(KeyCode.E) || Mobile.MobileInput.GetDown("Interact"))
                SitController.Sit(_player, this);
        }
    }

    /// <summary>
    /// 坐下/躺下的执行者：全局只有一处状态，起身时一定能还原。
    /// 挂在玩家身上（第一次坐下时自动补上）。
    /// </summary>
    public class SitController : MonoBehaviour
    {
        static SitController _inst;
        public static bool Seated => _inst != null && _inst._seat != null;

        Sittable _seat;
        Vector3 _standPos;
        Quaternion _visualRot;
        Vector3 _visualPos;
        Transform _visual;
        CharacterController _cc;
        PlayerController _pc;
        float _sinceSit;

        public static void Sit(PlayerController player, Sittable seat)
        {
            if (player == null || seat == null) return;
            if (_inst == null) _inst = player.gameObject.AddComponent<SitController>();
            if (_inst._seat != null) return;
            _inst.Begin(player, seat);
        }

        void Begin(PlayerController player, Sittable seat)
        {
            _pc = player;
            _seat = seat;
            _cc = player.GetComponent<CharacterController>();
            _visual = player.transform.Find("Visual");
            _standPos = player.transform.position;

            if (_cc != null) _cc.enabled = false;
            // 坐姿：人往下沉一点；躺姿：整个人放平躺在床面上
            Vector3 at = seat.SeatPoint + Vector3.up * (seat.lieDown ? 0.35f : 0.42f);
            player.transform.position = at;
            player.transform.rotation = Quaternion.LookRotation(
                new Vector3(seat.faceDir.x, 0f, seat.faceDir.z).normalized);

            if (_visual != null)
            {
                _visualPos = _visual.localPosition;
                _visualRot = _visual.localRotation;
                if (seat.lieDown)
                {
                    _visual.localRotation = Quaternion.Euler(-90f, 0, 0);
                    _visual.localPosition = _visualPos + new Vector3(0, -0.35f, 0.55f);
                }
                else
                {
                    _visual.localPosition = _visualPos + new Vector3(0, -0.42f, 0);
                }
            }

            player.enabled = false;   // 停掉移动/重力，人稳稳待在座位上
            _sinceSit = 0f;
            GameEvents.RaiseSubtitle(seat.lieDown
                ? "你躺了下来。什么都不用做——" + Mobile.MobileInput.UseHint + "起身。"
                : "你坐了下来。" + Mobile.MobileInput.UseHint + "起身。");
        }

        void Update()
        {
            if (_seat == null) return;
            _sinceSit += Time.deltaTime;

            // 休息回复：躺着比坐着快。这是住处该有的作用，不是数值补丁——
            // 从一场打不过的关卡退出来，回家躺一会儿再出门是这个游戏的节奏。
            if (_pc != null && _pc.Stats != null)
            {
                float rate = _seat.lieDown ? 6f : 2.5f;
                _pc.Stats.RestoreMental(rate * Time.deltaTime);
                var st = _pc.Stats;
                st.hp = Mathf.Min(st.maxHp, st.hp + rate * 0.5f * Time.deltaTime);
                GameEvents.RaisePlayerHpChanged(st.hp, st.maxHp);
            }

            if (_sinceSit > 0.4f &&
                (Input.GetKeyDown(KeyCode.E) || Mobile.MobileInput.GetDown("Interact") ||
                 Input.GetKeyDown(KeyCode.Space) || Input.GetAxisRaw("Vertical") != 0f))
                Stand();
        }

        void Stand()
        {
            if (_pc != null) _pc.enabled = true;
            if (_visual != null)
            {
                _visual.localPosition = _visualPos;
                _visual.localRotation = _visualRot;
            }
            // 起身站到座位旁边，避免卡在家具里
            Vector3 side = _seat != null
                ? _seat.SeatPoint - new Vector3(_seat.faceDir.x, 0, _seat.faceDir.z).normalized * -1.4f
                : _standPos;
            side.y = _standPos.y;
            if (_pc != null) _pc.transform.position = side;
            if (_cc != null) _cc.enabled = true;
            if (_pc != null) _pc.NotifyTeleported();
            _seat = null;
            GameEvents.RaiseSubtitle("你站了起来。");
        }
    }

    /// <summary>
    /// 会动的跑步机。
    ///
    /// 玩家的要求是"跑步机可以正常工作"。做成一个播动画的摆设很容易，但那不叫工作。
    /// 这里按真实跑步机的原理来：**开机后履带往后送**，站上去不动就会被送下去，
    /// 想留在上面就得一直往前跑——于是"跑步"这件事真的发生了，
    /// 而不是站着看一个循环动画。跑的时间会转成体力与意志的训练收益。
    /// </summary>
    public class Treadmill : MonoBehaviour
    {
        public float beltSpeed = 2.6f;
        public bool running;

        Transform[] _stripes;
        Transform _belt;
        TextMesh _panel;
        PlayerController _player;
        CharacterController _cc;
        float _lastHint = -99f;
        float _ranSeconds;
        float _rewardTick;

        public static Treadmill Build(WorldContext ctx, Vector3 at)
        {
            var body = ZoneBuilder.Box(ctx, "TreadBase", at + new Vector3(0, 0.2f, 0),
                new Vector3(1.8f, 0.4f, 4.0f), new Color(0.20f, 0.21f, 0.24f));
            var belt = ZoneBuilder.Box(ctx, "TreadBelt", at + new Vector3(0, 0.44f, -0.2f),
                new Vector3(1.4f, 0.08f, 3.2f), new Color(0.12f, 0.12f, 0.13f));

            for (int s = -1; s <= 1; s += 2)
                ZoneBuilder.Box(ctx, "TreadArm", at + new Vector3(s * 0.75f, 0.95f, 1.6f),
                    new Vector3(0.12f, 1.5f, 0.12f), new Color(0.62f, 0.64f, 0.68f));
            ZoneBuilder.Decoration(ctx, "TreadRail", at + new Vector3(0, 1.55f, 1.25f),
                new Vector3(1.6f, 0.1f, 0.8f), new Color(0.62f, 0.64f, 0.68f));
            ZoneBuilder.Decoration(ctx, "TreadPanel", at + new Vector3(0, 1.6f, 1.72f),
                new Vector3(1.5f, 0.55f, 0.12f), new Color(0.18f, 0.22f, 0.28f));

            var t = body.AddComponent<Treadmill>();
            t._belt = belt.transform;

            // 履带上的横纹：跑起来时它们往后走，一眼能看出机器在转
            var stripes = new Transform[7];
            for (int i = 0; i < stripes.Length; i++)
            {
                var st = ZoneBuilder.Decoration(ctx, "BeltStripe",
                    at + new Vector3(0, 0.49f, -1.6f + i * 0.45f),
                    new Vector3(1.3f, 0.02f, 0.12f), new Color(0.32f, 0.34f, 0.36f));
                stripes[i] = st.transform;
            }
            t._stripes = stripes;

            var panelGo = new GameObject("TreadReadout");
            panelGo.transform.position = at + new Vector3(0, 1.62f, 1.64f);
            panelGo.transform.rotation = Quaternion.Euler(0, 180f, 0);
            var tm = panelGo.AddComponent<TextMesh>();
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.fontSize = 46; tm.characterSize = 0.05f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = new Color(0.55f, 0.95f, 0.85f);
            tm.text = "跑步机 · 待机";
            var mr = panelGo.GetComponent<MeshRenderer>();
            if (tm.font != null) mr.material = tm.font.material;
            t._panel = tm;

            OpenWorldBuilder.HomeSign(at + new Vector3(0, 2.5f, 0), "跑 步 机");
            return t;
        }

        void Update()
        {
            if (_player == null)
            {
                _player = FindObjectOfType<PlayerController>();
                if (_player == null) return;
                _cc = _player.GetComponent<CharacterController>();
            }

            Vector3 beltAt = _belt != null ? _belt.position : transform.position;
            Vector3 p = _player.transform.position;
            bool onBelt = Mathf.Abs(p.x - beltAt.x) < 1.0f &&
                          Mathf.Abs(p.z - beltAt.z) < 1.9f &&
                          p.y - beltAt.y > -0.4f && p.y - beltAt.y < 2.4f;
            bool near = Vector3.Distance(p, transform.position) < 3.4f;

            if (near && Time.time - _lastHint > 8f)
            {
                _lastHint = Time.time;
                GameEvents.RaiseSubtitle(running
                    ? "【跑步机】运行中——站上履带往前跑；" + Mobile.MobileInput.UseHint + "停机。"
                    : "【跑步机】" + Mobile.MobileInput.UseHint + "开机，然后站到履带上。");
            }
            if (near && (Input.GetKeyDown(KeyCode.E) || Mobile.MobileInput.GetDown("Interact")))
            {
                running = !running;
                GameEvents.RaiseSubtitle(running ? "跑步机启动。" : "跑步机停机。");
            }

            if (!running)
            {
                if (_panel != null) _panel.text = "跑步机 · 待机";
                return;
            }

            float dt = Time.deltaTime;
            // 履带纹往后走并循环
            if (_stripes != null)
                foreach (var st in _stripes)
                {
                    if (st == null) continue;
                    var lp = st.position;
                    lp.z -= beltSpeed * dt;
                    if (lp.z < beltAt.z - 1.7f) lp.z += 3.15f;
                    st.position = lp;
                }

            if (onBelt && _cc != null && _cc.enabled)
            {
                // 履带把人往后送：站着不动就会被送下去，想留下就得跑
                _cc.Move(new Vector3(0, 0, -beltSpeed * dt));
                _ranSeconds += dt;
                _rewardTick += dt;
                if (_rewardTick > 5f)
                {
                    _rewardTick = 0f;
                    if (_player.Stats != null)
                    {
                        _player.Stats.RestoreMental(4f);
                        GameEvents.RaiseSubtitle("跑了 " + Mathf.RoundToInt(_ranSeconds) + " 秒——意志稳了一点。");
                    }
                }
            }

            if (_panel != null)
                _panel.text = "速度 " + beltSpeed.ToString("F1") + " · 已跑 " +
                              Mathf.RoundToInt(_ranSeconds) + " 秒";
        }
    }
}
