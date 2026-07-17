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
                // 找到解锁该区域的子章，明确告诉玩家这扇门什么时候开
                string when = "先完成当前子章的试炼";
                foreach (var ch in StoryManager.Chapters)
                    if (ch.zoneIndex == targetZoneIndex)
                    {
                        when = "主线推进到【" + ch.title + "】时开启";
                        break;
                    }
                GameEvents.RaiseSubtitle("此路通往【" + targetName + "】，现在被心魔封锁——" + when + "。");
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
