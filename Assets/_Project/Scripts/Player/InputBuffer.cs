using System.Collections.Generic;
using UnityEngine;

namespace AdversityRoad.Player
{
    /// <summary>
    /// 通用输入缓冲（大作「意图匹配」的基础设施）：
    /// 玩家在硬直、翻滚、出招锁定等【暂时无法响应】的窗口里按下的键不再被丢弃，
    /// 而是记录时间戳暂存；一旦角色恢复可控，立刻兑现——按下即生效，
    /// 不需要玩家自己去"卡时机重按"。这是"人机合一"最关键的一环：
    /// 真实的意图是「我现在就要闪」，而不是「我要在恰好可闪的那一帧闪」。
    ///
    /// 窗口过期即作废（防止陈旧输入迟到触发，那会读作"角色自己乱动"）。
    /// 全部使用 unscaledTime：顿帧/慢镜期间的输入同样不丢。
    /// </summary>
    public class InputBuffer
    {
        readonly Dictionary<string, float> _pressed = new Dictionary<string, float>();

        /// <summary>记录一次按下。</summary>
        public void Press(string key) => _pressed[key] = Time.unscaledTime;

        /// <summary>窗口内是否有未兑现的按键（不消费——用于先判条件再决定是否兑现）。</summary>
        public bool Has(string key, float window)
            => _pressed.TryGetValue(key, out float t) && Time.unscaledTime - t <= window;

        /// <summary>兑现并清除（动作真正执行时调用）。</summary>
        public void Consume(string key) => _pressed.Remove(key);

        /// <summary>窗口内有则兑现并返回 true。</summary>
        public bool TryConsume(string key, float window)
        {
            if (!Has(key, window)) return false;
            _pressed.Remove(key);
            return true;
        }

        public void Clear() => _pressed.Clear();
    }
}
