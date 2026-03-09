/*
 * Relative Path: Eternal/Source/Eternal/Healing/EternalHealingProcessor.cs
 * Creation Date: 01-01-2026
 * Last Edit: 21-02-2026
 * Author: 0Shard
 * Description: Centralized healing processor that manages all Eternal healing operations.
 *              Optimized to use per-tick cached pawn queries for improved performance.
 *              05-02: Exposed HediffHealer property for reactive and periodic healing history cleanup (SAFE-07).
 */

using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Eternal.DI;
using Eternal.Extensions;
using Eternal.Utils;
using Eternal.Interfaces;
using Eternal.Resources;

namespace Eternal
{
    /// <summary>
    /// Centralized healing processor that manages all Eternal healing operations.
    /// Simplified version without optimization system.
    /// </summary>
    public class EternalHealingProcessor
    {
        private EternalHediffHealer hediffHealer = new EternalHediffHealer();
        private EternalScarHealing scarHealing = new EternalScarHealing();
        public EternalScarHealing ScarHealing => scarHealing;

        /// <summary>
        /// Gets the hediff healer for external callers that need direct access (e.g., reactive cleanup).
        /// </summary>
        public EternalHediffHealer HediffHealer => hediffHealer;

        private int lastCleanupTick = 0;

        /// <summary>
        /// Gets food debt system instance.
        /// </summary>
        public IFoodDebtSystem FoodDebtSystem => EternalServiceContainer.Instance.FoodDebtSystem;

        /// <summary>
        /// Creates a new healing processor.
        /// </summary>
        public EternalHealingProcessor()
        {
            EternalLogger.Info("EternalHealingProcessor initialized");
        }

        /// <summary>
        /// Processes normal healing operations for living Eternals.
        /// Called by Eternal_Component at the configured normalTickRate.
        /// Uses cached pawn query for performance - no allocations per tick.
        /// </summary>
        public void ProcessNormalHealing()
        {
            // PERF: Use cached query - eliminates 2 List allocations per normal tick
            var livingEternals = PawnExtensions.GetAllLivingEternalPawnsCached();

            foreach (var pawn in livingEternals)
            {
                // Check if pawn can heal (has food, not in critical state, etc.)
                if (!CanHeal(pawn))
                {
                    continue;
                }

                // Process individual hediff healing directly
                hediffHealer.ProcessHediffHealing(pawn);

                // Process scar healing directly
                scarHealing.ProcessScarHealing();

                // Check for excessive food debt and pause if needed
                if (FoodDebtSystem.HasExcessiveDebt(pawn))
                {
                    EternalLogger.Info($"{pawn.Name?.ToStringShort ?? "Unknown"} healing paused due to excessive food debt");
                    continue;
                }
            }
        }

        /// <summary>
        /// Processes rare healing operations and deep healing checks.
        /// Called by Eternal_Component at the configured rareTickRate.
        /// Uses cached pawn query for performance.
        /// </summary>
        public void ProcessRareHealing()
        {
            // PERF: Use cached query - eliminates List allocation per rare tick
            var allEternals = PawnExtensions.GetAllEternalPawnsCached();

            foreach (var pawn in allEternals)
            {
                // Process both dead and living Eternals for rare healing
                ProcessPawnRareHealing(pawn);
            }
        }

        /// <summary>
        /// Processes rare healing operations for a specific pawn.
        /// </summary>
        private void ProcessPawnRareHealing(Pawn pawn)
        {
            if (pawn == null) return;

            // Register for food debt tracking if not already registered
            if (pawn.Dead)
            {
                FoodDebtSystem.RegisterPawn(pawn);
            }
            else
            {
                // Check if pawn should still be tracked for debt repayment
                if (FoodDebtSystem.GetDebt(pawn) > 0f)
                {
                    // Debt repayment is handled automatically via Pawn_Ingestion_Patch
                    // when pawns eat food
                }
                else
                {
                    // No debt, can unregister
                    FoodDebtSystem.UnregisterPawn(pawn);
                }
            }
        }

        /// <summary>
        /// Updates food debt systems - called by Eternal_Component on rare ticks.
        /// Performs periodic cleanup of invalid entries.
        /// Note: Debt accumulation happens during healing, repayment via food consumption.
        /// </summary>
        public void UpdateFoodDebtSystems()
        {
            int currentTick = Find.TickManager.TicksGame;

            // Periodic cleanup (every 10,000 ticks = ~2.8 minutes)
            if (currentTick - lastCleanupTick >= 10000)
            {
                hediffHealer.PerformPeriodicCleanup();
                FoodDebtSystem.CleanupInvalidEntries();
                lastCleanupTick = currentTick;
            }
        }

        /// <summary>
        /// Checks if a pawn can heal based on current conditions.
        /// Simplified system - just checks basic conditions.
        /// </summary>
        /// <param name="pawn">The pawn to check.</param>
        /// <returns>True if pawn can heal, false otherwise.</returns>
        private bool CanHeal(Pawn pawn)
        {
            if (pawn == null || pawn.health?.hediffSet == null)
                return false;

            // Don't heal dead pawns
            if (pawn.Dead)
                return false;

            // REMOVED: IsInCriticalState() check was blocking healing when pawns needed it most
            // Eternals SHOULD heal even in critical states - that's the entire point of immortality

            return true;
        }

        /// <summary>
        /// Starts regrowth process for a pawn.
        /// </summary>
        public void StartRegrowth(Pawn pawn)
        {
            if (pawn == null) return;

            // Log regrowth start
            EternalLogger.Info($"Started regrowth process for {pawn.Name?.ToStringShort ?? "Unknown"}");
        }

        /// <summary>
        /// Gets healing status for a pawn.
        /// </summary>
        /// <param name="pawn">The pawn to check.</param>
        /// <returns>Healing status message.</returns>
        public string GetPawnHealingStatus(Pawn pawn)
        {
            if (pawn == null) return "Unknown";
            if (pawn.Dead) return "Dead";
            if (pawn.health?.hediffSet == null) return "No health data";
            
            // Count harmful hediffs
            int harmfulCount = pawn.health.hediffSet.hediffs.Count(h => h.def.isBad);
            
            return harmfulCount > 0 ? $"Healing ({harmfulCount} conditions)" : "Healthy";
        }

        /// <summary>
        /// Initializes healing system for a new game or load.
        /// </summary>
        public void Initialize()
        {
            // Register all existing dead Eternals for debt tracking
            // PERF: Use cached query - runs once on load so allocation is acceptable
            var allEternals = PawnExtensions.GetAllEternalPawnsCached();
            foreach (var pawn in allEternals)
            {
                if (pawn.Dead)
                {
                    FoodDebtSystem.RegisterPawn(pawn);
                }
            }

            EternalLogger.Info($"EternalHealingProcessor initialized with {allEternals.Count} Eternals tracked");
        }

        /// <summary>
        /// Cleans up resources when component is destroyed.
        /// </summary>
        public void Cleanup()
        {
            // Clean up tracked pawns
            foreach (var pawn in FoodDebtSystem.GetTrackedPawns().ToList())
            {
                FoodDebtSystem.ClearDebt(pawn);
            }
            hediffHealer.ClearHealingProgress();
            EternalLogger.Info("EternalHealingProcessor cleaned up");
        }
    }
}
