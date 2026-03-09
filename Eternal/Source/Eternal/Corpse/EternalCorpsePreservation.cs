/*
 * Relative Path: Eternal/Source/Eternal/Corpse/EternalCorpsePreservation.cs
 * Creation Date: 09-11-2025
 * Last Edit: 20-02-2026
 * Author: 0Shard
 * Description: Manages preservation of Eternal corpses, preventing decay, deterioration, and destruction.
 *              Optimized with cached component references to avoid GetComp() lookups per tick.
 *              Fixed: MaintainPreservation() now processes removals in finally block for exception safety.
 *              03-02: All catch sites converted to EternalLogger.HandleException(CorpseTracking, ...).
 */

using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
// No corpse type alias needed - use Thing directly
using MapType = Verse.Map; // Alias to avoid namespace conflict
using Eternal.Exceptions;
using Eternal.Extensions;
using Eternal.Utils;
using Eternal.Utilities;

namespace Eternal.Corpse
{
    /// <summary>
    /// Manages preservation of Eternal corpses, preventing decay, deterioration, and destruction.
    /// Ensures Eternal corpses remain intact until resurrection can be performed.
    /// </summary>
    public class EternalCorpsePreservation
    {
        private readonly HashSet<Verse.Corpse> preservedCorpses = new HashSet<Verse.Corpse>();
        private int lastPreservationCheck = 0;
        private const int PRESERVATION_CHECK_INTERVAL = 1000; // Check every 1000 ticks

        // PERF: Cached component references to avoid GetComp() lookup per tick
        private readonly Dictionary<Verse.Corpse, CompRottable> _rottableCache = new Dictionary<Verse.Corpse, CompRottable>();

        /// <summary>
        /// Applies preservation effects to an Eternal corpse.
        /// </summary>
        /// <param name="corpse">The corpse to preserve</param>
        /// <param name="originalPawn">The original pawn</param>
        public void PreserveCorpse(Verse.Corpse corpse, Pawn originalPawn)
        {
            try
            {
                if (corpse == null || originalPawn == null)
                {
                    Log.Warning("[Eternal] Cannot preserve null corpse or pawn");
                    return;
                }

                if (!originalPawn.IsValidEternal())
                {
                    Log.Warning($"[Eternal] Attempted to preserve non-Eternal pawn: {originalPawn.Name}");
                    return;
                }

                // PERF: Cache component references on registration
                CacheComponents(corpse);

                // Apply preservation effects
                PreventDecay(corpse);
                PreventDeterioration(corpse);
                PreventDestruction(corpse);
                MakeIndestructible(corpse);

                preservedCorpses.Add(corpse);

                Log.Message($"[Eternal] Preserved corpse of {originalPawn.Name} at {corpse.Position}");
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "PreserveCorpse", originalPawn, ex);
            }
        }

        /// <summary>
        /// Removes preservation from a corpse (typically after resurrection).
        /// </summary>
        /// <param name="corpse">The corpse to unpreserve</param>
        public void UnpreserveCorpse(Verse.Corpse corpse)
        {
            try
            {
                if (corpse == null)
                    return;

                preservedCorpses.Remove(corpse);

                // PERF: Remove from component cache
                _rottableCache.Remove(corpse);

                // Restore normal corpse behavior
                RestoreNormalBehavior(corpse);

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Removed preservation from corpse at {corpse.Position}");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "UnpreserveCorpse", null, ex);
            }
        }

        /// <summary>
        /// Caches component references for a corpse.
        /// PERF: Called once on registration to avoid repeated GetComp() lookups.
        /// </summary>
        private void CacheComponents(Verse.Corpse corpse)
        {
            if (corpse == null) return;

            var rottable = corpse.GetComp<CompRottable>();
            if (rottable != null)
            {
                _rottableCache[corpse] = rottable;
            }
        }

        /// <summary>
        /// Gets the cached rottable component for a corpse.
        /// Falls back to GetComp() if not cached (shouldn't happen normally).
        /// </summary>
        private CompRottable GetCachedRottable(Verse.Corpse corpse)
        {
            if (_rottableCache.TryGetValue(corpse, out var cached))
            {
                return cached;
            }

            // Fallback: cache it now if missing
            var rottable = corpse.GetComp<CompRottable>();
            if (rottable != null)
            {
                _rottableCache[corpse] = rottable;
            }
            return rottable;
        }

