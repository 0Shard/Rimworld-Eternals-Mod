// Relative Path: Eternal/Source/Eternal/Patches/SaveOurShip2/SOS2_OrbitingShipPatch.cs
// Creation Date: 25-12-2025
// Last Edit: 20-02-2026
// Author: 0Shard
// Description: Harmony patches for Save Our Ship 2 compatibility.
//              Intercepts WorldObjectOrbitingShip.ShouldRemoveMapNow to save Eternal pawns
//              before SOS2 kills all pawns during ship burn-up. Creates crash sites for rescue.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Eternal.Compatibility;
using Eternal.Exceptions;
using Eternal.Extensions;
using Eternal.Utils;
using Eternal.World;

// Type aliases to resolve namespace shadowing (Eternal.Map/Corpse shadows Verse types)
using MapType = Verse.Map;
using CorpseType = Verse.Corpse;

namespace Eternal.Patches.SaveOurShip2
{
    /// <summary>
    /// Context data for passing state between Prefix and Postfix patches.
    /// Created fresh for each method call to prevent state corruption.
    /// </summary>
    public class SOS2OrbitingShipContext
    {
        public List<Pawn> EternalsToRescue { get; } = new List<Pawn>();
        public int WorldTile { get; set; } = -1;
        public bool WasBurningUp { get; set; } = false;
        public bool PrefixSucceeded { get; set; } = false;
    }

    /// <summary>
    /// Harmony patches for SOS2's WorldObjectOrbitingShip.ShouldRemoveMapNow.
    ///
    /// CRITICAL: SOS2's ShouldRemoveMapNow kills ALL pawns when ShipMapState == burnUpSet.
    /// This patch intercepts before that happens to rescue Eternal pawns.
    /// </summary>
    [HarmonyPatch]
    public static class SOS2_OrbitingShipPatch
    {
        /// <summary>
        /// Harmony's Prepare method - only apply this patch if SOS2 is loaded.
        /// </summary>
        public static bool Prepare()
        {
            bool sos2Active = SpaceModDetection.SaveOurShip2Active;
            if (sos2Active && Eternal_Mod.settings?.debugMode == true)
            {
                Log.Message("[Eternal] SOS2 detected - enabling WorldObjectOrbitingShip patch");
            }
            return sos2Active;
        }

        /// <summary>
        /// Dynamically targets SOS2's WorldObjectOrbitingShip.ShouldRemoveMapNow method.
        /// </summary>
        public static MethodBase TargetMethod()
        {
            var shipType = SpaceModDetection.WorldObjectOrbitingShipType;
            if (shipType == null)
            {
                Log.Warning("[Eternal] Could not find WorldObjectOrbitingShip type for patching");
                return null;
            }

            var method = AccessTools.Method(shipType, "ShouldRemoveMapNow");
            if (method == null)
            {
                Log.Warning("[Eternal] Could not find ShouldRemoveMapNow method on WorldObjectOrbitingShip");
                return null;
            }

            if (Eternal_Mod.settings?.debugMode == true)
            {
                Log.Message($"[Eternal] Found SOS2 target method: {method.DeclaringType.FullName}.{method.Name}");
            }

            return method;
        }

