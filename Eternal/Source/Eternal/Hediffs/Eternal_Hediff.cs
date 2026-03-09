// Relative Path: Eternal/Source/Eternal/Hediffs/Eternal_Hediff.cs
// Creation Date: 28-10-2025
// Last Edit: 09-03-2026
// Author: 0Shard
// Description: Core hediff class for Eternal mod, manages Eternal Essence hediff with enhanced validation, error handling, and healing system integration.
//              Added GetGizmos() override to show resurrection gizmo on corpses using RimWorld's native showGizmosOnCorpse mechanism.
//              Added Notify_PawnDied() override as PRIMARY corpse registration path (matches Immortals mod pattern for reliability).
//              Added caravan death handling - delegates to EternalCaravanDeathHandler when pawn dies in caravan.
//              BUGFIX: Pre-calculates healing queue at death time before RimWorld removes injuries from dead pawns.
//              05-02: Reactive healing history cleanup — calls HediffHealer.ClearPawnHealingProgress on essence removal (SAFE-07).
//              09-03: Removed debt display from SeverityLabel and GetHealingStatus — debt is now shown on Metabolic Recovery hediff only.

using System;
using System.Collections.Generic;
using Verse;
using Eternal.Caravan;
using Eternal.DI;
using Eternal.Extensions;
using Eternal.Healing;
using Eternal.Utils;
using Eternal.Exceptions;
using Eternal.Models;
using Eternal.Resources;
using Eternal.UI;

namespace Eternal
{
    /// <summary>
    /// Core hediff class for Eternal mod.
    /// Manages Eternal Essence hediff that enables immortality mechanics.
    /// Enhanced with comprehensive validation and error handling.
    /// </summary>
    public class Eternal_Hediff : HediffWithComps
    {
        /// <summary>
        /// Called when hediff is added to a pawn.
        /// </summary>
        /// <param name="dinfo">The damage info associated with hediff addition.</param>
        /// <exception cref="EternalValidationException">Thrown when pawn validation fails.</exception>
        /// <exception cref="EternalException">Thrown when operation fails unexpectedly.</exception>
        public override void PostAdd(DamageInfo? dinfo)
        {
            const string operation = "PostAdd";

            try
            {
                // Validate pawn state before proceeding
                if (pawn != null)
                {
                    EternalValidator.ValidatePawn(pawn, "pawn", operation);
                    EternalValidator.ValidateEternalPawn(pawn, "pawn", operation);
                }
                else
                {
                    throw new EternalValidationException(
                        "Pawn cannot be null when adding Eternal Essence hediff",
                        "pawn",
                        operation);
                }

                base.PostAdd(dinfo);

                // Integrate with healing system - register pawn for healing tracking
                var healingProcessor = Eternal_Component.Instance?.HealingProcessor;
                if (healingProcessor != null && pawn.Dead)
                {
                    // Register dead pawn for food debt tracking
                    healingProcessor.FoodDebtSystem.RegisterPawn(pawn);
                }

                // Log the addition of Eternal Essence with enhanced context
                EternalLogger.Info($"Eternal Essence added to {pawn.Name?.ToStringFull ?? "Unknown"}", operation, pawn);
            }
            catch (EternalValidationException ex)
            {
                EternalLogger.LogException(ex);
                throw ex; // Re-throw validation exceptions
            }
            catch (Exception ex)
            {
                EternalLogger.LogException(ex, operation, pawn);
                throw new EternalException("Failed to add Eternal Essence hediff", operation, ex) { Pawn = pawn };
            }
        }
         
