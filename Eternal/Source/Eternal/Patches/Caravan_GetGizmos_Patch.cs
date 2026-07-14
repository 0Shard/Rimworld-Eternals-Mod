// Relative Path: Eternal/Source/Eternal/Patches/Caravan_GetGizmos_Patch.cs
// Creation Date: 14-07-2026
// Last Edit: 14-07-2026
// Author: 0Shard
// Description: Harmony postfix on Caravan.GetGizmos adding a "Resurrect Eternal" command for
//              every tracked Eternal corpse the caravan carries (directly or inside a pawn's
//              inventory). A corpse held in caravan inventory has no selectable Thing, so the
//              map-side ResurrectionGizmo (Eternal_Hediff.GetGizmos) is unreachable while
//              traveling — this is the only initiation path for such corpses. Delegates to
//              EternalCorpseHealingProcessor.StartCorpseHealing, the same entry the map gizmo
//              uses (pre-calculated death-time queue, regrowth init, Metabolic Recovery hediff).

using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Models;
using Eternal.Utils;

// Type aliases to resolve namespace shadowing (Eternal.Corpse/Eternal.Caravan shadow game types)
using CorpseType = Verse.Corpse;
using CaravanType = RimWorld.Planet.Caravan;

namespace Eternal.Patches
{
    /// <summary>
    /// Adds a per-corpse "Resurrect Eternal" gizmo to player caravans carrying tracked
    /// Eternal corpses, enabling resurrection to start (and complete) while traveling.
    /// </summary>
    [HarmonyPatch(typeof(CaravanType), nameof(CaravanType.GetGizmos))]
    public static class Caravan_GetGizmos_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(CaravanType __instance, ref IEnumerable<Gizmo> __result)
        {
            try
            {
                if (EternalModState.IsDisabled || __instance == null || !__instance.IsPlayerControlled)
                {
                    return;
                }

                var corpseManager = EternalServiceContainer.Instance?.CorpseManager;
                if (corpseManager == null || corpseManager.TrackedCount == 0)
                {
                    return;
                }

                List<Gizmo> resurrectionGizmos = null;
                foreach (var thing in __instance.AllThings)
                {
                    if (!(thing is CorpseType corpse) || corpse.InnerPawn == null)
                    {
                        continue;
                    }

                    var trackingEntry = corpseManager.GetCorpseData(corpse.InnerPawn);
                    if (trackingEntry == null || trackingEntry.Corpse != corpse)
                    {
                        continue;
                    }

                    if (resurrectionGizmos == null)
                    {
                        resurrectionGizmos = new List<Gizmo>();
                    }
                    resurrectionGizmos.Add(CreateResurrectionCommand(trackingEntry));
                }

                if (resurrectionGizmos != null)
                {
                    __result = __result.Concat(resurrectionGizmos);
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "Caravan_GetGizmos.Postfix", null, ex);
            }
        }

        /// <summary>
        /// Builds the resurrection command for one tracked corpse. Mirrors the map-side
        /// ResurrectionGizmo semantics: disabled with "in progress" while healing is active.
        /// </summary>
        private static Gizmo CreateResurrectionCommand(CorpseTrackingEntry trackingEntry)
        {
            string pawnName = trackingEntry.OriginalPawn?.Name?.ToStringShort ?? "Unknown";

            var command = new Command_Action
            {
                defaultLabel = trackingEntry.IsHealingActive
                    ? $"Resurrecting {pawnName}..."
                    : $"Resurrect {pawnName}",
                defaultDesc = $"Begin the Eternal resurrection process for {pawnName}'s corpse " +
                    "carried by this caravan. Healing continues while the caravan travels; " +
                    "nutrition debt accumulates and is repaid after resurrection.",
                icon = ContentFinder<Texture2D>.Get("UI/Gizmos/Eternal_Resurrection", false),
                Order = 1000f,
                action = () => StartCaravanResurrection(trackingEntry, pawnName),
            };

            if (trackingEntry.IsHealingActive)
            {
                command.Disable("Resurrection already in progress");
            }

            return command;
        }

        private static void StartCaravanResurrection(CorpseTrackingEntry trackingEntry, string pawnName)
        {
            try
            {
                var healingProcessor = EternalServiceContainer.Instance?.CorpseHealingProcessor;
                if (healingProcessor == null)
                {
                    Log.Error("[Eternal] Caravan resurrection: CorpseHealingProcessor not available");
                    return;
                }

                if (healingProcessor.StartCorpseHealing(trackingEntry))
                {
                    Messages.Message($"{pawnName} has begun Eternal resurrection. " +
                        "Healing continues while the caravan travels.",
                        MessageTypeDefOf.PositiveEvent);
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "StartCaravanResurrection", trackingEntry?.OriginalPawn, ex);
            }
        }
    }
}
