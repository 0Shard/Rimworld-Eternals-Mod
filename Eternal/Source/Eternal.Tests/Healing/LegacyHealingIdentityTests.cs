// Author: 0Shard
// Description: Runs production candidate resolution with BCL inputs; verifies game-dependent keys via compiled contracts.
using System;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using Eternal.Infrastructure;
using Eternal.Tests.Infrastructure;
using Xunit;

namespace Eternal.Tests.Healing
{
    public class LegacyHealingIdentityTests
    {
        private const string KeyType = "Eternal.Infrastructure.HealingDictionaryKey";
        private static readonly string[] ScalarParameters = { "System.Int32", "System.String", "System.String", "System.Int32" };

        [Fact]
        public void LegacyConstructor_DelegatesToRealScalarConstructorWithNegativeInstanceId()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var constructor = assembly.Method(KeyType, ".ctor", ScalarParameters.Take(3).ToArray(), "System.Void", 0, false);
            var scalarConstructor = assembly.Method(KeyType, ".ctor", ScalarParameters, "System.Void", 0, false);
            var instructions = assembly.Instructions(constructor);
            var call = Assert.Single(instructions.Where(instruction => instruction.Code == OpCodes.Call &&
                instruction.MemberHandle == (EntityHandle)scalarConstructor));

            Assert.Equal(ScalarParameters, call.CallSignature.Value.ParameterTypes.ToArray());
            Assert.Equal(OpCodes.Ldc_I4_M1, instructions[instructions.IndexOf(call) - 1].Code);
        }

