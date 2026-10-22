// Author: 0Shard
// Description: Explicitly opted-in, isolated command-line game host for native synthetic validation.
#if DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Eternal.InGameTests.Performance;

namespace Eternal.InGameTests.Helpers
{
    [StaticConstructorOnStartup]
    internal static class DevelopmentGameHost
    {
        private static string mode;
        private static string configuration;
        private static string output;
        private static string persistence;
        private static string resumeSave;
        private static string checkpoint;
        private static int setupSeed;
        private static float planetCoverage;
        private static bool requested;
        private static bool ready;
        private static bool running;
        private static bool complete;
        private static NativeUiProbe uiProbe;
        private static XElement pendingSmokeReport;
        private static IEnumerator<XElement> benchmarkEnumerator;
        private static XElement pendingBenchmarkReport;

        static DevelopmentGameHost()
        {
            if (!GenCommandLine.TryGetCommandLineArg("eternal-test-mode", out mode)) { return; }
            try
            {
                string session;
                string data;
                if (!GenCommandLine.TryGetCommandLineArg("eternal-test-config", out configuration)
                    || !GenCommandLine.TryGetCommandLineArg("eternal-test-output", out output)
                    || !GenCommandLine.TryGetCommandLineArg("eternal-test-session", out session)
                    || !GenCommandLine.TryGetCommandLineArg("savedatafolder", out data))
                {
                    throw new InvalidOperationException("All isolated Eternal test-host arguments are required");
                }
                session = Path.GetFullPath(session).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                output = Path.GetFullPath(output);
                if (session.Length < 5 || !Directory.Exists(session)
                    || !EqualPath(data, Path.Combine(session, "savedata"))
                    || !output.StartsWith(session + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || !EqualPath(GenFilePaths.SaveDataFolderPath, data))
                {
                    throw new InvalidOperationException("Test host requires its own session/data folder and contained report directory");
                }
                if (mode != "benchmark" && mode != "failure" && mode != "smoke")
                {
                    throw new InvalidOperationException("Unknown Eternal test-host mode");
                }
                if (GenCommandLine.TryGetCommandLineArg("eternal-test-persistence", out persistence))
                {
                    if (mode != "smoke" || (persistence != "create" && persistence != "verify"))
                    {
                        throw new InvalidOperationException("Persistence fixtures require smoke/create or smoke/verify");
                    }
                    if (persistence == "verify")
                    {
                        if (!GenCommandLine.TryGetCommandLineArg("eternal-test-resume", out resumeSave)
                            || !GenCommandLine.TryGetCommandLineArg("eternal-test-checkpoint", out checkpoint)
                            || !InsideSession(resumeSave, session) || !InsideSession(checkpoint, session)
                            || !File.Exists(resumeSave) || !File.Exists(checkpoint))
                        {
                            throw new InvalidOperationException("Persistence verification needs contained copied fixture files");
                        }
                    }
                }
                configuration = Path.GetFullPath(configuration);
                setupSeed = PerformanceConfiguration.Load(configuration).Seed;
                var automation = XDocument.Load(configuration).Root?.Element("automation");
                if (!float.TryParse((string)automation?.Attribute("planetCoverage"), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out planetCoverage) || planetCoverage <= 0f || planetCoverage > 1f
                    || float.IsNaN(planetCoverage))
                {
                    throw new InvalidOperationException("Native host needs configured planetCoverage in (0,1]");
                }
                Directory.CreateDirectory(output);
                requested = true;
                new Harmony("Eternal.Tests.SeededSetup").Patch(
                    AccessTools.Method(typeof(Game), nameof(Game.InitNewGame)),
                    prefix: new HarmonyMethod(typeof(DevelopmentGameHost), nameof(BeforeNewGame)),
                    finalizer: new HarmonyMethod(typeof(DevelopmentGameHost), nameof(AfterNewGame)));
                new Harmony("Eternal.Tests.SeededSetup").Patch(
                    AccessTools.Method(typeof(TickManager), nameof(TickManager.TickManagerUpdate)),
                    prefix: new HarmonyMethod(typeof(DevelopmentGameHost), nameof(BeforeAutomaticTicks)));
                // Startup constructors run before the queued interface initialization.
                // Queue behind that initialization so native world generation has a SoundRoot.
                LongEventHandler.QueueLongEvent(StartNewGame, "GeneratingMap", doAsynchronously: false, null);
            }
            catch (Exception exception)
            {
                Log.Error("[EternalGameHost] Invalid isolated host request: " + exception);
            }
        }

        private static bool EqualPath(string first, string second) => string.Equals(
            Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

        private static bool InsideSession(string path, string session) =>
            Path.GetFullPath(path).StartsWith(session + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        private static void StartNewGame()
        {
            if (!requested || Current.Game != null) { return; }
            try
            {
                Log.Message("[EternalGameHost] Preparing disposable native game: " + mode);
                Prefs.DevMode = true;
                if (persistence == "verify")
                {
                    GameDataSaveLoader.LoadGame(new FileInfo(resumeSave));
                    return;
                }
                var config = PerformanceConfiguration.Load(configuration);
                Rand.PushState(config.Seed);
                try
                {
                    // The public quick-setup path uses grammar for its seed and leaves queued
                    // initialization unseeded. These equivalent calls make fixture inputs explicit.
                    Current.ProgramState = ProgramState.Entry;
                    Game.ClearCaches();
                    Current.Game = new Game { InitData = new GameInitData(), Scenario = ScenarioDefOf.Crashlanded.scenario };
                    Find.Scenario.PreConfigure();
                    Current.Game.storyteller = new Storyteller(StorytellerDefOf.Cassandra, DifficultyDefOf.Rough);
                    Current.Game.World = WorldGenerator.GenerateWorld(planetCoverage,
                        "Eternal-" + config.Seed.ToString(CultureInfo.InvariantCulture), OverallRainfall.Normal,
                        OverallTemperature.Normal, OverallPopulation.Normal, LandmarkDensity.Normal);
                    Find.GameInitData.ChooseRandomStartingTile();
                    Find.Scenario.PostIdeoChosen();
                    Find.GameInitData.mapSize = config.Profiles.Min(profile => profile.MapSize);
                    Find.GameInitData.PrepForMapGen();
                    Find.Scenario.PreMapGenerate();
                    LongEventHandler.QueueLongEvent(() => { }, "Play", "GeneratingMap", doAsynchronously: false,
                        GameAndMapInitExceptionHandlers.ErrorWhileGeneratingMap);
                }
                finally { Rand.PopState(); }
            }
            catch (Exception exception) { Finish(null, exception); }
        }

        internal static void StartedNewGame()
        {
            if (requested)
            {
                Find.TickManager.Pause();
                ready = true;
            }
        }

        private static void BeforeNewGame(out bool __state)
        {
            __state = requested;
            if (__state) { Rand.PushState(setupSeed); }
        }

        private static bool BeforeAutomaticTicks()
        {
            // The host owns its explicit tick/frame schedule. Native render frames must not
            // consume ambient RNG or IDs while waiting for the test scene's fade to finish.
            return !requested;
        }

        private static Exception AfterNewGame(bool __state, Exception __exception)
        {
            if (__state) { Rand.PopState(); }
            return __exception;
        }

        private static XElement RunFailureSuites(Verse.Map map)
        {
            var requestedSuites = ((string)XDocument.Load(configuration).Root?.Element("automation")?.Attribute("failureSuites"))
                ?.Split(',');
            var suites = new List<(string Name, TestSuiteResult Result)>();
            var fixtures = new[]
            {
                "MapRemovalFailure", "CaravanDeath", "RecoverySiteFailure", "SoloDeathFailure",
                "DebtPersistenceFailure", "AuditHealingTimeFailure", "ResurrectionOutcomeFailure", "AuditSettingsFailure"
            };
            TestAssert.IsTrue(requestedSuites == null || requestedSuites.All(fixtures.Contains),
                "Requested failure suites must be registered fixture identities");
            foreach (string name in fixtures.Where(name => requestedSuites == null || requestedSuites.Contains(name)))
            {
                suites.Add((name, name == "CaravanDeath" ? TestSuites.CaravanDeathTests.RunAll(map)
                    : InvokeFixture<TestSuiteResult>(name + "Tests", "RunAll", map)));
            }
            var report = new XElement("failureSuites", suites.Select(suite => SuiteResult(suite.Name, suite.Result)));
            report.Save(Path.Combine(output, "failure.xml"));
            foreach (var suite in suites)
            {
                TestAssert.AreEqual(0, suite.Result.Failed, suite.Name + " failures must pass");
                TestAssert.AreEqual(0, suite.Result.Unavailable, suite.Name + " required cases must be available");
            }
            return report;
        }

        internal static void Update()
        {
            if (!requested || !ready || running || complete || Current.Game == null) { return; }
            if (uiProbe != null)
            {
                PollUiProbe();
                return;
            }
            if (benchmarkEnumerator != null)
            {
                // Standard-window loading events allow UI updates but still pause native ticks.
                if (LongEventHandler.ForcePause)
                {
                    return;
                }
                PollBenchmark();
                return;
            }
            if (LongEventHandler.ForcePause || !Current.Game.PlayerHasControl) { return; }
            running = true;
            try
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                if (persistence == "verify")
                {
                    Finish(InvokePersistence("Verify", checkpoint), null);
                }
                else if (persistence == "create")
                {
                    ClearOwnedStartupPopulation();
                    Finish(InvokePersistence("Create", configuration, output), null);
                }
                else if (mode == "benchmark")
                {
                    ClearOwnedStartupPopulation();
                    benchmarkEnumerator = PerformanceSuite.RunAll(configuration, output).GetEnumerator();
                }
                else
                {
                    ClearOwnedStartupPopulation();
                    using (var maps = new TestMapScope())
                    {
                        var map = maps.Create(75, home: true);
                        Current.Game.CurrentMap = map;
                        if (mode == "failure")
                        {
                            Finish(RunFailureSuites(map), null);
                        }
                        else
                        {
                            // The existing runner drives real hooks and removes only its owned fixtures.
                            var entry = typeof(EternalTestRunner).GetMethod("RunAllTestsWithResults", BindingFlags.Static | BindingFlags.Public);
                            TestAssert.IsNotNull(entry, "Structured native smoke entry point");
                            var suites = (IReadOnlyList<(string Name, TestSuiteResult Result)>)entry.Invoke(null, null);
                            var report = new XElement("smoke", suites.Select(suite => SuiteResult(suite.Name, suite.Result)));
                            report.Save(Path.Combine(output, "smoke.xml"));
                            TestAssert.IsTrue(suites.Count > 0, "Native smoke suites must execute");
                            TestAssert.AreEqual(0, suites.Sum(suite => suite.Result.Failed), "Native smoke failures must pass");
                            pendingSmokeReport = report;
                        }
                    }
                    if (pendingSmokeReport != null && !complete)
                    {
                        uiProbe = new NativeUiProbe();
                        uiProbe.Start();
                    }
                }
            }
            catch (Exception exception)
            {
                uiProbe?.Dispose();
                uiProbe = null;
                Finish(null, exception is TargetInvocationException invocation && invocation.InnerException != null
                    ? invocation.InnerException : exception);
            }
            finally { running = false; }
        }

        private static void PollBenchmark()
        {
            running = true;
            try
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                // One MoveNext per native Update gives deferred engine cleanup a frame between profiles.
                if (benchmarkEnumerator.MoveNext())
                {
                    pendingBenchmarkReport = benchmarkEnumerator.Current;
                    if ((bool?)pendingBenchmarkReport.Attribute("complete") == true
                        && (bool?)pendingBenchmarkReport.Attribute("passed") == true)
                    {
                        Finish(pendingBenchmarkReport, null);
                    }
                }
                else
                {
                    TestAssert.IsTrue((bool?)pendingBenchmarkReport?.Attribute("complete") == true
                        && (bool?)pendingBenchmarkReport?.Attribute("passed") == true,
                        "Benchmark iterator must finish with a complete successful report");
                    Finish(pendingBenchmarkReport, null);
                }
            }
            catch (Exception exception) { Finish(pendingBenchmarkReport, exception); }
            finally { running = false; }
        }

        private static void ReleaseBenchmark()
        {
            var iterator = benchmarkEnumerator;
            benchmarkEnumerator = null;
            pendingBenchmarkReport = null;
            iterator?.Dispose();
        }

        private static void PollUiProbe()
        {
            running = true;
            try
            {
                var report = uiProbe.Poll();
                if (report == null) { return; }
                uiProbe = null;
                report.Save(Path.Combine(output, "native-ui.xml"));
                pendingSmokeReport.Add(report);
                pendingSmokeReport.Save(Path.Combine(output, "smoke.xml"));
                TestAssert.IsTrue((bool)report.Attribute("success"), "Native Eternal UI surfaces must render successfully");
                Finish(pendingSmokeReport, null);
            }
            catch (Exception exception)
            {
                uiProbe?.Dispose();
                uiProbe = null;
                Finish(pendingSmokeReport, exception);
            }
            finally { running = false; }
        }

        private static XElement SuiteResult(string name, TestSuiteResult result) => new XElement("nativeSuite",
            new XAttribute("name", name), new XAttribute("passed", result.Passed),
            new XAttribute("failed", result.Failed), new XAttribute("unavailable", result.Unavailable),
            new XAttribute("elapsedMs", result.ElapsedMs), result.Failures.Select(failure => new XElement("failure", failure)));

        private static XElement InvokePersistence(string methodName, params string[] arguments)
            => InvokeFixture<XElement>("PersistenceFixtureTests", methodName, arguments.Cast<object>().ToArray());

        private static T InvokeFixture<T>(string typeName, string methodName, params object[] arguments)
        {
            var type = typeof(Eternal_Component).Assembly.GetType(
                "Eternal.InGameTests.FailureScenarios." + typeName, throwOnError: true);
            var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public);
            TestAssert.IsNotNull(method, "Native fixture entry point " + typeName + "." + methodName);
            return (T)method.Invoke(null, arguments);
        }

