using System;
using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.OpenWorld
{
    /// <summary>Player Home 里承载菜单功能的房间物件（方案 17.3）。</summary>
    public enum HomeFixtureKind
    {
        GoalBoard,   // 墙上目标板 → Goal Board / 旅程图
        Desk,        // 书桌 → 复盘与行动计划
        Wardrobe,    // 衣柜/武器架 → 装备
        Mirror,      // 镜子 → 角色与自尊/旧我模块
        Computer,    // 电脑 → AI 个性化与目标图谱
        Phone,       // 手机 → 现实行动打卡 / 消息
        Fridge,      // 冰箱 → 补给与恢复
        Tv,          // 客厅墙上的超大屏电视 → 频道 / 播放 / 后台 / 悬浮小窗
    }

    /// <summary>
    /// 家中的物理 UI（方案 17.3）：
    /// 安全屋的菜单功能尽量通过房间物件承载——墙上目标板对应 Goal Board，
    /// 书桌对应复盘与行动计划，衣柜对应装备，镜子对应角色与旧我，
    /// 电脑对应 AI 个性化和目标图谱，门负责回到开放世界。
    ///
    /// 走近显示提示，按 E（手机端「用」键区域为交互距离自动触发确认）打开对应面板。
    /// </summary>
    public class HomeFixture : MonoBehaviour
    {
        /// <summary>由 GameBootstrap 注入：把物件映射到已建好的 UI 面板。</summary>
        public static Action<HomeFixtureKind> Open;

        public HomeFixtureKind kind = HomeFixtureKind.Desk;
        public float range = 3.2f;

        PlayerController _player;
        float _lastHint = -99f;
        bool _inRange;

        // 书桌/电脑/手机彼此只隔半米——同一次按键必须只开一个面板。
        // Update 里各自报名，LateUpdate 里只有最近的那个真正响应。
        static HomeFixture _candidate;
        static float _candidateDist;
        static int _candidateFrame = -1;

        public static HomeFixture Attach(GameObject go, HomeFixtureKind kind)
        {
            var f = go.AddComponent<HomeFixture>();
            f.kind = kind;
            return f;
        }

        public static string Label(HomeFixtureKind k)
        {
            switch (k)
            {
                case HomeFixtureKind.GoalBoard: return "目标板";
                case HomeFixtureKind.Desk: return "书桌";
                case HomeFixtureKind.Wardrobe: return "衣柜/武器架";
                case HomeFixtureKind.Mirror: return "镜子";
                case HomeFixtureKind.Computer: return "电脑";
                case HomeFixtureKind.Phone: return "手机";
                case HomeFixtureKind.Tv: return "电视";
                default: return "冰箱";
            }
        }

        static string Hint(HomeFixtureKind k)
        {
            switch (k)
            {
                case HomeFixtureKind.GoalBoard: return "查看目标、里程碑与旅程图";
                case HomeFixtureKind.Desk: return "复盘四栏与行动计划";
                case HomeFixtureKind.Wardrobe: return "更换装备与套装";
                case HomeFixtureKind.Mirror: return "角色外观 · 自尊与旧我";
                case HomeFixtureKind.Computer: return "AI 个性化 · 弱点/优势图谱";
                case HomeFixtureKind.Phone: return "现实行动打卡与提醒";
                case HomeFixtureKind.Tv: return "开机换台 · 后台播放 · 切悬浮小窗";
                default: return "补给与恢复";
            }
        }

        void Update()
        {
            if (_player == null)
            {
                _player = AdversityRoad.Core.ActorRegistry.Player;
                if (_player == null) return;
            }

            float dist = Vector3.Distance(transform.position, _player.transform.position);
            bool near = dist <= range;
            if (near && !_inRange && Time.time - _lastHint > 6f)
            {
                _lastHint = Time.time;
                GameEvents.RaiseSubtitle("【" + Label(kind) + "】" + Hint(kind) + "——按 E 使用。");
            }
            _inRange = near;
            if (!near) return;

            if (_candidateFrame != Time.frameCount)
            {
                _candidateFrame = Time.frameCount;
                _candidate = this;
                _candidateDist = dist;
            }
            else if (dist < _candidateDist)
            {
                _candidate = this;
                _candidateDist = dist;
            }
        }

        void LateUpdate()
        {
            if (SitController.Busy) return;   // 坐着/躺着时不抢交互键
            if (_candidate != this || _candidateFrame != Time.frameCount) return;
            if (Input.GetKeyDown(KeyCode.E) || Mobile.MobileInput.GetDown("Interact"))
                Open?.Invoke(kind);
        }
    }
}
