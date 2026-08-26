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
    }
}
