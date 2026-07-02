namespace AdversityRoad.Combat
{
    public enum CombatState
    {
        Idle, Locomotion,
        LightAttack, HeavyAttack, ComboWindow,
        Dodge, Parry, Guard,
        HitReaction, Knockdown,
        MentalStagger,      // 心理硬直：心理属性归零触发
        InnerPowerCast,     // 内功释放
        Finisher,           // 绝招
        Death, Recovery
    }
}
