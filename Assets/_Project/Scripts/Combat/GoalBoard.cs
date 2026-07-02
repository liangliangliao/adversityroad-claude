using UnityEngine;
using AdversityRoad.Mobile;
using AdversityRoad.Player;

namespace AdversityRoad.Combat
{
    /// <summary>目标板：靠近后按住 E 键或触屏"互"键蓄力，恢复意志与决断。</summary>
    public class GoalBoard : MonoBehaviour
    {
        public float interactRange = 2.5f;
        public float restorePerSec = 20f;

        PlayerController _player;

        void Update()
        {
            if (_player == null)
            {
                _player = FindObjectOfType<PlayerController>();
                if (_player == null) return;
            }
            if (Vector3.Distance(transform.position, _player.transform.position) > interactRange) return;
            if (Input.GetKey(KeyCode.E) || MobileInput.GetHeld("Interact"))
                _player.Stats.RestoreMental(restorePerSec * Time.deltaTime);
        }
    }
}
