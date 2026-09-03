using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.Core;

namespace AdversityRoad.World
{
    /// <summary>
    /// 围观群众。
    ///
    /// 玩家的要求：广场这类场景在战斗时要有一圈人在看，"能够去观看玩家和敌人的战斗，
    /// 能够欢呼，能够议论起来"，而不是像现在的行人那样只会来回走。
    ///
    /// 所以这不是又一个 PedestrianWanderer：围观者**不走**，他站在自己的位置上，
    /// 身体朝着战斗的方向，按现场发生的事说话——
    ///   · 没打起来：零星闲聊，音量小、间隔长（人群的底噪）
    ///   · 打起来了：转过身盯着看，议论变密
    ///   · 有人被击倒：起哄叫好（OnEnemyKilled）
    /// 三档由「附近有没有正在交战的敌人」决定，与镜头判交战用的是同一条判据
    ///（状态不是待机/巡逻），全局口径一致。
    ///
    /// 台词按档分池，随机取；同一个人两次不重样（记住上一句）。
    /// 每人各自的说话间隔带随机相位，避免一圈人整齐划一地同时开口。
    /// </summary>
    public class Spectator : MonoBehaviour
    {
        /// <summary>看向哪儿：战场中心（通常是竞技场圆心）。</summary>
        public Vector3 arenaCenter;
        /// <summary>多远之内的交战算"这场架"（米）。</summary>
        public float watchRange = 26f;

        static readonly string[] Idle =
        {
            "……今天风挺大。", "你说他会来吗？", "站这儿看得清楚。",
            "刚才那边好像有动静。", "我就看看，不掺和。", "这地方总有事。",
        };
        static readonly string[] Watching =
        {
            "打起来了！", "这下有得看了。", "他撑得住吗？",
            "别退啊——", "那一下够狠的。", "让开点，别挡着。",
            "他还站着呢。", "这架看着不轻松。",
        };
        static readonly string[] Cheer =
        {
            "好！", "漂亮！", "干得漂亮！", "这一下解气！", "厉害啊！", "赢了赢了！",
        };

        TextMesh _tm;
        Combat.HumanoidAnimator _anim;
        Transform _player;
        float _nextTalk, _hideAt, _cheerUntil;
        float _phase;                 // 各人各自的动作相位，避免一圈人同手同脚
        string _last = "";
        System.Random _rng;
        // 每人一个不同的随机种子。不能用 GetInstanceID()（Unity 6000.5 起废弃），
        // 自增序号就够了——只要一圈人彼此不同相位即可。
        static int _seq;

        void Start()
        {
            _rng = new System.Random(++_seq * 7919);
            _phase = (float)(_rng.NextDouble() * 10.0);
            _anim = GetComponent<Combat.HumanoidAnimator>();
            var p = ActorRegistry.Player;
            if (p != null) _player = p.transform;

            var go = new GameObject("SpectatorLine");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0, 1.15f, 0);
            _tm = go.AddComponent<TextMesh>();
            _tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _tm.fontSize = 48;
            _tm.characterSize = 0.045f;
            _tm.anchor = TextAnchor.LowerCenter;
            _tm.alignment = TextAlignment.Center;
            _tm.color = new Color(0.95f, 0.95f, 0.85f, 0.9f);
            var r = go.GetComponent<MeshRenderer>();
            if (_tm.font != null) r.material = _tm.font.material;
            _tm.text = "";

            // 起始相位随机：一圈人不要同时开口
            _nextTalk = Time.time + 2f + (float)_rng.NextDouble() * 8f;
            GameEvents.OnEnemyKilled += OnKill;
        }

        void OnDestroy() => GameEvents.OnEnemyKilled -= OnKill;

        void OnKill(string _)
        {
            // 只有看得见这场架的人才起哄
            if (!Watching_()) return;
            _cheerUntil = Time.time + 3.5f;
            _nextTalk = Time.time + (float)_rng.NextDouble() * 0.8f;
        }

        /// <summary>这一圈里有没有正在交战的敌人（与镜头侧同一条判据）。</summary>
        bool Watching_()
        {
            foreach (var e in ActorRegistry.Enemies)
            {
                if (e == null || e.State == AI.EnemyState.Dead) continue;
                if (e.State == AI.EnemyState.Idle || e.State == AI.EnemyState.Patrol) continue;
                if ((e.transform.position - arenaCenter).sqrMagnitude < watchRange * watchRange)
                    return true;
            }
            return false;
        }

        void Update()
        {
            bool watching = Watching_();

            // 朝向：打起来了就看战斗（优先看玩家——观众看的是"他"），
            // 没打就大致朝场地中心站着。转身要慢，一圈人齐刷刷转头很假。
            Vector3 look = watching && _player != null ? _player.position : arenaCenter;
            Vector3 to = look - transform.position; to.y = 0f;
            if (to.sqrMagnitude > 0.04f)
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, Quaternion.LookRotation(to.normalized), 120f * Time.deltaTime);

