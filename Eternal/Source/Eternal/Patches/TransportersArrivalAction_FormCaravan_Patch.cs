// Relative Path: Eternal/Source/Eternal/Patches/TransportersArrivalAction_FormCaravan_Patch.cs
// Creation Date: 14-07-2026
// Last Edit: 14-07-2026
// Author: 0Shard
// Description: Harmony postfix on TransportersArrivalAction_FormCaravan.Arrived. Vanilla only
//              caravans Pawn things at pod arrival; a tracked Eternal corpse goes through
//              CaravanInventoryUtility.GiveThing into a pawn's inventory where a dead pawn is
//              invisible (no colonist bar, no caravan row, no gizmo). This postfix finds tracked
//              corpses that landed in the freshly formed caravan, persists the caravan reference
//              on the tracking entry (SAFE-04 CaravanId pattern, same state as
//              EternalCaravanDeathHandler deaths), and fires a letter naming the carrier so the
//              corpse is never silently "lost". Resurrection is then started from the caravan's
//              gizmo (Caravan_GetGizmos_Patch) and completes while traveling.

using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Utils;

// Type aliases to resolve namespace shadowing (Eternal.Corpse/Eternal.Caravan shadow game types)
using CorpseType = Verse.Corpse;
using CaravanType = RimWorld.Planet.Caravan;

namespace Eternal.Patches
{
    /// <summary>
    /// Surfaces tracked Eternal corpses that arrive by transport pod and land inside a caravan
    /// pawn's inventory: sets CorpseTrackingEntry.CaravanId and fires a letter naming the carrier.
    /// </summary>
    [HarmonyPatch(typeof(TransportersArrivalAction_FormCaravan), nameof(TransportersArrivalAction_FormCaravan.Arrived))]
    public static class TransportersArrivalAction_FormCaravan_Patch
    {
        /// <summary>
        /// After vanilla forms the caravan and sweeps pod contents into pawn inventories,
        /// re-links every tracked unspawned corpse to the caravan now holding it.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                if (EternalModState.IsDisabled)
                {
                    return;
                }

                var corpseManager = EternalServiceContainer.Instance?.CorpseManager;
                if (corpseManager == null || corpseManager.TrackedCount == 0)
                {
                    return;
                }

                foreach (var trackingEntry in corpseManager.GetAllCorpses())
                {
                    var corpse = trackingEntry?.Corpse;
                    if (corpse == null || corpse.Destroyed || corpse.Spawned)
                    {
                        continue;
                    }

                    CaravanType containingCaravan = null;
                    foreach (var caravan in Find.WorldObjects.Caravans)
                    {
                        if (caravan.AllThings.Contains(corpse))
                        {
                            containingCaravan = caravan;
                            break;
                        }
                    }

                    // Already linked to this caravan (e.g. registered by EternalCaravanDeathHandler
                    // at death) — nothing new to announce.
                    if (containingCaravan == null || trackingEntry.CaravanId == containingCaravan.ID)
                    {
                        continue;
                    }

                    // SAFE-04: persist the caravan reference so FindCaravanContainingCorpse's
                    // fast path and save/load resolution work exactly like caravan-death corpses.
                    trackingEntry.CaravanId = containingCaravan.ID;

                    var carrier = CaravanInventoryUtility.GetOwnerOf(containingCaravan, corpse);
                    string carrierName = carrier?.Name?.ToStringShort ?? containingCaravan.Label;

                    Log.Message($"[Eternal] Tracked corpse of {trackingEntry.OriginalPawn?.Name} arrived by transporter " +
                        $"into caravan {containingCaravan.Label} (carried by {carrierName})");

                    Find.LetterStack?.ReceiveLetter(
                        "EternalCorpseAboardCaravan".Translate(),
                        "EternalCorpseAboardCaravanDesc".Translate(
                            trackingEntry.OriginalPawn?.Name?.ToStringFull ?? "Unknown",
                            carrierName,
                            containingCaravan.Label),
                        LetterDefOf.NeutralEvent,
                        containingCaravan);
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "TransportersArrivalAction_FormCaravan.Postfix", null, ex);
            }
        }
    }
}