        private static void ClearOwnedStartupPopulation()
        {
            // This freshly generated session owns all startup objects; no saved or ordinary game can enter this host.
            foreach (var map in Find.Maps.ToArray())
            {
                foreach (var pawn in map.mapPawns.AllPawnsSpawned.ToArray())
                {
                    TestPawnFactory.OwnThing(pawn);
                }
                foreach (var corpse in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).ToArray())
                {
                    TestPawnFactory.OwnThing(corpse);
                }
            }
            TestPawnFactory.CleanupAll();
            foreach (var map in Find.Maps.ToArray())
            {
                var parent = map.Parent;
                Current.Game.DeinitAndRemoveMap(map, false);
                if (parent != null && parent.Spawned) { Find.WorldObjects.Remove(parent); }
            }
            TestAssert.AreEqual(0, Find.Maps.Count, "Disposable seed maps must be removed before scenario generation");
        }

        private static void Finish(XElement result, Exception exception)
        {
            if (complete) { return; }
            complete = true;
            try
            {
                var report = new XElement("gameHost", new XAttribute("mode", mode ?? "unknown"),
                    new XAttribute("success", exception == null),
                    new XAttribute("gameVersion", VersionControl.CurrentVersionString),
                    new XAttribute("framework", Environment.Version.ToString()),
                    new XAttribute("timestamp", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)));
                if (result != null) { report.Add(result); }
                if (exception != null) { report.Add(new XElement("failure", exception.ToString())); }
                report.Save(Path.Combine(output, "game-host-result.xml"));
                Log.Message("[EternalGameHost] COMPLETE success=" + (exception == null));
                if (exception != null) { Log.Error("[EternalGameHost] Failure: " + exception); }
            }
            finally
            {
                try { ReleaseBenchmark(); }
                finally
                {
                    // A single isolated game process exits; the normal game and Steam session remain independent.
                    Root.Shutdown();
                }
            }
        }
    }

    public sealed class DevelopmentGameHostComponent : GameComponent
    {
        public DevelopmentGameHostComponent(Game game) { }
        public override void StartedNewGame() { DevelopmentGameHost.StartedNewGame(); }
        public override void LoadedGame() { DevelopmentGameHost.StartedNewGame(); }
        public override void GameComponentUpdate() { DevelopmentGameHost.Update(); }
    }
}
#endif
