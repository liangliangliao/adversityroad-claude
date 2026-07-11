using System.Collections.Generic;
using UnityEngine;

namespace AdversityRoad.Player
{
    /// <summary>
    /// 手指握拳叠加（武器真实"握住"剑柄的关键）：动作库的通用动画不带握持手型，
    /// 手是张开的——本组件在动画求值后，把右手五指各关节绕【柄轴】卷曲，
    /// 指头环绕剑柄合拢成握姿。卷曲方向按几何自动判定（把指尖转向柄心的一侧），
    /// 跨骨架通用；卸下外装武器时移除本组件即恢复原手型。
    /// </summary>
    public class FingerGrip : MonoBehaviour
    {
        Transform _hand;
        Vector3 _axisHandLocal;     // 柄轴（手骨局部）
        Vector3 _gripHandLocal;     // 柄心（手骨局部）
        readonly List<List<Transform>> _fingers = new List<List<Transform>>();
        List<Transform> _thumb;
        float _sign;
        bool _init;

        // 各关节卷曲角度（近节/中节/远节）；拇指浅握
        static readonly float[] CurlAngles = { 46f, 60f, 48f };
        static readonly float[] ThumbAngles = { 24f, 20f, 0f };

        public void Setup(Transform hand, Vector3 axisHandLocal, Vector3 gripWorld)
        {
            _hand = hand;
            _axisHandLocal = axisHandLocal.normalized;
            _gripHandLocal = hand.InverseTransformPoint(gripWorld);
            _fingers.Clear();
            _thumb = CollectChain(hand, "thumb");
            foreach (var key in new[] { "index", "middle", "ring", "pinky" })
            {
                var chain = CollectChain(hand, key);
                if (chain.Count > 0) _fingers.Add(chain);
            }
            _init = false;   // 卷曲方向下一帧按当前姿态判定
        }

        static List<Transform> CollectChain(Transform hand, string key)
        {
            var list = new List<Transform>();
            foreach (var t in hand.GetComponentsInChildren<Transform>(true))
                if (t != hand && t.name.ToLowerInvariant().Contains(key)) list.Add(t);
            // 按层级深度排序（近节→远节），最多取 3 节
            list.Sort((a, b) => Depth(a).CompareTo(Depth(b)));
            if (list.Count > 3) list.RemoveRange(3, list.Count - 3);
            return list;
        }

        static int Depth(Transform t)
        {
            int d = 0;
            while (t.parent != null) { d++; t = t.parent; }
            return d;
        }

        void LateUpdate()
        {
            if (_hand == null || _fingers.Count == 0) return;
            Vector3 axisW = _hand.TransformDirection(_axisHandLocal).normalized;
            if (axisW.sqrMagnitude < 0.5f) return;
            Vector3 gripW = _hand.TransformPoint(_gripHandLocal);

            // 卷曲方向：试转中指指尖 ±40°，取「转完离柄心更近」的一侧（几何判定，跨骨架通用）
            if (!_init)
            {
                _init = true;
                _sign = 1f;
                var chain = _fingers[_fingers.Count > 1 ? 1 : 0];
                if (chain.Count >= 2)
                {
                    Transform joint = chain[0], tip = chain[chain.Count - 1];
                    Vector3 rel = tip.position - joint.position;
                    Vector3 tipPlus = joint.position + Quaternion.AngleAxis(40f, axisW) * rel;
                    Vector3 tipMinus = joint.position + Quaternion.AngleAxis(-40f, axisW) * rel;
                    _sign = (tipPlus - gripW).sqrMagnitude <= (tipMinus - gripW).sqrMagnitude ? 1f : -1f;
                }
            }

            foreach (var chain in _fingers)
                for (int i = 0; i < chain.Count; i++)
                {
                    var j = chain[i];
                    if (j != null)
                        j.rotation = Quaternion.AngleAxis(_sign * CurlAngles[i], axisW) * j.rotation;
                }
            if (_thumb != null)
                for (int i = 0; i < _thumb.Count; i++)
                {
                    var j = _thumb[i];
                    if (j != null && ThumbAngles[i] > 0f)
                        j.rotation = Quaternion.AngleAxis(_sign * ThumbAngles[i], axisW) * j.rotation;
                }
        }
    }
}
