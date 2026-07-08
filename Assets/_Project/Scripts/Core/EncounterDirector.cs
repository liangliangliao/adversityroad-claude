using UnityEngine;
using AdversityRoad.AI;
using AdversityRoad.Personalization;
using AdversityRoad.Player;

namespace AdversityRoad.Core
{
    /// <summary>
    /// 个性化遭遇导演（第五阶段核心玩法）：读取玩家画像，
    /// 周期性让「最高分弱点轴」对应的心魔循迹而来发起遭遇战——
    /// 画像不只是数值加成，而是真的决定你会遇见什么敌人。
    /// 难度由画像的挑战承受度决定；场上敌人较多时不加戏。
    /// </summary>
    public class EncounterDirector : MonoBehaviour
    {
        public System.Func<EnemyType, EnemyTier, Vector3, bool, GameObject> spawner;
        public float firstDelay = 75f;

        float _next;

        void Start() => _next = Time.time + firstDelay;

        void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + Random.Range(80f, 140f);

            var gm = GameManager.Instance;
            if (gm == null || gm.CurrentProfile == null || spawner == null) return;
            var player = FindObjectOfType<PlayerController>();
            if (player == null || player.Stats.IsDead) return;

            int alive = 0;
            foreach (var e in FindObjectsOfType<EnemyController>())
                if (e.State != EnemyState.Dead) alive++;
            if (alive >= 3) return;

            var profile = gm.CurrentProfile;
            WeaknessAxis top = TopAxis(profile);
            EnemyType type = MapAxis(top);
            EnemyTier tier = profile.challengeTolerance > 0.72f ? EnemyTier.Standard
                : EnemyTier.Novice;

            Vector3 pos = player.transform.position +
                Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward * 9f;
            if (UnityEngine.AI.NavMesh.SamplePosition(pos, out var hit, 8f,
                    UnityEngine.AI.NavMesh.AllAreas))
                pos = hit.position + Vector3.up * 1.1f;
            else return;

            spawner(type, tier, pos, true);
            GameEvents.RaiseSubtitle("画像感应：你的「" + AxisName(top) + "」心魔循迹而来……");
        }

        static WeaknessAxis TopAxis(PlayerProfile profile)
        {
            WeaknessAxis best = WeaknessAxis.Procrastination;
            float bestScore = -1;
            foreach (var w in profile.weaknessScores)
                if (w.score > bestScore) { bestScore = w.score; best = w.axis; }
            return best;
        }

        static EnemyType MapAxis(WeaknessAxis axis)
        {
            switch (axis)
            {
                case WeaknessAxis.NoiseSensitivity: return EnemyType.CoughAssassin;
                case WeaknessAxis.Shame: return EnemyType.ShameMirror;
                case WeaknessAxis.JobAnxiety: return EnemyType.NoReplyKing;
                case WeaknessAxis.BoundaryConflict:
                case WeaknessAxis.FairnessSensitivity: return EnemyType.TotalResponsibilityJudge;
                case WeaknessAxis.SelfDoubt:
                case WeaknessAxis.LowConfidence:
                case WeaknessAxis.FailureFear: return EnemyType.SelfDoubtWhisper;
                default: return EnemyType.TomorrowPhantom;
            }
        }

        static string AxisName(WeaknessAxis a)
        {
            switch (a)
            {
                case WeaknessAxis.NoiseSensitivity: return "噪声敏感";
                case WeaknessAxis.Shame: return "羞耻敏感";
                case WeaknessAxis.JobAnxiety: return "求职焦虑";
                case WeaknessAxis.SelfDoubt: return "自我怀疑";
                case WeaknessAxis.LowConfidence: return "低信心";
                case WeaknessAxis.FailureFear: return "失败恐惧";
                default: return "拖延";
            }
        }
    }
}