        /// <summary>
        /// Prefix: Before SOS2's ShouldRemoveMapNow runs, check if ship is burning up.
        /// If so, rescue all Eternal pawns before they're killed.
        /// </summary>
        [HarmonyPrefix]
        public static void RescueEternalsBeforeBurnUp(MapParent __instance, out SOS2OrbitingShipContext __state)
        {
            __state = new SOS2OrbitingShipContext();

            try
            {
                // Only act if this is an orbiting ship that's burning up
                if (!SpaceModDetection.IsOrbitingShip(__instance))
                {
                    return;
                }

                if (!SpaceModDetection.IsShipBurningUp(__instance))
                {
                    return;
                }

                __state.WasBurningUp = true;
                __state.WorldTile = __instance.Tile;

                // Check if the map exists and has pawns
                if (!__instance.HasMap || __instance.Map == null)
                {
                    return;
                }

                MapType map = __instance.Map;

                // Find all Eternal pawns (both alive and dead) before SOS2 kills them
                var allPawns = map.mapPawns?.AllPawnsSpawned?.ToList();
                if (allPawns == null || allPawns.Count == 0)
                {
                    return;
                }

                foreach (var pawn in allPawns)
                {
                    // Check if this is an Eternal pawn that belongs to the player
                    if (pawn != null && pawn.IsValidEternal() && pawn.Faction == Faction.OfPlayer)
                    {
                        __state.EternalsToRescue.Add(pawn);

                        // Despawn BEFORE SOS2's kill loop runs
                        // Using WillReplace to preserve the pawn object
                        if (pawn.Spawned)
                        {
                            pawn.DeSpawn(DestroyMode.WillReplace);
                        }

                        Log.Message($"[Eternal] Rescued {pawn.Name} from SOS2 ship burn-up");
                    }
                }

                // Also check for Eternal corpses
                var corpses = map.listerThings?.ThingsInGroup(ThingRequestGroup.Corpse)?.ToList();
                if (corpses != null)
                {
                    foreach (var thing in corpses)
                    {
                        if (thing is CorpseType corpse && corpse.InnerPawn != null)
                        {
                            var innerPawn = corpse.InnerPawn;
                            if (innerPawn.IsValidEternal())
                            {
                                // Add the inner pawn to rescue list
                                __state.EternalsToRescue.Add(innerPawn);

                                // Despawn the corpse
                                if (corpse.Spawned)
                                {
                                    corpse.DeSpawn(DestroyMode.WillReplace);
                                }

                                Log.Message($"[Eternal] Rescued corpse of {innerPawn.Name} from SOS2 ship burn-up");
                            }
                        }
                    }
                }

                __state.PrefixSucceeded = true;

                if (__state.EternalsToRescue.Count > 0)
                {
                    Log.Message($"[Eternal] Saved {__state.EternalsToRescue.Count} Eternal(s) from SOS2 ship burn-up");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "RescueEternalsBeforeBurnUp", null, ex);
                __state.PrefixSucceeded = false;
            }
        }

        /// <summary>
        /// Postfix: After SOS2's method runs, create crash sites for rescued Eternals.
        /// </summary>
        [HarmonyPostfix]
        public static void CreateCrashSitesForRescuedEternals(MapParent __instance, SOS2OrbitingShipContext __state)
        {
            // Safety checks
            if (__state == null || !__state.PrefixSucceeded || !__state.WasBurningUp)
            {
                return;
            }

            if (__state.EternalsToRescue.Count == 0)
            {
                return;
            }

            try
            {
                // Create crash site at the ship's world tile
                var crashSite = CreateOrGetCrashSite(__state.WorldTile);

                if (crashSite == null)
                {
                    // Emergency fallback: spawn at player colony
                    HandleCrashSiteFailure(__state.EternalsToRescue);
                    return;
                }

                foreach (var eternal in __state.EternalsToRescue)
                {
                    if (eternal == null || eternal.Destroyed)
                    {
                        continue;
                    }

                    // Apply fall damage for living pawns (dead ones already have injuries)
                    if (!eternal.Dead)
                    {
                        ApplyFallDamage(eternal);
                    }

                    // Add to crash site
                    crashSite.AddPawn(eternal);

                    Log.Message($"[Eternal] {eternal.Name} added to crash site from SOS2 ship burn-up");
                }

                // Send letter to player
                Find.LetterStack.ReceiveLetter(
                    "EternalFellFromSpace".Translate(),
                    "EternalSOS2ShipBurnUp".Translate(__state.EternalsToRescue.Count),
                    LetterDefOf.NegativeEvent,
                    crashSite);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "CreateCrashSitesForRescuedEternals", null, ex);
                // Emergency fallback
                HandleCrashSiteFailure(__state.EternalsToRescue);
            }
        }

