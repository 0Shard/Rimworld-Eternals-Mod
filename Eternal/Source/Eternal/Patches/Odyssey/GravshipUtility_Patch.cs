// file path: Eternal/Source/Eternal/Patches/Odyssey/GravshipUtility_Patch.cs
// Author Name: 0Shard
// Date Created: 06-12-2025
// Date Last Modified: 04-03-2026
// Description: Harmony patches for Odyssey DLC compatibility.
//              Prevents Eternal pawns from being destroyed when left behind in space.
//              Instead, they fall to the planet surface and can be rescued.
//              Uses thread-safe local data to prevent state corruption between calls.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Eternal.Exceptions;
using Eternal.Extensions;
using Eternal.Utils;
using Eternal.World;

// Type alias to resolve namespace shadowing (Eternal.Map shadows Verse.Map)
using MapType = Verse.Map;

namespace Eternal.Patches.Odyssey
{
    /// <summary>
    /// Data structure to safely pass data between Prefix and Postfix.
    /// Created fresh for each method call to prevent state corruption.
    /// Must be public for Harmony's __state parameter to work correctly.
    /// </summary>
    public class AbandonMapContext
    {
        public List<Pawn> EternalsToSave { get; } = new List<Pawn>();
        public int SavedWorldTile { get; set; } = -1;
        public bool PrefixSucceeded { get; set; }
    }

    /// <summary>
    /// Harmony patches for GravshipUtility.AbandonMap to save Eternal pawns
    /// from being destroyed when the grav ship leaves without them.
    /// Uses reflection-based conditional patching to avoid TypeLoadException when
    /// the Odyssey DLC is absent. The patch is skipped entirely if Odyssey is not active.
    /// </summary>
    [HarmonyPatch]
    public static class GravshipUtility_AbandonMap_Patch
    {
        /// <summary>
        /// Guard: only apply this patch when the Odyssey DLC is active.
        /// Harmony calls Prepare() before applying the patch. Returning false skips it.
        /// </summary>
        public static bool Prepare() => ModsConfig.OdysseyActive;

        /// <summary>
        /// Resolve the target method via reflection so we never reference GravshipUtility
        /// at compile time. This prevents TypeLoadException when Odyssey is absent.
        /// Returns null to skip patching if the type or method cannot be found.
        /// </summary>
        public static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("RimWorld.GravshipUtility");
            if (type == null)
            {
                Log.Warning("[Eternal] Skipping GravshipUtility.AbandonMap patch — type not found (Odyssey absent or API changed)");
                return null;
            }

            var method = AccessTools.Method(type, "AbandonMap");
            if (method == null)
            {
                Log.Warning("[Eternal] Skipping GravshipUtility.AbandonMap patch — method not found (Odyssey API changed)");
            }

            return method;
        }

        /// <summary>
        /// Prefix: Identify and save Eternal pawns before map abandonment.
        /// Despawns them so the original method doesn't destroy them.
        /// Uses __state to pass context data safely to Postfix.
        /// </summary>
        [HarmonyPrefix]
        public static void SaveEternalsBeforeAbandonment(MapType map, out AbandonMapContext __state)
        {
            // Initialize state for this specific call
            __state = new AbandonMapContext();

            // Only run if Odyssey DLC is active
            if (!ModsConfig.OdysseyActive)
            {
                return;
            }

            if (map == null)
            {
                return;
            }

            try
            {
                __state.SavedWorldTile = map.Tile;

                // Find all Eternal pawns that would be destroyed
                // Use ToList() to create a snapshot since we'll be modifying the collection
                var allPawns = map.mapPawns?.AllPawnsSpawned?.ToList();
                if (allPawns == null)
                {
                    return;
                }

                foreach (var pawn in allPawns)
                {
                    if (pawn != null && pawn.IsValidEternal() && pawn.Faction == Faction.OfPlayer)
                    {
                        __state.EternalsToSave.Add(pawn);

                        // Despawn BEFORE the original loop destroys them
                        // Using WillReplace to preserve the pawn object
                        if (pawn.Spawned)
                        {
                            pawn.DeSpawn(DestroyMode.WillReplace);
                        }

                        Log.Message($"[Eternal] Saving {pawn.Name} from space destruction");
                    }
                }

                __state.PrefixSucceeded = true;

                if (__state.EternalsToSave.Count > 0)
                {
                    Log.Message($"[Eternal] Saved {__state.EternalsToSave.Count} Eternal pawn(s) from map abandonment");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "SaveEternalsBeforeAbandonment", null, ex);
                __state.PrefixSucceeded = false;
            }
        }

