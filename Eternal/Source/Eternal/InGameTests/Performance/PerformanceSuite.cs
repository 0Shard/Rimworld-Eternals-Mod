// Author: 0Shard
// Description: Concurrent native ticks on isolated maps; separately timed Eternal callbacks and observed events.
#if DEBUG
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Eternal.DI;
using Eternal.InGameTests.Helpers;
using RimWorld;
using Verse;
using MapType = Verse.Map;

namespace Eternal.InGameTests.Performance
{
    public static class PerformanceSuite
    {
        private static readonly Func<long> AllocationCounter = CreateAllocationCounter();
        public static bool IsRunning { get; private set; }

        public static IEnumerable<XElement> RunAll(string configPath, string reportDirectory)
        {
            var configuration = PerformanceConfiguration.Load(configPath);
            if (string.IsNullOrWhiteSpace(reportDirectory))
            {
                throw new ArgumentException("Explicit report directory is required");
            }

            Directory.CreateDirectory(reportDirectory);
            var result = new XElement("performanceSuite", new XAttribute("configuration", Path.GetFullPath(configPath)),
                new XAttribute("execution", "yield-between-profiles"));
            var failures = new List<Exception>();
            foreach (var profile in configuration.Profiles)
            {
                try { result.Add(RunProfile(profile, configuration, reportDirectory)); }
                catch (Exception ex)
                {
                    result.Add(new XElement("profileFailure", new XAttribute("name", profile.Name), ex.ToString()));
                    failures.Add(ex);
                }
                Save(result, Path.Combine(reportDirectory, "suite.xml"));
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                // Each profile retains its full preparation and tick schedule. Return to the native frame loop between profiles.
                yield return result;
            }
            // Terminal functional control can create native game-over letters. It must follow every timed profile.
            var allDeadProfile = configuration.Profiles.Where(p => !p.EmptyControl)
                .OrderByDescending(p => p.Population).ThenBy(p => p.MapSize).FirstOrDefault();
            if (allDeadProfile != null)
            {
                try { RunAllDeadControl(allDeadProfile, configuration, result); }
                catch (Exception ex)
                {
                    result.Add(new XElement("allDeadFailure", ex.ToString()));
                    failures.Add(ex);
                }
                Save(result, Path.Combine(reportDirectory, "suite.xml"));
            }
            if (failures.Count > 0)
            {
                throw new AggregateException("Performance profiles failed; see suite.xml", failures);
            }

            result.SetAttributeValue("complete", true);
            result.SetAttributeValue("passed", true);
            Save(result, Path.Combine(reportDirectory, "suite.xml"));
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            yield return result;
        }

