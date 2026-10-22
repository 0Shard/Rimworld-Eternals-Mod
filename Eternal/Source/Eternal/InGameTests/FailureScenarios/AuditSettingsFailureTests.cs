#if DEBUG
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Eternal.Settings;
using Eternal.DI;
using Eternal.UI.Management;
using Eternal.UI.HediffSettings;
using Eternal.InGameTests.Performance;
using Verse;

namespace Eternal.InGameTests.FailureScenarios
{
    public static class AuditSettingsFailureTests
    {
        public static TestSuiteResult RunAll(Verse.Map unused)
        {
            var result = new TestSuiteResult(0, 0, new System.Collections.Generic.List<string>());
            result.RunTest("AuditSettings", "NewRootLoadsSeparateXmlOnceBeforeSave", NewRootLoadsXml);
            result.RunTest("AuditSettings", "AbsentDefinitionCanHealOnlyOverrideSurvivesRoundTrip", AbsentDefinitionRoundTrip);
            result.RunTest("AuditSettings", "ExplicitResetRemovesBothOrphanChoicesAndLegacyReinitialization", ResetAbsentDefinitions);
            result.RunTest("AuditSettings", "RejectedSourcesSurviveOrdinarySavesAndExplicitResetBacksUp", RejectedSources);
            result.RunTest("AuditSettings", "ValidReloadUnlocksProtectedSource", ValidReload);
            result.RunTest("AuditSettings", "EveryGlobalFloatNormalizesAtWriteBoundary", GlobalFloats);
            result.RunTest("AuditSettings", "FilteredSelectionAppliesToConcreteRemainder", Selection);
            result.RunTest("AuditSettings", "FreeAndPaidRowsUseActualBudgetWithoutMutatingPawn", FreeRowSnapshot);
            return result;
        }

        private static void WithPrivateSettings(Action<Eternal_Settings, string> action)
        {
            string session, data;
            TestAssert.IsTrue(GenCommandLine.TryGetCommandLineArg("eternal-test-session", out session)
                && GenCommandLine.TryGetCommandLineArg("savedatafolder", out data)
                && string.Equals(Path.GetFullPath(data).TrimEnd(Path.DirectorySeparatorChar),
                    Path.Combine(Path.GetFullPath(session), "savedata"), StringComparison.Ordinal)
                && string.Equals(Path.GetFullPath(GenFilePaths.SaveDataFolderPath).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(data).TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal),
                "Settings fixtures require the disposable host profile");
            string path = HediffSettingsXmlStore.GetFilePath();
            byte[] original = File.Exists(path) ? File.ReadAllBytes(path) : null;
            var prior = Eternal_Mod.settings;
            var priorInstance = Eternal_Settings.instance;
            try
            {
                var settings = new Eternal_Settings();
                Eternal_Mod.settings = settings;
                action(settings, path);
            }
            finally
            {
                Eternal_Mod.settings = prior;
                Eternal_Settings.instance = priorInstance;
                if (original == null) { if (File.Exists(path)) { File.Delete(path); } }
                else { File.WriteAllBytes(path, original); }
            }
        }

        private static void WriteRecord(string path, string name, string canHeal = "true", int version = 2)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            new XDocument(new XElement("EternalHediffSettings", new XElement("version", version),
                new XElement("settings", new XElement("li", new XElement("defName", name),
                    new XElement("canHeal", canHeal), new XElement("healingRate", "-1"))))).Save(path);
        }

        private static void NewRootLoadsXml() => WithPrivateSettings((settings, path) =>
        {
            WriteRecord(path, "Flu");
            string identifier = "Eternal_Debug_MissingMain_" + Guid.NewGuid().ToString("N");
            TestAssert.IsFalse(File.Exists(Path.Combine(GenFilePaths.ConfigFolderPath,
                "Mod_" + identifier + "_Eternal_Mod.xml")), "Synthetic main ModSettings file is absent");
            settings = LoadedModManager.ReadModSettings<Eternal_Settings>(identifier, "Eternal_Mod");
            Eternal_Mod.settings = settings;
            TestAssert.IsNull(settings.hediffManager.Store.GetExisting("Flu"), "Native absent-main loader returns a newly constructed root");
            settings.ScheduleHediffSettingsInitialization();
            TestAssert.IsTrue(settings.hediffManager.Store.GetExisting("Flu")?.canHeal == true,
                "Definition-ready fresh root loads the separate XML");
            settings.hediffManager.Store.GetExisting("Flu").canHeal = false;
            settings.ScheduleHediffSettingsInitialization();
            settings.InitializeHediffSettings();
            TestAssert.IsFalse(settings.hediffManager.Store.GetExisting("Flu").canHeal,
                "A second initializer does not reload over current edits");
            settings.SaveHediffSettings();
            TestAssert.IsNotNull(XDocument.Load(path).Root, "Initialized native Scribe write creates valid XML");
        });

