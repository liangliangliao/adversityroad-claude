using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.World;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 经典关卡（主线 24 关）的顶部常驻目标行。
    ///
    /// 【为什么现在才需要它】以前所有关卡只有一个通关方式——打死关底心魔——
    /// 那件事玩家不用被提醒，敌人自己会来找他。改成"外部心魔可以不战而过"之后
    /// 情况变了：**通关条件从"场上那个人"变成了"对面那扇门"**，而门在几十米外，
    /// 不说就没人知道。进场那一句字幕会滚走，任务面板要点开才看得见，
    /// 只有这一行是随时抬头就在的。
    ///
    /// 【为什么要避让】HUD 顶部这一行全场只有一条，另外两处也在写：
    /// AI 生成场景的 SiteObjective、第八章的 ShameLineController。
    /// 它们各自管着自己的关卡，所以这里的规矩是"人在它们的地盘上就闭嘴"——
    /// 两条目标行来回打架比没有目标行更糟。
    /// </summary>
    public class ChapterObjectiveHud : MonoBehaviour
    {
        float _next;
        bool _writing;

        public static void Ensure()
        {
            if (FindFirstObjectByType<ChapterObjectiveHud>() != null) return;
            var go = new GameObject("ChapterObjectiveHud");
            DontDestroyOnLoad(go);
            go.AddComponent<ChapterObjectiveHud>();
        }

        void OnDisable()
        {
            if (_writing) HUDController.SetObjective("");
        }

        void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.4f;

            if (!ShouldWrite())
            {
                // 只清自己写过的那一行：别人正在写的时候把它抹掉就是抢屏
                if (_writing) { HUDController.SetObjective(""); _writing = false; }
                return;
            }

            var story = StoryManager.Instance;
            var ch = story.Current;
            var rule = LevelRules.Of(ch);
            var player = ActorRegistry.Player;

            string line = "◆ " + ch.title + " —— ";
            if (rule == LevelClearRule.Escape)
            {
                // 目标是那扇门，不是那个人：报方向与距离，并把"够不够算穿过来了"如实写出来。
                // 玩家可以照旧去打，所以后半句仍然留着那条路。
                line += "走到出口即通关（不必应战）";
                var exit = ExitDoorOf(ch.zoneIndex);
                if (player != null && exit != null)
                    line += Where(player, exit.transform.position);
                if (player != null &&
                    !LevelTraverse.Crossed(ch.zoneIndex, player.transform.position, out float walked))
                    line += " · 已离入口 " + Mathf.RoundToInt(walked) + "/" +
                            Mathf.RoundToInt(LevelTraverse.MinDistance) + "m";
                line += " · 或打倒关底心魔";
            }
            else line += "打倒关底心魔（它跟着你走，换个地方没有用）";

            HUDController.SetObjective(line);
            _writing = true;
        }

        /// <summary>此刻该不该由我来写这一行。</summary>
        static bool ShouldWrite()
        {
            var story = StoryManager.Instance;
            if (story == null || story.AllCleared || story.Current == null) return false;
            // 人在 AI 生成场景里 / 第八章两关里：那两处有自己的目标行
            if (OpenWorld.SiteGate.InsideSite) return false;
            if (Shame.ShameLine.InChapter) return false;
            // 只在"当前这一关的那个区"里显示：在城里逛街时不该顶着一行关卡目标
            return ZoneBuilder.IndexOfZone(ZoneBuilder.CurrentZoneId) == story.Current.zoneIndex;
        }

        // 门只找一次：全世界四十多扇门，每 0.4 秒全场扫一遍纯属浪费。
        // 门是 ZoneBuilder 建世界时一次性建好的，位置不会变，缓存住即可。
        Portal _exitDoor;
        int _exitDoorZone = -1;

        /// <summary>这个区里那扇"走出去即通关"的向前门（找不到返回 null）。</summary>
        Portal ExitDoorOf(int zoneIndex)
        {
            if (_exitDoorZone == zoneIndex && _exitDoor != null) return _exitDoor;
            _exitDoorZone = zoneIndex;
            _exitDoor = null;
            foreach (var p in FindObjectsByType<Portal>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (p != null && p.homeZone == zoneIndex &&
                    p.role == PortalRole.Forward && p.explicitZone < 0) { _exitDoor = p; break; }
            return _exitDoor;
        }

        /// <summary>「↗ 32m」：把一个世界坐标变成相对镜头的方位与距离。</summary>
        static string Where(Player.PlayerController player, Vector3 target)
        {
            Vector3 to = target - player.transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.01f) return "";
            var cam = player.cameraTransform;
            Vector3 fwd = cam != null ? cam.forward : player.transform.forward;
            fwd.y = 0f;
            float ang = Vector3.SignedAngle(fwd.normalized, to.normalized, Vector3.up);
            return " · " + Arrow(ang) + Mathf.RoundToInt(to.magnitude) + "m";
        }

        static string Arrow(float angle)
        {
            float a = Mathf.Repeat(angle + 180f, 360f) - 180f;
            if (a > -22.5f && a <= 22.5f) return "↑ ";
            if (a > 22.5f && a <= 67.5f) return "↗ ";
            if (a > 67.5f && a <= 112.5f) return "→ ";
            if (a > 112.5f && a <= 157.5f) return "↘ ";
            if (a > -67.5f && a <= -22.5f) return "↖ ";
            if (a > -112.5f && a <= -67.5f) return "← ";
            if (a > -157.5f && a <= -112.5f) return "↙ ";
            return "↓ ";
        }
    }
}
