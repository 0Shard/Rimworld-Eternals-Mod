// Relative Path: Eternal/Source/Eternal/InGameTests/TestSuites/MaintainabilityBoundaryTests.cs
// Author: 0Shard
// Description: Real game-host settings, calculations, ledgers, food and graph boundaries.
#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using Eternal.Corpse;
using Eternal.DI;
using Eternal.Extensions;
using Eternal.Healing;
using Eternal.Resources;
using Eternal.Settings;
using Eternal.InGameTests.Helpers;

namespace Eternal.InGameTests.TestSuites
{
    public static class MaintainabilityBoundaryTests
    {
        public static TestSuiteResult RunAll(Verse.Map map)
        {
            var result = new TestSuiteResult(0, 0, new List<string>());
            result.RunTest("MaintainabilityBoundary", "SettingsMappingAndSnapshot", SettingsMappingAndSnapshot);
            result.RunTest("MaintainabilityBoundary", "RealHealingAdapters", () => RealHealingAdapters(map));
            result.RunTest("MaintainabilityBoundary", "LedgerLifecycle", () => LedgerLifecycle(map));
            result.RunTest("MaintainabilityBoundary", "FoodAndLedgerRepayment", () => FoodAndLedgerRepayment(map));
            result.RunTest("MaintainabilityBoundary", "FoodNeedWaiverClearsExistingState", () => FoodNeedWaiverClearsExistingState(map));
            result.RunTest("MaintainabilityBoundary", "LiveAndIsolatedGraphIdentity", LiveAndIsolatedGraphIdentity);
            result.RunTest("MaintainabilityBoundary", "TickFailureRestoresSpeed", TickFailureRestoresSpeed);
            result.RunTest("MaintainabilityBoundary", "TickFinalBoundaryAndValidation", TickFinalBoundaryAndValidation);
            result.RunTest("MaintainabilityBoundary", "AssertionsRejectNonfinite", AssertionsRejectNonfinite);
            result.RunTest("MaintainabilityBoundary", "HarnessCountsUnavailableAndFailures", HarnessCountsUnavailableAndFailures);
            return result;
        }

        private static void WithFreshSettings(Action<Eternal_Settings> test)
        {
            var original = Eternal_Mod.settings;
            var originalInstance = Eternal_Settings.instance;
            try
            {
                // Construction changes the legacy static instance too; neither reference may leak.
                var settings = new Eternal_Settings();
                Eternal_Mod.settings = settings;
                test(settings);
            }
            finally
            {
                Eternal_Mod.settings = original;
                Eternal_Settings.instance = originalInstance;
                TestAssert.IsTrue(ReferenceEquals(original, Eternal_Mod.settings), "Restore exact settings reference");
                TestAssert.IsTrue(ReferenceEquals(originalInstance, Eternal_Settings.instance), "Restore legacy settings instance");
            }
        }

