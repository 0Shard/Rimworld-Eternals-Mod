// Relative Path: Eternal/Source/Eternal/Patches/VGE/VGE_CrashLanding_Patch.cs
// Creation Date: 25-12-2025
// Last Edit: 13-07-2026
// Author: 0Shard
// Description: Harmony patches for VGE (Vanilla Gravship Expanded) crash landing compatibility.
//              VGE's LandingEnded prefix runs ApplyCrashlanding on EVERY landing: gravship things
//              overlapping a destroyable blocking thing take 25%-MaxHP blunt, and things overlapping
//              an INDESTRUCTIBLE blocker are Destroy(0)'d outright (no corpse). This patch despawns
//              Eternal pawns/corpses on those overlap cells before VGE runs and respawns them after.
//              The postfix uses the Map captured in __state - vanilla LandingEnded() always nulls
//              the controller's map field, so re-reading it would misreport "map destroyed".

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Eternal.Compatibility;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Extensions;
using Eternal.Utils;
using Eternal.World;

// Type aliases to resolve namespace shadowing
using MapType = Verse.Map;
using CorpseType = Verse.Corpse;
using GravshipType = RimWorld.Planet.Gravship;

namespace Eternal.Patches.VGE
{
    /// <summary>
    /// Context data for passing Eternal corpse information between Prefix and Postfix.
    /// </summary>
    public class VGELandingContext
    {
        public List<CorpseType> EternalCorpsesToSave { get; } = new List<CorpseType>();
        public List<Pawn> EternalPawnsToSave { get; } = new List<Pawn>();
        public MapType Map { get; set; }
        public int WorldTile { get; set; } = -1;
        public IntVec3 LandingPosition { get; set; } = IntVec3.Invalid;
        public bool PrefixSucceeded { get; set; }
    }

    /// <summary>
    /// Patches WorldComponent_GravshipController.LandingEnded to protect Eternals from VGE's
    /// crash-landing collision damage. Runs BEFORE VGE's patch (High priority vs their Normal),
    /// despawns Eternals on blocker-overlap cells, and respawns them after landing completes.
    /// </summary>
    [HarmonyPatch]
    public static class VGE_LandingEnded_Patch
    {
        private static Type _gravshipControllerType;
        private static FieldInfo _mapField;
        private static FieldInfo _gravshipField;
        private static FieldInfo _blockingThingsField;
        private static bool _typesInitialized = false;

        /// <summary>
        /// Determines if this patch should be applied. Only patches when VGE is active.
        /// </summary>
        public static bool Prepare()
        {
            if (!SpaceModDetection.VanillaGravshipExpandedActive)
            {
                return false;
            }

            try
            {
                EnsureTypesInitialized();
                return _gravshipControllerType != null;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "VGE_LandingEnded_Patch.Prepare", null, ex);
                return false;
            }
        }

        /// <summary>
        /// Initializes VGE types via reflection.
        /// </summary>
        private static void EnsureTypesInitialized()
        {
            if (_typesInitialized)
            {
                return;
            }

            _typesInitialized = true;

            // WorldComponent_GravshipController is a vanilla RimWorld type from Odyssey DLC
            _gravshipControllerType = typeof(WorldComponent_GravshipController);

            if (_gravshipControllerType != null)
            {
                _mapField = AccessTools.Field(_gravshipControllerType, "map");
                _gravshipField = AccessTools.Field(_gravshipControllerType, "gravship");
            }

            // VGE's landing-collision set: things on the destination map overlapping the ship footprint
            var mapGenUtilityType = AccessTools.TypeByName("VanillaGravshipExpanded.GravshipMapGenUtility");
            if (mapGenUtilityType != null)
            {
                _blockingThingsField = AccessTools.Field(mapGenUtilityType, "BlockingThings");
            }

            if (_blockingThingsField == null)
            {
                Log.Warning("[Eternal] VGE GravshipMapGenUtility.BlockingThings not found - " +
                    "landing collision protection disabled (VGE API changed?)");
            }

            if (Eternal_Mod.settings?.debugMode == true)
            {
                Log.Message($"[Eternal] VGE crash landing patch types initialized: " +
                    $"map={_mapField != null}, gravship={_gravshipField != null}, " +
                    $"blockingThings={_blockingThingsField != null}");
            }
        }

