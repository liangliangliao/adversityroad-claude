using UnityEngine;

namespace AdversityRoad.Combat
{
    /// <summary>这一招靠什么够到对方：手、腿，还是手里的兵器。</summary>
    public enum ReachLimb { Arm, Leg, Weapon }

    /// <summary>
    /// 一个角色的攻击距离，全部从**实际骨架与实际兵器网格**量出来（米）。
    ///
    /// 为什么需要这个：此前每一招的判定框长度是写死的常量（见 PoseHitShape），
    /// 与"手里有没有东西、那东西多长"完全无关。于是空手、或者剑还插在鞘里，
    /// 横斩照样够到身前 1.85 米——那是一把大剑的长度，不是一条手臂的长度。
    /// 玩家的原话："一个人拿一米长的棍子能够到 1~3 米内的东西，手里没棍子显然不该够得着。"
    ///
    /// 量法（与姿势无关，取的是骨段长度而不是"此刻手在哪"）：
    ///     手 = 上臂 + 前臂 + 掌（肩点另计前向偏移）
    ///     腿 = 大腿 + 小腿 + 脚掌
    ///     刃 = 握位（手骨）到兵器网格最远角点的距离；兵器不在手上时为 0
    /// </summary>
    public struct ReachProfile
    {
        public bool valid;
        /// <summary>刃长这一项是不是**确知**的。
        /// 玩家侧永远确知（IsWeaponDrawn 明确告诉我们手里有没有东西）；
        /// 敌人侧如果在模型里认不出兵器节点，那就是"不知道"，不是"没有兵器"——
        /// 这两者必须分开：把"认不出"当成"空手"，会把一个拿着长刀的敌人
        /// 悄悄裁成够不到人的样子，而它看上去明明握着刀。
        /// 不确知时兵器系招式一律不裁，保持原设计。</summary>
        public bool bladeKnown;
        public float shoulderZ;   // 肩点相对角色根的前向偏移（含身体厚度那一截）
        public float hipZ;        // 髋点相对角色根的前向偏移
        public float arm;         // 肩→掌
        public float leg;         // 髋→脚尖
        public float blade;       // 握位→刃尖（空手/未出鞘 = 0）

        /// <summary>出招踏前与身体前倾的余量：判定框不是贴着身体不动的，
        /// 落刀那一瞬人已经压上去半步。给的是余量，不是"再送一截距离"。</summary>
        public const float StepPad = 0.25f;

        public float ArmReach => shoulderZ + arm;
        public float LegReach => hipZ + leg;
        public float WeaponReach => ArmReach + blade;

        /// <summary>这一招最远能够到多远（判定框前沿的上限）。</summary>
        public float LimitOf(ReachLimb limb)
        {
            switch (limb)
            {
                case ReachLimb.Leg: return LegReach + StepPad;
                case ReachLimb.Weapon:
                    // 认不出兵器 ⇒ 不裁（返回一个招式表不可能超过的上限）
                    return bladeKnown ? WeaponReach + StepPad : 99f;
                default: return ArmReach + StepPad;
            }
        }

        public override string ToString()
        {
            return "肩前" + shoulderZ.ToString("0.00") + " 臂" + arm.ToString("0.00")
                 + " 髋前" + hipZ.ToString("0.00") + " 腿" + leg.ToString("0.00")
                 + " 刃" + (bladeKnown ? blade.ToString("0.00") : "未知")
                 + " → 拳" + (ArmReach + StepPad).ToString("0.00")
                 + " 腿" + (LegReach + StepPad).ToString("0.00")
                 + " 兵器" + (bladeKnown ? (WeaponReach + StepPad).ToString("0.00") : "不裁");
        }
    }

