/*
 * Relative Path: Eternal/Source/Eternal/Infrastructure/ImmutableSettingsSnapshot.cs
 * Creation Date: 19-02-2026
 * Last Edit: 12-03-2026
 * Author: 0Shard
 * Description: Immutable snapshot of all Eternal mod settings, captured once per tick batch.
 *              Eliminates repeated null-checks and property lookups on the hot path (PERF-03).
 *              All sections are readonly record structs — stack-allocated, zero GC pressure.
 *              Produced by Eternal_Settings.CreateSnapshot(); consumed by Phase 2 tick processors.
 *              v1.0.1: Added EffectsSection for consciousness buff, mood buff, and population cap.
 */

namespace Eternal.Infrastructure
{
    /// <summary>
    /// Immutable snapshot of all Eternal mod settings.
    /// Captured once at the start of each tick batch via <see cref="Eternal.Eternal_Settings.CreateSnapshot"/>.
    /// All fields are value types — the struct is stack-allocated with zero GC pressure.
    /// </summary>
    public readonly record struct ImmutableSettingsSnapshot
    {
        // -------------------------------------------------------------------------
        // Sections — mirror Eternal_Settings region organization
        // -------------------------------------------------------------------------

        public GeneralSection General { get; init; }
        public HealingSection Healing { get; init; }
        public ResourceSection Resource { get; init; }
        public FoodDebtSection FoodDebt { get; init; }
        public PerfSection Perf { get; init; }
        public AdvancedHediffSection AdvancedHediff { get; init; }
        public MapSection Map { get; init; }
        public EffectsSection Effects { get; init; }

        // -------------------------------------------------------------------------
        // Nested section types
        // -------------------------------------------------------------------------

        /// <summary>General settings section.</summary>
        public readonly record struct GeneralSection
        {
            public bool ModEnabled { get; init; }
            public bool DebugMode { get; init; }
            public int LoggingLevel { get; init; }
        }

        /// <summary>Healing settings section.</summary>
        public readonly record struct HealingSection
        {
            public float BaseRate { get; init; }
            public bool ShowEffects { get; init; }
            public bool ShowProgress { get; init; }
        }

        /// <summary>Resource settings section.</summary>
        public readonly record struct ResourceSection
        {
            public float NutritionCostMultiplier { get; init; }
            public bool PauseOnResourceDepletion { get; init; }
            public float MinimumNutritionThreshold { get; init; }
            public bool AllowResourceBorrowing { get; init; }
        }

        /// <summary>Food debt settings section.</summary>
        public readonly record struct FoodDebtSection
        {
            public float MaxDebtMultiplier { get; init; }
            public float FoodDrainThreshold { get; init; }
            public float MinDebtDrainRate { get; init; }
            public float MaxDebtDrainRate { get; init; }
            public float SeverityToNutritionRatio { get; init; }

            /// <summary>Maximum hunger rate multiplier at 100% food debt (Metabolic Recovery).</summary>
            public float HungerMultiplierCap { get; init; }

            /// <summary>Whether the Metabolic Recovery hunger rate boost is active.</summary>
            public bool HungerBoostEnabled { get; init; }
        }

        /// <summary>Performance settings section (tick rates and intervals).</summary>
        public readonly record struct PerfSection
        {
            public int NormalTickRate { get; init; }
            public int RareTickRate { get; init; }
            public int TraitCheckInterval { get; init; }
            public int CorpseCheckInterval { get; init; }
            public int MapCheckInterval { get; init; }

            /// <summary>
            /// Interval in ticks for sweeping stale healing history entries.
            /// Default: 300000 (~5 in-game days). Range: 60000–900000.
            /// </summary>
            public int HealingHistorySweepInterval { get; init; }
        }

        /// <summary>Advanced hediff settings section (scalar fields only; hediffManager excluded).</summary>
        public readonly record struct AdvancedHediffSection
        {
            public bool AutoHealEnabled { get; init; }
            public HealingOrder HealingOrder { get; init; }
            public bool EnableIndividualHediffControl { get; init; }
        }

        /// <summary>Map protection settings section.</summary>
        public readonly record struct MapSection
        {
            /// <summary>
            /// Map protection action string (e.g., "teleport").
            /// Strings are immutable in .NET — safe to snapshot without copying.
            /// </summary>
            public string MapProtectionAction { get; init; }
            public bool EnableMapAnchors { get; init; }
            public int AnchorGracePeriodTicks { get; init; }
            public bool EnableRoofCollapseProtection { get; init; }
        }

        /// <summary>
        /// Effects settings section: consciousness buff, mood buff, and population cap.
        /// Consumed by Phases 2, 3, and 4 for runtime behavior wiring.
        /// </summary>
        public readonly record struct EffectsSection
        {
            public bool ConsciousnessBuffEnabled { get; init; }
            public float ConsciousnessMultiplier { get; init; }
            public bool MoodBuffEnabled { get; init; }
            public int MoodBuffValue { get; init; }
            public bool PopulationCapEnabled { get; init; }
            public int PopulationCap { get; init; }
        }
    }
}
