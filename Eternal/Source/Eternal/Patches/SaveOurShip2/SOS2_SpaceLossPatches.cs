// Relative Path: Eternal/Source/Eternal/Patches/SaveOurShip2/SOS2_SpaceLossPatches.cs
// Creation Date: 13-07-2026
// Last Edit: 13-07-2026
// Author: 0Shard
// Description: Harmony patches for SOS2 destruction paths beyond ship burn-up (which
//              SOS2_OrbitingShipPatch covers). Verified against SOS2 Steam V2.8.104:
//              - ShipMapComp.KillAllOffShip: Destroys every thing (and Crush-kills every pawn)
//                on cells outside MapShipCells during transit/landing.
//              - ShipInteriorMod2.RemoveShipOrArea: kills pawns and destroys things in a ship
//                area being removed (battle loss, endgame).
//              - ShipInteriorMod2.MoveShip: Destroy(0)'s non-pawn things (corpses!) in the
//                landing footprint on the target map.
//              - SpaceShipCache.FloatAndDestroy (private): Bomb-kills pawns and destroys things
//                on hull cells detached in combat.
//              - ShipMapComp.DeRegisterShuttleMission(destroyed: true): called BEFORE the
//                shoot-down kill loops (ShipMapComp.MapComponentTick and
//                Verb_LaunchProjectileShip.PointDefense); non-player pawn corpses and ALL
//                shuttle cargo (carried Eternal corpses) are lost there.
//              Eternals are routed through SpaceCrashRescueService (torso-only re-entry).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Eternal.Compatibility;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Extensions;
using Eternal.Utils;
using Eternal.World;

// Type aliases to resolve namespace shadowing (Eternal.Map/Corpse shadows Verse types)
using MapType = Verse.Map;
using CorpseType = Verse.Corpse;

namespace Eternal.Patches.SaveOurShip2
{
    /// <summary>
    /// Shared reflection lookups for the SOS2 space-loss patches.
    /// </summary>
    internal static class SOS2SpaceLossReflection
    {
        internal static Type ShipMapCompType => AccessTools.TypeByName("SaveOurShip2.ShipMapComp");
        internal static Type ShipInteriorModType => AccessTools.TypeByName("SaveOurShip2.ShipInteriorMod2");
        internal static Type SpaceShipCacheType => AccessTools.TypeByName("SaveOurShip2.SpaceShipCache");

        /// <summary>
        /// Reads ShipMapComp.MapShipCells keys (the cells occupied by ships on the map).
        /// Returns null when the member cannot be resolved (SOS2 API drift) so callers
        /// can skip instead of misclassifying every cell as off-ship.
        /// </summary>
        internal static HashSet<IntVec3> GetMapShipCells(object shipMapComp)
        {
            var mapShipCellsProperty = AccessTools.Property(shipMapComp.GetType(), "MapShipCells");
            if (mapShipCellsProperty?.GetValue(shipMapComp) is IDictionary cellDictionary)
            {
                var shipCells = new HashSet<IntVec3>();
                foreach (var key in cellDictionary.Keys)
                {
                    if (key is IntVec3 cell)
                    {
                        shipCells.Add(cell);
                    }
                }
                return shipCells;
            }

            return null;
        }
    }

    /// <summary>
    /// Rescues Eternals on off-ship cells before ShipMapComp.KillAllOffShip purges them
    /// during ship transit/landing.
    /// </summary>
    [HarmonyPatch]
    public static class SOS2_KillAllOffShip_Patch
    {
        public static bool Prepare() => SpaceModDetection.SaveOurShip2Active;

        public static MethodBase TargetMethod()
        {
            var method = AccessTools.Method(SOS2SpaceLossReflection.ShipMapCompType, "KillAllOffShip");
            if (method == null)
            {
                Log.Warning("[Eternal] Could not find ShipMapComp.KillAllOffShip (SOS2 API changed?)");
            }
            return method;
        }

