// Relative Path: Eternal/Source/Eternal/Caravan/EternalCaravanDeathHandler.cs
// Creation Date: 29-10-2025
// Last Edit: 21-02-2026
// Author: 0Shard
// Description: EternalCaravanDeathHandler handles teleportation of dead Eternals from caravans.
//              Provides static IsPawnInCaravan method for checking if a pawn or corpse is in a caravan.
//              Includes RegisterDeath overload for corpse registration with proper snapshot capture.
//              BUGFIX: Pre-calculates healing queue at death time before RimWorld removes injuries from dead pawns.
//              SAFE-04: Sets CaravanId (WorldObject.ID) on CorpseTrackingEntry at death time for persistent caravan lookup.

using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using RimWorld.Planet;
using Eternal.Exceptions;
using Eternal.Extensions;
using Eternal.Healing;
using Eternal.Models;
using Eternal.Utils;

namespace Eternal.Caravan
{
    /// <summary>
    /// EternalCaravanDeathHandler handles teleportation of dead Eternals from caravans.
    /// When an Eternal dies in a caravan, they teleport to the player's colony after a delay.
    /// </summary>
    public class EternalCaravanDeathHandler : GameComponent
    {
        private static EternalCaravanDeathHandler instance;
        public static EternalCaravanDeathHandler Instance => instance;
        
        private List<PendingTeleportation> pendingTeleportations = new List<PendingTeleportation>();
        private const int TELEPORTATION_DELAY = 25000; // ~12 hours in ticks
        private const int RECOVERY_DURATION = 18000; // ~6 hours in ticks
        
        /// <summary>
        /// Represents a pending teleportation for a dead Eternal.
        /// </summary>
        public class PendingTeleportation : IExposable
        {
            public Pawn pawn;
            public int deathTick;
            public IntVec3 deathLocation;
            public Verse.Map deathMap;
            public bool processed;
            
            /// <summary>
            /// Checks if teleportation should be processed.
            /// </summary>
            public bool ShouldProcess()
            {
                if (processed)
                    return false;
                    
                int currentTick = Find.TickManager.TicksGame;
                return currentTick - deathTick >= TELEPORTATION_DELAY;
            }
            
            /// <summary>
            /// Exposes data for save/load functionality.
            /// </summary>
            public void ExposeData()
            {
                Scribe_References.Look(ref pawn, "pawn");
                Scribe_Values.Look(ref deathTick, "deathTick", 0);
                Scribe_Values.Look(ref deathLocation, "deathLocation");
                Scribe_References.Look(ref deathMap, "deathMap");
                Scribe_Values.Look(ref processed, "processed", false);
            }
        }
        
        /// <summary>
        /// Initializes a new instance of EternalCaravanDeathHandler class.
        /// </summary>
        /// <param name="game">The current game instance.</param>
        public EternalCaravanDeathHandler(Game game)
        {
            instance = this;
        }
        
        /// <summary>
        /// Called every game tick to update teleportation states.
        /// </summary>
        public override void GameComponentUpdate()
        {
            base.GameComponentUpdate();
            
            // Process pending teleportations
            for (int i = pendingTeleportations.Count - 1; i >= 0; i--)
            {
                var teleportation = pendingTeleportations[i];
                if (teleportation.ShouldProcess())
                {
                    ProcessTeleportation(teleportation);
                    pendingTeleportations.RemoveAt(i);
                }
            }
        }
        
        /// <summary>
        /// Registers a dead Eternal for teleportation.
        /// </summary>
        /// <param name="pawn">The dead Eternal pawn.</param>
        /// <param name="location">The death location.</param>
        /// <param name="map">The death map.</param>
        public void RegisterDeath(Pawn pawn, IntVec3 location, Verse.Map map)
        {
            if (pawn == null || !pawn.Dead)
                return;
                
            // Check if pawn has Eternal trait (including suppressed traits)
            if (!pawn.HasTraitIgnoringSuppression(EternalDefOf.Eternal_GeneticMarker))
                return;
                
            // Check if pawn is in a caravan
            if (!IsPawnInCaravan(pawn))
                return;
                
            // Create pending teleportation
            var teleportation = new PendingTeleportation
            {
                pawn = pawn,
                deathTick = Find.TickManager.TicksGame,
                deathLocation = location,
                deathMap = map,
                processed = false
            };
            
            pendingTeleportations.Add(teleportation);
            
            // Log death registration
            if (Eternal_Mod.settings?.debugMode == true)
            {
                Log.Message($"Registered Eternal death for teleportation: {pawn.Name?.ToStringFull ?? "Unknown"} at {location}");
            }
        }

