using System;
using System.Collections.Generic;
using UnityEngine;

namespace AdversityRoad.Shame
{
    /// <summary>
    /// 指控注册表（方案 8.13.1 ClaimRegistry）：truthTag 标记、usedClaims 去重、复用权废除。
    ///
    /// 【为什么指控要注册而不是现编】
    /// 本章的逆袭判定第一条就是「指控复用失效」：同一条指控在本章后段再次出现时，
    /// 不再触发身份钉——因为玩家已经在第一次遭遇里完成了认领。
    /// 这条判定只有在指控是**有身份的对象**时才可能成立，所以它必须被登记下来。
    ///
    /// 【台词只由行为标签与场景标签生成】
    /// 注册表里存的是行为标签（"那笔钱我拖了很久没还"），不存任何玩家在游戏外输入的
    /// 经历原文，也不存身份判词（遵 8.7.1、8.12.3）。
    /// </summary>
    public static class ClaimRegistry
    {
        /// <summary>
        /// 本章的指控素材：只写**行为**，一条都不许写成"你是一种什么人"。
        /// 前半为真（认领不终审的正解），后半为假（事实之刃的正解）。
        /// </summary>
        static readonly string[] TrueClaimTags =
        {
            "那笔钱确实是我借的，到现在没有还清",
            "我确实说了下周给答复，然后一直没有给",
            "我确实把那件事瞒下来了，没有主动说",
            "我确实拿了不属于我的那二十元",
            "我确实在应该到场的那天没有到场",
            "我确实把责任推给了当时不在场的人",
        };

        static readonly string[] FalseClaimTags =
        {
            "你从来没有还过任何人的钱",
            "你每一次答应都是骗人的",
            "这件事从头到尾都是你一个人做的",
            "你身边所有人都在背后议论你",
        };

        /// <summary>为真的指控占比：本章的主战场是"指控成立"，假指控是用来考验判断的少数派。</summary>
        const float TrueRatio = 0.72f;

        static int _seq;

        public static List<ClaimData> All => ShameLine.Data.claims;

        /// <summary>
        /// 取一条可用的指控给敌人发招用。
        ///
        /// 已被认领的指控（used=true）**不再返回**——这就是"复用权废除"：
        /// 认过一次的事，本章里没有任何敌人能再拿它挂钉。
        /// 全部用尽时返回 null，调用方应改用非指认招式（这正是玩家想要的结果）。
        /// </summary>
        public static ClaimData Draw(string sourceEnemyId, bool? forceTruth = null)
        {
            var d = ShameLine.Data;

            // 先看有没有已登记但还没被认领的：同一条指控可以反复出现，直到被认领为止
            var pending = new List<ClaimData>();
            foreach (var c in d.claims)
                if (!c.used && (forceTruth == null || c.truthTag == forceTruth.Value))
                    pending.Add(c);
            if (pending.Count > 0)
                return pending[UnityEngine.Random.Range(0, pending.Count)];

            bool truth = forceTruth ?? (UnityEngine.Random.value < TrueRatio);
            var pool = truth ? TrueClaimTags : FalseClaimTags;

            // 同一条标签不重复登记（登记表就是"这一章一共有哪几件事"）
            var unusedTags = new List<string>();
            foreach (var tag in pool)
            {
                bool exists = false;
                foreach (var known in d.claims) if (known.claimTag == tag) { exists = true; break; }
                if (!exists) unusedTags.Add(tag);
            }
            if (unusedTags.Count == 0) return null;

            var claim = new ClaimData
            {
                claimId = "claim_" + (++_seq) + "_" + DateTime.UtcNow.Ticks,
                claimTag = unusedTags[UnityEngine.Random.Range(0, unusedTags.Count)],
                truthTag = truth,
                sourceEnemyId = sourceEnemyId,
                firstSeenAt = DateTime.UtcNow.ToString("o"),
            };
            d.claims.Add(claim);
            ShameLine.Persist();
            return claim;
        }

        /// <summary>玩家已经认领过这条指控吗——敌人据此跳过它（逆袭判定「指控复用失效」）。</summary>
        public static bool IsSpent(ClaimData claim) => claim == null || claim.used;

        /// <summary>认领成立：废除该条指控在本章内的复用权。</summary>
        public static void MarkOwned(ClaimData claim)
        {
            if (claim == null || claim.used) return;
            claim.used = true;
            claim.counteredBy = "认领不终审";
            ShameLine.Data.ownCount++;
            ShameLine.Persist();
        }

        /// <summary>虚假指控被事实之刃击穿：同样废除复用权，但记的是另一套语法。</summary>
        public static void MarkRefuted(ClaimData claim)
        {
            if (claim == null || claim.used) return;
            claim.used = true;
            claim.counteredBy = "事实之刃";
            ShameLine.Persist();
        }

        /// <summary>本章已废除复用权的指控数 / 总登记数——复盘页与逆袭判定读它。</summary>
        public static int SpentCount()
        {
            int n = 0;
            foreach (var c in ShameLine.Data.claims) if (c.used) n++;
            return n;
        }
    }
}
