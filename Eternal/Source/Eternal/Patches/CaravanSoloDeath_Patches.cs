// Author: 0Shard
// Description: Capture Eternal assignments before native Kill and persist last-owner caravan corpses.
using System;
using Eternal.Caravan;
using Eternal.Exceptions;
using Eternal.DI;
using Eternal.Extensions;
using Eternal.Models;
using Eternal.Utils;
using HarmonyLib;
using RimWorld.Planet;
using Verse;
using CaravanType = RimWorld.Planet.Caravan;

namespace Eternal.Patches
{
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill), new[] { typeof(DamageInfo?), typeof(Hediff) })]
    internal static class Pawn_Kill_SoloCaravan_Patch
    {
        internal sealed class DeathCapture
        {
            internal EternalCaravanDeathHandler.SoloDeathRecovery SoloDeath;
            internal PawnAssignmentSnapshot Assignments;
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        internal static bool Prefix(Pawn __instance, out DeathCapture __state)
        {
            __state = null;
            if (EternalModState.IsDisabled)
            {
                return true;
            }

            var handler = EternalCaravanDeathHandler.Instance;
            if (handler != null)
            {
                if (!handler.BeginSoloDeath(__instance, out var soloDeath))
                {
                    return false;
                }
                if (soloDeath != null)
                {
                    __state = new DeathCapture { SoloDeath = soloDeath, Assignments = soloDeath.Assignments };
                    return true;
                }
            }

            if (__instance == null || __instance.Dead
                || !__instance.HasTraitIgnoringSuppression(EternalDefOf.Eternal_GeneticMarker))
            {
                return true;
            }
            try
            {
                // Native Kill clears workSettings before its primary death notification.
                // Keep this call's snapshot in Harmony state, shared with the solo path.
                __state = new DeathCapture { Assignments = PawnAssignmentSnapshot.CaptureFrom(__instance) };
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Snapshot,
                    "Pawn.Kill.PreDeathAssignments", __instance, ex);
            }
            return true;
        }

        [HarmonyFinalizer]
        internal static Exception Finalizer(Pawn __instance, DeathCapture __state, Exception __exception)
        {
            try
            {
                if (__state?.SoloDeath != null)
                {
                    __state.SoloDeath.Handler.EndSoloDeath(__state.SoloDeath);
                }
                else if (__state?.Assignments != null && __instance?.Dead == true)
                {
                    var corpse = __instance.Corpse;
                    var entry = EternalServiceContainer.Instance?.CorpseManager?.GetCorpseData(__instance);
                    if (corpse != null && !corpse.Destroyed && entry != null
                        && ReferenceEquals(entry.OriginalPawn, __instance) && ReferenceEquals(entry.Corpse, corpse))
                    {
                        // Preserve the primary/fallback registration and its queue; only replace
                        // assignment data captured after native component removal.
                        entry.AssignmentSnapshot = __state.Assignments;
                    }
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "Pawn.Kill.CompleteDeathCapture", __instance, ex);
                return __exception ?? ex;
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(WorldObject), nameof(WorldObject.Destroy))]
    internal static class WorldObject_Destroy_SoloCaravan_Patch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        internal static bool Prefix(WorldObject __instance)
            => EternalModState.IsDisabled || __instance is not CaravanType
                || EternalCaravanDeathHandler.Instance?.DeferSoloCaravanRemoval(__instance) != true;
    }

    [HarmonyPatch(typeof(CaravanType), nameof(CaravanType.AddPawnOrItem), new[] { typeof(Thing), typeof(bool) })]
    internal static class Caravan_AddPawnOrItem_SoloDeath_Patch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        internal static bool Prefix(CaravanType __instance, Thing __0)
            => EternalModState.IsDisabled
                || EternalCaravanDeathHandler.Instance?.StageSoloCorpse(__instance, __0) != true;
    }
}
