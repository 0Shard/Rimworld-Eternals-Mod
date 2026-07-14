// Relative Path: Eternal/Source/Eternal/World/SpaceCrashRescueService.cs
// Creation Date: 13-07-2026
// Last Edit: 14-07-2026
// Author: 0Shard
// Description: Shared rescue pipeline for Eternals destroyed or stranded in space.
//              Converts victims to torso-only corpses (terminal-velocity re-entry), updates their
//              pre-calculated healing queue, and stores them in a ground crash site for recovery.
//              Consolidates the crash-site helpers previously duplicated across the
//              Odyssey/SOS2/VGE patch classes (CreateOrGetCrashSite, ApplyFallDamage, home fallback).
//              Added DeliverCorpsesToCrashSite: non-stripping delivery for corpses rescued from
//              container sweeps (corpse-only pod arrivals, silent Vanish-destroys).

using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Extensions;
using Eternal.Healing;
using Eternal.Utils;

// Type aliases to resolve namespace shadowing (Eternal.Map/Corpse shadow Verse types)
using MapType = Verse.Map;
using CorpseType = Verse.Corpse;

namespace Eternal.World
{
    /// <summary>
    /// Shared pipeline for rescuing Eternals from space destruction events.
    /// Living victims are killed in place (so death registration captures a snapshot),
    /// stripped to a torso-only corpse, and delivered to a crash site on the surface
    /// tile beneath the space tile. The normal corpse-resurrection pipeline recovers them.
    /// </summary>
    public static class SpaceCrashRescueService
    {
        /// <summary>
        /// Organ tags stripped by terminal-velocity re-entry. Tag-based (not defName-based)
        /// so modded bodies (EBF etc.) with renamed organs are still covered.
        /// </summary>
        private static readonly BodyPartTagDef[] StrippedOrganTags =
        {
            BodyPartTagDefOf.BloodPumpingSource,
            BodyPartTagDefOf.BloodFiltrationSource,
            BodyPartTagDefOf.BloodFiltrationLiver,
            BodyPartTagDefOf.BloodFiltrationKidney,
            BodyPartTagDefOf.BreathingSource,
            BodyPartTagDefOf.MetabolismSource,
        };

        /// <summary>
        /// Rescues Eternals from a doomed space map/area. Call from a Harmony PREFIX while the
        /// source map still exists: living pawns are killed in place so Eternal_Hediff.Notify_PawnDied
        /// registers them with a work/policy snapshot, then every victim is stripped to a torso,
        /// its healing queue recalculated, and its corpse despawned into a ground crash site.
        /// </summary>
        /// <param name="livingEternals">Living Eternal pawns still spawned on the doomed map (may be null)</param>
        /// <param name="corpses">Eternal corpses still spawned on the doomed map (may be null)</param>
        /// <param name="spaceTile">World tile of the doomed map/world object</param>
        /// <returns>Number of Eternals delivered to the crash site</returns>
        public static int CrashDownEternals(IEnumerable<Pawn> livingEternals, IEnumerable<CorpseType> corpses, PlanetTile spaceTile)
        {
            var rescuedCorpses = new List<CorpseType>();

            try
            {
                foreach (var pawn in livingEternals ?? Enumerable.Empty<Pawn>())
                {
                    var corpse = KillInPlace(pawn);
                    if (corpse != null)
                    {
                        rescuedCorpses.Add(corpse);
                    }
                }

                foreach (var corpse in corpses ?? Enumerable.Empty<CorpseType>())
                {
                    if (corpse?.InnerPawn != null && !corpse.Destroyed)
                    {
                        rescuedCorpses.Add(corpse);
                    }
                }

                if (rescuedCorpses.Count == 0)
                {
                    return 0;
                }

                var groundTile = ResolveGroundTile(spaceTile);
                var crashSite = groundTile.Valid ? CreateOrGetCrashSite(groundTile) : null;

                int delivered = 0;
                foreach (var corpse in rescuedCorpses)
                {
                    StripToTorso(corpse.InnerPawn);
                    RefreshPreCalculatedQueue(corpse.InnerPawn);

                    if (corpse.Spawned)
                    {
                        corpse.DeSpawn(DestroyMode.WillReplace);
                    }

                    if (crashSite != null)
                    {
                        crashSite.AddCorpse(corpse);
                        delivered++;
                    }
                    else
                    {
                        SpawnCorpseAtHomeColony(corpse);
                    }
                }

                if (crashSite != null && delivered > 0)
                {
                    Find.LetterStack?.ReceiveLetter(
                        "EternalFellFromSpace".Translate(),
                        "EternalSpaceLossTorsoDesc".Translate(delivered),
                        LetterDefOf.NegativeEvent,
                        crashSite);
                }

                return delivered;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "CrashDownEternals", null, ex);
                return 0;
            }
        }

