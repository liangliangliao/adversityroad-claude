using UnityEngine;
using AdversityRoad.Player;

namespace AdversityRoad.OpenWorld
{
    /// <summary>
    /// 室内区：玩家走进这块空间时，把镜头切成"室内舒适模式"。
    ///
    /// 玩家反馈"在房间里挨个转一圈之后会晕"。查下来不是帧率问题，是镜头：
    /// 4.6 米的吊杆在十来米的房间里，每转身、每过门都被墙推缩一次，
    /// 幅度接近 3 米的推轨反复发生；再叠上跟随运镜绕着人转，走一圈就晕了。
    /// 这个触发盒负责告诉镜头"你现在在屋里"，剩下的由 ThirdPersonCamera.IndoorMode 处理。
    ///
    /// 用触发盒而不是"离家近就算室内"：宅子有院子、泳池和车库，
    /// 那些地方是露天的，镜头该照常工作。
    /// </summary>
    public class IndoorZone : MonoBehaviour
    {
        static int _inside;

        public static IndoorZone Create(Vector3 center, Vector3 size, string name = "IndoorZone")
        {
            var go = new GameObject(name);
            go.transform.position = center;
            var box = go.AddComponent<BoxCollider>();
            box.size = size;
            box.isTrigger = true;
            return go.AddComponent<IndoorZone>();
        }

        void OnTriggerEnter(Collider other)
        {
            var pc = other.GetComponentInParent<PlayerController>();
            if (pc == null) return;
            _inside++;
            Apply(pc, true);
        }

        void OnTriggerExit(Collider other)
        {
            var pc = other.GetComponentInParent<PlayerController>();
            if (pc == null) return;
            _inside = Mathf.Max(0, _inside - 1);
            if (_inside == 0) Apply(pc, false);
        }

        static void Apply(PlayerController pc, bool inside)
        {
            ThirdPersonCamera.IndoorMode = inside;
            // 屋里只走不跑（玩家要求）：也顺带把"全速跑过一间小屋"这个晕动源去掉
            if (pc != null) pc.WalkOnly = inside;
            if (inside)
                Core.GameEvents.RaiseSubtitle("进屋了——在家里只慢慢走。");
        }

        /// <summary>重建世界/重载场景时清零：静态计数不会自己归位。</summary>
        public static void ResetAll()
        {
            _inside = 0;
            ThirdPersonCamera.IndoorMode = false;
            var pc = FindFirstObjectByType<PlayerController>();
            if (pc != null) pc.WalkOnly = false;
        }
    }
}