        private static void SettingsMappingAndSnapshot()
        {
            WithFreshSettings(s =>
            {
                s.loggingLevel = 3;
                s.baseHealingRate = 3.25f;
                s.showEternalPowerLabel = false;
                s.nutritionCostMultiplier = 1.7f;
                s.maxDebtMultiplier = 7f;
                s.foodDrainThreshold = 0.3f;
                s.debtRepaymentDays = 2f;
                s.normalTickRate = 83;
                s.rareTickRate = 317;
                s.traitCheckInterval = 6013;
                s.corpseCheckInterval = 1237;
                s.mapCheckInterval = 719;
                s.healingHistorySweepInterval = 420013;
                s.enableMapAnchors = false;
                s.anchorGracePeriodTicks = 24683;
                s.enableRoofCollapseProtection = false;
                s.consciousnessBuffEnabled = false;
                s.consciousnessMultiplier = 1.8f;
                s.moodBuffEnabled = false;
                s.moodBuffValue = 23;
                s.populationCapEnabled = true;
                s.populationCap = 17;

                var adapter = new SettingsAdapter();
                TestAssert.AreEqual(true, adapter.DebugMode, "Derived debug mode");
                TestAssert.AreEqual(3, adapter.LoggingLevel, "Logging provider");
                TestAssert.AreEqual(3.25f, adapter.BaseHealingRate, "Healing provider");
                TestAssert.AreEqual(false, adapter.ShowEternalPowerLabel, "Power label provider");
                TestAssert.AreEqual(1.7f, adapter.NutritionCostMultiplier, "Cost provider");
                TestAssert.AreEqual(7f, adapter.MaxDebtMultiplier, "Capacity provider");
                TestAssert.AreEqual(0.3f, adapter.FoodDrainThreshold, "Floor provider");
                TestAssert.AreEqual(2f, adapter.DebtRepaymentDays, "Window provider");
                TestAssert.AreEqual(83, adapter.NormalTickRate, "Normal provider");
                TestAssert.AreEqual(317, adapter.RareTickRate, "Rare provider");
                TestAssert.AreEqual(6013, adapter.TraitCheckInterval, "Trait provider");
                TestAssert.AreEqual(1237, adapter.CorpseCheckInterval, "Corpse provider");
                TestAssert.AreEqual(719, adapter.MapCheckInterval, "Map provider");
                TestAssert.AreEqual(420013, adapter.HealingHistorySweepInterval, "History provider");
                TestAssert.AreEqual(false, adapter.EnableMapAnchors, "Anchor provider");
                TestAssert.AreEqual(24683, adapter.AnchorGracePeriodTicks, "Grace provider");

                var snapshot = s.CreateSnapshot();
                TestAssert.AreEqual(true, snapshot.General.ModEnabled, "Legacy mod guard");
                TestAssert.AreEqual(true, snapshot.General.DebugMode, "Snapshot debug");
                TestAssert.AreEqual(3, snapshot.General.LoggingLevel, "Snapshot logging");
                TestAssert.AreEqual(3.25f, snapshot.Healing.BaseRate, "Snapshot healing");
                TestAssert.AreEqual(false, snapshot.Healing.ShowProgress, "Snapshot power label");
                TestAssert.AreEqual(1.7f, snapshot.Resource.NutritionCostMultiplier, "Snapshot cost");
                TestAssert.AreEqual(7f, snapshot.FoodDebt.MaxDebtMultiplier, "Snapshot capacity");
                TestAssert.AreEqual(0.3f, snapshot.FoodDebt.FoodDrainThreshold, "Snapshot floor");
                TestAssert.AreEqual(2f, snapshot.FoodDebt.DebtRepaymentDays, "Snapshot window");
                TestAssert.AreEqual(0.004f, snapshot.FoodDebt.SeverityToNutritionRatio, "Canonical severity ratio");
                TestAssert.AreEqual(83, snapshot.Perf.NormalTickRate, "Snapshot normal");
                TestAssert.AreEqual(317, snapshot.Perf.RareTickRate, "Snapshot rare");
                TestAssert.AreEqual(6013, snapshot.Perf.TraitCheckInterval, "Snapshot trait");
                TestAssert.AreEqual(1237, snapshot.Perf.CorpseCheckInterval, "Snapshot corpse");
                TestAssert.AreEqual(719, snapshot.Perf.MapCheckInterval, "Snapshot map");
                TestAssert.AreEqual(420013, snapshot.Perf.HealingHistorySweepInterval, "Snapshot history");
                TestAssert.AreEqual(false, snapshot.Map.EnableMapAnchors, "Snapshot anchors");
                TestAssert.AreEqual(24683, snapshot.Map.AnchorGracePeriodTicks, "Snapshot grace");
                TestAssert.AreEqual(false, snapshot.Map.EnableRoofCollapseProtection, "Snapshot roof protection");
                TestAssert.AreEqual(false, snapshot.Effects.ConsciousnessBuffEnabled, "Snapshot consciousness toggle");
                TestAssert.AreEqual(1.8f, snapshot.Effects.ConsciousnessMultiplier, "Snapshot consciousness multiplier");
                TestAssert.AreEqual(false, snapshot.Effects.MoodBuffEnabled, "Snapshot mood toggle");
                TestAssert.AreEqual(23, snapshot.Effects.MoodBuffValue, "Snapshot mood value");
                TestAssert.AreEqual(true, snapshot.Effects.PopulationCapEnabled, "Snapshot cap toggle");
                TestAssert.AreEqual(17, snapshot.Effects.PopulationCap, "Snapshot cap");

                s.baseHealingRate = 4.5f;
                s.rareTickRate = 419;
                s.moodBuffValue = 31;
                TestAssert.AreEqual(4.5f, adapter.BaseHealingRate, "Adapter reads subsequent settings edits");
                TestAssert.AreEqual(3.25f, snapshot.Healing.BaseRate, "Snapshot retains captured healing");
                TestAssert.AreEqual(317, snapshot.Perf.RareTickRate, "Snapshot retains cadence");
                TestAssert.AreEqual(23, snapshot.Effects.MoodBuffValue, "Snapshot retains effects");
                Eternal_Mod.settings = null;
                AssertDefaultAdapter(adapter);
            });
        }

