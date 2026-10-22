// Author: 0Shard
// Description: Native isolated ground maps, scoped population gates, and ownership-only cleanup.
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Eternal.DI;
using Eternal.Extensions;
using Eternal.InGameTests.Helpers;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using MapType = Verse.Map;
using CorpseType = Verse.Corpse;

namespace Eternal.InGameTests.Performance
{
    public sealed class TestMapScope : IDisposable
    {
        private static readonly Dictionary<MapType, bool> OwnedHomes = new Dictionary<MapType, bool>();
        private readonly List<MapType> maps = new List<MapType>();
        private readonly List<MapParent> parents = new List<MapParent>();
        private readonly List<Pawn> pawns = new List<Pawn>();
        private readonly Dictionary<MapType, Pawn> ordinarySentinels = new Dictionary<MapType, Pawn>();
        private readonly HashSet<WorldObject> originalObjects;
        private readonly HashSet<MapType> originalMaps;
        private readonly MapType originalCurrentMap;
        private readonly Harmony harmony;
        private bool disposed;
        public IReadOnlyList<MapType> Maps => maps;
        public IReadOnlyList<Pawn> Pawns => pawns;

        public TestMapScope()
        {
            TestAssert.Available(Current.Game, "Isolated fixture requires a loaded disposable test game");
            originalCurrentMap = Current.Game.CurrentMap;
            originalObjects = new HashSet<WorldObject>(Find.WorldObjects.AllWorldObjects);
            originalMaps = new HashSet<MapType>(Find.Maps);
            harmony = new Harmony("Eternal.Debug.IsolatedMaps." + Guid.NewGuid().ToString("N"));
            try
            {
                Patch(typeof(MapType), "get_IsPlayerHome", nameof(HomePrefix));
                Patch(typeof(Scenario), "PostMapGenerate", nameof(ScenarioPrefix));
                Patch(typeof(WildAnimalSpawner), "WildAnimalSpawnerTick", nameof(WildlifePrefix));
                Patch(typeof(Storyteller), "TryFire", nameof(IncidentPrefix));
            }
            catch { harmony.UnpatchAll(harmony.Id); throw; }
        }