        public static XElement RunProfile(PerformanceProfile profile, PerformanceConfiguration configuration, string reportDirectory)
        {
            if (IsRunning)
            {
                throw new InvalidOperationException("Recursive performance harness invocation");
            }

            TestAssert.Available(Find.TickManager, "Loaded native TickManager");
            TestAssert.Available(Eternal_Component.Current, "Initialized Eternal component");
            TestAssert.Available(Eternal_Mod.settings, "Loaded Eternal settings");
            TestAssert.IsFalse(Find.Maps.Any(m => m.mapPawns.AllPawnsSpawned.Count > 0
                || m.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).Count > 0),
                "Real ticks require a disposable test game with empty pre-existing maps");
            Directory.CreateDirectory(reportDirectory);
            var path = Path.Combine(reportDirectory, profile.Name + ".xml");
            var report = new XElement("profile", new XAttribute("name", profile.Name),
                new XAttribute("cohortMaps", profile.Population), new XAttribute("mapSize", profile.MapSize),
                new XAttribute("emptyControl", profile.EmptyControl), new XAttribute("seed", configuration.Seed),
                new XAttribute("timestampFrequency", Stopwatch.Frequency),
                new XAttribute("allocationCounterAvailable", AllocationCounter != null),
                new XAttribute("autosaveIntervalDays", Prefs.AutosaveIntervalDays),
                new XAttribute("warmupFrames", configuration.WarmupFrames), new XAttribute("batchFrames", configuration.BatchFrames),
                new XAttribute("frameSteps", string.Join(",", configuration.FrameSteps)),
                new XElement("timingNotes", "Stopwatch elapsed duration around main-thread work (not OS process CPU time). Total includes actual native ticks and their Eternal callbacks. Callback timing is the scoped profiler sum around ProcessTick; diagnostic Harmony profiling overhead is included. Generation, preparation, assertions, and event snapshots are excluded. Synthetic empty terrain workload. Empty profiles include one reported ordinary colonist on an auxiliary map. No rendering time or real-colony workload claim."));
            var samples = profile.Samples == 0 ? configuration.Samples : profile.Samples;
            var manager = Find.TickManager;
            var savedSpeed = manager.CurTimeSpeed;
            using (var profiler = new ProductionProfiler())
            {
                IsRunning = true;
                Log.Message("[EternalPerformance] START " + profile.Name + " samples=" + samples);
                try
                {
                    manager.CurTimeSpeed = TimeSpeed.Paused;
                    for (int sample = 0; sample < samples; sample++)
                    {
                        var sampleReport = new XElement("sample", new XAttribute("index", sample));
                        report.Add(sampleReport);
                        Save(report, path);
                        Rand.PushState(configuration.Seed + sample);
                        Action<int, string, string, MapType, Pawn> preparationProgress = (index, kind, stage, map, pawn) =>
                            WritePreparationProgress(report, sampleReport, path, profile.Name, sample, index, kind, stage, map, pawn);
                        try
                        {
                            WithOwnedMaps(scope =>
                            {
                                var states = Prepare(scope, profile, configuration, sample, preparationProgress);
                                sampleReport.Add(new XAttribute("totalMaps", Find.Maps.Count),
                                    new XAttribute("ownedMaps", scope.Maps.Count),
                                    new XAttribute("cohortPawns", profile.EmptyControl ? 0 : profile.Population),
                                    new XAttribute("auxiliaryHealthyControlPawns", !profile.EmptyControl && profile.Population == 1 ? 1 : 0),
                                    new XAttribute("auxiliaryOrdinaryColonistCount", profile.EmptyControl ? 1 : 0),
                                    new XAttribute("auxiliaryMapCount", scope.Maps.Count - profile.Population));
                                foreach (var control in states.Where(s => s.Kind == "ordinary-colonist-control"))
                                {
                                    sampleReport.Add(new XElement("ordinaryControl", new XAttribute("pawn", control.Pawn.thingIDNumber),
                                        new XAttribute("map", control.Map.uniqueID), new XAttribute("mapSize", control.Map.Size.x),
                                        new XAttribute("temperature", GenTemperature.GetTemperatureForCell(control.Pawn.Position, control.Map))));
                                }

                                var events = new XElement("events");
                                sampleReport.Add(events);
                                sampleReport.Add(InputFingerprint(scope, states));
                                Observe(scope, states, events, "prepared");
                                int frame = 0;
                                Drive(configuration.WarmupFrames, configuration, ref frame, false);
                                Observe(scope, states, events, "warmup");
                                var batches = new List<BatchMeasurement>();
                                var productionProfiles = new XElement("productionProfiles",
                                    new XAttribute("scope", "per measured batch; excludes setup and event/report formatting"));
                                sampleReport.Add(productionProfiles);
                                for (int batch = 0; batch < configuration.Batches; batch++)
                                {
                                    // A real second death is a fixture stimulus, outside the timing window.
                                    if (batch == configuration.Batches / 2)
                                    {
                                        foreach (var state in states.Where(s => s.Kind == "second-death"))
                                        {
                                            TestAssert.IsFalse(state.Pawn.Dead, "Second-death fixture must still be alive before stimulus");
                                            int actorIndex = states.IndexOf(state);
                                            preparationProgress(actorIndex, state.Kind, "trial-death-before", state.Map, state.Pawn);
                                            TestPawnFactory.KillPawn(state.Pawn);
                                            preparationProgress(actorIndex, state.Kind, "trial-death-after", state.Map, state.Pawn);
                                            state.SecondDeathTriggered = true;
                                            preparationProgress(actorIndex, state.Kind, "trial-healing-request-before", state.Map, state.Pawn);
                                            PerformanceStatePalette.StartHealing(state.Pawn);
                                            preparationProgress(actorIndex, state.Kind, "trial-healing-request-after", state.Map, state.Pawn);
                                        }
                                    }

                                    profiler.Reset();
                                    var measurement = Drive(configuration.BatchFrames, configuration, ref frame, true);
                                    var productionSnapshot = profiler.Snapshot();
                                    measurement.CallbackTimestampTicks = productionSnapshot.Elements("method")
                                        .Where(method => ((string)method.Attribute("name")).Contains("TickOrchestrator.ProcessTick("))
                                        .Sum(method => (long)method.Attribute("timestampTicks"));
                                    productionSnapshot.Add(new XAttribute("batch", batch));
                                    productionProfiles.Add(productionSnapshot);
                                    AssertProductionCadence(productionSnapshot, measurement.NativeTicks);
                                    batches.Add(measurement);
                                    sampleReport.Add(measurement.ToXml(batch));
                                    if (batch % configuration.EventEveryBatches == 0 || batch == configuration.Batches - 1)
                                    {
                                        Observe(scope, states, events, "batch-" + batch);
                                    }

                                    Save(report, path);
                                }
                                sampleReport.Add(Summarize(batches));
                                ValidateTransitions(scope, states, configuration, events);
                                sampleReport.Add(new XAttribute("observationsPassed", true));
                            }, stage => preparationProgress(-1, "fixture-scope", stage, null, null));
                            sampleReport.Add(new XAttribute("cleanupPassed", true));
                        }
                        catch (Exception ex)
                        {
                            sampleReport.Add(new XElement("failure", ex.ToString()));
                            Save(report, path);
                            throw;
                        }
                        finally { Rand.PopState(); }
                        Save(report, path);
                        Log.Message("[EternalPerformance] SAMPLE " + profile.Name + " " + (sample + 1) + "/" + samples);
                    }

                    report.Add(new XAttribute("passed", true));
                    Log.Message("[EternalPerformance] COMPLETE " + profile.Name + " report=" + path);
                    return report;
                }
                catch (Exception ex)
                {
                    report.Add(new XAttribute("passed", false), new XElement("profileError", ex.ToString()));
                    Log.Error("[EternalPerformance] FAIL " + profile.Name + " " + ex.Message);
                    throw;
                }
                finally
                {
                    manager.CurTimeSpeed = savedSpeed;
                    IsRunning = false;
                    Save(report, path);
                }
            }
        }