        private static void AssertDefaultAdapter(SettingsAdapter adapter)
        {
            TestAssert.AreEqual(SettingsDefaults.DebugMode, adapter.DebugMode, "Null debug fallback");
            TestAssert.AreEqual(SettingsDefaults.LoggingLevel, adapter.LoggingLevel, "Null logging fallback");
            TestAssert.AreEqual(SettingsDefaults.BaseHealingRate, adapter.BaseHealingRate, "Null healing fallback");
            TestAssert.AreEqual(SettingsDefaults.ShowEternalPowerLabel, adapter.ShowEternalPowerLabel, "Null label fallback");
            TestAssert.AreEqual(SettingsDefaults.NutritionCostMultiplier, adapter.NutritionCostMultiplier, "Null cost fallback");
            TestAssert.AreEqual(SettingsDefaults.MaxDebtMultiplier, adapter.MaxDebtMultiplier, "Null capacity fallback");
            TestAssert.AreEqual(SettingsDefaults.FoodDrainThreshold, adapter.FoodDrainThreshold, "Null floor fallback");
            TestAssert.AreEqual(SettingsDefaults.DebtRepaymentDays, adapter.DebtRepaymentDays, "Null window fallback");
            TestAssert.AreEqual(SettingsDefaults.NormalTickRate, adapter.NormalTickRate, "Null normal fallback");
            TestAssert.AreEqual(SettingsDefaults.RareTickRate, adapter.RareTickRate, "Null rare fallback");
            TestAssert.AreEqual(SettingsDefaults.TraitCheckInterval, adapter.TraitCheckInterval, "Null trait fallback");
            TestAssert.AreEqual(SettingsDefaults.CorpseCheckInterval, adapter.CorpseCheckInterval, "Null corpse fallback");
            TestAssert.AreEqual(SettingsDefaults.MapCheckInterval, adapter.MapCheckInterval, "Null map fallback");
            TestAssert.AreEqual(SettingsDefaults.HealingHistorySweepInterval, adapter.HealingHistorySweepInterval, "Null history fallback");
            TestAssert.AreEqual(SettingsDefaults.EnableMapAnchors, adapter.EnableMapAnchors, "Null anchor fallback");
            TestAssert.AreEqual(SettingsDefaults.AnchorGracePeriodTicks, adapter.AnchorGracePeriodTicks, "Null grace fallback");
        }

        private static void RealHealingAdapters(Verse.Map map)
        {
            WithFreshSettings(s =>
            {
                s.baseHealingRate = 3.25f;
                var pawn = TestPawnFactory.SpawnEternalPawn(map);
                var part = pawn.health.hediffSet.GetNotMissingParts().FirstOrDefault(p => p.def.defName == "Arm");
                TestAssert.Available(part, "Arm for real injury calculation");
                var injury = HediffMaker.MakeHediff(HediffDefOf.Cut, pawn, part);
                TestAssert.IsTrue(injury is Hediff_Injury, "Actual injury class required");
                var definition = new HediffDef
                {
                    defName = "EternalTestStagedDebuff",
                    isBad = true,
                    hediffClass = typeof(Hediff),
                    stages = new List<HediffStage> {
                        new HediffStage { minSeverity = 0f }, new HediffStage { minSeverity = 0.2f },
                        new HediffStage { minSeverity = 0.5f } }
                };
                var staged = HediffMaker.MakeHediff(definition, pawn);
                staged.Severity = 0.3f;
                TestAssert.AreEqual(1, staged.CurStageIndex, "Independent staged input setup");
                TestAssert.IsTrue(staged.IsDebuffWithStages(), "Actual staged classifier");
                var calculator = new UnifiedHediffHealingCalculator(new SettingsAdapter());
                TestAssert.IsNotNull(calculator, "Real provider constructor");
                var nullProviderCalculator = new UnifiedHediffHealingCalculator(null);
                TestAssert.IsNotNull(nullProviderCalculator, "Null provider constructor preserves compatibility");
                Near(1.2f, nullProviderCalculator.GetEffectiveRate(null), "Null provider fallback");
                Near(1f, calculator.GetBodySizeScaling((Pawn)null), "Null Pawn body fallback");
                Near(pawn.BodySize, calculator.GetBodySizeScaling(pawn), "Actual body size");
                Near(3.25f * pawn.BodySize, calculator.CalculateHediffHealing(pawn, injury, null), "Global injury rate/body");
                var custom = new EternalHediffSetting(HediffDefOf.Cut) { healingRate = 0.37f };
                Near(0.37f * pawn.BodySize, calculator.CalculateHediffHealing(pawn, injury, custom), "Custom injury rate/body");
                var global = new EternalHediffSetting(definition) { healingRate = EternalHediffSetting.USE_GLOBAL_RATE };
                Near(3.25f * 0.8f * pawn.BodySize * 0.01f,
                    calculator.CalculateHediffHealing(pawn, staged, global), "Global staged rate/stage/body/factor");
                custom.healingRate = 0.43f;
                Near(0.43f * 0.8f * pawn.BodySize * 0.01f,
                    calculator.CalculateHediffHealing(pawn, staged, custom), "Custom staged rate");
                Near(1f, calculator.GetSeverityScaling(staged, pawn), "Flat severity scaling");
                Near(0f, calculator.CalculateHediffHealing((Pawn)null, injury, custom), "Null pawn guard");
                Near(0f, calculator.CalculateHediffHealing(pawn, null, custom), "Null hediff guard");
            });
        }