        /// <summary>
        /// Delivers already-dead Eternal corpses to a ground crash site WITHOUT the
        /// terminal-velocity torso-strip: the corpse is being rescued from a container sweep
        /// (corpse-only pod arrival, silent Vanish-destroy), not falling from space, so its
        /// death-time healing queue stays valid. Each corpse is removed from its current
        /// map/container first — the crash site deep-saves its contents, so leaving the corpse
        /// in another ThingOwner would double-own it and corrupt the save.
        /// </summary>
        /// <param name="corpses">Tracked Eternal corpses to rescue (null-safe)</param>
        /// <param name="spaceTile">Best-known world tile of the loss event</param>
        /// <returns>Number of corpses delivered to the crash site</returns>
        public static int DeliverCorpsesToCrashSite(IEnumerable<CorpseType> corpses, PlanetTile spaceTile)
        {
            try
            {
                var rescuedCorpses = (corpses ?? Enumerable.Empty<CorpseType>())
                    .Where(corpse => corpse?.InnerPawn != null && !corpse.Destroyed)
                    .ToList();

                if (rescuedCorpses.Count == 0)
                {
                    return 0;
                }

                var groundTile = ResolveGroundTile(spaceTile);
                var crashSite = groundTile.Valid ? CreateOrGetCrashSite(groundTile) : null;

                int delivered = 0;
                foreach (var corpse in rescuedCorpses)
                {
                    if (corpse.Spawned)
                    {
                        corpse.DeSpawn(DestroyMode.WillReplace);
                    }
                    else
                    {
                        corpse.holdingOwner?.Remove(corpse);
                    }

                    if (crashSite != null)
                    {
                        crashSite.AddCorpse(corpse);
                        delivered++;
                    }
                    else
                    {
                        SpawnCorpseAtHomeColony(corpse);
                    }
                }

                if (crashSite != null && delivered > 0)
                {
                    Find.LetterStack?.ReceiveLetter(
                        "EternalFellFromSpace".Translate(),
                        "EternalCorpseRescuedDesc".Translate(delivered),
                        LetterDefOf.NegativeEvent,
                        crashSite);
                }

                return delivered;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "DeliverCorpsesToCrashSite", null, ex);
                return 0;
            }
        }

        /// <summary>
        /// Collects the Eternals a destruction event would claim on a map: living player
        /// Eternals (spawned) and all Eternal corpses. An optional cell filter restricts the
        /// sweep to the doomed cells (null = whole map).
        /// </summary>
        public static void CollectEternalsOnMap(MapType map, Func<IntVec3, bool> cellFilter,
            List<Pawn> living, List<CorpseType> corpses)
        {
            if (map == null)
            {
                return;
            }

            foreach (var pawn in map.mapPawns?.AllPawnsSpawned?.ToList() ?? new List<Pawn>())
            {
                if (pawn != null && pawn.IsValidEternal() && pawn.Faction == Faction.OfPlayer &&
                    (cellFilter == null || cellFilter(pawn.Position)))
                {
                    living.Add(pawn);
                }
            }

            foreach (var thing in map.listerThings?.ThingsInGroup(ThingRequestGroup.Corpse)?.ToList() ?? new List<Thing>())
            {
                if (thing is CorpseType corpse && corpse.InnerPawn != null &&
                    corpse.InnerPawn.IsValidEternal() &&
                    (cellFilter == null || cellFilter(corpse.Position)))
                {
                    corpses.Add(corpse);
                }
            }
        }

        /// <summary>
        /// Kills a living Eternal in place so the corpse spawns on the still-existing map and
        /// Eternal_Hediff.Notify_PawnDied auto-registers it (snapshot + pre-calculated queue).
        /// </summary>
        private static CorpseType KillInPlace(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return null;
            }

            try
            {
                if (!pawn.Dead)
                {
                    pawn.Kill(null);
                }

                var corpse = pawn.Corpse;
                if (corpse == null)
                {
                    EternalLogger.Warning($"No corpse produced for {pawn.Name} during space rescue - pawn may be lost",
                        "KillInPlace", pawn);
                }

                return corpse;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "KillInPlace", pawn, ex);
                return null;
            }
        }

        /// <summary>
        /// Strips a dead pawn to a torso-only corpse: neck and everything above (head/skull/brain
        /// cascade via Hediff_MissingPart descendants), all limbs (moving/manipulation limb cores),
        /// and all soft organs by tag. Torso bones (spine, pelvis, ribcage, sternum) remain.
        /// </summary>
        public static void StripToTorso(Pawn deadPawn)
        {
            if (deadPawn?.health?.hediffSet == null)
            {
                return;
            }

            try
            {
                string neckDefName = Constants.CriticalPartConstants.RegrowthSequence[0];
                var hediffSet = deadPawn.health.hediffSet;

                // Snapshot: adding MissingBodyPart mutates the not-missing set (children cascade)
                var candidateParts = hediffSet.GetNotMissingParts().ToList();

                foreach (var part in candidateParts)
                {
                    if (part?.def == null || part == deadPawn.RaceProps.body.corePart)
                    {
                        continue;
                    }

                    bool isNeckChain = part.def.defName == neckDefName;
                    bool isLimbCore = part.def.tags != null &&
                        (part.def.tags.Contains(BodyPartTagDefOf.MovingLimbCore) ||
                         part.def.tags.Contains(BodyPartTagDefOf.ManipulationLimbCore));
                    bool isOrgan = part.def.tags != null &&
                        part.def.tags.Any(tag => StrippedOrganTags.Contains(tag));

                    if (!isNeckChain && !isLimbCore && !isOrgan)
                    {
                        continue;
                    }

                    // Re-check each iteration: an ancestor removed earlier already covers this part,
                    // and AddDirect rejects hediffs on already-missing parts.
                    if (hediffSet.PartIsMissing(part))
                    {
                        continue;
                    }

                    deadPawn.health.AddHediff(HediffDefOf.MissingBodyPart, part);
                }

                Log.Message($"[Eternal] Stripped {deadPawn.Name} to torso after terminal-velocity re-entry");
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "StripToTorso", deadPawn, ex);
            }
        }

        /// <summary>
        /// Recalculates the corpse's healing queue after part-stripping. The queue captured at
        /// death predates the strip, so resurrection would otherwise skip the removed parts.
        /// Updates the existing tracking entry in place to preserve the assignment snapshot.
        /// </summary>
        private static void RefreshPreCalculatedQueue(Pawn deadPawn)
        {
            try
            {
                var corpseManager = EternalServiceContainer.Instance?.CorpseManager;
                var trackingEntry = corpseManager?.GetCorpseData(deadPawn);
                if (trackingEntry == null)
                {
                    // Not registered (e.g. non-player Eternal) - register now so resurrection is possible
                    if (deadPawn.Corpse != null && corpseManager != null)
                    {
                        corpseManager.RegisterCorpse(deadPawn.Corpse, deadPawn, null,
                            new EternalResurrectionCalculator().CalculateHealingQueue(deadPawn));
                    }
                    return;
                }

                trackingEntry.PreCalculatedHealingQueue =
                    new EternalResurrectionCalculator().CalculateHealingQueue(deadPawn);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "RefreshPreCalculatedQueue", deadPawn, ex);
            }
        }

        /// <summary>
        /// Resolves the surface tile beneath a space/orbit/atmosphere tile.
        /// Chain: tile already on a surface layer -> itself; adjacent Surface layer (same planet,
        /// the lookup LAO itself uses); any Surface layer; a fresh site tile near the player.
        /// </summary>
        public static PlanetTile ResolveGroundTile(PlanetTile spaceTile)
        {
            try
            {
                if (spaceTile.Valid && spaceTile.Layer != null && spaceTile.Layer.IsRootSurface)
                {
                    return spaceTile;
                }

                if (spaceTile.Valid &&
                    Find.WorldGrid.TryGetFirstAdjacentLayerOfDef(spaceTile, PlanetLayerDefOf.Surface, out var adjacentSurface))
                {
                    var closest = adjacentSurface.GetClosestTile_NewTemp(spaceTile);
                    if (closest.Valid)
                    {
                        return closest;
                    }
                }

                if (Find.WorldGrid.TryGetFirstLayerOfDef(PlanetLayerDefOf.Surface, out var anySurface))
                {
                    var closest = anySurface.GetClosestTile_NewTemp(spaceTile);
                    if (closest.Valid)
                    {
                        return closest;
                    }
                }

                if (TileFinder.TryFindNewSiteTile(out var fallbackTile))
                {
                    Log.Warning("[Eternal] Could not resolve surface tile below space tile " +
                        $"{spaceTile} - using fallback site tile {fallbackTile}");
                    return fallbackTile;
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "ResolveGroundTile", null, ex);
            }

            return PlanetTile.Invalid;
        }

        /// <summary>
        /// Creates a new crash site or returns the existing one at the tile.
        /// </summary>
        public static WorldObject_EternalCrashSite CreateOrGetCrashSite(PlanetTile worldTile)
        {
            if (!worldTile.Valid)
            {
                Log.Error("[Eternal] Invalid world tile for crash site");
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
        /// Applies severe but survivable fall damage to a LIVING pawn (ground-level crash
        /// scenarios such as VGE landings; space losses use StripToTorso instead).
        /// </summary>
        public static void ApplyFallDamage(Pawn pawn)
        {
            if (pawn?.health == null || pawn.Dead)
            {
                return;
            }

            try
            {
                pawn.TakeDamage(new DamageInfo(DamageDefOf.Blunt, amount: 80f, armorPenetration: 0f,
                    angle: -1f, instigator: null, hitPart: null, weapon: null));

                var limbParts = pawn.health.hediffSet?.GetNotMissingParts()
                    ?.Where(p => p.def?.tags != null && p.def.tags.Contains(BodyPartTagDefOf.MovingLimbCore))
                    .ToList();

                if (limbParts != null)
                {
                    foreach (var part in limbParts)
                    {
                        pawn.TakeDamage(new DamageInfo(DamageDefOf.Blunt, amount: 25f, armorPenetration: 0f,
                            angle: -1f, instigator: null, hitPart: part, weapon: null));
                    }
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "ApplyFallDamage", pawn, ex);
            }
        }

        /// <summary>
        /// Emergency fallback when no crash site could be created: spawn living pawns
        /// (with fall damage) or corpses at the edge of the player's home map.
        /// </summary>
        public static void SpawnAtHomeColony(IEnumerable<Pawn> eternals)
        {
            Log.Warning("[Eternal] Crash site unavailable - attempting emergency recovery at home colony");

            var homeMap = Find.AnyPlayerHomeMap;
            if (homeMap == null)
            {
                Log.Error("[Eternal] No player home map found - Eternals may be lost!");
                return;
            }

            try
            {
                foreach (var pawn in eternals ?? Enumerable.Empty<Pawn>())
                {
                    if (pawn == null || pawn.Destroyed)
                    {
                        continue;
                    }

                    var entryCell = CellFinder.RandomEdgeCell(homeMap);
                    if (!entryCell.IsValid)
                    {
                        continue;
                    }

                    Thing toSpawn = pawn.Dead ? (Thing)pawn.Corpse : pawn;
                    if (toSpawn != null && !toSpawn.Spawned)
                    {
                        GenSpawn.Spawn(toSpawn, entryCell, homeMap);
                    }

                    ApplyFallDamage(pawn);
                }

                Find.LetterStack?.ReceiveLetter(
                    "EternalFellFromSpace".Translate(),
                    "EternalEmergencyRecovery".Translate(),
                    LetterDefOf.NegativeEvent);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "SpawnAtHomeColony", null, ex);
            }
        }

        /// <summary>
        /// Last-resort corpse delivery when no crash site could be created.
        /// </summary>
        private static void SpawnCorpseAtHomeColony(CorpseType corpse)
        {
            var homeMap = Find.AnyPlayerHomeMap;
            if (homeMap == null || corpse == null || corpse.Destroyed)
            {
                Log.Error("[Eternal] No crash site and no home map - Eternal corpse may be lost");
                return;
            }

            var entryCell = CellFinder.RandomEdgeCell(homeMap);
            if (entryCell.IsValid && !corpse.Spawned)
            {
                GenSpawn.Spawn(corpse, entryCell, homeMap);
                EternalServiceContainer.Instance?.CorpseManager?.UpdateCorpseLocation(
                    corpse.InnerPawn, homeMap, entryCell);
            }
        }
    }
}
