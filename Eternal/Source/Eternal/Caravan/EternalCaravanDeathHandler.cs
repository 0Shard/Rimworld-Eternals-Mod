// Relative Path: Eternal/Source/Eternal/Caravan/EternalCaravanDeathHandler.cs
// Creation Date: 29-10-2025
// Last Edit: 21-08-2026
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
using Eternal.World;
using Eternal.DI;

namespace Eternal.Caravan
{
    /// <summary>
    /// EternalCaravanDeathHandler handles teleportation of dead Eternals from caravans.
    /// When an Eternal dies in a caravan, they teleport to the player's colony after a delay.
    /// </summary>
    public class EternalCaravanDeathHandler : IExposable
    {
        private static EternalCaravanDeathHandler instance;
        public static EternalCaravanDeathHandler Instance => instance;

        private List<PendingTeleportation> pendingTeleportations = new List<PendingTeleportation>();
        private const int TELEPORTATION_DELAY = 25000; // ~12 hours in ticks
        // Synchronous death windows only. Persistence stays with the reserved crash-site owner.
        private readonly Dictionary<Pawn, SoloDeathRecovery> soloDeaths = new Dictionary<Pawn, SoloDeathRecovery>();

        public sealed class SoloDeathRecovery
        {
            internal EternalCaravanDeathHandler Handler;
            internal Pawn Pawn;
            internal RimWorld.Planet.Caravan Caravan;
            internal WorldObject_EternalCrashSite Site;
            internal PawnAssignmentSnapshot Assignments;
            internal bool DeferredRemoval;
            internal Verse.Corpse Corpse;
            internal bool OwnsReservation;
            internal List<HealingItem> PreCalculatedQueue;
        }

