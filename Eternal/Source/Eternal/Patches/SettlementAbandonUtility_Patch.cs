// Relative Path: Eternal/Source/Eternal/Patches/SettlementAbandonUtility_Patch.cs
// Creation Date: 25-12-2025
// Last Edit: 21-02-2026
// Author: 0Shard
// Description: Harmony patch to prevent colony abandonment when Eternal pawns (dead or alive) are present.
//              Shows a blocking dialog explaining why abandonment is not allowed.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Eternal;
using Eternal.Exceptions;
using Eternal.Extensions;
using Eternal.Map;
using Eternal.Utils;

// Type alias to resolve namespace shadowing
using MapType = Verse.Map;

namespace Eternal.Patches
{
    /// <summary>
    /// Harmony patch to block settlement abandonment when Eternal pawns are present on the map.
    /// Intercepts the abandonment UI flow and shows a blocking dialog if conditions aren't met.
    /// </summary>
    [HarmonyPatch(typeof(SettlementAbandonUtility))]
    public static class SettlementAbandonUtility_Patch
    {
        /// <summary>
        /// Prefix patch for TryAbandonViaInterface to block abandonment when Eternals are present.
        /// This is the main UI entry point when the player clicks "Abandon Home".
        /// </summary>
        [HarmonyPatch(nameof(SettlementAbandonUtility.TryAbandonViaInterface))]
        [HarmonyPrefix]
        public static bool TryAbandonViaInterface_Prefix(MapParent settlement)
        {
            try
            {
                // SAFE-09: when mod is disabled, do not block abandonment flow.
                if (EternalModState.IsDisabled)
                    return true; // run original method

                // If no map, allow normal flow
                if (settlement == null || !settlement.HasMap)
                {
                    return true;
                }

                MapType map = settlement.Map;
                if (map == null)
                {
                    return true;
                }

                // Collect all Eternals on this map (dead and alive)
                var eternalsOnMap = GetAllEternalsOnMap(map);

                if (eternalsOnMap.Count == 0)
                {
                    // No Eternals present, allow normal abandonment flow
                    return true;
                }

                // Block abandonment and show dialog
                ShowBlockingDialog(eternalsOnMap);

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Blocked abandonment of {settlement.Label} - {eternalsOnMap.Count} Eternal(s) present");
                }

                return false; // Block the original method
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "TryAbandonViaInterface_Prefix", null, ex);
                // On error, allow original method to run (safe default)
                return true;
            }
        }

        /// <summary>
        /// Gets all Eternal pawns (living and dead) on the specified map.
        /// </summary>
        private static List<EternalPawnInfo> GetAllEternalsOnMap(MapType map)
        {
            var result = new List<EternalPawnInfo>();

            try
            {
                // Check living Eternals
                var allPawns = map.mapPawns?.AllPawnsSpawned;
                if (allPawns != null)
                {
                    foreach (var pawn in allPawns)
                    {
                        if (pawn != null && pawn.IsValidEternal() && pawn.Faction == Faction.OfPlayer)
                        {
                            result.Add(new EternalPawnInfo
                            {
                                Pawn = pawn,
                                IsDead = pawn.Dead
                            });
                        }
                    }
                }

                // Check corpses via corpse manager
                var corpseManager = Eternal_Component.Current?.CorpseManager;
                if (corpseManager != null)
                {
                    var trackedCorpses = corpseManager.GetCorpsesOnMap(map);
                    if (trackedCorpses != null)
                    {
                        foreach (var entry in trackedCorpses)
                        {
                            var pawn = entry?.OriginalPawn;
                            // Avoid duplicates (pawn might already be in result if spawned as corpse)
                            if (pawn != null && !result.Any(e => e.Pawn == pawn))
                            {
                                result.Add(new EternalPawnInfo
                                {
                                    Pawn = pawn,
                                    IsDead = true
                                });
                            }
                        }
                    }
                }

                // Also check for corpses directly on the map that might not be tracked yet
                var corpseThings = map.listerThings?.ThingsInGroup(ThingRequestGroup.Corpse);
                if (corpseThings != null)
                {
                    foreach (var thing in corpseThings)
                    {
                        if (thing is Verse.Corpse corpse && corpse.InnerPawn != null)
                        {
                            var innerPawn = corpse.InnerPawn;
                            if (innerPawn.IsValidEternal() && !result.Any(e => e.Pawn == innerPawn))
                            {
                                result.Add(new EternalPawnInfo
                                {
                                    Pawn = innerPawn,
                                    IsDead = true
                                });
                            }
                        }
                    }
                }

                // Check for active anchors (indicates resurrection in progress)
                var mapManager = map.GetComponent<EternalMapManager>();
                if (mapManager != null)
                {
                    var anchors = mapManager.GetActiveAnchors();
                    if (anchors != null)
                    {
                        foreach (var anchor in anchors)
                        {
                            var pawn = anchor.AnchoredPawn;
                            if (pawn != null && !result.Any(e => e.Pawn == pawn))
                            {
                                result.Add(new EternalPawnInfo
                                {
                                    Pawn = pawn,
                                    IsDead = pawn.Dead,
                                    HasActiveAnchor = true
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "GetAllEternalsOnMap", null, ex);
            }

            return result;
        }

        /// <summary>
        /// Shows a dialog explaining why abandonment is blocked.
        /// </summary>
        private static void ShowBlockingDialog(List<EternalPawnInfo> eternals)
        {
            var message = new StringBuilder();

            // Title/header
            message.AppendLine("Eternal_CannotAbandonDesc".Translate());
            message.AppendLine();

            // List each Eternal
            foreach (var info in eternals)
            {
                string status;
                if (info.HasActiveAnchor)
                {
                    status = "Eternal_PawnStatusResurrecting".Translate();
                }
                else if (info.IsDead)
                {
                    status = "Eternal_PawnStatusDead".Translate();
                }
                else
                {
                    status = "Eternal_PawnStatusAlive".Translate();
                }

                string pawnName = info.Pawn?.Name?.ToStringShort ?? "Unknown";
                message.AppendLine($"  • {pawnName} ({status})");
            }

            message.AppendLine();
            message.AppendLine("Eternal_CannotAbandonSuggestion".Translate());

            // Show the dialog
            Find.WindowStack.Add(new Dialog_MessageBox(
                message.ToString(),
                "OK".Translate(),
                null,
                null,
                null,
                "Eternal_CannotAbandonTitle".Translate(),
                false,
                null,
                null,
                WindowLayer.Dialog));
        }

        /// <summary>
        /// Helper class to track Eternal pawn information.
        /// </summary>
        private class EternalPawnInfo
        {
            public Pawn Pawn { get; set; }
            public bool IsDead { get; set; }
            public bool HasActiveAnchor { get; set; }
        }
    }
}
