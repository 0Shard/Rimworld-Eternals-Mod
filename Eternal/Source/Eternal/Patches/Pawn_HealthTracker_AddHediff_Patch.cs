// Relative Path: Eternal/Source/Eternal/Patches/Pawn_HealthTracker_AddHediff_Patch.cs
// Creation Date: 29-12-2025
// Last Edit: 16-07-2026
// Author: 0Shard
// Description: Harmony patch for Pawn_HealthTracker.AddHediff that defers live-health
//              reconciliation. The callback records only an eligible owning Pawn in the
//              component's deduplicating queue; it never creates thresholds, reads a retained Hediff, or
//              applies healing while RimWorld is still completing the mutation.

using System;
using HarmonyLib;
using Verse;
using Eternal.Exceptions;
using Eternal.Utils;

namespace Eternal.Patches
{
    /// <summary>
    /// Defers health reconciliation until the next safe orchestrator tick after AddHediff.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.AddHediff))]
    [HarmonyPatch(new Type[] { typeof(Hediff), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) })]
    public static class Pawn_HealthTracker_AddHediff_Patch
    {
        [HarmonyPostfix]
        public static void EnqueueLiveHealth(Hediff hediff)
        {
            try
            {
                if (EternalModState.IsDisabled)
                    return;

                Pawn pawn = hediff?.pawn;
                if (pawn == null)
                    return;

                Eternal_Component.Instance?.EnqueueHealthReconciliation(pawn);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(
                    EternalExceptionCategory.Resurrection,
                    "Pawn_HealthTracker_AddHediff_Patch",
                    hediff?.pawn,
                    ex);
            }
        }
    }
}