        public bool BeginSoloDeath(Pawn pawn, out SoloDeathRecovery state)
        {
            state = null;
            if (pawn == null || pawn.Dead || !pawn.HasTraitIgnoringSuppression(EternalDefOf.Eternal_GeneticMarker))
            {
                return true;
            }

            var caravan = pawn.GetCaravan();
            if (caravan == null || !caravan.Spawned || caravan.Faction != Faction.OfPlayer
                || caravan.PawnsListForReading.Any(member => member != pawn && caravan.IsOwner(member)))
            {
                return true;
            }

            if (soloDeaths.ContainsKey(pawn))
            {
                return false;
            }

            SoloDeathRecovery reservation = null;
            try
            {
                var previousSites = new HashSet<WorldObject_EternalCrashSite>(
                    Find.WorldObjects.AllWorldObjects.OfType<WorldObject_EternalCrashSite>());
                var site = SpaceCrashRescueService.CreateOrGetCrashSite(caravan.Tile);
                if (site?.Destroyed == true)
                {
                    site = null;
                }
                // A home map alone cannot guarantee persistent ownership if its later spawn
                // fails. Require the existing deep-save site before any native death mutation.
                if (site == null)
                {
                    EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                        "BeginSoloDeath", pawn, new InvalidOperationException(
                            "Deferred last-owner caravan death: no persistent recovery site can be reserved."));
                    return false;
                }
                reservation = new SoloDeathRecovery
                {
                    Handler = this,
                    Pawn = pawn,
                    Caravan = caravan,
                    Site = site,
                    OwnsReservation = !previousSites.Contains(site)
                };
                reservation.Assignments = PawnAssignmentSnapshot.CaptureFrom(pawn);
                reservation.PreCalculatedQueue = new EternalResurrectionCalculator().CalculateHealingQueue(pawn);
                soloDeaths.Add(pawn, reservation);
                state = reservation;
                return true;
            }
            catch (Exception ex)
            {
                try { RemoveUnusedReservation(reservation); }
                catch (Exception cleanupException)
                {
                    EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                        "BeginSoloDeath.ReservationCleanup", pawn, cleanupException);
                }
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection, "BeginSoloDeath", pawn, ex);
                return false;
            }
        }

        public bool DeferSoloCaravanRemoval(WorldObject worldObject)
        {
            var state = soloDeaths.Values.FirstOrDefault(window => ReferenceEquals(window.Caravan, worldObject));
            if (state == null)
            {
                return false;
            }

            state.DeferredRemoval = true;
            return true;
        }

        public bool StageSoloCorpse(RimWorld.Planet.Caravan caravan, Thing thing)
        {
            if (!(thing is Verse.Corpse corpse))
            {
                return false;
            }

            var state = soloDeaths.Values.FirstOrDefault(window => window.Caravan == caravan && window.Pawn == corpse.InnerPawn);
            if (state == null)
            {
                return false;
            }

            state.Corpse = corpse;
            // Empty caravans have no native inventory carrier. Never enter GiveThing here:
            // it destroys the item when no pawn exists, before the primary death hook runs.
            if (SpaceCrashRescueService.DeliverCorpsesToCrashSite(new[] { corpse }, caravan.Tile) > 0)
            {
                return true;
            }

            if (state.Site?.TryStageCorpseForRecovery(corpse) == true)
            {
                return true;
            }
            // The destination was reserved before the destructive pipeline. A failure here
            // must be reported rather than falling through to native inventory destruction.
            throw new InvalidOperationException("Reserved solo-caravan recovery destination rejected the corpse.");
        }

        public void EndSoloDeath(SoloDeathRecovery state)
        {
            if (state == null)
            {
                return;
            }

            if (!state.Pawn.Dead)
            {
                // Another Kill prefix may cancel after our preflight reserved a site.
                RemoveUnusedReservation(state);
                soloDeaths.Remove(state.Pawn);
                return;
            }

            var corpse = state.Corpse ?? state.Pawn.Corpse;
            if (corpse == null || corpse.Destroyed || corpse.InnerPawn != state.Pawn)
            {
                throw new InvalidOperationException("Solo-caravan death did not retain its native corpse; caravan cleanup deferred.");
            }

            // Pawn.Kill catches failures during AddPawnOrItem internally and can skip the
            // later primary death notification. Its finalizer must finish both guarantees
            // before releasing the window that protects the last native caravan owner.
            if (!HasPersistentCorpseOwner(corpse))
            {
                try { state.Site?.TryStageCorpseForRecovery(corpse); }
                catch (Exception ex)
                {
                    EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                        "EndSoloDeath.ReservedStaging", state.Pawn, ex);
                }
                if (!HasPersistentCorpseOwner(corpse))
                {
                    // A rejected transfer can leave the newly made corpse completely unowned.
                    // Retain it in the pre-reserved site's same deep list without detaching,
                    // moving or stealing anything that still belongs to another owner.
                    state.Site?.TryRetainUnownedCorpse(corpse);
                }
                if (!HasPersistentCorpseOwner(corpse))
                {
                    throw new InvalidOperationException("Solo-caravan corpse has no unique persistent recovery owner "
                        + $"(spawned={corpse.Spawned}, holder={corpse.ParentHolder?.GetType().FullName ?? "none"}); "
                        + "caravan cleanup deferred.");
                }
            }

            var manager = EternalServiceContainer.Instance?.CorpseManager;
            if (manager == null)
            {
                throw new InvalidOperationException("Solo-caravan corpse tracking is unavailable; caravan cleanup deferred.");
            }
            var entry = manager.GetCorpseData(state.Pawn);
            if (entry == null || !ReferenceEquals(entry.Corpse, corpse))
            {
                manager.RegisterCorpse(corpse, state.Pawn, state.Assignments, state.PreCalculatedQueue);
                entry = manager.GetCorpseData(state.Pawn);
            }
            if (entry == null || !ReferenceEquals(entry.Corpse, corpse) || !HasPersistentCorpseOwner(corpse))
            {
                throw new InvalidOperationException("Solo-caravan corpse registration or persistent ownership did not complete; caravan cleanup deferred.");
            }
            if ((entry.PreCalculatedHealingQueue == null || entry.PreCalculatedHealingQueue.Count == 0)
                && state.PreCalculatedQueue?.Count > 0)
            {
                // The generic Kill postfix can register an interrupted death without a queue
                // before this finalizer runs. Complete that entry without replacing real work
                // already captured by the primary death notification.
                entry.PreCalculatedHealingQueue = state.PreCalculatedQueue;
            }
            if (state.Assignments != null)
            {
                entry.AssignmentSnapshot = state.Assignments;
            }

            RemoveUnusedReservation(state);
            // Remove before native Destroy so its own prefix cannot retain an empty caravan.
            soloDeaths.Remove(state.Pawn);
            if (state.DeferredRemoval && state.Caravan.Spawned)
            {
                state.Caravan.Destroy();
            }
        }

        private static bool HasPersistentCorpseOwner(Verse.Corpse corpse)
        {
            if (corpse == null || corpse.Destroyed)
            {
                return false;
            }
            int deepOwners = Find.WorldObjects.AllWorldObjects.OfType<WorldObject_EternalCrashSite>()
                .Count(site => site.Spawned && !site.Destroyed && site.HasPendingCorpse(corpse));
            if (corpse.Spawned)
            {
                // Native map ownership can also supply holdingOwner; physical map ownership
                // is authoritative here, provided no site still deep-saves the same corpse.
                return deepOwners == 0 && corpse.Map != null && Find.Maps.Contains(corpse.Map);
            }
            return corpse.holdingOwner == null && deepOwners == 1;
        }

        private static void RemoveUnusedReservation(SoloDeathRecovery state)
        {
            var site = state?.Site;
            if (state?.OwnsReservation == true && site != null && site.Spawned && !site.Destroyed
                && !site.HasMap && !site.HasRecoveryContents)
            {
                site.Destroy();
            }
        }

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
                {
                    return false;
                }

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
        /// <remarks>
        /// Must stay parameterless: this handler is a plain IExposable nested inside
        /// <see cref="Eternal_Component"/> via Scribe_Deep without ctorArgs, so RimWorld
        /// rebuilds it on load through Activator.CreateInstance(type, null).
        /// </remarks>
        public EternalCaravanDeathHandler()
        {
            instance = this;
        }

        /// <summary>
        /// Executes every pending teleportation whose delay has elapsed. Driven by
        /// <see cref="TickOrchestrator"/>; this handler is not a registered game component.
        /// </summary>
        public void ProcessPendingTeleportations()
        {
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
            {
                return;
            }

            // Check if pawn has Eternal trait (including suppressed traits)
            if (!pawn.HasTraitIgnoringSuppression(EternalDefOf.Eternal_GeneticMarker))
            {
                return;
            }

            // Check if pawn is in a caravan
            if (!IsPawnInCaravan(pawn))
            {
                return;
            }

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
            {
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
            {
                return;
            }

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
            {
                return false;
            }

            // Check if living pawn is in caravan
            if (!pawn.Dead && pawn.GetCaravan() != null)
            {
                return true;
            }

            // Check if corpse is in a caravan
            var corpse = pawn.Corpse;
            if (corpse == null)
            {
                return false;
            }

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
        /// Exposes data for save/load functionality.
        /// </summary>
        public void ExposeData()
        {
            Scribe_Collections.Look(ref pendingTeleportations, "pendingTeleportations", LookMode.Deep);
        }
    }
}