        [Fact]
        public void CurrentConstructor_StoresIdentityAndNormalizesBothNullableLabels()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var instructions = assembly.Instructions(assembly.Method(KeyType, ".ctor", ScalarParameters, "System.Void", 0, false));
            foreach (string field in IdentityFields)
            {
                Assert.Single(instructions.Where(instruction => instruction.Code == OpCodes.Stfld && Equals(instruction.Operand, field)));
            }
            foreach (string field in new[] { "HediffDefName", "BodyPartLabel" })
            {
                int store = instructions.FindIndex(instruction => instruction.Code == OpCodes.Stfld && Equals(instruction.Operand, field));
                Assert.Equal(OpCodes.Ldsfld, instructions[store - 1].Code);
                Assert.Equal("System.String.Empty", instructions[store - 1].Operand);
                Assert.Equal(OpCodes.Pop, instructions[store - 2].Code);
                Assert.Equal(FlowControl.Cond_Branch, instructions[store - 3].Code.FlowControl);
                Assert.Equal(instructions[store].Offset, instructions[store - 3].Operand);
                Assert.Equal(OpCodes.Dup, instructions[store - 4].Code);
            }
        }

        [Fact]
        public void RealKey_EqualityAndHashIncludeThePersistedIdAndAllLegacyDimensions()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var equality = assembly.Instructions(assembly.Method(KeyType, "Equals", new[] { KeyType }, "System.Boolean", 0, false));
            var hash = assembly.Instructions(assembly.Method(KeyType, "GetHashCode", Array.Empty<string>(), "System.Int32", 0, false));
            foreach (string field in IdentityFields)
            {
                Assert.Equal(2, equality.Count(instruction => instruction.Code == OpCodes.Ldfld && Equals(instruction.Operand, field)));
                Assert.Contains(hash, instruction => instruction.Code == OpCodes.Ldfld && Equals(instruction.Operand, field));
            }
            Assert.Equal(2, equality.Count(instruction => Equals(instruction.Operand, "System.String.op_Equality")));
            int lastId = equality.FindLastIndex(instruction => instruction.Code == OpCodes.Ldfld && Equals(instruction.Operand, "HediffLoadID"));
            Assert.Equal(OpCodes.Ceq, equality[lastId + 1].Code);
            Assert.Equal(3, hash.Count(instruction => instruction.Code == OpCodes.Xor));

            var display = assembly.Instructions(assembly.Method(KeyType, "ToString", Array.Empty<string>(), "System.String", 0, false));
            Assert.Contains(display, instruction => Equals(instruction.Operand, "{0}@{1}#{2}({3})"));
        }

        private static readonly string[] IdentityFields = { "PawnThingIDNumber", "HediffDefName", "BodyPartLabel", "HediffLoadID" };

        [Fact]
        public void CandidateLookup_RetainsUniqueInstanceAndHasNoMissingCandidate()
        {
            var current = (42, "Cut", "Left arm", 101);
            var lookup = LegacyHealingCandidateLookup.BuildUniqueLookup(new[] { current }, ToLegacy);

            Assert.Equal(current, lookup[(42, "Cut", "Left arm", -1)].Value);
            Assert.False(lookup.ContainsKey((42, "Burn", "Left arm", -1)));
            Assert.False(lookup.ContainsKey(current));
            Assert.Empty(LegacyHealingCandidateLookup.BuildUniqueLookup(new (int, string, string, int)[0], ToLegacy));
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void CandidateLookup_DuplicateInstancesRemainAmbiguous(int count)
        {
            var keys = Enumerable.Range(101, count)
                .Select(id => (42, "Cut", "Left arm", id));
            var lookup = LegacyHealingCandidateLookup.BuildUniqueLookup(keys, ToLegacy);

            Assert.Single(lookup);
            Assert.Null(lookup[(42, "Cut", "Left arm", -1)]);
        }

        [Fact]
        public void CandidateLookup_RepeatedInputRemainsAmbiguous()
        {
            var current = (42, "Cut", "Left arm", 101);
            var lookup = LegacyHealingCandidateLookup.BuildUniqueLookup(new[] { current, current, current }, ToLegacy);

            Assert.Null(lookup[(42, "Cut", "Left arm", -1)]);
        }

        [Fact]
        public void CandidateLookup_SeparatesPawnDefAndExactPartLabel()
        {
            var keys = new[]
            {
                (42, "Cut", "Left arm", 101),
                (43, "Cut", "Left arm", 102),
                (42, "Burn", "Left arm", 103),
                (42, "Cut", "Right arm", 104),
                (42, "Cut", "", 105)
            };
            var lookup = LegacyHealingCandidateLookup.BuildUniqueLookup(keys, ToLegacy);

            Assert.Equal(keys.Length, lookup.Count);
            foreach (var key in keys)
            {
                Assert.Equal(key, lookup[(key.Item1, key.Item2, key.Item3, -1)].Value);
            }
        }

        private static (int, string, string, int) ToLegacy((int, string, string, int) key)
            => (key.Item1, key.Item2, key.Item3, -1);

        [Theory]
        [InlineData("Eternal.EternalHediffHealer", "legacyHealingProgress", "LegacyProgressKey", "MigrateLegacyProgress")]
        [InlineData("Eternal.EternalHediffSeverityTracker", "legacyHistory", "LegacyHistoryKey", "MigrateLegacyEntries")]
        [InlineData("Eternal.Healing.HediffHealingThresholdTracker", "_legacyThresholds", "LegacyThresholdKey", "MigrateLegacyEntries")]
        public void TrackerMigration_UsesSharedScalarKeyAndCandidateLookup(
            string type, string legacyField, string retiredType, string migration)
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            Assert.False(assembly.TryType(type + "+" + retiredType, out _));
            var field = Assert.Single(assembly.Reader.GetTypeDefinition(assembly.Type(type)).GetFields()
                .Where(handle => assembly.Reader.GetString(assembly.Reader.GetFieldDefinition(handle).Name) == legacyField));
            string fieldType = assembly.Reader.GetFieldDefinition(field).DecodeSignature(assembly.Names, (object)null);
            Assert.StartsWith("System.Collections.Generic.Dictionary`2<Eternal.Infrastructure.HealingDictionaryKey,", fieldType);

            var instructions = assembly.Instructions(assembly.Method(type, migration));
            var call = Assert.Single(instructions.Where(instruction => Equals(instruction.Operand,
                "Eternal.Infrastructure.LegacyHealingCandidateLookup.BuildUniqueLookup")));
            Assert.Equal(HandleKind.MethodSpecification, call.MemberHandle.Kind);
            var arguments = assembly.Reader.GetMethodSpecification((MethodSpecificationHandle)call.MemberHandle)
                .DecodeSignature(assembly.Names, (object)null);
            Assert.Equal(KeyType, Assert.Single(arguments));

            var projection = Assert.Single(instructions.Where(instruction => instruction.Code == OpCodes.Ldftn &&
                instruction.MemberHandle.Kind == HandleKind.MethodDefinition &&
                assembly.Signature((MethodDefinitionHandle)instruction.MemberHandle).ParameterTypes.SequenceEqual(new[] { KeyType })));
            var projectionBody = assembly.Instructions((MethodDefinitionHandle)projection.MemberHandle);
            Assert.Equal(IdentityFields.Take(3).ToArray(), projectionBody
                .Where(instruction => instruction.Code == OpCodes.Ldfld).Select(instruction => (string)instruction.Operand).ToArray());
            var legacyConstructor = assembly.Method(KeyType, ".ctor", ScalarParameters.Take(3).ToArray(), "System.Void", 0, false);
            var legacyConstruction = Assert.Single(projectionBody.Where(instruction => instruction.Code == OpCodes.Newobj &&
                instruction.MemberHandle == (EntityHandle)legacyConstructor));
            Assert.Equal(ScalarParameters.Take(3).ToArray(), legacyConstruction.CallSignature.Value.ParameterTypes.ToArray());
        }
    }
}