        private static void LedgerLifecycle(Verse.Map map)
        {
            WithFreshSettings(s =>
            {
                s.maxDebtMultiplier = 5f;
                var pawn = TestPawnFactory.SpawnEternalPawn(map);
                TestAssert.Available(pawn.needs?.food, "Food need for debt capacity");
                float baseCapacity = pawn.needs.food.MaxLevel * 5f;
                var manager = new UnifiedFoodDebtManager();
                try
                {
                    manager.RegisterPawn(pawn);
                    TestAssert.IsTrue(manager.GetTrackedPawns().Contains(pawn), "Actual manager registration");
                    Near(0f, manager.GetDebt(pawn), "New registered debt");
                    Near(baseCapacity, manager.GetMaxCapacity(pawn), "Nondefault food capacity");
                    TestAssert.IsTrue(manager.AddDebt(pawn, baseCapacity + 3f), "Ordinary addition handled");
                    Near(baseCapacity, manager.GetDebt(pawn), "Ordinary addition clamps at capacity");
                    manager.ClearDebt(pawn);
                    TestAssert.IsTrue(manager.AddResurrectionDebt(pawn, 6f), "Resurrection addition handled");
                    Near(6f, manager.GetDebt(pawn), "Uncapped resurrection amount");
                    Near(baseCapacity + 6f, manager.GetMaxCapacity(pawn), "Resurrection baseline headroom");
                    Near(2f, manager.RepayDebt(pawn, 2f), "Actual partial repayment");
                    Near(4f, manager.GetDebt(pawn), "Partial ledger remainder");
                    Near(baseCapacity + 4f, manager.GetMaxCapacity(pawn), "Baseline shrinks with repayment");
                    Near(6f, manager.GetPeakDebt(pawn), "Peak survives partial repayment");
                    TestAssert.IsTrue(manager.AddDebt(pawn, baseCapacity + 20f), "Ordinary headroom addition");
                    Near(baseCapacity + 4f, manager.GetDebt(pawn), "Ordinary debt respects raised capacity");
                    Near(baseCapacity + 4f, manager.RepayDebt(pawn, 1000f), "Full repayment clamps at actual debt");
                    Near(0f, manager.GetDebt(pawn), "Cleared ledger");
                    Near(0f, manager.GetPeakDebt(pawn), "Episode peak resets at zero");
                    Near(baseCapacity, manager.GetMaxCapacity(pawn), "Baseline resets at zero");
                    TestAssert.IsTrue(manager.AddDebt(pawn, 0.5f), "Second episode starts");
                    Near(0.5f, manager.GetPeakDebt(pawn), "Second episode has independent peak");
                }
                finally { manager.UnregisterPawn(pawn); }
                TestAssert.IsFalse(manager.GetTrackedPawns().Contains(pawn), "Isolated ledger cleanup");
            });
        }

