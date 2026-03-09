// file path: Eternal/Source/Eternal/Corpse/EternalCorpseComponent.cs
// Author Name: 0Shard
// Date Created: 09-11-2025
// Date Last Modified: 20-02-2026
// Description: Component attached to Eternal corpses that manages corpse-specific mechanics and state.
//              Enhanced to block rot and dessication every tick with cached CompRottable reference.
//              Implements IRoofCollapseAlert to protect corpses from mountain roof collapse.

using System;
using System.Linq;
using Verse;
using RimWorld;
using MapType = Verse.Map;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Utils;
using Eternal.Models;
using Eternal.Patches;

// Type alias for backwards compatibility
using EternalCorpseData = Eternal.Models.CorpseTrackingEntry;

namespace Eternal.Corpse
{
    /// <summary>
    /// Component attached to Eternal corpses that handles corpse-specific mechanics,
    /// including decay prevention, healing progress tracking, resurrection state management,
    /// and roof collapse protection via IRoofCollapseAlert interface.
    /// </summary>
    public class EternalCorpseComponent : ThingComp, IRoofCollapseAlert
    {
        private EternalCorpseData corpseData;
        private bool isInitialized = false;
        private int lastProgressUpdateTick = 0;

        // PERF: Cached CompRottable reference to avoid GetComp() lookup every tick
        private CompRottable _cachedRottable;
        private bool _rottableCached = false;

        /// <summary>
        /// The corpse data associated with this component.
        /// </summary>
        public EternalCorpseData CorpseData
        {
            get => corpseData;
            set
            {
                corpseData = value;
                if (corpseData != null && !isInitialized)
                {
                    Initialize();
                }
            }
        }

        /// <summary>
        /// Gets the cached CompRottable component, caching it on first access.
        /// </summary>
        private CompRottable CachedRottable
        {
            get
            {
                if (!_rottableCached && parent is Verse.Corpse corpse)
                {
                    _cachedRottable = corpse.GetComp<CompRottable>();
                    _rottableCached = true;
                }
                return _cachedRottable;
            }
        }

