using UnityEngine;

namespace AdversityRoad.OpenWorld
{
    /// <summary>
    /// OpenWorld bridge for the existing Combat.GoalBoard component.
    /// HomeRenovationBuilder lives in AdversityRoad.OpenWorld and intentionally
    /// uses the local GoalBoard name; this bridge keeps that builder decoupled
    /// from namespace imports while reusing the existing goal-board behavior.
    /// </summary>
    public sealed class GoalBoard : AdversityRoad.Combat.GoalBoard
    {
    }
}