        private static List<PerformancePawnState> Prepare(TestMapScope scope, PerformanceProfile profile,
            PerformanceConfiguration configuration, int sample, Action<int, string, string, MapType, Pawn> progress = null)
        {
            int auxiliary = profile.EmptyControl || profile.Population == 1 ? 1 : 0;
            TestAssert.IsTrue(Find.Maps.Count + profile.Population + auxiliary < 127,
                "Profile plus existing/control maps exceeds native map index capacity");
            var states = new List<PerformancePawnState>();
            for (int index = 0; index < profile.Population; index++)
            {
                var kindIndex = profile.Population == 1 ? sample % PerformanceStatePalette.Kinds.Length
                    : index == 0 ? 0 : 1 + (sample * (profile.Population - 1) + index - 1) % (PerformanceStatePalette.Kinds.Length - 1);
                string kind = profile.EmptyControl ? "empty-map" : PerformanceStatePalette.Kinds[kindIndex];
                progress?.Invoke(index, kind, "map-before", null, null);
                Action<string, MapType> mapProgress = progress == null ? null
                    : (stage, generated) => progress(index, kind, stage, generated, null);
                var map = scope.Create(profile.MapSize, true, false, mapProgress);
                progress?.Invoke(index, kind, "map-after", map, null);
                if (profile.EmptyControl) { scope.AssertPopulation(map); continue; }
                progress?.Invoke(index, kind, "pawn-before", map, null);
                var pawn = scope.SpawnEternalPawn(map);
                progress?.Invoke(index, kind, "pawn-after", map, pawn);
                progress?.Invoke(index, kind, "state-before", map, pawn);
                states.Add(PerformanceStatePalette.Prepare(pawn, map, kindIndex, configuration,
                    stage => progress?.Invoke(index, kind, stage, map, pawn)));
                progress?.Invoke(index, kind, "state-after", map, pawn);
                scope.AssertPopulation(map, pawn, states.Last().Kind);
            }
            if (profile.EmptyControl)
            {
                const string kind = "ordinary-colonist-control";
                progress?.Invoke(profile.Population, kind, "map-before", null, null);
                Action<string, MapType> mapProgress = progress == null ? null
                    : (stage, generated) => progress(profile.Population, kind, stage, generated, null);
                var map = scope.CreateOrdinaryControlMap(profile.MapSize, mapProgress);
                progress?.Invoke(profile.Population, kind, "map-after", map, null);
                progress?.Invoke(profile.Population, kind, "pawn-before", map, null);
                var pawn = scope.SpawnNonEternalPawn(map);
                progress?.Invoke(profile.Population, kind, "pawn-after", map, pawn);
                progress?.Invoke(profile.Population, kind, "state-before", map, pawn);
                // Healthy input normalization reuses the palette without adding any Eternal trait or Essence.
                PerformanceStatePalette.Prepare(pawn, map, 0, configuration);
                var food = ThingMaker.MakeThing(ThingDefOf.MealSimple);
                food.stackCount = food.def.stackLimit;
                GenSpawn.Spawn(food, CellFinder.RandomClosewalkCellNear(map.Center, map, 4), map);
                states.Add(new PerformancePawnState(pawn, map, "ordinary-colonist-control", false));
                progress?.Invoke(profile.Population, kind, "state-after", map, pawn);
                scope.AssertPopulation(map, pawn, "ordinary-colonist-control");
            }
            else if (auxiliary > 0)
            {
                const string kind = "healthy-auxiliary";
                progress?.Invoke(profile.Population, kind, "map-before", null, null);
                Action<string, MapType> mapProgress = progress == null ? null
                    : (stage, generated) => progress(profile.Population, kind, stage, generated, null);
                var map = scope.Create(profile.MapSize, true, false, mapProgress);
                progress?.Invoke(profile.Population, kind, "map-after", map, null);
                progress?.Invoke(profile.Population, kind, "pawn-before", map, null);
                var pawn = scope.SpawnEternalPawn(map);
                progress?.Invoke(profile.Population, kind, "pawn-after", map, pawn);
                progress?.Invoke(profile.Population, kind, "state-before", map, pawn);
                states.Add(PerformanceStatePalette.Prepare(pawn, map, 0, configuration));
                progress?.Invoke(profile.Population, kind, "state-after", map, pawn);
            }
            return states;
        }

