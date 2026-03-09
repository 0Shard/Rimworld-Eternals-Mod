// Relative Path: Eternal/Source/Eternal/Healing/EternalHealingPriority.cs
// Creation Date: 03-12-2025
// Last Edit: 24-02-2026
// Author: 0Shard
// Description: Time-based priority system for Eternal healing with uniform healing speed.
//              Uses extension methods from Eternal.Extensions for hediff classification.
//              Uses configurable severity-to-nutrition ratio (default 250:1).
//              Regrowth cost scales by body part HP (larger parts cost more nutrition).
//              Healing time calculation uses actual healing rates: severity / (baseHealingRate * bodySize) * tickInterval.
//              RC4-FIX: HealingItem implements IExposable for save/load persistence in PreCalculatedHealingQueue.
//              Scalar fields (Severity, Type, EnergyCost, etc.) are saved via Scribe_Values.
//              Hediff and Pawn references use Scribe_References — they become null after load on dead pawns,
//              which is acceptable since the pre-calculated queue only uses scalar data when consumed.

using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Eternal.Compat;
using Eternal.Corpse;
using Eternal.Exceptions;
using Eternal.Extensions;
using Eternal.Utils;

namespace Eternal
{
    /// <summary>
    /// Represents a healing item with its calculated priority and healing information.
    /// Implements IExposable so that PreCalculatedHealingQueue (captured at death) survives save/load.
    /// Scalar fields are fully persisted. Hediff and Pawn use Scribe_References — they become null
    /// after load when the dead pawn's hediffs are gone, which is safe because the pre-calculated queue
    /// is consumed by StartCorpseHealing() which only reads Severity, Type, and EnergyCost.
    /// Properties are backed by fields because Scribe requires ref parameters (cannot ref auto-props).
    /// </summary>
    public class HealingItem : IExposable
    {
        // Backing fields — Scribe requires ref parameters so we cannot use auto-properties directly
        private Hediff _hediff;
        private float _healingPriority;
        private float _estimatedHealingTime;
        private HealingType _type;
        private bool _isCritical;
        private bool _isHarmful;
        private float _severity;
        private Pawn _pawn;
        private float _energyCost;

        // Properties — preserve the existing public API unchanged so all callers compile cleanly
        public Hediff Hediff         { get => _hediff;              set => _hediff = value;              }
        public float HealingPriority { get => _healingPriority;     set => _healingPriority = value;     }
        public float EstimatedHealingTime { get => _estimatedHealingTime; set => _estimatedHealingTime = value; }
        public HealingType Type      { get => _type;                set => _type = value;                }
        public bool IsCritical       { get => _isCritical;          set => _isCritical = value;          }
        public bool IsHarmful        { get => _isHarmful;           set => _isHarmful = value;           }
        public float Severity        { get => _severity;            set => _severity = value;            }
        public Pawn Pawn             { get => _pawn;                set => _pawn = value;                }
        public float EnergyCost      { get => _energyCost;          set => _energyCost = value;          }

        /// <summary>
        /// Parameterless constructor required by Scribe_Collections with LookMode.Deep.
        /// </summary>
        public HealingItem() { }

        /// <summary>
        /// Serializes all fields for save/load persistence.
        /// Hediff and Pawn use Scribe_References and may be null after loading dead-pawn data.
        /// All scalar fields are fully saved and restored via Scribe_Values.
        /// </summary>
        public void ExposeData()
        {
            // Scalar fields — always present and fully recoverable after load
            Scribe_Values.Look(ref _healingPriority, "healingPriority", 0f);
            Scribe_Values.Look(ref _estimatedHealingTime, "estimatedHealingTime", 0f);
            Scribe_Values.Look(ref _type, "type", HealingType.Misc);
            Scribe_Values.Look(ref _isCritical, "isCritical", false);
            Scribe_Values.Look(ref _isHarmful, "isHarmful", false);
            Scribe_Values.Look(ref _severity, "severity", 0f);
            Scribe_Values.Look(ref _energyCost, "energyCost", 0f);

            // Live references — may be null after load when dead pawn hediffs have been removed.
            // The pre-calculated queue only needs scalar data (Severity, Type, EnergyCost) when
            // consumed by StartCorpseHealing(), so null references here are safe.
            Scribe_References.Look(ref _hediff, "hediff");
            Scribe_References.Look(ref _pawn, "pawn");
        }
    }

