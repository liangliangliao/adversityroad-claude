using UnityEngine;

namespace AdversityRoad.Combat
{
    /// <summary>角色位移的公共工具（玩家/敌人/技能位移共用）。</summary>
    public static class CharacterMotion
    {
        /// <summary>
        /// 分步位移：把一次大的 Move 拆成若干小步。
        ///
        /// CharacterController.Move 是**扫掠**检测，但一次调用只扫一条直线段；
        /// 位移超过胶囊半径时，薄墙可能整个落在两次采样之间——也就是穿墙。
        ///
        /// 单帧位移会大到什么程度：Time.deltaTime 在掉帧时可以到 0.1 秒甚至更多
        ///（Unity 默认 maximumDeltaTime 是 0.333s），配上冲刺速度 5.2m/s，
        /// 一次 Move 就是 0.5~1.7 米——远超胶囊半径，任何一堵普通厚度的墙都挡不住。
        /// 这不是"极端情况"：手机上一次 GC 或加载就够了，而且**掉帧越厉害越容易穿**，
        /// 正好与"卡顿时穿墙"的现象对上。
        ///
        /// 按半径的一半切分后，任何一步都不可能跨过一堵墙。
        ///
        /// 【上一版这里写错了，而且错在最需要它的那一档】
        ///     int steps = Mathf.Min(8, CeilToInt(len / max));
        ///     Vector3 part = delta / steps;
        /// 步数封顶 8，每一步却是**总位移 ÷ 步数**——于是位移一旦超过 8×max，
        /// 每一步就重新超过 max，分步等于没做。而且位移越大、每步越长：
        /// 玩家半径 0.4 ⇒ max=0.2 ⇒ 封顶在 1.6m 处开始失效，
        /// 3.2m 的位移每步 0.4m、6.4m 的位移每步 0.8m……
        /// **正是"卡顿时/突进时穿墙"那一档，切分被自己的封顶架空了。**
        ///
        /// 封顶的本意是"别让某次异常的巨大位移把这一帧拖垮"，那就该
        /// **截断总位移**，而不是放大每一步：这一帧少走一点，远比穿过一堵墙好，
        /// 而且下一帧会接着走完。步数上限同时提到 16（16×0.2=3.2m，
        /// 覆盖 0.6 秒的长帧配冲刺速度），十六次胶囊扫掠的开销可以忽略。
        /// </summary>
        const int MaxSteps = 16;

        public static void StepMove(CharacterController cc, Vector3 delta)
        {
            if (cc == null) return;
            float max = Mathf.Max(0.05f, cc.radius * 0.5f);
            float len = delta.magnitude;
            if (len <= max) { cc.Move(delta); return; }
            int steps = Mathf.CeilToInt(len / max);
            if (steps > MaxSteps)
            {
                // 截断到 MaxSteps 步能覆盖的距离——每一步仍然 ≤ max
                delta *= MaxSteps * max / len;
                steps = MaxSteps;
            }
            Vector3 part = delta / steps;
            for (int i = 0; i < steps; i++) cc.Move(part);
        }

        // ===== 嵌墙兜底：分步扫掠防不住的那一类 =====
        //
        // 【为什么还需要它】分步位移解决的是"一帧跨过一堵墙"。但玩家报的是
        // **原地画圈时穿墙**——那一档单帧位移只有几厘米，分步根本没被触发。
        // 实机日志佐证：单帧位移最大值这一版一次都没超阈值，而"推着杆贴墙"
        // 的时长从 6.0 秒涨到 24.0 秒（最长 3.35 秒）。
        //
        // 贴着墙持续推、同时身体高速转向，是 CharacterController 最容易出问题的
        // 工况：每帧 Move 都在做解穿插，方向又一直在变，在墙角/接缝处解算方向
        // 可能指向墙的另一侧，几帧下来人就到外面去了。追这个触发条件试了很多轮
        // 都没收敛，所以这里不再猜，直接补一张网：
        //   ① 每帧检查胶囊有没有和**静态环境**重叠，重叠就按最小平移量推出去；
        //   ② 推不动、且嵌得比半径还深，判定为**已经在墙里了**，回滚到上一个
        //      确认没重叠的位置。
        //
        // 【这张网自己不能变成新的漂移源】玩家这一路报的正是"人被瞬移"，所以
        // 三条硬约束：
        //   * 只认静态环境。敌人身上挂的是普通 CapsuleCollider（没有 Rigidbody，
        //     靠 NavMeshAgent 走），按"非运动学刚体"过滤根本滤不掉它们——那样
        //     每次贴身战斗都会被推开，甚至因为"嵌得深"触发回滚，等于我亲手造出
        //     一次瞬移。凡是父链上带 NavMeshAgent / CharacterController 的，一律跳过。
        //   * 推出量按帧封顶。一次推半米以上，在玩家眼里和穿墙没有区别。
        //   * 回滚距离封顶。safePos 正常只落后一帧（几厘米）；一旦因为连续回滚
        //     而拉开很远，回滚本身就比它要修的 bug 更像瞬移，这时宁可不回滚。
        //
        // 每帧一次 OverlapCapsule + 几次 ComputePenetration，只给玩家用，开销可忽略。

