// Relative Path: Eternal/Source/Eternal.Tests/Infrastructure/InGameBoundaryContractTests.cs
// Author: 0Shard
// Description: Decoded Debug registration, real boundary calls and protected cleanup; no host execution claims.
#if DEBUG
using System.Linq;
using System.Reflection.Emit;
#endif
using Eternal.Tests.UI;
using Xunit;

namespace Eternal.Tests.Infrastructure
{
    public class InGameBoundaryContractTests
    {
        private const string Runner = "Eternal.InGameTests.EternalTestRunner";
        private const string Suite = "Eternal.InGameTests.TestSuites.MaintainabilityBoundaryTests";
        private const string Factory = "Eternal.InGameTests.Helpers.TestPawnFactory";
#if DEBUG
        private const string Tick = "Eternal.InGameTests.Helpers.TickAdvancer";
#endif

        [Fact]
        public void ReleaseExcludesDebugGameHarness()
        {
#if DEBUG
            Assert.NotNull(MenuAssemblyContract.BaseType(Suite));
#else
            Assert.Null(MenuAssemblyContract.BaseType(Suite));
            Assert.Null(MenuAssemblyContract.BaseType(Runner));
            Assert.Null(MenuAssemblyContract.BaseType(Factory));
#endif
        }

#if DEBUG
        [Fact]
        public void BothActualDebugEntrypointsReachBoundarySuite()
        {
            foreach (var entry in new[] { "RunAllTests", "RunMaintainabilityBoundarySuite" })
            {
                var il = MenuAssemblyContract.ReachableInstructions(Runner, entry);
                Assert.Contains(il, i => i.Code == OpCodes.Call && Equals(i.Operand, Suite + ".RunAll"));
                Assert.Contains(il, i => i.Code == OpCodes.Ldstr && Equals(i.Operand, "MaintainabilityBoundary"));
                Assert.Contains("LudeonTK.DebugAction", MenuAssemblyContract.MethodAttributeText(Runner, entry));
            }
            Assert.Contains("Tests: MaintainabilityBoundary", MenuAssemblyContract.MethodAttributeText(Runner, "RunMaintainabilityBoundarySuite"));
        }

        [Fact]
        public void BothActualDebugEntrypointsReachModernRegrowthSuite()
        {
            const string modern = "Eternal.InGameTests.TestSuites.ModernRegrowthTests";
            foreach (string entry in new[] { "RunAllTests", "RunModernRegrowthSuite" })
            {
                var il = MenuAssemblyContract.ReachableInstructions(Runner, entry);
                Assert.Contains(il, i => i.Code == OpCodes.Call && Equals(i.Operand, modern + ".RunAll"));
                Assert.Contains(il, i => i.Code == OpCodes.Ldstr && Equals(i.Operand, "ModernRegrowth"));
                Assert.Contains("LudeonTK.DebugAction", MenuAssemblyContract.MethodAttributeText(Runner, entry));
            }
        }

        [Theory]
        [InlineData("ResurrectionCycleTests")]
        [InlineData("HediffSwapTests")]
        [InlineData("CaravanDeathTests")]
        [InlineData("MapProtectionTests")]
        [InlineData("RegrowthTests")]
        [InlineData("ModernRegrowthTests")]
        [InlineData("WorkPriorityTests")]
        [InlineData("ConsciousnessBuffTests")]
        [InlineData("MoodBuffTests")]
        [InlineData("ElixirPopulationCapTests")]
        [InlineData("ResurrectionSurvivalTests")]
        [InlineData("HealingGateTests")]
        [InlineData("ReactiveHealthTests")]
        [InlineData("MaintainabilityBoundaryTests")]
        public void EverySuiteUsesSharedResultAggregation(string suite)
        {
            string type = "Eternal.InGameTests.TestSuites." + suite;
            Assert.Contains(MenuAssemblyContract.Instructions(type, "RunAll"), i =>
                i.Code == OpCodes.Call && Equals(i.Operand, "Eternal.InGameTests.TestSuiteResult.RunTest"));
            Assert.Empty(MenuAssemblyContract.ParameterCounts(type, "RunTest"));
        }

