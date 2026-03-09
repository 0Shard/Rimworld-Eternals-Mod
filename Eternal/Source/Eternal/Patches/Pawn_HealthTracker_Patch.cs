// Relative Path: Eternal/Source/Eternal/Patches/Pawn_HealthTracker_Patch.cs
// Creation Date: 28-10-2025
// Last Edit: 04-03-2026
// Author: 0Shard
// Description: Harmony patch for Pawn death mechanics for Eternal resurrection.
//              Patches Pawn.Kill to intercept death events for Eternal pawns.
//              This is now the FALLBACK registration path - Notify_PawnDied is PRIMARY.
//              Captures PawnAssignmentSnapshot at death for work priority/policy preservation.
//              Creates map anchor to retain temporary maps when Eternal dies.
//              Note: Resurrection gizmo is now handled via Eternal_Hediff.GetGizmos() with showGizmosOnCorpse.
//              RC5-FIX: Changed pawn.Name.Named("PAWN") to pawn.Named("PAWN") so GrammarResolverSimple
//                       receives the Pawn object and generates PAWN_nameFull, PAWN_pronoun, etc. correctly.

using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Eternal;
using Eternal.Corpse;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Extensions;
using Eternal.Map;
using Eternal.Models;
using Eternal.Utils;

namespace Eternal.Patches
{
    /// <summary>
    /// Harmony patch for Pawn.Kill to handle Eternal death events.
    /// This is the correct patch target - Pawn.Kill is called when a pawn dies.
    /// Registers corpse in the EternalCorpseManager for potential resurrection.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Pawn_Kill_Patch
    {
        /// <summary>
        /// Postfix patch that runs after a pawn is killed.
        /// Handles Eternal-specific death processing including corpse registration,
        /// map anchoring, and player notification.
        /// </summary>
        /// <param name="__instance">The pawn that was killed</param>
        /// <param name="dinfo">Optional damage info that caused the death</param>
        /// <param name="exactCulprit">Optional hediff that caused the death</param>
        [HarmonyPostfix]
        public static void HandleEternalDeath(Pawn __instance, DamageInfo? dinfo, Hediff exactCulprit)
        {
            try
            {
                // SAFE-09: skip all processing when mod is disabled due to missing critical defs.
                if (EternalModState.IsDisabled)
                    return;

                // Validate input parameters
                if (__instance == null)
                {
                    Log.Warning("[Eternal] HandleEternalDeath called with null pawn");
                    return;
                }

                // Ensure pawn is actually dead (Kill may have been prevented by other mods)
                if (!__instance.Dead)
                {
                    return;
                }

                // Check if pawn has Eternal trait (including suppressed traits)
                if (!__instance.HasTraitIgnoringSuppression(EternalDefOf.Eternal_GeneticMarker))
                {
                    return;
                }

                // Log death details for debugging
                if (Eternal_Mod.settings?.debugMode == true)
                {
                    string deathCause = dinfo.HasValue ? dinfo.Value.Def?.defName ?? "Unknown" : "Natural causes";
                    Log.Message($"[Eternal] Eternal pawn {__instance.Name} has died. Cause: {deathCause}");
                }

                // Capture assignment snapshot BEFORE registering corpse
                // Pawn is still accessible with all data at this point
                var assignmentSnapshot = PawnAssignmentSnapshot.CaptureFrom(__instance);

                // Register the corpse for potential resurrection with the snapshot
                RegisterEternalCorpse(__instance, assignmentSnapshot);

                // Get the map from the corpse since pawn.Map may be null after death
                var corpseMap = __instance.Corpse?.Map;
                if (corpseMap != null)
                {
                    // Create anchor to retain temporary map if applicable
                    var mapManager = corpseMap.GetComponent<EternalMapManager>();
                    if (mapManager != null)
                    {
                        mapManager.CreateAnchor(__instance);
                    }
                    else
                    {
                        // MapComponent not registered - try dynamic creation
                        // This handles the case where XML registration is missing
                        TryCreateMapManager(corpseMap, __instance);
                    }
                }

                // Send death notification to player
                Find.LetterStack.ReceiveLetter(
                    "EternalPawnDied".Translate(),
                    "EternalPawnDiedDesc".Translate(__instance.Named("PAWN")),
                    LetterDefOf.NegativeEvent,
                    __instance);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "HandleEternalDeath", __instance, ex);
            }
        }

        /// <summary>
        /// Registers an Eternal pawn's corpse in the corpse management system.
        /// </summary>
        /// <param name="pawn">The pawn that died</param>
        /// <param name="assignmentSnapshot">Captured work priorities, policies, and schedule</param>
        private static void RegisterEternalCorpse(Pawn pawn, PawnAssignmentSnapshot assignmentSnapshot)
        {
            try
            {
                if (pawn == null || pawn.Corpse == null)
                {
                    Log.Warning($"[Eternal] Cannot register corpse - pawn or corpse is null for {pawn?.Name}");
                    return;
                }

                var corpse = pawn.Corpse;
                var corpseManager = EternalServiceContainer.Instance.CorpseManager;

                // CRITICAL: Explicitly check for null CorpseManager - don't use ?. which silently fails
                if (corpseManager == null)
                {
                    Log.Error($"[Eternal] CRITICAL: CorpseManager is null when attempting to register corpse for {pawn.Name}. " +
                              $"Service container may not be initialized. Corpse will NOT be registered for resurrection.");
                    return;
                }

                // FALLBACK: Skip if already registered (Notify_PawnDied should have done this already)
                if (corpseManager.IsTracked(pawn))
                {
                    if (Eternal_Mod.settings?.debugMode == true)
                    {
                        Log.Message($"[Eternal] Corpse for {pawn.Name} already registered (by Notify_PawnDied PRIMARY path)");
                    }
                    return;
                }

                // Register in the corpse manager with assignment snapshot - this is FALLBACK path
                corpseManager.RegisterCorpse(corpse, pawn, assignmentSnapshot);

                // Log as warning - indicates PRIMARY path may have failed (mod conflict, timing issue, etc.)
                Log.Warning($"[Eternal] RegisterEternalCorpse via Harmony FALLBACK for {pawn.Name} - Notify_PawnDied may have failed");
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "RegisterEternalCorpse", pawn, ex);
            }
        }

        /// <summary>
        /// Attempts to dynamically create an EternalMapManager for a map if it doesn't exist.
        /// This is a fallback for when XML registration is missing.
        /// </summary>
        /// <param name="map">The map to add the component to</param>
        /// <param name="pawn">The pawn that died (for anchor creation)</param>
        private static void TryCreateMapManager(Verse.Map map, Pawn pawn)
        {
            try
            {
                if (map == null) return;

                // Check if map is temporary (quest maps, etc.)
                var mapParent = map.Parent;
                if (mapParent == null) return;

                // Only create manager for maps that might need retention
                // Temporary maps are typically not the player's home map
                if (mapParent.def?.defName == null) return;

                // Create and register the component dynamically
                var mapManager = new EternalMapManager(map);
                map.components.Add(mapManager);

                // Now create the anchor
                mapManager.CreateAnchor(pawn);

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Dynamically created EternalMapManager for map {map.uniqueID}");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "TryCreateMapManager", pawn, ex);
            }
        }
    }
}
