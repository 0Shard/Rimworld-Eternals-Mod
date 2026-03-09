// Relative Path: Eternal/Source/Eternal/Patches/SaveOurShip2/SOS2_ShipDestroyPatch.cs
// Creation Date: 25-12-2025
// Last Edit: 20-02-2026
// Author: 0Shard
// Description: Harmony patches for Save Our Ship 2 ship abandonment.
//              Intercepts WorldObjectOrbitingShip.Destroy to save Eternal corpses
//              before the ship world object is removed. Creates crash sites for resurrection.

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
    /// Context data for ship destruction patches.
    /// </summary>
    public class SOS2ShipDestroyContext
    {
        public List<Pawn> EternalCorpses { get; } = new List<Pawn>();
        public int WorldTile { get; set; } = -1;
        public bool PrefixSucceeded { get; set; } = false;
    }

    /// <summary>
    /// Harmony patches for SOS2's WorldObjectOrbitingShip.Destroy.
    /// Saves Eternal corpses when a ship is abandoned via the gizmo.
    /// </summary>
    [HarmonyPatch]
    public static class SOS2_ShipDestroyPatch
    {
        /// <summary>
        /// Only apply this patch if SOS2 is loaded.
        /// </summary>
        public static bool Prepare()
        {
            return SpaceModDetection.SaveOurShip2Active;
        }

        /// <summary>
        /// Dynamically targets SOS2's WorldObjectOrbitingShip.Destroy method.
        /// </summary>
        public static MethodBase TargetMethod()
        {
            var shipType = SpaceModDetection.WorldObjectOrbitingShipType;
            if (shipType == null)
            {
                return null;
            }

            // Get the Destroy method (override from WorldObject)
            var method = AccessTools.Method(shipType, "Destroy");
            if (method == null)
            {
                Log.Warning("[Eternal] Could not find Destroy method on WorldObjectOrbitingShip");
                return null;
            }

            if (Eternal_Mod.settings?.debugMode == true)
            {
                Log.Message($"[Eternal] Found SOS2 Destroy method: {method.DeclaringType.FullName}.{method.Name}");
            }

            return method;
        }

        /// <summary>
        /// Prefix: Before ship is destroyed, save Eternal corpses for later rescue.
        /// Note: Live Eternals are handled by SOS2_OrbitingShipPatch if ship is burning up.
        /// This focuses on corpses that might be left behind during abandonment.
        /// </summary>
        [HarmonyPrefix]
        public static void SaveEternalCorpsesBeforeDestroy(WorldObject __instance, out SOS2ShipDestroyContext __state)
        {
            __state = new SOS2ShipDestroyContext();

            try
            {
                // Only act on orbiting ships
                if (!SpaceModDetection.IsOrbitingShip(__instance as MapParent))
                {
                    return;
                }

                var mapParent = __instance as MapParent;
                if (mapParent == null || !mapParent.HasMap)
                {
                    return;
                }

                __state.WorldTile = __instance.Tile;
                MapType map = mapParent.Map;

                // Find Eternal corpses on the map
                var corpses = map.listerThings?.ThingsInGroup(ThingRequestGroup.Corpse)?.ToList();
                if (corpses == null || corpses.Count == 0)
                {
                    return;
                }

                foreach (var thing in corpses)
                {
                    if (thing is CorpseType corpse && corpse.InnerPawn != null)
                    {
                        var innerPawn = corpse.InnerPawn;
                        if (innerPawn.IsValidEternal())
                        {
                            __state.EternalCorpses.Add(innerPawn);

                            // Despawn the corpse before the ship is destroyed
                            if (corpse.Spawned)
                            {
                                corpse.DeSpawn(DestroyMode.WillReplace);
                            }

                            Log.Message($"[Eternal] Saved corpse of {innerPawn.Name} from SOS2 ship destruction");
                        }
                    }
                }

                // Also save any living Eternals that weren't caught by burn-up patch
                // (e.g., ship abandoned without burning up)
                var livingEternals = map.mapPawns?.AllPawnsSpawned
                    ?.Where(p => p != null && p.IsValidEternal() && p.Faction == Faction.OfPlayer && !p.Dead)
                    .ToList();

                if (livingEternals != null)
                {
                    foreach (var pawn in livingEternals)
                    {
                        __state.EternalCorpses.Add(pawn);

                        if (pawn.Spawned)
                        {
                            pawn.DeSpawn(DestroyMode.WillReplace);
                        }

                        Log.Message($"[Eternal] Saved living {pawn.Name} from SOS2 ship destruction");
                    }
                }

                __state.PrefixSucceeded = true;

                if (__state.EternalCorpses.Count > 0)
                {
                    Log.Message($"[Eternal] Saved {__state.EternalCorpses.Count} Eternal(s) from SOS2 ship destruction");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "SaveEternalCorpsesBeforeDestroy", null, ex);
                __state.PrefixSucceeded = false;
            }
        }

        /// <summary>
        /// Postfix: After ship is destroyed, create crash sites for saved Eternals.
        /// </summary>
        [HarmonyPostfix]
        public static void CreateCrashSitesForSavedEternals(WorldObject __instance, SOS2ShipDestroyContext __state)
        {
            if (__state == null || !__state.PrefixSucceeded || __state.EternalCorpses.Count == 0)
            {
                return;
            }

            try
            {
                // Create crash site at the ship's last known position
                var crashSite = CreateOrGetCrashSite(__state.WorldTile);

                if (crashSite == null)
                {
                    HandleCrashSiteFailure(__state.EternalCorpses);
                    return;
                }

                foreach (var eternal in __state.EternalCorpses)
                {
                    if (eternal == null || eternal.Destroyed)
                    {
                        continue;
                    }

                    // Apply fall damage for living pawns
                    if (!eternal.Dead)
                    {
                        ApplyFallDamage(eternal);
                    }

                    crashSite.AddPawn(eternal);

                    Log.Message($"[Eternal] {eternal.Name} added to crash site after SOS2 ship destruction");
                }

                // Notify player
                Find.LetterStack.ReceiveLetter(
                    "EternalFellFromSpace".Translate(),
                    "EternalSOS2ShipAbandoned".Translate(__state.EternalCorpses.Count),
                    LetterDefOf.NegativeEvent,
                    crashSite);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "CreateCrashSitesForSavedEternals", null, ex);
                HandleCrashSiteFailure(__state.EternalCorpses);
            }
        }

        /// <summary>
        /// Creates or retrieves an existing crash site at the world tile.
        /// </summary>
        private static WorldObject_EternalCrashSite CreateOrGetCrashSite(int worldTile)
        {
            if (worldTile < 0)
            {
                return null;
            }

            var existing = Find.WorldObjects?.AllWorldObjects
                ?.OfType<WorldObject_EternalCrashSite>()
                .FirstOrDefault(x => x.Tile == worldTile);

            if (existing != null)
            {
                return existing;
            }

            var crashSiteDef = EternalDefOf.Eternal_CrashSite;
            if (crashSiteDef == null)
            {
                Log.Error("[Eternal] Eternal_CrashSite WorldObjectDef not found");
                return null;
            }

            var crashSite = (WorldObject_EternalCrashSite)WorldObjectMaker.MakeWorldObject(crashSiteDef);
            if (crashSite == null)
            {
                return null;
            }

            crashSite.Tile = worldTile;
            crashSite.SetFaction(Faction.OfPlayer);
            Find.WorldObjects.Add(crashSite);

            return crashSite;
        }

        /// <summary>
        /// Emergency fallback if crash site creation fails.
        /// </summary>
        private static void HandleCrashSiteFailure(List<Pawn> eternals)
        {
            try
            {
                var homeMap = Find.Maps?.FirstOrDefault(m => m.IsPlayerHome);
                if (homeMap == null)
                {
                    Log.Error("[Eternal] No home map for emergency recovery!");
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
                        Thing toSpawn = pawn.Dead ? (Thing)pawn.Corpse : (Thing)pawn;
                        if (toSpawn != null)
                        {
                            GenSpawn.Spawn(toSpawn, entryCell, homeMap);
                        }

                        if (!pawn.Dead)
                        {
                            ApplyFallDamage(pawn);
                        }
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
        /// Applies fall damage for living Eternals.
        /// </summary>
        private static void ApplyFallDamage(Pawn pawn)
        {
            if (pawn?.health == null || pawn.Dead)
            {
                return;
            }

            try
            {
                var damageInfo = new DamageInfo(
                    DamageDefOf.Blunt,
                    amount: 80f,
                    armorPenetration: 0f);

                pawn.TakeDamage(damageInfo);

                var limbParts = pawn.health.hediffSet?.GetNotMissingParts()
                    ?.Where(p => p.def?.tags?.Contains(BodyPartTagDefOf.MovingLimbCore) == true)
                    .ToList();

                if (limbParts != null)
                {
                    foreach (var part in limbParts)
                    {
                        pawn.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 25f, 0f, -1f, null, part));
                    }
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
