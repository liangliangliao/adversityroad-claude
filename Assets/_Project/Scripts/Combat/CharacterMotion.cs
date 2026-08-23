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
        /// 按半径的一半切分后，任何一步都不可能跨过一堵墙。步数封顶 8，
        /// 免得某次异常的巨大位移把这一帧拖垮。
        /// </summary>
        public static void StepMove(CharacterController cc, Vector3 delta)
        {
            if (cc == null) return;
            float max = Mathf.Max(0.05f, cc.radius * 0.5f);
            float len = delta.magnitude;
            if (len <= max) { cc.Move(delta); return; }
            int steps = Mathf.Min(8, Mathf.CeilToInt(len / max));
            Vector3 part = delta / steps;
            for (int i = 0; i < steps; i++) cc.Move(part);
        }
    }
}
