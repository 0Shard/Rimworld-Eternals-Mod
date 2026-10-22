/*
 * Relative Path: Eternal/Source/Eternal/World/WorldObject_EternalCrashSite.cs
 * Creation Date: 06-12-2025
 * Last Edit: 16-07-2026
 * Author: 0Shard
 * Description: World object representing a crash site where an Eternal fell from space.
 *              Spawns tracked corpses through the manager-owned relocation boundary.
 */

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

        internal bool HasRecoveryContents => crashedThings.Count != 0;
        internal bool HasPendingPawn(Pawn pawn) => crashedThings.Contains(pawn);

        internal bool TryRetainUnownedPawn(Pawn pawn)
        {
            if (!Spawned || Destroyed || pawn == null || pawn.Dead || pawn.Destroyed
                || pawn.Spawned || pawn.holdingOwner != null) { return false; }
            if (Find.WorldObjects.AllWorldObjects.OfType<WorldObject_EternalCrashSite>()
                .Any(site => site != this && site.HasPendingPawn(pawn))) { return false; }
            if (pawn.IsWorldPawn()) { Find.WorldPawns.RemovePawn(pawn); }
            if (!crashedThings.Contains(pawn)) { crashedThings.Add(pawn); }
            return true;
        }

        internal bool HasPendingCorpse(CorpseType corpse) => corpse != null && crashedThings.Contains(corpse);

        internal bool ContainsRecoveryCorpse(CorpseType corpse) => HasPendingCorpse(corpse)
            || (corpse != null && !corpse.Destroyed && corpse.Spawned && HasMap && corpse.Map == Map);

        internal IEnumerable<CorpseType> PendingCorpsesForRecovery()
            => crashedThings.OfType<CorpseType>().Where(corpse => !corpse.Destroyed).ToList();

        internal void ForgetPendingCorpse(CorpseType corpse)
            => crashedThings.RemoveAll(thing => ReferenceEquals(thing, corpse));

        /// <summary>
        /// Adds a living pawn to the crash site (before map generation).
        /// </summary>
        public void AddPawn(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed)
            {
                Log.Warning("[Eternal] Attempted to add null pawn to crash site");
                return;
            }

            if (TryRetainUnownedPawn(pawn))
            {
                Log.Message($"[Eternal] Added {pawn.Name} to crash site");
                DeliverPendingContents();
            }
        }

        /// <summary>
        /// Adds a corpse to the crash site (before map generation). The corpse must be
        /// despawned; the crash site becomes its holder until the map is generated.
        /// </summary>
        public void AddCorpse(CorpseType corpse)
        {
            if (!TryRetainUnownedCorpse(corpse))
            {
                Log.Warning("[Eternal] Could not stage a corpse at the recovery site");
            }
        }

        /// <summary>
        /// Stages a newly created solo-caravan corpse even if an external AddCorpse hook
        /// fails. Uses the existing deep-save list and never nests a corpse in its own pawn.
        /// </summary>
        internal bool TryStageCorpseForRecovery(CorpseType corpse)
        {
            if (Destroyed || corpse?.InnerPawn == null || corpse.Destroyed)
            {
                return false;
            }
            if (corpse.Spawned)
            {
                corpse.DeSpawn(DestroyMode.WillReplace);
            }
            corpse.holdingOwner?.Remove(corpse);
            if (corpse.Spawned || corpse.holdingOwner != null)
            {
                return false;
            }
            return AppendRecoveryCorpse(corpse);
        }

        /// <summary>
        /// Last-owner death fallback after transfer hooks reject staging. Only adopts a fully
        /// detached corpse; it cannot remove another physical or persistent owner.
        /// </summary>
        internal bool TryRetainUnownedCorpse(CorpseType corpse)
        {
            if (!Spawned || Destroyed || corpse?.InnerPawn == null || corpse.Destroyed
                || corpse.Spawned || corpse.holdingOwner != null)
            {
                return false;
            }
            if (Find.WorldObjects.AllWorldObjects.OfType<WorldObject_EternalCrashSite>()
                .Any(site => !ReferenceEquals(site, this) && site.HasPendingCorpse(corpse)))
            {
                return false;
            }
            return AppendRecoveryCorpse(corpse);
        }

        private bool AppendRecoveryCorpse(CorpseType corpse)
        {
            if (!crashedThings.Contains(corpse))
            {
                crashedThings.Add(corpse);
                Log.Message($"[Eternal] Added corpse of {corpse.InnerPawn.Name} to crash site");
            }
            return true;
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

            if (HasRecoveryContents)
            {
                return false;
            }

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
                DeliverPendingContents();

                Log.Message($"[Eternal] Crash site map generated at tile {Tile}");
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "CrashSite.PostMapGenerate", null, ex);
            }
        }

        internal void DeliverPendingContents()
        {
            if (!HasMap) { return; }
            foreach (var thing in crashedThings.ToList())
            {
                if (SpawnCrashedThingOnMap(thing, Map)) { crashedThings.Remove(thing); }
            }
        }

        internal bool CanRecover(CaravanType caravan, PlanetTile destination)
            => Spawned && !Destroyed && caravan != null && caravan.Spawned
                && caravan.Faction == Faction.OfPlayer && Tile == destination
                && (HasMap || HasRecoveryContents) && !this.EnterCooldownBlocksEntering();

        internal void EnterRecoveryMap(CaravanType caravan = null)
        {
            if (caravan != null && (!CanRecover(caravan, caravan.Tile) || caravan.Tile != Tile)) { return; }
            if (!Spawned || Destroyed || Faction != Faction.OfPlayer) { return; }
            LongEventHandler.QueueLongEvent(() =>
            {
                if (!Spawned || Destroyed || (caravan != null
                    && (!CanRecover(caravan, caravan.Tile) || caravan.Tile != Tile))) { return; }
                var map = GenerateRecoveryMap();
                if (map == null || Map != map) { return; }
                if (caravan != null)
                {
                    CaravanEnterMapUtility.Enter(caravan, map, CaravanEnterMode.Edge,
                        CaravanDropInventoryMode.DoNotDrop, false);
                }
                Current.Game.CurrentMap = map;
                CameraJumper.TryJump(map.Center, map);
            }, "GeneratingMapForNewEncounter", false, null);
        }

        internal MapType GenerateRecoveryMap()
        {
            if (!Spawned || Destroyed || Faction != Faction.OfPlayer) { return null; }
            var map = HasMap ? Map : MapGenerator.GenerateMap(Find.World.info.initialMapSize, this,
                MapGeneratorDef, ExtraGenStepDefs);
            if (map != null && Map == map) { DeliverPendingContents(); }
            return map;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var gizmo in base.GetGizmos()) { yield return gizmo; }
            if (Faction == Faction.OfPlayer && (HasMap || GetCrashedPawns().Any(p => p.Faction == Faction.OfPlayer)))
            {
                yield return new Command_Action
                {
                    defaultLabel = HasMap ? "View recovery site" : "Enter recovery site",
                    defaultDesc = "Generate or view the recovery map and take control of the stranded Eternals.",
                    action = () => EnterRecoveryMap()
                };
            }
        }

        /// <summary>
        /// Spawns a crashed Eternal (pawn or corpse) on the crash site map.
        /// </summary>
        private bool SpawnCrashedThingOnMap(Thing thing, MapType map)
        {
            if (thing == null || map == null)
            {
                return false;
            }

            try
            {
                IntVec3 spawnPos;
                if (!CellFinder.TryFindRandomCellNear(map.Center, map, 15,
                    c => c.Standable(map) && !c.Fogged(map), out spawnPos))
                {
                    spawnPos = CellFinder.RandomClosewalkCellNear(map.Center, map, 20, null);
                }

                var corpseManager = EternalServiceContainer.Instance?.CorpseManager;
                if (thing is CorpseType corpse
                    && corpseManager != null
                    && corpseManager.IsTracked(corpse.InnerPawn))
                {
                    if (!corpseManager.TryRelocateCorpse(corpse.InnerPawn, map, spawnPos))
                    {
                        return false;
                    }
                }
                else
                {
                    GenSpawn.Spawn(thing, spawnPos, map, WipeMode.Vanish);
                }

                if (!thing.Spawned || thing.Map != map)
                {
                    return false;
                }

                if (thing is Pawn eternal && eternal.Faction == Faction.OfPlayer)
                {
                    // Ensure they're controllable (undraft if drafted, clear stale jobs)
                    if (eternal.drafter != null)
                    {
                        eternal.drafter.Drafted = false;
                    }
                    eternal.jobs?.EndCurrentJob(Verse.AI.JobCondition.InterruptForced, true);
                }
                Log.Message($"[Eternal] Spawned {thing.LabelCap} at crash site position {spawnPos}");
                return true;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "SpawnCrashedThingOnMap", thing as Pawn, ex);
                return thing.Spawned && thing.Map == map;
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

            foreach (var option in CaravanArrivalActionUtility.GetFloatMenuOptions(
                () => CanRecover(caravan, Tile), () => new CaravanArrivalAction_EternalCrashSite(this, false),
                "EnterMap".Translate(Label), caravan, Tile, this))
            { yield return option; }
            if (crashedThings.Count > 0 && !HasMap)
            {
                foreach (var option in CaravanArrivalActionUtility.GetFloatMenuOptions(
                    () => CanRecover(caravan, Tile), () => new CaravanArrivalAction_EternalCrashSite(this, true),
                    "RescueEternalsWithCaravan".Translate(), caravan, Tile, this))
                { yield return option; }
            }
        }

        /// <summary>
        /// Rescues all crashed Eternals directly to a caravan without generating a map.
        /// Living pawns join the caravan; corpses go into caravan inventory.
        /// </summary>
        internal void RescueEternalsToCaravan(CaravanType caravan)
        {
            if (!CanRecover(caravan, caravan?.Tile ?? PlanetTile.Invalid) || caravan.Tile != Tile || HasMap)
            {
                return;
            }

            try
            {
                int rescued = 0;
                foreach (var thing in crashedThings.ToList())
                {
                    // The deep list owns only detached contents. A stale entry cannot take
                    // something that native transport or another container now physically owns.
                    if (thing == null || thing.Destroyed || thing.Spawned || thing.holdingOwner != null) { continue; }
                    var originalOwner = thing?.holdingOwner;
                    bool accepted = false;
                    try
                    {
                        if (thing is Pawn eternal && !eternal.Dead && !eternal.Destroyed)
                        {
                            caravan.AddPawn(eternal, true);
                            if (caravan.ContainsPawn(eternal) && !eternal.IsWorldPawn())
                            { Find.WorldPawns.PassToWorld(eternal); }
                            accepted = caravan.ContainsPawn(eternal) && eternal.IsWorldPawn();
                        }
                        else if (thing is CorpseType corpse)
                        {
                            bool Transfer()
                            {
                                CaravanInventoryUtility.GiveThing(caravan, corpse);
                                return !corpse.Destroyed && caravan.AllThings.Contains(corpse);
                            }
                            var corpseManager = EternalServiceContainer.Instance?.CorpseManager;
                            accepted = corpseManager != null && corpseManager.IsTracked(corpse.InnerPawn)
                                ? corpseManager.TryReleaseCorpseToUnspawnedOwner(corpse.InnerPawn, Transfer)
                                : Transfer();
                        }
                        if (accepted)
                        {
                            crashedThings.Remove(thing);
                            rescued++;
                        }
                    }
                    catch (Exception ex)
                    {
                        EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                            "RescueEternalsToCaravan.Transfer", thing as Pawn, ex);
                    }
                    finally
                    {
                        // The source is this deep-save list, not a ThingOwner. A late native
                        // inventory acceptance exception must not leave both owners populated.
                        if (!accepted && thing != null && thing.holdingOwner != originalOwner
                            && caravan.AllThings.Contains(thing))
                        {
                            thing.holdingOwner?.Remove(thing);
                        }
                        if (!accepted && thing is Pawn untransferred && untransferred.IsWorldPawn())
                        { Find.WorldPawns.RemovePawn(untransferred); }
                    }
                }

                if (!HasRecoveryContents)
                {
                    Find.WorldObjects.Remove(this);
                }

                if (rescued > 0)
                { Messages.Message("EternalsRescued".Translate(), caravan, MessageTypeDefOf.PositiveEvent, true); }
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
