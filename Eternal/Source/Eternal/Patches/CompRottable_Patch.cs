// Relative Path: Eternal/Source/Eternal/Patches/CompRottable_Patch.cs
// Creation Date: 13-01-2026
// Last Edit: 21-02-2026
// Author: 0Shard
// Description: Harmony fallback patch for CompRottable to prevent rot on Eternal corpses.
//              Primary rot blocking is handled by EternalCorpseComponent.CompTick() every tick.
//              This patch serves as a fallback for edge cases (caravan, mod conflicts, etc.).

using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Eternal;
using Eternal.Corpse;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Extensions;
using Eternal.Utils;

namespace Eternal.Patches
{
    /// <summary>
    /// Harmony fallback patch for CompRottable.CompTickRare to skip rot processing
    /// for Eternal corpses. Primary rot blocking is via EternalCorpseComponent.CompTick().
    /// This patch catches edge cases where the component might not tick properly.
    /// </summary>
    [HarmonyPatch(typeof(CompRottable), nameof(CompRottable.CompTickRare))]
    public static class CompRottable_CompTickRare_Patch
    {
        /// <summary>
        /// Prefix patch that skips rot processing for tracked Eternal corpses.
        /// Returns false to skip the original method entirely for Eternal corpses.
        /// </summary>
        /// <param name="__instance">The CompRottable component being processed</param>
        /// <returns>True to run original method, false to skip it</returns>
        [HarmonyPrefix]
        public static bool SkipRotForEternalCorpses(CompRottable __instance)
        {
            try
            {
                // SAFE-09: when mod is disabled, do not interfere with rot processing.
                if (EternalModState.IsDisabled)
                    return true; // run original method

                // Only apply to corpses
                if (!(__instance.parent is Verse.Corpse corpse))
                {
                    return true; // Not a corpse, run original
                }

                // Check if this corpse's pawn is tracked by the Eternal system
                var pawn = corpse.InnerPawn;
                if (pawn == null)
                {
                    return true; // No pawn, run original
                }

                // Get corpse manager from service container
                var corpseManager = EternalServiceContainer.Instance?.CorpseManager;
                if (corpseManager == null)
                {
                    return true; // Service not available, run original
                }

                // If tracked as Eternal corpse, skip rot processing entirely
                if (corpseManager.IsTracked(pawn))
                {
                    // Reset rot progress to ensure it stays at zero
                    // This is redundant with CompTick but provides defense in depth
                    if (__instance.RotProgress > 0f)
                    {
                        __instance.RotProgress = 0f;
                    }

                    return false; // Skip original method - no rot for Eternals
                }

                return true; // Not tracked, run original
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "CompRottable_CompTickRare_Patch", null, ex);
                return true; // On error, run original to avoid breaking vanilla behavior
            }
        }
    }

    /// <summary>
    /// Harmony patch for CompRottable.RotProgress setter to block any rot accumulation
    /// for Eternal corpses. This catches cases where rot is set directly rather than
    /// through CompTickRare.
    /// </summary>
    [HarmonyPatch(typeof(CompRottable), nameof(CompRottable.RotProgress), MethodType.Setter)]
    public static class CompRottable_RotProgress_Patch
    {
        /// <summary>
        /// Prefix patch that blocks setting RotProgress > 0 for Eternal corpses.
        /// Allows setting to 0 (reset) but prevents any rot accumulation.
        /// </summary>
        /// <param name="__instance">The CompRottable component</param>
        /// <param name="value">The value being set</param>
        /// <returns>True to run original setter, false to skip</returns>
        [HarmonyPrefix]
        public static bool BlockRotProgressForEternals(CompRottable __instance, ref float value)
        {
            try
            {
                // SAFE-09: when mod is disabled, do not block rot progress.
                if (EternalModState.IsDisabled)
                    return true; // run original setter

                // Allow setting to zero - that's what we want
                if (value <= 0f)
                {
                    return true;
                }

                // Only apply to corpses
                if (!(__instance.parent is Verse.Corpse corpse))
                {
                    return true;
                }

                var pawn = corpse.InnerPawn;
                if (pawn == null)
                {
                    return true;
                }

                var corpseManager = EternalServiceContainer.Instance?.CorpseManager;
                if (corpseManager == null)
                {
                    return true;
                }

                // If tracked, force value to 0 instead of allowing increase
                if (corpseManager.IsTracked(pawn))
                {
                    value = 0f; // Override to zero
                    return true; // Still run setter, but with our value
                }

                return true;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "CompRottable_RotProgress_Patch", null, ex);
                return true;
            }
        }
    }
}
