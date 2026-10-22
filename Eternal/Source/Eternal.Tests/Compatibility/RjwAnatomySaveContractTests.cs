// Author: 0Shard
// Description: Compiled anatomy persistence schema; real save/load and reconciliation run in-game.
using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Eternal.Tests.Infrastructure;
using Xunit;

namespace Eternal.Tests.Compatibility
{
    public class RjwAnatomySaveContractTests
    {
        [Theory]
        [InlineData("RJW anatomy remains pending: Saved anatomy definition/class cannot be resolved")]
        [InlineData("RJW anatomy remains pending: RJW anatomy did not attach to its saved body slot")]
        public void EstablishedSupportedCohortRevalidatesHistoricalDiagnostics(string diagnostic)
            => Assert.True(Eternal.Compatibility.RjwAnatomyRetryPolicy.CanRevalidate(true, true, diagnostic));

        [Theory]
        [InlineData(false, true, "historical failed random fallback")]
        [InlineData(true, false, "unsupported saved version")]
        [InlineData(false, false, null)]
        public void UnknownHistoryAndUnsupportedSchemaKeepTheirGuards(bool baseline, bool supported, string diagnostic)
            => Assert.False(Eternal.Compatibility.RjwAnatomyRetryPolicy.CanRevalidate(baseline, supported, diagnostic));

        [Fact]
        public void RetryPolicyIsConsumedBeforeCohortRestoreAndClearsOnlyAfterCompletion()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            const string tracker = "Eternal.Compatibility.RjwAnatomyTracker";
            var retry = assembly.Instructions(assembly.Method(tracker, "RestoreSlots"));
            Assert.Contains(retry, i => Equals(i.Operand, "Eternal.Compatibility.RjwAnatomyRetryPolicy.CanRevalidate"));
            var restore = assembly.Instructions(assembly.Method(tracker, "RestoreCohort"));
            int attach = restore.FindLastIndex(i => Equals(i.Operand, "Eternal.Compatibility.RjwReflectionCache.ApplySnapshot"));
            int diagnostic = restore.FindLastIndex(i => i.Code == OpCodes.Stfld && Equals(i.Operand, "BlockedReason"));
            Assert.True(attach >= 0 && diagnostic > attach);
        }

        [Fact]
        public void EverySnapshotRecord_HasScribeConstructorAndExposesAllSavedFields()
        {
            using var eternal = CompiledBuildReferences.OpenProduction();
            foreach (string shortName in new[] { "RjwBodyPartLocator", "RjwAnatomySlot", "RjwAnatomySnapshot", "RjwSnapshotComp", "RjwSnapshotValue" })
            {
                string type = "Eternal.Compatibility." + shortName;
                eternal.Method(type, ".ctor", Array.Empty<string>(), "System.Void", 0, false);
                var expose = eternal.Method(type, "ExposeData", Array.Empty<string>(), "System.Void", 0, false);
                var instructions = eternal.Instructions(expose);
                foreach (var field in eternal.Reader.GetTypeDefinition(eternal.Type(type)).GetFields())
                {
                    var definition = eternal.Reader.GetFieldDefinition(field);
                    if ((definition.Attributes & FieldAttributes.Static) != 0) { continue; }
                    Assert.Contains(instructions, instruction => instruction.Code == OpCodes.Ldflda &&
                        instruction.MemberHandle == (System.Reflection.Metadata.EntityHandle)field);
                }
                Assert.Contains(instructions, instruction => instruction.Operand is string called && called.StartsWith("Verse.Scribe_", StringComparison.Ordinal));
            }
        }

        [Fact]
        public void SnapshotSchema_PreservesRepeatedPartsNullStateAndIdentityWithoutRjwAssemblyDependency()
        {
            using var eternal = CompiledBuildReferences.OpenProduction();
            AssertField(eternal, "RjwAnatomySlot", "Parts", "System.Collections.Generic.List`1<Eternal.Compatibility.RjwAnatomySnapshot>");
            AssertField(eternal, "RjwAnatomySlot", "HasBaseline", "System.Boolean");
            AssertField(eternal, "RjwAnatomySlot", "Pending", "System.Boolean");
            AssertField(eternal, "RjwAnatomySlot", "BlockedReason", "System.String");
            AssertField(eternal, "RjwAnatomySnapshot", "OriginalLoadId", "System.Int32");
            AssertField(eternal, "RjwAnatomySnapshot", "AttachedLoadId", "System.Int32");
            AssertField(eternal, "RjwSnapshotValue", "IsNull", "System.Boolean");
            AssertField(eternal, "RjwSnapshotValue", "TypeName", "System.String");
            AssertField(eternal, "RjwBodyPartLocator", "BodyDefName", "System.String");
            AssertField(eternal, "RjwBodyPartLocator", "ChildIndices", "System.Collections.Generic.List`1<System.Int32>");
            AssertField(eternal, "RjwBodyPartLocator", "DefNames", "System.Collections.Generic.List`1<System.String>");
            Assert.DoesNotContain(eternal.Reader.AssemblyReferences, handle => eternal.Reader.GetString(eternal.Reader.GetAssemblyReference(handle).Name) == "RJW");
            Assert.DoesNotContain(eternal.Reader.TypeReferences, handle => eternal.MemberName(handle).StartsWith("rjw.", StringComparison.Ordinal));
        }