    /// <summary>
    /// Types of healing operations for categorization.
    /// </summary>
    public enum HealingType
    {
        Critical,   // Life-threatening conditions
        Injury,     // Physical injuries and wounds
        Disease,    // Diseases and infections
        Scar,       // Scars and permanent damage
        Regrowth,   // Body part regrowth
        Condition,  // Other conditions
        Misc        // Miscellaneous healing needs
    }

    /// <summary>
    /// Time-based priority system for Eternal healing with uniform healing speed.
    /// All conditions heal at the same speed, prioritized by total healing time.
    /// </summary>
    public static class EternalHealingPriority
    {
        #region Constants

        /// <summary>
        /// Default energy cost multiplier: 250 severity = 1.0 nutrition.
        /// Actual ratio is configurable via settings.severityToNutritionRatio.
        /// </summary>
        private const float DEFAULT_ENERGY_COST_MULTIPLIER = 0.004f;

        #endregion

        #region Priority Calculation

        /// <summary>
        /// Calculates healing priority based on estimated healing time.
        /// </summary>
        public static float CalculatePriority(HealingItem healingItem)
        {
            if (healingItem == null) return float.MaxValue;
            return healingItem.EstimatedHealingTime;
        }

        /// <summary>
        /// Calculates healing time for a healing item using actual healing rates.
        /// Formula: (severity / healPerTick) * tickInterval where healPerTick = baseHealingRate * bodySize
        /// </summary>
        /// <param name="healingItem">The healing item containing severity and pawn reference</param>
        /// <param name="baseHealingRate">Base healing rate from settings (default 0.01)</param>
        /// <param name="tickInterval">Tick interval for this healing type (60 for injuries, 250 for regrowth)</param>
        /// <returns>Estimated healing time in ticks</returns>
        public static float CalculateHealingTime(HealingItem healingItem, float baseHealingRate, int tickInterval)
        {
            if (healingItem == null) return 0f;

            float bodySize = healingItem.Pawn?.BodySize ?? 1.0f;
            float healPerTick = baseHealingRate * bodySize;

            // Prevent division by zero
            if (healPerTick <= 0f) healPerTick = 0.0001f;

            // Total ticks = (severity / heal_per_tick) * tickInterval
            // Because healing happens every tickInterval ticks, not every tick
            float totalTicks = (healingItem.Severity / healPerTick) * tickInterval;

            return totalTicks;
        }

        /// <summary>
        /// Calculates healing time for a healing item using settings-based healing rates.
        /// Automatically determines tick interval based on healing type.
        /// </summary>
        /// <param name="healingItem">The healing item</param>
        /// <returns>Estimated healing time in ticks</returns>
        public static float CalculateHealingTime(HealingItem healingItem)
        {
            if (healingItem == null) return 0f;

            // Get settings via guaranteed non-null accessor (SAFE-08)
            var s = Eternal_Mod.GetSettings();
            float baseHealingRate = s.baseHealingRate;
            int normalTickRate = s.normalTickRate;
            int rareTickRate = s.rareTickRate;

            // Regrowth and scars use rareTickRate, injuries use normalTickRate
            int tickInterval = (healingItem.Type == HealingType.Regrowth || healingItem.Type == HealingType.Scar)
                ? rareTickRate
                : normalTickRate;

            return CalculateHealingTime(healingItem, baseHealingRate, tickInterval);
        }

        /// <summary>
        /// Calculates energy cost for a healing item.
        /// Uses configurable severity-to-nutrition ratio (default 250:1).
        /// No type-specific multipliers - all healing costs the same per severity.
        /// </summary>
        public static float CalculateEnergyCost(HealingItem healingItem)
        {
            if (healingItem == null) return 0f;

            // Uniform cost: severity directly determines nutrition cost
            // Uses configurable ratio (default 250:1). GetSettings() guarantees non-null (SAFE-08).
            return healingItem.Severity * Eternal_Mod.GetSettings().severityToNutritionRatio;
        }