        /// <summary>
        /// Registers a death and starts tracking for corpses in caravans.
        /// This overload is used when corpse registration is needed (e.g., from death notification).
        /// </summary>
        /// <param name="pawn">The dead Eternal pawn.</param>
        /// <param name="corpse">The pawn's corpse.</param>
        public void RegisterDeath(Pawn pawn, Verse.Corpse corpse)
        {
            if (pawn == null)
                return;

            // Calculate healing queue NOW, before RimWorld removes injuries from dead pawn
            // This is critical because RimWorld may remove/modify hediffs on dead pawns over time
            List<HealingItem> preCalculatedQueue = null;
            try
            {
                var resurrectionCalculator = new EternalResurrectionCalculator();
                preCalculatedQueue = resurrectionCalculator.CalculateHealingQueue(pawn);

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Pre-calculated healing queue at caravan death for {pawn.Name}: {preCalculatedQueue?.Count ?? 0} items");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "RegisterDeath (pre-calculate healing queue)", pawn, ex);
            }

            // Find which caravan the corpse is in
            RimWorld.Planet.Caravan caravan = null;
            if (corpse != null && !corpse.Spawned && corpse.Tile >= 0)
            {
                foreach (var worldObject in Find.World.worldObjects.AllWorldObjects)
                {
                    if (worldObject is RimWorld.Planet.Caravan c && c.AllThings.Contains(corpse))
                    {
                        caravan = c;
                        break;
                    }
                }
            }

            if (caravan == null)
            {
                Log.Warning($"[Eternal] RegisterDeath called but corpse for {pawn.Name} is not in a caravan - using normal registration");
                // Fall back to normal corpse registration
                var corpseManager = Eternal_Component.Current?.CorpseManager;
                corpseManager?.RegisterCorpse(corpse, pawn, PawnAssignmentSnapshot.CaptureFrom(pawn), preCalculatedQueue);
                return;
            }

            Log.Message($"[Eternal] Registered caravan death for {pawn.Name} in caravan {caravan.Label}");

            // Register with corpse manager for tracking
            var manager = Eternal_Component.Current?.CorpseManager;
            if (manager != null)
            {
                var snapshot = PawnAssignmentSnapshot.CaptureFrom(pawn);
                manager.RegisterCorpse(corpse, pawn, snapshot, preCalculatedQueue);

                // SAFE-04: Persist the caravan's WorldObject.ID so it survives save/load.
                // CorpseTrackingEntry.PostLoadInit resolves this back to a live Caravan reference.
                var corpseData = manager.GetCorpseData(pawn);
                if (corpseData != null)
                {
                    corpseData.CaravanId = caravan.ID;
                }
            }
        }

        /// <summary>
        /// Processes a teleportation for a dead Eternal.
        /// </summary>
        /// <param name="teleportation">The teleportation to process.</param>
        private void ProcessTeleportation(PendingTeleportation teleportation)
        {
            if (teleportation.pawn == null || teleportation.processed)
                return;
                
            // Find player's home map
            Verse.Map homeMap = Find.AnyPlayerHomeMap;
            if (homeMap == null)
            {
                Log.Error("Cannot teleport Eternal - no player home map found");
                return;
            }
            
            // Find valid spawn location at map edge
            IntVec3 spawnLocation = FindValidSpawnLocation(homeMap);
            if (!spawnLocation.IsValid)
            {
                Log.Error($"Cannot teleport Eternal - no valid spawn location found on map {homeMap}");
                return;
            }
            
            // Teleport the pawn
            TeleportPawn(teleportation.pawn, spawnLocation, homeMap);
            
            // Mark as processed
            teleportation.processed = true;
            
            // Show notification and story text
            ShowTeleportationEvent(teleportation.pawn, teleportation.deathLocation, spawnLocation);
            
            // Log teleportation
            if (Eternal_Mod.settings?.debugMode == true)
            {
                Log.Message($"Teleported Eternal {teleportation.pawn.Name?.ToStringFull ?? "Unknown"} to {spawnLocation}");
            }
        }
        
