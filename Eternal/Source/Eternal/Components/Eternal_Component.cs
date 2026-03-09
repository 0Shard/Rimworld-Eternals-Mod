// Relative Path: Eternal/Source/Eternal/Components/Eternal_Component.cs
// Creation Date: 01-01-2025
// Last Edit: 06-03-2026
// SAFE-09: FinalizeInit() early-returns when EternalModState.IsDisabled — prevents SOS2 cache
//          init and healing processor init from running with null critical defs.
// Author: 0Shard
// Description: Main game component for Eternal mod. Serves as the central service locator.
//              Delegates tick processing to TickOrchestrator for separation of concerns.
//              Fixed: Added null/destroyed pawn checks in CheckTraitHediffConsistency() loops.
//              SAFE-02: Hard reset EternalServiceContainer on new session to eliminate stale manager references.
//              PERF-08: Invalidate cross-session stale pawn cache on session boundary.
//              Phase 4: Eagerly initialize SOS2ReflectionCache in FinalizeInit() for fail-fast detection.

using System;
using System.Collections.Generic;
using Verse;
using Eternal.Caravan;
using Eternal.Compatibility;
using Eternal.Components;
using Eternal.Corpse;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Extensions;
using Eternal.Healing;
using Eternal.Interfaces;
using Eternal.Resources;
using Eternal.Utils;

namespace Eternal
{
    /// <summary>
    /// Main game component for the Eternal mod.
    ///
    /// TICK STRUCTURE:
    /// ===============
    /// - Normal ticks (normalTickRate, default 60):
    ///   Processes injury healing for living pawns via EternalHealingProcessor.
    ///
    /// - Rare ticks (rareTickRate, default 250):
    ///   Processes scars, regrowth, corpse healing/resurrection, and food debt.
    ///
    /// - Corpse checks (1000 ticks):
    ///   Maintains corpse preservation and cleans up invalid corpses.
    ///
    /// - Map checks (500 ticks):
    ///   Protects maps containing Eternal corpses from being removed.
    ///
    /// - Trait checks (5000 ticks):
    ///   Ensures trait-hediff consistency (all Eternals have Eternal_Essence).
    ///
    /// SERVICE ACCESS:
    /// ================
    /// Via properties (preferred - implements IEternalServices):
    /// - HealingProcessor - Living pawn healing
    /// - CorpseManager - Dead pawn corpse tracking
    /// - CorpseHealingProcessor - Corpse healing/resurrection
    /// - FoodDebtSystem - Food debt tracking
    /// - CorpsePreservation - Corpse decay prevention
    /// - MapProtection - Map removal prevention
    /// - RegrowthManager - Limb regrowth coordination
    ///
    /// Via methods (backwards compatibility):
    /// - GetHealingProcessor(), GetCorpseManager(), etc.
    ///
    /// SINGLETON LIFECYCLE:
    /// ====================
    /// - Created: When a new game is started or an existing game is loaded
    /// - Instance available via: Eternal_Component.Current or Eternal_Component.Instance
    /// - Destroyed: When the game session ends (return to main menu)
    /// - Thread safety: All access should occur on the main Unity thread
    /// </summary>
    public class Eternal_Component : GameComponent, IEternalServices
    {
        #region Static Instance

        private static Eternal_Component instance;

        /// <summary>
        /// Gets the current instance of the Eternal component.
        /// Alias for Instance, following standard naming conventions.
        /// </summary>
        public static Eternal_Component Current => instance;

        /// <summary>
        /// Gets the singleton instance of the Eternal component.
        /// </summary>
        public static Eternal_Component Instance => instance;

        #endregion

        #region Managers

        private EternalRegrowthManager regrowthManager;
        private EternalCaravanDeathHandler caravanDeathHandler;
        private EternalHealingProcessor healingProcessor;
        private EternalCorpseManager corpseManager;
        private EternalCorpseHealingProcessor corpseHealingProcessor;
        private IFoodDebtSystem foodDebtSystem;
        private EternalCorpsePreservation corpsePreservation;
        private EternalMapProtection mapProtection;

