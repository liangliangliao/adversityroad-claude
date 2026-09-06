using UnityEngine;

namespace AdversityRoad.OpenWorld
{
    /// <summary>
    /// 住所 = 安全区。玩家在自己家里时，世界不该继续对他施压。
    ///
    /// 【为什么要单独一个判定，而不是每个系统各写一份】
    /// "在家时不发生"这条规则会落在很多系统上：随机社会事件、逆境导演的加压、
    /// 敌人的挑衅台词……这个工程反复吃过同一个亏——一条规则散在几十个调用点，
    /// 总有几个漏掉，玩家看到的就是"改了还在出现"。所以判定只写这一处，
    /// 各系统在 Update 第一行问一句。
    ///
    /// 判定用的是宅子的整块地（含院子），不是室内触发盒：站在自家院子里被人挑衅，
    /// 和站在客厅里被挑衅，是同一件不该发生的事。
    ///
    /// 每帧只算一次并缓存：这条会被十几个 Update 问到，逐个算 Bounds.Contains 是白费。
    /// </summary>
    public static class Sanctuary
    {
        static int _frame = -1;
        static bool _atHome;

        /// <summary>玩家此刻在自己住所（含院子）里吗。</summary>
        public static bool AtHome
        {
            get
            {
                if (_frame == Time.frameCount) return _atHome;
                _frame = Time.frameCount;
                _atHome = false;
                var lot = PlayerVilla.Lot;
                if (lot.size.sqrMagnitude < 1f) return false;   // 宅子还没建
                var p = Core.ActorRegistry.Player;
                if (p == null) return false;
                var q = p.transform.position;
                _atHome = lot.Contains(new Vector3(q.x, lot.center.y, q.z));
                return _atHome;
            }
        }

        /// <summary>某个坐标在住所范围内吗（给不以玩家为中心的系统用）。</summary>
        public static bool IsHome(Vector3 pos)
        {
            var lot = PlayerVilla.Lot;
            if (lot.size.sqrMagnitude < 1f) return false;
            return lot.Contains(new Vector3(pos.x, lot.center.y, pos.z));
        }
    }
}
