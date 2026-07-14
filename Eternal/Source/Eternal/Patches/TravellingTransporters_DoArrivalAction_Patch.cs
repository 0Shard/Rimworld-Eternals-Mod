// Relative Path: Eternal/Source/Eternal/Patches/TravellingTransporters_DoArrivalAction_Patch.cs
// Creation Date: 14-07-2026
// Last Edit: 14-07-2026
// Author: 0Shard
// Description: Harmony prefix on TravellingTransporters.DoArrivalAction blocking the silent
//              vaporize path for tracked Eternal corpses. When a pod group contains ONLY a corpse
//              (no living pawn), AnyPotentialCaravanOwner invalidates every fallback arrival
//              action, Arrived() nulls arrivalAction, and DoArrivalAction's null branch calls
//              innerContainer.ClearAndDestroyContentsOrPassToWorld() — pawns pass to world but a
//              Corpse is Destroy(Vanish)ed with zero log output, losing the Eternal permanently.
//              This prefix runs before that branch, extracts tracked corpses from the transporter
//              containers, and delivers them to a crash site at the destination tile. Vanilla
//              then runs unchanged on the remaining contents.

using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Utils;
using Eternal.World;

// Type alias to resolve namespace shadowing (Eternal.Corpse shadows Verse.Corpse)
using CorpseType = Verse.Corpse;

namespace Eternal.Patches
{
    /// <summary>
    /// Rescues tracked Eternal corpses from the corpse-only-pod vaporize fallback:
    /// when the arrival action is null, transporter contents are destroyed silently.
    /// </summary>
    [HarmonyPatch(typeof(TravellingTransporters), "DoArrivalAction")]
    public static class TravellingTransporters_DoArrivalAction_Patch
    {
        /// <summary>
        /// Arrived() nulls the arrival action BEFORE DoArrivalAction when StillValid fails and
        /// no fallback applies, so a null arrivalAction here is exactly the vaporize path.
        /// </summary>
        [HarmonyPrefix]
        public static void Prefix(TravellingTransporters __instance, List<ActiveTransporterInfo> ___transporters)
        {
            try
            {
                if (EternalModState.IsDisabled || __instance == null || __instance.arrivalAction != null)
                {
                    return;
                }

                var corpseManager = EternalServiceContainer.Instance?.CorpseManager;
                if (corpseManager == null || corpseManager.TrackedCount == 0 || ___transporters == null)
                {
                    return;
                }

                List<CorpseType> trackedCorpses = null;
                foreach (var transporter in ___transporters)
                {
                    var innerContainer = transporter?.innerContainer;
                    if (innerContainer == null)
                    {
                        continue;
                    }

                    foreach (var thing in innerContainer)
                    {
                        if (thing is CorpseType corpse && corpse.InnerPawn != null &&
                            corpseManager.IsTracked(corpse.InnerPawn))
                        {
                            if (trackedCorpses == null)
                            {
                                trackedCorpses = new List<CorpseType>();
                            }
                            trackedCorpses.Add(corpse);
                        }
                    }
                }

                if (trackedCorpses == null)
                {
                    return;
                }

                Log.Warning($"[Eternal] Transporters arrived with no valid arrival action — " +
                    $"{trackedCorpses.Count} tracked Eternal corpse(s) would have been vaporized silently. " +
                    $"Delivering to a crash site at tile {__instance.destinationTile}.");

                // Removes each corpse from its transporter container before the vanilla
                // ClearAndDestroyContentsOrPassToWorld sweep runs on what remains.
                SpaceCrashRescueService.DeliverCorpsesToCrashSite(trackedCorpses, __instance.destinationTile);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "TravellingTransporters_DoArrivalAction.Prefix", null, ex);
            }
        }
    }
}
