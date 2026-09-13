using UnityEngine;

namespace AdversityRoad.World
{
    /// <summary>
    /// 「从入口进来、走到出口出去」这个过程有没有真的完成。
    ///
    /// 外部敌人的关卡改成走出去就算通关之后，必须有人守住玩家原话里的那半句：
    /// **「但是必须要从一个关卡的入口沿着某个方向通过，到另一个出口，这个过程是
    /// 必须要完成的」**。没有这道检查，"不必交战"就会退化成"在门口原地转个身
    /// 再走回去也算通关"——那不是不应战，那是没进过这一关。
    ///
    /// 判定口径只有一条，而且是**几何**的，不靠时间也不靠计步：
    /// 记下玩家进这个区时的落点（关卡入口），玩家站进出口门里的那一刻，
    /// 量一次两点之间的直线距离。跨过 <see cref="MinDistance"/> 才算"通过"。
    ///
    /// 【为什么不做逐帧的路径长度累计】没必要。经典 24 关里，入口落点到"向前门"
    /// 的直线距离最短的一关是两元赌桌的 22.5 米，最长的城市广场 92 米，
    /// 而 12 米这个阈值把"在入口门口来回蹭"挡在外面绰绰有余。
    /// 逐帧累计路程反而会把"绕远路"和"走过去"算成两回事，那不是规则想区分的东西。
    ///
    /// 静态状态：场景重载后自己归零，重载时玩家本来就要重新进关。
    /// </summary>
    public static class LevelTraverse
    {
        /// <summary>算"通过了"的最短直线距离（米）。</summary>
        public const float MinDistance = 12f;

        static int _zone = -1;
        static Vector3 _entry;

        /// <summary>这一关的入口落点（没有记录时返回 false）。</summary>
        public static bool TryEntry(int zone, out Vector3 entry)
        {
            entry = _entry;
            return _zone == zone && _zone >= 0;
        }

        /// <summary>
        /// 玩家进入某个区域：记下入口。
        ///
        /// 由 <see cref="ZoneBuilder.CurrentZoneId"/> 的写入口统一触发——
        /// 传送门、传送面板、生成场景进出、开局落点全都要经过那一行，
        /// 在那里记一次，就不会漏掉任何一条进关的路。
        /// 玩家对象还没建出来时（开局世界刚搭完）退回该区的出生点，
        /// 那本来就是这一关的入口。
        /// </summary>
        public static void NoteEntered(int zone)
        {
            if (zone < 0) { _zone = -1; return; }
            _zone = zone;
            var player = AdversityRoad.Core.ActorRegistry.Player;
            _entry = player != null ? player.transform.position : ZoneBuilder.PlayerSpawnOf(zone);
        }

        /// <summary>玩家此刻站在 at，算不算"已经从入口穿到了这里"。</summary>
        public static bool Crossed(int zone, Vector3 at, out float distance)
        {
            distance = 0f;
            if (_zone < 0 || _zone != zone) return false;
            Vector3 d = at - _entry;
            d.y = 0f;                     // 只看平面距离：上下楼不算横穿关卡
            distance = d.magnitude;
            return distance >= MinDistance;
        }
    }
}