        /// <summary>
        /// Called when the component is initialized.
        /// </summary>
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            try
            {
                if (corpseData == null)
                {
                    // Try to load existing data from save
                    LoadCorpseData();
                }

                if (corpseData != null && !isInitialized)
                {
                    Initialize();
                }

                // Always reset rot on spawn/load to handle saves from before this fix
                BlockRotAndDessication();
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "PostSpawnSetup", corpseData?.OriginalPawn, ex);
            }
        }

        /// <summary>
        /// Called every game tick to update component state.
        /// CRITICAL: Blocks rot and dessication EVERY tick to prevent any decay accumulation.
        /// </summary>
        public override void CompTick()
        {
            base.CompTick();

            try
            {
                // CRITICAL: Block rot every tick - CompRottable.CompTickRare() runs every 250 ticks
                // and can increment RotProgress. We must reset it every tick to stay ahead.
                BlockRotAndDessication();

                if (corpseData?.IsHealingActive == true)
                {
                    UpdateHealingProgress();
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "CompTick", corpseData?.OriginalPawn, ex);
            }
        }

        /// <summary>
        /// Blocks rot progress and dessication state every tick.
        /// Uses cached CompRottable reference for performance.
        /// </summary>
        private void BlockRotAndDessication()
        {
            var rottable = CachedRottable;
            if (rottable == null)
                return;

            // Reset rot progress to zero if any accumulated
            if (rottable.RotProgress > 0f)
            {
                rottable.RotProgress = 0f;
            }

            // Check rot stage and force back to Fresh if it changed
            // This handles dessication (extreme rot) as well
            if (rottable.Stage != RotStage.Fresh)
            {
                // RotProgress = 0 should reset the stage, but force it by ensuring progress stays at 0
                rottable.RotProgress = 0f;

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Reset rot stage from {rottable.Stage} to Fresh for {corpseData?.OriginalPawn?.Name}");
                }
            }
        }

        /// <summary>
        /// Initializes corpse protection: sets forbidden status, repairs damage, etc.
        /// Rot blocking is handled separately by BlockRotAndDessication() every tick.
        /// </summary>
        private void Initialize()
        {
            try
            {
                if (parent == null || !(parent is Verse.Corpse corpse))
                {
                    return;
                }

                // Initial rot reset (ongoing blocking happens in CompTick)
                BlockRotAndDessication();

                // Handle temperature damage by repairing it
                if (corpse.GetComp<CompTemperatureDamaged>() is CompTemperatureDamaged tempDamage)
                {
                    // Repair any temperature damage immediately
                    if (corpse.HitPoints < corpse.def.BaseMaxHitPoints)
                    {
                        corpse.HitPoints = corpse.def.BaseMaxHitPoints;
                    }
                }

                // Prevent being destroyed by various means
                // Properties destroyAfterSolidifying, canBeBuried, canBeButchered, canBeCarried removed in RimWorld 1.6
                // These are now handled through ThingDef or other systems
                corpse.SetForbidden(true, false); // Forbidden by default

                // Make corpse indestructible
                if (corpse.def.useHitPoints)
                {
                    corpse.HitPoints = corpse.def.BaseMaxHitPoints;
                }

                isInitialized = true;

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Corpse protection initialized for {corpseData?.OriginalPawn?.Name}");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "Initialize", corpseData?.OriginalPawn, ex);
            }
        }

        /// <summary>
        /// Updates healing progress based on current healing state.
        /// </summary>
        private void UpdateHealingProgress()
        {
            try
            {
                if (corpseData?.HealingQueue == null || corpseData.HealingQueue.Count == 0)
                {
                    return;
                }

                int currentTick = Find.TickManager.TicksGame;
                if (currentTick - lastProgressUpdateTick < 250) // Update every 250 ticks
                {
                    return;
                }

                lastProgressUpdateTick = currentTick;

                // Calculate progress based on healing queue completion
                int totalItems = corpseData.HealingQueue.Count;
                int completedItems = corpseData.HealingQueue.Count(item => item.Severity <= 0.01f);

                float previousProgress = corpseData.HealingProgress;
                corpseData.HealingProgress = (float)completedItems / totalItems;

                // Notify about significant progress milestones
                if (corpseData.HealingProgress >= 1.0f && previousProgress < 1.0f)
                {
                    // Healing complete - ready for resurrection
                    OnHealingComplete();
                }
                else if ((corpseData.HealingProgress >= 0.5f && previousProgress < 0.5f) ||
                         (corpseData.HealingProgress >= 0.25f && previousProgress < 0.25f) ||
                         (corpseData.HealingProgress >= 0.75f && previousProgress < 0.75f))
                {
                    // Progress milestones
                    if (Eternal_Mod.settings?.debugMode == true)
                    {
                        Log.Message($"[Eternal] Healing progress for {corpseData.OriginalPawn.Name}: {corpseData.HealingProgress:P0}");
                    }
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "UpdateHealingProgress", corpseData?.OriginalPawn, ex);
            }
        }

        /// <summary>
        /// Called when healing is complete and corpse is ready for resurrection.
        /// </summary>
        private void OnHealingComplete()
        {
            try
            {
                corpseData.IsHealingActive = false;
                corpseData.HealingProgress = 1.0f;

                // Send notification to player
                Find.LetterStack.ReceiveLetter(
                    "EternalResurrectionReady".Translate(),
                    "EternalResurrectionReadyDesc".Translate(corpseData.OriginalPawn.Name.Named("PAWN")),
                    LetterDefOf.PositiveEvent,
                    parent);

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Healing complete for {corpseData.OriginalPawn.Name} - ready for resurrection");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "OnHealingComplete", corpseData?.OriginalPawn, ex);
            }
        }

        /// <summary>
        /// Attempts to load corpse data from save file or recreation.
        /// </summary>
        private void LoadCorpseData()
        {
            try
            {
                if (parent is Verse.Corpse corpse && corpse.InnerPawn != null)
                {
                    // Try to find existing data in corpse manager
                    corpseData = EternalServiceContainer.Instance.CorpseManager?.GetCorpseData(corpse.InnerPawn);

                    if (corpseData == null)
                    {
                        // Create basic data if not found (shouldn't happen in normal operation)
                        corpseData = new CorpseTrackingEntry(corpse, corpse.InnerPawn, corpse.Map, corpse.Position);

                        Log.Warning($"[Eternal] Created new corpse data for {corpse.InnerPawn.Name} - this should not happen normally");
                    }
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "LoadCorpseData", null, ex);
            }
        }

        /// <summary>
        /// Gets the current status description for UI display.
        /// </summary>
        /// <returns>Status description string</returns>
        public string GetStatusDescription()
        {
            try
            {
                if (corpseData == null)
                {
                    return "Error: No corpse data";
                }

                if (corpseData.IsHealingActive)
                {
                    return $"Healing: {corpseData.HealingProgress:P0} complete\n" +
                           $"Debt: {corpseData.FoodDebt:F1} nutrition\n" +
                           $"Items remaining: {corpseData.HealingQueue?.Count ?? 0}";
                }
                else if (corpseData.HealingProgress >= 1.0f)
                {
                    return "Ready for resurrection\n" +
                           $"Debt: {corpseData.FoodDebt:F1} nutrition";
                }
                else
                {
                    return $"Dead\n" +
                           $"Click 'Resurrect' to begin healing\n" +
                           $"Debt: {corpseData.FoodDebt:F1} nutrition";
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "GetStatusDescription", corpseData?.OriginalPawn, ex);
                return "Error retrieving status";
            }
        }

        #region IRoofCollapseAlert Implementation

        /// <summary>
        /// Called by RimWorld BEFORE roof collapse damage is applied.
        /// Returns RemoveThing to prevent destruction and teleports the corpse to safety.
        /// This is the PRIMARY protection mechanism using RimWorld's built-in IRoofCollapseAlert interface.
        /// The Harmony patch in RoofCollapse_Patch.cs serves as a FALLBACK for edge cases
        /// where the comp might not be attached yet.
        /// </summary>
        public RoofCollapseResponse Notify_OnBeforeRoofCollapse()
        {
            try
            {
                // Check if protection is enabled
                if (!Eternal_Mod.GetSettings().enableRoofCollapseProtection)
                    return RoofCollapseResponse.None;

                // Get corpse reference
                if (!(parent is Verse.Corpse corpse) || corpse.Map == null)
                    return RoofCollapseResponse.None;

                // Validate this is a tracked Eternal corpse
                var corpseManager = EternalServiceContainer.Instance?.CorpseManager;
                if (corpseManager == null || !corpseManager.IsTracked(corpse.InnerPawn))
                    return RoofCollapseResponse.None;

                // Find nearest safe cell using shared helper
                IntVec3 safeCell = RoofCollapseHelper.FindNearestSafeCell(corpse.Position, corpse.Map, 50);

                if (!safeCell.IsValid)
                {
                    // No safe cell found - try edge of map as last resort
                    safeCell = CellFinder.RandomEdgeCell(corpse.Map);
                    if (safeCell.Roofed(corpse.Map))
                    {
                        Log.Warning($"[Eternal] No safe cell found for roof collapse protection of {corpse.InnerPawn?.Name}");
                        return RoofCollapseResponse.None;
                    }
                }

                // Teleport corpse immediately (same map, different cell)
                MapType map = corpse.Map;
                corpse.DeSpawn(DestroyMode.WillReplace);
                GenSpawn.Spawn(corpse, safeCell, map);

                // Notify player (subtle message)
                Messages.Message(
                    "EternalRoofCollapseSaved".Translate(corpse.InnerPawn?.Name?.ToStringShort ?? "Eternal"),
                    new TargetInfo(safeCell, map),
                    MessageTypeDefOf.NeutralEvent);

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Comp: Teleported {corpse.InnerPawn?.Name} from roof collapse to {safeCell}");
                }

                return RoofCollapseResponse.RemoveThing;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "Notify_OnBeforeRoofCollapse", corpseData?.OriginalPawn, ex);
                return RoofCollapseResponse.None;
            }
        }

        #endregion

        /// <summary>
        /// Exposes data for save/load functionality.
        /// </summary>
        public override void PostExposeData()
        {
            base.PostExposeData();

            try
            {
                Scribe_Values.Look(ref isInitialized, "isInitialized", false);
                Scribe_Values.Look(ref lastProgressUpdateTick, "lastProgressUpdateTick", 0);

                // Note: corpseData is managed by EternalCorpseManager and not saved here
                // to avoid duplication and synchronization issues
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "PostExposeData", corpseData?.OriginalPawn, ex);
            }
        }
    }

    /// <summary>
    /// CompProperties for EternalCorpseComponent.
    /// </summary>
    public class CompProperties_EternalCorpse : CompProperties
    {
        public CompProperties_EternalCorpse()
        {
            compClass = typeof(EternalCorpseComponent);
        }
    }
}