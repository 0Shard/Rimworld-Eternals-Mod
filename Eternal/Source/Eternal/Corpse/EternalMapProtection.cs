/*
 * Relative Path: Eternal/Source/Eternal/Corpse/EternalMapProtection.cs
 * Creation Date: 09-11-2025
 * Last Edit: 13-10-2026
 *              Ground removals recover before native teardown; late ground callbacks only retire protection.
 *              05-02: HandleMapRemoval() event-driven rescue hook called from EternalMapManager.MapRemoved() (PERF-07).
 *              Gate 1: Pending teleport state is explicitly wired through Eternal_Component and
 *                       every relocation commits through EternalCorpseManager.
 * Author: 0Shard
 * Description: Prevents destruction of temporary maps containing Eternal corpses and provides corpse transfer mechanisms.
 *              Implements delayed teleportation system with save/load persistence for corpse relocation.
 *              Optimized with random sampling for spawn location search and pooled lists.
 *              03-02: All catch sites converted to EternalLogger.HandleException(MapProtection, ...).
 */

using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Utils;
using Eternal.Utilities;
using Eternal.Compatibility;
using Eternal.World;
using Eternal.Extensions;
using RimWorld.Planet;
using MapType = Verse.Map;

// Type alias for backwards compatibility
using EternalCorpseData = Eternal.Models.CorpseTrackingEntry;

namespace Eternal.Corpse
{
    /// <summary>
    /// Prevents destruction of temporary maps containing Eternal corpses.
    /// Schedules delayed corpse teleportation to the player home map.
    /// </summary>
    public class EternalMapProtection
    {
        private readonly HashSet<MapType> protectedMaps = new HashSet<MapType>();
        private readonly Dictionary<MapType, MapProtectionData> protectionData = new Dictionary<MapType, MapProtectionData>();
        private int lastProtectionCheck = 0;
        private const int PROTECTION_CHECK_INTERVAL = 500; // Check every 500 ticks

        // PERF: Random sampling for spawn location search instead of iterating all cells
        private const int SPAWN_LOCATION_SAMPLE_SIZE = 200; // Try 200 random cells instead of 62,500+
        private const int SPAWN_LOCATION_FALLBACK_SIZE = 500; // Larger sample if first pass fails

        /// <summary>
        /// Data structure for map protection information.
        /// </summary>
        public class MapProtectionData
        {
            public Verse.Map Map { get; set; }
            public int ProtectionStartTick { get; set; }
            public int EternalCorpseCount { get; set; }
            public MapProtectionStrategy Strategy { get; set; }
            public bool IsTemporaryMap { get; set; }
            public string OriginalMapPurpose { get; set; }
        }

        /// <summary>
        /// Strategies for handling Eternal corpses on temporary maps.
        /// </summary>
        public enum MapProtectionStrategy
        {
            TeleportToHome = 0
        }

        /// <summary>
        /// Represents a pending teleportation operation for a corpse.
        /// Supports save/load persistence via IExposable.
        /// </summary>
        private class PendingTeleport : IExposable
        {
            public Verse.Corpse Corpse;
            public Verse.Map TargetMap;
            public IntVec3 TargetLocation;
            public int TriggerTick;

            public void ExposeData()
            {
                Scribe_References.Look(ref Corpse, "corpse");
                Scribe_References.Look(ref TargetMap, "targetMap");
                Scribe_Values.Look(ref TargetLocation, "targetLocation");
                Scribe_Values.Look(ref TriggerTick, "triggerTick");
            }
        }

        // Pending teleportation queue for delayed corpse transfers
        private List<PendingTeleport> pendingTeleports = new List<PendingTeleport>();

        // Removal entrypoints nest (Site.Destroy -> WorldObject.Destroy -> Remove -> Game).
        // Keep their destinations excluded until the outermost native call finishes.
        private readonly Dictionary<MapType, int> removingMaps = new Dictionary<MapType, int>();
        private readonly HashSet<MapType> preparingRemoval = new HashSet<MapType>();
        private readonly Dictionary<WorldObject_EternalCrashSite, int> removingSites = new Dictionary<WorldObject_EternalCrashSite, int>();
        private readonly HashSet<WorldObject_EternalCrashSite> preparingSiteRemoval = new HashSet<WorldObject_EternalCrashSite>();