        private static void FoodAndLedgerRepayment(Verse.Map map)
        {
            WithFreshSettings(s =>
            {
                s.maxDebtMultiplier = 7f;
                s.debtRepaymentDays = 2f;
                s.rareTickRate = 317;
                s.foodDrainThreshold = 0.3f;
                var pawn = TestPawnFactory.SpawnEternalPawn(map);
                var food = pawn.needs?.food;
                TestAssert.Available(food, "Food need for repayment");
                float originalFood = food.CurLevel;
                var manager = new UnifiedFoodDebtManager();
                try
                {
                    manager.RegisterPawn(pawn);
                    TestAssert.IsTrue(manager.AddResurrectionDebt(pawn, 6f), "Seed actual repayment ledger");
                    food.CurLevel = food.MaxLevel * 0.8f;
                    float foodBefore = food.CurLevel;
                    var processor = new DebtRepaymentProcessor(new SettingsAdapter(), manager);
                    processor.ProcessDebtRepayment();
                    const float expectedDrain = 6f / 120000f * 317f;
                    Near(foodBefore - expectedDrain, food.CurLevel, "Real food subtraction");
                    Near(6f - expectedDrain, manager.GetDebt(pawn), "Same drain repays actual ledger");
                    food.CurLevel = food.MaxLevel * 0.3f;
                    float floorFood = food.CurLevel;
                    float debtAtFloor = manager.GetDebt(pawn);
                    processor.ProcessDebtRepayment();
                    Near(floorFood, food.CurLevel, "Configured floor blocks drain");
                    Near(debtAtFloor, manager.GetDebt(pawn), "Configured floor blocks repayment");
                    food.CurLevel = floorFood + 0.002f;
                    processor.ProcessDebtRepayment();
                    Near(floorFood, food.CurLevel, "Drain clamps to available food above floor");
                    Near(debtAtFloor - 0.002f, manager.GetDebt(pawn), "Food clamp reaches ledger");
                }
                finally
                {
                    try { food.CurLevel = originalFood; }
                    finally { manager.UnregisterPawn(pawn); }
                }
            });
        }

        private static void FoodNeedWaiverClearsExistingState(Verse.Map map)
        {
            WithFreshSettings(s =>
            {
                var pawn = TestPawnFactory.SpawnEternalPawn(map);
                TestAssert.Available(pawn.needs?.food, "Food need before waiver");
                TestAssert.Available(EternalDefOf.Eternal_MetabolicRecovery, "Recovery def for waiver cleanup");
                var manager = new UnifiedFoodDebtManager();
                var originalHediffs = pawn.health.hediffSet.hediffs.ToList();
                var originalFood = pawn.needs.food;
                float foodLevel = originalFood.CurLevel;
                Hediff noFood = null;
                try
                {
                    manager.RegisterPawn(pawn);
                    TestAssert.IsTrue(manager.AddResurrectionDebt(pawn, 6f), "Seed debt before waiver");
                    var recovery = HediffMaker.MakeHediff(EternalDefOf.Eternal_MetabolicRecovery, pawn);
                    TestAssert.IsTrue(recovery is Eternal.Hediffs.MetabolicRecovery_Hediff, "Actual recovery class required");
                    recovery.Severity = 0.5f;
                    pawn.health.AddHediff(recovery);
                    TestAssert.IsTrue(pawn.health.hediffSet.hediffs.Contains(recovery), "Recovery must exist before waiver");
                    var definition = new HediffDef
                    {
                        defName = "EternalTestNoFood",
                        hediffClass = typeof(Hediff),
                        stages = new List<HediffStage> { new HediffStage {
                            disablesNeeds = new List<NeedDef> { NeedDefOf.Food } } }
                    };
                    noFood = HediffMaker.MakeHediff(definition, pawn);
                    noFood.Severity = 1f;
                    pawn.health.AddHediff(noFood);
                    TestAssert.IsTrue(pawn.health.hediffSet.hediffs.Contains(noFood), "Food-disabling hediff must be present");
                    TestAssert.IsTrue(pawn.health.hediffSet.DisablesNeed(NeedDefOf.Food), "Real active hediff stage disables food");
                    pawn.needs.AddOrRemoveNeedsAsAppropriate();
                    TestAssert.IsNull(pawn.needs.food, "Real needs tracker must remove disabled food");
                    TestAssert.IsTrue(pawn.HasFoodNeedDisabled(), "Actual living food waiver detection");
                    manager.CheckFoodNeedStateChanges();
                    Near(0f, manager.GetDebt(pawn), "Waiver clears existing ledger");
                    Near(0f, manager.GetPeakDebt(pawn), "Waiver clears peak");
                    TestAssert.IsNull(pawn.health.hediffSet.GetFirstHediffOfDef(EternalDefOf.Eternal_MetabolicRecovery),
                        "Waiver removes existing Recovery state");
                    TestAssert.IsTrue(manager.AddDebt(pawn, 5f), "Waived ordinary addition handled");
                    TestAssert.IsTrue(manager.AddResurrectionDebt(pawn, 7f), "Waived resurrection addition handled");
                    Near(0f, manager.GetDebt(pawn), "Neither waived addition creates debt");
                }
                finally
                {
                    try
                    {
                        foreach (var added in pawn.health.hediffSet.hediffs.Except(originalHediffs).ToList())
                        {
                            pawn.health.RemoveHediff(added);
                        }

                        foreach (var removed in originalHediffs.Except(pawn.health.hediffSet.hediffs).ToList())
                        {
                            pawn.health.AddHediff(removed);
                        }

                        pawn.needs.AddOrRemoveNeedsAsAppropriate();
                        TestAssert.IsNotNull(pawn.needs.food, "Food need must be restored after waiver fixture");
                        // Restore the fixture's exact original Need object as well as its nutrition.
                        pawn.needs.AllNeeds.Remove(pawn.needs.food);
                        pawn.needs.AllNeeds.Add(originalFood);
                        pawn.needs.food = originalFood;
                        originalFood.CurLevel = foodLevel;
                    }
                    finally { manager.UnregisterPawn(pawn); }
                }
            });
        }

