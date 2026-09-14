using UnityEngine;

namespace AdversityRoad.InternalOS
{
    /// <summary>
    /// 第 9-26 章关卡的**唯一落位规则**。
    ///
    /// 【为什么必须只有一份】
    /// 玩家连着两轮说"任务点密集、杂乱无章、堆在一块"。我上一轮把三套东西
    /// 各自"铺开"了一点，但根因不在铺得够不够开，在于**同一条主轴上有三套
    /// 互不知情的落位代码**：
    ///
    ///   · InternalProps.Build   把关键物摆在 t = 0.45 / 0.60 / 0.75 / 0.80；
    ///   · Level0901BlankPage    把六张修改卡摆在 t = 0.52 … 0.82；
    ///   · Level0902ProofRoom    把八张修改项摆在 t = 0.40 … 0.75。
    ///
    /// 三套区间彼此重叠，于是无论每一套内部排得多整齐，合到场上就是一堆东西
    /// 挤在主轴中段那十几米里——这正是截图上的样子。
    ///
    /// 所以规则收到这一个文件里：主轴被切成**互不重叠的带**，每一类东西只许
    /// 落在自己那一带。落位不再是三处巧合，而是一条可以讲给玩家听的规则：
    ///
    ///   0.00        入口       —— 你从这儿进来
    ///   0.10-0.30   战斗区     —— 空地，一件陈设都不摆，敌人开场在这儿
    ///   0.30        起手位     —— 这一关**第一件要动手的东西**（9-1 的工作台就在这儿）
    ///   0.40-0.64   资料带     —— 要读的东西分两排立在过道两侧，你从中间走过
    ///   0.70-0.82   工作带     —— 其余要动手的关键物（完成标准锁 / 检查点 / 目标箱）
    ///   0.90        交付点     —— Execution Gate，这一关的事在这儿算完成
    ///   1.00        出口
    ///
    /// 顺序就是玩法顺序：**先打出空间，先动手做出那一件，再读、再处理，最后交付、出门。**
    ///
    /// 【为什么"起手位"要排在资料带前面】
    /// 这批关卡的共同结构是"先有东西，再谈它够不够好"：9-1 是先有第一版才谈改哪几处，
    /// 9-3 是先把目标箱拿上手才谈送哪一箱。把第一件关键物排在要读的东西后面，
    /// 玩家会先站在一排看不懂的卡片前面——他还没有那样东西，读了也无从判断。
    ///
    /// 走一遍这条轴，这一关该做的事正好按顺序做完一遍——
    /// 位置本身就是流程说明，不需要再多立一块牌子。
    /// </summary>
    public static class InternalLayout
    {
        // 各带的首尾（沿"落点→出口"主轴的比例）
        public const float CombatT = 0.22f;
        public const float OpeningT = 0.30f;
        public const float ReadFrom = 0.40f, ReadTo = 0.64f;
        public const float WorkFrom = 0.70f, WorkTo = 0.82f;
        public const float GateT = 0.90f;

        // ===== 每条带的名字 =====
        //
        // 【为什么名字要写在这儿，而不是写在建牌子那一行】
        // CI 有一道门禁（CIDiagnostics「目标行/玩法说明指向核对」）：玩法说明里
        // 每一个【X】，场上都必须真有一块写着 X 的牌子，否则玩家会满场找一个
        // 不存在的名字。这道门禁原来只认关键物的牌面，不认区域牌——
        // 于是说明里写"去【起手位】"就被判成指向不存在的东西。
        //
        // 名字散在两处迟早会对不上，所以收在这里一份：
        // SiteBuilder 拿它刻牌子，CIDiagnostics 拿它核说明，两边永远一致。
        public const string ZoneCombat  = "战斗区";
        public const string ZoneOpening = "起手位";
        public const string ZoneRead    = "资料带";
        public const string ZoneWork    = "工作带";
        public const string ZoneGate    = "交付点";

        /// <summary>场上会立出来的区域牌名（玩法说明里点名它们是合法的）。</summary>
        public static readonly string[] ZoneNames =
        {
            ZoneCombat, ZoneOpening, ZoneRead, ZoneWork, ZoneGate
        };

        /// <summary>资料带里两排之间的半宽：过道净宽 15 米，打起来也转得开。</summary>
        public const float AisleHalf = 7.5f;
        /// <summary>工作带里左右错开的距离。关键物少，错开小一点就够认。</summary>
        public const float WorkOffset = 4.5f;

        /// <summary>
        /// 资料带里的第 index 件（共 count 件）：**分两排夹一条过道**。
        ///
        /// 偶数在左排、奇数在右排，同一排里由近及远。玩家从过道中间走过去，
        /// 左右各一列，一件一件读得到，而不是站在一堆卡片中间分不清先后。
        /// </summary>
        public static Vector3 Aisle(Vector3 spawn, Vector3 exit, int index, int count)
        {
            int rows = Mathf.Max(1, (count + 1) / 2);
            int row = index / 2;
            float f = rows <= 1 ? 0.5f : (float)row / (rows - 1);
            float t = Mathf.Lerp(ReadFrom, ReadTo, f);
            float side = (index % 2 == 0) ? -1f : 1f;
            return Snap(Vector3.Lerp(spawn, exit, t) + Right(spawn, exit) * (side * AisleHalf));
        }

        /// <summary>起手位：这一关第一件要动手的关键物，在战斗区之后、资料带之前。</summary>
        public static Vector3 Opening(Vector3 spawn, Vector3 exit)
            => Snap(Vector3.Lerp(spawn, exit, OpeningT));

        /// <summary>工作带里的第 index 件（共 count 件）：沿轴依次排开，左右小幅错开。</summary>
        public static Vector3 Station(Vector3 spawn, Vector3 exit, int index, int count)
        {
            float f = count <= 1 ? 0.5f : (float)index / (count - 1);
            float t = Mathf.Lerp(WorkFrom, WorkTo, f);
            float side = (index % 2 == 0) ? -1f : 1f;
            return Snap(Vector3.Lerp(spawn, exit, t) + Right(spawn, exit) * (side * WorkOffset));
        }

        /// <summary>交付点：主轴上，出口门之前。</summary>
        public static Vector3 Gate(Vector3 spawn, Vector3 exit)
            => Snap(Vector3.Lerp(spawn, exit, GateT));

        /// <summary>战斗区中心：开场敌人和玩家都在这一段。</summary>
        public static Vector3 Combat(Vector3 spawn, Vector3 exit)
            => Vector3.Lerp(spawn, exit, CombatT);

        /// <summary>主轴的右手方向（水平）。</summary>
        public static Vector3 Right(Vector3 spawn, Vector3 exit)
        {
            Vector3 fwd = exit - spawn;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            return Vector3.Cross(Vector3.up, fwd.normalized);
        }

        /// <summary>贴到可走的地面上；贴不到就按原样返回（别把东西吞掉）。</summary>
        public static Vector3 Snap(Vector3 p)
        {
            if (UnityEngine.AI.NavMesh.SamplePosition(p, out var hit, 12f,
                    UnityEngine.AI.NavMesh.AllAreas)) return hit.position;
            return p;
        }
    }
}
