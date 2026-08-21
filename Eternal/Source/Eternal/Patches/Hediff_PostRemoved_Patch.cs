// Relative Path: Eternal/Source/Eternal/Patches/Hediff_PostRemoved_Patch.cs
// Creation Date: 29-12-2025
// Last Edit: 16-07-2026
// Author: 0Shard
// Description: Harmony patch for Hediff.PostRemoved that defers live-health reconciliation.
//              Removal callbacks enqueue only an eligible owning Pawn so direct HediffSet list
//              changes are handled on the next safe tick without retaining a removed Hediff reference.

using System;
using HarmonyLib;
using Verse;
using Eternal.Exceptions;
using Eternal.Utils;

namespace Eternal.Patches
{
    /// <summary>
    /// Defers cleanup and current-state registration until the pawn's mutation has completed.
    /// </summary>
    [HarmonyPatch(typeof(Hediff), nameof(Hediff.PostRemoved))]
    public static class Hediff_PostRemoved_Patch
    {
        [HarmonyPostfix]
        public static void EnqueueLiveHealth(Hediff __instance)
        {
            try
            {
                if (EternalModState.IsDisabled)
                    return;

                Pawn pawn = __instance?.pawn;
                if (pawn == null)
                    return;

                Eternal_Component.Instance?.EnqueueHealthReconciliation(pawn, __instance);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(
                    EternalExceptionCategory.Regrowth,
                    "Hediff_PostRemoved_Patch",
                    __instance?.pawn,
                    ex);
            }
        }
    }
}
