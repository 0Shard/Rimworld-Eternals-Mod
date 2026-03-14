// file path: Eternal/Source/Eternal/Corpse/EternalCorpseManager.cs
// Author Name: 0Shard
// Date Created: 09-11-2025
// Date Last Modified: 12-03-2026
// Description: Manages all dead Eternal corpses globally, tracking their locations, states, and resurrection progress.
//              Implements IExposable for save/load persistence of corpse tracking data.
//              Now accepts and stores PawnAssignmentSnapshot for work priority/policy preservation.
//              Fixed: Added logging for orphaned/invalid corpse entries skipped during load.
//              Added: ResetAllRotProgress() to fix rot on saves from before the rot prevention fix.
//              Added: PreCalculatedHealingQueue parameter to capture injuries at death before RimWorld removes them.
//              Added: GetHealingCorpseCount() for live Effects tab population count display.

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

        /// <summary>
        /// Default constructor for new instances and IExposable deserialization.
        /// </summary>
        public EternalCorpseManager()
        {
            trackedCorpses = new Dictionary<Pawn, CorpseTrackingEntry>();
            corpsesByMap = new Dictionary<MapType, HashSet<Pawn>>();
        }

        /// <summary>
        /// Serializes/deserializes corpse tracking data for save/load.
        /// Uses Scribe_Collections for dictionary serialization.
        /// </summary>
        public void ExposeData()
        {
            // Serialize tracked corpses as list of entries
            // We use a list because dictionaries with Pawn keys are complex
            List<CorpseTrackingEntry> corpseEntries = null;

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                corpseEntries = trackedCorpses.Values.ToList();
            }

            Scribe_Collections.Look(ref corpseEntries, "trackedCorpses", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Reconstruct dictionaries from loaded entries
                trackedCorpses = new Dictionary<Pawn, CorpseTrackingEntry>();
                corpsesByMap = new Dictionary<MapType, HashSet<Pawn>>();

                if (corpseEntries != null)
                {
                    int loadedCount = 0;
                    int skippedCount = 0;

                    foreach (var entry in corpseEntries)
                    {
                        if (entry?.OriginalPawn != null && entry.IsValid())
                        {
                            trackedCorpses[entry.OriginalPawn] = entry;

                            // Rebuild corpsesByMap
                            if (entry.CurrentMap != null)
                            {
                                if (!corpsesByMap.ContainsKey(entry.CurrentMap))
                                {
                                    corpsesByMap[entry.CurrentMap] = new HashSet<Pawn>();
                                }
                                corpsesByMap[entry.CurrentMap].Add(entry.OriginalPawn);
                            }
                            loadedCount++;
                        }
                        else
                        {
                            // Log skipped/orphaned corpse entries for debugging
                            skippedCount++;
                            string pawnName = entry?.OriginalPawn?.Name?.ToStringShort ?? "null";
                            bool isValid = entry?.IsValid() ?? false;
                            Log.Warning($"[Eternal] Skipped orphaned corpse entry on load: Pawn={pawnName}, IsValid={isValid}");
                        }
                    }

                    if (skippedCount > 0)
                    {
                        Log.Warning($"[Eternal] {skippedCount} corpse entries were orphaned/invalid and skipped during load");
                    }
                }

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Loaded {trackedCorpses.Count} tracked Eternal corpses");
                }

                // Reset rot progress for all tracked corpses to handle saves from before rot fix
                ResetAllRotProgress();
            }
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

                var corpseData = new CorpseTrackingEntry(corpse, originalPawn, corpse.Map, corpse.Position);

                // Store the assignment snapshot for restoration after resurrection
                corpseData.AssignmentSnapshot = assignmentSnapshot;

                // Store the pre-calculated healing queue (captured at death before RimWorld removes injuries)
                if (preCalculatedQueue != null && preCalculatedQueue.Count > 0)
                {
                    corpseData.PreCalculatedHealingQueue = preCalculatedQueue;
                }

                trackedCorpses[originalPawn] = corpseData;

                // Track by map
                if (corpse.Map != null)
                {
                    if (!corpsesByMap.ContainsKey(corpse.Map))
                    {
                        corpsesByMap[corpse.Map] = new HashSet<Pawn>();
                    }
                    corpsesByMap[corpse.Map].Add(originalPawn);
                }

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

                // Remove from map tracking
                if (corpseData.CurrentMap != null && corpsesByMap.TryGetValue(corpseData.CurrentMap, out var mapCorpses))
                {
                    mapCorpses.Remove(originalPawn);
                    if (mapCorpses.Count == 0)
                    {
                        corpsesByMap.Remove(corpseData.CurrentMap);
                    }
                }

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
        /// Gets all Eternal corpses on a specific map.
        /// </summary>
        /// <param name="map">The map to check</param>
        /// <returns>Collection of corpses on the map</returns>
        public IEnumerable<CorpseTrackingEntry> GetCorpsesOnMap(MapType map)
        {
            if (!corpsesByMap.TryGetValue(map, out var mapCorpses))
            {
                return Enumerable.Empty<EternalCorpseData>();
            }

            return mapCorpses.Select(pawn => trackedCorpses[pawn]);
        }

        /// <summary>
        /// Checks if a map contains any Eternal corpses.
        /// </summary>
        /// <param name="map">The map to check</param>
        /// <returns>True if the map contains Eternal corpses</returns>
        public bool HasEternalCorpses(Verse.Map map)
        {
            return corpsesByMap.ContainsKey(map) && corpsesByMap[map].Count > 0;
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
        /// Updates corpse location if moved between maps.
        /// </summary>
        /// <param name="pawn">The pawn whose corpse moved</param>
        /// <param name="newMap">The new map location</param>
        /// <param name="newPosition">The new position</param>
        public void UpdateCorpseLocation(Pawn pawn, MapType newMap, IntVec3 newPosition)
        {
            if (!trackedCorpses.TryGetValue(pawn, out var corpseData))
            {
                return;
            }

            var oldMap = corpseData.CurrentMap;

            // Remove from old map tracking
            if (oldMap != null && corpsesByMap.TryGetValue(oldMap, out var oldMapCorpses))
            {
                oldMapCorpses.Remove(pawn);
                if (oldMapCorpses.Count == 0)
                {
                    corpsesByMap.Remove(oldMap);
                }
            }

            // Update location
            corpseData.UpdateLocation(newMap, newPosition);

            // Add to new map tracking
            if (newMap != null)
            {
                if (!corpsesByMap.ContainsKey(newMap))
                {
                    corpsesByMap[newMap] = new HashSet<Pawn>();
                }
                corpsesByMap[newMap].Add(pawn);
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
