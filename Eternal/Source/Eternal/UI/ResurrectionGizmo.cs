// Relative Path: Eternal/Source/Eternal/UI/ResurrectionGizmo.cs
// Creation Date: 29-10-2025
// Last Edit: 24-02-2026
// Author: 0Shard
// Description: Enhanced ResurrectionGizmo for corpse-based Eternal resurrection system with cost calculation and progress tracking.
//              Fixed: Now properly assigns base Command class fields (icon, disabled, hotKey, Order) for RimWorld UI rendering.
//              Note: Fallback registration here is LAST RESORT - Notify_PawnDied (PRIMARY) and Harmony patch (FALLBACK) should have registered already.
//              Uses EternalHealingPriority.FormatResurrectionTime() for accurate, human-friendly time display.
//              Fixed: ResurrectImmediately now delegates to EternalCorpseHealingProcessor.StartCorpseHealing instead of calling
//              ResurrectionUtility.TryResurrect directly — the direct call crashed with WorkTab's SetPriority transpiler
//              (IndexOutOfRangeException in DefMap) and also failed to preserve Eternal_Essence via the HediffSet swap.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;
using Eternal;
using Eternal.Corpse;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Extensions;
using Eternal.Healing;
using Eternal.Models;
using Eternal.Utils;

// Type alias for backwards compatibility
using EternalCorpseData = Eternal.Models.CorpseTrackingEntry;

namespace Eternal.UI
{
    /// <summary>
    /// Enhanced ResurrectionGizmo for corpse-based Eternal resurrection system.
    /// Calculates costs, manages healing queue, and provides detailed resurrection information.
    /// </summary>
    public class ResurrectionGizmo : Command
    {
        private readonly Pawn originalPawn;
        private readonly Verse.Corpse corpse;
        private EternalCorpseData corpseData;
        private EternalResurrectionCalculator resurrectionCalculator;

        private float totalHealingCost;
        private float estimatedTimeTicks;
        private int totalHealingItems;
        private bool canResurrect;
        private bool isAlreadyResurrecting;

        /// <summary>
        /// Initializes a new instance of ResurrectionGizmo class.
        /// </summary>
        /// <param name="pawn">The original dead Eternal pawn.</param>
        /// <param name="corpse">The corpse object.</param>
        public ResurrectionGizmo(Pawn pawn, Verse.Corpse corpse = null)
        {
            originalPawn = pawn;
            this.corpse = corpse ?? pawn.Corpse;
            resurrectionCalculator = new EternalResurrectionCalculator();

            // Assign base class fields for proper RimWorld gizmo rendering
            this.defaultLabel = "Resurrect Eternal";
            this.defaultDesc = "Begin the Eternal resurrection process.";
            this.hotKey = KeyBindingDefOf.Misc1;
            this.Order = 1000f;

            // Load icon with fallback (false = don't throw if missing)
            this.icon = ContentFinder<Texture2D>.Get("UI/Gizmos/Eternal_Resurrection", false);

            CalculateRequirements();
        }

        /// <summary>
        /// Calculates resurrection requirements and availability.
        /// </summary>
        private void CalculateRequirements()
        {
            try
            {
                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] ResurrectionGizmo.CalculateRequirements() for {originalPawn?.Name?.ToStringShort ?? "null"}");
                    Log.Message($"[Eternal]   - CorpseManager null: {EternalServiceContainer.Instance.CorpseManager == null}");
                }

                var corpseManager = EternalServiceContainer.Instance.CorpseManager;

                // Check if CorpseManager is available
                if (corpseManager == null)
                {
                    canResurrect = false;
                    this.disabled = true;
                    this.disabledReason = "Resurrection system not initialized";
                    Log.Error("[Eternal] CorpseManager is null in ResurrectionGizmo - service container may not be initialized");
                    return;
                }

                // Get corpse data
                corpseData = corpseManager.GetCorpseData(originalPawn);

