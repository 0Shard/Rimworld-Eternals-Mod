// Relative Path: Eternal/Source/Eternal/Healing/EternalResurrectionCalculator.cs
// Creation Date: 09-11-2025
// Last Edit: 04-03-2026
// Author: 0Shard
// Description: Calculates healing requirements, costs, and time estimates for Eternal corpse resurrection.
//              Delegates to EternalHealingPriority for HealingItem creation to ensure EnergyCost is set.
//              Resurrection debt is capped at RESURRECTION_DEBT_CAP (2.0) × pawn nutrition capacity.
//              Time estimates use actual healing rates: severity / (baseHealingRate * bodySize) * tickInterval.

using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Eternal.Corpse;
using Eternal.Exceptions;
using Eternal.Extensions;
using Eternal.Utils;

namespace Eternal.Healing
{
    /// <summary>
    /// Calculates healing requirements, costs, and time estimates for Eternal corpse resurrection.
    /// Uses EternalHealingPriority factory methods for consistent HealingItem creation.
    /// </summary>
    public class EternalResurrectionCalculator
    {

        /// <summary>
        /// Calculates the complete healing queue for a dead pawn.
        /// </summary>
        /// <param name="pawn">The dead pawn to analyze</param>
        /// <returns>Sorted list of healing items by time-based priority</returns>
        public List<HealingItem> CalculateHealingQueue(Pawn pawn)
        {
            var healingItems = new List<HealingItem>();

            try
            {
                if (pawn == null || pawn.health == null || pawn.health.hediffSet == null)
                {
                    Log.Warning("[Eternal] Cannot calculate healing queue for null pawn");
                    return healingItems;
                }

                // Analyze all hediffs (injuries, diseases, conditions, etc.)
                foreach (var hediff in pawn.health.hediffSet.hediffs)
                {
                    if (ShouldHealHediff(hediff))
                    {
                        var healingItem = CreateHealingItem(hediff, pawn);
                        if (healingItem != null)
                        {
                            healingItems.Add(healingItem);
                        }
                    }
                }

                // Add missing body parts for regrowth
                var missingParts = pawn.health.hediffSet.GetMissingPartsCommonAncestors();
                foreach (var missingPart in missingParts)
                {
                    var healingItem = CreateMissingPartHealingItem(missingPart, pawn);
                    if (healingItem != null)
                    {
                        healingItems.Add(healingItem);
                    }
                }

                // No priority ordering: return the queue as-is so all items can
                // progress in parallel during corpse healing.
                return healingItems;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "CalculateHealingQueue", pawn, ex);
                return healingItems;
            }
        }

        /// <summary>
        /// Calculates the total nutrition cost for a healing queue.
        /// Uses EnergyCost from HealingItems created by EternalHealingPriority.
        /// </summary>
        /// <param name="healingItems">The healing items to calculate cost for</param>
        /// <returns>Total nutrition cost</returns>
        public float CalculateTotalCost(List<HealingItem> healingItems)
        {
            if (healingItems == null)
                return 0f;

            // Use EnergyCost set by EternalHealingPriority factory methods
            return healingItems.Sum(item => item?.EnergyCost ?? 0f);
        }

        /// <summary>
        /// Calculates the total nutrition cost for a healing queue, capped at 2.0 × pawn's nutrition capacity.
        /// This prevents resurrection from accumulating excessive debt.
        /// </summary>
        /// <param name="healingItems">The healing items to calculate cost for</param>
        /// <param name="pawn">The pawn being resurrected (for nutrition cap calculation)</param>
        /// <returns>Total nutrition cost, capped at 2.0 × nutrition capacity</returns>
        public float CalculateTotalCostCapped(List<HealingItem> healingItems, Pawn pawn)
        {
            float uncappedCost = CalculateTotalCost(healingItems);
            float costCap = GetResurrectionCostCap(pawn);
            return Math.Min(uncappedCost, costCap);
        }

