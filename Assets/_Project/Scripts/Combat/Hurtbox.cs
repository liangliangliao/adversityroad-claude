using UnityEngine;

namespace AdversityRoad.Combat
{
    /// <summary>受击判定框：把命中转发给所属者（玩家或敌人），并标注**打中了哪个部位**。</summary>
    public class Hurtbox : MonoBehaviour
    {
        public Transform OwnerRoot => transform.root;

        /// <summary>这块受击框代表的身体部位（None=全身兜底框，由高度粗分）。</summary>
        public BodyPart part = BodyPart.None;

        /// <summary>精确度：同一次挥击同时罩到多块受击框时，取最高的那块结算。
        /// 0 = 全身兜底胶囊，1 = 挂在骨骼上的部位框。</summary>
        public int specificity;

        void Awake()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        public void ReceiveHit(DamageInfo dmg)
        {
            // 部位落款：部位框直接报自己；全身兜底框按接触点相对高度粗分
            // （没装部位框的角色也有合理区分，而不是全身一个价）
            if (part != BodyPart.None) dmg.bodyPart = part;
            else if (dmg.bodyPart == BodyPart.None)
            {
                Vector3 root = OwnerRoot != null ? OwnerRoot.position : transform.position;
                Vector3 p = dmg.hasContact ? dmg.contactPoint : transform.position;
                dmg.bodyPart = BodyPartTable.FromHeight(p.y - root.y);
            }

            var enemy = GetComponentInParent<AI.EnemyController>();
            if (enemy != null) { enemy.TakeHit(dmg); return; }

            var player = GetComponentInParent<PlayerCombatController>();
            if (player != null) player.TakeHit(dmg);
        }
    }
}