        // Tick orchestrator handles timing and processing
        private TickOrchestrator tickOrchestrator;

        #endregion

        #region Initialization

        /// <summary>
        /// Initializes a new instance of Eternal_Component.
        /// Registered programmatically via Harmony patch in GameComponentRegistration_Patch.cs.
        /// </summary>
        public Eternal_Component(Game game)
        {
            instance = this;

            // SAFE-02: Hard reset on new session — eliminates stale manager references.
            // EternalServiceContainer._initialized is static and survives across RimWorld sessions.
            // Without this reset, Initialize() silently returns on the second game load, leaving
            // the container pointing at managers from the previous session.
            bool wasInitialized = EternalServiceContainer.Instance.IsInitialized;
            if (wasInitialized)
            {
                EternalServiceContainer.Instance.Reset();
            }

            // PERF-08: Invalidate cross-session stale pawn cache.
            // _cachedEternals is static and survives across sessions; clearing prevents stale pawn
            // references from the previous save being served to hot-path callers in the new session.
            int evictedPawns = PawnExtensions.InvalidateEternalPawnCache();

            if (Eternal_Mod.settings?.debugMode == true)
            {
                if (wasInitialized)
                    Log.Message("[Eternal] EternalServiceContainer hard reset for new game session");
                if (evictedPawns > 0)
                    Log.Message($"[Eternal] Pawn cache cleared on session start ({evictedPawns} stale entries evicted)");
            }

            // Initialize all managers as new instances (no singletons)
            regrowthManager = new EternalRegrowthManager();
            caravanDeathHandler = new EternalCaravanDeathHandler(game);
            healingProcessor = new EternalHealingProcessor();
            corpseManager = new EternalCorpseManager();
            corpseHealingProcessor = new EternalCorpseHealingProcessor();
            foodDebtSystem = new UnifiedFoodDebtManager();
            corpsePreservation = new EternalCorpsePreservation();
            mapProtection = new EternalMapProtection();

            // Initialize the service container with all services
            EternalServiceContainer.Instance.Initialize(
                foodDebtSystem,
                healingProcessor,
                corpseManager,
                corpseHealingProcessor,
                corpsePreservation,
                mapProtection,
                regrowthManager);

            // NOTE: healingProcessor.Initialize() is called in FinalizeInit() after game world is ready
            // Calling it here would crash because Current.Game is null during component construction

            // Create tick orchestrator with all dependencies
            tickOrchestrator = new TickOrchestrator(
                healingProcessor,
                corpseHealingProcessor,
                corpseManager,
                corpsePreservation,
                mapProtection,
                regrowthManager,
                caravanDeathHandler,
                foodDebtSystem,
                EternalServiceContainer.Instance.Settings,
                CheckTraitHediffConsistency);

            Log.Message("[Eternal] Eternal_Component instantiated successfully");
        }

        #endregion

        #region Tick Processing

        /// <summary>
        /// Called every game tick to update Eternal mechanics.
        /// Delegates all tick processing to the TickOrchestrator.
        /// </summary>
        public override void GameComponentUpdate()
        {
            base.GameComponentUpdate();
            tickOrchestrator?.ProcessTick();
        }

        #endregion

        #region Public API

        /// <summary>
        /// Starts the regrowth/resurrection process for a pawn.
        /// </summary>
        public void StartRegrowth(Pawn pawn)
        {
            if (!pawn.IsValidEternal())
                return;

            if (pawn.Dead)
            {
                var corpseData = corpseManager.GetCorpseData(pawn);
                if (corpseData != null)
                {
                    corpseHealingProcessor.StartCorpseHealing(corpseData);
                }
            }
            else
            {
                healingProcessor.StartRegrowth(pawn);
            }
        }

        #endregion

        #region Service Access (IEternalServices Implementation)

        /// <summary>
        /// Gets the food debt tracking system.
        /// </summary>
        public IFoodDebtSystem FoodDebtSystem => foodDebtSystem;

