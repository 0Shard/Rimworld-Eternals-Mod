// Author: 0Shard
// Description: Real isolated native saves and fresh-process continuation checks by checkpoint identities.
#if DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Eternal.Components;
using Eternal.DI;
using Eternal.Extensions;
using Eternal.Resources;
using Eternal.InGameTests.Helpers;
using Eternal.InGameTests.Performance;
using HarmonyLib;
using RimWorld;
using Verse;
using CorpseType = Verse.Corpse;

namespace Eternal.InGameTests.FailureScenarios
{
    [StaticConstructorOnStartup]
    public static class PersistenceFixtureTests
    {
        private const string WaiverName = "Eternal_Debug_PersistenceFoodWaiver";
        private static readonly string[] CommonHediffDefinitions = { "Flu", "WoundInfection" };
        private const string UnresolvedHediffDefinition = "Eternal_Debug_AbsentDiseaseForPersistence";
        private static readonly HashSet<string> FloatSettingFields = new HashSet<string>(typeof(Eternal_Settings)
            .GetFields(BindingFlags.Public | BindingFlags.Instance).Where(field => field.FieldType == typeof(float)).Select(field => field.Name),
            StringComparer.Ordinal);
        private static TestMapScope creationScope;

        static PersistenceFixtureTests()
        {
            // Definitions must exist before Scribe resolves a save in every fresh Debug process.
            if (DefDatabase<MapGeneratorDef>.GetNamedSilentFail("Eternal_Debug_EmptyMap") == null)
            {
                DefDatabase<MapGeneratorDef>.Add(new MapGeneratorDef
                { defName = "Eternal_Debug_EmptyMap", genSteps = new List<GenStepDef>() });
            }

            if (DefDatabase<HediffDef>.GetNamedSilentFail(WaiverName) == null)
            {
                DefDatabase<HediffDef>.Add(new HediffDef
                {
                    defName = WaiverName,
                    label = "synthetic food waiver",
                    hediffClass = typeof(Hediff),
                    stages = new List<HediffStage> { new HediffStage { disablesNeeds = new List<NeedDef> { NeedDefOf.Food } } }
                });
            }
        }

