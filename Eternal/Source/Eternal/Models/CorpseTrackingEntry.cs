/*
 * Relative Path: Eternal/Source/Eternal/Models/CorpseTrackingEntry.cs
 * Creation Date: 03-12-2025
 * Last Edit: 21-02-2026
 *              BUGFIX: Cached component properties now validate corpse isn't destroyed before returning.
 *              Added PreCalculatedHealingQueue to capture injuries at death before RimWorld removes them.
 *              SAFE-04: Added CaravanId (WorldObject.ID) field for persistent caravan reference across save/load.
 *                       PostLoadInit resolves CaravanId back to live caravan; logs warning if dissolved.
 * Author: 0Shard
 * Description: Consolidated data structure for corpse tracking, eliminating parallel dictionaries.
 *              Implements IExposable for save/load persistence.
 *              Now includes PawnAssignmentSnapshot for preserving work priorities and policies.
 *              Optimized with cached component references to avoid GetComp() lookups per tick.
 */

using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using RimWorld.Planet;
using Eternal.Healing;
using Eternal.Corpse;

// Type aliases to resolve namespace shadowing (Eternal.Corpse and Eternal.Map shadow Verse types)
using CorpseType = Verse.Corpse;
using MapType = Verse.Map;

namespace Eternal.Models
{
    /// <summary>
    /// Consolidated data structure for tracking an Eternal corpse.
    /// Combines corpse data with map location in a single structure,
    /// eliminating the need for parallel dictionaries.
    /// Implements IExposable for save/load persistence.
    /// Uses fields instead of auto-properties for Scribe compatibility.
    /// </summary>
    public class CorpseTrackingEntry : IExposable
    {
        /// <summary>
        /// The corpse object being tracked.
        /// </summary>
        public CorpseType Corpse;

        /// <summary>
        /// The original pawn before death.
        /// </summary>
        public Pawn OriginalPawn;

        /// <summary>
        /// The tick when the pawn died.
        /// </summary>
        public int DeathTick;

        /// <summary>
        /// Current map location of the corpse.
        /// Note: Map references are reconstructed after load using corpse position.
        /// </summary>
        public MapType CurrentMap;

        /// <summary>
        /// Current position on the map.
        /// </summary>
        public IntVec3 Position;

        /// <summary>
        /// Whether resurrection has been initiated.
        /// </summary>
        public bool IsRegisteredForResurrection;

        /// <summary>
        /// Overall healing progress (0.0 to 1.0).
        /// </summary>
        public float HealingProgress;

        /// <summary>
        /// Accumulated food debt for this corpse.
        /// </summary>
        public float FoodDebt;

        /// <summary>
        /// Whether healing is currently active.
        /// </summary>
        public bool IsHealingActive;

        /// <summary>
        /// Total cost calculated for full healing.
        /// </summary>
        public float TotalHealingCost;

        /// <summary>
        /// Tick when healing started.
        /// </summary>
        public int HealingStartTick;

        /// <summary>
        /// Queue of healing items to process.
        /// Note: Healing queue is recalculated on load for simplicity.
        /// </summary>
        public List<HealingItem> HealingQueue = new List<HealingItem>();

        /// <summary>
        /// Pre-calculated healing queue captured at death, before RimWorld removes injuries.
        /// Used by StartCorpseHealing() to ensure injuries are not lost.
        /// Saved/loaded via Scribe_Collections with LookMode.Deep.
        /// </summary>
        public List<HealingItem> PreCalculatedHealingQueue;

        /// <summary>
        /// SAFE-04: WorldObject.ID of the caravan that contained this corpse at death.
        /// Sentinel value -1 means "not in a caravan" (default for living-pawn deaths and old saves).
        /// Persisted via Scribe_Values and resolved back to a live Caravan reference in PostLoadInit.
        /// Old saves load with -1 naturally — no migration pass needed.
        /// </summary>
        public int CaravanId = -1;