        [HarmonyPrefix]
        public static void RescueEternalsOffShip(MapComponent __instance)
        {
            try
            {
                MapType map = __instance?.map;
                if (map == null)
                {
                    return;
                }

                var shipCells = SOS2SpaceLossReflection.GetMapShipCells(__instance);
                if (shipCells == null)
                {
                    Log.Warning("[Eternal] MapShipCells unavailable - skipping off-ship rescue");
                    return;
                }

                var living = new List<Pawn>();
                var corpses = new List<CorpseType>();
                SpaceCrashRescueService.CollectEternalsOnMap(map,
                    cell => !shipCells.Contains(cell), living, corpses);

                int rescued = SpaceCrashRescueService.CrashDownEternals(living, corpses, map.Tile);
                if (rescued > 0)
                {
                    Log.Message($"[Eternal] Crash-downed {rescued} Eternal(s) from SOS2 off-ship purge");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "RescueEternalsOffShip", null, ex);
            }
        }
    }

    /// <summary>
    /// Rescues Eternals inside a ship area that ShipInteriorMod2.RemoveShipOrArea is about
    /// to purge (battle loss, ship deletion, endgame departure).
    /// </summary>
    [HarmonyPatch]
    public static class SOS2_RemoveShipOrArea_Patch
    {
        public static bool Prepare() => SpaceModDetection.SaveOurShip2Active;

        public static MethodBase TargetMethod()
        {
            var method = AccessTools.Method(SOS2SpaceLossReflection.ShipInteriorModType, "RemoveShipOrArea");
            if (method == null)
            {
                Log.Warning("[Eternal] Could not find ShipInteriorMod2.RemoveShipOrArea (SOS2 API changed?)");
            }
            return method;
        }

        [HarmonyPrefix]
        public static void RescueEternalsInArea(MapType map, int index, HashSet<IntVec3> area, bool killPawns)
        {
            try
            {
                if (!killPawns || map == null)
                {
                    return;
                }

                var doomedCells = area ?? ResolveShipAreaByIndex(map, index);
                if (doomedCells == null || doomedCells.Count == 0)
                {
                    return;
                }

                var living = new List<Pawn>();
                var corpses = new List<CorpseType>();
                SpaceCrashRescueService.CollectEternalsOnMap(map,
                    cell => doomedCells.Contains(cell), living, corpses);

                int rescued = SpaceCrashRescueService.CrashDownEternals(living, corpses, map.Tile);
                if (rescued > 0)
                {
                    Log.Message($"[Eternal] Crash-downed {rescued} Eternal(s) from SOS2 ship-area removal");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "RescueEternalsInArea", null, ex);
            }
        }

        /// <summary>
        /// Resolves a ship's cell area from its index via ShipMapComp.ShipsOnMap[index].Area.
        /// Null when unresolvable - callers must then skip rather than over-rescue pawns
        /// that are not on the doomed ship.
        /// </summary>
        private static HashSet<IntVec3> ResolveShipAreaByIndex(MapType map, int index)
        {
            if (index < 0 || SOS2SpaceLossReflection.ShipMapCompType == null)
            {
                return null;
            }

            var shipMapComp = map.GetComponent(SOS2SpaceLossReflection.ShipMapCompType);
            if (shipMapComp == null)
            {
                return null;
            }

            var shipsOnMapProperty = AccessTools.Property(shipMapComp.GetType(), "ShipsOnMap");
            if (!(shipsOnMapProperty?.GetValue(shipMapComp) is IDictionary shipsOnMap) ||
                !shipsOnMap.Contains(index))
            {
                return null;
            }

            var shipCache = shipsOnMap[index];
            var areaField = AccessTools.Field(SOS2SpaceLossReflection.SpaceShipCacheType, "Area");
            return areaField?.GetValue(shipCache) as HashSet<IntVec3>;
        }
    }

    /// <summary>
    /// Context for the MoveShip patch: corpses lifted out of the landing footprint.
    /// </summary>
    public class SOS2MoveShipContext
    {
        public List<(CorpseType corpse, IntVec3 position)> LiftedCorpses { get; } =
            new List<(CorpseType, IntVec3)>();
        public MapType TargetMap { get; set; }
    }

