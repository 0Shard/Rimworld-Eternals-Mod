/*
 * Relative Path: Eternal/Source/Eternal/Components/TickOrchestrator.cs
 * Creation Date: 29-12-2025
 * Last Edit: 12-07-2026
 * SAFE-09: ProcessTick() early-returns when EternalModState.IsDisabled to prevent NRE floods
 *          when critical defs are missing.
 * Author: 0Shard
 * Description: Orchestrates tick-based processing for the Eternal mod.
 *              Extracted from Eternal_Component to reduce god class complexity.
 *              Manages timing intervals and delegates to specialized processors.
 *              PERF-08: Replaced 1000-tick cached settings with per-batch ImmutableSettingsSnapshot.
 *              Settings changes now apply immediately (next ProcessTick call) instead of after 1000 ticks.
 *              Regrowth progress scales with part max HP (see EternalRegrowthManager);
 *              food cost stays effort-based (healAmount per regrowing part per pass).
 *              Corpse injury healing now runs at normalTickRate (60 ticks) for parity with living pawns.
 *              05-02: Added healing history sweep timer and SweepStaleHealingHistory for SAFE-07 bounded cleanup.
 *              09-03: Added ProcessDebtRepayment() — syncs Metabolic Recovery hediff lifecycle and repays debt
 *                     proportional to extra food consumed via hunger rate boost. CheckFoodNeedStateChanges()
 *                     called each rare tick to clear debt when food need is disabled.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Eternal;
using Eternal.Extensions;
using Eternal.Healing;
using Eternal.Corpse;
using Eternal.Interfaces;
using Eternal.Caravan;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Hediffs;
using Eternal.Infrastructure;
using Eternal.Resources;
using Eternal.Utils;

namespace Eternal.Components
{
    /// <summary>
    /// Orchestrates all tick-based processing for the Eternal mod.
    /// Manages timing intervals and delegates to specialized processors.
    /// </summary>
    /// <remarks>
    /// TICK STRUCTURE:
    /// - Normal ticks (normalTickRate, default 60): Injury healing for living pawns
    /// - Rare ticks (rareTickRate, default 250): Scars, regrowth, corpse healing, food debt
    /// - Corpse checks (1000 ticks): Corpse preservation and cleanup
    /// - Map checks (500 ticks): Map protection for Eternal corpses
    /// - Trait checks (5000 ticks): Trait-hediff consistency
    /// </remarks>
    public class TickOrchestrator : IExposable
    {
        #region Timing State

        private int lastNormalHealTick = 0;
        private int lastRareHealTick = 0;
        private int lastTraitCheckTick = 0;
        private int lastCorpseCheckTick = 0;
        private int lastMapCheckTick = 0;
        private int lastHealingSweepTick = 0;

        #endregion

        #region Dependencies (Injected)

        private readonly EternalHealingProcessor _healingProcessor;
        private readonly EternalCorpseHealingProcessor _corpseHealingProcessor;
        private readonly EternalCorpseManager _corpseManager;
        private readonly EternalCorpsePreservation _corpsePreservation;
        private readonly EternalMapProtection _mapProtection;
        private readonly EternalRegrowthManager _regrowthManager;
        private readonly EternalCaravanDeathHandler _caravanDeathHandler;
        private readonly IFoodDebtSystem _foodDebtSystem;
        private readonly ISettingsProvider _settings;

        // Callback for trait-hediff consistency (component owns this logic)
        private readonly Action _checkTraitHediffConsistency;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new TickOrchestrator with all dependencies.
        /// </summary>
        public TickOrchestrator(
            EternalHealingProcessor healingProcessor,
            EternalCorpseHealingProcessor corpseHealingProcessor,
            EternalCorpseManager corpseManager,
            EternalCorpsePreservation corpsePreservation,
            EternalMapProtection mapProtection,
            EternalRegrowthManager regrowthManager,
            EternalCaravanDeathHandler caravanDeathHandler,
            IFoodDebtSystem foodDebtSystem,
            ISettingsProvider settings,
            Action checkTraitHediffConsistency)
        {
            _healingProcessor = healingProcessor;
            _corpseHealingProcessor = corpseHealingProcessor;
            _corpseManager = corpseManager;
            _corpsePreservation = corpsePreservation;
            _mapProtection = mapProtection;
            _regrowthManager = regrowthManager;
            _caravanDeathHandler = caravanDeathHandler;
            _foodDebtSystem = foodDebtSystem;
            _settings = settings;
            _checkTraitHediffConsistency = checkTraitHediffConsistency;
        }

        /// <summary>
        /// Parameterless constructor for serialization.
        /// Dependencies must be set via SetDependencies() after deserialization.
        /// </summary>
        public TickOrchestrator()
        {
            // Parameterless constructor for Scribe_Deep.Look
        }

        #endregion

        #region Tick Processing

        /// <summary>
        /// Processes all tick-based mechanics for the current game tick.
        /// </summary>
        public void ProcessTick()
        {
            // SAFE-09: early-return when mod is disabled due to missing critical defs.
            if (EternalModState.IsDisabled)
                return;

            if (!CanProcessTick())
                return;

            // PERF-08: Capture snapshot once per batch — ImmutableSettingsSnapshot is a readonly record struct
            // (stack-allocated, zero GC pressure). Settings changes apply immediately on the next call.
            var snapshot = Eternal_Mod.GetSettings().CreateSnapshot();

            if (!snapshot.General.ModEnabled)
                return;

            int currentTick = Find.TickManager.TicksGame;

            // Normal tick: injury healing
            if (currentTick - lastNormalHealTick >= snapshot.Perf.NormalTickRate)
            {
                ProcessNormalTick();
                lastNormalHealTick = currentTick;
            }

            // Rare tick: scars, regrowth, corpse healing, food debt
            if (currentTick - lastRareHealTick >= snapshot.Perf.RareTickRate)
            {
                ProcessRareTick();
                lastRareHealTick = currentTick;
            }

            // Corpse preservation
            if (currentTick - lastCorpseCheckTick >= snapshot.Perf.CorpseCheckInterval)
            {
                ProcessCorpseTick();
                lastCorpseCheckTick = currentTick;
            }

            // Map protection
            if (currentTick - lastMapCheckTick >= snapshot.Perf.MapCheckInterval)
            {
                ProcessMapTick();
                lastMapCheckTick = currentTick;
            }

            // Trait-hediff consistency
            if (currentTick - lastTraitCheckTick >= snapshot.Perf.TraitCheckInterval)
            {
                ProcessTraitTick();
                lastTraitCheckTick = currentTick;
            }

            // Healing history sweep: bounded cleanup of orphaned entries (SAFE-07)
            if (currentTick - lastHealingSweepTick >= snapshot.Perf.HealingHistorySweepInterval)
            {
                SweepStaleHealingHistory();
                lastHealingSweepTick = currentTick;
            }
        }

        /// <summary>
        /// Checks if tick processing can occur.
        /// </summary>
        private bool CanProcessTick()
        {
            return Find.TickManager != null && !Find.TickManager.Paused;
        }

        /// <summary>
        /// Processes normal tick (injury healing for living pawns and corpses).
        /// Corpse injury healing added for speed parity with living pawn healing.
        /// </summary>
        private void ProcessNormalTick()
        {
            _healingProcessor?.ProcessNormalHealing();
            _corpseHealingProcessor?.ProcessCorpseInjuryHealing();
        }

        /// <summary>
        /// Processes rare tick (scars, regrowth, corpse regrowth, food debt repayment).
        /// Note: Corpse injury healing moved to ProcessNormalTick for speed parity.
        /// </summary>
        private void ProcessRareTick()
        {
            UpdateAllRegrowth();
            UpdateCaravanDeaths();
            _healingProcessor?.ProcessRareHealing();
            _corpseHealingProcessor?.ProcessCorpseRegrowth();
            _healingProcessor?.UpdateFoodDebtSystems();

            // Tick-based debt repayment: gradually drain food bar when pawn has debt
            EternalServiceContainer.Instance.DebtRepaymentProcessor?.ProcessDebtRepayment();

            // Metabolic Recovery sync: check food need state changes, repay debt from extra hunger,
            // and synchronize hediff lifecycle (add when debt appears, remove when debt clears).
            try
            {
                var snapshot = Eternal_Mod.GetSettings().CreateSnapshot();
                ProcessDebtRepaymentAndSync(snapshot);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "ProcessDebtRepaymentAndSync", null, ex);
            }
        }

        /// <summary>
        /// Processes corpse tick (preservation and cleanup).
        /// </summary>
        private void ProcessCorpseTick()
        {
            _corpsePreservation?.MaintainPreservation();
            _corpseManager?.CleanupInvalidCorpses();
        }

        /// <summary>
        /// Processes map tick (map protection).
        /// </summary>
        private void ProcessMapTick()
        {
            _mapProtection?.CheckAndProtectMaps();
        }

        /// <summary>
        /// Processes trait tick (trait-hediff consistency and debt cleanup).
        /// </summary>
        private void ProcessTraitTick()
        {
            _checkTraitHediffConsistency?.Invoke();
            _foodDebtSystem?.CleanupInvalidEntries();
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Updates regrowth status for all Eternal pawns.
        /// Handles both starting new regrowth and progressing existing regrowth for living pawns.
        /// Dead pawns are handled by EternalCorpseHealingProcessor.
        /// PERF: Uses cached Eternal pawn query instead of iterating all spawned pawns.
        /// </summary>
        private void UpdateAllRegrowth()
        {
            if (_regrowthManager == null)
                return;

            // Get base healing rate for regrowth progression
            float baseHealRate = _settings.BaseHealingRate;

            // PERF: Use cached Eternals list - eliminates O(n) iteration of all spawned pawns
            // Previous: Iterated ALL spawned pawns (~100s) then filtered
            // Now: Iterates only Eternals (~1-10 typically)
            foreach (var pawn in PawnExtensions.GetAllEternalPawnsCached())
            {
                // Skip dead pawns - corpse processor handles them via ProcessCorpseHealing()
                if (pawn.Dead)
                    continue;

                // Check if pawn is already in regrowth
                bool isInRegrowth = _regrowthManager.IsPawnInRegrowth(pawn);

                // Start regrowth for pawns with missing parts (not already in regrowth)
                if (!isInRegrowth)
                {
                    var missingParts = pawn.health?.hediffSet?.GetMissingPartsCommonAncestors();
                    if (missingParts != null && missingParts.Any())
                    {
                        // Check resource availability before starting
                        bool hasDebtCapacity = _foodDebtSystem?.GetRemainingCapacity(pawn) > 0f;
                        float foodLevel = pawn.needs?.food?.CurLevel ?? 0f;
                        float maxFood = pawn.needs?.food?.MaxLevel ?? 1f;
                        bool canDrainFood = foodLevel > (maxFood * 0.15f);  // Above food drain threshold

                        if (hasDebtCapacity || canDrainFood)
                        {
                            _regrowthManager.StartRegrowth(pawn);
                            isInRegrowth = true;

                            if (_settings?.DebugMode == true)
                            {
                                Log.Message($"[Eternal] Started limb regrowth for living pawn {pawn.Name}");
                            }
                        }
                    }
                }

                // Progress regrowth for pawns currently regrowing
                if (isInRegrowth)
                {
                    ProgressLivingPawnRegrowth(pawn, baseHealRate);
                }
            }
        }

        /// <summary>
        /// Progresses regrowth for a living pawn and processes associated food costs.
        /// </summary>
        /// <param name="pawn">The pawn to progress regrowth for.</param>
        /// <param name="baseHealRate">Base healing rate from settings.</param>
        private void ProgressLivingPawnRegrowth(Pawn pawn, float baseHealRate)
        {
            // Check resource availability before progressing
            bool hasDebtCapacity = _foodDebtSystem?.GetRemainingCapacity(pawn) > 0f;
            float foodLevel = pawn.needs?.food?.CurLevel ?? 0f;
            float maxFood = pawn.needs?.food?.MaxLevel ?? 1f;
            bool canDrainFood = foodLevel > (maxFood * 0.15f);

            if (!hasDebtCapacity && !canDrainFood)
                return;

            // Get regrowing hediffs before progression to calculate food cost
            var regrowingHediffs = _regrowthManager.GetRegrowingHediffs(pawn).ToList();
            if (regrowingHediffs.Count == 0)
                return;

            // Calculate heal amount: baseRate * bodySize
            float healAmount = baseHealRate * pawn.BodySize;

            // Food cost is effort-based: each regrowing part consumes the full healAmount of
            // healing effort per pass (regrowth PROGRESS is divided by part HP in
            // EternalRegrowthManager, but the biological effort — and thus nutrition — is not).
            // Total cost of a part therefore scales with its HP: ~1.2 nutrition for an arm.
            float totalHealingEffort = regrowingHediffs.Count * healAmount;

            // Progress regrowth (advances severity on all regrowing hediffs, HP-scaled)
            _regrowthManager.ProgressRegrowth(pawn, healAmount);

            // Process food cost for regrowth (configurable ratio, default 250:1)
            if (totalHealingEffort > 0f)
            {
                float severityToNutritionRatio = _settings.SeverityToNutritionRatio;
                float nutritionCost = totalHealingEffort * severityToNutritionRatio;
                EternalServiceContainer.Instance.FoodCostProcessor?.ProcessHealingCost(pawn, nutritionCost);
            }

            if (_settings?.DebugMode == true)
            {
                float severityToNutritionRatio = _settings.SeverityToNutritionRatio;
                Log.Message($"[Eternal] Progressed regrowth for {pawn.Name}: " +
                           $"{regrowingHediffs.Count} parts, heal={healAmount:F4}, cost={totalHealingEffort * severityToNutritionRatio:F4}");
            }
        }

        /// <summary>
        /// Updates caravan deaths for all Eternal pawns.
        /// </summary>
        private void UpdateCaravanDeaths()
        {
            _caravanDeathHandler?.GameComponentUpdate();
        }

        /// <summary>
        /// Synchronizes the Metabolic Recovery hediff lifecycle for all tracked pawns and clears
        /// debt for pawns whose food need was disabled (e.g. fed by gene).
        ///
        /// Repayment itself is handled solely by DebtRepaymentProcessor (constant food-bar drain:
        /// peakDebt / (60000 × debtRepaymentDays) per tick). The hediff is a pure status display.
        /// </summary>
        private void ProcessDebtRepaymentAndSync(ImmutableSettingsSnapshot snapshot)
        {
            var foodDebtSystem = EternalServiceContainer.Instance.FoodDebtSystem;
            if (foodDebtSystem == null)
                return;

            // Clear debt for pawns whose food need has been disabled (genes, traits, ideology, race).
            // CheckFoodNeedStateChanges is on the concrete type — cast if available.
            if (foodDebtSystem is UnifiedFoodDebtManager concreteFoodDebt)
            {
                concreteFoodDebt.CheckFoodNeedStateChanges();
            }

            // Repayment itself is handled solely by DebtRepaymentProcessor (constant food-bar
            // drain). This method only keeps the Metabolic Recovery hediff display in sync.
            foreach (var pawn in foodDebtSystem.GetTrackedPawns())
            {
                if (pawn == null || pawn.Dead)
                    continue;

                // Sync hediff lifecycle (removes if zero debt, adds/updates severity if > 0)
                SyncMetabolicRecoveryHediff(pawn, foodDebtSystem);
            }
        }

        /// <summary>
        /// Synchronizes the Metabolic Recovery hediff lifecycle for a pawn based on current debt state.
        /// - Adds the hediff if the pawn has debt and the hediff is missing.
        /// - Removes the hediff if debt is zero and the pawn is alive.
        /// - Calls SyncSeverityFromDebt() to keep severity in lockstep with the debt ratio.
        /// </summary>
        private void SyncMetabolicRecoveryHediff(Pawn pawn, IFoodDebtSystem foodDebtSystem)
        {
            if (pawn?.health?.hediffSet == null)
                return;

            if (EternalDefOf.Eternal_MetabolicRecovery == null)
                return;

            float debt = foodDebtSystem.GetDebt(pawn);
            var hediff = pawn.health.hediffSet.GetFirstHediffOfDef(EternalDefOf.Eternal_MetabolicRecovery);

            if (debt <= 0f)
            {
                // Debt cleared — remove hediff from living pawns (dead pawns keep it until resurrection)
                if (hediff != null && !pawn.Dead)
                {
                    pawn.health.RemoveHediff(hediff);
                }
                return;
            }

            // Debt present — ensure hediff exists
            if (hediff == null)
            {
                hediff = HediffMaker.MakeHediff(EternalDefOf.Eternal_MetabolicRecovery, pawn);
                pawn.health.AddHediff(hediff);
            }

            // Sync severity from debt ratio so hediff stage (hunger rate factor) is correct
            if (hediff is MetabolicRecovery_Hediff metabolicHediff)
            {
                metabolicHediff.SyncSeverityFromDebt();
            }
        }

        /// <summary>
        /// Sweeps stale healing history entries on the periodic interval (SAFE-07).
        /// Builds live pawn ID set and active corpse ID set, then delegates to HediffHealer.
        /// Pitfall 5 guard: corpse IDs prevent removing entries for dead pawns mid-resurrection.
        /// </summary>
        private void SweepStaleHealingHistory()
        {
            var healer = _healingProcessor?.HediffHealer;
            if (healer == null)
                return;

            // Build set of all currently spawned pawn IDs across all maps
            var liveIds = new HashSet<int>();
            if (Find.Maps != null)
            {
                foreach (var map in Find.Maps)
                {
                    if (map?.mapPawns == null)
                        continue;
                    foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                    {
                        liveIds.Add(pawn.thingIDNumber);
                    }
                }
            }

            // Build set of pawn IDs for active corpse entries (Pitfall 5: never sweep these)
            var corpseIds = new HashSet<int>();
            var corpseManager = EternalServiceContainer.Instance?.CorpseManager;
            if (corpseManager != null)
            {
                foreach (var entry in corpseManager.GetAllCorpses())
                {
                    if (entry?.OriginalPawn != null)
                    {
                        corpseIds.Add(entry.OriginalPawn.thingIDNumber);
                    }
                }
            }

            healer.SweepStaleEntries(liveIds, corpseIds);
        }

        #endregion

        #region Serialization

        /// <summary>
        /// Serializes and deserializes the timing state.
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref lastNormalHealTick, "lastNormalHealTick", 0);
            Scribe_Values.Look(ref lastRareHealTick, "lastRareHealTick", 0);
            Scribe_Values.Look(ref lastTraitCheckTick, "lastTraitCheckTick", 0);
            Scribe_Values.Look(ref lastCorpseCheckTick, "lastCorpseCheckTick", 0);
            Scribe_Values.Look(ref lastMapCheckTick, "lastMapCheckTick", 0);
            Scribe_Values.Look(ref lastHealingSweepTick, "lastHealingSweepTick", 0);
        }

        /// <summary>
        /// Sets timing values from legacy save data for backwards compatibility.
        /// </summary>
        public void SetTimingValues(
            int normalHealTick,
            int rareHealTick,
            int traitCheckTick,
            int corpseCheckTick,
            int mapCheckTick,
            int healingSweepTick)
        {
            lastNormalHealTick = normalHealTick;
            lastRareHealTick = rareHealTick;
            lastTraitCheckTick = traitCheckTick;
            lastCorpseCheckTick = corpseCheckTick;
            lastMapCheckTick = mapCheckTick;
            lastHealingSweepTick = healingSweepTick;
        }

        /// <summary>
        /// Gets the current timing values for serialization.
        /// </summary>
        public (int normalHeal, int rareHeal, int traitCheck, int corpseCheck, int mapCheck, int healingSweep) GetTimingValues()
        {
            return (lastNormalHealTick, lastRareHealTick, lastTraitCheckTick, lastCorpseCheckTick, lastMapCheckTick, lastHealingSweepTick);
        }

        #endregion
    }
}
