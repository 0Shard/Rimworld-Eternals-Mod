// Relative Path: Eternal/Source/Eternal/DI/EternalServiceContainer.cs
// Creation Date: 29-12-2025
// Last Edit: 21-02-2026
// Author: 0Shard
// Description: Simple service container for dependency injection. Wires up all Eternal
//              services with proper dependencies. No external DI framework needed.
//              Fixed: Added null validation for required debtSystem parameter.
//              Phase 4: Added SOS2ReflectionCache with session reset support.

using Verse;
using Eternal.Compatibility;
using Eternal.Corpse;
using Eternal.Healing;
using Eternal.Interfaces;
using Eternal.Resources;
using Eternal.Settings;

namespace Eternal.DI
{
    /// <summary>
    /// Service container for Eternal mod.
    /// Provides centralized dependency injection without external frameworks.
    /// </summary>
    /// <remarks>
    /// Service Lifetime:
    /// - Singleton services are created once and reused
    /// - All services are initialized together when Initialize() is called
    /// - Call Initialize() during mod startup (from Eternal_Component constructor)
    /// </remarks>
    public class EternalServiceContainer
    {
        private static EternalServiceContainer _instance;

        /// <summary>
        /// Gets the singleton instance of the service container.
        /// </summary>
        public static EternalServiceContainer Instance => _instance ??= new EternalServiceContainer();

        private bool _initialized;

        // Core services (created by container)
        private ISettingsProvider _settings;
        private IHealingRateCalculator _rateCalculator;
        private IHediffHealingCalculator _hediffHealingCalculator;
        private IDebtAccumulator _debtAccumulator;
        private IPartRestorer _partRestorer;
        private HediffHealingThresholdTracker _thresholdTracker;
        private IFoodCostProcessor _foodCostProcessor;
        private DebtRepaymentProcessor _debtRepaymentProcessor;

        // Manager services (provided by Eternal_Component, singleton-backed for save/load)
        private IFoodDebtSystem _debtSystem;
        private EternalHealingProcessor _healingProcessor;
        private EternalCorpseManager _corpseManager;
        private EternalCorpseHealingProcessor _corpseHealingProcessor;
        private EternalCorpsePreservation _corpsePreservation;
        private EternalMapProtection _mapProtection;
        private EternalRegrowthManager _regrowthManager;

        // Compatibility cache (session-scoped, reset on new game)
        private SOS2ReflectionCache _spaceModCache;

        /// <summary>
        /// Gets the settings provider.
        /// </summary>
        public ISettingsProvider Settings => _settings;

        /// <summary>
        /// Gets the healing rate calculator (simple regrowth/corpse healing).
        /// </summary>
        public IHealingRateCalculator RateCalculator => _rateCalculator;

        /// <summary>
        /// Gets the hediff healing calculator (complex per-hediff healing with stages).
        /// </summary>
        public IHediffHealingCalculator HediffHealingCalculator => _hediffHealingCalculator;

        /// <summary>
        /// Gets the debt accumulator.
        /// </summary>
        public IDebtAccumulator DebtAccumulator => _debtAccumulator;

        /// <summary>
        /// Gets the part restorer.
        /// </summary>
        public IPartRestorer PartRestorer => _partRestorer;

        /// <summary>
        /// Gets the food debt system.
        /// </summary>
        public IFoodDebtSystem FoodDebtSystem => _debtSystem;

        /// <summary>
        /// Gets the healing processor for living pawns.
        /// </summary>
        public EternalHealingProcessor HealingProcessor => _healingProcessor;

        /// <summary>
        /// Gets the corpse manager.
        /// </summary>
        public EternalCorpseManager CorpseManager => _corpseManager;

        /// <summary>
        /// Gets the corpse healing processor.
        /// </summary>
        public EternalCorpseHealingProcessor CorpseHealingProcessor => _corpseHealingProcessor;

        /// <summary>
        /// Gets the corpse preservation system.
        /// </summary>
        public EternalCorpsePreservation CorpsePreservation => _corpsePreservation;

        /// <summary>
        /// Gets the map protection system.
        /// </summary>
        public EternalMapProtection MapProtection => _mapProtection;

        /// <summary>
        /// Gets the regrowth manager.
        /// </summary>
        public EternalRegrowthManager RegrowthManager => _regrowthManager;

        /// <summary>
        /// Gets the SOS2 reflection cache. Initialized eagerly in FinalizeInit().
        /// Null until Initialize() has been called (before FinalizeInit they are the same session).
        /// </summary>
        public SOS2ReflectionCache SpaceModCache => _spaceModCache;

        /// <summary>
        /// Gets the healing threshold tracker for debuff hediffs.
        /// </summary>
        public HediffHealingThresholdTracker ThresholdTracker => _thresholdTracker;

