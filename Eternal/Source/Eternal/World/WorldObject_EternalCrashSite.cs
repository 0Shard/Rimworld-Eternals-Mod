// file path: Eternal/Source/Eternal/World/WorldObject_EternalCrashSite.cs
// Author Name: 0Shard
// Date Created: 06-12-2025
// Date Last Modified: 13-07-2026
// Description: World object representing a crash site where an Eternal fell from space.
//              Holds living pawns AND corpses (torso-only re-entry victims). Entering the site
//              generates a map (vanilla Encounter generator via MapParent.MapGeneratorDef default)
//              and PostMapGenerate spawns the crashed Eternals. Supports caravan rescue.

using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Utils;

// Type aliases to resolve namespace shadowing (Eternal.Map and Eternal.Caravan shadow Verse/RimWorld types)
using MapType = Verse.Map;
using CaravanType = RimWorld.Planet.Caravan;
using CorpseType = Verse.Corpse;

namespace Eternal.World
{
    /// <summary>
    /// World object representing a crash site where an Eternal fell from space.
    /// Holds living pawns and corpses until the player enters (map generation spawns them)
    /// or rescues them with a caravan.
    /// </summary>
    public class WorldObject_EternalCrashSite : MapParent
    {
        /// <summary>
        /// Crashed Eternals waiting to be spawned: living Pawns or Corpses.
        /// Deep-owned here while unspawned so they survive save/load.
        /// </summary>
        private List<Thing> crashedThings = new List<Thing>();

        /// <summary>
        /// Legacy storage from saves created before corpse support (pawns only).
        /// Merged into crashedThings on load.
        /// </summary>
        private List<Pawn> crashedEternals;

        /// <summary>
        /// Adds a living pawn to the crash site (before map generation).
        /// </summary>
        public void AddPawn(Pawn pawn)
        {
            if (pawn == null)
            {
                Log.Warning("[Eternal] Attempted to add null pawn to crash site");
                return;
            }

            if (!crashedThings.Contains(pawn))
            {
                crashedThings.Add(pawn);
                Log.Message($"[Eternal] Added {pawn.Name} to crash site");
            }
        }

        /// <summary>
        /// Adds a corpse to the crash site (before map generation). The corpse must be
        /// despawned; the crash site becomes its holder until the map is generated.
        /// </summary>
        public void AddCorpse(CorpseType corpse)
        {
            if (corpse?.InnerPawn == null)
            {
                Log.Warning("[Eternal] Attempted to add null corpse to crash site");
                return;
            }

            if (!crashedThings.Contains(corpse))
            {
                crashedThings.Add(corpse);
                Log.Message($"[Eternal] Added corpse of {corpse.InnerPawn.Name} to crash site");
            }
        }

        /// <summary>
        /// Gets all pawns at this crash site (waiting or spawned on the generated map).
        /// Corpses yield their inner pawn.
        /// </summary>
        public IEnumerable<Pawn> GetCrashedPawns()
        {
            foreach (var thing in crashedThings)
            {
                if (thing is Pawn pawn)
                {
                    yield return pawn;
                }
                else if (thing is CorpseType corpse && corpse.InnerPawn != null)
                {
                    yield return corpse.InnerPawn;
                }
            }

            if (HasMap)
            {
                foreach (var pawn in Map.mapPawns.AllPawns.Where(p => p.Faction == Faction.OfPlayer))
                {
                    yield return pawn;
                }
            }
        }