        /// <summary>
        /// Called when hediff is removed from a pawn.
        /// </summary>
        /// <exception cref="EternalValidationException">Thrown when pawn validation fails.</exception>
        /// <exception cref="EternalException">Thrown when operation fails unexpectedly.</exception>
        public override void PostRemoved()
        {
            const string operation = "PostRemoved";

            try
            {
                // Validate pawn state before proceeding
                if (pawn != null)
                {
                    EternalValidator.ValidatePawn(pawn, "pawn", operation);
                }
                else
                {
                    throw new EternalValidationException(
                        "Pawn cannot be null when removing Eternal Essence hediff",
                        "pawn",
                        operation);
                }

                base.PostRemoved();

                // Integrate with healing system - cleanup pawn from healing tracking
                var healingProcessor = Eternal_Component.Instance?.HealingProcessor;
                if (healingProcessor != null)
                {
                    // Unregister pawn from all healing systems
                    healingProcessor.FoodDebtSystem.UnregisterPawn(pawn);
                    healingProcessor.ScarHealing.ClearPawnHealingRecords(pawn);
                    // Reactive cleanup: clear healing history when Eternal_Essence is removed
                    healingProcessor.HediffHealer?.ClearPawnHealingProgress(pawn);
                }

                // Log the removal of Eternal Essence with enhanced context
                EternalLogger.Info($"Eternal Essence removed from {pawn.Name?.ToStringFull ?? "Unknown"}", operation, pawn);
            }
            catch (EternalValidationException ex)
            {
                EternalLogger.LogException(ex);
                throw ex; // Re-throw validation exceptions
            }
            catch (Exception ex)
            {
                EternalLogger.LogException(ex, operation, pawn);
                throw new EternalException("Failed to remove Eternal Essence hediff", operation, ex) { Pawn = pawn };
            }
        }

        /// <summary>
        /// Called by RimWorld when pawn dies. This is the PRIMARY registration path for corpse tracking.
        /// More reliable than Harmony patches because it's called directly by RimWorld's death logic.
        /// Matches the pattern used by Immortals mod for guaranteed registration.
        /// </summary>
        /// <param name="dinfo">Optional damage info that caused death</param>
        /// <param name="culprit">Optional hediff that caused death</param>
        public override void Notify_PawnDied(DamageInfo? dinfo, Hediff culprit = null)
        {
            const string operation = "Notify_PawnDied";

            try
            {
                // Call base first
                base.Notify_PawnDied(dinfo, culprit);

                // Validate pawn
                if (pawn == null || !pawn.Dead)
                {
                    EternalLogger.Warning("Notify_PawnDied called but pawn is null or not dead", operation);
                    return;
                }

                // Validate corpse exists
                if (pawn.Corpse == null)
                {
                    EternalLogger.Warning($"Notify_PawnDied: No corpse found for {pawn.Name}", operation, pawn);
                    return;
                }

                // Check for caravan death - handler takes over if pawn died in caravan
                if (EternalCaravanDeathHandler.IsPawnInCaravan(pawn))
                {
                    var caravanHandler = EternalCaravanDeathHandler.Instance;
                    if (caravanHandler != null)
                    {
                        if (Eternal_Mod.settings?.debugMode == true)
                        {
                            Log.Message($"[Eternal] Pawn {pawn.Name} died in caravan - delegating to caravan death handler");
                        }
                        caravanHandler.RegisterDeath(pawn, pawn.Corpse);
                        return; // Caravan handler takes over
                    }
                }

                // Get corpse manager
                var corpseManager = EternalServiceContainer.Instance.CorpseManager;
                if (corpseManager == null)
                {
                    EternalLogger.Error($"CorpseManager is null in Notify_PawnDied for {pawn.Name} - cannot register corpse", operation, pawn);
                    return;
                }

                // Calculate healing queue NOW, before RimWorld removes injuries from dead pawn
                // This is critical because RimWorld may remove/modify hediffs on dead pawns over time
                List<HealingItem> preCalculatedQueue = null;
                try
                {
                    var resurrectionCalculator = new EternalResurrectionCalculator();
                    preCalculatedQueue = resurrectionCalculator.CalculateHealingQueue(pawn);

                    if (Eternal_Mod.settings?.debugMode == true)
                    {
                        Log.Message($"[Eternal] Pre-calculated healing queue at death for {pawn.Name}: {preCalculatedQueue?.Count ?? 0} items");
                    }
                }
                catch (Exception ex)
                {
                    EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                        "CalculateHealingQueue", pawn, ex);
                }

                // Register the corpse - this is the PRIMARY registration path (no IsTracked check - always register)
                // If already registered, CorpseManager will handle idempotency
                // Capture assignment snapshot for work priority preservation
                var assignmentSnapshot = PawnAssignmentSnapshot.CaptureFrom(pawn);
                corpseManager.RegisterCorpse(pawn.Corpse, pawn, assignmentSnapshot, preCalculatedQueue);

                EternalLogger.Info($"Registered corpse for {pawn.Name} via Notify_PawnDied (PRIMARY hediff callback)", operation, pawn);
            }
            catch (Exception ex)
            {
                EternalLogger.LogException(ex, operation, pawn);
                // Don't re-throw - we don't want to break the death process
            }
        }

