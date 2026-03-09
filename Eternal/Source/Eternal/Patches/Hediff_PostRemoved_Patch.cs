// Relative Path: Eternal/Source/Eternal/Patches/Hediff_PostRemoved_Patch.cs
// Creation Date: 29-12-2025
// Last Edit: 21-02-2026
// Author: 0Shard
// Description: Harmony patch for Hediff.PostRemoved to clean up healing threshold data
//              when a hediff is removed from a pawn. This prevents memory leaks from
//              accumulated threshold entries for hediffs that no longer exist.

using System;
using HarmonyLib;
using Verse;
using Eternal;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Utils;

namespace Eternal.Patches
{
    /// <summary>
    /// Harmony patch for Hediff.PostRemoved.
    /// Cleans up healing threshold data when a hediff is removed.
    /// </summary>
    /// <remarks>
    /// This ensures we don't accumulate stale threshold entries in the tracker.
    /// Called whenever any hediff is removed from any pawn.
    /// </remarks>
    [HarmonyPatch(typeof(Hediff), nameof(Hediff.PostRemoved))]
    public static class Hediff_PostRemoved_Patch
    {
        /// <summary>
        /// Postfix patch that cleans up threshold data after a hediff is removed.
        /// </summary>
        /// <param name="__instance">The hediff that was removed</param>
        [HarmonyPostfix]
        public static void CleanupThreshold(Hediff __instance)
        {
            try
            {
                // SAFE-09: skip when mod is disabled.
                if (EternalModState.IsDisabled)
                    return;

                // Validate input
                if (__instance?.pawn == null)
                    return;

                // Get the tracker from DI container
                var tracker = EternalServiceContainer.Instance?.ThresholdTracker;
                if (tracker == null)
                    return;

                // Remove the threshold entry (if it exists)
                tracker.RemoveThreshold(__instance.pawn, __instance);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Regrowth,
                    "Hediff_PostRemoved_Patch", __instance?.pawn, ex);
            }
        }
    }
}
