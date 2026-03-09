// Relative Path: Eternal/Source/Eternal/Patches/RoofCollapse_Patch.cs
// Creation Date: 20-01-2026
// Last Edit: 21-02-2026
// Author: 0Shard
// Description: FALLBACK Harmony patch and helper for roof collapse protection.
//              Primary protection: IRoofCollapseAlert interface on EternalCorpseComponent (RimWorld's built-in hook).
//              Fallback protection: This Harmony patch catches edge cases where comp might not be attached yet.
//              Both mechanisms ensure corpses survive mountain roof collapses.

using System;
using HarmonyLib;
using Verse;
using RimWorld;
using Eternal;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Extensions;
using Eternal.Corpse;
using Eternal.Utils;

// Type alias to avoid conflict with Eternal.Map namespace
using MapType = Verse.Map;

namespace Eternal.Patches
{
    /// <summary>
    /// FALLBACK Harmony patches for RoofCollapserImmediate to protect Eternal corpses from roof collapse.
    /// The PRIMARY protection is the IRoofCollapseAlert interface in EternalCorpseComponent (RimWorld's built-in hook).
    /// This patch catches edge cases where the EternalCorpseComponent might not be attached yet (race conditions).
    /// </summary>
    [HarmonyPatch(typeof(RoofCollapserImmediate))]
    public static class RoofCollapserImmediate_Patch
    {
        /// <summary>
        /// Prefix patch that teleports Eternal corpses to safety BEFORE roof collapse damage is applied.
        /// Uses Priority.High to run early in the patch chain, ensuring corpses are moved before any damage.
        /// </summary>
        /// <param name="c">Cell where roof is collapsing</param>
        /// <param name="map">Map where collapse is occurring</param>
        [HarmonyPatch("DropRoofInCellPhaseOne")]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)]
        public static void TeleportEternalCorpsesBeforeCollapse(IntVec3 c, MapType map)
        {
            try
            {
                // SAFE-09: skip when mod is disabled.
                if (EternalModState.IsDisabled)
                    return;

                // Check if protection is enabled
                if (!Eternal_Mod.GetSettings().enableRoofCollapseProtection)
                    return;

                // Validate inputs
                if (map == null || !c.InBounds(map))
                    return;

                var corpseManager = EternalServiceContainer.Instance?.CorpseManager;
                if (corpseManager == null)
                    return;

                // Check all things in the collapsing cell for Eternal corpses
                var things = c.GetThingList(map);
                for (int i = things.Count - 1; i >= 0; i--)
                {
                    if (!(things[i] is Verse.Corpse corpse))
                        continue;

                    var pawn = corpse.InnerPawn;
                    if (pawn == null)
                        continue;

                    // Check if Eternal (tracked OR has trait)
                    bool isTracked = corpseManager.IsTracked(pawn);
                    bool isEternal = pawn.IsValidEternalCorpse();

                    if (!isTracked && !isEternal)
                        continue;

                    // Skip if already has comp - IRoofCollapseAlert (primary) will handle it
                    // This avoids duplicate teleportation; we only handle corpses WITHOUT comp (fallback)
                    if (corpse.GetComp<EternalCorpseComponent>() != null)
                        continue;

                    // Find nearest safe cell and teleport
                    IntVec3 safeCell = RoofCollapseHelper.FindNearestSafeCell(c, map, 50);
                    if (!safeCell.IsValid)
                    {
                        safeCell = CellFinder.RandomEdgeCell(map);
                        if (safeCell.Roofed(map))
                        {
                            Log.Warning($"[Eternal] Fallback: No safe cell found for {pawn.Name}, corpse may be destroyed");
                            continue; // No safe cell, skip
                        }
                    }

                    // Teleport corpse to safety
                    corpse.DeSpawn(DestroyMode.WillReplace);
                    GenSpawn.Spawn(corpse, safeCell, map);

                    // Notify player
                    Messages.Message(
                        "EternalRoofCollapseSaved".Translate(pawn.Name?.ToStringShort ?? "Eternal"),
                        new TargetInfo(safeCell, map),
                        MessageTypeDefOf.NeutralEvent);

                    if (Eternal_Mod.settings?.debugMode == true)
                    {
                        Log.Message($"[Eternal] Prefix: Saved {pawn.Name} from roof collapse via Harmony patch");
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error but don't prevent the original method from running
                EternalLogger.HandleException(EternalExceptionCategory.GameStateInvalid,
                    "TeleportEternalCorpsesBeforeCollapse", null, ex);
            }
        }
    }

    /// <summary>
    /// Helper class for roof collapse protection logic.
    /// Shared between the Harmony patch (primary) and EternalCorpseComponent (backup).
    /// </summary>
    public static class RoofCollapseHelper
    {
        /// <summary>
        /// Finds the nearest cell that is safe from roof collapse.
        /// A cell is considered safe if it has no roof OR has a thin roof (not thick/mountain roof).
        /// Thick roofs are dangerous because they can collapse and deal massive damage.
        /// </summary>
        /// <param name="origin">Starting position to search from</param>
        /// <param name="map">Map to search on</param>
        /// <param name="maxSearchRadius">Maximum distance to search (default 50 cells)</param>
        /// <returns>Valid safe cell, or IntVec3.Invalid if none found</returns>
        public static IntVec3 FindNearestSafeCell(IntVec3 origin, MapType map, int maxSearchRadius = 50)
        {
            // Search in expanding rings from the origin
            for (int radius = 1; radius <= maxSearchRadius; radius++)
            {
                // GenRadial.RadialCellsAround gives cells in a ring at the specified radius
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(origin, radius, radius + 1))
                {
                    // Skip if cell is outside map bounds
                    if (!cell.InBounds(map))
                        continue;

                    // Skip if cell is not standable (blocked by wall, water, etc.)
                    if (!cell.Standable(map))
                        continue;

                    // Check the roof at this cell
                    RoofDef roof = map.roofGrid.RoofAt(cell);

                    // Safe if: no roof OR thin roof (not thick/mountain)
                    // Thick roofs (isThickRoof) are mountain roofs that collapse catastrophically
                    if (roof == null || !roof.isThickRoof)
                    {
                        return cell;
                    }
                }
            }

            // No safe cell found within search radius
            return IntVec3.Invalid;
        }
    }
}