        #endregion

        #region Hediff Classification (delegates to extensions)

        /// <summary>
        /// Determines the healing type for a hediff.
        /// </summary>
        public static HealingType GetHealingType(Hediff hediff)
        {
            return hediff.GetHealingType();
        }

        /// <summary>
        /// Checks if a hediff is critical.
        /// </summary>
        public static bool IsCriticalHediff(Hediff hediff)
        {
            return hediff.IsCritical();
        }

        /// <summary>
        /// Checks if a body part is critical.
        /// </summary>
        public static bool IsCriticalBodyPart(BodyPartRecord part)
        {
            return part.IsCritical();
        }

        /// <summary>
        /// Checks if a hediff is harmful and should be healed.
        /// </summary>
        public static bool IsHarmfulHediff(Hediff hediff)
        {
            return hediff.IsHarmful();
        }

        #endregion

        #region Factory Methods

        /// <summary>
        /// Creates a healing item from a hediff with calculated time and energy cost.
        /// </summary>
        public static HealingItem CreateHealingItem(Hediff hediff, Pawn pawn)
        {
            if (hediff == null || pawn == null) return null;

            try
            {
                var healingItem = new HealingItem
                {
                    Hediff = hediff,
                    Pawn = pawn,
                    Severity = hediff.Severity,
                    Type = hediff.GetHealingType(),
                    IsCritical = hediff.IsCritical(),
                    IsHarmful = hediff.IsHarmful()
                };

                healingItem.EstimatedHealingTime = CalculateHealingTime(healingItem);
                healingItem.EnergyCost = CalculateEnergyCost(healingItem);
                healingItem.HealingPriority = CalculatePriority(healingItem);

                return healingItem;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "CreateHealingItem", pawn, ex);
                return null;
            }
        }

        /// <summary>
        /// Creates a healing item for a missing body part.
        /// Regrowth cost scales by body part HP (larger parts cost more nutrition).
        /// </summary>
        public static HealingItem CreateMissingPartHealingItem(Hediff_MissingPart missingPart, Pawn pawn)
        {
            if (missingPart == null || pawn == null) return null;

            try
            {
                // Regrowth severity equals partMaxHP so cost scales with body part size
                // Uses EBFCompat for Elite Bionics Framework compatibility (soft dependency)
                float partMaxHP = missingPart.Part != null
                    ? EBFCompat.GetMaxHealth(missingPart.Part, pawn)
                    : 1.0f;

                var healingItem = new HealingItem
                {
                    Hediff = missingPart,
                    Pawn = pawn,
                    Severity = partMaxHP, // Larger parts = higher severity = higher cost
                    Type = HealingType.Regrowth,
                    IsCritical = missingPart.Part.IsCritical(),
                    IsHarmful = true
                };

                healingItem.EstimatedHealingTime = CalculateHealingTime(healingItem);
                healingItem.EnergyCost = CalculateEnergyCost(healingItem);
                healingItem.HealingPriority = CalculatePriority(healingItem);

                return healingItem;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "CreateMissingPartHealingItem", pawn, ex);
                return null;
            }
        }

        #endregion

        #region Utility

        /// <summary>
        /// Formats resurrection time with smart units (minutes, hours, or days).
        /// Provides human-friendly time estimates based on actual tick counts.
        /// </summary>
        /// <param name="totalTicks">Total ticks for the healing operation</param>
        /// <returns>Formatted time string with appropriate units</returns>
        public static string FormatResurrectionTime(float totalTicks)
        {
            if (totalTicks <= 0f) return "instant";

            float totalDays = totalTicks / GenDate.TicksPerDay;

            if (totalDays < 1f / 24f)  // Less than 1 hour
            {
                int minutes = Math.Max(1, (int)(totalDays * 24f * 60f));
                return $"{minutes} minute{(minutes == 1 ? "" : "s")}";
            }
            else if (totalDays < 1f)  // Less than 1 day
            {
                float hours = totalDays * 24f;
                return $"{hours:F1} hours";
            }
            else
            {
                return $"{totalDays:F1} days";
            }
        }

        #endregion
    }
}
