// file path: Eternal/Source/Eternal/World/WorldObject_EternalCrashSite.cs
// Author Name: 0Shard
// Date Created: 06-12-2025
// Date Last Modified: 20-02-2026
// Description: World object representing a crash site where an Eternal fell from space.
//              This is a playable map where the player can control the Eternal directly.
//              Supports self-rescue, caravan rescue, or direct map control.

using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Eternal.Exceptions;
using Eternal.Utils;

// Type aliases to resolve namespace shadowing (Eternal.Map and Eternal.Caravan shadow Verse/RimWorld types)
using MapType = Verse.Map;
using CaravanType = RimWorld.Planet.Caravan;

namespace Eternal.World
{
    /// <summary>
    /// World object representing a crash site where an Eternal fell from space.
    /// This is an actual playable map where the player controls the Eternal.
    /// </summary>
    public class WorldObject_EternalCrashSite : MapParent
    {
        /// <summary>
        /// Eternals waiting to be spawned when map is generated.
        /// </summary>
        private List<Pawn> crashedEternals = new List<Pawn>();

        /// <summary>
        /// Whether the map has been generated yet.
        /// </summary>
        private bool mapGenerated = false;

        /// <summary>
        /// Adds a pawn to the crash site (before map generation).
        /// </summary>
        public void AddPawn(Pawn pawn)
        {
            if (pawn == null)
            {
                Log.Warning("[Eternal] Attempted to add null pawn to crash site");
                return;
            }

            if (!crashedEternals.Contains(pawn))
            {
                crashedEternals.Add(pawn);
                Log.Message($"[Eternal] Added {pawn.Name} to crash site");
            }
        }

        /// <summary>
        /// Gets all pawns at this crash site (spawned on map or waiting).
        /// </summary>
        public IEnumerable<Pawn> GetCrashedPawns()
        {
            // Return pawns waiting at crash site (not yet on map)
            foreach (var pawn in crashedEternals)
            {
                yield return pawn;
            }

            // Also return any spawned pawns if map exists
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
                // No map, check if we still have pawns waiting
                if (crashedEternals.Count == 0)
                {
                    alsoRemoveWorldObject = true;
                    return false;
                }
                return false;
            }

            // Check if any player pawns remain on map
            bool hasPlayerPawns = Map.mapPawns.AnyPawnBlockingMapRemoval;

            if (!hasPlayerPawns)
            {
                alsoRemoveWorldObject = true;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Generate the crash site map when first entering.
        /// </summary>
        private MapType GenerateCrashSiteMap()
        {
            try
            {
                // Generate small map (crash site) - 75x75
                IntVec3 mapSize = new IntVec3(75, 1, 75);

                MapType map = MapGenerator.GenerateMap(
                    mapSize,
                    this,
                    MapGeneratorDefOf.Base_Player);

                if (map == null)
                {
                    Log.Error("[Eternal] Failed to generate crash site map");
                    return null;
                }

                // Spawn crashed Eternals on the map
                foreach (var eternal in crashedEternals)
                {
                    SpawnEternalOnMap(eternal, map);
                }

                crashedEternals.Clear();
                mapGenerated = true;

                Log.Message($"[Eternal] Generated crash site map at tile {Tile}");

                return map;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "GenerateCrashSiteMap", null, ex);
                return null;
            }
        }

        /// <summary>
        /// Spawns an Eternal pawn on the crash site map.
        /// </summary>
        private void SpawnEternalOnMap(Pawn eternal, MapType map)
        {
            if (eternal == null || map == null)
            {
                return;
            }

            try
            {
                // Find valid spawn location near center
                IntVec3 spawnPos;
                if (!CellFinder.TryFindRandomCellNear(map.Center, map, 15,
                    c => c.Standable(map) && !c.Fogged(map), out spawnPos))
                {
                    // Fallback to any walkable cell
                    spawnPos = CellFinder.RandomClosewalkCellNear(map.Center, map, 20, null);
                }

                GenSpawn.Spawn(eternal, spawnPos, map, WipeMode.Vanish);

                // Ensure they're controllable (undraft if drafted, clear jobs)
                if (eternal.Faction == Faction.OfPlayer)
                {
                    if (eternal.drafter != null)
                    {
                        eternal.drafter.Drafted = false;
                    }

                    // Clear any stale jobs
                    eternal.jobs?.EndCurrentJob(Verse.AI.JobCondition.InterruptForced, true);
                }

                Log.Message($"[Eternal] Spawned {eternal.Name} at crash site map position {spawnPos}");
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "SpawnEternalOnMap", eternal, ex);
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
            if (crashedEternals.Count > 0 && !mapGenerated)
            {
                yield return new FloatMenuOption(
                    "RescueEternalsWithCaravan".Translate(),
                    () => RescueEternalsToCaravan(caravan));
            }
        }

        /// <summary>
        /// Rescues all crashed Eternals directly to a caravan without generating a map.
        /// </summary>
        private void RescueEternalsToCaravan(CaravanType caravan)
        {
            if (caravan == null)
            {
                return;
            }

            try
            {
                foreach (var eternal in crashedEternals.ToList())
                {
                    caravan.AddPawn(eternal, true);
                    Log.Message($"[Eternal] Rescued {eternal.Name} to caravan");
                }

                crashedEternals.Clear();

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

            if (!mapGenerated && crashedEternals.Count > 0)
            {
                if (!string.IsNullOrEmpty(text))
                {
                    text += "\n";
                }

                text += "EternalsCrashed".Translate(crashedEternals.Count);
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

            Scribe_Collections.Look(ref crashedEternals, "crashedEternals", LookMode.Deep);
            Scribe_Values.Look(ref mapGenerated, "mapGenerated", false);

            // Ensure list is not null after loading
            if (crashedEternals == null)
            {
                crashedEternals = new List<Pawn>();
            }
        }
    }
}
