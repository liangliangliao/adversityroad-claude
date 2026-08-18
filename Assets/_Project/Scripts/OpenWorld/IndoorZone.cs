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
        static int _runPasses;   // "屋里可以跑"的豁免数（跑步机是唯一开口，见 Treadmill）

        /// <summary>玩家此刻在屋里吗。</summary>
        public static bool Inside => _inside > 0;

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
            Refresh(pc);
            if (inside)
                Core.GameEvents.RaiseSubtitle("进屋了——在家里只慢慢走。");
        }

        /// <summary>
        /// 把"屋里只走不跑"重新算一遍。
        ///
        /// 【为什么要留豁免口】"在家只走"是为了不让人在自己客厅里狂奔（也是防晕的一环），
        /// 但健身房的跑步机偏偏**必须跑**：履带把人往后送，走的速度正好等于履带速度，
        /// 于是玩家在自己家的跑步机上永远跑不起来——器材是坏的。
        /// 站上履带即开一个豁免，下来即收回。规则还是"屋里不跑"，
        /// 只是跑步机是屋里唯一一个"就是用来跑的地方"。
        /// </summary>
        public static void Refresh(PlayerController pc)
        {
            if (pc == null) pc = FindFirstObjectByType<PlayerController>();
            if (pc != null) pc.WalkOnly = _inside > 0 && _runPasses <= 0;
        }

        /// <summary>领取/交回"屋里可以跑"的豁免（跑步机上下履带时调用）。</summary>
        public static void SetRunPass(bool granted)
        {
            _runPasses = Mathf.Max(0, _runPasses + (granted ? 1 : -1));
            Refresh(null);
        }

        /// <summary>重建世界/重载场景时清零：静态计数不会自己归位。</summary>
        public static void ResetAll()
        {
            _inside = 0;
            _runPasses = 0;
            ThirdPersonCamera.IndoorMode = false;
            SitController.ForceStand();   // 绝不能带着"坐着"的状态离开住处
            var pc = FindFirstObjectByType<PlayerController>();
            if (pc != null) pc.WalkOnly = false;
        }
    }
}
