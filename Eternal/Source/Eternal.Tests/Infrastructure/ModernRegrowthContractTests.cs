// Relative Path: Eternal/Source/Eternal.Tests/Infrastructure/ModernRegrowthContractTests.cs
// Creation Date: 13-10-2026
// Author: 0Shard
// Description: Compiled live-regrowth wiring contracts; Verse behavior is exercised separately in the host suite.
using System;
using System.Linq;
using System.Reflection.Emit;
using Eternal.Tests.UI;
using Xunit;

namespace Eternal.Tests.Infrastructure
{
    public class ModernRegrowthContractTests
    {
        private const string State = "Eternal.EternalRegrowthState";
        private const string Manager = "Eternal.EternalRegrowthManager";
        private const string Suite = "Eternal.InGameTests.TestSuites.ModernRegrowthTests";

        [Fact]
        public void ManagerCapturesHediffBatchBeforeConstructingItsProjection()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var constructor = assembly.Method(State, ".ctor", new[]
            {
                "Verse.Pawn", "System.Collections.Generic.List`1<Eternal.EternalRegrowing_Hediff>"
            });
            var constructorIl = assembly.Instructions(constructor);
            int batch = constructorIl.FindIndex(i => i.Code == OpCodes.Stfld && Equals(i.Operand, "capturedHediffs"));
            Assert.True(batch > 0);
            Assert.Equal(OpCodes.Ldarg_2, constructorIl[batch - 1].Code);

            var il = MenuAssemblyContract.Instructions(Manager, "GetRegrowthState", "Verse.Pawn");
            int read = il.FindIndex(i => Equals(i.Operand, Manager + ".GetRegrowingHediffs"));
            int capture = il.FindIndex(i => (i.Operand as string)?.EndsWith(".ToList", StringComparison.Ordinal) == true);
            int project = il.FindIndex(i => i.Code == OpCodes.Newobj && Equals(i.Operand, State + "..ctor"));
            Assert.True(read >= 0 && capture > read && project > capture,
                "The wrapper must receive a captured hediff list rather than a live manager enumeration.");
            Assert.Equal("Verse.Pawn", il[project].CallSignature.Value.ParameterTypes[0]);
            Assert.Equal(2, il[project].CallSignature.Value.ParameterTypes.Length);
        }

        [Fact]
        public void HealingDelegatesPawnAndAmountThenInvalidatesTheProjectionCache()
        {
            var il = MenuAssemblyContract.Instructions(State, "ApplyHealing", "System.Single")
                .Where(i => i.Code != OpCodes.Nop).ToList();
            int progress = il.FindIndex(i => Equals(i.Operand, Manager + ".ProgressRegrowth"));
            Assert.True(progress >= 3);
            Assert.Equal(OpCodes.Ldfld, il[progress - 3].Code);
            Assert.Equal("pawn", il[progress - 3].Operand);
            Assert.Equal(OpCodes.Ldarg_1, il[progress - 2].Code);
            Assert.Equal(OpCodes.Ldc_R4, il[progress - 1].Code);
            Assert.Equal(float.PositiveInfinity, il[progress - 1].Operand);
            int invalidate = il.FindIndex(progress + 1, i => i.Code == OpCodes.Stfld && Equals(i.Operand, "cacheValid"));
            Assert.True(invalidate > progress);
            Assert.Equal(OpCodes.Ldc_I4_0, il[invalidate - 1].Code);
            Assert.DoesNotContain(il, i => Equals(i.Operand, "Verse.Pawn_HealthTracker.RestorePart"));
        }

        [Fact]
        public void CompletionReadsCapturedBatchWhileManagementConsumesTheSamePhaseAndEligibilityView()
        {
            var completion = MenuAssemblyContract.Instructions(State, "IsRegrowthComplete");
            Assert.Contains(completion, i => i.Code == OpCodes.Ldfld && Equals(i.Operand, "capturedHediffs"));
            Assert.Contains(completion, i => (i.Operand as string)?.EndsWith(".All", StringComparison.Ordinal) == true);
            Assert.DoesNotContain(completion, i => (i.Operand as string)?.StartsWith(Manager + ".", StringComparison.Ordinal) == true);
            var view = MenuAssemblyContract.Calls("Eternal.UI.Management.ManagementTabModel", "CaptureParts");
            foreach (var call in new[] { "get_partPhases", "get_partProgress", "GetPhaseSimpleName", "CanUpdatePart" })
            {
                Assert.Contains(State + "." + call, view);
            }
            Assert.Contains(Manager + ".CanCompleteRegrowth", view);
        }

        [Fact]
        public void ModernManagerRetainsCriticalParentAndOriginalAnatomyRestorationGates()
        {
            var start = MenuAssemblyContract.Calls(Manager, "CanStartRegrowthForPart");
            Assert.Contains("Eternal.Constants.CriticalPartConstants.GetSequenceIndex", start);
            Assert.Contains(Manager + ".PartAvailableForChildRegrowth", start);
            Assert.Contains(Manager + ".GetRegrowthSeverity", start);
            Assert.Contains("Verse.HediffSet.PartIsMissing", MenuAssemblyContract.Calls(Manager, "CanCompleteRegrowth"));
            Assert.Contains(Manager + ".CanCompleteRegrowth", MenuAssemblyContract.Calls(Manager, "ProgressRegrowth"));
            Assert.Contains("Eternal.Compatibility.RjwAnatomyTracker.RestorePart", MenuAssemblyContract.Calls(Manager, "CompleteRegrowth"));
        }

        [Fact]
        public void HostProjectionCasesAreDebugOnly()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
#if DEBUG
            Assert.True(assembly.TryType(Suite, out _));
            var cases = MenuAssemblyContract.ReachableInstructions(Suite, "RunAll");
            foreach (var method in new[] { "ExactPhaseBoundaries", "UiEligibilityAndCacheRefresh",
                "ChildStartAndCompletionGates", "CapturedBatchCompletesBeforeReleasedChildren" })
            {
                Assert.Contains(cases, i => Equals(i.Operand, Suite + "." + method));
            }
            Assert.Contains(cases, i => Equals(i.Operand, State + ".ApplyHealing"));
            Assert.Contains(cases, i => Equals(i.Operand, Manager + ".StartRegrowth"));
            Assert.Contains(cases, i => Equals(i.Operand, "Eternal.InGameTests.TestSuiteResult.RunTest"));
            Assert.DoesNotContain(cases, i => (i.Operand as string)?.StartsWith(
                "Eternal.InGameTests.Helpers.TickAdvancer.", StringComparison.Ordinal) == true);
#else
            Assert.False(assembly.TryType(Suite, out _));
#endif
        }
    }
}
