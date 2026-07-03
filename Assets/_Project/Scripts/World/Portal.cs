using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.World
{
    /// <summary>
    /// 区域传送门：玩家走入后传送到目标区域；未解锁章节的区域会被剧情锁住。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Portal : MonoBehaviour
    {
        public int targetZoneIndex;
        public Vector3 targetPosition;
        public string targetName = "";

        static float _lastTeleport = -10f;

        void Awake() => GetComponent<Collider>().isTrigger = true;

        void OnTriggerEnter(Collider other)
        {
            if (Time.time - _lastTeleport < 1.5f) return;
            var player = other.GetComponentInParent<PlayerController>();
            if (player == null) return;

            var story = StoryManager.Instance;
            if (story != null && !story.ZoneUnlocked(targetZoneIndex))
            {
                GameEvents.RaiseSubtitle("此路被心魔封锁——先完成当前章节的试炼。");
                return;
            }

            _lastTeleport = Time.time;
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.position = targetPosition;
            if (cc != null) cc.enabled = true;
            ZoneBuilder.CurrentZoneId = ZoneBuilder.ZoneIdOf(targetZoneIndex);
            if (!string.IsNullOrEmpty(targetName))
                GameEvents.RaiseSubtitle("—— 进入 " + targetName + " ——");
        }
    }
}