        const float MaxPushPerFrame = 0.35f;   // 单帧最大推出量（米）
        const float MaxRollbackDist = 2f;      // 回滚距离上限（米），超了就不回滚

        static readonly Collider[] _overlap = new Collider[12];

        /// <summary>诊断：本帧解穿插推出的距离（米）。</summary>
        public static float DbgDepenetrate { get; private set; }
        /// <summary>诊断：本帧最深的横向嵌入量（米）。</summary>
        public static float DbgDeepest { get; private set; }
        /// <summary>诊断：累计回滚次数（判定为"已经在墙里"）。</summary>
        public static int DbgRollbacks { get; private set; }

        static bool IsEnvironment(Collider col, Transform self)
        {
            if (col == null) return false;
            var ct = col.transform;
            if (ct == self || ct.IsChildOf(self)) return false;
            // 会自己动的东西一律不参与解穿插：推它们只会互相顶，
            // 真正要防的是静态环境。
            var rb = col.attachedRigidbody;
            if (rb != null && !rb.isKinematic) return false;
            if (col.GetComponentInParent<CharacterController>() != null) return false;
            if (col.GetComponentInParent<UnityEngine.AI.NavMeshAgent>() != null) return false;
            return true;
        }

        /// <summary>
        /// 把角色从静态环境里推出来；嵌得太深则回滚到 safePos。
        /// 返回本帧确认安全的位置，调用方应把它存下来作为下一帧的 safePos。
        /// </summary>
        public static Vector3 ResolvePenetration(CharacterController cc, Vector3 safePos,
                                                 bool hasSafe)
        {
            DbgDepenetrate = 0f;
            DbgDeepest = 0f;
            if (cc == null || !cc.enabled) return cc != null ? cc.transform.position : safePos;
            var t = cc.transform;
            float r = Mathf.Max(0.05f, cc.radius);
            float half = Mathf.Max(r, cc.height * 0.5f) - r;
            Vector3 c = t.TransformPoint(cc.center);
            Vector3 axis = t.up * half;
            int n = Physics.OverlapCapsuleNonAlloc(c - axis, c + axis, r, _overlap,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            Vector3 push = Vector3.zero;
            float deepest = 0f;
            for (int i = 0; i < n; i++)
            {
                var col = _overlap[i];
                if (!IsEnvironment(col, t)) continue;
                if (!Physics.ComputePenetration(cc, t.position, t.rotation,
                                                col, col.transform.position, col.transform.rotation,
                                                out Vector3 dir, out float dist))
                    continue;
                // 只取水平分量，竖直方向交给重力/台阶，免得把人顶上天。
                // 注意这里是**投影**（dir.x/dir.z 直接乘 dist），不是把水平分量
                // 归一化后再乘 dist：法线越接近竖直（斜坡、台阶边缘），横向该推的
                // 就越少；归一化会把一次几乎纯竖直的微小穿插放大成整段横向推力，
                // 人会莫名其妙被斜坡"甩"出去。
                Vector3 h = new Vector3(dir.x, 0f, dir.z) * dist;
                float hm = h.magnitude;
                if (hm < 1e-4f) continue;
                push += h;
                if (hm > deepest) deepest = hm;
            }

            DbgDeepest = deepest;

            float pm = push.magnitude;
            if (pm > 1e-4f)
            {
                if (pm > MaxPushPerFrame) push *= MaxPushPerFrame / pm;
                DbgDepenetrate = push.magnitude;
                cc.Move(push);                      // 走 Move，接地判定才不会乱
            }

            // 推完还嵌得比半径深 = 已经进到墙体内部了，扫掠救不回来，只能回滚。
            // 但回滚超过 MaxRollbackDist 就是另一种瞬移，那时宁可留在原地。
            if (hasSafe && deepest > r)
            {
                Vector3 back = safePos - t.position;
                back.y = 0f;
                if (back.sqrMagnitude <= MaxRollbackDist * MaxRollbackDist)
                {
                    DbgRollbacks++;
                    bool was = cc.enabled;
                    cc.enabled = false;
                    t.position = safePos;
                    cc.enabled = was;
                    return safePos;
                }
            }
            return t.position;
        }
    }
}
