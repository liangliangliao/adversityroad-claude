using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AdversityRoad.Core
{
    /// <summary>
    /// 画面色彩分级的总开关。
    ///
    /// 【为什么需要它】玩家反复反馈"模型放进游戏，皮肤和衣服的颜色跟原来完全不一样"。
    /// 查到最后，贴图、法线、高光、分辨率、压缩全部都是对的（见 CIDIAG 材质诊断），
    /// 真正改颜色的是**两层叠在一起的色彩分级**：
    ///
    ///   全局（GameBootstrap，priority 10）：对比 +12、饱和 +8、暖色滤镜(1, .98, .94)
    ///   分区（ZoneBuilder，priority 20）：每个区再叠一套滤镜/饱和/对比，个别区还有色相偏移
    ///       独居小屋  滤镜(1.00, 0.92, 0.78) 饱和 −12   ← 玩家最常看角色的地方
    ///       求职荒原  饱和 −28
    ///       车库寒夜  滤镜(0.86, 0.92, 1.08) 饱和 −26
    ///       拖延沼泽  绿滤镜 + 色相 −6
    ///
    /// 一张按中性环境做的皮肤贴图，经过"压掉 22% 蓝 + 再去 12% 饱和"之后，
    /// 不可能还和原图一样。这不是 bug——这是这套 24 区色彩脚本的设计意图。
    ///
    /// 所以这里不删美术设计，只给一个开关：关掉之后画面回到未分级的本色，
    /// 玩家可以自己对比"原始模型"与"氛围渲染"，再决定要哪一个。
    /// 只摘 ColorAdjustments（改颜色的那一层），保留 Bloom / Tonemapping / Vignette
    /// ——那些不改物体固有色，关掉反而会让画面过曝。
    /// </summary>
    public static class PostGrading
    {
        const string Key = "ar_post_grading";
        const string NeutralKey = "ar_neutral_light";

        /// <summary>
        /// 【模型本色对照】把场景照明整体换成中性白环境光：主光关、镜头补光关、
        /// 雾关、分级关。这时角色身上看到的**几乎就是底色贴图本身**
        ///（只再乘一层模型自带的 AO），是能在游戏里做到的、与原图最接近的一次呈现。
        ///
        /// 这不是画面模式，是一次**判定**：
        ///   · 开了之后如果和原图对上了 —— 贴图/材质/着色器全都没问题，
        ///     游戏里那点差别全部来自打光与分区分级，那是美术口径的取舍；
        ///   · 开了之后仍然对不上 —— 那就是资产或着色器这一层还有问题，我继续查。
        /// 一次开关就能把范围砍掉一半，比继续凭截图猜快得多。
        /// </summary>
        public static bool NeutralLight
        {
            get => PlayerPrefs.GetInt(NeutralKey, 0) != 0;
            set
            {
                PlayerPrefs.SetInt(NeutralKey, value ? 1 : 0);
                PlayerPrefs.Save();
                Apply();
            }
        }

        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(Key, 1) != 0;
            set
            {
                PlayerPrefs.SetInt(Key, value ? 1 : 0);
                PlayerPrefs.Save();
                Apply();
            }
        }

        /// <summary>
        /// 把当前开关状态套到场景里所有 Volume 上。
        ///
        /// 分区 Volume 是 ZoneBuilder 在建关时才生成的，所以不能只在启动时调一次；
        /// 切区/重建世界之后要再调一次（GameBootstrap 与 ZoneBuilder 都会调）。
        /// </summary>
        public static void Apply()
        {
            // 本色对照期间分级一律关掉：留着它就不叫"本色"了。
            bool on = Enabled && !NeutralLight;
            foreach (var v in Object.FindObjectsByType<Volume>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var p = v.profile;      // 实例化该 Volume 自己的 profile 副本
                if (p == null) continue;
                if (p.TryGet(out ColorAdjustments ca)) ca.active = on;
            }
        }
    }
}