                // LAST RESORT: Attempt registration if corpse not found (both PRIMARY and FALLBACK paths failed)
                if (corpseData == null && originalPawn != null && corpse != null)
                {
                    // This should rarely happen - indicates both Notify_PawnDied and Harmony patch failed
                    Log.Warning($"[Eternal] Corpse for {originalPawn.Name} not registered - attempting LAST RESORT registration in gizmo (both PRIMARY and FALLBACK paths failed)");

                    // Validate pawn has Eternal trait before attempting registration (ignoring gene suppression)
                    if (originalPawn.HasTraitIgnoringSuppression(EternalDefOf.Eternal_GeneticMarker))
                    {
                        try
                        {
                            var snapshot = PawnAssignmentSnapshot.CaptureFrom(originalPawn);
                            corpseManager.RegisterCorpse(corpse, originalPawn, snapshot);
                            corpseData = corpseManager.GetCorpseData(originalPawn);

                            if (corpseData != null)
                            {
                                Log.Warning($"[Eternal] LAST RESORT registration successful for {originalPawn.Name} in ResurrectionGizmo - investigate why PRIMARY and FALLBACK paths failed");
                            }
                        }
                        catch (Exception regEx)
                        {
                            EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                                "LAST RESORT registration", originalPawn, regEx);
                        }
                    }
                }

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal]   - CorpseData null: {corpseData == null}");
                    if (corpseData != null)
                    {
                        Log.Message($"[Eternal]   - IsHealingActive: {corpseData.IsHealingActive}");
                        Log.Message($"[Eternal]   - FoodDebt: {corpseData.FoodDebt:F2}");
                    }
                }

                if (corpseData == null)
                {
                    canResurrect = false;
                    this.disabled = true;
                    this.disabledReason = "Corpse not registered for resurrection";

                    if (Eternal_Mod.settings?.debugMode == true)
                    {
                        Log.Warning($"[Eternal]   - Gizmo DISABLED: Corpse not registered even after fallback attempt");
                    }
                    return;
                }

                // Check if already resurrecting
                isAlreadyResurrecting = corpseData.IsHealingActive;
                if (isAlreadyResurrecting)
                {
                    canResurrect = false;
                    this.disabled = true;
                    this.disabledReason = "Resurrection already in progress";
                    return;
                }

                // Calculate healing requirements
                var healingItems = resurrectionCalculator.CalculateHealingQueue(originalPawn);
                totalHealingItems = healingItems.Count;
                totalHealingCost = resurrectionCalculator.CalculateTotalCost(healingItems);
                estimatedTimeTicks = resurrectionCalculator.CalculateEstimatedTimeTicks(healingItems);

                // Check if within debt capacity limits
                canResurrect = CanAccumulateDebt(totalHealingCost);

                // Set disabled state for base Command class (RimWorld reads this field)
                this.disabled = !canResurrect || isAlreadyResurrecting;
                if (this.disabled)
                {
                    this.disabledReason = isAlreadyResurrecting
                        ? "Resurrection already in progress"
                        : GetCannotResurrectReason();
                }

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal]   - Final state: canResurrect={canResurrect}, disabled={this.disabled}");
                    if (this.disabled)
                    {
                        Log.Message($"[Eternal]   - Disabled reason: {this.disabledReason}");
                    }
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "CalculateRequirements", originalPawn, ex);
                canResurrect = false;
                this.disabled = true;
                this.disabledReason = "Error calculating resurrection requirements";
            }
        }

        /// <summary>
        /// Checks if the pawn can accumulate the required debt.
        /// Uses body size fallback for dead pawns (whose needs.food is null).
        /// </summary>
        /// <param name="debtAmount">The debt amount to check.</param>
        /// <returns>True if debt can be accumulated, false otherwise.</returns>
        private bool CanAccumulateDebt(float debtAmount)
        {
            float maxDebt = GetMaxDebtCapacity();
            if (maxDebt <= 0f)
                return false;

            float currentDebt = corpseData?.FoodDebt ?? 0f;

            return (currentDebt + debtAmount) <= maxDebt;
        }

        /// <summary>
        /// Gets the gizmo label.
        /// </summary>
        public override string Label
        {
            get
            {
                if (isAlreadyResurrecting)
                {
                    return "Resurrecting...";
                }
                return "Resurrect Eternal";
            }
        }

        /// <summary>
        /// Gets the gizmo description with detailed information.
        /// </summary>
        public override string Desc
        {
            get
            {
                try
                {
                    if (corpseData == null)
                    {
                        return "Error: Corpse data not found.";
                    }

                    if (isAlreadyResurrecting)
                    {
                        return $"Resurrection in progress for {originalPawn.Name?.ToStringFull ?? "Unknown"}.\n\n" +
                               $"Progress: {corpseData.HealingProgress:P0}\n" +
                               $"Items remaining: {corpseData.HealingQueue?.Count ?? 0}\n" +
                               $"Current debt: {corpseData.FoodDebt:F1} nutrition";
                    }

                    if (!canResurrect)
                    {
                        return $"Cannot resurrect {originalPawn.Name?.ToStringFull ?? "Unknown"} at this time.\n\n" +
                               $"Reason: {GetCannotResurrectReason()}";
                    }

                    return $"Resurrect {originalPawn.Name?.ToStringFull ?? "Unknown"} using Eternal regrowth.\n\n" +
                           $"Healing items: {totalHealingItems}\n" +
                           $"Total energy cost: {totalHealingCost:F1} nutrition\n" +
                           $"Estimated time: {EternalHealingPriority.FormatResurrectionTime(estimatedTimeTicks)}\n" +
                           $"Current debt: {corpseData.FoodDebt:F1} nutrition\n" +
                           $"Max debt capacity: {GetMaxDebtCapacity():F1} nutrition";
                }
                catch (Exception ex)
                {
                    EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                        "ResurrectionGizmo.Desc", originalPawn, ex);
                    return "Error retrieving resurrection information.";
                }
            }
        }

        /// <summary>
        /// Gets the reason why resurrection cannot be performed.
        /// </summary>
        /// <returns>Reason string.</returns>
        private string GetCannotResurrectReason()
        {
            if (!CanAccumulateDebt(totalHealingCost))
            {
                float maxDebt = GetMaxDebtCapacity();
                float currentDebt = corpseData?.FoodDebt ?? 0f;
                return $"Insufficient debt capacity. Required: {totalHealingCost:F1}, Available: {(maxDebt - currentDebt):F1}";
            }

            return "Unknown reason";
        }

        /// <summary>
        /// Gets the maximum debt capacity for the pawn.
        /// Uses body size fallback for dead pawns (whose needs.food is null).
        /// Matches logic in UnifiedFoodDebtManager.GetMaxCapacity().
        /// </summary>
        /// <returns>Maximum debt capacity.</returns>
        private float GetMaxDebtCapacity()
        {
            float maxDebtMultiplier = Eternal_Mod.GetSettings().maxDebtMultiplier;

            // For dead pawns, needs.food is typically null - use body size fallback
            if (originalPawn?.needs?.food != null)
            {
                return originalPawn.needs.food.MaxLevel * maxDebtMultiplier;
            }

            // Fallback for dead pawns: use body size (matching UnifiedFoodDebtManager.GetMaxCapacity())
            float bodySize = originalPawn?.BodySize ?? 1.0f;
            return 1.0f * bodySize * maxDebtMultiplier;
        }

        /// <summary>
        /// Gets whether the gizmo is visible.
        /// </summary>
        public override bool Visible
        {
            get
            {
                // SAFE-09: hide gizmo entirely when mod is disabled due to missing critical defs.
                if (EternalModState.IsDisabled)
                    return false;

                return originalPawn != null &&
                       originalPawn.Dead &&
                       originalPawn.HasTraitIgnoringSuppression(EternalDefOf.Eternal_GeneticMarker) &&
                       corpse != null;
            }
        }

        /// <summary>
        /// Processes the gizmo action.
        /// </summary>
        public override void ProcessInput(Event ev)
        {
            // SAFE-09: do not process input when mod is disabled.
            if (EternalModState.IsDisabled)
                return;

            if (!canResurrect || isAlreadyResurrecting)
                return;

            // Show confirmation dialog with detailed information
            string dialogText = $"Are you sure you want to resurrect {originalPawn.Name?.ToStringFull ?? "Unknown"}?\n\n" +
                               $"This will begin the healing process on the corpse and accumulate {totalHealingCost:F1} nutrition debt.\n" +
                               $"Estimated time: {EternalHealingPriority.FormatResurrectionTime(estimatedTimeTicks)}\n" +
                               $"The pawn will resurrect once all injuries and missing parts are healed.\n\n" +
                               $"Note: While dead, the pawn will accumulate debt. Once resurrected, " +
                               $"100% of food consumption will go to debt repayment until the debt is cleared.";

            DiaNode node = new DiaNode("Resurrect Eternal");
            node.text = dialogText;

            DiaOption yesOption = new DiaOption("Begin Resurrection");
            yesOption.resolveTree = true;
            yesOption.action = () =>
            {
                StartResurrection();
            };

            DiaOption noOption = new DiaOption("Cancel");
            noOption.resolveTree = false;

            node.options.Add(yesOption);
            node.options.Add(noOption);

            Find.WindowStack.Add(new Dialog_NodeTree(node));
        }

        /// <summary>
        /// Starts the resurrection process by initiating healing on the corpse.
        /// </summary>
        private void StartResurrection()
        {
            try
            {
                if (corpseData == null)
                {
                    Log.Error("[Eternal] Cannot start resurrection - corpse data is null");
                    return;
                }

                // Calculate healing queue
                var healingQueue = resurrectionCalculator.CalculateHealingQueue(originalPawn);
                if (healingQueue.Count == 0)
                {
                    // Nothing to heal - resurrect immediately
                    ResurrectImmediately();
                    return;
                }

                // Initialize healing process
                corpseData.IsHealingActive = true;
                corpseData.HealingQueue = healingQueue;
                corpseData.HealingStartTick = Find.TickManager.TicksGame;
                corpseData.TotalHealingCost = totalHealingCost;
                corpseData.IsRegisteredForResurrection = true;

                // Add initial debt
                // Debt will accumulate as healing progresses

                Log.Message($"[Eternal] Started resurrection process for {originalPawn.Name} - {healingQueue.Count} healing items, {totalHealingCost:F1} total cost");

                // Show notification
                Messages.Message(
                    new Message($"{originalPawn.Name?.ToStringFull ?? "Unknown"} has begun Eternal resurrection. " +
                               $"Estimated time: {EternalHealingPriority.FormatResurrectionTime(estimatedTimeTicks)}.",
                               MessageTypeDefOf.PositiveEvent));

                // Refresh gizmo state
                CalculateRequirements();
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "StartResurrection", originalPawn, ex);
                Messages.Message(
                    new Message("Failed to start resurrection process. See log for details.",
                               MessageTypeDefOf.NegativeEvent));
            }
        }

        /// <summary>
        /// Resurrects a pawn immediately when no healing is needed.
        /// Delegates to EternalCorpseHealingProcessor.StartCorpseHealing which correctly handles
        /// the HediffSet swap pattern, WorkTab compatibility, and debt tracking.
        /// Never calls ResurrectionUtility.TryResurrect directly — that path crashes with WorkTab.
        /// </summary>
        private void ResurrectImmediately()
        {
            try
            {
                if (corpse == null || originalPawn == null)
                {
                    Log.Error("[Eternal] Cannot resurrect immediately - corpse or pawn is null");
                    return;
                }

                var healingProcessor = EternalServiceContainer.Instance?.CorpseHealingProcessor;
                if (healingProcessor == null)
                {
                    Log.Error("[Eternal] ResurrectImmediately: CorpseHealingProcessor not available");
                    return;
                }

                // Re-fetch corpse data (may have changed since gizmo was constructed)
                var freshCorpseData = EternalServiceContainer.Instance.CorpseManager?.GetCorpseData(originalPawn);
                if (freshCorpseData == null)
                {
                    Log.Error($"[Eternal] ResurrectImmediately: No corpse data for {originalPawn.Name}");
                    return;
                }

                // Delegate to the processor's StartCorpseHealing — it handles the hediff swap,
                // WorkTab compatibility, caravan detection, debt transfer, and assignment snapshot restore.
                // When HealingQueue is empty, StartCorpseHealing calls its own ResurrectImmediately(corpseData).
                healingProcessor.StartCorpseHealing(freshCorpseData);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "ResurrectImmediately", originalPawn, ex);
            }
        }
    }
}