        /// <summary>
        /// Creates a new crash site or returns existing one at the tile.
        /// </summary>
        private static WorldObject_EternalCrashSite CreateOrGetCrashSite(int worldTile)
        {
            if (worldTile < 0)
            {
                Log.Error("[Eternal] Invalid world tile for crash site");
                return null;
            }

            // Check if crash site already exists at this tile
            var existing = Find.WorldObjects?.AllWorldObjects
                ?.OfType<WorldObject_EternalCrashSite>()
                .FirstOrDefault(x => x.Tile == worldTile);

            if (existing != null)
            {
                return existing;
            }

            // Create new crash site
            var crashSiteDef = EternalDefOf.Eternal_CrashSite;
            if (crashSiteDef == null)
            {
                Log.Error("[Eternal] Eternal_CrashSite WorldObjectDef not found");
                return null;
            }

            var crashSite = (WorldObject_EternalCrashSite)WorldObjectMaker.MakeWorldObject(crashSiteDef);

            if (crashSite == null)
            {
                Log.Error("[Eternal] Failed to create WorldObject_EternalCrashSite");
                return null;
            }

            crashSite.Tile = worldTile;
            crashSite.SetFaction(Faction.OfPlayer);
            Find.WorldObjects.Add(crashSite);

            Log.Message($"[Eternal] Created crash site at world tile {worldTile} for SOS2 rescue");

            return crashSite;
        }

        /// <summary>
        /// Emergency handler if crash site creation fails.
        /// </summary>
        private static void HandleCrashSiteFailure(List<Pawn> eternals)
        {
            Log.Warning("[Eternal] SOS2 crash site creation failed - attempting emergency recovery");

            try
            {
                var homeMap = Find.Maps?.FirstOrDefault(m => m.IsPlayerHome);
                if (homeMap == null)
                {
                    Log.Error("[Eternal] No player home map found for emergency recovery!");
                    return;
                }

                foreach (var pawn in eternals)
                {
                    if (pawn == null || pawn.Destroyed)
                    {
                        continue;
                    }

                    var entryCell = CellFinder.RandomEdgeCell(homeMap);
                    if (entryCell.IsValid)
                    {
                        // Spawn living pawns or their corpses
                        Thing toSpawn = pawn.Dead ? (Thing)pawn.Corpse : (Thing)pawn;
                        if (toSpawn != null)
                        {
                            GenSpawn.Spawn(toSpawn, entryCell, homeMap);
                        }

                        if (!pawn.Dead)
                        {
                            ApplyFallDamage(pawn);
                        }

                        Log.Message($"[Eternal] Emergency spawned {pawn.Name} at colony edge");
                    }
                }

                Find.LetterStack.ReceiveLetter(
                    "EternalFellFromSpace".Translate(),
                    "EternalEmergencyRecovery".Translate(),
                    LetterDefOf.NegativeEvent);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "HandleCrashSiteFailure", null, ex);
            }
        }

        /// <summary>
        /// Applies fall damage to simulate falling from orbit.
        /// </summary>
        private static void ApplyFallDamage(Pawn pawn)
        {
            if (pawn?.health == null || pawn.Dead)
            {
                return;
            }

            try
            {
                // Apply significant blunt damage to torso
                var damageInfo = new DamageInfo(
                    DamageDefOf.Blunt,
                    amount: 80f,
                    armorPenetration: 0f,
                    angle: -1f,
                    instigator: null,
                    hitPart: null,
                    weapon: null);

                pawn.TakeDamage(damageInfo);

                // Additional injuries to limbs
                var limbParts = pawn.health.hediffSet?.GetNotMissingParts()
                    ?.Where(p => p.def?.tags != null &&
                           p.def.tags.Contains(BodyPartTagDefOf.MovingLimbCore))
                    .ToList();

                if (limbParts != null)
                {
                    foreach (var part in limbParts)
                    {
                        var limbDamage = new DamageInfo(
                            DamageDefOf.Blunt,
                            amount: 25f,
                            armorPenetration: 0f,
                            angle: -1f,
                            instigator: null,
                            hitPart: part,
                            weapon: null);

                        pawn.TakeDamage(limbDamage);
                    }
                }

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Applied SOS2 fall damage to {pawn.Name}");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "ApplyFallDamage", pawn, ex);
            }
        }
    }
}