        [Fact]
        public void TrackerPersistence_StoresExplicitSnapshotVersionWithSupportedSchemaConstant()
        {
            using var eternal = CompiledBuildReferences.OpenProduction();
            const string type = "Eternal.Compatibility.RjwAnatomyTracker";
            var fields = eternal.Reader.GetTypeDefinition(eternal.Type(type)).GetFields();
            var version = Assert.Single(fields.Where(field => eternal.Reader.GetString(eternal.Reader.GetFieldDefinition(field).Name) == "CurrentSnapshotVersion"));
            var constant = eternal.Reader.GetConstant(eternal.Reader.GetFieldDefinition(version).GetDefaultValue());
            Assert.Equal(1, eternal.Reader.GetBlobReader(constant.Value).ReadInt32());
            var savedVersion = Assert.Single(fields.Where(field => eternal.Reader.GetString(eternal.Reader.GetFieldDefinition(field).Name) == "snapshotVersion"));
            Assert.Equal("System.Int32", eternal.Reader.GetFieldDefinition(savedVersion).DecodeSignature(eternal.Names, (object)null));
            var instructions = eternal.Instructions(eternal.Method(type, "ExposeData", Array.Empty<string>(), "System.Void", 0, false));
            Assert.Contains(instructions, instruction => instruction.Code == OpCodes.Ldflda && instruction.MemberHandle == (System.Reflection.Metadata.EntityHandle)savedVersion);
            Assert.Contains(instructions, instruction => Equals(instruction.Operand, "rjwAnatomySnapshotVersion"));
        }

        [Fact]
        public void LegacySelector_PostfixUsesScopeAndPureCapWithoutAdditionalRandomDraw()
        {
            using var eternal = CompiledBuildReferences.OpenProduction();
            var postfix = eternal.Instructions(eternal.Method("Eternal.Patches.Rjw.Rjw_LegacyBiology_Patch", "KeepBiology"));
            Assert.Contains(postfix, instruction => Equals(instruction.Operand, "Eternal.Compatibility.RjwAnatomyTracker.IsLegacyFallbackActive"));
            Assert.Contains(postfix, instruction => Equals(instruction.Operand, "Eternal.Compatibility.RjwHealthPolicy.LimitLegacySelector"));
            var helper = eternal.Instructions(eternal.Method("Eternal.Compatibility.RjwHealthPolicy", "LimitLegacySelector"));
            Assert.Contains(helper, instruction => Equals(instruction.Operand, "System.Math.Min"));
            foreach (var instructions in new[] { postfix, helper })
            {
                Assert.DoesNotContain(instructions, instruction => instruction.Operand is string called && called.StartsWith("Verse.Rand.", StringComparison.Ordinal));
                Assert.DoesNotContain(instructions, instruction => instruction.Code.FlowControl == FlowControl.Branch && instruction.Operand is int target && target <= instruction.Offset);
                Assert.DoesNotContain(instructions, instruction => instruction.Code.FlowControl == FlowControl.Cond_Branch && instruction.Operand is int target && target <= instruction.Offset);
            }
        }

        private static void AssertField(CompiledAssemblyContract assembly, string shortType, string name, string expectedType)
        {
            var fields = assembly.Reader.GetTypeDefinition(assembly.Type("Eternal.Compatibility." + shortType)).GetFields();
            var handle = Assert.Single(fields.Where(field => assembly.Reader.GetString(assembly.Reader.GetFieldDefinition(field).Name) == name));
            Assert.Equal(expectedType, assembly.Reader.GetFieldDefinition(handle).DecodeSignature(assembly.Names, (object)null));
        }
    }
}