        /// <summary>
        /// Snapshot of work priorities, policies, and schedule captured at death.
        /// Used to restore pawn assignments after resurrection.
        /// </summary>
        public PawnAssignmentSnapshot AssignmentSnapshot;

        #region Cached Components (Performance Optimization)

        /// <summary>
        /// Cached reference to the corpse's rottable component.
        /// PERF: Avoids GetComp() lookup every preservation tick.
        /// </summary>
        private CompRottable _cachedRottableComponent;
        private bool _rottableCached;

        /// <summary>
        /// Cached reference to the Eternal corpse component.
        /// PERF: Avoids GetComp() lookup every healing tick.
        /// </summary>
        private EternalCorpseComponent _cachedEternalComponent;
        private bool _eternalComponentCached;

        /// <summary>
        /// Gets the cached rottable component, caching on first access.
        /// Returns null if corpse is invalid, destroyed, or component doesn't exist.
        /// BUGFIX: Now validates corpse isn't destroyed before returning cached component.
        /// </summary>
        public CompRottable CachedRottable
        {
            get
            {
                // BUGFIX: Invalidate cache if corpse is destroyed to prevent stale references
                if (Corpse == null || Corpse.Destroyed)
                {
                    _cachedRottableComponent = null;
                    _rottableCached = false;
                    return null;
                }

                if (!_rottableCached)
                {
                    _cachedRottableComponent = Corpse.GetComp<CompRottable>();
                    _rottableCached = true;
                }
                return _cachedRottableComponent;
            }
        }

        /// <summary>
        /// Gets the cached Eternal corpse component, caching on first access.
        /// Returns null if corpse is invalid, destroyed, or component doesn't exist.
        /// BUGFIX: Now validates corpse isn't destroyed before returning cached component.
        /// </summary>
        public EternalCorpseComponent CachedEternalComponent
        {
            get
            {
                // BUGFIX: Invalidate cache if corpse is destroyed to prevent stale references
                if (Corpse == null || Corpse.Destroyed)
                {
                    _cachedEternalComponent = null;
                    _eternalComponentCached = false;
                    return null;
                }

                if (!_eternalComponentCached)
                {
                    _cachedEternalComponent = Corpse.GetComp<EternalCorpseComponent>();
                    _eternalComponentCached = true;
                }
                return _cachedEternalComponent;
            }
        }

        /// <summary>
        /// Pre-caches all component references.
        /// Call this after registration for optimal performance.
        /// </summary>
        public void CacheComponents()
        {
            if (Corpse == null) return;

            _cachedRottableComponent = Corpse.GetComp<CompRottable>();
            _rottableCached = true;

            _cachedEternalComponent = Corpse.GetComp<EternalCorpseComponent>();
            _eternalComponentCached = true;
        }

        /// <summary>
        /// Invalidates cached components.
        /// Call if the corpse reference changes.
        /// </summary>
        public void InvalidateComponentCache()
        {
            _cachedRottableComponent = null;
            _rottableCached = false;
            _cachedEternalComponent = null;
            _eternalComponentCached = false;
        }

        #endregion

        /// <summary>
        /// Creates a new corpse tracking entry.
        /// Required for IExposable deserialization.
        /// </summary>
        public CorpseTrackingEntry()
        {
            HealingQueue = new List<HealingItem>();
        }

        /// <summary>
        /// Creates a new corpse tracking entry with initial values.
        /// Pre-caches component references for optimal performance.
        /// </summary>
        public CorpseTrackingEntry(CorpseType corpse, Pawn originalPawn, MapType map, IntVec3 position)
        {
            Corpse = corpse;
            OriginalPawn = originalPawn;
            CurrentMap = map;
            Position = position;
            DeathTick = Find.TickManager?.TicksGame ?? 0;
            IsRegisteredForResurrection = false;
            HealingProgress = 0f;
            FoodDebt = 0f;
            IsHealingActive = false;
            TotalHealingCost = 0f;
            HealingStartTick = 0;
            HealingQueue = new List<HealingItem>();

            // PERF: Pre-cache component references on registration
            CacheComponents();
        }