        /// <summary>
        /// Called every tick to update hediff state.
        /// </summary>
        /// <exception cref="EternalValidationException">Thrown when pawn validation fails.</exception>
        /// <exception cref="EternalException">Thrown when operation fails unexpectedly.</exception>
        public override void Tick()
        {
            const string operation = "Tick";
            
            try
            {
                // Validate pawn state before proceeding
                if (pawn != null)
                {
                    EternalValidator.ValidatePawn(pawn, "pawn", operation);
                }
                else
                {
                    // Skip processing for null pawn but log warning
                    EternalLogger.Warning("Tick called with null pawn, skipping processing", operation);
                    return;
                }
                
                // Ensure pawn still has Eternal trait with safe navigation
                // Use HasTraitIgnoringSuppression to handle Biotech gene trait suppression
                if (pawn.story?.traits != null)
                {
                    if (!pawn.HasTraitIgnoringSuppression(EternalDefOf.Eternal_GeneticMarker))
                    {
                        // Remove hediff if trait is no longer present
                        EternalLogger.Debug($"Removing Eternal Essence from {pawn.Name} - trait no longer present", operation, pawn);
                        
                        if (pawn.health?.hediffSet != null)
                        {
                            pawn.health.RemoveHediff(this);
                        }
                        else
                        {
                            EternalLogger.Warning($"Cannot remove Eternal Essence from {pawn.Name} - hediff set is null", operation, pawn);
                        }
                    }
                    else
                    {
                        EternalLogger.Debug($"Eternal trait confirmed for {pawn.Name}", operation, pawn);
                    }
                }
                else
                {
                    EternalLogger.Warning($"Pawn {pawn.Name} has null story traits", operation, pawn);
                }
            }
            catch (EternalValidationException ex)
            {
                EternalLogger.LogException(ex);
                // Don't re-throw validation exceptions in Tick to avoid breaking game loop
            }
            catch (Exception ex)
            {
                EternalLogger.LogException(ex, operation, pawn);
                // Don't re-throw exceptions in Tick to avoid breaking game loop
            }
        }
         
        /// <summary>
        /// Gets the severity label for the hediff with healing system information.
        /// </summary>
        /// <exception cref="EternalException">Thrown when settings access fails.</exception>
        public override string SeverityLabel
        {
            get
            {
                const string operation = "GetSeverityLabel";

                try
                {
                    // Validate settings access with fallback
                    if (Eternal_Mod.settings != null)
                    {
                        // Validate settings values
                        EternalValidator.ValidateRange(Eternal_Mod.settings.showRegrowthProgress ? 1f : 0f, 0f, 1f, "showRegrowthProgress", operation);

                        // Display immortality power level based on settings.
                        // Debt display is intentionally removed — food debt is shown on
                        // the Metabolic Recovery hediff (single source of truth, 09-03).
                        if (Eternal_Mod.settings.showRegrowthProgress)
                        {
                            return $"Eternal Power: {Severity:F1}";
                        }
                    }
                    else
                    {
                        EternalLogger.Warning("Settings is null, using default severity label", operation);
                    }

                    return base.SeverityLabel;
                }
                catch (Exception ex)
                {
                    EternalLogger.LogException(ex, operation);
                    return base.SeverityLabel; // Fallback to default behavior
                }
            }
        }

