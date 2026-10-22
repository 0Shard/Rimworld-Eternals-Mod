// Author: 0Shard
// Description: Compiled contracts for the captured reconciliation batch; runtime semantics belong to ReactiveHealth tests.
using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Eternal.Tests.Infrastructure;
using Xunit;

namespace Eternal.Tests.Healing
{
    public class ReconciliationAllocationContractTests
    {
        private const string Snapshot = "Eternal.Infrastructure.LiveHealthReconciliationSnapshot";
        private const string Key = "Eternal.Infrastructure.HealingDictionaryKey";

        [Theory]
        [InlineData("Eternal.EternalHediffHealer", "MigrateLegacyProgress", "legacyHealingProgress")]
        [InlineData("Eternal.EternalHediffSeverityTracker", "MigrateLegacyEntries", "legacyHistory")]
        [InlineData("Eternal.Healing.HediffHealingThresholdTracker", "MigrateLegacyEntries", "_legacyThresholds")]
        public void EmptyLegacyStateReturnsBeforeCandidateWork(string type, string migration, string field)
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var instructions = assembly.Instructions(assembly.Method(type, migration));
            int fieldRead = instructions.FindIndex(i => i.Code == OpCodes.Ldfld && Equals(i.Operand, field));
            int candidateWork = instructions.FindIndex(i => Equals(i.Operand, Snapshot + ".get_LegacyCandidates"));
            int countRead = instructions.FindIndex(fieldRead + 1,
                i => (i.Operand as string)?.EndsWith(".get_Count", StringComparison.Ordinal) == true);
            int branch = instructions.FindIndex(countRead + 1, i => i.Code.FlowControl == FlowControl.Cond_Branch);
            int emptyExit = instructions.FindIndex(branch + 1,
                i => i.Code == OpCodes.Ret || i.Code == OpCodes.Br || i.Code == OpCodes.Br_S);

            Assert.True(fieldRead >= 0 && countRead > fieldRead && branch > countRead);
            Assert.True(emptyExit > branch && emptyExit < candidateWork);
            Assert.True((int)instructions[branch].Operand > instructions[emptyExit].Offset);
            Assert.True((int)instructions[branch].Operand <= instructions[candidateWork].Offset);
            if (instructions[emptyExit].Code != OpCodes.Ret)
            {
                int target = instructions.FindIndex(i => i.Offset == (int)instructions[emptyExit].Operand);
                Assert.All(instructions.Skip(target), i => Assert.True(i.Code == OpCodes.Nop || i.Code == OpCodes.Ret));
            }
        }

        [Fact]
        public void CoordinatedOwnersShareTheCapturedBatchAndPublicValidationStaysSeparate()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var processor = assembly.Instructions(assembly.Method("Eternal.EternalHealingProcessor", "ReconcileLiveHealth"));
            Assert.Single(processor.Where(i => i.Code == OpCodes.Newobj && Equals(i.Operand, Snapshot + "..ctor")));
            foreach (string type in new[] { "Eternal.Healing.HediffHealingThresholdTracker", "Eternal.EternalHediffHealer" })
            {
                var call = Assert.Single(processor.Where(i => Equals(i.Operand, type + ".ReconcileValidatedHealth")));
                Assert.Equal(new[] { "Verse.Pawn", Snapshot }, call.CallSignature.Value.ParameterTypes.ToArray());
            }

            foreach (string type in new[] { "Eternal.Healing.HediffHealingThresholdTracker", "Eternal.EternalHediffHealer",
                "Eternal.EternalHediffSeverityTracker" })
            {
                var direct = assembly.Method(type, "ReconcileLiveHealth");
                Assert.Equal(MethodAttributes.Public, assembly.Reader.GetMethodDefinition(direct).Attributes & MethodAttributes.MemberAccessMask);
                Assert.True(assembly.Body(direct).ExceptionRegions.Length > 0);
                var captured = assembly.Method(type, "ReconcileValidatedHealth", new[] { "Verse.Pawn", Snapshot });
                Assert.Equal(MethodAttributes.Assembly, assembly.Reader.GetMethodDefinition(captured).Attributes & MethodAttributes.MemberAccessMask);
                var capturedInstructions = assembly.Instructions(captured);
                Assert.Contains(capturedInstructions, i => Equals(i.Operand, type + ".ReconcileLiveHealthCore"));
                Assert.DoesNotContain(capturedInstructions, i => i.Code == OpCodes.Newobj);
            }

            var progress = assembly.Instructions(assembly.Method("Eternal.EternalHediffHealer", "ReconcileLiveHealthCore"));
            Assert.Contains(progress, i => Equals(i.Operand, "Eternal.EternalHediffSeverityTracker.ReconcileValidatedHealth"));
        }

        [Fact]
        public void LegacyResolutionUsesTheOrderedCapturedKeysAndNeverReconstructsGameIdentity()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var lookup = assembly.Instructions(assembly.Method(Snapshot, "get_LegacyCandidates"));
            Assert.Contains(lookup, i => i.Code == OpCodes.Ldfld && Equals(i.Operand, "keys"));
            Assert.DoesNotContain(lookup, i => Equals(i.Operand, "CurrentKeys"));
            Assert.Single(lookup.Where(i => Equals(i.Operand, "Eternal.Infrastructure.LegacyHealingCandidateLookup.BuildUniqueLookup")));
            Assert.DoesNotContain(lookup, i => i.Code == OpCodes.Newobj && Equals(i.Operand, Key + "..ctor"));

            var snapshot = assembly.Reader.GetTypeDefinition(assembly.Type(Snapshot));
            Assert.DoesNotContain(snapshot.GetFields(), field =>
                (assembly.Reader.GetFieldDefinition(field).Attributes & FieldAttributes.Static) != 0);
            Assert.Empty(assembly.Methods(Snapshot, "ExposeData"));
            Assert.Empty(assembly.Methods(Snapshot, "Clear"));
        }
    }
}