        /// <summary>
        /// Serializes/deserializes the corpse tracking entry for save/load.
        /// RimWorld's Scribe system handles the actual serialization.
        /// </summary>
        public void ExposeData()
        {
            // Core references - use Scribe_References for Thing/Pawn references
            Scribe_References.Look(ref Corpse, "corpse");
            Scribe_References.Look(ref OriginalPawn, "originalPawn");

            // Simple values
            Scribe_Values.Look(ref DeathTick, "deathTick", 0);
            Scribe_Values.Look(ref Position, "position", IntVec3.Invalid);
            Scribe_Values.Look(ref IsRegisteredForResurrection, "isRegisteredForResurrection", false);
            Scribe_Values.Look(ref HealingProgress, "healingProgress", 0f);
            Scribe_Values.Look(ref FoodDebt, "foodDebt", 0f);
            Scribe_Values.Look(ref IsHealingActive, "isHealingActive", false);
            Scribe_Values.Look(ref TotalHealingCost, "totalHealingCost", 0f);
            Scribe_Values.Look(ref HealingStartTick, "healingStartTick", 0);

            // SAFE-04: Persist caravan ID for fast post-load resolution.
            // Default -1 = "not in a caravan". Old saves load with -1 — no migration needed.
            Scribe_Values.Look(ref CaravanId, "caravanId", -1);

            // Assignment snapshot - use Deep for complex objects
            Scribe_Deep.Look(ref AssignmentSnapshot, "assignmentSnapshot");

            // Pre-calculated healing queue captured at death (before RimWorld removes injuries)
            Scribe_Collections.Look(ref PreCalculatedHealingQueue, "preCalculatedHealingQueue", LookMode.Deep);

            // Note: HealingQueue is NOT saved - it will be recalculated when healing resumes
            // This simplifies serialization and ensures queue is always consistent with pawn state

            // Reconstruct map reference and re-cache components after loading
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (Corpse != null)
                {
                    CurrentMap = Corpse.Map;
                    if (CurrentMap != null && Position.IsValid)
                    {
                        // Update position to corpse's actual position (may have moved)
                        Position = Corpse.Position;
                    }

                    // PERF: Re-cache component references after loading
                    CacheComponents();
                }

                // SAFE-04: Resolve persisted CaravanId back to a live caravan reference.
                // If the caravan dissolved between save and load, log a warning and continue
                // with null — FindCaravanContainingCorpse() linear scan is the fallback.
                if (CaravanId != -1)
                {
                    var resolved = Find.WorldObjects.Caravans.FirstOrDefault(c => c.ID == CaravanId);
                    if (resolved == null)
                    {
                        string pawnName = OriginalPawn?.Name?.ToStringFull ?? "unknown";
                        Log.Warning($"[Eternal] CaravanId {CaravanId} for {pawnName} not found after load. " +
                                    "Caravan may have dissolved. Tracking continues with null caravan reference.");
                        // CaravanId kept as-is — FindCaravanContainingCorpse() will fall back to linear scan.
                    }
                    // No live reference field needed — FindCaravanContainingCorpse() uses CaravanId for fast lookup.
                }
            }
        }

        /// <summary>
        /// Updates the location of this corpse.
        /// </summary>
        public void UpdateLocation(MapType newMap, IntVec3 newPosition)
        {
            CurrentMap = newMap;
            Position = newPosition;
        }

        /// <summary>
        /// Checks if the corpse is still valid (not destroyed).
        /// </summary>
        public bool IsValid()
        {
            return Corpse != null && !Corpse.Destroyed && OriginalPawn != null;
        }

        /// <summary>
        /// Checks if the corpse location needs updating.
        /// </summary>
        public bool NeedsLocationUpdate()
        {
            if (Corpse == null)
                return false;

            return CurrentMap != Corpse.Map || Position != Corpse.Position;
        }
    }
}
