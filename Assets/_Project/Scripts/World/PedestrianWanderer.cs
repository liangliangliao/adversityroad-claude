using UnityEngine;
using UnityEngine.AI;

namespace AdversityRoad.World
{
    /// <summary>背景行人：在出生点附近随机漫步（NavMesh）。</summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class PedestrianWanderer : MonoBehaviour
    {
        public float wanderRadius = 18f;

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