        /// <summary>
        /// Gets the healing processor for living pawns.
        /// </summary>
        public EternalHealingProcessor HealingProcessor => healingProcessor;

        /// <summary>
        /// Gets the corpse manager for tracking dead Eternals.
        /// </summary>
        public EternalCorpseManager CorpseManager => corpseManager;

        /// <summary>
        /// Gets the corpse healing processor for resurrection.
        /// </summary>
        public EternalCorpseHealingProcessor CorpseHealingProcessor => corpseHealingProcessor;

        /// <summary>
        /// Gets the corpse preservation system.
        /// </summary>
        public EternalCorpsePreservation CorpsePreservation => corpsePreservation;

        /// <summary>
        /// Gets the map protection system.
        /// </summary>
        public EternalMapProtection MapProtection => mapProtection;

        /// <summary>
        /// Gets the regrowth manager for limb regeneration.
        /// </summary>
        public EternalRegrowthManager RegrowthManager => regrowthManager;

        #endregion

        #region Consistency Checks

        /// <summary>
        /// Ensures all pawns with Eternal trait have the Eternal_Essence hediff.
        /// </summary>
        private void CheckTraitHediffConsistency()
        {
            try
            {
                // Check map pawns
                var mapPawns = Find.CurrentMap?.mapPawns?.AllPawnsSpawned ?? new List<Pawn>();
                foreach (var pawn in mapPawns)
                {
                    // Skip null or destroyed pawns (defensive check)
                    if (pawn == null || pawn.Destroyed)
                        continue;

                    EnsureEternalHediff(pawn);
                }

                // Check caravan pawns
                foreach (var caravan in Find.WorldObjects.Caravans)
                {
                    if (caravan?.PawnsListForReading == null)
                        continue;

                    foreach (var pawn in caravan.PawnsListForReading)
                    {
                        // Skip null or destroyed pawns (defensive check)
                        if (pawn == null || pawn.Destroyed)
                            continue;

                        EnsureEternalHediff(pawn);
                    }
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.InternalError,
                    "CheckTraitHediffConsistency", null, ex);
            }
        }

        /// <summary>
        /// Ensures a pawn with Eternal trait has the Eternal_Essence hediff.
        /// </summary>
        private void EnsureEternalHediff(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed)
                return;

            if (pawn.health?.hediffSet == null)
                return;

            // Check if pawn has trait but missing hediff
            bool hasTrait = pawn.HasTraitIgnoringSuppression(EternalDefOf.Eternal_GeneticMarker);
            bool hasHediff = pawn.health.hediffSet.HasHediff(EternalDefOf.Eternal_Essence);

            if (hasTrait && !hasHediff)
            {
                Hediff hediff = HediffMaker.MakeHediff(EternalDefOf.Eternal_Essence, pawn);
                if (hediff != null)
                {
                    hediff.Severity = 1.0f;
                    pawn.health.AddHediff(hediff);
                    Log.Message($"[Eternal] Added missing Eternal_Essence hediff to {pawn.Name?.ToStringShort ?? "Unknown"}");
                }
            }
        }

        #endregion

        #region Serialization

        // Timing fields for serialization (backwards compatibility)
        private int lastNormalHealTick = 0;
        private int lastRareHealTick = 0;
        private int lastTraitCheckTick = 0;
        private int lastCorpseCheckTick = 0;
        private int lastMapCheckTick = 0;
        private int lastHealingSweepTick = 0;

