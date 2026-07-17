using UnityEngine;

namespace AdversityRoad.AI
{
    /// <summary>
    /// 敌人生成钩子：GameBootstrap 在启动时注入生成委托，
    /// Boss 行为组件（明天之王召唤泥怪、旧我失败召回）用它在战斗中生成援军。
    /// 生成的敌人一律 uniqueId=true，不推进章节任务。
    /// </summary>
    public static class EnemySpawnHook
    {
        public static System.Func<EnemyType, EnemyTier, Vector3, bool, GameObject> Spawn;

        /// <summary>在指定位置附近生成一个敌人（吸附 NavMesh），钩子未注入时返回 null。</summary>
        public static GameObject SpawnNear(EnemyType type, EnemyTier tier, Vector3 pos)
        {
            if (Spawn == null) return null;
            if (UnityEngine.AI.NavMesh.SamplePosition(pos, out var hit, 6f,
                    UnityEngine.AI.NavMesh.AllAreas))
                pos = hit.position + Vector3.up * 1.1f;
            return Spawn(type, tier, pos, true);
        }

        /// <summary>场上（半径内）某基础 id 前缀的存活敌人数——召唤类 Boss 控制人数上限。</summary>
        public static int AliveCount(string baseIdPrefix)
        {
            int n = 0;
            foreach (var e in Object.FindObjectsOfType<EnemyController>())
                if (e.State != EnemyState.Dead && e.profile != null &&
                    e.profile.enemyId != null && e.profile.enemyId.StartsWith(baseIdPrefix))
                    n++;
            return n;
        }
    }
}