        /// <summary>
        /// Gets the food cost processor for instant food drain and debt accumulation.
        /// </summary>
        public IFoodCostProcessor FoodCostProcessor => _foodCostProcessor;

        /// <summary>
        /// Gets the debt repayment processor for tick-based gradual food drain.
        /// </summary>
        public DebtRepaymentProcessor DebtRepaymentProcessor => _debtRepaymentProcessor;

        /// <summary>
        /// Whether the container has been initialized.
        /// </summary>
        public bool IsInitialized => _initialized;

        private EternalServiceContainer()
        {
            // Private constructor for singleton
        }

        /// <summary>
        /// Initializes all services with their dependencies.
        /// Must be called once during mod startup.
        /// </summary>
        /// <param name="debtSystem">The food debt system (created by Eternal_Component)</param>
        /// <param name="healingProcessor">The healing processor for living pawns</param>
        /// <param name="corpseManager">The corpse manager for tracking dead Eternals</param>
        /// <param name="corpseHealingProcessor">The corpse healing processor for resurrection</param>
        /// <param name="corpsePreservation">The corpse preservation system</param>
        /// <param name="mapProtection">The map protection system</param>
        /// <param name="regrowthManager">The regrowth manager for limb regeneration</param>
        public void Initialize(
            IFoodDebtSystem debtSystem,
            EternalHealingProcessor healingProcessor = null,
            EternalCorpseManager corpseManager = null,
            EternalCorpseHealingProcessor corpseHealingProcessor = null,
            EternalCorpsePreservation corpsePreservation = null,
            EternalMapProtection mapProtection = null,
            EternalRegrowthManager regrowthManager = null)
        {
            if (_initialized)
                return;

            // Validate required parameter
            if (debtSystem == null)
            {
                Log.Error("[Eternal] EternalServiceContainer.Initialize() called with null debtSystem. " +
                         "This is a required dependency and will cause NullReferenceExceptions.");
                return;
            }

            // Store manager services (use provided or fall back to singletons)
            _debtSystem = debtSystem;
            _healingProcessor = healingProcessor;
            _corpseManager = corpseManager;
            _corpseHealingProcessor = corpseHealingProcessor;
            _corpsePreservation = corpsePreservation;
            _mapProtection = mapProtection;
            _regrowthManager = regrowthManager;

            // Create core services with dependencies
            _settings = new SettingsAdapter();
            _rateCalculator = new UnifiedHealingRateCalculator(_settings);
            _hediffHealingCalculator = new UnifiedHediffHealingCalculator(_settings);
            _debtAccumulator = new UnifiedDebtAccumulator(_debtSystem);
            _partRestorer = new UnifiedPartRestorer(_settings);
            _thresholdTracker = new HediffHealingThresholdTracker();

            // Create food cost services (depend on settings and debt system)
            _foodCostProcessor = new FoodCostProcessor(_settings, _debtSystem);
            _debtRepaymentProcessor = new DebtRepaymentProcessor(_settings, _debtSystem);

            // Create SOS2 reflection cache (not initialized yet — that happens in FinalizeInit)
            _spaceModCache = new SOS2ReflectionCache();

            _initialized = true;
        }

        /// <summary>
        /// Updates manager references after loading from save.
        /// Called from Eternal_Component.ExposeData() in PostLoadInit mode.
        /// </summary>
        public void UpdateManagerReferences(
            EternalHealingProcessor healingProcessor,
            EternalCorpseManager corpseManager,
            EternalCorpseHealingProcessor corpseHealingProcessor,
            EternalCorpsePreservation corpsePreservation,
            EternalMapProtection mapProtection,
            EternalRegrowthManager regrowthManager)
        {
            _healingProcessor = healingProcessor;
            _corpseManager = corpseManager;
            _corpseHealingProcessor = corpseHealingProcessor;
            _corpsePreservation = corpsePreservation;
            _mapProtection = mapProtection;
            _regrowthManager = regrowthManager;
        }

        /// <summary>
        /// Resets the container (for testing or mod reload).
        /// </summary>
        public void Reset()
        {
            _initialized = false;

            // Core services
            _settings = null;
            _rateCalculator = null;
            _hediffHealingCalculator = null;
            _debtAccumulator = null;
            _partRestorer = null;
            _thresholdTracker = null;
            _foodCostProcessor = null;
            _debtRepaymentProcessor = null;

            // Manager services
            _debtSystem = null;
            _healingProcessor = null;
            _corpseManager = null;
            _corpseHealingProcessor = null;
            _corpsePreservation = null;
            _mapProtection = null;
            _regrowthManager = null;

            // Compatibility cache — FinalizeInit re-creates on next session
            _spaceModCache = null;
        }

        /// <summary>
        /// Creates a new instance (for testing - normally use Instance).
        /// </summary>
        public static EternalServiceContainer CreateNew()
        {
            return new EternalServiceContainer();
        }
    }
}