        /// <summary>
        /// Gets healing status information for this pawn.
        /// </summary>
        /// <returns>Dictionary containing healing status information</returns>
        public Dictionary<string, object> GetHealingStatus()
        {
            const string operation = "GetHealingStatus";
            var status = new Dictionary<string, object>();

            try
            {
                if (pawn == null)
                {
                    status["Error"] = "Pawn is null";
                    return status;
                }

                var healingProcessor = Eternal_Component.Instance?.HealingProcessor;
                if (healingProcessor != null)
                {
                    var pawnStatus = healingProcessor.GetPawnHealingStatus(pawn);
                    status["Healing Status"] = pawnStatus;
                }
                else
                {
                    status["Error"] = "Healing processor not available";
                }

                status["Eternal Hediff Severity"] = Severity;
                status["Has Eternal Trait"] = pawn.HasTraitIgnoringSuppression(EternalDefOf.Eternal_GeneticMarker);

                // Food debt display removed from Eternal_Essence status (09-03).
                // Debt is now shown on the Metabolic Recovery hediff (single source of truth).
            }
            catch (Exception ex)
            {
                EternalLogger.LogException(ex, operation, pawn);
                status["Error"] = ex.Message;
            }

            return status;
        }

        /// <summary>
        /// Gets gizmos for this hediff. Shows resurrection gizmo when pawn is dead.
        /// Uses RimWorld's native showGizmosOnCorpse mechanism.
        /// </summary>
        /// <returns>Collection of gizmos for this hediff.</returns>
        public override IEnumerable<Gizmo> GetGizmos()
        {
            const string operation = "GetGizmos";

            // Yield all base gizmos first (with null check - base can return null)
            var baseGizmos = base.GetGizmos();
            if (baseGizmos != null)
            {
                foreach (var gizmo in baseGizmos)
                {
                    yield return gizmo;
                }
            }

            // Only show resurrection gizmo when pawn is dead
            if (pawn != null && pawn.Dead && pawn.Corpse != null)
            {
                // Check if corpse is registered before creating gizmo
                var corpseManager = EternalServiceContainer.Instance.CorpseManager;
                bool isRegistered = corpseManager?.IsTracked(pawn) == true;

                // Attempt defensive registration if not registered (edge case - PRIMARY should have registered)
                if (!isRegistered && corpseManager != null)
                {
                    EternalLogger.Debug($"Corpse for {pawn.Name} not registered in GetGizmos - attempting defensive registration (edge case)", operation, pawn);
                    try
                    {
                        var snapshot = PawnAssignmentSnapshot.CaptureFrom(pawn);
                        corpseManager.RegisterCorpse(pawn.Corpse, pawn, snapshot);
                        isRegistered = corpseManager.IsTracked(pawn);

                        if (isRegistered)
                        {
                            EternalLogger.Info($"Defensive registration successful for {pawn.Name}", operation, pawn);
                        }
                    }
                    catch (Exception regEx)
                    {
                        EternalLogger.LogException(regEx, "DefensiveRegistration", pawn);
                    }
                }

                // Only create gizmo if corpse is registered (or registration fallback succeeded)
                if (isRegistered)
                {
                    ResurrectionGizmo resurrectionGizmo = null;
                    try
                    {
                        resurrectionGizmo = new ResurrectionGizmo(pawn, pawn.Corpse);
                        EternalLogger.Debug($"Created ResurrectionGizmo for dead pawn {pawn.Name}", operation, pawn);
                    }
                    catch (Exception ex)
                    {
                        EternalLogger.LogException(ex, operation, pawn);
                    }

                    if (resurrectionGizmo != null)
                    {
                        yield return resurrectionGizmo;
                    }
                }
                else if (corpseManager == null)
                {
                    EternalLogger.Error($"Cannot show resurrection gizmo - CorpseManager is null for {pawn.Name}", operation, pawn);
                }
            }
        }

    }
}