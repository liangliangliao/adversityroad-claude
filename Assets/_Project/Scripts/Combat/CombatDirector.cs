using System.Collections.Generic;
using UnityEngine;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 战斗导演（对齐大型动作游戏的「攻击令牌 / 围攻礼让」机制）：
    /// 同一时刻只允许有限数量的普通敌人真正对玩家发起攻击，其余敌人围绕走位、
    /// 伺机而动——避免被一群敌人同时糊脸的廉价难度，让群战更有节奏、更可读
    /// （蝙蝠侠·阿卡姆 / 战神 / 中土世界 / 黑神话 都采用此技术）。
    ///
    /// · Boss 不占用、不受令牌限制（它是舞台的主角，始终可攻）；
    /// · 令牌在攻击一套结束、被打断（硬直/击倒）、死亡、脱离攻击态时归还；
    /// · 令牌持有超时自动回收（保险：防止某敌人异常未归还导致其他敌人一直干等）。
    /// </summary>
    public static class CombatDirector
    {
        /// <summary>同时允许攻击的普通敌人上限（大作群战常用 1—2）。</summary>
        public const int MaxConcurrentAttackers = 2;
        const float HoldTimeout = 3.5f;   // 令牌最长持有时间（保险回收）

        static readonly Dictionary<MonoBehaviour, float> _holders =
            new Dictionary<MonoBehaviour, float>();

        /// <summary>尝试取得攻击令牌。Boss 直接放行；普通敌人受并发上限约束。</summary>
        public static bool TryAcquire(MonoBehaviour enemy, bool isBoss)
        {
            if (enemy == null) return false;
            if (isBoss) return true;
            Cleanup();
            if (_holders.ContainsKey(enemy)) { _holders[enemy] = Time.time; return true; }
            if (_holders.Count >= MaxConcurrentAttackers) return false;
            _holders[enemy] = Time.time;
            return true;
        }

        public static void Release(MonoBehaviour enemy)
        {
            if (enemy != null) _holders.Remove(enemy);
        }

        /// <summary>清理已销毁的敌人与超时未归还的令牌。</summary>
        static void Cleanup()
        {
            if (_holders.Count == 0) return;
            _tmp.Clear();
            foreach (var kv in _holders)
                if (kv.Key == null || Time.time - kv.Value > HoldTimeout) _tmp.Add(kv.Key);
            foreach (var k in _tmp) _holders.Remove(k);
        }
        static readonly List<MonoBehaviour> _tmp = new List<MonoBehaviour>();

        public static void Clear() => _holders.Clear();
    }
}