        private static void WritePreparationProgress(XElement report, XElement sampleReport, string path,
            string profile, int sample, int index, string kind, string stage, MapType map, Pawn pawn)
        {
            var previous = sampleReport.Element("preparation");
            var progress = new XElement("preparation", new XAttribute("sequence", ((int?)previous?.Attribute("sequence") ?? -1) + 1),
                new XAttribute("profile", profile), new XAttribute("sample", sample), new XAttribute("actorIndex", index),
                new XAttribute("kind", kind), new XAttribute("stage", stage), new XAttribute("T", Find.TickManager.TicksGame),
                new XAttribute("map", map?.uniqueID ?? -1), new XAttribute("pawn", pawn?.thingIDNumber ?? -1));
            if (previous == null)
            {
                sampleReport.Add(progress);
            }
            else
            {
                previous.ReplaceWith(progress);
            }
            // Persist the latest boundary immediately; the complete ordered history stays in the native log.
            Save(report, path);
            Log.Message("[EternalPerformance] PREPARE " + progress.ToString(SaveOptions.DisableFormatting));
        }

        private static BatchMeasurement Drive(int frames, PerformanceConfiguration configuration, ref int frame, bool measured)
        {
            var result = new BatchMeasurement();
            var manager = Find.TickManager;
            var savedSpeed = manager.CurTimeSpeed;
            try
            {
                // All production callbacks run inside these actual native ticks.
                manager.CurTimeSpeed = TimeSpeed.Normal;
                NativePauseDiagnostics.RequireRunnable("drive-start", frame);
                int tickBefore = manager.TicksGame;
                int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
                long allocatedBefore = measured && AllocationCounter != null ? AllocationCounter() : 0;
                long totalStart = measured ? Stopwatch.GetTimestamp() : 0;
                for (int index = 0; index < frames; index++, frame++)
                {
                    int steps = configuration.FrameSteps[frame % configuration.FrameSteps.Count];
                    // Multi-tick frames advance all native systems; zero steps exercise repeat callbacks at the same T.
                    for (int step = 0; step < steps; step++)
                    {
                        manager.DoSingleTick();
                    }

                    // Invalid paused trials stop here; their duration is never accepted as a measurement.
                    NativePauseDiagnostics.RequireRunnable("drive-frame", frame);
                }
                long totalEnd = measured ? Stopwatch.GetTimestamp() : 0;
                result.TotalTimestampTicks = totalEnd - totalStart;
                result.AllocatedBytes = measured && AllocationCounter != null ? (long?)(AllocationCounter() - allocatedBefore) : null;
                result.Gc0 = GC.CollectionCount(0) - gc0; result.Gc1 = GC.CollectionCount(1) - gc1; result.Gc2 = GC.CollectionCount(2) - gc2;
                result.Frames = frames; result.NativeTicks = manager.TicksGame - tickBefore;
                result.StartT = tickBefore; result.EndT = manager.TicksGame;
                return result;
            }
            finally
            {
                manager.CurTimeSpeed = savedSpeed;
            }
        }

