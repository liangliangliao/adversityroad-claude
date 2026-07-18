using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 目标板（独居小屋物件）：走近自动蓄力恢复心理属性；
    /// 并联动目标板系统——靠近时提示今日目标状态（未钉/进行中/已完成）。
    /// </summary>
    public class GoalBoard : MonoBehaviour
    {
        public float interactRange = 2.5f;
        public float restorePerSec = 20f;

        PlayerController _player;
        float _remindAt = -99f;

        void Update()
        {
            if (_player == null)
            {
                _player = FindObjectOfType<PlayerController>();
                if (_player == null) return;
            }
            if (Vector3.Distance(transform.position, _player.transform.position) > interactRange) return;
            // 走近即自动蓄力恢复（无需按键）
            _player.Stats.RestoreMental(restorePerSec * Time.deltaTime);

            // 目标板联动：提示今日目标（30 秒最多提醒一次，不刷屏）
            if (Time.time - _remindAt > 30f)
            {
                _remindAt = Time.time;
                GameEvents.RaiseSubtitle(GoalSystem.DoneToday
                    ? "目标板：今日目标已完成——「" + GoalSystem.CurrentGoal + "」。"
                    : GoalSystem.PinnedToday
                        ? "目标板：今日目标进行中——「" + GoalSystem.CurrentGoal + "」（安全屋「目标」面板打卡）。"
                        : "目标板空着——打开安全屋「目标」面板，钉下今天只做的一件事。");
            }
        }
    }
}