        private static void AbsentDefinitionRoundTrip() => WithPrivateSettings((settings, path) =>
        {
            const string name = "Eternal_Debug_TemporarilyAbsentDisease";
            TestAssert.IsNull(DefDatabase<HediffDef>.GetNamedSilentFail(name), "Fixture starts with a truly absent def");
            WriteRecord(path, name);
            settings.InitializeHediffSettings();
            settings.SaveHediffSettings();
            var row = XDocument.Load(path).Descendants("li").Single(li => li.Element("defName")?.Value == name);
            TestAssert.IsTrue(row.Element("canHeal") == null ? SettingsDefaults.HediffCanHeal : bool.Parse(row.Element("canHeal").Value),
                "Explicit orphan record retains its opt-in, including native Scribe's omitted true default");
            var reloaded = new HediffSettingsStore();
            reloaded.LoadFromXml();
            TestAssert.IsTrue(reloaded.GetExisting(name).canHeal, "Native Scribe round trip retains canHeal-only choice");
        });

        private static void ResetAbsentDefinitions() => WithPrivateSettings((settings, path) =>
        {
            foreach (bool canHeal in new[] { true, false })
            {
                string name = "Eternal_Debug_ResetAbsent_" + canHeal;
                TestAssert.IsNull(DefDatabase<HediffDef>.GetNamedSilentFail(name), "Reset fixture begins with an unresolved definition");
                WriteRecord(path, name, canHeal ? "true" : "false");
                var store = new HediffSettingsStore();
                store.LoadFromXml();
                var orphan = store.GetExisting(name);
                TestAssert.AreEqual(canHeal, orphan.canHeal, "Native load retains the original explicit orphan choice");
                var legacy = (System.Collections.Generic.Dictionary<string, EternalHediffSetting>)HarmonyLib.AccessTools
                    .Field(typeof(HediffSettingsStore), "savedSettings").GetValue(store);
                legacy[name] = orphan;
                store.ResetAllAndDeleteXml();
                TestAssert.IsNull(store.GetExisting(name), "Explicit reset removes unresolved records instead of fabricating defaults");
                TestAssert.IsFalse(store.HasOldSavedSettings(), "Reset also clears the legacy source of later initialization");
                store.Clear();
                TestAssert.IsFalse(store.GetAll().Any(row => row.Key == name), "Reinitialization cannot resurrect the legacy orphan");
                store.SaveToXml();
                TestAssert.IsFalse(XDocument.Load(path).Descendants("defName").Any(node => node.Value == name),
                    "Ordinary save after reset contains no explicit orphan record");
                var restored = new HediffDef { defName = name, label = name, isBad = true, hediffClass = typeof(HediffWithComps) };
                DefDatabase<HediffDef>.Add(restored);
                try
                {
                    var reloaded = new HediffSettingsStore();
                    reloaded.LoadFromXml();
                    TestAssert.IsFalse(reloaded.GetOrCreate(restored).canHeal, "Restored definition uses its actual conservative default");
                }
                finally { HarmonyLib.AccessTools.Method(typeof(DefDatabase<HediffDef>), "Remove").Invoke(null, new object[] { restored }); }
            }
        });

        private static void RejectedSources() => WithPrivateSettings((settings, path) =>
        {
            foreach (string source in new[] { "<unclosed", "<EternalHediffSettings><version>1</version><settings><li><defName>Flu</defName><canHeal>tru</canHeal></li></settings></EternalHediffSettings>",
                "<EternalHediffSettings><version>2</version><settings><li><defName>Flu</defName><canHeal>tru</canHeal></li></settings></EternalHediffSettings>" })
            {
                File.WriteAllText(path, source);
                settings.hediffManager.Store.LoadFromXml();
                TestAssert.AreEqual(HediffSettingsLoadOutcome.Rejected, settings.hediffManager.Store.XmlLoadOutcome,
                    "Malformed source has explicit rejected outcome");
                settings.moodBuffValue++;
                settings.ValidateBeforeWrite();
                LoadedModManager.GetMod<Eternal_Mod>().WriteSettings();
                TestAssert.AreEqual(source, File.ReadAllText(path), "Unrelated global save preserves rejected bytes");
            }
            string rejected = File.ReadAllText(path);
            var before = Directory.GetFiles(Path.GetDirectoryName(path), Path.GetFileName(path) + ".rejected-*.bak");
            settings.hediffManager.Store.ResetAllAndDeleteXml();
            var backup = Directory.GetFiles(Path.GetDirectoryName(path), Path.GetFileName(path) + ".rejected-*.bak").Except(before).Single();
            TestAssert.AreEqual(rejected, File.ReadAllText(backup), "Explicit reset preserves a durable original backup");
            TestAssert.IsFalse(File.Exists(path), "Explicit reset can replace the protected source after backup");
        });

