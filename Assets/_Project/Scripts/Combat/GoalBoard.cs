using UnityEngine;
using AdversityRoad.Player;

namespace AdversityRoad.Combat
{
    /// <summary>目标板（独居小屋）：靠近并按 E 蓄力，恢复意志与决断。</summary>
    public class GoalBoard : MonoBehaviour
    {
        public float interactRange = 2.5f;
        public float restorePerSec = 20f;

        PlayerController _player;

        void Start()
        {
            var p = FindObjectOfType<PlayerController>();
            if (p != null) _player = p;
        }

        void Update()
        {
            if (_player == null) return;
            if (Vector3.Distance(transform.position, _player.transform.position) > interactRange) return;
            if (Input.GetKey(KeyCode.E))
                _player.Stats.RestoreMental(restorePerSec * Time.deltaTime);
        }
    }
}
