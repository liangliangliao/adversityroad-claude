using System.Reflection;
using UnityEngine;

namespace AdversityRoad.OpenWorld
{
    /// <summary>
    /// Emergency startup guard for the first residential renovation integration.
    /// The renovation builder previously self-started in every scene via
    /// RuntimeInitializeOnLoadMethod. On mobile this can interfere with the
    /// title/home bootstrap before the open-world player exists.
    ///
    /// This guard prevents that global auto-start. The renovation will be
    /// re-enabled later through an explicit OpenWorldBuilder call, so the
    /// normal title flow is never touched by residential generation.
    /// </summary>
    internal static class HomeRenovationStartupGuard
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void DisableGlobalAutoStart()
        {
            try
            {
                var field = typeof(HomeRenovationBuilder).GetField(
                    "_started",
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (field != null)
                    field.SetValue(null, true);
            }
            catch
            {
                // Never allow the guard itself to affect startup.
            }
        }
    }
}