    /// <summary>
    /// Protects Eternal corpses on the target map while ShipInteriorMod2.MoveShip lands a ship:
    /// non-pawn things (corpses) in the landing footprint are Destroy(0)'d by SOS2. The landing
    /// footprint is computed deep inside MoveShip, so every tracked-map Eternal corpse is lifted
    /// for the duration and put back afterwards, nudged to the nearest standable cell.
    /// </summary>
    [HarmonyPatch]
    public static class SOS2_MoveShip_Patch
    {
        public static bool Prepare() => SpaceModDetection.SaveOurShip2Active;

        public static MethodBase TargetMethod()
        {
            var method = AccessTools.Method(SOS2SpaceLossReflection.ShipInteriorModType, "MoveShip");
            if (method == null)
            {
                Log.Warning("[Eternal] Could not find ShipInteriorMod2.MoveShip (SOS2 API changed?)");
            }
            return method;
        }

        [HarmonyPrefix]
        public static void LiftEternalCorpses(MapType targetMap, out SOS2MoveShipContext __state)
        {
            __state = new SOS2MoveShipContext();

            try
            {
                if (targetMap == null)
                {
                    return;
                }

                __state.TargetMap = targetMap;

                var living = new List<Pawn>();
                var corpses = new List<CorpseType>();
                // Living pawns are teleported clear by MoveShip itself; only corpses are destroyed.
                SpaceCrashRescueService.CollectEternalsOnMap(targetMap, null, living, corpses);

                foreach (var corpse in corpses)
                {
                    if (corpse.Spawned)
                    {
                        __state.LiftedCorpses.Add((corpse, corpse.Position));
                        corpse.DeSpawn(DestroyMode.WillReplace);
                    }
                }

                if (__state.LiftedCorpses.Count > 0)
                {
                    Log.Message($"[Eternal] Lifted {__state.LiftedCorpses.Count} Eternal corpse(s) clear of SOS2 ship landing");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "LiftEternalCorpses", null, ex);
            }
        }

        [HarmonyPostfix]
        public static void ReplaceEternalCorpses(SOS2MoveShipContext __state)
        {
            if (__state == null || __state.LiftedCorpses.Count == 0 || __state.TargetMap == null)
            {
                return;
            }

            try
            {
                foreach (var (corpse, originalPosition) in __state.LiftedCorpses)
                {
                    if (corpse == null || corpse.Destroyed)
                    {
                        continue;
                    }

                    IntVec3 spawnPos = CellFinder.StandableCellNear(originalPosition, __state.TargetMap, 10f);
                    if (!spawnPos.IsValid)
                    {
                        spawnPos = CellFinder.RandomEdgeCell(__state.TargetMap);
                    }

                    if (spawnPos.IsValid)
                    {
                        GenSpawn.Spawn(corpse, spawnPos, __state.TargetMap);
                        EternalServiceContainer.Instance?.CorpseManager?.UpdateCorpseLocation(
                            corpse.InnerPawn, __state.TargetMap, spawnPos);
                    }
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "ReplaceEternalCorpses", null, ex);
            }
        }
    }

    /// <summary>
    /// Rescues Eternals standing on hull cells that SpaceShipCache.FloatAndDestroy is about
    /// to detach and purge (pawns Bomb-killed, things destroyed) during ship combat.
    /// </summary>
    [HarmonyPatch]
    public static class SOS2_FloatAndDestroy_Patch
    {
        public static bool Prepare() => SpaceModDetection.SaveOurShip2Active;

        public static MethodBase TargetMethod()
        {
            // Private method - resolved by name on the cache type
            var method = AccessTools.Method(SOS2SpaceLossReflection.SpaceShipCacheType, "FloatAndDestroy");
            if (method == null)
            {
                Log.Warning("[Eternal] Could not find SpaceShipCache.FloatAndDestroy (SOS2 API changed?)");
            }
            return method;
        }

