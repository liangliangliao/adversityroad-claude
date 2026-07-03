using UnityEngine;

namespace AdversityRoad.World
{
    /// <summary>背景车辆：在道路两端点之间往返行驶（到头调头）。</summary>
    public class CarMover : MonoBehaviour
    {
        public Vector3 pointA;
        public Vector3 pointB;
        public float speed = 8f;

        bool _toB = true;

        void Update()
        {
            Vector3 target = _toB ? pointB : pointA;
            Vector3 to = target - transform.position;
            to.y = 0;
            if (to.magnitude < 1.5f)
            {
                _toB = !_toB;
                return;
            }
            transform.position += to.normalized * speed * Time.deltaTime;
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(to), 4f * Time.deltaTime);
        }
    }
}