        private static void LiveAndIsolatedGraphIdentity()
        {
            var live = EternalServiceContainer.Instance;
            var component = Eternal_Component.Current;
            TestAssert.Available(component, "Live Eternal component");
            TestAssert.IsTrue(live.IsInitialized, "Live container must be initialized");
            var liveBefore = Graph(live);
            var orchestrator = TestPawnFactory.RequiredField(component, "tickOrchestrator");
            Same(component.FoodDebtSystem, live.FoodDebtSystem, "Component/container debt identity");
            Same(component.HealingProcessor, live.HealingProcessor, "Component/container healer identity");
            Same(component.CorpseManager, live.CorpseManager, "Component/container corpse identity");
            Same(component.CorpseHealingProcessor, live.CorpseHealingProcessor, "Component/container corpse healer identity");
            Same(component.CorpsePreservation, live.CorpsePreservation, "Component/container preservation identity");
            Same(component.MapProtection, live.MapProtection, "Component/container map identity");
            Same(component.RegrowthManager, live.RegrowthManager, "Component/container regrowth identity");
            Same(live.FoodDebtSystem, TestPawnFactory.RequiredField(orchestrator, "_foodDebtSystem"), "Orchestrator debt identity");
            Same(live.HealingProcessor, TestPawnFactory.RequiredField(orchestrator, "_healingProcessor"), "Orchestrator healer identity");
            Same(live.CorpseManager, TestPawnFactory.RequiredField(orchestrator, "_corpseManager"), "Orchestrator corpse identity");
            Same(live.CorpseHealingProcessor, TestPawnFactory.RequiredField(orchestrator, "_corpseHealingProcessor"), "Orchestrator corpse healer identity");
            Same(live.CorpsePreservation, TestPawnFactory.RequiredField(orchestrator, "_corpsePreservation"), "Orchestrator preservation identity");
            Same(live.MapProtection, TestPawnFactory.RequiredField(orchestrator, "_mapProtection"), "Orchestrator map identity");
            Same(live.RegrowthManager, TestPawnFactory.RequiredField(orchestrator, "_regrowthManager"), "Orchestrator regrowth identity");
            Same(live.Settings, TestPawnFactory.RequiredField(orchestrator, "_settings"), "Orchestrator settings identity");
            var isolated = EternalServiceContainer.CreateNew();
            try
            {
                TestAssert.IsFalse(ReferenceEquals(live, isolated), "Isolated container differs from live instance");
                var firstDebt = new UnifiedFoodDebtManager();
                InitializeManagers(isolated, firstDebt);
                TestAssert.IsTrue(isolated.IsInitialized, "Isolated initialization");
                Same(firstDebt, isolated.FoodDebtSystem, "Isolated debt identity");
                var firstGraph = Graph(isolated);
                var replacementHealing = new EternalHealingProcessor();
                var replacementCorpses = new EternalCorpseManager();
                var replacementCorpseHealing = new EternalCorpseHealingProcessor();
                var replacementPreservation = new EternalCorpsePreservation();
                var replacementMap = new EternalMapProtection();
                var replacementRegrowth = new EternalRegrowthManager();
                isolated.UpdateManagerReferences(replacementHealing, replacementCorpses, replacementCorpseHealing,
                    replacementPreservation, replacementMap, replacementRegrowth);
                Same(replacementHealing, isolated.HealingProcessor, "Updated healer identity");
                Same(replacementCorpses, isolated.CorpseManager, "Updated corpse identity");
                Same(replacementCorpseHealing, isolated.CorpseHealingProcessor, "Updated corpse healer identity");
                Same(replacementPreservation, isolated.CorpsePreservation, "Updated preservation identity");
                Same(replacementMap, isolated.MapProtection, "Updated map identity");
                Same(replacementRegrowth, isolated.RegrowthManager, "Updated regrowth identity");
                Same(firstDebt, isolated.FoodDebtSystem, "Reference update keeps debt owner");
                Same(firstGraph[0], isolated.Settings, "Reference update keeps core settings");
                Same(firstGraph[3], isolated.DebtAccumulator, "Reference update keeps debt accumulator");
                isolated.Reset();
                TestAssert.IsFalse(isolated.IsInitialized, "Isolated reset state");
                foreach (var service in Graph(isolated))
                {
                    TestAssert.IsNull(service, "Isolated reset clears all services");
                }

                var secondDebt = new UnifiedFoodDebtManager();
                InitializeManagers(isolated, secondDebt);
                Same(secondDebt, isolated.FoodDebtSystem, "Isolated reinitialization debt");
                var secondGraph = Graph(isolated);
                for (int i = 0; i < firstGraph.Length; i++)
                {
                    TestAssert.IsNotNull(secondGraph[i], $"Reinitialized service {i}");
                    TestAssert.IsFalse(ReferenceEquals(firstGraph[i], secondGraph[i]), $"Fresh isolated service {i}");
                }
                Same(secondDebt, TestPawnFactory.RequiredField(isolated.DebtRepaymentProcessor, "_debtSystem"), "Repayment constructor debt wiring");
                Same(isolated.Settings, TestPawnFactory.RequiredField(isolated.DebtRepaymentProcessor, "_settings"), "Repayment constructor settings wiring");
            }
            finally
            {
                isolated.Reset();
                Same(live, EternalServiceContainer.Instance, "Live singleton untouched");
                Same(component, Eternal_Component.Current, "Live component untouched");
                var after = Graph(live);
                for (int i = 0; i < liveBefore.Length; i++)
                {
                    Same(liveBefore[i], after[i], $"Live service {i} untouched");
                }

                Same(orchestrator, TestPawnFactory.RequiredField(component, "tickOrchestrator"), "Live orchestrator untouched");
            }
        }