        /// <summary>
        /// Gets the map from the gravship controller using reflection.
        /// </summary>
        private static MapType GetMap(object controller)
        {
            if (_mapField == null || controller == null)
                return null;

            try
            {
                return _mapField.GetValue(controller) as MapType;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "VGE_GetControllerMap", null, ex);
                return null;
            }
        }

        /// <summary>
        /// Gets the gravship from the controller using reflection.
        /// </summary>
        private static GravshipType GetGravship(object controller)
        {
            if (_gravshipField == null || controller == null)
                return null;

            try
            {
                return _gravshipField.GetValue(controller) as GravshipType;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "VGE_GetGravship", null, ex);
                return null;
            }
        }

        /// <summary>
        /// Gets the cells where VGE's ApplyCrashlanding will damage or destroy gravship things:
        /// the occupied rects of everything in GravshipMapGenUtility.BlockingThings.
        /// Empty on a clean landing.
        /// </summary>
        private static HashSet<IntVec3> GetBlockerOverlapCells()
        {
            var overlapCells = new HashSet<IntVec3>();

            if (_blockingThingsField == null)
            {
                return overlapCells;
            }

            try
            {
                if (_blockingThingsField.GetValue(null) is IEnumerable<Thing> blockingThings)
                {
                    foreach (var blocker in blockingThings)
                    {
                        if (blocker == null)
                        {
                            continue;
                        }

                        foreach (var cell in GenAdj.OccupiedRect(blocker))
                        {
                            overlapCells.Add(cell);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "VGE_GetBlockerOverlapCells", null, ex);
            }

            return overlapCells;
        }

        /// <summary>
        /// Specifies the target method to patch.
        /// </summary>
        public static MethodBase TargetMethod()
        {
            EnsureTypesInitialized();

            if (_gravshipControllerType == null)
            {
                Log.Error("[Eternal] VGE_LandingEnded_Patch: WorldComponent_GravshipController type not found");
                return null;
            }

            var method = AccessTools.Method(_gravshipControllerType, "LandingEnded");
            if (method == null)
            {
                Log.Error("[Eternal] VGE_LandingEnded_Patch: LandingEnded method not found");
            }

            return method;
        }

        /// <summary>
        /// Prefix: Save Eternals on blocker-overlap cells BEFORE VGE's crash landing logic runs.
        /// Uses HarmonyPriority.High to run before VGE's Normal priority patch.
        /// No-op on clean landings (no blocking things).
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)]
        public static void SaveEternalsBeforeCrash(object __instance, out VGELandingContext __state)
        {
            __state = new VGELandingContext();

            if (!SpaceModDetection.VanillaGravshipExpandedActive)
            {
                return;
            }

            try
            {
                MapType map = GetMap(__instance);
                if (map == null)
                {
                    return;
                }

                __state.Map = map;
                __state.WorldTile = map.Tile;

                var gravship = GetGravship(__instance);
                if (gravship?.Engine != null)
                {
                    __state.LandingPosition = gravship.Engine.Position;
                }

                // Only landings that collide with existing map things are dangerous.
                var overlapCells = GetBlockerOverlapCells();
                if (overlapCells.Count == 0)
                {
                    __state.PrefixSucceeded = true;
                    return;
                }

                // Riders: gravship things on overlap cells get damaged, or Destroy(0)'d outright
                // when the blocker is indestructible.
                if (gravship != null)
                {
                    foreach (var thing in gravship.Things.ToList())
                    {
                        if (thing is Pawn pawn && pawn.IsValidEternal() &&
                            pawn.Faction == Faction.OfPlayer && overlapCells.Contains(pawn.Position))
                        {
                            __state.EternalPawnsToSave.Add(pawn);
                            if (pawn.Spawned)
                            {
                                pawn.DeSpawn(DestroyMode.WillReplace);
                            }
                        }
                        else if (thing is CorpseType riderCorpse && riderCorpse.InnerPawn != null &&
                            riderCorpse.InnerPawn.IsValidEternal() && overlapCells.Contains(riderCorpse.Position))
                        {
                            __state.EternalCorpsesToSave.Add(riderCorpse);
                            if (riderCorpse.Spawned)
                            {
                                riderCorpse.DeSpawn(DestroyMode.WillReplace);
                            }
                        }
                    }
                }

                // Destination-map side: tracked Eternal corpses that ARE blocking things
                // (or sit on overlap cells) get destroyed by VGE's blocker cleanup.
                var corpseManager = EternalServiceContainer.Instance.CorpseManager;
                if (corpseManager != null && corpseManager.HasEternalCorpses(map))
                {
                    foreach (var entry in corpseManager.GetCorpsesOnMap(map).ToList())
                    {
                        if (entry?.Corpse != null && entry.Corpse.Spawned &&
                            overlapCells.Contains(entry.Corpse.Position) &&
                            !__state.EternalCorpsesToSave.Contains(entry.Corpse))
                        {
                            __state.EternalCorpsesToSave.Add(entry.Corpse);
                            entry.Corpse.DeSpawn(DestroyMode.WillReplace);
                        }
                    }
                }

                __state.PrefixSucceeded = true;

                int totalSaved = __state.EternalCorpsesToSave.Count + __state.EternalPawnsToSave.Count;
                if (totalSaved > 0)
                {
                    Log.Message($"[Eternal] Protected {totalSaved} Eternal(s) from VGE crash landing collision");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "SaveEternalsBeforeCrash", null, ex);
                __state.PrefixSucceeded = false;
            }
        }

        /// <summary>
        /// Postfix: Respawn saved Eternals after crash landing completes.
        /// Uses the Map captured by the prefix - the controller's field is always null here.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        public static void RespawnEternalsAfterCrash(object __instance, VGELandingContext __state)
        {
            if (__state == null || !__state.PrefixSucceeded)
            {
                return;
            }

            int totalToRespawn = __state.EternalCorpsesToSave.Count + __state.EternalPawnsToSave.Count;
            if (totalToRespawn == 0)
            {
                return;
            }

            try
            {
                MapType map = __state.Map;
                bool mapGone = map == null || map.Disposed || Find.Maps == null || !Find.Maps.Contains(map);
                if (mapGone)
                {
                    HandleMapDestroyed(__state);
                    return;
                }

                foreach (var corpse in __state.EternalCorpsesToSave)
                {
                    if (corpse == null || corpse.Destroyed)
                    {
                        continue;
                    }

                    IntVec3 spawnPos = FindSafeSpawnLocation(map, __state.LandingPosition);
                    if (spawnPos.IsValid)
                    {
                        GenSpawn.Spawn(corpse, spawnPos, map);
                        EternalServiceContainer.Instance?.CorpseManager?.UpdateCorpseLocation(
                            corpse.InnerPawn, map, spawnPos);
                    }
                }

                foreach (var pawn in __state.EternalPawnsToSave)
                {
                    if (pawn == null || pawn.Destroyed)
                    {
                        continue;
                    }

                    IntVec3 spawnPos = FindSafeSpawnLocation(map, __state.LandingPosition);
                    if (spawnPos.IsValid)
                    {
                        GenSpawn.Spawn(pawn, spawnPos, map);
                        ApplyCrashDamage(pawn);
                    }
                }

                Find.LetterStack?.ReceiveLetter(
                    "EternalVGECrashSurvival".Translate(),
                    "EternalVGECrashSurvivalDesc".Translate(totalToRespawn),
                    LetterDefOf.NeutralEvent);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "RespawnEternalsAfterCrash", null, ex);
                // Emergency fallback
                HandleMapDestroyed(__state);
            }
        }

        /// <summary>
        /// Finds a safe spawn location near the target position.
        /// </summary>
        private static IntVec3 FindSafeSpawnLocation(MapType map, IntVec3 target)
        {
            if (!target.IsValid)
            {
                target = map.Center;
            }

            // Try to find a walkable cell near the target
            for (int radius = 0; radius < 30; radius++)
            {
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(target, radius, true))
                {
                    if (cell.InBounds(map) && cell.Standable(map) && !cell.Fogged(map))
                    {
                        return cell;
                    }
                }
            }

            // Fallback to random edge cell
            return CellFinder.RandomEdgeCell(map);
        }

        /// <summary>
        /// Applies crash/fall damage to a pawn.
        /// </summary>
        private static void ApplyCrashDamage(Pawn pawn)
        {
            if (pawn?.health == null)
            {
                return;
            }

            try
            {
                // Apply moderate blunt damage - crash landing is dangerous
                var damageInfo = new DamageInfo(
                    DamageDefOf.Blunt,
                    amount: 40f,
                    armorPenetration: 0f,
                    angle: -1f,
                    instigator: null,
                    hitPart: null,
                    weapon: null);

                pawn.TakeDamage(damageInfo);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "ApplyCrashDamage", pawn, ex);
            }
        }

        /// <summary>
        /// Emergency handler when the landing map is genuinely gone: deliver the saved
        /// Eternals to a crash site at the landing tile (already a ground tile).
        /// </summary>
        private static void HandleMapDestroyed(VGELandingContext state)
        {
            Log.Warning("[Eternal] VGE landing map was destroyed - attempting crash site recovery");

            try
            {
                var crashSite = state.WorldTile >= 0
                    ? SpaceCrashRescueService.CreateOrGetCrashSite(state.WorldTile)
                    : null;

                if (crashSite == null)
                {
                    SpaceCrashRescueService.SpawnAtHomeColony(
                        state.EternalPawnsToSave.Concat(
                            state.EternalCorpsesToSave.Select(c => c.InnerPawn)));
                    return;
                }

                foreach (var corpse in state.EternalCorpsesToSave)
                {
                    if (corpse != null && !corpse.Destroyed)
                    {
                        crashSite.AddCorpse(corpse);
                    }
                }

                foreach (var pawn in state.EternalPawnsToSave)
                {
                    if (pawn != null && !pawn.Destroyed)
                    {
                        SpaceCrashRescueService.ApplyFallDamage(pawn);
                        crashSite.AddPawn(pawn);
                    }
                }

                Find.LetterStack?.ReceiveLetter(
                    "EternalVGECrashSite".Translate(),
                    "EternalVGECrashSiteDesc".Translate(),
                    LetterDefOf.NegativeEvent,
                    crashSite);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "HandleMapDestroyed", null, ex);
                SpaceCrashRescueService.SpawnAtHomeColony(
                    state.EternalPawnsToSave.Concat(
                        state.EternalCorpsesToSave.Select(c => c.InnerPawn)));
            }
        }
    }
}
