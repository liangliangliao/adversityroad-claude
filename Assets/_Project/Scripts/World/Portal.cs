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

        // 刚被传送过来的玩家：必须先【走出】这扇门的触发体，这扇门才会再次生效。
        //
        // 「有时会突然从一个场景跳到另一个场景」的成因：传送是**碰到就走**的，
        // 而落点常常就在门框附近——落地那一刻若仍压在某扇门（可能是回程门）的
        // 触发体里，原来只有一个 1.5 秒的全局冷却挡着；冷却一过，任何一次
        // 走位/闪避的微小抖动让 OnTriggerEnter 再次触发，人就被弹去了别的区域。
        // 冷却是「防连击」，不是「防误触」——真正该判的是"你有没有离开过这扇门"。
        bool _armed = true;

        void Awake() => GetComponent<Collider>().isTrigger = true;

        void OnTriggerExit(Collider other)
        {
            if (other.GetComponentInParent<PlayerController>() != null) _armed = true;
        }

        void OnTriggerEnter(Collider other)
        {
            if (Time.time - _lastTeleport < 1.5f) { _armed = false; return; }
            var player = other.GetComponentInParent<PlayerController>();
            if (player == null) return;
            // 落点压在门里 → 本扇门先失效，等玩家自己走出去再说
            if (!_armed) return;

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
            _armed = false;
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
