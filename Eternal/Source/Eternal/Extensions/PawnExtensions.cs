// Relative Path: Eternal/Source/Eternal/Extensions/PawnExtensions.cs
// Creation Date: 01-01-2025
// Last Edit: 19-02-2026
// Author: 0Shard
// Description: Extension methods for Eternal pawn validation and map queries with per-tick caching.
//              Fixed: Cache race condition at tick 0 where pawn death wouldn't refresh cache.
//              Fixed: Trait suppression issue with Biotech genes - now checks hediff first and ignores suppression.
//              Added: HasFoodNeedDisabled() for detecting pawns with food need disabled (genes, hediffs, etc.)
//              PERF-08: InvalidateEternalPawnCache() now returns int (evicted count) for session-boundary logging.

using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

// Type alias to resolve namespace shadowing (Eternal.Map shadows Verse.Map)
using MapType = Verse.Map;

namespace Eternal.Extensions
{
    /// <summary>
    /// Extension methods for Eternal pawn validation.
    /// Includes per-tick caching for performance optimization.
    /// </summary>
    public static class PawnExtensions
    {
        #region Cache Storage

        private static List<Pawn> _cachedEternals;
        private static List<Pawn> _cachedLivingEternals;
        private static int _cacheTickStamp = -1;

        #endregion

        #region Pawn Validation

        /// <summary>
        /// Checks if this pawn is a valid Eternal (has the trait and is not destroyed).
        /// Checks hediff first (most reliable), then falls back to trait check ignoring suppression.
        /// </summary>
        public static bool IsValidEternal(this Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.health?.hediffSet == null)
                return false;

            // Check hediff first (most reliable - not subject to gene suppression)
            if (pawn.health.hediffSet.HasHediff(EternalDefOf.Eternal_Essence))
                return true;

            // Fallback: Check trait exists, ignoring suppression
            return pawn.HasTraitIgnoringSuppression(EternalDefOf.Eternal_GeneticMarker);
        }

        /// <summary>
        /// Checks if a pawn is a valid Eternal corpse for resurrection registration.
        /// Unlike IsValidEternal(), this does NOT require hediffSet (which may be null for some dead pawns)
        /// and explicitly validates the trait for corpse-based operations.
        /// Checks hediff first (most reliable), then falls back to trait check ignoring suppression.
        /// NOTE: Does NOT check pawn.Destroyed - dead pawns ARE destroyed Things, this is expected.
        /// </summary>
        public static bool IsValidEternalCorpse(this Pawn pawn)
        {
            if (pawn == null) return false;

            // NOTE: We intentionally do NOT check pawn.Destroyed here.
            // When Notify_PawnDied() is called, the pawn is already marked Destroyed
            // because Pawn.Kill() calls Thing.Destroy() BEFORE notifying hediffs.
            // For corpse registration, pawn.Destroyed == true is the expected state.

            // Check hediff first (most reliable - not subject to gene suppression)
            if (pawn.health?.hediffSet?.HasHediff(EternalDefOf.Eternal_Essence) == true)
                return true;

            // DefOf null safety - catch binding failures early
            if (EternalDefOf.Eternal_GeneticMarker == null)
            {
                Log.Error("[Eternal] EternalDefOf.Eternal_GeneticMarker is null - DefOf binding failed!");
                return false;
            }

            // Fallback: Check trait exists in allTraits, ignoring suppression
            return pawn.HasTraitIgnoringSuppression(EternalDefOf.Eternal_GeneticMarker);
        }

