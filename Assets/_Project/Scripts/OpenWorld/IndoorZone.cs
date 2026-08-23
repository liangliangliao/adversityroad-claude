using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.Player;

namespace AdversityRoad.OpenWorld
{
    /// <summary>
    /// 室内区：玩家在这块空间里时，镜头切成"室内舒适模式"，并且只走不跑。
    ///
    /// 玩家反馈"在房间里挨个转一圈之后会晕"。查下来不是帧率问题，是镜头：
    /// 4.6 米的吊杆在十来米的房间里，每转身、每过门都被墙推缩一次，
    /// 幅度接近 3 米的推轨反复发生；再叠上跟随运镜绕着人转，走一圈就晕了。
    ///
    /// 【为什么不用触发盒了：玩家反馈"走出住所后跑步还是被禁用"】
    /// 上一版靠 OnTriggerEnter/Exit 累加一个静态计数。问题是"离开"有好几种走法，
    /// 而只有一种会发退出事件：
    ///   · 走出大门 —— 会发，计数正确；
    ///   · 从传送面板直接跳到别的关卡 —— 玩家被瞬移走，触发盒往往等不到退出回调；
    ///   · 世界重建把触发盒直接销毁 —— 更不会有人来减那个计数。
    /// 后两种一旦发生，计数就永远停在 1，于是**这辈子再也跑不起来**——
    /// 玩家看到的就是"出门了还只能走"。
    ///
    /// 现在改成**按包含关系判定**：每帧看玩家坐标在不在任何一块室内区里。
    /// 这是个无状态的判断，不管玩家是走出去的、被传送走的、还是整个世界被重建的，
    /// 下一帧状态就是对的。跑步机的豁免口照旧（见 SetRunPass）。
    /// </summary>
    public class IndoorZone : MonoBehaviour
    {
        static readonly List<IndoorZone> _all = new List<IndoorZone>();
        static int _runPasses;      // "屋里可以跑"的豁免数（跑步机是唯一开口，见 Treadmill）
        static bool _inside;
        static int _frame = -1;
        static PlayerController _pc;

        /// <summary>玩家此刻在屋里吗。</summary>
        public static bool Inside => _inside;

        /// <summary>这块室内区的范围（世界坐标，轴对齐——房子本来就是轴对齐的）。</summary>
        Bounds _bounds;

        public static IndoorZone Create(Vector3 center, Vector3 size, string name = "IndoorZone")
        {
            var go = new GameObject(name);
            go.transform.position = center;
            var z = go.AddComponent<IndoorZone>();
            z._bounds = new Bounds(center, size);
            return z;
        }

        void OnEnable()
        {
            if (!_all.Contains(this)) _all.Add(this);
        }

        void OnDisable()
        {
            _all.Remove(this);
            // 区域被销毁/关掉的那一刻就重算：这正是上一版"计数减不回去"的地方
            Evaluate();
        }

        void Update()
        {
            // 一帧只算一次（多块区域共享同一次判定）
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            Evaluate();
        }

        /// <summary>
        /// 重新判定"在不在屋里"，并把镜头模式与只走不跑同步过去。
        ///
        /// 【为什么要留跑步豁免口】"屋里只走"是为了不让人在自己客厅里狂奔（也是防晕的
        /// 一环），但健身房的跑步机偏偏**必须跑**：履带把人往后送，走的速度正好等于
        /// 履带速度，于是玩家在自己家的跑步机上永远跑不起来——器材是坏的。
        /// 站上履带即开一个豁免，下来即收回。
        /// </summary>
        static void Evaluate()
        {
            if (_pc == null) _pc = FindFirstObjectByType<PlayerController>();

            bool inside = false;
            if (_pc != null)
            {
                Vector3 p = _pc.transform.position;
                for (int i = _all.Count - 1; i >= 0; i--)
                {
                    var z = _all[i];
                    if (z == null) { _all.RemoveAt(i); continue; }
                    if (z._bounds.Contains(p)) { inside = true; break; }
                }
            }

            bool changed = inside != _inside;
            _inside = inside;
            ThirdPersonCamera.IndoorMode = inside;
            // 只写 IndoorPace，不碰 WalkOnly：WalkOnly 属于身份钉的冲刺锁，
            // 两个功能共用一个字段时，后跑的那个会把前一个的效果抹掉。
            if (_pc != null) _pc.IndoorPace = inside && _runPasses <= 0;

            if (!changed) return;
            if (inside)
                Core.GameEvents.RaiseSubtitle("进屋了——在家里只慢慢走。");
            else
                Core.GameEvents.RaiseSubtitle("出门了——可以放开跑（按住冲刺键）。");
        }

        /// <summary>把"屋里只走不跑"重新算一遍（跑步机上下履带、外部改状态时用）。</summary>
        public static void Refresh(PlayerController pc)
        {
            if (pc != null) _pc = pc;
            Evaluate();
        }

        /// <summary>领取/交回"屋里可以跑"的豁免（跑步机上下履带时调用）。</summary>
        public static void SetRunPass(bool granted)
        {
            _runPasses = Mathf.Max(0, _runPasses + (granted ? 1 : -1));
            Evaluate();
        }

        /// <summary>重建世界/重载场景时清零：静态状态不会自己归位。</summary>
        public static void ResetAll()
        {
            _all.Clear();
            _runPasses = 0;
            _inside = false;
            _frame = -1;
            ThirdPersonCamera.IndoorMode = false;
            SitController.ForceStand();   // 绝不能带着"坐着"的状态离开住处
            _pc = FindFirstObjectByType<PlayerController>();
            if (_pc != null) _pc.IndoorPace = false;
        }
    }
}
