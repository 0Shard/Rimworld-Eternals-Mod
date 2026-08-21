// Relative Path: Eternal/Source/Eternal/Patches/Hediff_MissingPart_PostAdd_Patch.cs
// Creation Date: 16-07-2026
// Last Edit: 16-07-2026
// Author: 0Shard
// Description: Harmony seam for missing-part additions that may bypass the ordinary health
//              tracker path. It enqueues only an eligible owning Pawn for deferred
//              reconciliation and never starts regrowth or heals inline from PostAdd.

using System;
using HarmonyLib;
using Verse;
using Eternal.Exceptions;
using Eternal.Utils;

namespace Eternal.Patches
{
    [HarmonyPatch(typeof(Hediff_MissingPart), nameof(Hediff_MissingPart.PostAdd))]
    public static class Hediff_MissingPart_PostAdd_Patch
    {
        [HarmonyPostfix]
        public static void EnqueueLiveHealth(Hediff_MissingPart __instance)
        {
            try
            {
                if (EternalModState.IsDisabled)
                    return;

                Eternal_Component.Instance?.EnqueueHealthReconciliation(__instance?.pawn);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(
                    EternalExceptionCategory.Regrowth,
                    "Hediff_MissingPart_PostAdd_Patch",
                    __instance?.pawn,
                    ex);
            }
        }
    }
}