        [HarmonyPrefix]
        public static void RescueEternalsOnDetachedHull(object __instance, HashSet<IntVec3> detachArea)
        {
            try
            {
                if (detachArea == null || detachArea.Count == 0)
                {
                    return;
                }

                var mapProperty = AccessTools.Property(__instance.GetType(), "Map");
                if (!(mapProperty?.GetValue(__instance) is MapType map))
                {
                    return;
                }

                var living = new List<Pawn>();
                var corpses = new List<CorpseType>();
                SpaceCrashRescueService.CollectEternalsOnMap(map,
                    cell => detachArea.Contains(cell), living, corpses);

                int rescued = SpaceCrashRescueService.CrashDownEternals(living, corpses, map.Tile);
                if (rescued > 0)
                {
                    Log.Message($"[Eternal] Crash-downed {rescued} Eternal(s) from SOS2 hull detach");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "RescueEternalsOnDetachedHull", null, ex);
            }
        }
    }

    /// <summary>
    /// Rescues Eternals from a shuttle shot down in ship combat. SOS2 calls
    /// DeRegisterShuttleMission(destroyed: true) BEFORE its kill loop (both call sites),
    /// so at prefix time everyone is still aboard. SOS2 itself drops player pawns (downed)
    /// or their corpses back to the origin map - those are left alone. Lost without this
    /// patch: non-player Eternal pawns (killed, corpse never spawned) and every Eternal
    /// corpse carried as cargo (GetDirectlyHeldThings is Kill()'d wholesale).
    /// </summary>
    [HarmonyPatch]
    public static class SOS2_ShuttleShootdown_Patch
    {
        public static bool Prepare() => SpaceModDetection.SaveOurShip2Active;

        public static MethodBase TargetMethod()
        {
            var method = AccessTools.Method(SOS2SpaceLossReflection.ShipMapCompType, "DeRegisterShuttleMission");
            if (method == null)
            {
                Log.Warning("[Eternal] Could not find ShipMapComp.DeRegisterShuttleMission (SOS2 API changed?)");
            }
            return method;
        }

        [HarmonyPrefix]
        public static void RescueEternalsFromShuttle(MapComponent __instance, object mission, bool destroyed)
        {
            try
            {
                if (!destroyed || mission == null)
                {
                    return;
                }

                var shuttleField = AccessTools.Field(mission.GetType(), "shuttle");
                if (!(shuttleField?.GetValue(mission) is Pawn shuttle))
                {
                    return;
                }

                var living = new List<Pawn>();
                var corpses = new List<CorpseType>();

                // Cargo: carried Eternal corpses are Kill()'d with the shuttle
                var heldThings = shuttle.GetDirectlyHeldThings();
                if (heldThings != null)
                {
                    foreach (var thing in heldThings.ToList())
                    {
                        if (thing is CorpseType corpse && corpse.InnerPawn != null &&
                            corpse.InnerPawn.IsValidEternal())
                        {
                            heldThings.Remove(corpse);
                            corpses.Add(corpse);
                        }
                    }
                }

                // Passengers: SOS2 recovers player pawns/corpses itself; non-player Eternals
                // are killed while held (no corpse is ever created for them).
                var pawnsAboardProperty = AccessTools.Property(shuttle.GetType(), "AllPawnsAboard");
                var removePawnMethod = AccessTools.Method(shuttle.GetType(), "RemovePawn");
                if (pawnsAboardProperty?.GetValue(shuttle) is IEnumerable<Pawn> pawnsAboard &&
                    removePawnMethod != null)
                {
                    foreach (var pawn in pawnsAboard.ToList())
                    {
                        if (pawn != null && pawn.IsValidEternal() && pawn.Faction != Faction.OfPlayer)
                        {
                            // Must leave the vehicle holder before Kill, or no corpse is created
                            removePawnMethod.Invoke(shuttle, new object[] { pawn });
                            living.Add(pawn);
                        }
                    }
                }

                int rescued = SpaceCrashRescueService.CrashDownEternals(living, corpses,
                    __instance?.map?.Tile ?? RimWorld.Planet.PlanetTile.Invalid);
                if (rescued > 0)
                {
                    Log.Message($"[Eternal] Crash-downed {rescued} Eternal(s) from SOS2 shuttle shoot-down");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "RescueEternalsFromShuttle", null, ex);
            }
        }
    }
}
