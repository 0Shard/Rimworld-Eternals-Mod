// Relative Path: Eternal/Source/Eternal/Patches/VGE/VGE_CrashLanding_Patch.cs
// Creation Date: 25-12-2025
// Last Edit: 20-02-2026
// Author: 0Shard
// Description: Harmony patches for VGE (Vanilla Gravship Expanded) crash landing compatibility.
//              Protects Eternal corpses from being destroyed during gravship crash landings.
//              Uses the same rescue pattern as SOS2/Odyssey patches - despawn before destruction,
//              respawn after with fall damage applied.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Eternal.Compatibility;
using Eternal.Corpse;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Extensions;
using Eternal.Utils;
using Eternal.World;

// Type aliases to resolve namespace shadowing
using MapType = Verse.Map;
using CorpseType = Verse.Corpse;

namespace Eternal.Patches.VGE
{
    /// <summary>
    /// Context data for passing Eternal corpse information between Prefix and Postfix.
    /// </summary>
    public class VGELandingContext
    {
        public List<CorpseType> EternalCorpsesToSave { get; } = new List<CorpseType>();
        public List<Pawn> EternalPawnsToSave { get; } = new List<Pawn>();
        public int WorldTile { get; set; } = -1;
        public IntVec3 LandingPosition { get; set; } = IntVec3.Invalid;
        public bool PrefixSucceeded { get; set; }
    }

    /// <summary>
    /// Patches WorldComponent_GravshipController.LandingEnded to protect Eternal corpses
    /// during crash landings. Runs BEFORE VGE's patch to save corpses, then respawns them after.
    /// </summary>
    /// <remarks>
    /// VGE's ApplyCrashlanding method (called in their Prefix) destroys things that collide
    /// with the gravship during landing. We intercept this by:
    /// 1. Running at High priority (before VGE's Normal priority)
    /// 2. Temporarily despawning Eternal corpses before VGE processes them
    /// 3. Respawning them after landing with appropriate fall damage
    /// </remarks>
    [HarmonyPatch]
    public static class VGE_LandingEnded_Patch
    {
        private static Type _gravshipControllerType;
        private static FieldInfo _mapField;
        private static FieldInfo _gravshipField;
        private static bool _typesInitialized = false;