        public override void ExposeData()
        {
            base.ExposeData();

            // Timing values - saved at component level for backwards compatibility
            // On save: get from orchestrator; on load: stored temporarily, then passed to new orchestrator
            if (Scribe.mode == LoadSaveMode.Saving && tickOrchestrator != null)
            {
                var timing = tickOrchestrator.GetTimingValues();
                lastNormalHealTick = timing.normalHeal;
                lastRareHealTick = timing.rareHeal;
                lastTraitCheckTick = timing.traitCheck;
                lastCorpseCheckTick = timing.corpseCheck;
                lastMapCheckTick = timing.mapCheck;
                lastHealingSweepTick = timing.healingSweep;
            }

            Scribe_Values.Look(ref lastNormalHealTick, "lastNormalHealTick", 0);
            Scribe_Values.Look(ref lastRareHealTick, "lastRareHealTick", 0);
            Scribe_Values.Look(ref lastTraitCheckTick, "lastTraitCheckTick", 0);
            Scribe_Values.Look(ref lastCorpseCheckTick, "lastCorpseCheckTick", 0);
            Scribe_Values.Look(ref lastMapCheckTick, "lastMapCheckTick", 0);
            Scribe_Values.Look(ref lastHealingSweepTick, "lastHealingSweepTick", 0);

            // Manager serialization - directly serialize all IExposable managers
            Scribe_Deep.Look(ref regrowthManager, "regrowthManager");
            Scribe_Deep.Look(ref caravanDeathHandler, "caravanDeathHandler");
            Scribe_Deep.Look(ref corpseManager, "corpseManager");

            // Food debt system serialization (cast to IExposable)
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // Serialize the debt system's data
                (foodDebtSystem as IExposable)?.ExposeData();
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // Create new debt system instance and load its data
                if (foodDebtSystem == null)
                {
                    foodDebtSystem = new UnifiedFoodDebtManager();
                }
                (foodDebtSystem as IExposable)?.ExposeData();
            }

            // Threshold tracker serialization (stored in service container)
            EternalServiceContainer.Instance.ThresholdTracker?.ExposeData();

            // Post-load initialization
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Create new instances for non-serialized managers
                if (healingProcessor == null)
                    healingProcessor = new EternalHealingProcessor();
                if (corpseHealingProcessor == null)
                    corpseHealingProcessor = new EternalCorpseHealingProcessor();
                if (corpsePreservation == null)
                    corpsePreservation = new EternalCorpsePreservation();
                if (mapProtection == null)
                    mapProtection = new EternalMapProtection();
                if (corpseManager == null)
                    corpseManager = new EternalCorpseManager();
                if (foodDebtSystem == null)
                    foodDebtSystem = new UnifiedFoodDebtManager();

                // Update service container with loaded manager references
                EternalServiceContainer.Instance.UpdateManagerReferences(
                    healingProcessor,
                    corpseManager,
                    corpseHealingProcessor,
                    corpsePreservation,
                    mapProtection,
                    regrowthManager);

                // Initialize healing processor
                healingProcessor?.Initialize();

                // Recreate tick orchestrator with dependencies and restored timing
                tickOrchestrator = new TickOrchestrator(
                    healingProcessor,
                    corpseHealingProcessor,
                    corpseManager,
                    corpsePreservation,
                    mapProtection,
                    regrowthManager,
                    caravanDeathHandler,
                    foodDebtSystem,
                    EternalServiceContainer.Instance.Settings,
                    CheckTraitHediffConsistency);

                // Restore timing values from loaded save
                tickOrchestrator.SetTimingValues(
                    lastNormalHealTick,
                    lastRareHealTick,
                    lastTraitCheckTick,
                    lastCorpseCheckTick,
                    lastMapCheckTick,
                    lastHealingSweepTick);

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Post-load initialization complete. Tracking {corpseManager?.TrackedCount ?? 0} corpses.");
                }
            }
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();

            // Verify DefOf bindings early to catch XML/definition issues.
            // If any critical def is null, VerifyBindings() triggers EternalModState.Disable()
            // and sends an in-game letter. We early-return here to skip all subsequent
            // initialization that would NRE without the defs.
            EternalDefOf.VerifyBindings();

            if (EternalModState.IsDisabled)
                return;

            // Initialize SOS2 reflection cache eagerly — fail-fast if SOS2 API changed between versions.
            // Must run after mods are loaded (FinalizeInit is the correct hook).
            EternalServiceContainer.Instance.SpaceModCache?.Initialize(SpaceModDetection.SaveOurShip2Active);

            // Initialize healing processor now that game world is fully loaded
            // This is the correct lifecycle hook - called after maps and pawns exist
            healingProcessor?.Initialize();
        }

        #endregion
    }
}