        public static bool IsGroundMap(MapType map)
        {
            return map != null && !(map.Parent is SpaceMapParent)
                && !SpaceModDetection.IsOrbitingShip(map.Parent);
        }

        public bool IsRemovalInProgress(MapType map)
        {
            return map != null && removingMaps.ContainsKey(map);
        }

        public bool BeginRecoverySiteRemoval(WorldObject_EternalCrashSite site)
        {
            removingSites.TryGetValue(site, out int depth);
            removingSites[site] = depth + 1;
            if (!preparingSiteRemoval.Add(site))
            {
                return false;
            }

            try
            {
                var manager = EternalServiceContainer.Instance?.CorpseManager;
                if (manager == null)
                {
                    return !site.PendingCorpsesForRecovery().Any(corpse => corpse.InnerPawn.IsValidEternalCorpse());
                }

                bool recovered = true;
                foreach (var corpse in site.PendingCorpsesForRecovery())
                {
                    var entry = manager.GetCorpseData(corpse.InnerPawn);
                    if (entry?.Corpse != corpse)
                    {
                        // Solo death stages the real corpse before Notify_PawnDied registers
                        // it. Keep the persistent owner through that synchronous gap; do not
                        // synthesize a queue or treat a mismatched registration as acceptance.
                        if (corpse.InnerPawn.IsValidEternalCorpse())
                        {
                            recovered = false;
                        }

                        continue;
                    }

                    if (TryRecoverCorpse(entry, site.HasMap ? site.Map : null, site))
                    {
                        site.ForgetPendingCorpse(corpse);
                    }
                    else
                    {
                        recovered = false;
                    }
                }
                return recovered && !site.PendingCorpsesForRecovery().Any(corpse =>
                    corpse.InnerPawn.IsValidEternalCorpse()
                        || manager.GetCorpseData(corpse.InnerPawn)?.Corpse == corpse);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection, "BeginRecoverySiteRemoval", null, ex);
                return false;
            }
            finally { preparingSiteRemoval.Remove(site); }
        }

        public void EndRecoverySiteRemoval(WorldObject_EternalCrashSite site)
        {
            if (site == null || !removingSites.TryGetValue(site, out int depth))
            {
                return;
            }

            if (depth > 1)
            {
                removingSites[site] = depth - 1;
            }
            else
            {
                removingSites.Remove(site);
            }
        }