        private static void AssertProductionCadence(XElement snapshot, int nativeTicks)
        {
            long dispatches = snapshot.Elements("method")
                .Where(row => ((string)row.Attribute("name"))?.Contains("TickOrchestrator.ProcessTick(") == true)
                .Sum(row => (long)row.Attribute("calls"));
            TestAssert.AreEqual((long)nativeTicks, dispatches, "Every real native tick dispatches exactly one Eternal callback");
            TestAssert.IsTrue(nativeTicks >= Math.Max(Eternal_Mod.settings.normalTickRate, Eternal_Mod.settings.rareTickRate),
                "Each measured batch must span both configured healing intervals");
            foreach (var method in new[] { "ProcessNormalTick", "ProcessRareTick" })
            {
                long calls = snapshot.Elements("method")
                    .Where(row => ((string)row.Attribute("name"))?.Contains("." + method + "(") == true)
                    .Sum(row => (long)row.Attribute("calls"));
                TestAssert.IsTrue(calls > 0, "Measured production cadence must actually execute " + method);
            }
        }

        private static XElement InputFingerprint(TestMapScope scope, List<PerformancePawnState> states)
        {
            var ids = Find.UniqueIDsManager;
            return new XElement("inputFingerprint",
                new XAttribute("T", Find.TickManager.TicksGame), new XAttribute("absoluteT", GenTicks.TicksAbs),
                new XAttribute("worldSeed", Find.World.info.seedString),
                new XElement("random", new XAttribute("seed", typeof(Rand).GetField("seed", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)),
                    new XAttribute("iterations", typeof(Rand).GetField("iterations", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null))),
                new XElement("allocators", ids.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
                    .Where(field => field.FieldType.IsPrimitive).OrderBy(field => field.Name, StringComparer.Ordinal)
                    .Select(field => new XElement("counter", new XAttribute("name", field.Name), new XAttribute("value", field.GetValue(ids))))),
                new XElement("maps", scope.Maps.Select(map => new XElement("map", new XAttribute("id", map.uniqueID),
                    new XAttribute("tile", map.Tile.ToString()), new XAttribute("size", map.Size.ToString())))),
                new XElement("actors", states.Select(state => new XElement("actor", new XAttribute("kind", state.Kind),
                    new XAttribute("id", state.Pawn.thingIDNumber), new XAttribute("map", state.Map.uniqueID),
                    new XElement("hediffs", state.Pawn.health.hediffSet.hediffs.Select(hediff => new XElement("hediff",
                        new XAttribute("id", hediff.loadID), new XAttribute("def", hediff.def.defName),
                        new XAttribute("severity", hediff.Severity), new XAttribute("part", hediff.Part?.def.defName ?? "whole"))))))));
        }