        /// <summary>
        /// Checks if pawn has a trait, ignoring suppression status.
        /// Use this for Eternal validation since genes can suppress traits at death.
        /// </summary>
        public static bool HasTraitIgnoringSuppression(this Pawn pawn, TraitDef traitDef)
        {
            if (pawn?.story?.traits?.allTraits == null) return false;

            foreach (var trait in pawn.story.traits.allTraits)
            {
                if (trait.def == traitDef) return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if this pawn has their food need disabled.
        /// For living pawns: checks if food need is null (RimWorld handles the logic).
        /// For dead pawns: checks underlying conditions (race, hediff, gene, trait, ideo).
        /// </summary>
        /// <remarks>
        /// Used by the food debt system to waive nutrient costs for pawns that don't eat.
        /// Mirrors RimWorld's Pawn_NeedsTracker.ShouldHaveNeed() logic.
        /// </remarks>
        public static bool HasFoodNeedDisabled(this Pawn pawn)
        {
            if (pawn == null)
                return true;

            // Living pawns with needs tracker: use RimWorld's existing logic
            if (!pawn.Dead && pawn.needs != null)
                return pawn.needs.food == null;

            // Dead pawns or no needs tracker: check underlying conditions

            // Race doesn't eat food (mechanoids, etc.)
            if (pawn.RaceProps != null && !pawn.RaceProps.EatsFood)
                return true;

            // Hediff disables food need
            if (pawn.health?.hediffSet?.DisablesNeed(NeedDefOf.Food) == true)
                return true;

            // Gene disables food need (Biotech DLC)
            if (ModsConfig.BiotechActive && pawn.genes?.DisablesNeed(NeedDefOf.Food) == true)
                return true;

            // Trait disables food need
            if (pawn.story?.traits?.DisablesNeed(NeedDefOf.Food) == true)
                return true;

            // Ideology disables food need (Ideology DLC)
            if (ModsConfig.IdeologyActive && pawn.Ideo?.DisablesNeed(NeedDefOf.Food) == true)
                return true;

            return false;
        }

        #endregion

        #region Map Extension Methods

        /// <summary>
        /// Gets all Eternal pawns on a specific map (both living and dead).
        /// </summary>
        public static IEnumerable<Pawn> GetAllEternalPawns(this MapType map)
        {
            if (map?.mapPawns?.AllPawns == null)
                return Enumerable.Empty<Pawn>();

            return map.mapPawns.AllPawns.Where(p => p.IsValidEternal());
        }

        /// <summary>
        /// Gets all Eternal pawns across all maps (both living and dead).
        /// Non-cached version for when fresh data is required.
        /// </summary>
        public static IEnumerable<Pawn> GetAllEternalPawnsAllMaps()
        {
            // Guard against early initialization when game isn't loaded yet
            // Current.Game is null during component construction; Find.Maps would throw
            if (Current.Game == null || Find.Maps == null)
                return Enumerable.Empty<Pawn>();

            return Find.Maps.SelectMany(m => m.GetAllEternalPawns());
        }

        #endregion

        #region Cached Queries (Performance Optimized)

        /// <summary>
        /// Gets all Eternal pawns across all maps (cached per tick).
        /// Use this in hot paths for better performance.
        /// </summary>
        public static IReadOnlyList<Pawn> GetAllEternalPawnsCached()
        {
            RefreshCacheIfNeeded();
            return _cachedEternals;
        }

        /// <summary>
        /// Gets all living Eternal pawns across all maps (cached per tick).
        /// Use this in hot paths for better performance.
        /// </summary>
        public static IReadOnlyList<Pawn> GetAllLivingEternalPawnsCached()
        {
            RefreshCacheIfNeeded();
            return _cachedLivingEternals;
        }

        /// <summary>
        /// Invalidates the eternal pawn cache.
        /// Call this when pawns are added, removed, or their Eternal status changes.
        /// </summary>
        /// <returns>The number of cached pawn entries that were evicted (for session-boundary logging).</returns>
        public static int InvalidateEternalPawnCache()
        {
            int count = _cachedEternals?.Count ?? 0;
            _cachedEternals = null;
            _cachedLivingEternals = null;
            _cacheTickStamp = -1;
            return count;
        }

        /// <summary>
        /// Refreshes the cache if the tick has changed.
        /// </summary>
        private static void RefreshCacheIfNeeded()
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;

            // Always refresh at tick 0 (game load) since rapid state changes can occur
            // This prevents stale cache issues where pawns die at tick 0
            if (_cachedEternals == null || currentTick != _cacheTickStamp || currentTick == 0)
            {
                _cachedEternals = GetAllEternalPawnsAllMaps().ToList();
                _cachedLivingEternals = _cachedEternals.Where(p => !p.Dead).ToList();
                _cacheTickStamp = currentTick;
            }
        }

        #endregion
    }
}
