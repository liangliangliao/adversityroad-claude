using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.World
{
    /// <summary>
    /// 区域传送门：玩家【站在门里停留一小会儿】才会传送；未解锁章节的区域会被剧情锁住。
    ///
    /// 【为什么不是"碰到就走"】
    /// 反复出现的"一边移动一边打着打着突然跳到另一个场景"，根因是传送用的是
    /// OnTriggerEnter——碰一下就走。而战斗中玩家的位移根本不受自己完全控制：
    /// 翻滚 10m/s、绝招突进 2.6m、被击退、敌人把你顶开……任何一次擦过门框
    /// 都会被判成"我要换区域"。冷却和"必须先走出去"只能防重复触发，
    /// 防不住这种**误触**——因为误触本来就是一次合法的首次进入。
    ///
    /// 大型动作游戏对区域切换的通行做法是三条一起上，这里全部照做：
    ///   ① 要【停留】：站进门里连续 DwellTime 秒才生效，擦过去不算；
    ///   ② 要【慢】：高速穿过（翻滚/突进/被击飞）期间计时不累积，一旦提速就清零；
    ///   ③ 【战斗中不放行】：附近还有活着且已经盯上你的敌人时，门直接不开，
    ///      并明确告诉玩家为什么——这同时避免了"打到一半被传走、回来敌人全没了"。
    /// 停留时间刻意压到 0.2 秒（≈6 帧）：正常走进门几乎无感，挡住的只是"擦过去"那一帧。
    /// 真正拦住误触的是 ② 和 ③——它们不需要玩家等，所以门不该有等待感。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Portal : MonoBehaviour
    {
        public int targetZoneIndex;
        public Vector3 targetPosition;
        public string targetName = "";

        /// <summary>需要连续停留的时间。
        ///
        /// 上一版设成 0.75 秒，玩家立刻反馈"通关进下一关为什么要站定几秒"——
        /// 是对的：正常走进门本来就该立刻过去，等待感只该出现在**不正常**的进入方式上。
        /// 误触本来也不是靠"等得久"挡住的，真正挡住它的是下面两条（高速穿过不计时、
        /// 战斗中不放行）。所以停留时间只要够长到滤掉"一帧擦过"就行：
        /// 0.2 秒 ≈ 6 帧，走进去几乎无感，而任何一次翻滚/突进都会先被速度门槛拦下。</summary>
        const float DwellTime = 0.2f;

        /// <summary>判定"这不是在正常走路"的速度上限（m/s）。跑步 ≈6，翻滚 10+，突进更快。</summary>
        const float WalkThroughSpeed = 7.5f;

        /// <summary>战斗封锁半径：这个范围内有盯上你的活敌人就不放行。</summary>
        const float CombatLockRadius = 22f;

        static float _lastTeleport = -10f;

        // 刚被传送过来的玩家：必须先【走出】这扇门的触发体，这扇门才会再次生效。
        bool _armed = true;
        float _dwell;
        Vector3 _lastPlayerPos;
        bool _tracking;
        float _lastHint = -99f;

        void Awake() => GetComponent<Collider>().isTrigger = true;

        void OnTriggerExit(Collider other)
        {
            if (other.GetComponentInParent<PlayerController>() == null) return;
            _armed = true;
            _dwell = 0f;
            _tracking = false;
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<PlayerController>() == null) return;
            // 刚被传送过来就压在门里 → 本扇门先失效，等玩家自己走出去再说
            if (Time.time - _lastTeleport < 1.5f) _armed = false;
            _dwell = 0f;
            _tracking = false;
        }

        void OnTriggerStay(Collider other)
        {
            if (!_armed) return;
            var player = other.GetComponentInParent<PlayerController>();
            if (player == null) return;

            // ---- ② 高速穿过不算停留：翻滚/突进/被击退期间计时清零 ----
            Vector3 pos = player.transform.position;
            if (!_tracking) { _lastPlayerPos = pos; _tracking = true; }
            float speed = Time.deltaTime > 1e-4f
                ? Vector3.Distance(pos, _lastPlayerPos) / Time.deltaTime : 0f;
            _lastPlayerPos = pos;
            if (speed > WalkThroughSpeed || player.IsDodging) { _dwell = 0f; return; }

            // ---- ③ 战斗中不放行 ----
            if (EnemyEngaged(pos))
            {
                _dwell = 0f;
                Hint("战斗未了——先解决眼前的敌人，才能离开这片区域。");
                return;
            }

            // ---- 剧情锁 ----
            var story = StoryManager.Instance;
            if (story != null && !story.ZoneUnlocked(targetZoneIndex))
            {
                _dwell = 0f;
                string when = "先完成当前子章的试炼";
                foreach (var ch in StoryManager.Chapters)
                    if (ch.zoneIndex == targetZoneIndex)
                    {
                        when = "主线推进到【" + ch.title + "】时开启";
                        break;
                    }
                Hint("此路通往【" + targetName + "】，现在被心魔封锁——" + when + "。");
                return;
            }

            // ---- ① 停留计时 ----
            // 0.2 秒内不再刷任何提示：正常走进门应当是"走过去就到了"，
            // 弹一句"站定 0.1 秒"只会让玩家以为这里有个需要等待的机关。
            _dwell += Time.deltaTime;
            if (_dwell < DwellTime) return;

            Teleport(player);
        }

        /// <summary>附近是否还有活着且已经进入战斗状态的敌人。</summary>
        static bool EnemyEngaged(Vector3 pos)
        {
            foreach (var e in Object.FindObjectsByType<AI.EnemyController>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                // 待机/巡逻的敌人还没发现你，不该锁门；只有真正咬上来的才算交战
                if (e == null || e.State == AI.EnemyState.Dead ||
                    e.State == AI.EnemyState.Idle || e.State == AI.EnemyState.Patrol)
                    continue;
                if (Vector3.SqrMagnitude(e.transform.position - pos)
                    <= CombatLockRadius * CombatLockRadius) return true;
            }
            return false;
        }

        void Hint(string msg, float interval = 1.5f)
        {
            if (Time.time - _lastHint < interval) return;
            _lastHint = Time.time;
            GameEvents.RaiseSubtitle(msg);
        }

        void Teleport(PlayerController player)
        {
            _lastTeleport = Time.time;
            _armed = false;
            _dwell = 0f;
            _tracking = false;
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.position = targetPosition;
            if (cc != null) cc.enabled = true;
            ZoneBuilder.CurrentZoneId = ZoneBuilder.ZoneIdOf(targetZoneIndex);
            if (!string.IsNullOrEmpty(targetName))
                GameEvents.RaiseSubtitle("—— 进入 " + targetName + " ——");
        }
    }
}