        private static void Observe(TestMapScope scope, List<PerformancePawnState> states, XElement events, string stage)
        {
            foreach (var map in scope.Maps)
            {
                var state = states.FirstOrDefault(s => s.Map == map);
                scope.AssertPopulation(map, state?.Pawn, state?.Kind);
            }
            foreach (var state in states)
            {
                var pawn = state.Pawn;
                bool dead = pawn.Dead;
                if (dead)
                {
                    state.SawDeath = true;
                }
                else if (state.SawDeath)
                {
                    state.SawResurrection = true;
                }

                string health = string.Join(";", pawn.health.hediffSet.hediffs.Select(h =>
                    h.def.defName + ":" + h.Severity.ToString("R", CultureInfo.InvariantCulture) + ":" + (h.Part?.def.defName ?? "whole")));
                string regrowth = string.Join(";", EternalServiceContainer.Instance.RegrowthManager.GetRegrowingHediffs(pawn)
                    .Select(h => (h.forPart?.def.defName ?? "null") + ":" + h.Severity.ToString("R", CultureInfo.InvariantCulture)));
                float debt = EternalServiceContainer.Instance.FoodDebtSystem.GetDebt(pawn);
                string snapshot = dead + "|" + health + "|" + regrowth + "|" + debt.ToString("R", CultureInfo.InvariantCulture);
                if (snapshot == state.LastSnapshot && stage != "prepared")
                {
                    continue;
                }

                state.LastSnapshot = snapshot;
                events.Add(new XElement("event", new XAttribute("T", Find.TickManager.TicksGame),
                    new XAttribute("pawn", pawn.thingIDNumber), new XAttribute("map", state.Map.uniqueID),
                    new XAttribute("kind", state.Kind), new XAttribute("stage", stage), new XAttribute("dead", dead),
                    new XAttribute("trackedCorpse", EternalServiceContainer.Instance.CorpseManager.IsTracked(pawn)),
                    new XAttribute("debt", debt), new XAttribute("food", pawn.needs?.food?.CurLevel.ToString("R", CultureInfo.InvariantCulture) ?? "disabled"),
                    new XAttribute("health", health), new XAttribute("regrowth", regrowth)));
            }
        }

        private static void ValidateTransitions(TestMapScope scope, List<PerformancePawnState> states,
            PerformanceConfiguration configuration, XElement events)
        {
            foreach (var state in states.Where(s => s.ExpectDeath))
            {
                if (!state.Pawn.Dead)
                {
                    TickAdvancer.AdvanceUntil(() => state.Pawn.Dead, configuration.DeathValidationTicks,
                        "Native dying fixture did not die: " + state.Kind);
                }

                Observe(scope, states, events, "death-validation");
                TestAssert.IsNotNull(EternalServiceContainer.Instance.CorpseManager.GetCorpseData(state.Pawn),
                    "Actual tick-driven death must be tracked: " + state.Kind);
            }
            foreach (var state in states.Where(s => s.Kind == "second-death"))
            {
                TestAssert.IsTrue(state.SecondDeathTriggered, "Second death stimulus must run");
            }

            foreach (var state in states.Where(s => s.Kind == "healthy" || s.Kind == "ordinary-colonist-control"))
            {
                TestAssert.IsFalse(state.Pawn.Dead, "Healthy control must survive measured/validation ticks");
            }
        }

        private static void RunAllDeadControl(PerformanceProfile profile, PerformanceConfiguration configuration, XElement report)
        {
            var events = new XElement("allDeadControl");
            report.Add(events);
            WithOwnedMaps(scope =>
            {
                var states = new List<PerformancePawnState>();
                for (int index = 0; index < profile.Population; index++)
                {
                    var map = scope.Create(profile.MapSize, true, false);
                    var pawn = scope.SpawnEternalPawn(map);
                    var state = PerformanceStatePalette.Prepare(pawn, map, 0, configuration);
                    TestPawnFactory.KillPawn(pawn);
                    states.Add(state);
                }
                TickAdvancer.AdvanceTicks(1);
                Observe(scope, states, events, "all-dead-control");
                foreach (var state in states)
                {
                    TestAssert.IsTrue(state.Pawn.Dead, "All-dead control must remain dead without player healing request");
                    TestAssert.IsTrue(EternalServiceContainer.Instance.CorpseManager.IsTracked(state.Pawn),
                        "All-dead control must retain each tracked Eternal corpse");
                }
            });
            events.Add(new XAttribute("cleanupPassed", true));
        }