    /// <summary>把骨架与兵器量成 <see cref="ReachProfile"/>。</summary>
    public static class ReachModel
    {
        /// <summary>
        /// 量一个角色。<paramref name="root"/> 是角色根（判定框 center 的参考系），
        /// <paramref name="model"/> 是骨架所在的模型根，<paramref name="weaponInHand"/>
        /// 是此刻真正握在手里的兵器（没有就传 null——空手就是空手）。
        /// 量不到骨骼时返回 valid=false：调用方一律按"不限制"处理，
        /// 宁可保持旧行为，也不要因为量错而让谁突然够不着。
        /// </summary>
        public static ReachProfile Measure(Transform root, Transform model, Transform weaponInHand,
                                           bool bladeKnown = true)
        {
            var p = new ReachProfile();
            if (root == null || model == null) return p;

            Transform shoulder = MecanimCharacter.FindBone(model, "rightshoulder");
            Transform upperArm = MecanimCharacter.FindBone(model, "rightarm");
            Transform foreArm = MecanimCharacter.FindBone(model, "rightforearm");
            Transform hand = MecanimCharacter.FindBone(model, "righthand");
            Transform upLeg = MecanimCharacter.FindBone(model, "rightupleg")
                              ?? MecanimCharacter.FindBone(model, "rightthigh");
            Transform lowLeg = MecanimCharacter.FindBone(model, "rightleg");
            Transform foot = MecanimCharacter.FindBone(model, "rightfoot");
            Transform toe = MecanimCharacter.FindBone(model, "righttoebase")
                            ?? MecanimCharacter.FindBone(model, "righttoe");

            if (upperArm == null || foreArm == null || hand == null) return p;
            if (upLeg == null || lowLeg == null || foot == null) return p;

            // 骨段长度与当前姿势无关——这正是不能直接量"肩到手的直线距离"的原因：
            // 待机时手垂在身侧，那条直线比伸直的手臂短一大截。
            p.arm = Vector3.Distance(upperArm.position, foreArm.position)
                  + Vector3.Distance(foreArm.position, hand.position)
                  + HandSpan(hand);
            p.leg = Vector3.Distance(upLeg.position, lowLeg.position)
                  + Vector3.Distance(lowLeg.position, foot.position)
                  + (toe != null ? Vector3.Distance(foot.position, toe.position) : 0f);

            Transform sh = shoulder != null ? shoulder : upperArm;
            p.shoulderZ = Mathf.Max(0f, root.InverseTransformPoint(sh.position).z);
            p.hipZ = Mathf.Max(0f, root.InverseTransformPoint(upLeg.position).z);
            p.blade = BladeLength(hand, weaponInHand);
            // 说"确知"的前提是：要么明确知道手里没东西，要么真的量到了刃长。
            // 认出了兵器节点却量出 0（例如那个节点下面根本没有 Renderer），
            // 同样算不确知——否则一个拿刀的敌人会被当成空手。
            p.bladeKnown = bladeKnown && (weaponInHand == null || p.blade > 0.15f);
            p.valid = p.arm > 0.05f && p.leg > 0.05f;
            return p;
        }

        /// <summary>掌心到拳面/握位的那一小截：手骨到中指骨，量不到就按上臂的 22% 估。</summary>
        static float HandSpan(Transform hand)
        {
            if (hand == null) return 0f;
            float best = 0f;
            for (int i = 0; i < hand.childCount; i++)
            {
                var c = hand.GetChild(i);
                float d = Vector3.Distance(hand.position, c.position);
                if (d > best) best = d;
            }
            return best > 0.001f ? best : 0.08f;
        }

        /// <summary>
        /// 刃长 = 握位到兵器网格最远角点。取网格包围盒的角点而不是"兵器根到子物体"，
        /// 是因为剑身往往就是一个没有子节点的网格——按层级量出来会是 0。
        /// </summary>
        public static float BladeLength(Transform hand, Transform weapon)
        {
            if (hand == null || weapon == null) return 0f;
            float best = 0f;
            var rends = weapon.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++)
            {
                var r = rends[i];
                if (r == null || !r.enabled) continue;   // 放在兵器架上的剑不算在手里
                var b = r.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3(
                        (c & 1) == 0 ? b.min.x : b.max.x,
                        (c & 2) == 0 ? b.min.y : b.max.y,
                        (c & 4) == 0 ? b.min.z : b.max.z);
                    float d = Vector3.Distance(hand.position, corner);
                    if (d > best) best = d;
                }
            }
            return best;
        }

        /// <summary>这一招用的是手、腿还是兵器。表里没有的按手算（最保守的那一档）。</summary>
        public static ReachLimb LimbOf(PoseState p)
        {
            switch (p)
            {
                case PoseState.AttackKick:
                case PoseState.SideKick:
                case PoseState.SpinKick:
                case PoseState.JumpKick:
                case PoseState.Sweep:
                    return ReachLimb.Leg;
                case PoseState.PunchJab:
                case PoseState.PunchCross:
                    return ReachLimb.Arm;
                case PoseState.Attack:
                case PoseState.AttackUp:
                case PoseState.SwordThrust:
                case PoseState.AttackSpin:
                case PoseState.HeavyAttack:
                case PoseState.AttackLeap:
                case PoseState.JumpAttack:
                    return ReachLimb.Weapon;
                default:
                    return ReachLimb.Arm;
            }
        }
    }
}
