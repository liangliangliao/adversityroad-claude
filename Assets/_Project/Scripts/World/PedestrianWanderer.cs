using UnityEngine;
using UnityEngine.AI;

namespace AdversityRoad.World
{
    /// <summary>背景行人：在出生点附近随机漫步（NavMesh）。</summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class PedestrianWanderer : MonoBehaviour
    {
        public float wanderRadius = 18f;
        /// <summary>程序化人形动画：按 NavMesh 实际移速喂步态（不喂就一直站着滑行）。</summary>
        public Combat.HumanoidAnimator anim;

        NavMeshAgent _agent;
        Vector3 _home;
        float _nextPick;

        void Start()
        {
            _agent = GetComponent<NavMeshAgent>();
            _home = transform.position;
            _agent.speed = Random.Range(1.2f, 2.4f);
        }

        void Update()
        {
            if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh) return;

            // 步态：把实际移速折算成 0-1 喂给骨骼动画，行人才是"走"而不是"滑"
            if (anim != null)
            {
                float v = _agent.velocity.magnitude;
                anim.SetLocomotion(Mathf.Clamp01(v / 3.2f), false, true, v);
                anim.SetArmed(false);   // 路人是平民，用空手那一套
            }

            if (Time.time < _nextPick) return;
            if (_agent.pathPending || _agent.remainingDistance > 0.8f) return;

            Vector2 r = Random.insideUnitCircle * wanderRadius;
            Vector3 candidate = _home + new Vector3(r.x, 0, r.y);
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                _agent.SetDestination(hit.position);
            _nextPick = Time.time + Random.Range(2f, 6f);
        }
    }
}