        private static void InitializeManagers(EternalServiceContainer container, UnifiedFoodDebtManager debt)
        {
            var healing = new EternalHealingProcessor();
            var corpses = new EternalCorpseManager();
            var corpseHealing = new EternalCorpseHealingProcessor();
            var preservation = new EternalCorpsePreservation();
            var map = new EternalMapProtection();
            var regrowth = new EternalRegrowthManager();
            container.Initialize(debt, healing, corpses, corpseHealing, preservation, map, regrowth);
            Same(healing, container.HealingProcessor, "Initialized healer identity");
            Same(corpses, container.CorpseManager, "Initialized corpse identity");
            Same(corpseHealing, container.CorpseHealingProcessor, "Initialized corpse healer identity");
            Same(preservation, container.CorpsePreservation, "Initialized preservation identity");
            Same(map, container.MapProtection, "Initialized map identity");
            Same(regrowth, container.RegrowthManager, "Initialized regrowth identity");
        }

        private static object[] Graph(EternalServiceContainer c)
        {
            return new object[] { c.Settings, c.RateCalculator, c.HediffHealingCalculator,
                c.DebtAccumulator, c.PartRestorer, c.ThresholdTracker, c.FoodCostProcessor,
                c.DebtRepaymentProcessor, c.SpaceModCache, c.FoodDebtSystem, c.HealingProcessor,
                c.CorpseManager, c.CorpseHealingProcessor, c.CorpsePreservation, c.MapProtection, c.RegrowthManager };
        }

