// Relative Path: Eternal/Source/Eternal/Healing/TypeSpecificHealing.cs
// Creation Date: 09-11-2025
// Last Edit: 13-01-2026
// Author: 0Shard
// Description: Extensible type-specific healing dispatch using dictionary pattern.
//              All injury types heal at uniform linear rate.

using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Eternal.Utils;

namespace Eternal.Healing
{
    /// <summary>
    /// Delegate for type-specific healing handlers.
    /// </summary>
    /// <param name="pawn">The pawn being healed.</param>
    /// <param name="hediff">The hediff to heal.</param>
    /// <param name="amount">Base healing amount.</param>
    public delegate void HealingHandler(Pawn pawn, Hediff hediff, float amount);

    /// <summary>
    /// Extensible type-specific healing dispatch system.
    /// Uses dictionary-based dispatch for O(1) lookup and easy extensibility.
    /// </summary>
    public class TypeSpecificHealing
    {
        private static TypeSpecificHealing _instance;
        public static TypeSpecificHealing Instance => _instance ??= new TypeSpecificHealing();

        private readonly Dictionary<HealingType, HealingHandler> _healers;

        #region Constants

        private const float CRITICAL_MULTIPLIER = 1.0f;
        private const float DISEASE_MULTIPLIER = 1.0f;
        private const float IMMUNITY_BOOST = 0.1f;

        #endregion

        /// <summary>
        /// Initializes the healing dispatch system with default handlers.
        /// </summary>
        public TypeSpecificHealing()
        {
            _healers = new Dictionary<HealingType, HealingHandler>
            {
                { HealingType.Critical, HealCritical },
                { HealingType.Disease, HealDisease },
                { HealingType.Injury, HealInjury },
                { HealingType.Condition, HealCondition },
                { HealingType.Scar, HealScar },
                { HealingType.Regrowth, HealRegrowth },
                { HealingType.Misc, HealMisc }
            };
        }

        #region Dispatch API

        /// <summary>
        /// Heals a hediff using the appropriate handler for its type.
        /// </summary>
        public void Heal(Pawn pawn, HealingItem item, float healingAmount)
        {
            if (pawn == null || item?.Hediff == null)
                return;

            if (_healers.TryGetValue(item.Type, out var healer))
            {
                healer(pawn, item.Hediff, healingAmount);
            }
            else
            {
                ApplyStandardHealing(item.Hediff, healingAmount);
            }
        }

        /// <summary>
        /// Heals a hediff by type using the appropriate handler.
        /// Static convenience method for backwards compatibility.
        /// </summary>
        public static void HealByType(Pawn pawn, HealingItem item, float healingAmount)
        {
            Instance.Heal(pawn, item, healingAmount);
        }

        #endregion

        #region Type-Specific Handlers

        /// <summary>
        /// Heals a critical condition with accelerated healing.
        /// </summary>
        private void HealCritical(Pawn pawn, Hediff hediff, float amount)
        {
            if (pawn == null || hediff == null)
                return;

            ApplyStandardHealing(hediff, amount * CRITICAL_MULTIPLIER);

            // Blood loss: refresh capacities
            if (hediff.def.defName.Contains("BloodLoss") && pawn.health?.capacities != null)
            {
                pawn.health.capacities.Notify_CapacityLevelsDirty();
            }
        }

        /// <summary>
        /// Heals a disease with immune system support.
        /// </summary>
        private void HealDisease(Pawn pawn, Hediff hediff, float amount)
        {
            if (pawn == null || hediff == null)
                return;

            ApplyStandardHealing(hediff, amount * DISEASE_MULTIPLIER);
            BoostImmunity(pawn, hediff);
        }

        /// <summary>
        /// Heals an injury with tissue regeneration.
        /// </summary>
        private void HealInjury(Pawn pawn, Hediff hediff, float amount)
        {
            if (pawn == null || hediff == null)
                return;

            ApplyStandardHealing(hediff, amount);
        }

        /// <summary>
        /// Heals a general condition.
        /// </summary>
        private void HealCondition(Pawn pawn, Hediff hediff, float amount)
        {
            if (pawn == null || hediff == null)
                return;

            ApplyStandardHealing(hediff, amount);
        }

        /// <summary>
        /// Heals a scar with gradual severity reduction.
        /// </summary>
        private void HealScar(Pawn pawn, Hediff hediff, float amount)
        {
            if (pawn == null || hediff == null)
                return;

            // Scars heal slower than regular injuries
            ApplyStandardHealing(hediff, amount * 0.5f);
        }

        /// <summary>
        /// Handles regrowth healing (delegates to regrowth system).
        /// </summary>
        private void HealRegrowth(Pawn pawn, Hediff hediff, float amount)
        {
            // Regrowth is handled by EternalRegrowthState
            // This is a placeholder for direct regrowth healing if needed
            if (pawn == null || hediff == null)
                return;

            ApplyStandardHealing(hediff, amount);
        }

        /// <summary>
        /// Heals miscellaneous conditions.
        /// </summary>
        private void HealMisc(Pawn pawn, Hediff hediff, float amount)
        {
            if (pawn == null || hediff == null)
                return;

            ApplyStandardHealing(hediff, amount);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Applies standard healing to a hediff.
        /// </summary>
        public static void ApplyStandardHealing(Hediff hediff, float amount)
        {
            if (hediff == null || amount <= 0f)
                return;

            hediff.Severity -= amount;

            // Remove if fully healed
            if (hediff.Severity <= 0f && hediff.pawn != null)
            {
                hediff.pawn.health.RemoveHediff(hediff);
                EternalLogger.Debug($"Removed {hediff.def.LabelCap} from {hediff.pawn.Name?.ToStringShort ?? "Unknown"}");
            }
        }

        /// <summary>
        /// Boosts immunity for a disease hediff.
        /// </summary>
        private void BoostImmunity(Pawn pawn, Hediff hediff)
        {
            var immunity = pawn?.health?.immunity;
            if (immunity == null || hediff?.def == null)
                return;

            if (!immunity.ImmunityRecordExists(hediff.def))
                return;

            var immunityRecord = immunity.GetImmunityRecord(hediff.def);
            if (immunityRecord != null)
            {
                immunityRecord.immunity += IMMUNITY_BOOST;
            }
        }

        #endregion
    }
}