        private static void WithOwnedMaps(Action<TestMapScope> action, Action<string> setupProgress = null)
        {
            setupProgress?.Invoke("scope-before");
            var scope = new TestMapScope();
            Exception observationFailure = null;
            try
            {
                setupProgress?.Invoke("scope-after");
                action(scope);
            }
            catch (Exception ex) { observationFailure = ex; throw; }
            finally
            {
                try { scope.Dispose(); }
                catch (Exception cleanupFailure)
                {
                    if (observationFailure != null)
                    {
                        throw new AggregateException("Fixture observations and cleanup both failed", observationFailure, cleanupFailure);
                    }

                    throw;
                }
            }
        }

        private static XElement Summarize(List<BatchMeasurement> batches)
        {
            return new XElement("summary", new XAttribute("totalMedianMs", Percentile(batches.Select(b => b.TotalTimestampTicks), 0.5)),
                new XAttribute("totalP95Ms", Percentile(batches.Select(b => b.TotalTimestampTicks), 0.95)),
                new XAttribute("callbackMedianMs", Percentile(batches.Select(b => b.CallbackTimestampTicks), 0.5)),
                new XAttribute("callbackP95Ms", Percentile(batches.Select(b => b.CallbackTimestampTicks), 0.95)),
                new XAttribute("gc0", batches.Sum(b => b.Gc0)), new XAttribute("gc1", batches.Sum(b => b.Gc1)),
                new XAttribute("gc2", batches.Sum(b => b.Gc2)));
        }
        internal static double Percentile(IEnumerable<long> values, double percentile)
        {
            var ordered = values.OrderBy(x => x).ToArray();
            if (ordered.Length == 0 || percentile < 0 || percentile > 1)
            {
                throw new ArgumentException("Invalid percentile sample");
            }

            return ordered[Math.Max(0, (int)Math.Ceiling(percentile * ordered.Length) - 1)] * 1000d / Stopwatch.Frequency;
        }
        private static Func<long> CreateAllocationCounter()
        {
            try
            {
                var method = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Static | BindingFlags.Public);
                if (method == null || method.ReturnType != typeof(long))
                {
                    return null;
                }

                var counter = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), method);
                long before = counter();
                var probe = new byte[4096];
                long after = counter();
                GC.KeepAlive(probe);
                return after - before >= 4096 ? counter : null;
            }
            catch { return null; }
        }
        private static void Save(XElement report, string path)
        {
            // Flush after each batch/sample. Failed runs retain the observations produced before failure.
            new XDocument(new XElement(report)).Save(path);
        }
        private sealed class BatchMeasurement
        {
            internal long TotalTimestampTicks, CallbackTimestampTicks;
            internal long? AllocatedBytes;
            internal int Gc0, Gc1, Gc2, Frames, NativeTicks, StartT, EndT;
            internal XElement ToXml(int index)
            {
                var element = new XElement("batch", new XAttribute("index", index), new XAttribute("startT", StartT),
                    new XAttribute("endT", EndT), new XAttribute("frames", Frames), new XAttribute("nativeTicks", NativeTicks),
                    new XAttribute("totalTimestampTicks", TotalTimestampTicks), new XAttribute("callbackTimestampTicks", CallbackTimestampTicks),
                    new XAttribute("gc0", Gc0), new XAttribute("gc1", Gc1), new XAttribute("gc2", Gc2));
                if (AllocatedBytes.HasValue)
                {
                    element.Add(new XAttribute("allocatedBytes", AllocatedBytes.Value));
                }

                return element;
            }
        }
    }
}
#endif