        public static XElement Create(string configPath, string reportDirectory)
        {
            RequireIsolatedHost(reportDirectory, "create");
            TestAssert.IsNull(creationScope, "Persistence creation may run once per disposable process");
            TestAssert.IsFalse(Find.Maps.Any(m => m.mapPawns.AllPawnsSpawned.Count > 0
                || m.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).Count > 0), "Creation requires empty disposable seed maps");
            var config = PerformanceConfiguration.Load(configPath);
            var options = LoadOptions(configPath);
            Directory.CreateDirectory(reportDirectory);
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            Eternal_Mod.settings.moodBuffValue = options.MoodBuffValue;
            PrepareCommonHediffSettings();
            LoadedModManager.GetMod<Eternal_Mod>().WriteSettings();
            var scope = new TestMapScope();
            try
            {
                var actors = new List<PerformancePawnState>();
                var kinds = new[] { "regrowth-phase-0", "regrowth-phase-1", "regrowth-phase-2", "regrowth-phase-3",
                    "corpse-waiting", "corpse-active", "food-floor", "food-waiver", "healthy", "casket-debt", "terminal-dust" };
                Rand.PushState(config.Seed);
                try
                {
                    foreach (var kind in kinds)
                    {
                        var map = scope.Create(options.MapSize, true, false);
                        var pawn = scope.SpawnEternalPawn(map);
                        PerformancePawnState actor;
                        if (kind == "casket-debt" || kind == "terminal-dust")
                        {
                            PerformanceStatePalette.Prepare(pawn, map, 0, config);
                            var debt = EternalServiceContainer.Instance.FoodDebtSystem;
                            debt.ClearDebt(pawn);
                            TestAssert.IsTrue(debt.AddResurrectionDebt(pawn, kind == "casket-debt" ? 0.1f : 1e-9f), "Saved exact episode must exist");
                            if (kind == "casket-debt")
                            {
                                var casket = (Building_CryptosleepCasket)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("CryptosleepCasket"));
                                TestPawnFactory.OwnThing(casket);
                                GenSpawn.Spawn(casket, map.Center, map);
                                pawn.DeSpawn();
                                TestAssert.IsTrue(casket.TryAcceptThing(pawn, false) && pawn.Suspended, "Saved fixture requires an actual suspended owner");
                            }
                            actor = new PerformancePawnState(pawn, map, kind, false);
                        }
                        else if (kind == "food-waiver")
                        {
                            PerformanceStatePalette.Prepare(pawn, map, 0, config);
                            pawn.health.AddHediff(HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed(WaiverName), pawn));
                            TestAssert.IsTrue(pawn.HasFoodNeedDisabled(), "Registered saved waiver must disable the native food need");
                            TestAssert.IsTrue(EternalServiceContainer.Instance.FoodDebtSystem.AddDebt(pawn, 1f), "Saved waiver cost must succeed");
                            actor = new PerformancePawnState(pawn, map, kind, false);
                        }
                        else
                        {
                            actor = PerformanceStatePalette.Prepare(pawn, map, Array.IndexOf(PerformanceStatePalette.Kinds, kind), config);
                        }
                        actors.Add(actor);
                        if (kind == "food-floor" || kind == "corpse-active")
                        {
                            TestAssert.IsTrue(EternalServiceContainer.Instance.FoodDebtSystem.AddResurrectionDebt(pawn, 0.25f),
                                "Persistence must include a real nonzero resurrection baseline and peak");
                        }

                        AssertActorPopulation(scope, actor);
                    }
                }
                finally { Rand.PopState(); }
                // Drain real deferred input reconciliation once before capturing the saved boundary.
                var speed = Find.TickManager.CurTimeSpeed;
                try { Find.TickManager.CurTimeSpeed = TimeSpeed.Normal; Find.TickManager.DoSingleTick(); }
                finally { Find.TickManager.CurTimeSpeed = speed; }
                var checkpoint = SaveCheckpoint(actors, config.FrameSteps, options.ContinuationTicks, config.Seed, reportDirectory, "persistence-checkpoint.xml");
                creationScope = scope; // Retain fixtures and their gates until the dedicated process exits.
                return Result("create", checkpoint, reportDirectory, actors.Count, false);
            }
            catch (Exception observationFailure)
            {
                try { scope.Dispose(); }
                catch (Exception cleanupFailure) { throw new AggregateException("Persistence creation and cleanup failed", observationFailure, cleanupFailure); }
                throw;
            }
        }

        public static XElement Verify(string checkpointPath)
        {
            RequireIsolatedHost(checkpointPath, "verify");
            var expected = XDocument.Load(checkpointPath).Root;
            TestAssert.IsTrue(expected?.Name == "persistenceCheckpoint" && (string)expected.Attribute("schemaVersion") == "1",
                "Expected a version-1 synthetic persistence checkpoint");
            string reportDirectory = Path.GetDirectoryName(Path.GetFullPath(checkpointPath));
            string saveName = Required(expected, "saveName");
            TestAssert.IsTrue(saveName.StartsWith("EternalSynthetic-", StringComparison.Ordinal)
                && Path.GetFileName(saveName) == saveName, "Only named synthetic saves may be verified");
            string sourcePath = GenFilePaths.FilePathForSavedGame(saveName);
            TestAssert.AreEqual(Required(expected, "sourceSaveSha256"), HashFile(sourcePath), "Loaded source must match the exact checkpoint save");
            TestAssert.AreEqual((int)expected.Attribute("T"), Find.TickManager.TicksGame, "Fresh loaded fixture must begin at its saved tick");
            var settingsFileVerification = VerifySettingsFiles(expected.Element("settingsFiles"));
            EqualXml(expected.Element("commonHediffSettings"), CommonHediffSettingsSnapshot(), "Exact persisted common per-hediff overrides");
            EqualXml(expected.Element("settings"), SettingsSnapshot(), "Persisted native mod settings");
            EqualXml(expected.Element("timing"), TimingSnapshot(), "Persisted production cadence stamps");
            var actors = ResolveActors(expected);
            var scope = new TestMapScope();
            Exception verificationFailure = null;
            XElement verificationResult = null;
            var savedSpeed = Find.TickManager.CurTimeSpeed;
            try
            {
                foreach (var actor in actors)
                {
                    scope.OwnLoadedPersistenceMap(actor.Map, actor.Pawn, actor.Map.uniqueID, actor.Pawn.thingIDNumber);
                    AssertActorPopulation(scope, actor);
                    var saved = expected.Element("actors").Elements("actor").Single(x => (int)x.Attribute("id") == actor.Pawn.thingIDNumber);
                    EqualXml(saved, ActorSnapshot(actor), "Loaded actor " + actor.Kind + "#" + actor.Pawn.thingIDNumber);
                    AssertManagerOwnership(actor);
                }
                var before = actors.ToDictionary(a => a.Pawn.thingIDNumber, ActorSnapshot);
                var steps = Required(expected, "frameSteps").Split(',').Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                int ticks = (int)expected.Attribute("continuationTicks");
                int continuationSeed = int.Parse(Required(expected, "continuationSeed"), CultureInfo.InvariantCulture);
                TestAssert.IsTrue(ticks > 0 && steps.Any(s => s > 0) && steps.All(s => s >= 0), "Valid continuation schedule");
                int nativeTicks = 0, frames = 0, normalPasses = 0, rarePasses = 0;
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                var beforeActors = new XElement("beforeActors", actors.Select(actor => new XElement(before[actor.Pawn.thingIDNumber])));
                var transitions = new XElement("transitions");
                XElement continuationStart;
                XElement continuationEnd;
                Rand.PushState(continuationSeed);
                try
                {
                    continuationStart = ContinuationFingerprint("continuationStart");
                    while (nativeTicks < ticks)
                    {
                        int frameStartT = Find.TickManager.TicksGame;
                        var oldTiming = TimingSnapshot();
                        int advance = Math.Min(steps[frames % steps.Length], ticks - nativeTicks);
                        for (int tick = 0; tick < advance; tick++)
                        {
                            Find.TickManager.DoSingleTick();
                        }

                        nativeTicks += advance;
                        NativePauseDiagnostics.RequireRunnable("persistence-frame", frames);
                        var timing = TimingSnapshot();
                        int frameNormalPasses = CheckCadence(oldTiming, timing, "normal", Eternal_Mod.settings.normalTickRate);
                        int frameRarePasses = CheckCadence(oldTiming, timing, "rare", Eternal_Mod.settings.rareTickRate);
                        normalPasses += frameNormalPasses;
                        rarePasses += frameRarePasses;
                        var debt = actors.ToDictionary(a => a.Pawn.thingIDNumber, a => DebtSnapshot(a.Pawn));
                        Eternal_Component.Current.GameComponentTick(); // Same T must not heal or charge twice.
                        Eternal_Component.Current.GameComponentUpdate(); // Frames alone never do work.
                        EqualXml(timing, TimingSnapshot(), "Repeated-T callbacks preserve cadence stamps");
                        foreach (var actor in actors)
                        {
                            EqualXml(debt[actor.Pawn.thingIDNumber], DebtSnapshot(actor.Pawn), "Repeated-T callbacks must not duplicate debt/baseline/peak");
                        }

                        TestAssert.AreEqual(advance, Find.TickManager.TicksGame - frameStartT, "Only explicit continuation ticks may advance the native clock");
                        transitions.Add(new XElement("frame", new XAttribute("index", frames),
                            new XAttribute("scheduleIndex", frames % steps.Length), new XAttribute("requestedNativeTicks", steps[frames % steps.Length]),
                            new XAttribute("startT", frameStartT), new XAttribute("endT", Find.TickManager.TicksGame),
                            new XAttribute("nativeTicks", advance), new XAttribute("normalPasses", frameNormalPasses),
                            new XAttribute("rarePasses", frameRarePasses), new XElement(timing),
                            new XElement("actors", actors.Select(ActorSnapshot))));
                        frames++;
                    }
                    continuationEnd = ContinuationFingerprint("continuationEnd");
                }
                finally { Rand.PopState(); }
                TestAssert.IsTrue(normalPasses > 0 && rarePasses > 0, "Fresh-process continuation must execute actual normal and rare production passes");
                // Ejection runs native sickness/jobs and consumes RNG. Give these post-trace
                // stimuli an explicit seed too, so frame comparisons share identical inputs.
                Rand.PushState(continuationSeed + 1);
                try
                {
                    foreach (var actor in actors)
                    {
                        AssertActorPopulation(scope, actor);
                        AssertContinuation(actor, before[actor.Pawn.thingIDNumber]);
                        AssertManagerOwnership(actor);
                    }
                }
                finally { Rand.PopState(); }
                var afterActors = new XElement("afterActors", actors.Select(ActorSnapshot));
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                var next = SaveCheckpoint(actors, steps, ticks, continuationSeed, reportDirectory, "persistence-next-checkpoint.xml");
                var result = Result("verify", next, reportDirectory, actors.Count, true);
                result.Add(new XAttribute("verifiedSourceSave", saveName), new XAttribute("nativeTicks", nativeTicks),
                    new XAttribute("frames", frames), new XAttribute("normalPasses", normalPasses), new XAttribute("rarePasses", rarePasses),
                    new XAttribute("continuationSeed", continuationSeed), new XAttribute("verifiedSourceSha256", Required(expected, "sourceSaveSha256")),
                    settingsFileVerification, CommonHediffSettingsSnapshot(), continuationStart, beforeActors, transitions, afterActors, continuationEnd);
                verificationResult = result;
                return result;
            }
            catch (Exception ex) { verificationFailure = ex; throw; }
            finally
            {
                Find.TickManager.CurTimeSpeed = savedSpeed;
                try { scope.Dispose(); }
                catch (Exception cleanupFailure)
                {
                    if (verificationFailure != null)
                    {
                        throw new AggregateException("Persistence verification and owned cleanup failed", verificationFailure, cleanupFailure);
                    }

                    throw;
                }
                if (verificationResult != null)
                {
                    verificationResult.Add(new XAttribute("cleanupPassed", true));
                    verificationResult.Save(Path.Combine(reportDirectory, "persistence-verification.xml"));
                }
            }
        }

        private static List<PerformancePawnState> ResolveActors(XElement checkpoint)
        {
            var result = new List<PerformancePawnState>();
            var rows = checkpoint.Element("actors")?.Elements("actor").ToArray();
            TestAssert.IsTrue(rows != null && (rows.Length == 9 || rows.Length == 11)
                && rows.Select(r => (int)r.Attribute("id")).Distinct().Count() == rows.Length,
                "Checkpoint must identify every distinct synthetic actor (legacy nine or extended eleven)");
            foreach (var row in rows)
            {
                int mapId = (int)row.Attribute("mapId"), pawnId = (int)row.Attribute("id");
                var map = Find.Maps.SingleOrDefault(m => m.uniqueID == mapId);
                TestAssert.IsNotNull(map, "Saved owned map " + mapId);
                var candidates = map.mapPawns.AllPawnsSpawned.Concat(map.listerThings.AllThings.OfType<Building_Casket>()
                    .SelectMany(casket => casket.GetDirectlyHeldThings().OfType<Pawn>())).Concat(map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse)
                    .OfType<CorpseType>().Select(c => c.InnerPawn)).Where(p => p != null && p.thingIDNumber == pawnId).ToArray();
                TestAssert.AreEqual(1, candidates.Length, "Actor must resolve from its saved physical map or its own corpse");
                TestAssert.AreEqual((int)row.Attribute("parentId"), map.Parent.ID, "Saved map parent identity");
                result.Add(new PerformancePawnState(candidates[0], map, Required(row, "kind"), false));
            }
            return result;
        }

        private static void AssertActorPopulation(TestMapScope scope, PerformancePawnState actor)
        {
            if (actor.Kind != "casket-debt" || !actor.Pawn.Suspended)
            {
                scope.AssertPopulation(actor.Map, actor.Pawn, actor.Kind);
                return;
            }
            var caskets = actor.Map.listerThings.AllThings.OfType<Building_CryptosleepCasket>().ToArray();
            TestAssert.AreEqual(1, caskets.Length, "Suspended fixture has exactly one native casket");
            TestAssert.IsTrue(ReferenceEquals(actor.Pawn.MapHeld, actor.Map) && ReferenceEquals(actor.Pawn.ParentHolder, caskets[0]),
                "Suspended fixture retains its actual map and casket owner");
            TestAssert.AreEqual(1, caskets[0].GetDirectlyHeldThings().Count, "Casket contains only its assigned pawn");
            TestAssert.AreEqual(0, actor.Map.mapPawns.AllPawnsSpawned.Count, "Suspended fixture has no extra spawned pawn");
            TestAssert.AreEqual(0, actor.Map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).Count,
                "Suspended fixture has no unexpected corpse");
        }

        private static XElement SaveCheckpoint(List<PerformancePawnState> actors, IReadOnlyList<int> steps, int ticks, int continuationSeed,
            string reportDirectory, string checkpointFile)
        {
            LoadedModManager.GetMod<Eternal_Mod>().WriteSettings();
            string saveName = "EternalSynthetic-" + Guid.NewGuid().ToString("N");
            GameDataSaveLoader.SaveGame(saveName);
            string savePath = GenFilePaths.FilePathForSavedGame(saveName);
            TestAssert.IsTrue(File.Exists(savePath) && new FileInfo(savePath).Length > 0, "Actual native save must exist");
            TestAssert.IsTrue(XDocument.Load(savePath).Root?.Name == "savegame", "Native save must be complete parseable XML");
            var checkpoint = new XElement("persistenceCheckpoint", new XAttribute("schemaVersion", "1"),
                new XAttribute("saveName", saveName), new XAttribute("saveFileName", saveName + ".rws"), new XAttribute("savePath", savePath),
                new XAttribute("sourceSaveSha256", HashFile(savePath)), new XAttribute("T", Find.TickManager.TicksGame),
                new XAttribute("continuationTicks", ticks), new XAttribute("continuationSeed", continuationSeed), new XAttribute("frameSteps", string.Join(",", steps)),
                new XAttribute("gameVersion", VersionControl.CurrentVersionString), SettingsFilesSnapshot(), SettingsSnapshot(), CommonHediffSettingsSnapshot(), TimingSnapshot(),
                new XElement("actors", actors.Select(ActorSnapshot)));
            checkpoint.Save(Path.Combine(reportDirectory, checkpointFile));
            checkpoint.Add(new XAttribute("checkpointPath", Path.Combine(reportDirectory, checkpointFile)));
            return checkpoint;
        }

        private static XElement ContinuationFingerprint(string elementName)
        {
            var ids = Find.UniqueIDsManager;
            return new XElement(elementName, new XAttribute("T", Find.TickManager.TicksGame),
                new XAttribute("absoluteT", GenTicks.TicksAbs), new XAttribute("worldSeed", Find.World.info.seedString),
                new XElement("random", new XAttribute("seed", AccessTools.Field(typeof(Rand), "seed").GetValue(null)),
                    new XAttribute("iterations", AccessTools.Field(typeof(Rand), "iterations").GetValue(null))),
                new XElement("allocators", ids.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
                    .Where(field => field.FieldType.IsPrimitive).OrderBy(field => field.Name, StringComparer.Ordinal)
                    .Select(field => new XElement("counter", new XAttribute("name", field.Name), new XAttribute("value", field.GetValue(ids))))));
        }

        private static XElement ActorSnapshot(PerformancePawnState actor)
        {
            var pawn = actor.Pawn;
            var manager = EternalServiceContainer.Instance.CorpseManager;
            var entry = manager.GetCorpseData(pawn);
            var result = new XElement("actor", new XAttribute("id", pawn.thingIDNumber), new XAttribute("mapId", actor.Map.uniqueID),
                new XAttribute("parentId", actor.Map.Parent.ID), new XAttribute("kind", actor.Kind), new XAttribute("dead", pawn.Dead),
                new XAttribute("nativeHome", actor.Map.IsPlayerHome), new XAttribute("foodWaived", pawn.HasFoodNeedDisabled()),
                new XAttribute("suspended", pawn.Suspended),
                new XAttribute("food", pawn.needs?.food?.CurLevel.ToString("R", CultureInfo.InvariantCulture) ?? "disabled"),
                DebtSnapshot(pawn), new XElement("health", pawn.health.hediffSet.hediffs.OrderBy(h => h.loadID).Select(h =>
                {
                    var regrowth = h as EternalRegrowing_Hediff;
                    var row = new XElement("hediff", new XAttribute("loadId", h.loadID), new XAttribute("def", h.def.defName),
                        new XAttribute("severity", h.Severity), new XAttribute("part", PartIdentity(pawn, h.Part)));
                    if (regrowth != null)
                    {
                        row.Add(new XAttribute("forPart", PartIdentity(pawn, regrowth.forPart)),
                        new XAttribute("partMaxHp", regrowth.partMaxHp), new XAttribute("phase", Math.Min(3, (int)(regrowth.Severity * 4f))));
                    }

                    return row;
                })));
            var corpse = new XElement("corpse", new XAttribute("tracked", entry != null));
            if (entry != null)
            {
                corpse.Add(new XAttribute("id", entry.Corpse.thingIDNumber), new XAttribute("currentMapId", entry.CurrentMap?.uniqueID ?? -1),
                    new XAttribute("position", entry.Position), new XAttribute("active", entry.IsHealingActive), new XAttribute("deathT", entry.DeathTick),
                    new XAttribute("startT", entry.HealingStartTick), new XAttribute("progress", entry.HealingProgress), new XAttribute("foodDebt", entry.FoodDebt),
                    new XAttribute("totalCost", entry.TotalHealingCost), new XAttribute("hydrated", entry.HealingQueueHydrated),
                    QueueSnapshot("queue", entry.HealingQueue), QueueSnapshot("capturedQueue", entry.PreCalculatedHealingQueue));
            }

            result.Add(corpse);
            var anatomy = EternalServiceContainer.Instance.RjwAnatomy;
            result.Add(new XElement("capturedAnatomy", new XAttribute("available", anatomy?.IsAvailable ?? false),
                anatomy?.Snapshots.Where(s => ReferenceEquals(s.Pawn, pawn)).Select(s => new XElement("slot",
                    new XAttribute("body", s.Locator?.BodyDefName ?? "null"), new XAttribute("path", string.Join(",", s.Locator?.ChildIndices ?? new List<int>())),
                    new XAttribute("baseline", s.HasBaseline), new XAttribute("pending", s.Pending), new XAttribute("blocked", s.BlockedReason ?? ""),
                    s.Parts.Select(p => new XElement("part", new XAttribute("def", p.HediffDefName ?? ""),
                        new XAttribute("class", p.HediffClassName ?? ""), new XAttribute("originalLoadId", p.OriginalLoadId),
                        new XAttribute("attachedLoadId", p.AttachedLoadId)))))));
            return result;
        }

        private static XElement QueueSnapshot(string name, IEnumerable<HealingItem> queue)
        {
            return new XElement(name, new XAttribute("present", queue != null), queue?.Select(item => new XElement("item",
                new XAttribute("loadId", item.HediffLoadId), new XAttribute("def", item.HediffDefName ?? ""), new XAttribute("severity", item.Severity),
                new XAttribute("type", item.Type), new XAttribute("priority", item.HealingPriority), new XAttribute("time", item.EstimatedHealingTime),
                new XAttribute("energy", item.EnergyCost), new XAttribute("critical", item.IsCritical), new XAttribute("harmful", item.IsHarmful))));
        }
        private static XElement DebtSnapshot(Pawn pawn)
        {
            var debt = EternalServiceContainer.Instance.FoodDebtSystem;
            var baseline = (Dictionary<Pawn, NutritionAmount>)AccessTools.Field(debt.GetType(), "_resurrectionDebtBaseline").GetValue(debt);
            var exact = (Dictionary<Pawn, NutritionAmount>)AccessTools.Field(debt.GetType(), "_debtTracker").GetValue(debt);
            var peak = (Dictionary<Pawn, NutritionAmount>)AccessTools.Field(debt.GetType(), "_peakDebt").GetValue(debt);
            return new XElement("debt", new XAttribute("amount", debt.GetDebt(pawn)), new XAttribute("peak", debt.GetPeakDebt(pawn)),
                new XAttribute("baseline", baseline.TryGetValue(pawn, out var amount) ? amount.ToFloat() : 0f),
                new XAttribute("exactUnits", exact.TryGetValue(pawn, out amount) ? amount.Serialize() : "0"),
                new XAttribute("baselineUnits", baseline.TryGetValue(pawn, out amount) ? amount.Serialize() : "0"),
                new XAttribute("peakUnits", peak.TryGetValue(pawn, out amount) ? amount.Serialize() : "0"),
                new XAttribute("owners", debt.GetTrackedPawns().Count(p => ReferenceEquals(p, pawn))));
        }
        private static void AssertManagerOwnership(PerformancePawnState actor)
        {
            var manager = EternalServiceContainer.Instance.CorpseManager;
            var entry = manager.GetCorpseData(actor.Pawn);
            if (actor.Pawn.Dead)
            {
                TestAssert.IsNotNull(entry, "Dead saved actor must be owned by actual corpse manager");
                TestAssert.IsTrue(ReferenceEquals(entry.OriginalPawn, actor.Pawn) && ReferenceEquals(entry.Corpse, actor.Pawn.Corpse)
                    && ReferenceEquals(entry.CurrentMap, actor.Map), "Loaded corpse manager ownership must match the physical source");
                var component = entry.Corpse.GetComp<Eternal.Corpse.EternalCorpseComponent>();
                TestAssert.IsNotNull(component, "Loaded corpse component");
                TestAssert.IsTrue(ReferenceEquals(component.parent, entry.Corpse) && ReferenceEquals(component.CorpseData, entry),
                    "Loaded corpse component must bind the actual corpse and manager entry");
                foreach (var item in entry.HealingQueue.Where(i => i.Hediff != null))
                {
                    TestAssert.IsTrue(ReferenceEquals(item.Pawn, actor.Pawn) && actor.Pawn.health.hediffSet.hediffs.Contains(item.Hediff),
                        "Loaded active queue references must resolve into the actor's actual current health set");
                }
            }
            else
            {
                TestAssert.IsNull(entry, "Living saved actor must not retain a corpse entry");
            }
        }
        private static void AssertContinuation(PerformancePawnState actor, XElement before)
        {
            if (actor.Kind == "casket-debt")
            {
                TestAssert.IsTrue(actor.Pawn.Suspended, "Native casket suspension must survive Scribe/load");
                EqualXml(before.Element("debt"), DebtSnapshot(actor.Pawn), "Suspended exact debt/baseline/peak remain unchanged through native ticks");
                TestAssert.AreEqual((string)before.Attribute("food"), actor.Pawn.needs.food.CurLevel.ToString("R", CultureInfo.InvariantCulture),
                    "Suspended food remains bitwise unchanged through load and continuation");
                var casket = actor.Pawn.ParentHolder as Building_CryptosleepCasket;
                TestAssert.IsNotNull(casket, "Loaded pawn has its actual native casket owner");
                casket.EjectContents();
                AuditHealingTimeFailureTests.Advance(Eternal_Mod.settings.rareTickRate);
                TestAssert.IsTrue(EternalServiceContainer.Instance.FoodDebtSystem.GetDebt(actor.Pawn) < (float)before.Element("debt").Attribute("amount"),
                    "Loaded casket debt resumes after actual ejection");
            }
            if (actor.Kind == "terminal-dust")
            {
                var debt = EternalServiceContainer.Instance.FoodDebtSystem;
                TestAssert.AreEqual(0f, debt.GetDebt(actor.Pawn), "Loaded terminal dust closes through native repayment path");
                TestAssert.AreEqual(0f, debt.GetPeakDebt(actor.Pawn), "Loaded terminal closure resets peak");
                TestAssert.IsTrue(debt.AddDebt(actor.Pawn, 0.01f), "Next episode can accept an ordinary charge");
                TestAssert.AreEqual(0.01f, debt.GetPeakDebt(actor.Pawn), "Next episode has its own fresh peak");
            }
            if (actor.Kind.StartsWith("regrowth-phase-", StringComparison.Ordinal))
            {
                var prior = before.Element("health").Elements("hediff").Single(h => h.Attribute("forPart") != null);
                var part = actor.Pawn.RaceProps.body.AllParts.Single(p => PartIdentity(actor.Pawn, p) == (string)prior.Attribute("forPart"));
                var current = EternalServiceContainer.Instance.RegrowthManager.GetRegrowingHediffs(actor.Pawn).FirstOrDefault(h => h.forPart == part);
                TestAssert.IsTrue(!actor.Pawn.health.hediffSet.PartIsMissing(part) || current != null && current.Severity > (float)prior.Attribute("severity"),
                    "Loaded regrowth must progress through real rare passes");
            }
            if (actor.Kind == "corpse-waiting")
            {
                TestAssert.IsTrue(actor.Pawn.Dead, "Waiting saved corpse must await a player healing request");
            }

            if (actor.Kind == "healthy")
            {
                TestAssert.IsFalse(actor.Pawn.Dead, "Loaded healthy control must survive");
            }

            if (actor.Kind == "food-waiver")
            {
                TestAssert.IsTrue(actor.Pawn.HasFoodNeedDisabled(), "Saved registered waiver must survive native load");
                TestAssert.AreEqual(0f, EternalServiceContainer.Instance.FoodDebtSystem.GetDebt(actor.Pawn), "Waiver must retain zero cost");
            }
            if (actor.Kind == "food-floor")
            {
                TestAssert.AreEqual((float)before.Element("debt").Attribute("amount"), EternalServiceContainer.Instance.FoodDebtSystem.GetDebt(actor.Pawn),
                    "Food-floor debt must not repay below the configured floor");
            }

            if (actor.Kind == "corpse-active" && (bool)before.Attribute("dead"))
            {
                var after = ActorSnapshot(actor);
                TestAssert.IsTrue(!actor.Pawn.Dead || !PersistenceSnapshotComparer.AreEqual(before.Element("corpse"), after.Element("corpse"), FloatSettingFields)
                    || !PersistenceSnapshotComparer.AreEqual(before.Element("health"), after.Element("health"), FloatSettingFields), "Active saved corpse healing must continue or resurrect");
            }
        }
        private static XElement SettingsFilesSnapshot()
        {
            var mod = LoadedModManager.GetMod<Eternal_Mod>();
            string mainName = GenText.SanitizeFilename("Mod_" + mod.Content.FolderName + "_Eternal_Mod.xml");
            var names = new List<string> { mainName };
            if (File.Exists(Path.Combine(GenFilePaths.ConfigFolderPath, "Eternal_HediffSettings.xml")))
            {
                names.Add("Eternal_HediffSettings.xml");
            }

            return new XElement("settingsFiles", names.Select(name => new XElement("file", new XAttribute("name", name),
                new XAttribute("sha256", HashFile(Path.Combine(GenFilePaths.ConfigFolderPath, name))),
                new XAttribute("semanticSha256", SemanticSettingsHash(Path.Combine(GenFilePaths.ConfigFolderPath, name))))));
        }
        private static XElement VerifySettingsFiles(XElement expected)
        {
            TestAssert.IsNotNull(expected, "Checkpoint must list exact Eternal settings files");
            var files = expected.Elements("file").ToArray();
            TestAssert.IsTrue(files.Length > 0 && files.Select(f => Required(f, "name")).Distinct().Count() == files.Length,
                "Distinct checkpoint settings files are required");
            var verification = new XElement("settingsFileVerification", new XAttribute("loadedComparison", "semanticXML"),
                new XAttribute("inputByteProofOwner", "isolated host"),
                new XAttribute("inputByteProof", Path.Combine(Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath.TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), "session.json")));
            foreach (var file in files)
            {
                string name = Required(file, "name");
                TestAssert.IsTrue(Path.GetFileName(name) == name && (name == "Eternal_HediffSettings.xml"
                    || name.StartsWith("Mod_", StringComparison.Ordinal) && name.EndsWith("_Eternal_Mod.xml", StringComparison.Ordinal)),
                    "Only Eternal settings files may be inspected");
                string path = Path.Combine(GenFilePaths.ConfigFolderPath, name);
                string sourceHash = Required(file, "sha256");
                string loadedHash = HashFile(path);
                string semanticHash = SemanticSettingsHash(path);
                TestAssert.AreEqual(Required(file, "semanticSha256"), semanticHash,
                    "Loaded Eternal settings must retain checkpoint XML values: " + name);
                verification.Add(new XElement("file", new XAttribute("name", name), new XAttribute("sourceSha256", sourceHash),
                    new XAttribute("loadedSha256", loadedHash), new XAttribute("bytesUnchanged", sourceHash == loadedHash),
                    new XAttribute("semanticSha256", semanticHash)));
            }
            return verification;
        }

        private static string SemanticSettingsHash(string path)
        {
            var root = XDocument.Load(path).Root;
            TestAssert.IsNotNull(root, "Settings XML must have a root");
            // LINQ-to-XML discards insignificant indentation and normalizes line endings.
            // Element names, order, attributes, and every persisted field value remain checked.
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(root.ToString(SaveOptions.DisableFormatting))))
            {
                return HashStream(stream);
            }
        }

        private static void PrepareCommonHediffSettings()
        {
            foreach (string name in CommonHediffDefinitions)
            {
                TestAssert.Available(DefDatabase<HediffDef>.GetNamedSilentFail(name), "Common saved hediff definition " + name);
                var setting = Eternal_Mod.settings.hediffManager.GetHediffSetting(name);
                setting.canHeal = true;
                setting.healingRate = 0.03125f;
                setting.noThreshold = true;
            }
            TestAssert.IsNull(DefDatabase<HediffDef>.GetNamedSilentFail(UnresolvedHediffDefinition), "Persistence orphan def must be absent");
            var orphan = Eternal_Mod.settings.hediffManager.Store.GetOrCreate(UnresolvedHediffDefinition);
            orphan.canHeal = true;
            orphan.healingRate = SettingsDefaults.HediffHealingRateUseGlobal;
            orphan.noThreshold = false;
        }

        private static XElement CommonHediffSettingsSnapshot()
        {
            var snapshot = new XElement("commonHediffSettings", CommonHediffDefinitions.Select(name =>
            {
                TestAssert.Available(DefDatabase<HediffDef>.GetNamedSilentFail(name), "Loaded common hediff definition " + name);
                var setting = Eternal_Mod.settings.hediffManager.GetHediffSetting(name);
                return new XElement("setting", new XAttribute("def", name), new XAttribute("canHeal", setting.canHeal),
                    new XAttribute("healingRate", setting.healingRate), new XAttribute("noThreshold", setting.noThreshold));
            }));
            var orphan = Eternal_Mod.settings.hediffManager.Store.GetExisting(UnresolvedHediffDefinition);
            TestAssert.IsNotNull(orphan, "Fresh process must load the explicit absent-def record before any settings save");
            TestAssert.IsTrue(orphan.canHeal, "Fresh process retains the absent-def canHeal-only opt-in");
            snapshot.Add(new XElement("unresolvedSetting", new XAttribute("def", UnresolvedHediffDefinition),
                new XAttribute("canHeal", orphan.canHeal), new XAttribute("healingRate", orphan.healingRate),
                new XAttribute("noThreshold", orphan.noThreshold)));
            return snapshot;
        }

        private static XElement SettingsSnapshot()
        {
            return new XElement("settings", typeof(Eternal_Settings).GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.FieldType.IsPrimitive || f.FieldType.IsEnum || f.FieldType == typeof(string)).OrderBy(f => f.Name, StringComparer.Ordinal)
                .Select(f => new XElement("field", new XAttribute("name", f.Name), new XAttribute("value", Convert.ToString(f.GetValue(Eternal_Mod.settings), CultureInfo.InvariantCulture) ?? ""))));
        }
        private static XElement TimingSnapshot()
        {
            var timing = ((TickOrchestrator)TestPawnFactory.RequiredField(Eternal_Component.Current, "tickOrchestrator")).GetTimingValues();
            return new XElement("timing", new XAttribute("normal", timing.normalHeal), new XAttribute("rare", timing.rareHeal),
                new XAttribute("trait", timing.traitCheck), new XAttribute("corpse", timing.corpseCheck),
                new XAttribute("map", timing.mapCheck), new XAttribute("sweep", timing.healingSweep));
        }
        private static int CheckCadence(XElement before, XElement after, string cadence, int interval)
        {
            int oldT = (int)before.Attribute(cadence), newT = (int)after.Attribute(cadence);
            if (oldT == newT)
            {
                return 0;
            }

            TestAssert.IsTrue(newT <= Find.TickManager.TicksGame && newT - oldT >= interval
                && (newT - oldT) % interval == 0, "Loaded native cadence must respect " + cadence + " interval");
            return (newT - oldT) / interval;
        }
        private static string PartIdentity(Pawn pawn, BodyPartRecord part)
        {
            return part == null ? "whole" : pawn.RaceProps.body.defName + "/" + pawn.RaceProps.body.AllParts.IndexOf(part) + "/" + part.def.defName;
        }
        private static void EqualXml(XElement expected, XElement actual, string message)
        {
            TestAssert.IsTrue(PersistenceSnapshotComparer.AreEqual(expected, actual, FloatSettingFields),
                message + " expected=" + expected + " actual=" + actual);
        }
        private static string Required(XElement element, string name)
        {
            var value = (string)element.Attribute(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new FormatException("Missing persistence attribute " + name);
            }

            return value;
        }
        private static string HashFile(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                return HashStream(stream);
            }
        }

        private static string HashStream(Stream stream)
        {
            using (var hash = SHA256.Create())
            {
                return string.Concat(hash.ComputeHash(stream).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
            }
        }
        private static XElement Result(string mode, XElement checkpoint, string reportDirectory, int actors, bool cleanup)
        {
            return new XElement("persistence", new XAttribute("mode", mode), new XAttribute("passed", true), new XAttribute("actors", actors),
                new XAttribute("saveName", Required(checkpoint, "saveName")), new XAttribute("savePath", Required(checkpoint, "savePath")),
                new XAttribute("checkpointPath", Required(checkpoint, "checkpointPath")), new XAttribute("settingsFolder", GenFilePaths.ConfigFolderPath),
                new XAttribute("cleanupAfterVerify", cleanup), new XAttribute("reportDirectory", reportDirectory),
                new XElement("coverage", "Native whole-game Scribe save and fresh-process identity/state continuation. Captured RJW anatomy is checked when present; absent RJW produces explicit available=false and no fabricated anatomy."));
        }
        private static void RequireIsolatedHost(string path, string expectedMode)
        {
            string session, data, mode;
            if (!GenCommandLine.TryGetCommandLineArg("eternal-test-session", out session)
                || !GenCommandLine.TryGetCommandLineArg("savedatafolder", out data)
                || !GenCommandLine.TryGetCommandLineArg("eternal-test-persistence", out mode)
                || mode != expectedMode)
            {
                throw new InvalidOperationException("Persistence requires the explicitly isolated development host");
            }
            string root = Path.GetFullPath(session).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            TestAssert.IsTrue(string.Equals(Path.GetFullPath(data).TrimEnd(Path.DirectorySeparatorChar), Path.Combine(root, "savedata"), StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetFullPath(GenFilePaths.SaveDataFolderPath).TrimEnd(Path.DirectorySeparatorChar), Path.Combine(root, "savedata"), StringComparison.OrdinalIgnoreCase)
                && Path.GetFullPath(path).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                "Persistence must stay within its disposable session, save-data, and checkpoint/report paths");
            TestAssert.Available(Eternal_Component.Current, "Initialized loaded Eternal persistence component");
        }
        private static Options LoadOptions(string path)
        {
            var element = XDocument.Load(path).Root?.Element("persistence");
            if (element == null)
            {
                throw new FormatException("Missing authoritative persistence configuration");
            }

            var options = new Options
            {
                MapSize = (int)element.Attribute("mapSize"),
                ContinuationTicks = (int)element.Attribute("continuationTicks"),
                MoodBuffValue = (int)element.Attribute("moodBuffValue")
            };
            if (options.MapSize < 25 || options.MapSize > 250 || options.ContinuationTicks < 500 || options.ContinuationTicks > 100000
                || options.MoodBuffValue < 0 || options.MoodBuffValue > 20)
            {
                throw new FormatException("Invalid persistence operational defaults");
            }

            return options;
        }
        private sealed class Options { internal int MapSize, ContinuationTicks, MoodBuffValue; }
    }

    // Pure comparer: native snapshot text remains unchanged; only comparison copies encode Single bits.
    internal static class PersistenceSnapshotComparer
    {
        internal static bool AreEqual(XElement expected, XElement actual, ISet<string> floatSettingFields)
        {
            return XNode.DeepEquals(ComparisonCopy(expected, floatSettingFields), ComparisonCopy(actual, floatSettingFields));
        }

        private static XElement ComparisonCopy(XElement source, ISet<string> floatSettingFields)
        {
            if (source == null)
            {
                return null;
            }

            var copy = new XElement(source);
            foreach (var element in copy.DescendantsAndSelf())
            {
                foreach (var attribute in element.Attributes().ToArray())
                {
                    if (IsSingleField(element, attribute, floatSettingFields))
                    {
                        attribute.Value = SingleBits(attribute.Value);
                    }
                }
            }

            return copy;
        }

        private static bool IsSingleField(XElement element, XAttribute attribute, ISet<string> floatSettingFields)
        {
            string name = attribute.Name.LocalName;
            if (attribute.Name.Namespace != XNamespace.None || element.Name.Namespace != XNamespace.None)
            {
                return false;
            }

            switch (element.Name.LocalName)
            {
                case "actor": return name == "food" && attribute.Value != "disabled";
                case "hediff":
                    return element.Parent?.Name == "health" && (name == "severity" || name == "partMaxHp");
                case "corpse": return name == "progress" || name == "foodDebt" || name == "totalCost";
                case "item":
                    return (element.Parent?.Name == "queue" || element.Parent?.Name == "capturedQueue")
                        && (name == "severity" || name == "priority" || name == "time" || name == "energy");
                case "debt": return name == "amount" || name == "peak" || name == "baseline";
                case "setting": return element.Parent?.Name == "commonHediffSettings" && name == "healingRate";
                case "field":
                    return element.Parent?.Name == "settings" && name == "value"
                        && floatSettingFields?.Contains((string)element.Attribute("name")) == true;
                default: return false;
            }
        }

        private static string SingleBits(string text)
        {
            float value = XmlConvert.ToSingle(text);
            // Older runtimes can parse negative zero as positive zero. Its XML sign still identifies its IEEE bit pattern.
            uint bits = value == 0f && text.TrimStart().StartsWith("-", StringComparison.Ordinal)
                ? 0x80000000u : BitConverter.ToUInt32(BitConverter.GetBytes(value), 0);
            return "single32:" + bits.ToString("x8", CultureInfo.InvariantCulture);
        }
    }
}
#endif