        /// <summary>
        /// Determines if this patch should be applied.
        /// Only patches when VGE is active.
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
                // Cache the field accessors
                _mapField = AccessTools.Field(_gravshipControllerType, "map");
                _gravshipField = AccessTools.Field(_gravshipControllerType, "gravship");

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] VGE crash landing patch types initialized: " +
                        $"map={_mapField != null}, gravship={_gravshipField != null}");
                }
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
        private static object GetGravship(object controller)
        {
            if (_gravshipField == null || controller == null)
                return null;

            try
            {
                return _gravshipField.GetValue(controller);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "VGE_GetGravship", null, ex);
                return null;
            }
        }

        /// <summary>
        /// Gets the engine position from a gravship object using reflection.
        /// </summary>
        private static IntVec3 GetGravshipPosition(object gravship)
        {
            if (gravship == null)
                return IntVec3.Invalid;

            try
            {
                var engineProp = AccessTools.Property(gravship.GetType(), "Engine");
                if (engineProp == null)
                    return IntVec3.Invalid;

                var engine = engineProp.GetValue(gravship) as Thing;
                return engine?.Position ?? IntVec3.Invalid;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "VGE_GetGravshipPosition", null, ex);
                return IntVec3.Invalid;
            }
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
        /// Prefix: Save Eternal corpses BEFORE VGE's crash landing logic runs.
        /// Uses HarmonyPriority.High to run before VGE's Normal priority patch.
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

                __state.WorldTile = map.Tile;

                // Get landing position from gravship if available
                var gravship = GetGravship(__instance);
                if (gravship != null)
                {
                    __state.LandingPosition = GetGravshipPosition(gravship);
                }

                // Find all Eternal corpses on the map
                var corpseManager = EternalServiceContainer.Instance.CorpseManager;
                if (corpseManager != null && corpseManager.HasEternalCorpses(map))
                {
                    var trackedCorpses = corpseManager.GetCorpsesOnMap(map).ToList();

                    foreach (var entry in trackedCorpses)
                    {
                        if (entry?.Corpse != null && entry.Corpse.Spawned)
                        {
                            __state.EternalCorpsesToSave.Add(entry.Corpse);
                            entry.Corpse.DeSpawn(DestroyMode.WillReplace);

                            if (Eternal_Mod.settings?.debugMode == true)
                            {
                                Log.Message($"[Eternal] Saved corpse of {entry.OriginalPawn?.Name} from VGE crash landing");
                            }
                        }
                    }
                }

                // Also find any living Eternal pawns that might be in the crash zone
                var allPawns = map.mapPawns?.AllPawnsSpawned?.ToList();
                if (allPawns != null)
                {
                    foreach (var pawn in allPawns)
                    {
                        if (pawn != null && pawn.IsValidEternal() && pawn.Faction == Faction.OfPlayer)
                        {
                            // Check if pawn is near the landing zone (within gravship bounds)
                            if (__state.LandingPosition.IsValid &&
                                pawn.Position.DistanceTo(__state.LandingPosition) < 20f)
                            {
                                __state.EternalPawnsToSave.Add(pawn);
                                pawn.DeSpawn(DestroyMode.WillReplace);

                                if (Eternal_Mod.settings?.debugMode == true)
                                {
                                    Log.Message($"[Eternal] Saved {pawn.Name} from VGE crash landing zone");
                                }
                            }
                        }
                    }
                }

                __state.PrefixSucceeded = true;

                int totalSaved = __state.EternalCorpsesToSave.Count + __state.EternalPawnsToSave.Count;
                if (totalSaved > 0)
                {
                    Log.Message($"[Eternal] Protected {totalSaved} Eternal(s) from VGE crash landing");
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
        /// Applies fall damage and spawns them near the landing site.
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
                MapType map = GetMap(__instance);
                if (map == null)
                {
                    // Map was destroyed - try to create crash site
                    HandleMapDestroyed(__state);
                    return;
                }

                // Respawn corpses on the map
                foreach (var corpse in __state.EternalCorpsesToSave)
                {
                    if (corpse == null || corpse.Destroyed)
                    {
                        continue;
                    }

                    // Find a valid spawn location near the landing site
                    IntVec3 spawnPos = FindSafeSpawnLocation(map, __state.LandingPosition);
                    if (spawnPos.IsValid)
                    {
                        GenSpawn.Spawn(corpse, spawnPos, map);

                        if (Eternal_Mod.settings?.debugMode == true)
                        {
                            Log.Message($"[Eternal] Respawned corpse at {spawnPos} after VGE landing");
                        }
                    }
                }

                // Respawn living pawns with fall damage
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

                        if (Eternal_Mod.settings?.debugMode == true)
                        {
                            Log.Message($"[Eternal] Respawned {pawn.Name} with crash damage after VGE landing");
                        }
                    }
                }

                if (totalToRespawn > 0)
                {
                    // Notify player
                    Find.LetterStack?.ReceiveLetter(
                        "Eternal.VGECrashSurvival".Translate(),
                        "Eternal.VGECrashSurvivalDesc".Translate(totalToRespawn),
                        LetterDefOf.NeutralEvent);
                }
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
        /// Emergency handler when the map was destroyed.
        /// Creates a crash site for rescued Eternals.
        /// </summary>
        private static void HandleMapDestroyed(VGELandingContext state)
        {
            Log.Warning("[Eternal] VGE landing map was destroyed - attempting crash site recovery");

            try
            {
                // Create crash site at world tile
                if (state.WorldTile < 0)
                {
                    Log.Error("[Eternal] No valid world tile for crash site");
                    SpawnAtHomeColony(state);
                    return;
                }

                var crashSite = CreateOrGetCrashSite(state.WorldTile);
                if (crashSite == null)
                {
                    SpawnAtHomeColony(state);
                    return;
                }

                // Add corpses to crash site (they'll spawn when player visits)
                foreach (var corpse in state.EternalCorpsesToSave)
                {
                    if (corpse?.InnerPawn != null)
                    {
                        crashSite.AddPawn(corpse.InnerPawn);
                    }
                }

                // Add living pawns to crash site
                foreach (var pawn in state.EternalPawnsToSave)
                {
                    if (pawn != null && !pawn.Destroyed)
                    {
                        ApplyCrashDamage(pawn);
                        crashSite.AddPawn(pawn);
                    }
                }

                Find.LetterStack?.ReceiveLetter(
                    "Eternal.VGECrashSite".Translate(),
                    "Eternal.VGECrashSiteDesc".Translate(),
                    LetterDefOf.NegativeEvent,
                    crashSite);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "HandleMapDestroyed", null, ex);
                SpawnAtHomeColony(state);
            }
        }

        /// <summary>
        /// Creates or retrieves an existing crash site at the given tile.
        /// </summary>
        private static WorldObject_EternalCrashSite CreateOrGetCrashSite(int worldTile)
        {
            // Check for existing crash site
            var existing = Find.WorldObjects?.AllWorldObjects
                ?.OfType<WorldObject_EternalCrashSite>()
                .FirstOrDefault(x => x.Tile == worldTile);

            if (existing != null)
            {
                return existing;
            }

            // Create new crash site
            var crashSiteDef = EternalDefOf.Eternal_CrashSite;
            if (crashSiteDef == null)
            {
                Log.Error("[Eternal] Eternal_CrashSite WorldObjectDef not found");
                return null;
            }

            var crashSite = (WorldObject_EternalCrashSite)WorldObjectMaker.MakeWorldObject(crashSiteDef);
            if (crashSite == null)
            {
                return null;
            }

            crashSite.Tile = worldTile;
            crashSite.SetFaction(Faction.OfPlayer);
            Find.WorldObjects.Add(crashSite);

            return crashSite;
        }

        /// <summary>
        /// Emergency fallback: spawn at home colony.
        /// </summary>
        private static void SpawnAtHomeColony(VGELandingContext state)
        {
            var homeMap = Find.AnyPlayerHomeMap;
            if (homeMap == null)
            {
                Log.Error("[Eternal] No home map for emergency spawn - Eternals may be lost");
                return;
            }

            foreach (var corpse in state.EternalCorpsesToSave)
            {
                if (corpse != null && !corpse.Destroyed)
                {
                    var cell = CellFinder.RandomEdgeCell(homeMap);
                    if (cell.IsValid)
                    {
                        GenSpawn.Spawn(corpse, cell, homeMap);
                    }
                }
            }

            foreach (var pawn in state.EternalPawnsToSave)
            {
                if (pawn != null && !pawn.Destroyed)
                {
                    var cell = CellFinder.RandomEdgeCell(homeMap);
                    if (cell.IsValid)
                    {
                        GenSpawn.Spawn(pawn, cell, homeMap);
                        ApplyCrashDamage(pawn);
                    }
                }
            }

            Find.LetterStack?.ReceiveLetter(
                "Eternal.EmergencyRecovery".Translate(),
                "Eternal.EmergencyRecoveryDesc".Translate(),
                LetterDefOf.NegativeEvent);
        }
    }
}