        private static void ValidReload() => WithPrivateSettings((settings, path) =>
        {
            File.WriteAllText(path, "<invalid");
            settings.hediffManager.Store.LoadFromXml();
            WriteRecord(path, "Flu");
            settings.hediffManager.Store.LoadFromXml();
            TestAssert.AreEqual(HediffSettingsLoadOutcome.Valid, settings.hediffManager.Store.XmlLoadOutcome, "Valid reload releases protection");
            settings.hediffManager.Store.GetExisting("Flu").healingRate = 0.5f;
            settings.hediffManager.Store.SaveToXml();
            TestAssert.IsTrue(XDocument.Load(path).Descendants("healingRate").Any(element => element.Value == "0.5"), "Recovered store can save intended edits");
        });

        private static void GlobalFloats() => WithPrivateSettings((settings, path) =>
        {
            foreach (var field in typeof(Eternal_Settings).GetFields().Where(field => field.FieldType == typeof(float)))
            {
                foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                {
                    field.SetValue(settings, invalid);
                    settings.ValidateBeforeWrite();
                    float actual = (float)field.GetValue(settings);
                    TestAssert.IsFalse(float.IsNaN(actual) || float.IsInfinity(actual), "Global write normalizes " + field.Name);
                    string catalogName = char.ToUpperInvariant(field.Name[0]) + field.Name.Substring(1);
                    TestAssert.AreEqual((float)typeof(SettingsDefaults).GetField(catalogName).GetRawConstantValue(), actual,
                        "Global write uses canonical default for " + field.Name);
                }
            }
        });

        private static void Selection() => WithPrivateSettings((settings, path) =>
        {
            var manager = settings.hediffManager;
            var filtered = manager.GetFilteredHediffs().Select(pair => pair.Key).OrderBy(name => name).ToArray();
            manager.SelectAllHediffs(true);
            TestAssert.IsTrue(filtered.SequenceEqual(manager.GetSelectedHediffs().OrderBy(name => name)), "Native caller materializes exactly its filtered universe");
            manager.Selection.SelectAll(new[] { "Flu", "Burn" });
            manager.Selection.Deselect("Flu");
            TestAssert.AreEqual(1, manager.GetSelectedCount(), "Concrete selection count survives deselect-one");
            TestAssert.AreEqual("Burn", manager.GetSelectedHediffs().Single(), "Application enumerates the selected remainder");
            var presenter = new EternalHediffPresenter(new EternalHediffModel(manager));
            presenter.BulkTemplate.healingRate = 0.25f;
            TestAssert.AreEqual(1, presenter.OnApplyToSelected(), "Production Apply to Selected updates the one remaining def");
            TestAssert.AreEqual(0.25f, manager.Store.GetExisting("Burn").healingRate, "Remaining selected def receives the real template");
        });

        private static void FreeRowSnapshot() => WithPrivateSettings((settings, path) =>
        {
            using (var scope = new TestMapScope())
            {
                var pawn = scope.SpawnEternalPawn(scope.Create(50, true));
                var part = pawn.health.hediffSet.GetNotMissingParts().First(record => record.def.defName == "Arm");
                var free = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("Cut"), pawn, part);
                var paid = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("Burn"), pawn, part);
                free.Severity = 4f;
                paid.Severity = 4f;
                pawn.health.AddHediff(free, part);
                pawn.health.AddHediff(paid, part);
                settings.hediffManager.Store.GetOrCreate(free.def).nutritionCostMultiplier = 0f;
                settings.hediffManager.Store.GetOrCreate(paid.def).nutritionCostMultiplier = 1f;
                pawn.needs.food.CurLevel = pawn.needs.food.MaxLevel * settings.foodDrainThreshold;
                var debt = EternalServiceContainer.Instance.FoodDebtSystem;
                debt.ClearDebt(pawn);
                debt.AddDebt(pawn, debt.GetMaxCapacity(pawn));
                float food = pawn.needs.food.CurLevel;
                float balance = debt.GetDebt(pawn);
                var snapshot = new ManagementTabModel(pawn).Capture();
                TestAssert.IsTrue(snapshot.Status.StartsWith("Healing available", StringComparison.Ordinal), "Free row makes overall healing available");
                TestAssert.IsTrue(snapshot.Conditions.Single(row => row.Label.StartsWith(free.LabelCap, StringComparison.Ordinal)).Status.StartsWith("Eligible", StringComparison.Ordinal), "Free native injury remains eligible");
                TestAssert.IsTrue(snapshot.Conditions.Single(row => row.Label.StartsWith(paid.LabelCap, StringComparison.Ordinal)).Status.Contains("Waiting for food"), "Paid native injury remains blocked");
                TestAssert.AreEqual(food, pawn.needs.food.CurLevel, "Snapshot cannot drain native food");
                TestAssert.AreEqual(balance, debt.GetDebt(pawn), "Snapshot cannot settle native debt");
                TestAssert.AreEqual(4f, free.Severity, "Snapshot cannot heal free native injury");
                TestAssert.AreEqual(4f, paid.Severity, "Snapshot cannot heal paid native injury");
            }
        });
    }
}
#endif