        /// <summary>
        /// Determines when the map should be removed.
        /// </summary>
        public override bool ShouldRemoveMapNow(out bool alsoRemoveWorldObject)
        {
            alsoRemoveWorldObject = false;

            if (!HasMap)
            {
                // No map, check if we still have things waiting
                if (crashedThings.Count == 0)
                {
                    alsoRemoveWorldObject = true;
                    return false;
                }
                return false;
            }

            // Check if any player pawns (or tracked Eternal corpses, via the
            // MapPawns_AnyPawnBlockingMapRemoval postfix) remain on map
            bool hasPlayerPawns = Map.mapPawns.AnyPawnBlockingMapRemoval;

            if (!hasPlayerPawns)
            {
                alsoRemoveWorldObject = true;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Called by MapGenerator after the site map is generated (player entered the site).
        /// Spawns all waiting crashed Eternals near the map center.
        /// </summary>
        public override void PostMapGenerate()
        {
            base.PostMapGenerate();

            try
            {
                foreach (var thing in crashedThings.ToList())
                {
                    SpawnCrashedThingOnMap(thing, Map);
                }

                crashedThings.Clear();

                Log.Message($"[Eternal] Crash site map generated at tile {Tile}");
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "CrashSite.PostMapGenerate", null, ex);
            }
        }

        /// <summary>
        /// Spawns a crashed Eternal (pawn or corpse) on the crash site map.
        /// </summary>
        private void SpawnCrashedThingOnMap(Thing thing, MapType map)
        {
            if (thing == null || map == null)
            {
                return;
            }

            try
            {
                IntVec3 spawnPos;
                if (!CellFinder.TryFindRandomCellNear(map.Center, map, 15,
                    c => c.Standable(map) && !c.Fogged(map), out spawnPos))
                {
                    spawnPos = CellFinder.RandomClosewalkCellNear(map.Center, map, 20, null);
                }

                GenSpawn.Spawn(thing, spawnPos, map, WipeMode.Vanish);

                if (thing is Pawn eternal && eternal.Faction == Faction.OfPlayer)
                {
                    // Ensure they're controllable (undraft if drafted, clear stale jobs)
                    if (eternal.drafter != null)
                    {
                        eternal.drafter.Drafted = false;
                    }
                    eternal.jobs?.EndCurrentJob(Verse.AI.JobCondition.InterruptForced, true);
                }
                else if (thing is CorpseType corpse)
                {
                    // Re-home the tracking entry so corpse healing/preservation see the new map
                    EternalServiceContainer.Instance?.CorpseManager?.UpdateCorpseLocation(
                        corpse.InnerPawn, map, spawnPos);
                }

                Log.Message($"[Eternal] Spawned {thing.LabelCap} at crash site position {spawnPos}");
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "SpawnCrashedThingOnMap", thing as Pawn, ex);
            }
        }

        /// <summary>
        /// Gets float menu options when a caravan right-clicks this site.
        /// </summary>
        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(CaravanType caravan)
        {
            foreach (var option in base.GetFloatMenuOptions(caravan))
            {
                yield return option;
            }

            // Option to rescue without entering (just pick them up)
            if (crashedThings.Count > 0 && !HasMap)
            {
                yield return new FloatMenuOption(
                    "RescueEternalsWithCaravan".Translate(),
                    () => RescueEternalsToCaravan(caravan));
            }
        }

        /// <summary>
        /// Rescues all crashed Eternals directly to a caravan without generating a map.
        /// Living pawns join the caravan; corpses go into caravan inventory.
        /// </summary>
        private void RescueEternalsToCaravan(CaravanType caravan)
        {
            if (caravan == null)
            {
                return;
            }

            try
            {
                foreach (var thing in crashedThings.ToList())
                {
                    if (thing is Pawn eternal)
                    {
                        caravan.AddPawn(eternal, true);
                        Log.Message($"[Eternal] Rescued {eternal.Name} to caravan");
                    }
                    else if (thing is CorpseType corpse)
                    {
                        CaravanInventoryUtility.GiveThing(caravan, corpse);
                        Log.Message($"[Eternal] Rescued corpse of {corpse.InnerPawn?.Name} to caravan inventory");
                    }
                }

                crashedThings.Clear();

                // Remove the world object since we're done
                Find.WorldObjects.Remove(this);

                Messages.Message("EternalsRescued".Translate(), caravan, MessageTypeDefOf.PositiveEvent, true);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "RescueEternalsToCaravan", null, ex);
            }
        }

        /// <summary>
        /// Gets the inspect string for this world object.
        /// </summary>
        public override string GetInspectString()
        {
            string text = base.GetInspectString();

            if (!HasMap && crashedThings.Count > 0)
            {
                if (!string.IsNullOrEmpty(text))
                {
                    text += "\n";
                }

                text += "EternalsCrashed".Translate(crashedThings.Count);
                text += "\n" + "ClickToEnterCrashSite".Translate();
            }

            return text;
        }

        /// <summary>
        /// Save/load data.
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Collections.Look(ref crashedThings, "crashedThings", LookMode.Deep);
            // Legacy list from saves created before corpse support
            Scribe_Collections.Look(ref crashedEternals, "crashedEternals", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (crashedThings == null)
                {
                    crashedThings = new List<Thing>();
                }

                if (crashedEternals != null)
                {
                    foreach (var legacyPawn in crashedEternals.Where(p => p != null && !crashedThings.Contains(p)))
                    {
                        crashedThings.Add(legacyPawn);
                    }
                    crashedEternals = null;
                }
            }
        }
    }
}