        [Fact]
        public void SharedHarnessCleansBeforeCountingAndHasUnavailableOutcome()
        {
            var il = MenuAssemblyContract.Instructions("Eternal.InGameTests.TestSuiteResult", "RunTest");
            int invoke = il.FindIndex(i => Equals(i.Operand, "System.Action.Invoke"));
            int cleanup = il.FindIndex(i => Equals(i.Operand, Factory + ".CleanupAll"));
            int passed = il.FindIndex(i => i.Code == OpCodes.Stfld && Equals(i.Operand, "Passed"));
            Assert.True(invoke >= 0 && cleanup > invoke && passed > cleanup);
            Assert.Contains(MenuAssemblyContract.ReachableInstructions("Eternal.InGameTests.TestSuiteResult", "RunTest", true),
                i => Equals(i.Operand, Factory + ".CleanupAll"));
            Assert.Contains(il, i => i.Code == OpCodes.Isinst && Equals(i.Operand, "Eternal.InGameTests.TestUnavailableException"));
            Assert.Contains(il, i => i.Code == OpCodes.Stfld && Equals(i.Operand, "Unavailable"));
            Assert.Contains(il, i => i.Code == OpCodes.Stfld && Equals(i.Operand, "Failed"));
            Assert.Contains(il, i => i.Code == OpCodes.Newobj && Equals(i.Operand, "System.AggregateException..ctor"));
        }

        [Fact]
        public void RealBoundariesAreReachableFromRunAllAndCleanupIsInFinally()
        {
            var il = MenuAssemblyContract.ReachableInstructions(Suite, "RunAll");
            foreach (var call in new[] { "Eternal.Settings.SettingsAdapter..ctor", "Eternal.Eternal_Settings.CreateSnapshot",
                "Eternal.Healing.UnifiedHediffHealingCalculator.CalculateHediffHealing", "Eternal.Resources.UnifiedFoodDebtManager.RegisterPawn",
                "Eternal.Resources.UnifiedFoodDebtManager.AddDebt", "Eternal.Resources.UnifiedFoodDebtManager.AddResurrectionDebt",
                "Eternal.Resources.UnifiedFoodDebtManager.RepayDebt", "Eternal.Resources.UnifiedFoodDebtManager.GetPeakDebt",
                "Eternal.Resources.UnifiedFoodDebtManager.CheckFoodNeedStateChanges", "Eternal.Resources.DebtRepaymentProcessor.ProcessDebtRepayment",
                "Eternal.DI.EternalServiceContainer.CreateNew", "Eternal.DI.EternalServiceContainer.Initialize",
                "Eternal.DI.EternalServiceContainer.UpdateManagerReferences", Tick + ".AdvanceForTest" })
            {
                Assert.Contains(il, i => Equals(i.Operand, call));
            }

            var cleanup = MenuAssemblyContract.ReachableInstructions(Suite, "RunAll", true);
            Assert.Contains(cleanup, i => Equals(i.Operand, "Eternal.Resources.UnifiedFoodDebtManager.UnregisterPawn"));
            Assert.Contains(cleanup, i => i.Code == OpCodes.Stsfld && Equals(i.Operand, "settings"));
            Assert.Contains(cleanup, i => i.Code == OpCodes.Stsfld && Equals(i.Operand, "instance"));
            Assert.Contains(cleanup, i => Equals(i.Operand, "RimWorld.Need.set_CurLevel"));
            Assert.DoesNotContain(il, i => Equals(i.Operand, "Eternal.Eternal_Component..ctor"));
            Assert.DoesNotContain(il, i => (i.Operand as string)?.Contains("Scribe") == true);
        }

        [Fact]
        public void HealingObservationsReachIndependentAssertionsAndSettingsRestoreCapturedReferences()
        {
            var body = MenuAssemblyContract.ReachableInstructions(Suite, "RealHealingAdapters");
            var calculations = body.Select((instruction, index) => new { instruction, index }).Where(x =>
                Equals(x.instruction.Operand, "Eternal.Healing.UnifiedHediffHealingCalculator.CalculateHediffHealing")).ToArray();
            Assert.Equal(6, calculations.Length);
            foreach (var calculation in calculations)
            {
                // The observed return remains on the stack as Near's actual input.
                Assert.Equal(OpCodes.Ldstr, body[calculation.index + 1].Code);
                Assert.Equal(Suite + ".Near", body[calculation.index + 2].Operand);
            }
            var setup = MenuAssemblyContract.Instructions(Suite, "WithFreshSettings");
            var cleanup = MenuAssemblyContract.ReachableInstructions(Suite, "WithFreshSettings", true);
            foreach (var field in new[] { "settings", "instance" })
            {
                int captured = setup.FindIndex(i => i.Code == OpCodes.Ldsfld && Equals(i.Operand, field));
                int restored = cleanup.FindIndex(i => i.Code == OpCodes.Stsfld && Equals(i.Operand, field));
                Assert.True(captured >= 0 && restored > 0);
                int original = CalculationAdapterContractTests.Local(setup[captured + 1]);
                Assert.True(original >= 0);
                Assert.Equal(original, CalculationAdapterContractTests.Local(cleanup[restored - 1]));
            }
        }

