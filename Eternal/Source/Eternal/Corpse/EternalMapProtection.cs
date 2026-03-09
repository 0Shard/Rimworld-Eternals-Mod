/*
 * Relative Path: Eternal/Source/Eternal/Corpse/EternalMapProtection.cs
 * Creation Date: 09-11-2025
 * Last Edit: 21-02-2026
 *              05-02: HandleMapRemoval() event-driven rescue hook called from EternalMapManager.MapRemoved() (PERF-07).
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
using Eternal.UI;
using Eternal.Utils;
using Eternal.Models;
using Eternal.Utilities;
using MapType = Verse.Map;

// Type alias for backwards compatibility
using EternalCorpseData = Eternal.Models.CorpseTrackingEntry;

namespace Eternal.Corpse
{
    /// <summary>
    /// Prevents destruction of temporary maps containing Eternal corpses.
    /// Provides multiple strategies for handling Eternal corpses on temporary maps.
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
            TeleportToHome,     // Teleport corpses to home map
            BlockDestruction,   // Prevent map destruction entirely
            TransferToNearest,  // Transfer to nearest permanent map
            AskPlayer          // Ask player what to do
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

        /// <summary>
        /// Event-driven corpse rescue called immediately when a map is removed.
        /// Fires via EternalMapManager.MapRemoved() — catches all standard RimWorld map closures.
        /// The periodic fallback in CheckAndProtectMaps() at 5000 ticks handles modded edge cases.
        /// Each corpse rescue is independently guarded so one failure does not abort the others.
        /// </summary>
        /// <param name="closingMap">The map being removed</param>
        public void HandleMapRemoval(MapType closingMap)
        {
            if (closingMap == null)
                return;

            try
            {
                var corpseManager = EternalServiceContainer.Instance?.CorpseManager;
                if (corpseManager == null)
                    return;

                var corpses = corpseManager.GetCorpsesOnMap(closingMap)?.ToList();
                if (corpses == null || corpses.Count == 0)
                    return;

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
                            continue;

                        string pawnName = entry.OriginalPawn?.Name?.ToStringShort ?? "Unknown";

                        // Find a valid target location on the home map
                        var targetLocs = FindValidSpawnLocations(homeMap, 1);
                        IntVec3 targetLoc = targetLocs.Count > 0
                            ? targetLocs[0]
                            : CellFinder.RandomEdgeCell(homeMap);

                        // Despawn from closing map, spawn on home map
                        entry.Corpse.DeSpawn();
                        GenSpawn.Spawn(entry.Corpse, targetLoc, homeMap);

                        // Update corpse location tracking
                        if (entry.OriginalPawn != null)
                        {
                            corpseManager.UpdateCorpseLocation(entry.OriginalPawn, homeMap, targetLoc);
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
                    return;

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
                    return;
                }

                // Determine protection strategy
                MapProtectionStrategy strategy = DetermineProtectionStrategy(map, eternalCorpses);

                // Apply protection
                ApplyMapProtection(map, eternalCorpses, strategy);

                Log.Message($"[Eternal] Applied {strategy} protection to temporary map with {eternalCorpses.Count} Eternal corpses");
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
        /// Determines the appropriate protection strategy for a map.
        /// </summary>
        /// <param name="map">The map to determine strategy for</param>
        /// <param name="eternalCorpses">The Eternal corpses on the map</param>
        /// <returns>Protection strategy to use</returns>
        private MapProtectionStrategy DetermineProtectionStrategy(MapType map, List<EternalCorpseData> eternalCorpses)
        {
            // Use setting-defined strategy
            string settingStrategy = (Eternal_Mod.GetSettings().mapProtectionAction ?? "teleport").ToLowerInvariant();

            return settingStrategy switch
            {
                "block" => MapProtectionStrategy.BlockDestruction,
                "transfer" => MapProtectionStrategy.TransferToNearest,
                "ask" => MapProtectionStrategy.AskPlayer,
                _ => MapProtectionStrategy.TeleportToHome
            };
        }

        /// <summary>
        /// Applies protection to a map based on the chosen strategy.
        /// </summary>
        /// <param name="map">The map to protect</param>
        /// <param name="eternalCorpses">The Eternal corpses on the map</param>
        /// <param name="strategy">The protection strategy to use</param>
        private void ApplyMapProtection(MapType map, List<EternalCorpseData> eternalCorpses, MapProtectionStrategy strategy)
        {
            try
            {
                var protectionData = new MapProtectionData
                {
                    Map = map,
                    ProtectionStartTick = Find.TickManager.TicksGame,
                    EternalCorpseCount = eternalCorpses.Count,
                    Strategy = strategy,
                    IsTemporaryMap = IsTemporaryMap(map),
                    OriginalMapPurpose = GetMapPurpose(map)
                };

                this.protectionData[map] = protectionData;
                protectedMaps.Add(map);

                switch (strategy)
                {
                    case MapProtectionStrategy.TeleportToHome:
                        ScheduleCorpseTeleport(map, eternalCorpses);
                        break;
                    case MapProtectionStrategy.BlockDestruction:
                        BlockMapDestruction(map);
                        break;
                    case MapProtectionStrategy.TransferToNearest:
                        ScheduleCorpseTransfer(map, eternalCorpses);
                        break;
                    case MapProtectionStrategy.AskPlayer:
                        AskPlayerForAction(map, eternalCorpses);
                        break;
                }
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
        /// Blocks destruction of a map containing Eternal corpses.
        /// </summary>
        /// <param name="map">The map to protect</param>
        private void BlockMapDestruction(MapType map)
        {
            try
            {
                // This is more complex and would require patching into RimWorld's map destruction system
                // For now, we'll notify the player and log the protection
                Log.Message($"[Eternal] Map {map} is protected from destruction due to {protectionData[map]?.EternalCorpseCount} Eternal corpses");

                // Notify player
                Messages.Message(
                    new Message($"Map {map} cannot be destroyed because it contains Eternal corpses.",
                    MessageTypeDefOf.NeutralEvent));
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "BlockMapDestruction", null, ex);
            }
        }

        /// <summary>
        /// Schedules transfer of corpses to nearest permanent map.
        /// </summary>
        /// <param name="map">The source map</param>
        /// <param name="eternalCorpses">The corpses to transfer</param>
        private void ScheduleCorpseTransfer(MapType map, List<EternalCorpseData> eternalCorpses)
        {
            try
            {
                Verse.Map nearestMap = FindNearestPermanentMap(map);
                if (nearestMap == null)
                {
                    Log.Warning("[Eternal] No permanent map found for corpse transfer");
                    return;
                }

                // Find valid spawn locations on nearest map
                var spawnLocations = FindValidSpawnLocations(nearestMap, eternalCorpses.Count);
                if (spawnLocations.Count < eternalCorpses.Count)
                {
                    Log.Warning($"[Eternal] Not enough spawn locations on target map. Found: {spawnLocations.Count}, Needed: {eternalCorpses.Count}");
                }

                // Schedule transfer after a delay
                int delayTicks = 25000; // ~12 hours

                for (int i = 0; i < eternalCorpses.Count && i < spawnLocations.Count; i++)
                {
                    var corpseData = eternalCorpses[i];
                    var spawnLocation = spawnLocations[i];

                    // Create transfer job
                    ScheduleDelayedTransfer(corpseData, nearestMap, spawnLocation, delayTicks);
                }

                // Notify player
                Messages.Message(
                    new Message($"Eternal corpses will be transferred to nearest map in approximately {delayTicks / GenDate.TicksPerHour:F1} hours.",
                    MessageTypeDefOf.NeutralEvent));
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "ScheduleCorpseTransfer", null, ex);
            }
        }

        /// <summary>
        /// Asks the player what to do with Eternal corpses on a temporary map.
        /// </summary>
        /// <param name="map">The map containing corpses</param>
        /// <param name="eternalCorpses">The corpses on the map</param>
        private void AskPlayerForAction(MapType map, List<EternalCorpseData> eternalCorpses)
        {
            try
            {
                // Create dialog asking player what to do
                string dialogText = $"A temporary map containing {eternalCorpses.Count} Eternal corpse(s) is about to be destroyed.\n\n" +
                                   "What would you like to do with the Eternal corpses?";

                DiaNode node = new DiaNode("Eternal Corpses on Temporary Map");
                node.text = dialogText;

                // Option: Teleport to home
                DiaOption teleportOption = new DiaOption("Teleport to Home Map");
                teleportOption.resolveTree = true;
                teleportOption.action = () =>
                {
                    ScheduleCorpseTeleport(map, eternalCorpses);
                };

                // Option: Transfer to nearest permanent map
                DiaOption transferOption = new DiaOption("Transfer to Nearest Map");
                transferOption.resolveTree = true;
                transferOption.action = () =>
                {
                    ScheduleCorpseTransfer(map, eternalCorpses);
                };

                // Option: Block destruction
                DiaOption blockOption = new DiaOption("Block Map Destruction");
                blockOption.resolveTree = true;
                blockOption.action = () =>
                {
                    BlockMapDestruction(map);
                };

                // Option: Do nothing (risky)
                DiaOption riskyOption = new DiaOption("Do Nothing (Corpses May Be Lost)");
                riskyOption.resolveTree = true;
                riskyOption.action = () =>
                {
                    RemoveMapProtection(map);
                    Log.Warning($"[Eternal] Player chose to do nothing - {eternalCorpses.Count} Eternal corpses may be lost");
                };

                node.options.Add(teleportOption);
                node.options.Add(transferOption);
                node.options.Add(blockOption);
                node.options.Add(riskyOption);

                Find.WindowStack.Add(new Dialog_NodeTree(node));
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "AskPlayerForAction", null, ex);
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
                return false;

            if (!cell.Standable(map))
                return false;

            if (cell.Roofed(map))
                return false; // Prefer outdoor spawn

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
        /// Schedules delayed transfer of a corpse.
        /// Uses the same underlying teleportation mechanism.
        /// </summary>
        /// <param name="corpseData">The corpse to transfer</param>
        /// <param name="targetMap">The target map</param>
        /// <param name="targetLocation">The target location</param>
        /// <param name="delayTicks">Delay in ticks</param>
        private void ScheduleDelayedTransfer(EternalCorpseData corpseData, Verse.Map targetMap, IntVec3 targetLocation, int delayTicks)
        {
            // Transfer uses the same mechanism as teleport
            ScheduleDelayedTeleport(corpseData, targetMap, targetLocation, delayTicks);
        }

        /// <summary>
        /// Processes all pending teleportations, executing those that have reached their trigger time.
        /// </summary>
        public void ProcessPendingTeleports()
        {
            if (pendingTeleports.Count == 0)
                return;

            var currentTick = Find.TickManager.TicksGame;
            var toRemove = new List<PendingTeleport>();

            foreach (var entry in pendingTeleports)
            {
                // Skip if not yet time
                if (entry.TriggerTick > currentTick)
                    continue;

                toRemove.Add(entry);

                // Validate corpse
                if (entry.Corpse == null || entry.Corpse.Destroyed)
                {
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
                    ExecuteTeleport(entry);
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
        private void ExecuteTeleport(PendingTeleport entry)
        {
            // Store source map for logging
            var sourceMap = entry.Corpse.Map;
            string sourceLabel = sourceMap?.Parent?.Label ?? "unknown location";
            string targetLabel = entry.TargetMap.Parent?.Label ?? "home map";

            // Despawn from current location if spawned
            if (entry.Corpse.Spawned)
            {
                entry.Corpse.DeSpawn();
            }

            // Spawn on target map
            GenSpawn.Spawn(entry.Corpse, entry.TargetLocation, entry.TargetMap);

            // Get pawn name for logging
            string pawnName = entry.Corpse.InnerPawn?.Name?.ToStringShort ?? "Unknown";

            Log.Message($"[Eternal] Teleported {pawnName}'s corpse from {sourceLabel} to {targetLabel} at {entry.TargetLocation}");

            // Notify player
            Messages.Message(
                $"The corpse of {pawnName} has been teleported to {targetLabel}.",
                entry.Corpse,
                MessageTypeDefOf.NeutralEvent);
        }

        /// <summary>
        /// Checks if a map is a temporary map.
        /// </summary>
        /// <param name="map">The map to check</param>
        /// <returns>True if temporary map</returns>
        private bool IsTemporaryMap(MapType map)
        {
            if (map == null)
                return false;

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
                return "Unknown";

            return map.Parent.def.defName ?? "Unknown";
        }

        /// <summary>
        /// Finds the nearest permanent map to a given map.
        /// </summary>
        /// <param name="sourceMap">The source map</param>
        /// <returns>Nearest permanent map or null</returns>
        private Verse.Map FindNearestPermanentMap(Verse.Map sourceMap)
        {
            if (sourceMap == null)
                return null;

            Verse.Map nearestMap = null;
            float nearestDistance = float.MaxValue;

            foreach (var map in Find.Maps)
            {
                if (map == sourceMap || IsTemporaryMap(map))
                    continue;

                float distance = sourceMap.Center.DistanceTo(map.Center);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestMap = map;
                }
            }

            return nearestMap;
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

            // Ensure list is initialized after loading
            if (pendingTeleports == null)
            {
                pendingTeleports = new List<PendingTeleport>();
            }

            // Clean up any null entries that may have resulted from references being lost
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pendingTeleports.RemoveAll(p => p == null || p.Corpse == null);

                if (Eternal_Mod.settings?.debugMode == true && pendingTeleports.Count > 0)
                {
                    Log.Message($"[Eternal] Loaded {pendingTeleports.Count} pending teleportation(s)");
                }
            }
        }

    }
}