        /// <summary>
        /// Postfix: After map is abandoned, spawn Eternals at crash site on planet.
        /// Uses __state from Prefix to get the saved pawns.
        /// </summary>
        [HarmonyPostfix]
        public static void SpawnEternalsAtCrashSite(MapType map, AbandonMapContext __state)
        {
            // Only run if Odyssey DLC is active
            if (!ModsConfig.OdysseyActive)
            {
                return;
            }

            // Safety check - ensure state was properly initialized by Prefix
            if (__state == null || !__state.PrefixSucceeded)
            {
                return;
            }

            if (__state.EternalsToSave.Count == 0)
            {
                return;
            }

            try
            {
                // Create crash site at the world tile where the map was
                var crashSite = CreateOrGetCrashSite(__state.SavedWorldTile);

                if (crashSite == null)
                {
                    // Emergency fallback: try to spawn them at player's home colony
                    HandleCrashSiteFailure(__state.EternalsToSave);
                    return;
                }

                int savedCount = __state.EternalsToSave.Count;

                foreach (var eternal in __state.EternalsToSave)
                {
                    if (eternal == null || eternal.Destroyed)
                    {
                        continue;
                    }

                    // Apply fall damage (severe but non-lethal for Eternals)
                    ApplyFallDamage(eternal);

                    // Add to crash site for rescue/control
                    crashSite.AddPawn(eternal);

                    Log.Message($"[Eternal] {eternal.Name} fell to planet surface at crash site");
                }

                // Send letter to player
                Find.LetterStack.ReceiveLetter(
                    "EternalFellFromSpace".Translate(),
                    "EternalFellFromSpaceDesc".Translate(savedCount),
                    LetterDefOf.NegativeEvent,
                    crashSite);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "SpawnEternalsAtCrashSite", null, ex);
                // Try emergency fallback
                HandleCrashSiteFailure(__state.EternalsToSave);
            }
        }

        /// <summary>
        /// Emergency handler if crash site creation fails.
        /// Attempts to spawn pawns at player's main colony.
        /// </summary>
        private static void HandleCrashSiteFailure(List<Pawn> eternals)
        {
            Log.Warning("[Eternal] Failed to create crash site - attempting emergency recovery");

            try
            {
                // Find player's home map
                var homeMap = Find.Maps?.FirstOrDefault(m => m.IsPlayerHome);
                if (homeMap == null)
                {
                    Log.Error("[Eternal] No player home map found - Eternals may be lost!");
                    return;
                }

                foreach (var pawn in eternals)
                {
                    if (pawn == null || pawn.Destroyed)
                    {
                        continue;
                    }

                    // Find a spot near map edge
                    var entryCell = CellFinder.RandomEdgeCell(homeMap);
                    if (entryCell.IsValid)
                    {
                        GenSpawn.Spawn(pawn, entryCell, homeMap);
                        ApplyFallDamage(pawn);
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
                Log.Error("[Eternal] Failed to create WorldObject_EternalCrashSite - is the def loaded?");
                return null;
            }

            crashSite.Tile = worldTile;
            crashSite.SetFaction(Faction.OfPlayer);
            Find.WorldObjects.Add(crashSite);

            Log.Message($"[Eternal] Created crash site at world tile {worldTile}");

            return crashSite;
        }

        /// <summary>
        /// Applies fall damage to simulate falling from orbit.
        /// Eternals will regenerate, but should be severely injured initially.
        /// </summary>
        private static void ApplyFallDamage(Pawn pawn)
        {
            if (pawn?.health == null)
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

                // Additional injuries to limbs (legs especially)
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

                Log.Message($"[Eternal] Applied fall damage to {pawn.Name}");
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "ApplyFallDamage", pawn, ex);
            }
        }
    }
}
