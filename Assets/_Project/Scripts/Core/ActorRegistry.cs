using System.Collections.Generic;
using UnityEngine;

namespace AdversityRoad.Core
{
    /// <summary>
    /// 玩家与敌人的常驻登记表——用来替掉散布在各处 Update 里的 FindObjectOfType。
    ///
    /// 【为什么必须有这个】
    /// FindObjectOfType / FindObjectsOfType 是**全场景线性扫描**，后者每次调用还
    /// **新建一个数组**。而项目里有二十来个道具脚本在自己的 Update 里每帧调它，
    /// 且这些脚本是**按道具实例挂的**——一个场景摆三十个道具，就是每帧三十次全场景
    /// 扫描外加三十次数组分配。实机 131 个角色 + 上千个物件时，这一项就足以把帧率
    /// 压到二十几，而分配出来的垃圾又会周期性触发 GC，表现成 50~66ms 的长帧
    ///（"偶尔快进、偶尔很慢、偶尔卡顿"里的卡顿正是这个）。
    ///
    /// 登记表把这件事变成 O(1) 取值：玩家缓存一份，敌人由自己在启用/禁用时登记。
    /// </summary>
    public static class ActorRegistry
    {
        static Player.PlayerController _player;

        /// <summary>当前玩家（缓存；失效时才回退到一次全场景查找）。</summary>
        public static Player.PlayerController Player
        {
            get
            {
                // Unity 的"假空"：对象被销毁后引用非 null 但 == null 成立，
                // 所以这里必须用 == null 判定，不能用 is null。
                if (_player == null)
                    _player = Object.FindFirstObjectByType<Player.PlayerController>();
                return _player;
            }
        }

        /// <summary>玩家的 Transform（没有玩家时返回 null）。</summary>
        public static Transform PlayerTransform
        {
            get { var p = Player; return p != null ? p.transform : null; }
        }

        static readonly List<AI.EnemyController> _enemies = new List<AI.EnemyController>();
        static AI.EnemyController[] _cache = System.Array.Empty<AI.EnemyController>();
        static bool _dirty = true;

        /// <summary>
        /// 在场的敌人（由 EnemyController 自行登记/注销，不做任何扫描）。
        ///
        /// 【为什么返回数组快照而不是 List 本身】
        /// 调用方大多是 `foreach (var e in ...) { 打它 }`，而打死一个敌人可能当场
        /// 触发 OnDisable → Unregister。若直接遍历 List，这一下就是"遍历期间结构
        /// 被改动"，会抛 InvalidOperationException。返回快照则天然免疫，
        /// 而且与原来的 FindObjectsOfType 返回数组的语义完全一致（drop-in）。
        ///
        /// 快照**只在名册真正变动时**重建（一场战斗里也就几次），
        /// 而不是像原来那样每帧、每个脚本各分配一次。
        /// </summary>
        public static AI.EnemyController[] Enemies
        {
            get
            {
                if (_dirty)
                {
                    // 顺手扫掉已销毁的条目（正常情况下 OnDisable 会自己注销，
                    // 这一步是防止某次异常销毁在名册里留下"假空"引用）
                    _enemies.RemoveAll(e => e == null);
                    _cache = _enemies.ToArray();
                    _dirty = false;
                }
                return _cache;
            }
        }

        /// <summary>诊断：最近一次登记的敌人是谁、什么时候、累计登记了几次。
        /// 「按下闪就多一个打不死的敌人」这类问题，静态搜代码找不到——
        /// 但**所有**敌人都必须经过 Register 这一个入口，在这里记一笔就跑不掉。</summary>
        public static string LastSpawn { get; private set; } = "—";
        public static float LastSpawnAt { get; private set; } = -99f;
        public static int SpawnCount { get; private set; }

        public static void Register(AI.EnemyController e)
        {
            if (e == null || _enemies.Contains(e)) return;
            _enemies.Add(e);
            _dirty = true;
            SpawnCount++;
            LastSpawnAt = Time.unscaledTime;
            LastSpawn = e.profile != null && !string.IsNullOrEmpty(e.profile.displayName)
                ? e.profile.displayName : e.name;
        }

        public static void Unregister(AI.EnemyController e)
        {
            if (e != null && _enemies.Remove(e)) _dirty = true;
        }

        /// <summary>换场景/重建世界时清干净：静态表不会自己归位。</summary>
        public static void Reset()
        {
            _player = null;
            _enemies.Clear();
            _dirty = true;
        }
    }
}