        /// <summary>
        /// Runs before any native map or parent destruction. False cancels that entrypoint;
        /// entries, queues and debt remain owned by their existing managers.
        /// </summary>
        public bool BeginMapRemoval(MapType closingMap)
        {
            if (!IsGroundMap(closingMap))
            {
                return true;
            }

            removingMaps.TryGetValue(closingMap, out int depth);
            removingMaps[closingMap] = depth + 1;
            // A transfer callback may re-enter removal. It must not destroy its source while
            // ownership is between maps; the outer call will recheck every remaining entry.
            if (!preparingRemoval.Add(closingMap))
            {
                return false;
            }

            try
            {
                return TryPrepareMapRemoval(closingMap);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "BeginMapRemoval", null, ex);
                return false;
            }
            finally
            {
                preparingRemoval.Remove(closingMap);
            }
        }

        public void EndMapRemoval(MapType closingMap)
        {
            if (closingMap == null || !removingMaps.TryGetValue(closingMap, out int depth))
            {
                return;
            }

            if (depth > 1)
            {
                removingMaps[closingMap] = depth - 1;
            }
            else
            {
                removingMaps.Remove(closingMap);
            }

            // A failed transfer or another prefix cancelling removal must leave protection
            // and scheduled work intact. The native maps collection proves committed removal.
            if (Find.Maps != null && !Find.Maps.Contains(closingMap))
            {
                RemoveMapProtection(closingMap);
            }
        }

        private bool TryPrepareMapRemoval(MapType closingMap)
        {
            var manager = EternalServiceContainer.Instance?.CorpseManager;
            if (manager == null)
            {
                return true;
            }

            // Include held/unspawned corpses; Spawned was the late callback's loss window.
            var entries = manager.GetAllCorpses()
                .Where(entry => IsSourceOwnedCorpse(entry, closingMap)).ToList();
            bool allRecovered = true;
            foreach (var entry in entries)
            {
                allRecovered &= TryRecoverCorpse(entry, closingMap, closingMap.Parent as WorldObject_EternalCrashSite)
                    && !IsSourceOwnedCorpse(entry, closingMap);
            }

            // Re-query after partial success so the failed subset keeps its source index.
            // Require both accepted delivery and surviving ownership before native teardown.
            return allRecovered && !manager.GetAllCorpses()
                .Any(entry => IsSourceOwnedCorpse(entry, closingMap));
        }

        private bool TryRecoverCorpse(EternalCorpseData entry, MapType closingMap, WorldObject_EternalCrashSite closingSite)
        {
            var manager = EternalServiceContainer.Instance.CorpseManager;
            bool UnavailableMap(MapType map) => map == closingMap || IsRemovalInProgress(map)
                || map.Parent?.Destroyed == true
                || (map.Parent is WorldObject_EternalCrashSite parent && removingSites.ContainsKey(parent));
            foreach (var home in Find.Maps.Where(map => map.IsPlayerHome && !UnavailableMap(map)).ToList())
            {
                try
                {
                    var locations = FindValidSpawnLocations(home, 1);
                    var location = locations.Count > 0 ? locations[0] : CellFinder.RandomEdgeCell(home);
                    if (manager.TryRelocateCorpse(entry.OriginalPawn, home, location))
                    {
                        return !entry.Corpse.Destroyed && entry.Corpse.Spawned && entry.Corpse.Map == home
                            && ReferenceEquals(manager.GetCorpseData(entry.OriginalPawn), entry);
                    }
                }
                catch (Exception ex)
                {
                    EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                        "TryRecoverCorpse.Home", entry.OriginalPawn, ex);
                }
            }
            // A closing unloaded site has no Map, so exclude its world object explicitly.
            bool delivered = SpaceCrashRescueService.DeliverCorpsesToCrashSite(new[] { entry.Corpse },
                closingSite != null ? closingSite.Tile : closingMap.Tile, UnavailableMap,
                site => site == closingSite || site.Destroyed || removingSites.ContainsKey(site)) > 0;
            return delivered && !entry.Corpse.Destroyed
                && ReferenceEquals(manager.GetCorpseData(entry.OriginalPawn), entry);
        }

        private static bool IsSourceOwnedCorpse(EternalCorpseData entry, MapType source)
        {
            if (entry?.OriginalPawn == null || entry.Corpse == null || entry.Corpse.Destroyed)
            {
                return false;
            }

            if (SpaceCrashRescueService.IsDepartingGravshipCargo(entry.Corpse)) { return false; }
            var physicalMap = entry.Corpse.MapHeld;
            return physicalMap == source || (physicalMap == null && entry.CurrentMap == source);
        }

        /// <summary>
        /// Late removal notification: ground recovery has already run before deinitialization.
        /// Retains the existing space-map callback policy and retires committed ground protection.
        /// </summary>
        /// <param name="closingMap">The map being removed</param>
        public void HandleMapRemoval(MapType closingMap)
        {
            if (closingMap == null)
            {
                return;
            }

            if (IsGroundMap(closingMap))
            {
                // MapRemoved runs after deinitialization. Recovery belongs to early prefixes;
                // this callback only retires protection for a committed native removal.
                if (Find.Maps != null && !Find.Maps.Contains(closingMap))
                {
                    RemoveMapProtection(closingMap);
                }
                return;
            }

            try
            {
                var corpseManager = EternalServiceContainer.Instance?.CorpseManager;
                if (corpseManager == null)
                {
                    return;
                }

                var corpses = corpseManager.GetCorpsesOnMap(closingMap)?.ToList();
                if (corpses == null || corpses.Count == 0)
                {
                    return;
                }

                Verse.Map homeMap = Find.AnyPlayerHomeMap;
                if (homeMap == null)
                {
                    EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                        "HandleMapRemoval", null,
                        new InvalidOperationException("No player home map found — cannot rescue Eternal corpses from closing map"));
                    return;
                }

                foreach (var entry in corpses)
                {
                    try
                    {
                        // Idempotent guard: skip already-despawned or destroyed corpses (Pitfall 6)
                        if (entry?.Corpse == null || entry.Corpse.Destroyed || !entry.Corpse.Spawned)
                        {
                            continue;
                        }

                        string pawnName = entry.OriginalPawn?.Name?.ToStringShort ?? "Unknown";

                        // Find a valid target location on the home map
                        var targetLocs = FindValidSpawnLocations(homeMap, 1);
                        IntVec3 targetLoc = targetLocs.Count > 0
                            ? targetLocs[0]
                            : CellFinder.RandomEdgeCell(homeMap);

                        // The manager owns the physical transfer and index commit. A failed
                        // transfer leaves this entry eligible for later cleanup/retry.
                        if (entry.OriginalPawn == null
                            || !corpseManager.TryRelocateCorpse(entry.OriginalPawn, homeMap, targetLoc))
                        {
                            Log.Warning($"[Eternal] HandleMapRemoval: failed to rescue {pawnName}'s corpse");
                            continue;
                        }

                        // Transient message — not a letter (plan requirement)
                        Messages.Message(
                            $"The Eternal corpse of {pawnName} was rescued from a closing map.",
                            entry.Corpse,
                            MessageTypeDefOf.NeutralEvent);

                        if (Eternal_Mod.settings?.debugMode == true)
                        {
                            Log.Message($"[Eternal] HandleMapRemoval: rescued {pawnName}'s corpse to home map at {targetLoc}");
                        }
                    }
                    catch (Exception ex)
                    {
                        EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                            "HandleMapRemoval.RescueCorpse", entry?.OriginalPawn, ex);
                    }
                }

                // Clean up protection state for the closing map
                RemoveMapProtection(closingMap);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "HandleMapRemoval", null, ex);
            }
        }

        /// <summary>
        /// Checks and applies protection to maps containing Eternal corpses.
        /// Also processes any pending teleportations.
        /// </summary>
        public void CheckAndProtectMaps()
        {
            try
            {
                int currentTick = Find.TickManager.TicksGame;

                // Process pending teleports every tick (they have their own trigger timing)
                ProcessPendingTeleports();

                if (currentTick - lastProtectionCheck < PROTECTION_CHECK_INTERVAL)
                {
                    return; // Not time for full protection check yet
                }

                lastProtectionCheck = currentTick;

                // Check all maps
                var allMaps = Find.Maps;
                var mapsToCheck = allMaps.ToList();

                foreach (var map in mapsToCheck)
                {
                    ProcessMapProtection(map);
                }

                // Clean up invalid map entries
                CleanupInvalidMaps();
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "CheckAndProtectMaps", null, ex);
            }
        }

        /// <summary>
        /// Processes protection for a specific map.
        /// </summary>
        /// <param name="map">The map to process</param>
        private void ProcessMapProtection(MapType map)
        {
            try
            {
                if (map == null)
                {
                    return;
                }

                // Check if map contains Eternal corpses
                var eternalCorpses = EternalServiceContainer.Instance.CorpseManager?.GetCorpsesOnMap(map)?.ToList() ?? new List<EternalCorpseData>();
                int corpseCount = eternalCorpses.Count;

                if (corpseCount == 0)
                {
                    // No Eternal corpses, remove protection if it exists
                    RemoveMapProtection(map);
                    return;
                }

                // Check if this is a temporary map
                bool isTemporaryMap = IsTemporaryMap(map);

                if (isTemporaryMap)
                {
                    HandleTemporaryMapWithCorpses(map, eternalCorpses);
                }
                else
                {
                    HandlePermanentMapWithCorpses(map, eternalCorpses);
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "ProcessMapProtection", null, ex);
            }
        }

        /// <summary>
        /// Handles a temporary map that contains Eternal corpses.
        /// </summary>
        /// <param name="map">The temporary map</param>
        /// <param name="eternalCorpses">The Eternal corpses on the map</param>
        private void HandleTemporaryMapWithCorpses(MapType map, List<EternalCorpseData> eternalCorpses)
        {
            try
            {
                // Check if map is already protected
                if (protectedMaps.Contains(map))
                {
                    // Update protection data
                    if (protectionData.TryGetValue(map, out var data))
                    {
                        data.EternalCorpseCount = eternalCorpses.Count;
                    }
                    ScheduleCorpseTeleport(map, eternalCorpses);
                    return;
                }

                ApplyMapProtection(map, eternalCorpses);

                Log.Message($"[Eternal] Applied {MapProtectionStrategy.TeleportToHome} protection to temporary map with {eternalCorpses.Count} Eternal corpses");
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "HandleTemporaryMapWithCorpses", null, ex);
            }
        }

        /// <summary>
        /// Handles a permanent map that contains Eternal corpses.
        /// </summary>
        /// <param name="map">The permanent map</param>
        /// <param name="eternalCorpses">The Eternal corpses on the map</param>
        private void HandlePermanentMapWithCorpses(MapType map, List<EternalCorpseData> eternalCorpses)
        {
            try
            {
                // Permanent maps don't need protection from destruction,
                // but we should ensure corpses are preserved
                foreach (var corpseData in eternalCorpses)
                {
                    if (corpseData?.Corpse != null)
                    {
                        EternalServiceContainer.Instance.CorpsePreservation?.PreserveCorpse(corpseData.Corpse, corpseData.OriginalPawn);
                    }
                }

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Ensured preservation of {eternalCorpses.Count} Eternal corpses on permanent map");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "HandlePermanentMapWithCorpses", null, ex);
            }
        }

        /// <summary>
        /// Protects a map and schedules its corpses for delayed teleportation home.
        /// </summary>
        /// <param name="map">The map to protect</param>
        /// <param name="eternalCorpses">The Eternal corpses on the map</param>
        private void ApplyMapProtection(MapType map, List<EternalCorpseData> eternalCorpses)
        {
            try
            {
                var protectionData = new MapProtectionData
                {
                    Map = map,
                    ProtectionStartTick = Find.TickManager.TicksGame,
                    EternalCorpseCount = eternalCorpses.Count,
                    Strategy = MapProtectionStrategy.TeleportToHome,
                    IsTemporaryMap = IsTemporaryMap(map),
                    OriginalMapPurpose = GetMapPurpose(map)
                };

                this.protectionData[map] = protectionData;
                protectedMaps.Add(map);

                ScheduleCorpseTeleport(map, eternalCorpses);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "ApplyMapProtection", null, ex);
            }
        }

        /// <summary>
        /// Schedules teleportation of corpses to home map.
        /// </summary>
        /// <param name="map">The source map</param>
        /// <param name="eternalCorpses">The corpses to teleport</param>
        private void ScheduleCorpseTeleport(MapType map, List<EternalCorpseData> eternalCorpses)
        {
            try
            {
                eternalCorpses = eternalCorpses.Where(entry => entry?.Corpse != null
                    && !pendingTeleports.Any(pending => pending.Corpse == entry.Corpse)).ToList();
                if (eternalCorpses.Count == 0) { return; }
                Verse.Map homeMap = Find.AnyPlayerHomeMap;
                if (homeMap == null)
                {
                    Log.Warning("[Eternal] No home map found for corpse teleportation");
                    return;
                }

                // Schedule teleportation after a delay
                int delayTicks = 25000; // ~12 hours

                // Find valid spawn locations on home map
                var spawnLocations = FindValidSpawnLocations(homeMap, eternalCorpses.Count);
                if (spawnLocations.Count < eternalCorpses.Count)
                {
                    Log.Warning($"[Eternal] Not enough spawn locations on home map. Found: {spawnLocations.Count}, Needed: {eternalCorpses.Count}");
                }

                // Schedule delayed teleportation
                for (int i = 0; i < eternalCorpses.Count && i < spawnLocations.Count; i++)
                {
                    var corpseData = eternalCorpses[i];
                    var spawnLocation = spawnLocations[i];

                    // Create teleportation job
                    ScheduleDelayedTeleport(corpseData, homeMap, spawnLocation, delayTicks);
                }

                // Notify player
                Messages.Message(
                    new Message($"Eternal corpses will be teleported to home map in approximately {delayTicks / GenDate.TicksPerHour:F1} hours.",
                    MessageTypeDefOf.NeutralEvent));
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "ScheduleCorpseTeleport", null, ex);
            }
        }

        /// <summary>
        /// Finds valid spawn locations on a map.
        /// PERF: Uses random sampling instead of iterating all 62,500+ cells.
        /// </summary>
        /// <param name="map">The map to search</param>
        /// <param name="requiredCount">Number of locations needed</param>
        /// <returns>List of valid spawn locations</returns>
        private List<IntVec3> FindValidSpawnLocations(MapType map, int requiredCount)
        {
            var locations = new List<IntVec3>();

            // PERF: Use random sampling instead of iterating all cells
            // First try with standard sample size
            FindSpawnLocationsRandomSample(map, requiredCount, SPAWN_LOCATION_SAMPLE_SIZE, locations);

            // If not enough found, try with larger sample
            if (locations.Count < requiredCount)
            {
                FindSpawnLocationsRandomSample(map, requiredCount, SPAWN_LOCATION_FALLBACK_SIZE, locations);
            }

            return locations;
        }

        /// <summary>
        /// Finds spawn locations using random sampling.
        /// PERF: O(sampleSize) instead of O(mapCells).
        /// BUGFIX: Uses RimWorld's Rand class for proper randomness and HashSet for O(1) duplicate detection.
        /// </summary>
        private void FindSpawnLocationsRandomSample(MapType map, int requiredCount, int sampleSize, List<IntVec3> locations)
        {
            int mapWidth = map.Size.x;
            int mapHeight = map.Size.z;

            // BUGFIX: Use HashSet for O(1) duplicate detection instead of O(n) List.Contains
            var seen = new HashSet<IntVec3>();
            foreach (var loc in locations)
            {
                seen.Add(loc);
            }

            for (int i = 0; i < sampleSize && locations.Count < requiredCount; i++)
            {
                // BUGFIX: Use RimWorld's Rand class instead of System.Random
                // System.Random uses time-based seed causing identical sequences when called rapidly
                int x = Rand.Range(0, mapWidth);
                int z = Rand.Range(0, mapHeight);
                var cell = new IntVec3(x, 0, z);

                if (!seen.Contains(cell) && IsValidSpawnLocation(map, cell))
                {
                    locations.Add(cell);
                    seen.Add(cell);
                }
            }
        }

        /// <summary>
        /// Checks if a location is valid for spawning corpses.
        /// PERF: Optimized to avoid LINQ allocation in hot path.
        /// </summary>
        /// <param name="map">The map to check</param>
        /// <param name="cell">The cell to check</param>
        /// <returns>True if valid spawn location</returns>
        private bool IsValidSpawnLocation(MapType map, IntVec3 cell)
        {
            if (!cell.InBounds(map))
            {
                return false;
            }

            if (!cell.Standable(map))
            {
                return false;
            }

            if (cell.Roofed(map))
            {
                return false; // Prefer outdoor spawn
            }

            // PERF: Avoid LINQ allocation - use direct iteration
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var pawn = pawns[i];
                if (pawn.Position.DistanceTo(cell) < 10f && pawn.HostileTo(Faction.OfPlayer))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Schedules delayed teleportation of a corpse.
        /// Adds the corpse to the pending teleport queue for processing.
        /// </summary>
        /// <param name="corpseData">The corpse to teleport</param>
        /// <param name="targetMap">The target map</param>
        /// <param name="targetLocation">The target location</param>
        /// <param name="delayTicks">Delay in ticks</param>
        private void ScheduleDelayedTeleport(EternalCorpseData corpseData, Verse.Map targetMap, IntVec3 targetLocation, int delayTicks)
        {
            try
            {
                if (corpseData?.Corpse == null || targetMap == null)
                {
                    Log.Warning("[Eternal] Cannot schedule teleport - corpse or target map is null");
                    return;
                }

                // Check if already scheduled
                if (pendingTeleports.Any(p => p.Corpse == corpseData.Corpse))
                {
                    if (Eternal_Mod.settings?.debugMode == true)
                    {
                        Log.Message($"[Eternal] Corpse already scheduled for teleportation, skipping duplicate");
                    }
                    return;
                }

                var teleportEntry = new PendingTeleport
                {
                    Corpse = corpseData.Corpse,
                    TargetMap = targetMap,
                    TargetLocation = targetLocation,
                    TriggerTick = Find.TickManager.TicksGame + delayTicks
                };

                pendingTeleports.Add(teleportEntry);

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    string pawnName = corpseData.OriginalPawn?.Name?.ToStringShort ?? "Unknown";
                    string mapLabel = targetMap.Parent?.Label ?? "home map";
                    Log.Message($"[Eternal] Scheduled teleportation of {pawnName}'s corpse to {mapLabel} at {targetLocation} in {delayTicks} ticks");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "ScheduleDelayedTeleport", corpseData?.OriginalPawn, ex);
            }
        }

        /// <summary>
        /// Processes all pending teleportations, executing those that have reached their trigger time.
        /// </summary>
        public void ProcessPendingTeleports()
        {
            if (pendingTeleports.Count == 0)
            {
                return;
            }

            var currentTick = Find.TickManager.TicksGame;
            var toRemove = new List<PendingTeleport>();

            foreach (var entry in pendingTeleports)
            {
                // Skip if not yet time
                if (entry.TriggerTick > currentTick)
                {
                    continue;
                }

                // Validate corpse
                if (entry.Corpse == null || entry.Corpse.Destroyed)
                {
                    toRemove.Add(entry);
                    if (Eternal_Mod.settings?.debugMode == true)
                    {
                        Log.Message("[Eternal] Skipping teleport - corpse is null or destroyed");
                    }
                    continue;
                }

                // Validate target map - try to find fallback if invalid
                if (entry.TargetMap == null || !Find.Maps.Contains(entry.TargetMap))
                {
                    entry.TargetMap = Find.AnyPlayerHomeMap;
                    if (entry.TargetMap == null)
                    {
                        Log.Warning("[Eternal] Cannot teleport corpse - no valid target map available");
                        continue;
                    }

                    // Need new spawn location for fallback map
                    var fallbackLocations = FindValidSpawnLocations(entry.TargetMap, 1);
                    if (fallbackLocations.Count > 0)
                    {
                        entry.TargetLocation = fallbackLocations[0];
                    }
                    else
                    {
                        // Last resort: use random edge cell
                        entry.TargetLocation = CellFinder.RandomEdgeCell(entry.TargetMap);
                    }
                }

                // Validate target location - find new one if invalid
                if (!entry.TargetLocation.InBounds(entry.TargetMap) || !entry.TargetLocation.Standable(entry.TargetMap))
                {
                    var newLocations = FindValidSpawnLocations(entry.TargetMap, 1);
                    if (newLocations.Count > 0)
                    {
                        entry.TargetLocation = newLocations[0];
                    }
                    else
                    {
                        entry.TargetLocation = CellFinder.RandomEdgeCell(entry.TargetMap);
                    }
                }

                try
                {
                    if (ExecuteTeleport(entry))
                    {
                        toRemove.Add(entry);
                    }
                }
                catch (Exception ex)
                {
                    EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                        "ExecuteTeleport", null, ex);
                }
            }

            // Clean up processed entries
            foreach (var entry in toRemove)
            {
                pendingTeleports.Remove(entry);
            }
        }

        /// <summary>
        /// Executes the actual teleportation of a corpse.
        /// </summary>
        /// <param name="entry">The pending teleport entry to execute</param>
        private bool ExecuteTeleport(PendingTeleport entry)
        {
            if (entry?.Corpse == null || entry.TargetMap == null)
            {
                return false;
            }

            var corpseManager = EternalServiceContainer.Instance?.CorpseManager;
            var originalPawn = entry.Corpse.InnerPawn;
            if (corpseManager == null || originalPawn == null)
            {
                return false;
            }

            // Store source map for logging before the manager performs the transfer.
            var sourceMap = entry.Corpse.Map;
            string sourceLabel = sourceMap?.Parent?.Label ?? "unknown location";
            string targetLabel = entry.TargetMap.Parent?.Label ?? "home map";

            if (!corpseManager.TryRelocateCorpse(originalPawn, entry.TargetMap, entry.TargetLocation))
            {
                return false;
            }

            string pawnName = originalPawn.Name?.ToStringShort ?? "Unknown";
            Log.Message($"[Eternal] Teleported {pawnName}'s corpse from {sourceLabel} to {targetLabel} at {entry.TargetLocation}");

            Messages.Message(
                $"The corpse of {pawnName} has been teleported to {targetLabel}.",
                entry.Corpse,
                MessageTypeDefOf.NeutralEvent);
            return true;
        }

        /// <summary>
        /// Checks if a map is a temporary map.
        /// </summary>
        /// <param name="map">The map to check</param>
        /// <returns>True if temporary map</returns>
        private bool IsTemporaryMap(MapType map)
        {
            if (map == null)
            {
                return false;
            }

            // Check various indicators of temporary maps
            return map.Parent?.def?.defName?.Contains("Quest") == true ||
                   map.Parent?.def?.defName?.Contains("Caravan") == true ||
                   map.Parent?.def?.defName?.Contains("Site") == true ||
                   !map.IsPlayerHome;
        }

        /// <summary>
        /// Gets the purpose of a map.
        /// </summary>
        /// <param name="map">The map to check</param>
        /// <returns>Map purpose description</returns>
        private string GetMapPurpose(MapType map)
        {
            if (map?.Parent == null)
            {
                return "Unknown";
            }

            return map.Parent.def.defName ?? "Unknown";
        }

        /// <summary>
        /// Removes protection from a map.
        /// </summary>
        /// <param name="map">The map to remove protection from</param>
        private void RemoveMapProtection(MapType map)
        {
            try
            {
                if (protectedMaps.Contains(map))
                {
                    protectedMaps.Remove(map);
                    protectionData.Remove(map);

                    if (Eternal_Mod.settings?.debugMode == true)
                    {
                        Log.Message($"[Eternal] Removed protection from map {map}");
                    }
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "RemoveMapProtection", null, ex);
            }
        }

        /// <summary>
        /// Cleans up invalid map entries.
        /// PERF: Uses pooled list to avoid allocation.
        /// </summary>
        private void CleanupInvalidMaps()
        {
            try
            {
                var toRemove = ListPool<MapType>.Get();
                try
                {
                    foreach (var map in protectedMaps)
                    {
                        if (map == null || !Find.Maps.Contains(map))
                        {
                            toRemove.Add(map);
                        }
                        else
                        {
                            // PERF: Check count directly without allocating a list
                            var corpseManager = EternalServiceContainer.Instance.CorpseManager;
                            bool hasCorpses = corpseManager?.GetCorpsesOnMap(map)?.Any() ?? false;
                            if (!hasCorpses)
                            {
                                toRemove.Add(map);
                            }
                        }
                    }

                    foreach (var map in toRemove)
                    {
                        RemoveMapProtection(map);
                    }
                }
                finally
                {
                    ListPool<MapType>.Return(toRemove);
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "CleanupInvalidMaps", null, ex);
            }
        }

        /// <summary>
        /// Gets protection statistics for debugging.
        /// </summary>
        /// <returns>Dictionary containing protection statistics</returns>
        public Dictionary<string, object> GetProtectionStats()
        {
            var stats = new Dictionary<string, object>
            {
                ["ProtectedMapsCount"] = protectedMaps.Count,
                ["LastProtectionCheck"] = lastProtectionCheck
            };

            // Count by strategy
            var byStrategy = new Dictionary<string, int>();
            foreach (var data in protectionData.Values)
            {
                string strategyName = data.Strategy.ToString();
                byStrategy[strategyName] = byStrategy.TryGetValue(strategyName, out int count) ? count + 1 : 1;
            }
            stats["ProtectedMapsByStrategy"] = byStrategy;

            // Count total corpses in protected maps
            int totalCorpses = 0;
            foreach (var data in protectionData.Values)
            {
                totalCorpses += data.EternalCorpseCount;
            }
            stats["TotalEternalCorpsesProtected"] = totalCorpses;

            // Add pending teleport count
            stats["PendingTeleportsCount"] = pendingTeleports.Count;

            return stats;
        }

        /// <summary>
        /// Saves and loads pending teleport data for save/load persistence.
        /// Called by the parent component's ExposeData.
        /// </summary>
        public void ExposeData()
        {
            Scribe_Collections.Look(ref pendingTeleports, "pendingTeleports", LookMode.Deep);

            if (pendingTeleports == null)
            {
                pendingTeleports = new List<PendingTeleport>();
            }
        }

        /// <summary>
        /// Removes entries whose cross-references could not be resolved. The component calls this
        /// only after the loaded corpse manager and service references are wired.
        /// </summary>
        public void PostLoadInit()
        {
            pendingTeleports.RemoveAll(pendingTeleport =>
                pendingTeleport == null || pendingTeleport.Corpse == null);

            if (Eternal_Mod.settings?.debugMode == true && pendingTeleports.Count > 0)
            {
                Log.Message($"[Eternal] Loaded {pendingTeleports.Count} pending teleportation(s)");
            }
        }

    }
}