        [Fact]
        public void FactoryOwnsBeforeSetupAndDoesNotAddObservedEssenceResult()
        {
            var generation = MenuAssemblyContract.Instructions(Factory, "GeneratePawn");
            int generated = generation.FindIndex(i => Equals(i.Operand, "Verse.PawnGenerator.GeneratePawn"));
            int owned = generation.FindIndex(generated + 1, i => (i.Operand as string)?.EndsWith(".Add") == true);
            int setup = generation.FindIndex(owned + 1, i => Equals(i.Operand, "Eternal.InGameTests.TestAssert.Available"));
            Assert.True(generated >= 0 && owned > generated && setup > owned);
            var hook = MenuAssemblyContract.Instructions(Factory, "SpawnEternalPawn");
            Assert.Contains(hook, i => Equals(i.Operand, "RimWorld.TraitSet.GainTrait"));
            Assert.Contains(hook, i => Equals(i.Operand, "Verse.HediffSet.HasHediff"));
            Assert.DoesNotContain(hook, i => Equals(i.Operand, "Verse.Pawn_HealthTracker.AddHediff"));
            var cleanup = MenuAssemblyContract.ReachableInstructions(Factory, "CleanupAll");
            Assert.Contains(cleanup, i => Equals(i.Operand, "Eternal.Interfaces.IFoodDebtWriter.UnregisterPawn"));
            Assert.Contains(cleanup, i => Equals(i.Operand, "Eternal.Healing.HediffHealingThresholdTracker.ClearPawnThresholds"));
            Assert.Contains(cleanup, i => Equals(i.Operand, "Eternal.EternalHediffHealer.ClearPawnHealingProgress"));
            Assert.Contains(cleanup, i => Equals(i.Operand, "Eternal.Corpse.EternalCorpseManager.UnregisterCorpse"));
        }

        [Fact]
        public void TickDefaultDrivesBothUpdatesAndRestoresSpeedInFinally()
        {
            var il = MenuAssemblyContract.Instructions(Tick, "Advance");
            Assert.Contains(il, i => Equals(i.Operand, "Verse.TickManager.DoSingleTick"));
            int component = il.FindIndex(i => Equals(i.Operand, "Eternal.Eternal_Component.get_Current"));
            int update = il.FindIndex(i => Equals(i.Operand, "Verse.GameComponent.GameComponentUpdate"));
            Assert.True(component >= 0 && update > component);
            Assert.Equal(CalculationAdapterContractTests.Local(il[component + 1]),
                CalculationAdapterContractTests.Local(il[update - 1]));
            Assert.Contains(il, i => i.Code == OpCodes.Throw);
            Assert.Contains(il, i => i.Code == OpCodes.Ldstr && (i.Operand as string)?.Contains("Tick advancement failed at index") == true);
            Assert.Contains(MenuAssemblyContract.ReachableInstructions(Tick, "Advance", true), i =>
                Equals(i.Operand, "Verse.TickManager.set_CurTimeSpeed"));
        }

        [Fact]
        public void ReactiveTestsAvoidGlobalClearAndProtectSettingRestoration()
        {
            var type = "Eternal.InGameTests.TestSuites.ReactiveHealthTests";
            foreach (var method in new[] { "InjectProgress", "InjectLegacyProgress", "InjectLegacyThreshold", "InjectLegacyHistory", "NonEternalHealthMutationsAreNotQueued" })
            {
                Assert.DoesNotContain(MenuAssemblyContract.Instructions(type, method), i => (i.Operand as string)?.EndsWith(".Clear") == true);
            }

            foreach (var method in new[] { "AddMutationDefersThresholdRegistration", "NonEternalReconciliationClearsDerivedState", "AmbiguousLegacyRowsResetForDuplicateHediffs", "DirectHediffSetRemovalIsPruned" })
            {
                Assert.Contains(MenuAssemblyContract.ReachableInstructions(type, method, true), i => i.Code == OpCodes.Stfld && Equals(i.Operand, "canHeal"));
            }
        }
#endif
    }
}
