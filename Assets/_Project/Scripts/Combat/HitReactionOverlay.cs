using System.Collections.Generic;
using UnityEngine;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 部位受击反应：按命中点找到最近的身体部位（头/胸/腰腹/左腿/右腿），
    /// 在动画求值之后对该部位骨骼叠加一记短促的「受击冲量」旋转——
    /// 头被击中就向打击方向甩头后仰，胸腹被击中就折身，腿被扫中就屈弯踉跄。
    /// 冲量彼此独立可叠加：连续两次命中头部，就有两次可见的甩头（打几下动几下）。
    /// 同时在接触点弹出部位标签（·头部 / ·躯干 / ·左腿…），一眼看清打中了哪里。
    /// 兼容动捕骨骼（mixamorig）与程序化方块骨骼（HumanoidRig），骨骼名懒扫描。
    /// </summary>
    public class HitReactionOverlay : MonoBehaviour
    {
        struct Impulse
        {
            public Transform bone;
            public Vector3 worldAxis;   // 受击瞬间锁定的世界轴（绕它向打击方向倒去）
            public float amp, t, dur;
        }

        readonly List<Impulse> _list = new List<Impulse>(8);
        Transform _head, _chest, _hips, _legL, _legR;
        bool _scanned;

        /// <summary>在 root 上触发一次部位受击（无该组件会自动补挂）。
        /// hitDir = 打击推动方向（攻击者 → 受击者）。</summary>
        public static void Trigger(Transform root, Vector3 contact, Vector3 hitDir, bool heavy)
        {
            var o = root.GetComponentInChildren<HitReactionOverlay>();
            if (o == null) o = root.gameObject.AddComponent<HitReactionOverlay>();
            o.AddHit(contact, hitDir, heavy);
        }

        public void AddHit(Vector3 contact, Vector3 hitDir, bool heavy)
        {
            if (!_scanned) Scan();
            hitDir.y = 0;
            if (hitDir.sqrMagnitude < 0.01f) hitDir = -transform.forward;
            hitDir.Normalize();

            // 命中点离哪个部位骨骼最近，就算打中了哪个部位
            Transform bone = null; string label = null; float amp = 0f;
            float best = float.MaxValue;
            Consider(_head, "头部", heavy ? 34f : 22f, contact, ref bone, ref label, ref amp, ref best);
            Consider(_chest, "躯干", heavy ? 26f : 15f, contact, ref bone, ref label, ref amp, ref best);
            Consider(_hips, "腰腹", heavy ? 20f : 12f, contact, ref bone, ref label, ref amp, ref best);
            Consider(_legL, "左腿", heavy ? 24f : 15f, contact, ref bone, ref label, ref amp, ref best);
            Consider(_legR, "右腿", heavy ? 24f : 15f, contact, ref bone, ref label, ref amp, ref best);
            if (bone == null) return;

            // 反应轴：绕「与打击方向垂直的水平轴」正转 = 部位向打击方向倒去
            Vector3 axis = Vector3.Cross(Vector3.up, hitDir);
            if (axis.sqrMagnitude < 0.001f) axis = transform.right;
            _list.Add(new Impulse
            {
                bone = bone,
                worldAxis = axis.normalized,
                amp = amp,
                t = 0f,
                dur = heavy ? 0.42f : 0.26f
            });

            // 部位标签贴在接触点旁：连击同一部位就连续弹出，次数所见即所得
            CombatFeedback.HitPartLabel(contact, "·" + label, new Color(1f, 0.55f, 0.35f));
        }

        static void Consider(Transform b, string name, float strength, Vector3 contact,
            ref Transform bone, ref string label, ref float amp, ref float best)
        {
            if (b == null) return;
            float d = (b.position - contact).sqrMagnitude;
            if (d >= best) return;
            best = d; bone = b; label = name; amp = strength;
        }

        void LateUpdate()
        {
            if (_list.Count == 0) return;
            float dt = Time.deltaTime;
            for (int i = _list.Count - 1; i >= 0; i--)
            {
                var imp = _list[i];
                imp.t += dt;
                if (imp.bone == null || imp.t >= imp.dur) { _list.RemoveAt(i); continue; }
                // 包络：前 25% 快速打出（挨了一下的顿挫），后 75% 弹性回位
                float k = imp.t / imp.dur;
                float env = k < 0.25f ? k / 0.25f : 1f - (k - 0.25f) / 0.75f;
                env = Mathf.SmoothStep(0f, 1f, env);
                // 动画（Update 阶段求值）已写好本帧姿态，这里在其上叠加冲量
                Vector3 local = imp.bone.InverseTransformDirection(imp.worldAxis);
                imp.bone.localRotation = imp.bone.localRotation *
                    Quaternion.AngleAxis(imp.amp * env, local);
                _list[i] = imp;
            }
        }

        static string Norm(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        /// <summary>按关键词优先级找骨骼：前面的关键词优先；同词多个候选取名字最短的
        /// （mixamorigHead 优先于 mixamorigHeadTopEnd）。</summary>
        Transform Find(params string[] keys)
        {
            var all = GetComponentsInChildren<Transform>(true);
            foreach (var k in keys)
            {
                Transform best = null;
                int bestLen = int.MaxValue;
                foreach (var t in all)
                {
                    string n = Norm(t.name);
                    if (!n.Contains(k) || n.Length >= bestLen) continue;
                    bestLen = n.Length;
                    best = t;
                }
                if (best != null) return best;
            }
            return null;
        }

        void Scan()
        {
            _scanned = true;
            _head = Find("head");
            _chest = Find("spine2", "spine1", "spine", "torso");
            _hips = Find("hips", "pelvis");
            _legL = Find("leftupleg", "hipl");
            _legR = Find("rightupleg", "hipr");
        }
    }
}