        private void Patch(Type type, string method, string prefix)
        {
            var target = AccessTools.Method(type, method);
            TestAssert.IsNotNull(target, "Native fixture gate " + type.Name + "." + method);
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(TestMapScope), prefix));
        }

        public MapType Create(int size, bool home = true, bool temporary = false, Action<string, MapType> preparationProgress = null)
        {
            var def = temporary ? WorldObjectDefOf.Camp : WorldObjectDefOf.Settlement;
            preparationProgress?.Invoke("parent-construction-before", null);
            var parent = (MapParent)WorldObjectMaker.MakeWorldObject(def);
            preparationProgress?.Invoke("parent-construction-after", null);
            int selectedTile = FindUnusedSurfaceTile(preparationProgress: preparationProgress);
            preparationProgress?.Invoke("parent-config-before", null);
            parent.Tile = selectedTile;
            parent.SetFaction(Faction.OfPlayer);
            preparationProgress?.Invoke("parent-config-after", null);
            return CreateWithParent(size, parent, home, preparationProgress);
        }

        // Caller supplies a correctly initialized parent (for example SiteMaker.MakeSite).
        // The scope takes ownership before native map generation can fail.
        public MapType CreateWithParent(int size, MapParent parent, bool home, Action<string, MapType> preparationProgress = null)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(TestMapScope));
            }

            preparationProgress?.Invoke("parent-validation-before", null);
            TestAssert.IsTrue(size >= 25 && size <= 500, "Fixture size must be 25..500");
            TestAssert.IsTrue(Find.Maps.Count < 126, "Native sbyte map index requires fewer than 127 maps including fixtures");
            TestAssert.IsNotNull(parent, "Fixture parent");
            TestAssert.IsFalse(parent.HasMap, "Fixture parent must not already own a map");
            TestAssert.IsFalse(originalObjects.Contains(parent), "Cannot take ownership of an existing world object");
            TestAssert.IsTrue(parent.Tile.Valid && parent.Tile.Layer.IsRootSurface, "Fixture parent requires a valid surface tile");
            var tile = Find.WorldGrid[(int)parent.Tile];
            TestAssert.IsTrue(tile.PrimaryBiome != null && tile.PrimaryBiome.implemented && !tile.PrimaryBiome.impassable
                && tile.PrimaryBiome.extraGenSteps.Count == 0 && tile.Mutators.Count == 0,
                "Fixture tile must have an implemented biome without extra generation steps or mutators");
            TestAssert.IsFalse(Find.WorldObjects.AllWorldObjects.Any(o => !ReferenceEquals(o, parent) && o.Tile == parent.Tile),
                "Fixture tile must be distinct from every existing world object");
            preparationProgress?.Invoke("parent-validation-after", null);
            preparationProgress?.Invoke("parent-registration-before", null);
            parents.Add(parent);
            if (!Find.WorldObjects.AllWorldObjects.Contains(parent))
            {
                Find.WorldObjects.Add(parent);
            }
            preparationProgress?.Invoke("parent-registration-after", null);

            var generator = DefDatabase<MapGeneratorDef>.GetNamedSilentFail("Eternal_Debug_EmptyMap") ?? new MapGeneratorDef
            {
                defName = "Eternal_Debug_EmptyMap",
                genSteps = new List<GenStepDef>()
            };
            // Native construction/finalization builds path grids, regions, and all map components.
            // Empty steps skip scenario pawns, wildlife, terrain extras, and structures.
            preparationProgress?.Invoke("native-generation-before", null);
            var map = MapGenerator.GenerateMap(new IntVec3(size, 1, size), parent, generator,
                new GenStepWithParams[0], generated =>
                {
                    preparationProgress?.Invoke("native-init-before", generated);
                    OwnMap(generated, home);
                    preparationProgress?.Invoke("native-owned", generated);
                    preparationProgress?.Invoke("native-terrain-before", generated);
                    foreach (var cell in generated.AllCells)
                    {
                        generated.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
                    }

                    preparationProgress?.Invoke("native-terrain-after", generated);
                    MapGenerator.PlayerStartSpot = generated.Center;
                    preparationProgress?.Invoke("native-init-after", generated);
                }, false, false);
            preparationProgress?.Invoke("native-generation-return", map);
            preparationProgress?.Invoke("map-validation-before", map);
            TestAssert.IsTrue(map.Center.Walkable(map), "Native fixture center must be walkable");
            TestAssert.AreEqual(0, map.mapPawns.AllPawnsSpawned.Count, "Generated fixture must contain no pawns");
            preparationProgress?.Invoke("map-validation-after", map);
            return map;
        }

        internal void OwnLoadedPersistenceMap(MapType map, Pawn pawn, int expectedMapId, int expectedPawnId)
        {
            string mode;
            TestAssert.IsTrue(GenCommandLine.TryGetCommandLineArg("eternal-test-persistence", out mode)
                && mode == "verify", "Loaded ownership requires the isolated persistence host");
            TestAssert.IsTrue(map != null && pawn != null && map.uniqueID == expectedMapId && pawn.thingIDNumber == expectedPawnId,
                "Loaded ownership must match explicit checkpoint map and pawn identities");
            TestAssert.IsTrue(map.Parent is Settlement && map.Parent.Faction == Faction.OfPlayer && map.IsPlayerHome,
                "Loaded persistence fixture requires its native player Settlement parent");
            originalMaps.Remove(map);
            originalObjects.Remove(map.Parent);
            if (!parents.Contains(map.Parent))
            {
                parents.Add(map.Parent);
            }

            OwnMap(map, true);
            OwnPawn(pawn);
        }

        public MapType CreateOrdinaryControlMap(int size, Action<string, MapType> preparationProgress = null)
        {
            preparationProgress?.Invoke("parent-construction-before", null);
            var parent = (MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
            preparationProgress?.Invoke("parent-construction-after", null);
            // Fixed fixture input bounds select a naturally mild climate; no game temperature is overridden.
            int selectedTile = FindUnusedSurfaceTile(tile =>
            {
                float temperature = GenTemperature.GetTemperatureFromSeasonAtTile(GenTicks.TicksAbs, tile);
                return temperature >= 15f && temperature <= 25f && GenTemperature.SeasonalShiftAmplitudeAt(tile) <= 10f;
            }, preparationProgress);
            preparationProgress?.Invoke("parent-config-before", null);
            parent.Tile = selectedTile;
            parent.SetFaction(Faction.OfPlayer);
            preparationProgress?.Invoke("parent-config-after", null);
            return CreateWithParent(size, parent, true, preparationProgress);
        }

        public int FindUnusedSurfaceTile(Func<int, bool> tileCondition = null, Action<string, MapType> preparationProgress = null)
        {
            preparationProgress?.Invoke("tile-index-before", null);
            var occupied = new HashSet<int>(Find.WorldObjects.AllWorldObjects.Select(x => (int)x.Tile));
            preparationProgress?.Invoke("tile-index-after", null);
            preparationProgress?.Invoke("tile-scan-before", null);
            for (int index = 0; index < Find.WorldGrid.TilesCount; index++)
            {
                var tile = Find.WorldGrid[index];
                if (!occupied.Contains(index) && tile.PrimaryBiome != null && tile.PrimaryBiome.implemented
                    && !tile.PrimaryBiome.impassable && tile.PrimaryBiome.extraGenSteps.Count == 0
                    && tile.Mutators.Count == 0 && (tileCondition == null || tileCondition(index)))
                {
                    preparationProgress?.Invoke("tile-scan-after", null);
                    return index;
                }
            }
            throw new TestUnavailableException("No unused implemented surface tile without biome steps or mutators");
        }

        public void OwnMap(MapType map) { OwnMap(map, map.IsPlayerHome); }
        private void OwnMap(MapType map, bool home)
        {
            TestAssert.IsFalse(originalMaps.Contains(map), "Cannot take ownership of a pre-existing map");
            if (!maps.Contains(map))
            {
                maps.Add(map);
            }

            OwnedHomes[map] = home;
        }
        public Pawn SpawnEternalPawn(MapType map)
        {
            return SpawnOwnedPawn(() => TestPawnFactory.SpawnEternalPawn(map));
        }

        public Pawn SpawnNonEternalPawn(MapType map)
        {
            TestAssert.IsTrue(maps.Contains(map), "Ordinary sentinel requires its own fixture map");
            TestAssert.IsFalse(ordinarySentinels.ContainsKey(map), "An auxiliary map may own only one ordinary sentinel");
            var pawn = SpawnOwnedPawn(() => TestPawnFactory.SpawnNonEternalPawn(map));
            ordinarySentinels.Add(map, pawn);
            return pawn;
        }

        private Pawn SpawnOwnedPawn(Func<Pawn> spawn)
        {
            var tracked = (IList)AccessTools.Field(typeof(TestPawnFactory), "_spawnedTestThings").GetValue(null);
            var before = new HashSet<Thing>(tracked.Cast<Thing>());
            try { return spawn(); }
            finally
            {
                // Both factory paths can fail after generation but before returning the owned pawn.
                foreach (var pawn in tracked.Cast<Thing>().OfType<Pawn>().Where(p => !before.Contains(p)))
                {
                    OwnPawn(pawn);
                }
            }
        }
        public void OwnPawn(Pawn pawn)
        {
            TestAssert.IsNotNull(pawn, "Owned pawn");
            if (!pawns.Contains(pawn))
            {
                pawns.Add(pawn);
            }
        }

        public void AssertPopulation(MapType map, Pawn pawn = null, string kind = null)
        {
            TestAssert.IsTrue(maps.Contains(map), "Population check requires an owned map");
            var living = map.mapPawns.AllPawnsSpawned;
            var corpses = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).OfType<CorpseType>().ToList();
            int expected = pawn == null ? 0 : 1;
            bool unexpectedIdentity = pawn != null
                && (!living.All(p => ReferenceEquals(p, pawn)) || !corpses.All(c => c.InnerPawn == pawn));
            ordinarySentinels.TryGetValue(map, out var sentinel);
            bool invalidSentinel = sentinel != null && (!ReferenceEquals(pawn, sentinel) || sentinel.Dead
                || sentinel.story.traits.GetTrait(EternalDefOf.Eternal_GeneticMarker) != null
                || sentinel.health.hediffSet.HasHediff(EternalDefOf.Eternal_Essence));
            bool unsafeTemperature = sentinel != null && sentinel.Spawned
                && !sentinel.SafeTemperatureRange().Includes(GenTemperature.GetTemperatureForCell(sentinel.Position, map));
            string diagnostic = expected != living.Count + corpses.Count || unexpectedIdentity || invalidSentinel || unsafeTemperature
                ? " Context: " + PopulationDiagnostic(map, pawn, kind, living, corpses)
                : string.Empty;
            TestAssert.AreEqual(expected, living.Count + corpses.Count,
                "Each fixture map must contain its one assigned pawn or corpse; measured empty controls remain empty" + diagnostic);
            if (pawn == null)
            {
                return;
            }

            TestAssert.IsTrue(!unexpectedIdentity,
                "Unexpected population on isolated fixture map" + diagnostic);
            if (sentinel != null)
            {
                TestAssert.IsTrue(ReferenceEquals(pawn, sentinel), "Auxiliary solitude check must identify its registered ordinary sentinel" + diagnostic);
                TestAssert.IsFalse(sentinel.Dead, "Ordinary auxiliary colonist must remain alive" + diagnostic);
                TestAssert.IsTrue(sentinel.story.traits.GetTrait(EternalDefOf.Eternal_GeneticMarker) == null
                    && !sentinel.health.hediffSet.HasHediff(EternalDefOf.Eternal_Essence),
                    "Ordinary auxiliary colonist must remain non-Eternal" + diagnostic);
                TestAssert.IsTrue(sentinel.SafeTemperatureRange().Includes(GenTemperature.GetTemperatureForCell(sentinel.Position, map)),
                    "Ordinary auxiliary colonist must remain within its native safe temperature range" + diagnostic);
            }
        }

        private static string PopulationDiagnostic(MapType map, Pawn pawn, string kind,
            IEnumerable<Pawn> living, IEnumerable<CorpseType> corpses)
        {
            var manager = EternalServiceContainer.Instance.CorpseManager;
            var entry = pawn == null ? null : manager?.GetCorpseData(pawn);
            var trackedCorpse = entry?.Corpse;
            var component = trackedCorpse?.GetComp<Eternal.Corpse.EternalCorpseComponent>();
            // Keep reads independent: a broken holder must not hide the original solitude failure.
            return "kind=" + (kind ?? InferDiagnosticKind(map, pawn))
                + "; fixtureMap=" + MapIdentity(map)
                + "; livingIDs=" + string.Join(",", living.Select(p => p.thingIDNumber).OrderBy(id => id))
                + "; corpseIDs=" + string.Join(",", corpses.Select(c => c.thingIDNumber).OrderBy(id => id))
                + "; pawn=" + ThingDiagnostic(pawn)
                + "; pawnDead=" + ReadDiagnostic(() => pawn?.Dead.ToString() ?? "null")
                + "; pawnCorpse=" + ReadDiagnostic(() => ThingDiagnostic(pawn?.Corpse))
                + "; tracked=" + (entry != null)
                + "; trackedMap=" + MapIdentity(entry?.CurrentMap)
                + "; trackedCorpse=" + ThingDiagnostic(trackedCorpse)
                + "; corpseInnerPawn=" + ReadDiagnostic(() => trackedCorpse?.InnerPawn?.thingIDNumber.ToString(CultureInfo.InvariantCulture) ?? "null")
                + "; componentPresent=" + (component != null)
                + "; componentParent=" + ThingDiagnostic(component?.parent)
                + "; temperature=" + ReadDiagnostic(() => GenTemperature.GetTemperatureForCell(pawn?.Position ?? map.Center, map).ToString("R", CultureInfo.InvariantCulture))
                + "; roofProtection=" + (Eternal_Mod.settings?.enableRoofCollapseProtection.ToString() ?? "null");
        }

        private static string InferDiagnosticKind(MapType map, Pawn pawn)
        {
            if (pawn == null)
            {
                return "empty-control";
            }
            // This is a diagnostic hint, not a substitute for the caller's exact palette label.
            if (pawn.Dead && pawn.Position.InBounds(map) && map.roofGrid.RoofAt(pawn.Position)?.isThickRoof == true)
            {
                return "roof-death(inferred)";
            }

            return pawn.Dead ? "dead-unspecified" : "living-unspecified";
        }

        private static string ThingDiagnostic(Thing thing)
        {
            if (thing == null)
            {
                return "null";
            }

            return "{id=" + thing.thingIDNumber
                + ",type=" + thing.GetType().FullName
                + ",spawned=" + ReadDiagnostic(() => thing.Spawned.ToString())
                + ",destroyed=" + ReadDiagnostic(() => thing.Destroyed.ToString())
                + ",map=" + ReadDiagnostic(() => MapIdentity(thing.Map))
                + ",mapHeld=" + ReadDiagnostic(() => MapIdentity(thing.MapHeld))
                + ",position=" + ReadDiagnostic(() => thing.Position.ToString())
                + ",holder=" + ReadDiagnostic(() => HolderIdentity(thing.ParentHolder)) + "}";
        }

        private static string HolderIdentity(IThingHolder holder)
        {
            if (holder == null)
            {
                return "null";
            }

            if (holder is Thing thing)
            {
                return holder.GetType().FullName + "#" + thing.thingIDNumber;
            }

            if (holder is MapType map)
            {
                return MapIdentity(map);
            }

            return holder.GetType().FullName;
        }

        private static string MapIdentity(MapType map)
        {
            return map == null ? "null" : "Map#" + map.uniqueID.ToString(CultureInfo.InvariantCulture);
        }

        private static string ReadDiagnostic(Func<string> read)
        {
            try { return read(); }
            catch (Exception ex) { return "unreadable(" + ex.GetType().FullName + ")"; }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            var failures = new List<Exception>();
            Action<Action> attempt = action => { try { action(); } catch (Exception ex) { failures.Add(ex); } };
            try
            {
                attempt(RemoveOwnedProtection);
                foreach (var pawn in pawns.ToArray())
                {
                    attempt(() => CleanupPawn(pawn));
                }

                foreach (var map in maps.AsEnumerable().Reverse())
                {
                    attempt(() => { if (Find.Maps.Contains(map)) { Current.Game.DeinitAndRemoveMap(map, false); } });
                }

                foreach (var parent in parents.AsEnumerable().Reverse())
                {
                    attempt(() => { if (Find.WorldObjects.AllWorldObjects.Contains(parent)) { Find.WorldObjects.Remove(parent); } });
                }
                // Camp removal creates an AbandonedCamp on the same owned tile. Remove only these remnants.
                var tiles = new HashSet<int>(parents.Select(p => (int)p.Tile));
                foreach (var worldObject in Find.WorldObjects.AllWorldObjects.ToArray())
                {
                    if (!originalObjects.Contains(worldObject) && tiles.Contains((int)worldObject.Tile)
                        && worldObject.def == WorldObjectDefOf.AbandonedCamp)
                    {
                        attempt(() => Find.WorldObjects.Remove(worldObject));
                    }
                }

                attempt(RemoveOwnedProtection);
                attempt(() => PawnExtensions.InvalidateEternalPawnCache());
                attempt(() => TestAssert.IsFalse(maps.Any(m => Find.Maps.Contains(m)), "Owned map retained after cleanup"));
                attempt(() => TestAssert.IsFalse(parents.Any(p => Find.WorldObjects.AllWorldObjects.Contains(p)), "Owned parent retained after cleanup"));
                if (originalCurrentMap != null && Find.Maps.Contains(originalCurrentMap))
                {
                    attempt(() => Current.Game.CurrentMap = originalCurrentMap);
                }
            }
            finally
            {
                foreach (var map in maps)
                {
                    OwnedHomes.Remove(map);
                }

                harmony.UnpatchAll(harmony.Id);
                ordinarySentinels.Clear();
            }
            if (failures.Count > 0)
            {
                throw new AggregateException("Isolated fixture cleanup failed", failures);
            }
        }

        private void CleanupPawn(Pawn pawn)
        {
            // Reuse the existing production-ledger cleanup and remove only this scope's factory reference.
            var cleanup = AccessTools.Method(typeof(TestPawnFactory), "CleanupPawn");
            try { cleanup.Invoke(null, new object[] { pawn }); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
            var tracked = (IList)AccessTools.Field(typeof(TestPawnFactory), "_spawnedTestThings").GetValue(null);
            while (tracked.Contains(pawn))
            {
                tracked.Remove(pawn);
            }
        }

        private void RemoveOwnedProtection()
        {
            var manager = EternalServiceContainer.Instance.MapProtection;
            if (manager == null)
            {
                return;
            }

            var pending = (IList)AccessTools.Field(manager.GetType(), "pendingTeleports")?.GetValue(manager);
            if (pending != null)
            {
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    var row = pending[i];
                    var corpse = AccessTools.Field(row.GetType(), "Corpse").GetValue(row) as CorpseType;
                    var target = AccessTools.Field(row.GetType(), "TargetMap").GetValue(row) as MapType;
                    if ((corpse != null && pawns.Contains(corpse.InnerPawn)) || maps.Contains(target))
                    {
                        pending.RemoveAt(i);
                    }
                }
            }

            var protectedMaps = AccessTools.Field(manager.GetType(), "protectedMaps")?.GetValue(manager) as HashSet<MapType>;
            var data = AccessTools.Field(manager.GetType(), "protectionData")?.GetValue(manager) as IDictionary;
            foreach (var map in maps) { protectedMaps?.Remove(map); data?.Remove(map); }
        }

        private static bool HomePrefix(MapType __instance, ref bool __result)
        {
            if (!OwnedHomes.TryGetValue(__instance, out bool home))
            {
                return true;
            }

            __result = home;
            return false;
        }
        private static bool ScenarioPrefix(MapType map) { return !OwnedHomes.ContainsKey(map); }
        private static bool WildlifePrefix(WildAnimalSpawner __instance)
        {
            var map = (MapType)AccessTools.Field(typeof(WildAnimalSpawner), "map").GetValue(__instance);
            return !OwnedHomes.ContainsKey(map);
        }
        private static bool IncidentPrefix(FiringIncident fi, ref bool __result)
        {
            if (!(fi.parms.target is MapType map) || !OwnedHomes.ContainsKey(map))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }
}
#endif
