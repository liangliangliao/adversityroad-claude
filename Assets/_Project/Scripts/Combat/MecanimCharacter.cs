using UnityEngine;

namespace AdversityRoad.Combat
{
    /// <summary>
    /// 动捕角色装配：若 Resources 里存在 Mixamo 人形模型预制体，则实例化它并让
    /// HumanoidAnimator 进入 Playables 动捕模式（挂兵器到右手骨骼）；否则返回 false，
    /// 上层继续用程序化方块骨骼。这样在动捕资源到位前构建始终可用（自动回退）。
    ///
    /// 资源契约（详见 MIXAMO_SETUP.md）：
    ///   Resources/Characters/PlayerModel  （玩家人形，Humanoid Rig + Animator，Avatar 已指定）
    ///   Resources/Characters/EnemyModel   （敌人人形，可与玩家相同）
    ///   Resources/Characters/Anims/<PoseState名 及 Idle/Walk/Run/CombatIdle>
    /// </summary>
    public static class MecanimCharacter
    {
        /// <summary>该项目是否配置了动捕资源（有任一模型预制体即认为启用）。</summary>
        public static bool Available =>
            Resources.Load<GameObject>("Characters/PlayerModel") != null ||
            Resources.Load<GameObject>("Characters/EnemyModel") != null;

        /// <summary>
        /// 尝试用动捕模型装配。成功则模型已挂在 visualRoot 下、poser 进入动捕模式并返回 true。
        /// </summary>
        public static bool TryBuild(Transform visualRoot, HumanoidAnimator poser, bool isPlayer,
            Material baseMaterial, WeaponKind weapon)
        {
            if (visualRoot == null || poser == null) return false;

            var prefab = Resources.Load<GameObject>("Characters/" + (isPlayer ? "PlayerModel" : "EnemyModel"))
                      ?? Resources.Load<GameObject>("Characters/PlayerModel");
            if (prefab == null) return false;

            var model = Object.Instantiate(prefab, visualRoot, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            var animator = model.GetComponentInChildren<Animator>();
            if (animator == null || animator.avatar == null || !animator.isHuman)
            {
                Object.Destroy(model);
                return false;
            }
            animator.applyRootMotion = false;   // 位移由 CharacterController/NavMesh 负责

            if (!poser.TryEnableMecanim(animator))
            {
                Object.Destroy(model);
                return false;
            }

            // 兵器挂到右手骨骼（Mixamo 角色不自带兵器）
            var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (hand != null && weapon != WeaponKind.None)
            {
                var wr = WeaponFactory.Build(weapon, hand, baseMaterial,
                    new Vector3(0.02f, 0f, 0.04f), new Vector3(0f, 0f, 0f));
                if (wr != null)
                {
                    poser.weaponPivot = wr.pivot;
                    poser.weaponTrail = wr.trail;
                }
            }
            return true;
        }
    }
}
