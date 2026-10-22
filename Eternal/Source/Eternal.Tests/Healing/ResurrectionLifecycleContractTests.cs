// Relative Path: Eternal/Source/Eternal.Tests/Healing/ResurrectionLifecycleContractTests.cs
// Author: 0Shard
// Description: Compiled lifecycle wiring and exception-region contracts without loading Verse.
using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using Eternal.Tests.Infrastructure;
using Xunit;

namespace Eternal.Tests.Healing
{
    public class ResurrectionLifecycleContractTests
    {
        private const string Processor = "Eternal.Healing.EternalCorpseHealingProcessor";

        [Fact]
        public void RetainedRecoveryDestinationPrecedesEveryPlacementRetry()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var ownership = assembly.Instructions(assembly.Method(Processor, "EnsureResurrectedOwner"));
            int retained = ownership.FindIndex(i => Equals(i.Operand, "Eternal.World.WorldObject_EternalCrashSite.HasPendingPawn"));
            int placement = ownership.FindIndex(i => Equals(i.Operand, "RimWorld.Planet.Caravan.AddPawn"));
            int fallback = ownership.FindIndex(i => Equals(i.Operand, "Eternal.World.WorldObject_EternalCrashSite.TryRetainUnownedPawn"));
            Assert.True(retained >= 0 && placement > retained);
            var selected = ownership.Skip(retained + 1).Take(placement - retained - 1).ToArray();
            Assert.Contains(selected, i => i.Code == OpCodes.Ldc_I4_1);
            Assert.Contains(selected, i => i.Code == OpCodes.Ret ||
                (i.Code.FlowControl == FlowControl.Branch && (int)i.Operand > ownership[fallback].Offset));
            Assert.DoesNotContain(assembly.Instructions(assembly.Method(Processor, "Resurrect")),
                i => Equals(i.Operand, "RimWorld.Planet.Caravan.AddPawn"));
        }