        private static void TickFailureRestoresSpeed()
        {
            TestAssert.Available(Find.TickManager, "Tick manager for injected failure");
            var speed = Find.TickManager.CurTimeSpeed;
            int steps = 0;
            var nested = new TestSuiteResult(0, 0, new List<string>());
            nested.RunTest("ExpectedTickFailure", "InjectedStep", () =>
                TickAdvancer.AdvanceForTest(3, null, () => { steps++; throw new InvalidOperationException("injected-step-failure"); }));
            TestAssert.AreEqual(1, nested.Failed, "Injected tick failure must be a named failed test");
            TestAssert.AreEqual(0, nested.Passed, "Failed advancement cannot pass");
            string failure = nested.Failures.Single();
            TestAssert.IsTrue(failure.Contains("InjectedStep") && failure.Contains("index 0") && failure.Contains("injected-step-failure"),
                "Named tick failure keeps index and exception context");
            TestAssert.AreEqual(1, steps, "Failure prevents subsequent steps");
            TestAssert.AreEqual(speed, Find.TickManager.CurTimeSpeed, "Injected failure restores speed");
        }

        private static void TickFinalBoundaryAndValidation()
        {
            int steps = 0;
            TestAssert.AreEqual(2, TickAdvancer.AdvanceForTest(2, () => steps == 2, () => steps++), "Final boundary condition is checked");
            ExpectFailure(() => TickAdvancer.AdvanceTicks(-1), "Negative count must fail");
            ExpectFailure(() => TickAdvancer.AdvanceUntil(null, 1), "Missing condition must fail");
            ExpectFailure(() => TickAdvancer.AdvanceForTest(0, () => false, () => { }), "False condition at zero must fail");
            TestAssert.AreEqual(0, TickAdvancer.AdvanceForTest(0, () => true, () => { }), "True initial condition needs no ticks");
        }

        private static void AssertionsRejectNonfinite()
        {
            ExpectFailure(() => TestAssert.GreaterThan(float.NaN, 1f, "NaN"), "NaN greater-than must fail");
            ExpectFailure(() => TestAssert.LessThanOrEqual(float.PositiveInfinity, 1f, "Infinity"), "Infinite less-than must fail");
            ExpectFailure(() => TestAssert.InRange(float.NaN, 0f, 1f, "NaN"), "NaN range must fail");
            ExpectFailure(() => TestAssert.InRange(0.5f, float.NegativeInfinity, 1f, "Infinite bound"), "Infinite bound must fail");
        }

        private static void HarnessCountsUnavailableAndFailures()
        {
            var nested = new TestSuiteResult(0, 0, new List<string>());
            nested.RunTest("ExpectedHarnessOutcomes", "MissingFoodSetup", () => TestAssert.Available(null, "ExpectedMissingFood"));
            nested.RunTest("ExpectedHarnessOutcomes", "FailedSetup", () => TestAssert.IsTrue(false, "Expected setup failure"));
            nested.RunTest("ExpectedHarnessOutcomes", "SuccessfulAssertion", () => TestAssert.AreEqual(3, 3, "Expected success"));
            TestAssert.AreEqual(1, nested.Unavailable, "Unavailable setup has its own count");
            TestAssert.AreEqual(1, nested.Failed, "Failed setup is counted as failure");
            TestAssert.AreEqual(1, nested.Passed, "Only a successful body passes");
            TestAssert.IsTrue(nested.UnavailableReasons.Single().Contains("ExpectedMissingFood"), "Named unavailable setup is retained");
            TestAssert.IsTrue(nested.Failures.Single().Contains("Expected setup failure"), "Named failure is retained");
        }

        private static void ExpectFailure(Action action, string message)
        {
            bool failed = false;
            try { action(); } catch (TestFailedException) { failed = true; }
            TestAssert.IsTrue(failed, message);
        }

        private static void Near(float expected, float actual, string message)
        {
            TestAssert.InRange(actual, expected - 0.00001f, expected + 0.00001f, message);
        }

        private static void Same(object expected, object actual, string message)
        {
            TestAssert.IsNotNull(expected, message + " prerequisite");
            TestAssert.IsTrue(ReferenceEquals(expected, actual), message);
        }
    }
}
#endif
