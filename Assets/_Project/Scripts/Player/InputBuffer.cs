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
    /// 全部使用 unscaledTime：顿帧/慢镜期间的输入同样不丢。
    ///
    /// ===== 两种意图，两种寿命（大作通用，此前本类只有一种，是丢输入的根因）=====
    /// 缓冲寿命过去按"按下时刻 + 固定窗口"算，而动作锁时长是任意的（技能 1.15~1.75s、
    /// 蓄力气场可达 2.05s），两个量毫无关系——只要动作比窗口长，那次按键必被静默吞掉。
    /// 实测：闪避窗 0.30s 会被翻滚自身(0.42~0.7s)、被每一个技能、被蓄力二连全部吃掉。
    ///
    /// 现在按**按下时角色在不在动作里**区分意图：
    ///   · 锁定中按下 = "我要接下一招"，是明确的排队意图 → 活到该动作结束（上限 QueuedLife）；
    ///   · 自由时按下 = "我现在就要做"，做不成就说明条件不满足 → 短窗口过期作废。
    /// 这样既不再吞输入，也不会让真正陈旧的输入过几秒突然自己蹦出来。
    /// </summary>
    public class InputBuffer
    {
        /// <summary>排队式输入的寿命上限：覆盖最长动作（蓄力气场 2.05s）后仍留余量。
        /// 超过它一律作废——防的是"角色自己乱动"，不是防正常排队。</summary>
        public const float QueuedLife = 2.5f;

        readonly Dictionary<string, float> _pressed = new Dictionary<string, float>();
        readonly Dictionary<string, bool> _queued = new Dictionary<string, bool>();

        /// <summary>记录一次按下。<paramref name="duringAction"/>=按下时角色正处于
        /// 动作锁/翻滚等不可响应状态——此时该输入按"排队"处理，寿命拉长到动作结束。</summary>
        public void Press(string key, bool duringAction = false)
        {
            _pressed[key] = Time.unscaledTime;
            _queued[key] = duringAction;
        }

        /// <summary>窗口内是否有未兑现的按键（不消费——用于先判条件再决定是否兑现）。</summary>
        public bool Has(string key, float window)
        {
            if (!_pressed.TryGetValue(key, out float t)) return false;
            float age = Time.unscaledTime - t;
            bool queued = _queued.TryGetValue(key, out bool q) && q;
            return age <= (queued ? QueuedLife : window);
        }

        /// <summary>兑现并清除（动作真正执行时调用）。</summary>
        public void Consume(string key) { _pressed.Remove(key); _queued.Remove(key); }

        /// <summary>窗口内有则兑现并返回 true。</summary>
        public bool TryConsume(string key, float window)
        {
            if (!Has(key, window)) return false;
            Consume(key);
            return true;
        }

        public void Clear() { _pressed.Clear(); _queued.Clear(); }
    }
}