        /// <summary>
        /// Checks if a pawn or their corpse is in a caravan.
        /// Uses direct type checking via Find.WorldObjects.Caravans for performance.
        /// </summary>
        /// <param name="pawn">The pawn to check.</param>
        /// <returns>True if pawn or corpse is in a caravan, false otherwise.</returns>
        public static bool IsPawnInCaravan(Pawn pawn)
        {
            if (pawn == null)
                return false;

            // Check if living pawn is in caravan
            if (!pawn.Dead && pawn.GetCaravan() != null)
                return true;

            // Check if corpse is in a caravan
            var corpse = pawn.Corpse;
            if (corpse == null)
                return false;

            // Corpse not spawned on a map means it's likely in world (caravan/storage)
            if (!corpse.Spawned && corpse.Tile >= 0)
            {
                foreach (var worldObject in Find.World.worldObjects.AllWorldObjects)
                {
                    if (worldObject is RimWorld.Planet.Caravan caravan && caravan.AllThings.Contains(corpse))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        
        /// <summary>
        /// Finds a valid spawn location at the edge of a map.
        /// </summary>
        /// <param name="map">The map to find spawn location on.</param>
        /// <returns>Valid spawn location, or invalid if none found.</returns>
        private IntVec3 FindValidSpawnLocation(Verse.Map map)
        {
            // Try multiple locations at map edge
            for (int i = 0; i < 20; i++)
            {
                IntVec3 location = CellFinder.RandomEdgeCell(map);
                if (location.IsValid && location.Standable(map))
                {
                    return location;
                }
            }
            
            return IntVec3.Invalid;
        }
        
        /// <summary>
        /// Teleports a pawn to a new location.
        /// </summary>
        /// <param name="pawn">The pawn to teleport.</param>
        /// <param name="location">The target location.</param>
        /// <param name="map">The target map.</param>
        private void TeleportPawn(Pawn pawn, IntVec3 location, Verse.Map map)
        {
            // Remove pawn from current location
            if (pawn.Map != null)
            {
                pawn.DeSpawn();
            }
            
            // Spawn at new location
            GenSpawn.Spawn(pawn, location, map);
            
            // Apply teleportation recovery hediff
            var recoveryHediff = HediffMaker.MakeHediff(EternalDefOf.Eternal_TeleportationRecovery, pawn);
            pawn.health.AddHediff(recoveryHediff);
            
            // Start regrowth process
            Eternal_Component.Instance?.StartRegrowth(pawn);
        }
        
        /// <summary>
        /// Shows the teleportation event notification and story text.
        /// </summary>
        /// <param name="pawn">The teleported pawn.</param>
        /// <param name="fromLocation">The original death location.</param>
        /// <param name="toLocation">The new spawn location.</param>
        private void ShowTeleportationEvent(Pawn pawn, IntVec3 fromLocation, IntVec3 toLocation)
        {
            // Create story text
            string storyText = $"Eternal Return\n\n" +
                             $"{pawn.Name?.ToStringFull ?? "Unknown"}, who died at {fromLocation}, " +
                             $"has miraculously returned to the colony at {toLocation}.\n\n" +
                             "The Eternal's biological regeneration has activated, " +
                             "drawing them back from the brink of oblivion. " +
                             "They will now begin the regrowth process.";
            
            // Show letter
            LetterDef letterDef = LetterDefOf.PositiveEvent;
            Letter letter = LetterMaker.MakeLetter(
                storyText,
                storyText,
                letterDef);
                 
            Find.LetterStack.ReceiveLetter(letter);
        }
        
        /// <summary>
        /// Gets all pending teleportations.
        /// </summary>
        /// <returns>List of pending teleportations.</returns>
        public List<PendingTeleportation> GetPendingTeleportations()
        {
            return new List<PendingTeleportation>(pendingTeleportations);
        }
        
        /// <summary>
        /// Exposes data for save/load functionality.
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref pendingTeleportations, "pendingTeleportations", LookMode.Deep);
        }
    }
}