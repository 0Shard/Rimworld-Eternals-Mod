// file path: Eternal/Source/Eternal/Hediffs/EternalTeleportationRecovery_Hediff.cs
// Author Name: 0Shard
// Date Created: 29-10-2025
// Date Last Modified: 29-10-2025
// Description: EternalTeleportationRecovery hediff handles recovery period after teleportation.

using System;
using UnityEngine;
using Verse;
using RimWorld;

namespace Eternal.Hediffs
{
    /// <summary>
    /// EternalTeleportationRecovery hediff handles recovery period after teleportation.
    /// Applied to Eternals after they teleport from caravans to prevent immediate regrowth.
    /// </summary>
    public class EternalTeleportationRecovery_Hediff : HediffWithComps
    {
        private const int RECOVERY_DURATION = 18000; // ~6 hours in ticks
        private int startTick;
        
        /// <summary>
        /// Initializes a new instance of EternalTeleportationRecovery_Hediff class.
        /// </summary>
        public EternalTeleportationRecovery_Hediff()
        {
            startTick = Find.TickManager.TicksGame;
        }
        
        /// <summary>
        /// Gets the current recovery progress.
        /// </summary>
        public float RecoveryProgress
        {
            get
            {
                int currentTick = Find.TickManager.TicksGame;
                float progress = (float)(currentTick - startTick) / RECOVERY_DURATION;
                return Mathf.Clamp01(progress);
            }
        }
        
        /// <summary>
        /// Gets the remaining recovery time.
        /// </summary>
        public int RemainingTicks
        {
            get
            {
                int currentTick = Find.TickManager.TicksGame;
                int elapsed = currentTick - startTick;
                return Math.Max(0, RECOVERY_DURATION - elapsed);
            }
        }
        
        /// <summary>
        /// Gets whether recovery is complete.
        /// </summary>
        public bool RecoveryComplete
        {
            get
            {
                return RemainingTicks <= 0;
            }
        }
        
        /// <summary>
        /// Gets the hediff label.
        /// </summary>
        public override string LabelBase
        {
            get
            {
                if (RecoveryComplete)
                    return "Teleportation Recovery Complete";
                    
                return "Teleportation Recovery";
            }
        }
        
        /// <summary>
        /// Gets the hediff description.
        /// </summary>
        public override string Description
        {
            get
            {
                float progress = RecoveryProgress * 100f;
                return $"This Eternal is recovering from teleportation.\n\n" +
                       $"Progress: {progress:F1}%\n" +
                       $"Time remaining: {RemainingTicks / 2500:F1} hours";
            }
        }
        
        /// <summary>
        /// Gets whether the hediff should be visible.
        /// </summary>
        public override bool Visible
        {
            get
            {
                return true;
            }
        }
        
        /// <summary>
        /// Gets whether the hediff is bad.
        /// </summary>
        public bool IsBad
        {
            get
            {
                return !RecoveryComplete;
            }
        }
        
        /// <summary>
        /// Gets whether the hediff is tendable.
        /// </summary>
        public bool Tendable
        {
            get
            {
                return false;
            }
        }
        
        /// <summary>
        /// Gets whether the hediff should be displayed on health tab.
        /// </summary>
        public bool ShouldDisplay
        {
            get
            {
                return true;
            }
        }
        
        /// <summary>
        /// Called every tick to update recovery state.
        /// </summary>
        public override void Tick()
        {
            base.Tick();
            
            // Check if recovery is complete
            if (RecoveryComplete)
            {
                CompleteRecovery();
            }
        }
        
        /// <summary>
        /// Completes the recovery process.
        /// </summary>
        private void CompleteRecovery()
        {
            if (pawn == null)
                return;
                
            // Remove this hediff
            pawn.health.RemoveHediff(this);
            
            // Log recovery completion
            if (Eternal_Mod.settings?.debugMode == true)
            {
                Log.Message($"Teleportation recovery complete for {pawn.Name?.ToStringFull ?? "Unknown"}");
            }
            
            // Show notification
            Messages.Message(
                new Message($"{pawn.Name?.ToStringFull ?? "Unknown"} has completed teleportation recovery.", 
                MessageTypeDefOf.PositiveEvent));
        }
        
        /// <summary>
        /// Exposes data for save/load functionality.
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref startTick, "startTick", 0);
        }
    }
}