        [Fact]
        public void NativeResultAndPersistentOwnershipGateSanitizationAndCallerCompletion()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var il = assembly.Instructions(assembly.Method(Processor, "Resurrect"));
            int native = il.FindIndex(i => Equals(i.Operand, "RimWorld.ResurrectionUtility.TryResurrect"));
            int owner = il.FindIndex(i => Equals(i.Operand, Processor + ".EnsureResurrectedOwner"));
            int sanitize = il.FindIndex(i => Equals(i.Operand, Processor + ".SanitizeHediffSetBeforeRestore"));
            Assert.True(native >= 0 && owner > native && sanitize > owner);
            Assert.Equal("System.Boolean", il[native].CallSignature.Value.ReturnType);
            Assert.True(IsLocalStore(il[native + 1]), "Native false must be retained as data, never popped");
            Assert.Contains(il.Skip(native + 1).Take(owner - native - 1), i => i.Code.FlowControl == FlowControl.Cond_Branch);
            foreach (string caller in new[] { "StartCorpseHealing", "ProcessCorpseInjuryHealing", "ProcessCorpseRegrowth" })
            {
                Assert.Contains(assembly.Instructions(assembly.Method(Processor, caller)),
                    i => Equals(i.Operand, Processor + ".IsCompleted"));
            }
            var ownership = assembly.Instructions(assembly.Method(Processor, "EnsureResurrectedOwner"));
            foreach (string required in new[] { "Verse.GenSpawn.Spawn", "RimWorld.Planet.Caravan.ContainsPawn",
                "RimWorld.Planet.WorldPawns.PassToWorld", "Eternal.World.WorldObject_EternalCrashSite.TryRetainUnownedPawn" })
            { Assert.Contains(ownership, i => Equals(i.Operand, required)); }
        }

        [Fact]
        public void PhysicalCaravanLookupAndSupportedCorpseAdmissionReachBothActions()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var lookup = assembly.ReachableInstructions(Processor, "FindCaravanContainingCorpse", false);
            Assert.Contains(lookup, i => Equals(i.Operand, "RimWorld.Planet.Caravan.get_AllThings"));
            Assert.Contains(lookup, i => Equals(i.Operand, "Eternal.Models.CorpseTrackingEntry.set_CaravanId") ||
                (i.Code == OpCodes.Stfld && Equals(i.Operand, "CaravanId")));
            var supports = assembly.Instructions(assembly.Method(Processor, "SupportsResurrection"));
            Assert.Contains(supports, i => i.Code == OpCodes.Isinst && Equals(i.Operand, "Verse.UnnaturalCorpse"));
            foreach (var entry in new[] { (Processor, "StartCorpseHealing"), ("Eternal.UI.ResurrectionGizmo", "Refresh") })
            { Assert.Contains(assembly.Instructions(assembly.Method(entry.Item1, entry.Item2)), i => Equals(i.Operand, Processor + ".SupportsResurrection")); }
        }

        [Theory]
        [InlineData("CompleteResurrection", 0)]
        [InlineData("ResurrectImmediately", 1)]
        public void EntryPointsDispatchToOneLifecycleWithDistinctModes(string entryPoint, int mode)
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var wrapper = assembly.Method(Processor, entryPoint);
            var instructions = assembly.Instructions(wrapper).Where(i => i.Code != OpCodes.Nop).ToList();
            var call = Assert.Single(instructions.Where(i => i.Code == OpCodes.Call));
            Assert.Equal(Processor + ".Resurrect", call.Operand);
            Assert.Equal(mode, instructions[instructions.IndexOf(call) - 1].Code == OpCodes.Ldc_I4_0 ? 0 :
                instructions[instructions.IndexOf(call) - 1].Code == OpCodes.Ldc_I4_1 ? 1 : -1);
            Assert.Equal(MethodAttributes.Private,
                assembly.Reader.GetMethodDefinition(wrapper).Attributes & MethodAttributes.MemberAccessMask);
            Assert.Equal(new[] { "Eternal.Models.CorpseTrackingEntry", Processor + "+ResurrectionMode" },
                assembly.Signature(assembly.Method(Processor, "Resurrect")).ParameterTypes.ToArray());
            Assert.Empty(assembly.Methods(Processor, "ApplyCompleteHealingEffects"));
        }

        [Fact]
        public void SharedLifecycleKeepsHealthCaravanAssignmentsAndSingleDebtTransfer()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var instructions = assembly.Instructions(assembly.Method(Processor, "Resurrect"));
            foreach (string target in new[]
            {
                Processor + ".ValidatePreResurrection", Processor + ".FindCaravanContainingCorpse",
                "RimWorld.ResurrectionUtility.TryResurrect", Processor + ".SanitizeHediffSetBeforeRestore",
                "Eternal.Eternal_Component.ReconcileLiveHealth",
                "Eternal.Models.PawnAssignmentSnapshot.ApplyTo", Processor + ".RestoreMissingBodyParts",
                "Eternal.EternalRegrowthManager.RemoveCompletedRegrowth",
                "Eternal.Interfaces.IFoodDebtWriter.RegisterPawn", "Eternal.Interfaces.IFoodDebtReader.GetDebt"
            })
            {
                Assert.Single(instructions.Where(i => IsCall(i) && Equals(i.Operand, target)));
            }

            Assert.DoesNotContain(instructions, i => IsCall(i) &&
                (Equals(i.Operand, "Eternal.Interfaces.IFoodDebtWriter.UnregisterPawn") ||
                Equals(i.Operand, "Eternal.Interfaces.IFoodDebtWriter.AddResurrectionDebt")));
            Assert.True(instructions.FindIndex(i => Equals(i.Operand, "Eternal.Interfaces.IFoodDebtWriter.RegisterPawn")) <
                instructions.FindIndex(i => Equals(i.Operand, Processor + ".ReleaseResurrectionTracking")));
            Assert.Single(assembly.Instructions(assembly.Method(Processor, "ReleaseResurrectionTracking"))
                .Where(i => Equals(i.Operand, "Eternal.Corpse.EternalCorpseManager.UnregisterCorpse")));

            int sanitize = instructions.FindIndex(i => Equals(i.Operand, Processor + ".SanitizeHediffSetBeforeRestore"));
            int reconcile = instructions.FindIndex(i => Equals(i.Operand, "Eternal.Eternal_Component.ReconcileLiveHealth"));
            Assert.True(sanitize < reconcile);
            foreach (string field in new[] { "hediffSet", "immunity" })
            {
                var assignments = instructions.Where(i => i.Code == OpCodes.Stfld && Equals(i.Operand, "Verse.Pawn_HealthTracker." + field)).ToArray();
                Assert.Equal(2, assignments.Length);
                Assert.True(assignments[0].Offset < instructions[sanitize].Offset);
                Assert.True(assignments[1].Offset > instructions[sanitize].Offset);
                Assert.True(assignments[1].Offset < instructions[reconcile].Offset);
            }
            foreach (string key in new[] { "EternalResurrectionImmediateDesc", "EternalResurrectionCompleteDesc",
                "CompleteResurrection", "ResurrectImmediately", ".PostWork" })
            {
                Assert.Contains(instructions, i => i.Code == OpCodes.Ldstr && Equals(i.Operand, key));
            }
        }

        [Fact]
        public void RollbackIsArmedBeforeFirstTrackerAndBothCleanupCallsLiveInFinally()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var method = assembly.Method(Processor, "Resurrect");
            var instructions = assembly.Instructions(method);
            var regions = assembly.Body(method).ExceptionRegions;
            foreach (string cleanup in new[] { Processor + ".AttemptHediffRestore",
                "Eternal.Corpse.EternalCorpseManager.UnmarkExpectedDestruction" })
            {
                var call = Assert.Single(instructions.Where(i => IsCall(i) && Equals(i.Operand, cleanup)));
                Assert.Contains(regions, r => r.Kind == ExceptionRegionKind.Finally && InHandler(r, call));
            }

            var rollbackCall = instructions.Single(i => Equals(i.Operand, Processor + ".AttemptHediffRestore"));
            var rollback = regions.Single(r => r.Kind == ExceptionRegionKind.Finally && InHandler(r, rollbackCall));
            var guard = instructions.First(i => InHandler(rollback, i) && i.Code != OpCodes.Nop);
            int flag = CompiledAssemblyContract.Local(guard);
            Assert.True(flag >= 0, "Rollback finally must inspect the swap-active local");
            int cleanTracker = instructions.FindIndex(i => i.Code == OpCodes.Newobj && Equals(i.Operand, "Verse.HediffSet..ctor"));
            Assert.True(cleanTracker >= 0);
            Assert.Contains(Enumerable.Range(1, cleanTracker - 1), index =>
                instructions[index - 1].Code == OpCodes.Ldc_I4_1 && IsLocalStore(instructions[index]) &&
                CompiledAssemblyContract.Local(instructions[index]) == flag);
        }

        private static bool InHandler(ExceptionRegion region, CompiledInstruction instruction)
            => instruction.Offset >= region.HandlerOffset && instruction.Offset < region.HandlerOffset + region.HandlerLength;
        private static bool IsCall(CompiledInstruction instruction)
            => instruction.Code == OpCodes.Call || instruction.Code == OpCodes.Callvirt;
        private static bool IsLocalStore(CompiledInstruction instruction)
            => instruction.Code.Name.StartsWith("stloc", StringComparison.Ordinal);
    }
}
