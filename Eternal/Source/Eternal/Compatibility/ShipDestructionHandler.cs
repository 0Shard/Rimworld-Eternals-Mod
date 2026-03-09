// Relative Path: Eternal/Source/Eternal/Compatibility/ShipDestructionHandler.cs
// Creation Date: 12-11-2025
// Last Edit: 20-02-2026
// Author: 0Shard
// Description: Handles emergency procedures when ships are destroyed in space with Eternal pawns aboard.
//              Provides teleportation to player bases or creates debris field maps as fallback.

using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using RimWorld.Planet;
using Eternal.Exceptions;
using Eternal.Map;
using Eternal.Utils;

namespace Eternal.Compatibility
{
    /// <summary>
    /// Handles emergency procedures when ships are destroyed in space.
    /// Ensures Eternal pawns and their corpses are not lost permanently.
    /// </summary>
    public static class ShipDestructionHandler
    {
        /// <summary>
        /// Main handler for ship destruction events involving Eternal pawns.
        /// Decides between teleportation and debris field creation based on available options.
        /// </summary>
        /// <param name="eternalPawn">The Eternal pawn (dead or alive) that needs rescue</param>
        /// <param name="destroyedMap">The map that is being destroyed</param>
        public static void HandleShipDestruction(Pawn eternalPawn, Verse.Map destroyedMap)
        {
            try
            {
                if (eternalPawn == null)
                {
                    Log.Warning("[Eternal] HandleShipDestruction called with null pawn");
                    return;
                }

                if (destroyedMap == null)
                {
                    Log.Warning("[Eternal] HandleShipDestruction called with null map");
                    return;
                }

                // Log the event
                Log.Message($"[Eternal] Handling ship destruction for Eternal pawn {eternalPawn.Name} on map {destroyedMap}");

                // Strategy 1: Teleport to player base if available
                if (SpaceModDetection.IsPlayerBaseAvailable())
                {
                    if (Eternal_Mod.settings?.debugMode == true)
                    {
                        Log.Message($"[Eternal] Player base available - teleporting {eternalPawn.Name}");
                    }
                    
                    TeleportToPlayerBase(eternalPawn, destroyedMap);
                    return;
                }

                // No player base available - log warning
                Log.Warning($"[Eternal] Could not handle ship destruction for {eternalPawn.Name} - no player base available");

                // Send player notification about the problem
                Messages.Message(
                    $"Eternal pawn {eternalPawn.Name.ToStringShort} is trapped on a destroyed ship with no player base available!",
                    MessageTypeDefOf.NegativeEvent);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "HandleShipDestruction", eternalPawn, ex);
            }
        }

        /// <summary>
        /// Teleports an Eternal pawn (or their corpse) to the player's home base.
        /// </summary>
        /// <param name="pawn">The pawn to teleport</param>
        /// <param name="sourceMap">The map they're being teleported from</param>
        private static void TeleportToPlayerBase(Pawn pawn, Verse.Map sourceMap)
        {
            try
            {
                Verse.Map targetMap = SpaceModDetection.GetPlayerHomeMap();
                if (targetMap == null)
                {
                    Log.Error($"[Eternal] TeleportToPlayerBase failed - no home map found");
                    return;
                }

                // Find a safe spawn location
                IntVec3 spawnLocation = FindSafeSpawnLocation(targetMap);
                if (!spawnLocation.IsValid)
                {
                    Log.Error($"[Eternal] TeleportToPlayerBase failed - no valid spawn location");
                    return;
                }

                // Handle dead vs alive pawn differently
                Thing thingToTeleport = pawn.Dead ? (Thing)pawn.Corpse : (Thing)pawn;
                
                if (thingToTeleport == null)
                {
                    Log.Error($"[Eternal] TeleportToPlayerBase failed - pawn/corpse is null");
                    return;
                }

                // Remove from source map
                thingToTeleport.DeSpawn();

                // Spawn at target location
                GenSpawn.Spawn(thingToTeleport, spawnLocation, targetMap);

                // Notify player
                string message = pawn.Dead 
                    ? $"The corpse of Eternal pawn {pawn.Name.ToStringShort} was emergency teleported to {targetMap.Parent.Label}."
                    : $"Eternal pawn {pawn.Name.ToStringShort} was emergency teleported to {targetMap.Parent.Label}.";
                
                Messages.Message(message, new TargetInfo(spawnLocation, targetMap), MessageTypeDefOf.NeutralEvent);

                // Create visual effect at teleport location
                CreateTeleportEffect(spawnLocation, targetMap);

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Successfully teleported {pawn.Name} to {targetMap} at {spawnLocation}");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "TeleportToPlayerBase", pawn, ex);
            }
        }

        /// <summary>
        /// Finds a safe spawn location on the target map for teleportation.
        /// Prefers outdoor locations near the map center.
        /// </summary>
        /// <param name="map">The target map</param>
        /// <returns>A valid spawn location, or IntVec3.Invalid if none found</returns>
        private static IntVec3 FindSafeSpawnLocation(Verse.Map map)
        {
            try
            {
                // Try to find a spot near the map center that's standable
                IntVec3 center = map.Center;
                
                // Search in expanding radius from center
                for (int radius = 5; radius < 50; radius += 5)
                {
                    IntVec3 candidate = CellFinder.RandomClosewalkCellNear(center, map, radius);
                    
                    if (candidate.IsValid && 
                        candidate.Standable(map) && 
                        !candidate.Fogged(map))
                    {
                        return candidate;
                    }
                }

                // Fallback: any valid spawn location
                return CellFinder.RandomSpawnCellForPawnNear(center, map);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.GameStateInvalid,
                    "FindSafeSpawnLocation", null, ex);
                return IntVec3.Invalid;
            }
        }

        /// <summary>
        /// Creates a visual teleportation effect at the spawn location.
        /// </summary>
        /// <param name="location">The location to create the effect</param>
        /// <param name="map">The map to create the effect on</param>
        private static void CreateTeleportEffect(IntVec3 location, Verse.Map map)
        {
            try
            {
                // Create a flash effect or mote at the teleport location
                // Using RimWorld's effect system
                FleckMaker.ThrowSmoke(location.ToVector3(), map, 2f);
                FleckMaker.ThrowLightningGlow(location.ToVector3(), map, 2f);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "CreateTeleportEffect", null, ex);
            }
        }
    }
}