        /// <summary>
        /// Gets the maximum resurrection debt cap for a pawn.
        /// Cap is 2.0 × pawn's max nutrition (or 2.0 × bodySize if no food need).
        /// This prevents resurrection from accumulating excessive debt.
        /// </summary>
        /// <param name="pawn">The pawn to calculate cap for</param>
        /// <returns>Maximum resurrection debt</returns>
        public float GetResurrectionCostCap(Pawn pawn)
        {
            // Debt cap: 2.0 × nutrition capacity
            // Keeps resurrection cost reasonable even for heavily damaged pawns
            const float RESURRECTION_DEBT_CAP = 2.0f;

            if (pawn?.needs?.food != null)
            {
                return RESURRECTION_DEBT_CAP * pawn.needs.food.MaxLevel;
            }

            // Fallback for dead pawns without food need: use body size
            float bodySize = pawn?.BodySize ?? 1.0f;
            return RESURRECTION_DEBT_CAP * bodySize;
        }

        /// <summary>
        /// Calculates estimated time to complete healing in ticks.
        /// Uses actual healing rates from HealingItem.EstimatedHealingTime (already calculated correctly).
        /// </summary>
        /// <param name="healingItems">The healing items to calculate time for</param>
        /// <returns>Estimated time in ticks</returns>
        public float CalculateEstimatedTimeTicks(List<HealingItem> healingItems)
        {
            if (healingItems == null || healingItems.Count == 0)
                return 0f;

            // HealingItems already have EstimatedHealingTime calculated using actual healing rates
            // via EternalHealingPriority.CalculateHealingTime()
            return healingItems.Sum(item => item?.EstimatedHealingTime ?? 0f);
        }

        /// <summary>
        /// Calculates estimated time to complete healing.
        /// </summary>
        /// <param name="healingItems">The healing items to calculate time for</param>
        /// <returns>Estimated time in days</returns>
        public float CalculateEstimatedTime(List<HealingItem> healingItems)
        {
            float totalTicks = CalculateEstimatedTimeTicks(healingItems);
            return totalTicks / GenDate.TicksPerDay;
        }

        /// <summary>
        /// Determines if a hediff should be included in healing.
        /// </summary>
        /// <param name="hediff">The hediff to check</param>
        /// <returns>True if the hediff should be healed</returns>
        private bool ShouldHealHediff(Hediff hediff)
        {
            if (hediff == null)
                return false;

            // Don't heal Eternal Essence hediff itself
            if (hediff.def == EternalDefOf.Eternal_Essence)
                return false;

            // Don't include Metabolic Recovery — severity is debt-driven, not healed by the system
            // Excluded by type (09-02) AND by def (09-03 after DefOf binding is live).
            if (hediff is Eternal.Hediffs.MetabolicRecovery_Hediff)
                return false;
            if (EternalDefOf.Eternal_MetabolicRecovery != null
                && hediff.def == EternalDefOf.Eternal_MetabolicRecovery)
                return false;

            // Heal any hediff that has severity > 0 and is harmful
            return hediff.Severity > 0.01f && IsHarmfulHediff(hediff);
        }

        /// <summary>
        /// Checks if a hediff is harmful and should be healed.
        /// Delegates to extension method for consistency.
        /// </summary>
        private bool IsHarmfulHediff(Hediff hediff) => hediff.IsHarmful();

        /// <summary>
        /// Creates a healing item for a specific hediff.
        /// Delegates to EternalHealingPriority to ensure EnergyCost is properly calculated.
        /// </summary>
        /// <param name="hediff">The hediff to heal</param>
        /// <param name="pawn">The pawn being healed</param>
        /// <returns>Healing item or null if not applicable</returns>
        private HealingItem CreateHealingItem(Hediff hediff, Pawn pawn)
        {
            // Delegate to EternalHealingPriority factory method which properly sets EnergyCost
            return EternalHealingPriority.CreateHealingItem(hediff, pawn);
        }

        /// <summary>
        /// Creates a healing item for a missing body part.
        /// Delegates to EternalHealingPriority to ensure EnergyCost is properly calculated.
        /// </summary>
        /// <param name="missingPart">The missing body part</param>
        /// <param name="pawn">The pawn being healed</param>
        /// <returns>Healing item or null if not applicable</returns>
        private HealingItem CreateMissingPartHealingItem(Hediff_MissingPart missingPart, Pawn pawn)
        {
            // Delegate to EternalHealingPriority factory method which properly sets EnergyCost
            return EternalHealingPriority.CreateMissingPartHealingItem(missingPart, pawn);
        }

    }
}