        /// <summary>
        /// Periodically checks and maintains preservation on all Eternal corpses.
        /// PERF: Uses pooled list to avoid allocation per tick.
        /// </summary>
        public void MaintainPreservation()
        {
            int currentTick = Find.TickManager.TicksGame;
            if (currentTick - lastPreservationCheck < PRESERVATION_CHECK_INTERVAL)
            {
                return; // Not time to check yet
            }

            lastPreservationCheck = currentTick;

            // PERF: Use pooled list for removal tracking
            var toRemove = ListPool<Verse.Corpse>.Get();

            try
            {
                foreach (var corpse in preservedCorpses)
                {
                    if (corpse == null || corpse.Destroyed)
                    {
                        toRemove.Add(corpse);
                        continue;
                    }

                    // Wrap each corpse's maintenance in its own try-catch
                    // so one bad corpse doesn't prevent others from being processed
                    try
                    {
                        MaintainPreservationEffects(corpse);
                    }
                    catch (Exception ex)
                    {
                        EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                            "MaintainPreservationEffects", null, ex);
                    }
                }
            }
            finally
            {
                // Always process removals, even if iteration failed partway through
                foreach (var corpse in toRemove)
                {
                    preservedCorpses.Remove(corpse);
                    _rottableCache.Remove(corpse);
                }

                ListPool<Verse.Corpse>.Return(toRemove);
            }
        }

        /// <summary>
        /// Prevents corpse decay and rotting.
        /// PERF: Uses cached component reference.
        /// </summary>
        /// <param name="corpse">The corpse to protect from decay</param>
        private void PreventDecay(Verse.Corpse corpse)
        {
            try
            {
                // PERF: Use cached rottable component
                var rottable = GetCachedRottable(corpse);
                if (rottable != null)
                {
                    rottable.RotProgress = 0f;
                }
                // Mushroom rotting is now handled by CompRottable component in RimWorld 1.6

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Prevented decay for corpse at {corpse.Position}");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "PreventDecay", null, ex);
            }
        }

        /// <summary>
        /// Prevents corpse deterioration from environmental factors.
        /// PERF: Uses cached component reference.
        /// </summary>
        /// <param name="corpse">The corpse to protect from deterioration</param>
        private void PreventDeterioration(Verse.Corpse corpse)
        {
            try
            {
                // PERF: Use cached rottable component
                var rottable = GetCachedRottable(corpse);
                if (rottable != null)
                {
                    rottable.RotProgress = 0f;  // Keep corpse fresh
                }

                // Handle temperature damage by repairing it
                // Note: CompTemperatureDamaged is not cached as it's rarely used
                if (corpse.GetComp<CompTemperatureDamaged>() is CompTemperatureDamaged tempDamage)
                {
                    // Repair any temperature damage immediately
                    if (corpse.HitPoints < corpse.def.BaseMaxHitPoints)
                    {
                        corpse.HitPoints = corpse.def.BaseMaxHitPoints;
                    }
                }

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Prevented deterioration for corpse at {corpse.Position}");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "PreventDeterioration", null, ex);
            }
        }

        /// <summary>
        /// Prevents various forms of corpse destruction.
        /// </summary>
        /// <param name="corpse">The corpse to protect from destruction</param>
        private void PreventDestruction(Verse.Corpse corpse)
        {
            try
            {
                // Properties destroyAfterSolidifying, canBeBuried, canBeButchered removed in RimWorld 1.6
                // These are now handled through ThingDef or other systems
                // Corpse preservation is now managed through components and ThingDef settings

                // Prevent being used as food
                // IngestibleNow is read-only in RimWorld 1.6 - handled by other means
                // corpse.IngestibleNow = false;

                // Make forbidden by default (player must manually allow)
                corpse.SetForbidden(true, false);

                // Prevent being incinerated (RimWorld 1.6 API)
                PreventFireOnCorpse(corpse);

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Prevented destruction for corpse at {corpse.Position}");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "PreventDestruction", null, ex);
            }
        }

        /// <summary>
        /// Makes the corpse indestructible by setting maximum hit points.
        /// </summary>
        /// <param name="corpse">The corpse to make indestructible</param>
        private void MakeIndestructible(Verse.Corpse corpse)
        {
            try
            {
                if (corpse.def.useHitPoints)
                {
                    // Set to maximum hit points
                    corpse.HitPoints = corpse.def.BaseMaxHitPoints;
                }

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Made corpse indestructible at {corpse.Position}");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "MakeIndestructible", null, ex);
            }
        }

        /// <summary>
        /// Maintains preservation effects on a corpse.
        /// PERF: Uses cached component reference.
        /// </summary>
        /// <param name="corpse">The corpse to maintain</param>
        private void MaintainPreservationEffects(Verse.Corpse corpse)
        {
            try
            {
                // PERF: Use cached rottable component
                var rottable = GetCachedRottable(corpse);
                if (rottable != null && rottable.RotProgress > 0f)
                {
                    rottable.RotProgress = 0f;
                }

                // Maintain hit points
                if (corpse.def.useHitPoints && corpse.HitPoints < corpse.def.BaseMaxHitPoints)
                {
                    corpse.HitPoints = corpse.def.BaseMaxHitPoints;
                }

                // Maintain forbidden status
                if (!corpse.IsForbidden(Faction.OfPlayer))
                {
                    corpse.SetForbidden(true, false);
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "MaintainPreservationEffects", null, ex);
            }
        }

        /// <summary>
        /// Restores normal corpse behavior.
        /// </summary>
        /// <param name="corpse">The corpse to restore</param>
        private void RestoreNormalBehavior(Verse.Corpse corpse)
        {
            try
            {
                // Allow normal deterioration (RimWorld 1.6 API)
                // Stop resetting rot progress - let natural deterioration resume
                // Temperature damage component will also function normally
                // No specific action needed - just stop the preservation maintenance

                // Normal burial handled by ThingDef in RimWorld 1.6
                // canBeBuried property removed from corpse class

                // Remove forbidden status
                corpse.SetForbidden(false, false);

                // Allow normal rotting - now handled by CompRottable component
                // Properties like canBeButchered removed in RimWorld 1.6 - handled by ThingDef

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Restored normal behavior for corpse at {corpse.Position}");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "RestoreNormalBehavior", null, ex);
            }
        }

        /// <summary>
        /// Checks if a corpse is preserved.
        /// </summary>
        /// <param name="corpse">The corpse to check</param>
        /// <returns>True if the corpse is preserved</returns>
        public bool IsPreserved(Verse.Corpse corpse)
        {
            return preservedCorpses.Contains(corpse);
        }

        /// <summary>
        /// Gets all preserved corpses.
        /// </summary>
        /// <returns>Collection of preserved corpses</returns>
        public IEnumerable<Verse.Corpse> GetPreservedCorpses()
        {
            return preservedCorpses;
        }

        /// <summary>
        /// Gets preserved corpses on a specific map.
        /// </summary>
        /// <param name="map">The map to check</param>
        /// <returns>Collection of preserved corpses on the map</returns>
        public IEnumerable<Verse.Corpse> GetPreservedCorpsesOnMap(MapType map)
        {
            if (map == null)
                return Enumerable.Empty<Verse.Corpse>();

            return preservedCorpses.Where(corpse => corpse.Map == map);
        }

        /// <summary>
        /// Forces preservation refresh on all corpses.
        /// </summary>
        public void RefreshAllPreservation()
        {
            try
            {
                var corpsesToCheck = preservedCorpses.ToList();
                var toRemove = new List<Verse.Corpse>();

                foreach (var corpse in corpsesToCheck)
                {
                    if (corpse == null || corpse.Destroyed)
                    {
                        toRemove.Add(corpse);
                        continue;
                    }

                    // Reapply all preservation effects
                    PreventDecay(corpse);
                    PreventDeterioration(corpse);
                    PreventDestruction(corpse);
                    MakeIndestructible(corpse);
                }

                // Remove invalid corpses
                foreach (var corpse in toRemove)
                {
                    preservedCorpses.Remove(corpse);
                }

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Refreshed preservation on {preservedCorpses.Count} corpses");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "RefreshAllPreservation", null, ex);
            }
        }

        /// <summary>
        /// Clears all preserved corpses (emergency use).
        /// </summary>
        public void ClearAllPreservation()
        {
            try
            {
                var corpsesToClear = preservedCorpses.ToList();
                foreach (var corpse in corpsesToClear)
                {
                    UnpreserveCorpse(corpse);
                }

                Log.Message("[Eternal] Cleared all corpse preservation");
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "ClearAllPreservation", null, ex);
            }
        }

        /// <summary>
        /// Gets preservation statistics for debugging.
        /// </summary>
        /// <returns>Dictionary containing preservation statistics</returns>
        public Dictionary<string, object> GetPreservationStats()
        {
            var stats = new Dictionary<string, object>
            {
                ["TotalPreservedCorpses"] = preservedCorpses.Count,
                ["LastPreservationCheck"] = lastPreservationCheck
            };

            // Count by map
            var byMap = new Dictionary<string, int>();
            foreach (var corpse in preservedCorpses)
            {
                if (corpse?.Map != null)
                {
                    string mapName = corpse.Map.ToString();
                    byMap[mapName] = byMap.TryGetValue(mapName, out int count) ? count + 1 : 1;
                }
            }
            stats["CorpsesByMap"] = byMap;

            return stats;
        }

        /// <summary>
        /// Prevents fire on a corpse using RimWorld 1.6 API.
        /// </summary>
        /// <param name="corpse">The corpse to protect from fire</param>
        private void PreventFireOnCorpse(Verse.Corpse corpse)
        {
            try
            {
                if (corpse == null)
                    return;

                // Use damage system to extinguish any existing fire
                // This is the most reliable method in RimWorld 1.6
                corpse.TakeDamage(new DamageInfo(DamageDefOf.Extinguish, 999f));
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "PreventFireOnCorpse", null, ex);
            }
        }
    }
}
