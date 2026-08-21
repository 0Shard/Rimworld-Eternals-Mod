/*
 * Relative Path: Eternal/Source/Eternal/Corpse/EternalCorpseManager.cs
 * Creation Date: 09-11-2025
 * Last Edit: 16-07-2026
 * Author: 0Shard
 * Description: Manages dead Eternal corpses globally, including save/load indexes,
 *              failure-safe relocation, and resurrection lifecycle state.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using MapType = Verse.Map;
using Eternal.Exceptions;
using Eternal.Extensions;
using Eternal.Healing;
using Eternal.Utils;
using Eternal.Models;

// Type alias for backwards compatibility
using EternalCorpseData = Eternal.Models.CorpseTrackingEntry;

namespace Eternal.Corpse
{
    /// <summary>
    /// Global manager for tracking all Eternal corpses across all maps.
    /// Provides centralized access to corpse information and handles corpse lifecycle events.
    /// Implements IExposable for save/load persistence.
    /// Access via EternalServiceContainer.Instance.CorpseManager.
    /// </summary>
    public class EternalCorpseManager : IExposable
    {
        private Dictionary<Pawn, CorpseTrackingEntry> trackedCorpses = new Dictionary<Pawn, CorpseTrackingEntry>();
        private Dictionary<MapType, HashSet<Pawn>> corpsesByMap = new Dictionary<MapType, HashSet<Pawn>>();
        private List<CorpseTrackingEntry> serializedCorpseEntries;
        private bool corpseIndexesRebuiltAfterLoad;
        private bool mapIndexHydrated = true;

        /// <summary>
        /// Corpses whose upcoming destruction is an expected part of a mod-controlled operation
        /// (ResurrectionUtility.TryResurrect consumes the corpse while it is still tracked).
        /// Consulted by Corpse_Destroy_Patch to let those destroys through while blocking
        /// silent third-party container sweeps. Transient by design (never scribed): the mark
        /// only lives for the duration of a single TryResurrect call.
        /// </summary>
        private readonly HashSet<Verse.Corpse> expectedDestructions = new HashSet<Verse.Corpse>();

        /// <summary>
        /// Default constructor for new instances and IExposable deserialization.
        /// </summary>
        public EternalCorpseManager()
        {
            trackedCorpses = new Dictionary<Pawn, CorpseTrackingEntry>();
            corpsesByMap = new Dictionary<MapType, HashSet<Pawn>>();
            corpseIndexesRebuiltAfterLoad = true;
        }

        /// <summary>
        /// Serializes/deserializes corpse tracking data for save/load.
        /// Uses Scribe_Collections for dictionary serialization.
        /// </summary>
        public void ExposeData()
        {
            // Keep the deep-loaded list on the manager. References inside each entry are not
            // resolved during LoadingVars, so rebuilding either dictionary in that phase loses
            // every entry whose Pawn or Corpse reference is still deferred.
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                serializedCorpseEntries = trackedCorpses.Values.ToList();
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                serializedCorpseEntries = null;
                corpseIndexesRebuiltAfterLoad = false;
                mapIndexHydrated = false;
            }

            Scribe_Collections.Look(ref serializedCorpseEntries, "trackedCorpses", LookMode.Deep);

            // Scribe invokes PostLoadInit after nested references have been resolved. Rebuild
            // both indexes once, from the fully hydrated entries, and never during LoadingVars.
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                RebuildIndexesAfterLoad();
            }
        }

        /// <summary>
        /// Rebuilds the pawn and map indexes after Scribe has resolved all entry references.
        /// Idempotence prevents duplicate work if a load harness invokes the post-load hook twice.
        /// </summary>
        public void RebuildIndexesAfterLoad()
        {
            if (corpseIndexesRebuiltAfterLoad)
                return;

            trackedCorpses = new Dictionary<Pawn, CorpseTrackingEntry>();
            corpsesByMap = new Dictionary<MapType, HashSet<Pawn>>();

            int loadedCount = 0;
            int skippedCount = 0;

            if (serializedCorpseEntries != null)
            {
                foreach (var entry in serializedCorpseEntries)
                {
                    if (entry?.OriginalPawn != null && entry.IsValid())
                    {
                        trackedCorpses[entry.OriginalPawn] = entry;
                        AddPawnToMapIndex(entry.CurrentMap, entry.OriginalPawn);
                        loadedCount++;
                    }
                    else
                    {
                        skippedCount++;
                        string pawnName = entry?.OriginalPawn?.Name?.ToStringShort ?? "null";
                        bool isValid = entry?.IsValid() ?? false;
                        Log.Warning($"[Eternal] Skipped orphaned corpse entry on load: Pawn={pawnName}, IsValid={isValid}");
                    }
                }
            }

            corpseIndexesRebuiltAfterLoad = true;
            mapIndexHydrated = AreMapIndexesEquivalentToScan();

            if (skippedCount > 0)
            {
                Log.Warning($"[Eternal] {skippedCount} corpse entries were orphaned/invalid and skipped during load");
            }

            if (Eternal_Mod.settings?.debugMode == true)
            {
                Log.Message($"[Eternal] Loaded {loadedCount} tracked Eternal corpses");
            }

            // Reset rot progress for all tracked corpses to handle saves from before the rot fix.
            ResetAllRotProgress();
            serializedCorpseEntries = null;
        }

        /// <summary>
        /// Registers a new Eternal corpse in the tracking system.
        /// </summary>
        /// <param name="corpse">The corpse object to track</param>
        /// <param name="originalPawn">The original pawn before death</param>
        /// <param name="assignmentSnapshot">Optional snapshot of work priorities, policies, and schedule captured at death</param>
        /// <param name="preCalculatedQueue">Optional pre-calculated healing queue captured at death before RimWorld removes injuries</param>
        public void RegisterCorpse(Verse.Corpse corpse, Pawn originalPawn, PawnAssignmentSnapshot assignmentSnapshot = null, List<HealingItem> preCalculatedQueue = null)
        {
            try
            {
                if (corpse == null || originalPawn == null)
                {
                    Log.Warning("[Eternal] Cannot register null corpse or pawn");
                    return;
                }

                if (!originalPawn.IsValidEternalCorpse())
                {
                    Log.Warning($"[Eternal] Attempted to register non-Eternal pawn for resurrection: {originalPawn.Name}");
                    return;
                }

                if (trackedCorpses.TryGetValue(originalPawn, out var previousEntry))
                {
                    RemovePawnFromMapIndex(previousEntry.CurrentMap, originalPawn);
                }

                var corpseData = new CorpseTrackingEntry(corpse, originalPawn, corpse.Map, corpse.Position);

                // Store the assignment snapshot for restoration after resurrection
                corpseData.AssignmentSnapshot = assignmentSnapshot;

                // Store the pre-calculated healing queue (captured at death before RimWorld removes injuries)
                if (preCalculatedQueue != null && preCalculatedQueue.Count > 0)
                {
                    corpseData.PreCalculatedHealingQueue = preCalculatedQueue;
                }

                trackedCorpses[originalPawn] = corpseData;

                // Track by map only after the entry is owned by this manager.
                AddPawnToMapIndex(corpse.Map, originalPawn);

                // Add corpse component
                AddCorpseComponent(corpse, corpseData);

                Log.Message($"[Eternal] Registered corpse for {originalPawn.Name} at {corpse.Position}");
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "RegisterCorpse", originalPawn, ex);
            }
        }

        /// <summary>
        /// Unregisters a corpse from the tracking system (typically after resurrection).
        /// </summary>
        /// <param name="originalPawn">The original pawn to unregister</param>
        public void UnregisterCorpse(Pawn originalPawn)
        {
            try
            {
                if (!trackedCorpses.TryGetValue(originalPawn, out var corpseData))
                {
                    Log.Warning($"[Eternal] Attempted to unregister untracked pawn: {originalPawn.Name}");
                    return;
                }

                RemovePawnFromMapIndex(corpseData.CurrentMap, originalPawn);

                // Remove corpse component
                RemoveCorpseComponent(corpseData.Corpse);

                trackedCorpses.Remove(originalPawn);

                Log.Message($"[Eternal] Unregistered corpse for {originalPawn.Name}");
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "UnregisterCorpse", originalPawn, ex);
            }
        }

        /// <summary>
        /// Gets corpse data for a specific pawn.
        /// </summary>
        /// <param name="pawn">The pawn to get data for</param>
        /// <returns>Corpse data or null if not found</returns>
        public CorpseTrackingEntry GetCorpseData(Pawn pawn)
        {
            return trackedCorpses.TryGetValue(pawn, out var data) ? data : null;
        }

        /// <summary>
        /// Checks if a pawn is being tracked as a corpse.
        /// </summary>
        /// <param name="pawn">The pawn to check</param>
        /// <returns>True if the pawn is tracked</returns>
        public bool IsTracked(Pawn pawn)
        {
            return pawn != null && trackedCorpses.ContainsKey(pawn);
        }

        /// <summary>
        /// Marks a corpse's upcoming destruction as expected (mod-controlled), so
        /// Corpse_Destroy_Patch does not block it. Always pair with UnmarkExpectedDestruction
        /// in a finally block.
        /// </summary>
        public void MarkExpectedDestruction(Verse.Corpse corpse)
        {
            if (corpse != null)
            {
                expectedDestructions.Add(corpse);
            }
        }

        /// <summary>
        /// Removes the expected-destruction mark set by MarkExpectedDestruction.
        /// </summary>
        public void UnmarkExpectedDestruction(Verse.Corpse corpse)
        {
            if (corpse != null)
            {
                expectedDestructions.Remove(corpse);
            }
        }

        /// <summary>
        /// Checks whether a corpse's destruction was marked as expected.
        /// </summary>
        public bool IsExpectedDestruction(Verse.Corpse corpse)
        {
            return corpse != null && expectedDestructions.Contains(corpse);
        }

        /// <summary>
        /// Gets all Eternal corpses on a specific map.
        /// </summary>
        /// <param name="map">The map to check</param>
        /// <returns>Collection of corpses on the map</returns>
        public IEnumerable<CorpseTrackingEntry> GetCorpsesOnMap(MapType map)
        {
            if (!mapIndexHydrated)
            {
                return GetCorpsesOnMapByScan(map);
            }

            if (!corpsesByMap.TryGetValue(map, out var mapCorpses))
            {
                return Enumerable.Empty<EternalCorpseData>();
            }

            return mapCorpses
                .Where(trackedCorpses.ContainsKey)
                .Select(trackedPawn => trackedCorpses[trackedPawn]);
        }

        /// <summary>
        /// Scans authoritative entry state while the map index is being hydrated or verified.
        /// This remains the correctness fallback until indexed and scanned results agree.
        /// </summary>
        private IEnumerable<CorpseTrackingEntry> GetCorpsesOnMapByScan(MapType map)
        {
            foreach (var corpseData in trackedCorpses.Values)
            {
                if (corpseData?.OriginalPawn == null || corpseData.Corpse == null || corpseData.Corpse.Destroyed)
                    continue;

                if (corpseData.CurrentMap == map || corpseData.Corpse.Map == map)
                {
                    yield return corpseData;
                }
            }
        }

        /// <summary>
        /// Checks if a map contains any Eternal corpses.
        /// </summary>
        /// <param name="map">The map to check</param>
        /// <returns>True if the map contains Eternal corpses</returns>
        public bool HasEternalCorpses(Verse.Map map)
        {
            return GetCorpsesOnMap(map).Any();
        }

        /// <summary>
        /// Gets all tracked Eternal corpses globally.
        /// </summary>
        /// <returns>All tracked corpse data</returns>
        public IEnumerable<CorpseTrackingEntry> GetAllCorpses()
        {
            return trackedCorpses.Values;
        }

        /// <summary>
        /// Gets the total count of tracked corpses.
        /// </summary>
        public int TrackedCount => trackedCorpses.Count;

        /// <summary>
        /// Returns the count of tracked corpses that are actively being healed.
        /// Used by the Effects settings tab for live population count display.
        /// Only counts entries where IsHealingActive is true — excludes corpses awaiting
        /// player activation ("Resurrect Eternal" gizmo not yet clicked).
        /// </summary>
        public int GetHealingCorpseCount()
        {
            int healingCount = 0;
            foreach (var entry in trackedCorpses.Values)
            {
                if (entry.IsHealingActive)
                    healingCount++;
            }
            return healingCount;
        }

        /// <summary>
        /// Relocates a tracked corpse and commits its entry/index changes only after the
        /// corpse is spawned on the requested map. A failed spawn attempts to restore the
        /// original ownership before touching either index.
        /// </summary>
        /// <param name="pawn">The pawn whose corpse moves.</param>
        /// <param name="targetMap">The map that should own the corpse.</param>
        /// <param name="targetPosition">The position on the target map.</param>
        /// <returns>True when the target map owns the spawned corpse.</returns>
        public bool TryRelocateCorpse(Pawn pawn, MapType targetMap, IntVec3 targetPosition)
        {
            if (!trackedCorpses.TryGetValue(pawn, out var corpseData)
                || corpseData?.Corpse == null
                || corpseData.Corpse.Destroyed
                || targetMap == null
                || !targetPosition.InBounds(targetMap))
            {
                return false;
            }

            var corpse = corpseData.Corpse;
            // GenSpawn.Spawn removes holdingOwner immediately before SpawnSetup. Capture the
            // exact ThingOwner before that call so a thrown SpawnSetup can be rolled back.
            ThingOwner sourceOwner = corpse.holdingOwner;
            MapType sourceMap = corpse.Spawned ? corpse.Map : null;
            IntVec3 sourcePosition = corpse.Spawned ? corpse.Position : IntVec3.Invalid;

            if (corpse.Spawned && corpse.Map == targetMap && corpse.Position == targetPosition)
            {
                CommitCorpseLocation(corpseData, targetMap, targetPosition);
                return true;
            }

            try
            {
                if (corpse.Spawned)
                {
                    corpse.DeSpawn(DestroyMode.WillReplace);
                }

                GenSpawn.Spawn(corpse, targetPosition, targetMap);
                if (!corpse.Spawned || corpse.Map != targetMap || corpse.Position != targetPosition)
                {
                    throw new InvalidOperationException("Corpse did not become owned by the target map after spawn.");
                }

                CommitCorpseLocation(corpseData, targetMap, targetPosition);
                return true;
            }
            catch (Exception ex)
            {
                bool ownershipRestored = TryRestoreCorpseOwnership(
                    corpse, sourceOwner, sourceMap, sourcePosition);

                if (corpse.Destroyed)
                {
                    UnregisterCorpse(pawn);
                }
                else if (!ownershipRestored && !corpse.Spawned)
                {
                    // A destroyed/unspawned corpse must never remain in an old map bucket.
                    RemovePawnFromMapIndex(corpseData.CurrentMap, pawn);
                    corpseData.UpdateLocation(null, IntVec3.Invalid);
                }

                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "TryRelocateCorpse", pawn, ex);
                return false;
            }
        }

        /// <summary>
        /// Commits a tracked corpse's transfer to an unspawned owner such as a caravan,
        /// crash-site container, or ship cargo. The callback owns the external transfer;
        /// this manager updates the entry/index only after it reports successful ownership.
        /// </summary>
        public bool TryReleaseCorpseToUnspawnedOwner(Pawn pawn, Func<bool> transferOwnership)
        {
            if (!trackedCorpses.TryGetValue(pawn, out var corpseData)
                || corpseData?.Corpse == null
                || corpseData.Corpse.Destroyed
                || transferOwnership == null)
            {
                return false;
            }

            var corpse = corpseData.Corpse;
            // The callback can remove the corpse from its holder before reporting failure, so
            // capture the exact owner before invoking external ownership code.
            ThingOwner sourceOwner = corpse.holdingOwner;
            MapType sourceMap = corpse.Spawned ? corpse.Map : null;
            IntVec3 sourcePosition = corpse.Spawned ? corpse.Position : IntVec3.Invalid;

            try
            {
                bool transferSucceeded = transferOwnership();
                if (!transferSucceeded || corpse.Destroyed || corpse.Spawned)
                {
                    if (corpse.Destroyed)
                    {
                        UnregisterCorpse(pawn);
                    }
                    else if (!corpse.Spawned)
                    {
                        bool ownershipRestored = TryRestoreCorpseOwnership(
                            corpse, sourceOwner, sourceMap, sourcePosition);
                        if (!ownershipRestored)
                        {
                            RemovePawnFromMapIndex(corpseData.CurrentMap, pawn);
                            corpseData.UpdateLocation(null, IntVec3.Invalid);
                        }
                    }
                    return false;
                }

                CommitCorpseLocation(corpseData, null, IntVec3.Invalid);
                return true;
            }
            catch (Exception ex)
            {
                bool ownershipRestored = TryRestoreCorpseOwnership(
                    corpse, sourceOwner, sourceMap, sourcePosition);
                if (corpse.Destroyed)
                {
                    UnregisterCorpse(pawn);
                }
                else if (!ownershipRestored && !corpse.Spawned)
                {
                    RemovePawnFromMapIndex(corpseData.CurrentMap, pawn);
                    corpseData.UpdateLocation(null, IntVec3.Invalid);
                }

                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "TryReleaseCorpseToUnspawnedOwner", pawn, ex);
                return false;
            }
        }

        /// <summary>
        /// Compatibility boundary for external integrations that already performed a spawn.
        /// It accepts the update only when the corpse's physical ownership matches the request.
        /// New mod-controlled moves should call TryRelocateCorpse instead.
        /// </summary>
        public void UpdateCorpseLocation(Pawn pawn, MapType newMap, IntVec3 newPosition)
        {
            if (!trackedCorpses.TryGetValue(pawn, out var corpseData)
                || corpseData?.Corpse == null
                || corpseData.Corpse.Destroyed)
            {
                return;
            }

            bool ownershipMatches = newMap == null
                ? !corpseData.Corpse.Spawned
                : corpseData.Corpse.Spawned && corpseData.Corpse.Map == newMap;
            if (!ownershipMatches)
            {
                return;
            }

            CommitCorpseLocation(corpseData, newMap, newPosition);
        }

        private void CommitCorpseLocation(CorpseTrackingEntry corpseData, MapType newMap, IntVec3 newPosition)
        {
            if (corpseData == null || corpseData.OriginalPawn == null)
                return;

            RemovePawnFromMapIndex(corpseData.CurrentMap, corpseData.OriginalPawn);
            corpseData.UpdateLocation(newMap, newPosition);
            AddPawnToMapIndex(newMap, corpseData.OriginalPawn);
        }

        private void AddPawnToMapIndex(MapType map, Pawn pawn)
        {
            if (map == null || pawn == null)
                return;

            if (!corpsesByMap.TryGetValue(map, out var mapCorpses))
            {
                mapCorpses = new HashSet<Pawn>();
                corpsesByMap[map] = mapCorpses;
            }

            mapCorpses.Add(pawn);
        }

        private void RemovePawnFromMapIndex(MapType map, Pawn pawn)
        {
            if (map == null || pawn == null || !corpsesByMap.TryGetValue(map, out var mapCorpses))
                return;

            mapCorpses.Remove(pawn);
            if (mapCorpses.Count == 0)
            {
                corpsesByMap.Remove(map);
            }
        }

        private bool AreMapIndexesEquivalentToScan()
        {
            foreach (var corpseData in trackedCorpses.Values)
            {
                if (corpseData?.OriginalPawn == null || corpseData.Corpse == null || corpseData.Corpse.Destroyed)
                    continue;

                if (corpseData.Corpse.Spawned && corpseData.CurrentMap != corpseData.Corpse.Map)
                {
                    return false;
                }

                var authoritativeMap = corpseData.Corpse.Map ?? corpseData.CurrentMap;
                if (authoritativeMap != null
                    && (!corpsesByMap.TryGetValue(authoritativeMap, out var mapCorpses)
                        || !mapCorpses.Contains(corpseData.OriginalPawn)))
                {
                    return false;
                }
            }

            foreach (var mapIndex in corpsesByMap)
            {
                foreach (var pawn in mapIndex.Value)
                {
                    if (!trackedCorpses.TryGetValue(pawn, out var corpseData)
                        || corpseData?.CurrentMap != mapIndex.Key)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool TryRestoreCorpseOwnership(
            Verse.Corpse corpse,
            ThingOwner sourceOwner,
            MapType sourceMap,
            IntVec3 sourcePosition)
        {
            if (corpse == null || corpse.Destroyed)
                return false;

            try
            {
                if (corpse.Spawned)
                {
                    corpse.DeSpawn(DestroyMode.WillReplace);
                }
            }
            catch (Exception despawnException)
            {
                // A third-party SpawnSetup failure can leave partial map state. Still attempt
                // the exact ThingOwner restore; leaving the corpse ownerless is the unsafe path.
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "TryRelocateCorpse.RestoreSourceDespawn", corpse.InnerPawn, despawnException);
            }

            if (sourceOwner != null)
            {
                if (corpse.holdingOwner == sourceOwner)
                    return true;

                try
                {
                    if (sourceOwner.TryAddOrTransfer(corpse, canMergeWithExistingStacks: false))
                    {
                        return true;
                    }
                }
                catch (Exception ownerException)
                {
                    EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                        "TryRelocateCorpse.RestoreSourceOwner", corpse.InnerPawn, ownerException);
                }
            }

            if (sourceMap == null || !sourcePosition.IsValid || !sourcePosition.InBounds(sourceMap))
                return false;

            try
            {
                var restoredCorpse = GenSpawn.Spawn(corpse, sourcePosition, sourceMap);
                return restoredCorpse == corpse
                    && corpse.Spawned
                    && corpse.Map == sourceMap;
            }
            catch (Exception restoreException)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "TryRelocateCorpse.RestoreSourceMap", corpse.InnerPawn, restoreException);
                return false;
            }
        }

        /// <summary>
        /// Adds the EternalCorpseComponent to a corpse object.
        /// </summary>
        /// <param name="corpse">The corpse to add component to</param>
        /// <param name="corpseData">The corpse data to store</param>
        private void AddCorpseComponent(Verse.Corpse corpse, CorpseTrackingEntry corpseData)
        {
            try
            {
                if (corpse == null) return;

                var component = corpse.GetComp<EternalCorpseComponent>();
                if (component == null)
                {
                    component = new EternalCorpseComponent();
                    corpse.AllComps.Add(component);
                }

                component.CorpseData = corpseData;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "AddCorpseComponent", corpseData?.OriginalPawn, ex);
            }
        }

        /// <summary>
        /// Removes the EternalCorpseComponent from a corpse object.
        /// </summary>
        /// <param name="corpse">The corpse to remove component from</param>
        private void RemoveCorpseComponent(Verse.Corpse corpse)
        {
            try
            {
                if (corpse == null) return;

                var component = corpse.GetComp<EternalCorpseComponent>();
                if (component != null)
                {
                    corpse.AllComps.Remove(component);
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "RemoveCorpseComponent", null, ex);
            }
        }

        /// <summary>
        /// Cleans up invalid or destroyed corpses from tracking.
        /// Should be called periodically to maintain data integrity.
        /// </summary>
        public void CleanupInvalidCorpses()
        {
            var toRemove = new List<Pawn>();

            foreach (var kvp in trackedCorpses)
            {
                var pawn = kvp.Key;
                var corpseData = kvp.Value;

                // Check if corpse was destroyed or invalid
                if (!corpseData.IsValid())
                {
                    toRemove.Add(pawn);
                    continue;
                }

                // Check if corpse is still at expected location
                if (corpseData.NeedsLocationUpdate())
                {
                    UpdateCorpseLocation(pawn, corpseData.Corpse.Map, corpseData.Corpse.Position);
                }
            }

            // Remove invalid entries
            foreach (var pawn in toRemove)
            {
                UnregisterCorpse(pawn);
            }

            if (toRemove.Count > 0)
            {
                Log.Message($"[Eternal] Cleaned up {toRemove.Count} invalid corpse entries");
            }
        }

        /// <summary>
        /// Resets rot progress to zero for all tracked Eternal corpses.
        /// Called on game load to handle saves from before the rot prevention fix.
        /// </summary>
        public void ResetAllRotProgress()
        {
            int resetCount = 0;
            foreach (var entry in trackedCorpses.Values)
            {
                if (entry?.Corpse == null || entry.Corpse.Destroyed)
                    continue;

                var rottable = entry.Corpse.GetComp<CompRottable>();
                if (rottable != null && rottable.RotProgress > 0f)
                {
                    rottable.RotProgress = 0f;
                    resetCount++;
                }
            }

            if (resetCount > 0)
            {
                Log.Message($"[Eternal] Reset rot progress for {resetCount} Eternal corpses on load");
            }
        }
    }
}