            // ===== 必须每帧驱动动画层，否则人是**冻住的** =====
            //
            // 玩家问"围观 NPC 没有相应动作反应呢？"——因为我只给了朝向和台词，
            // 没有人驱动 HumanoidAnimator。行人靠 PedestrianWanderer 每帧调
            // SetLocomotion 才动得起来；围观者没有那个组件，动画层从头到尾
            // 收不到任何输入，骨架就停在绑定姿势上，看起来像一排木桩。
            //
            // 速度传 0：他们本来就站着不动，要的是**站立待机**那一档动画
            //（呼吸、重心微调），而不是走路。
            //
            // 【上一轮为什么还是没反应】我用的是 SetCombatReady + PoseState.Flinch：
            //   · SetCombatReady 摆的是**格斗预备架势**——一圈看热闹的人全站成
            //     准备开打的桩子，这不是围观；
            //   · Flinch 在程序骨架（HumanoidRig）的招式 switch 里**根本没有分支**，
            //     等于一条空指令，叫好的时候身体一动不动。
            // 现在走 SetSpectate：观望=探身抱臂换脚、欢呼=举臂击掌踮跳，两档都
            // 真的画在程序骨架上。相位按人错开，一圈人不会同手同脚。
            if (_anim != null)
            {
                _anim.SetLocomotion(0f, false, true, 0f);
                _anim.SetSpectate(Time.time < _cheerUntil ? 2 : watching ? 1 : 0, _phase);
            }

            if (Time.time >= _nextTalk)
            {
                bool cheer = Time.time < _cheerUntil;
                var pool = cheer ? Cheer : watching ? Watching : Idle;
                string line = pool[_rng.Next(pool.Length)];
                if (line == _last && pool.Length > 1)          // 同一人不连说两遍一样的
                    line = pool[(_rng.Next(pool.Length - 1) + 1) % pool.Length];
                _last = line;
                _tm.text = line;
                _hideAt = Time.time + (cheer ? 1.6f : 2.4f);
                // 打起来时议论更密；闲时稀疏，免得广场变成菜市场
                float gap = cheer ? 1.2f : watching ? 4.5f : 11f;
                _nextTalk = Time.time + gap + (float)_rng.NextDouble() * gap;
            }
        }

        void LateUpdate()
        {
            if (_tm == null) return;
            if (Camera.main != null)
                _tm.transform.rotation = Quaternion.LookRotation(
                    _tm.transform.position - Camera.main.transform.position);
            if (_tm.text.Length > 0 && Time.time > _hideAt) _tm.text = "";
        }

        // ===== 布置 =====

        /// <summary>
        /// 在 center 周围摆一圈围观者。
        ///
        /// 半径给的是**观众席**的位置：要在战斗半径之外，否则围观者会被卷进判定框，
        /// 变成"群众参战"。每个人的角度与半径各带一点随机，一圈人不要排成正多边形。
        /// 落点一律吸附到导航面，吸不到就跳过——宁可少一个人，也不要有人站在半空。
        /// </summary>
        public static void Ring(WorldContext ctx, Vector3 center,
                                float radius, int count, int seed)
        {
            var rng = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                float ang = (360f / count) * i + (float)(rng.NextDouble() * 18.0 - 9.0);
                float rad = radius + (float)(rng.NextDouble() * 2.4 - 1.2);
                Vector3 at = center + Quaternion.Euler(0, ang, 0) * Vector3.forward * rad;
                if (!UnityEngine.AI.NavMesh.SamplePosition(at, out UnityEngine.AI.NavMeshHit hit,
                        4f, UnityEngine.AI.NavMesh.AllAreas))
                    continue;
                var go = ZoneBuilder.MakeHumanoidNpc(ctx, "Spectator", hit.position, rng);
                // 围观者不走动：把寻路代理关掉，人就钉在观众席上
                var agent = go.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (agent != null) agent.enabled = false;
                var sp = go.AddComponent<Spectator>();
                sp.arenaCenter = center;
            }
        }

        /// <summary>
        /// 沿一条走廊贴墙站两排。
        ///
        /// 走廊关（欠条长廊只有 9 米宽）不能用 Ring：一圈人有一半会站在走道正中，
        /// 而围观者是关掉寻路、带碰撞体的——那就成了一堵挡住玩家的人墙。
        /// 这里只在轴线两侧各让开 sideOffset 米，走道始终是空的。
        /// </summary>
        public static void Line(WorldContext ctx, Vector3 from, Vector3 to,
                                float sideOffset, int perSide, int seed)
        {
            var rng = new System.Random(seed);
            Vector3 axis = to - from; axis.y = 0f;
            if (axis.sqrMagnitude < 0.01f) return;
            Vector3 side = Vector3.Cross(Vector3.up, axis.normalized);
            Vector3 mid = (from + to) * 0.5f;
            for (int i = 0; i < perSide; i++)
                for (int s = -1; s <= 1; s += 2)
                {
                    float t = perSide == 1 ? 0.5f
                            : i / (float)(perSide - 1) * 0.86f + 0.07f;
                    Vector3 at = from + axis * t
                               + side * (s * (sideOffset + (float)(rng.NextDouble() * 0.4 - 0.2)));
                    if (!UnityEngine.AI.NavMesh.SamplePosition(at, out UnityEngine.AI.NavMeshHit hit,
                            2f, UnityEngine.AI.NavMesh.AllAreas))
                        continue;
                    var go = ZoneBuilder.MakeHumanoidNpc(ctx, "Spectator", hit.position, rng);
                    var agent = go.GetComponent<UnityEngine.AI.NavMeshAgent>();
                    if (agent != null) agent.enabled = false;
                    var sp = go.AddComponent<Spectator>();
                    sp.arenaCenter = mid;
                }
        }
    }
}
