// Author: 0Shard
// Description: Exact optional RJW reference ABI and Harmony injection checks without CLR game loading.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using Eternal.Tests.Infrastructure;
using Xunit;

namespace Eternal.Tests.Compatibility
{
    public class RjwReferenceContractTests
    {
        private static readonly string[] SurgeryParameters =
        {
            "Verse.Pawn", "Verse.BodyPartRecord", "Verse.Pawn",
            "System.Collections.Generic.List`1<Verse.Thing>", "RimWorld.Bill"
        };

        [OptionalModFact("RJW")]
        public void PinnedReference_AnatomyClassesAndPregnancyBaseHaveExpectedShape()
        {
            using var rjw = Open();
            Assert.Equal("RJW", rjw.Reader.GetString(rjw.Reader.GetAssemblyDefinition().Name));
            var contract = rjw.Type("rjw.ISexPartHediff");
            Assert.True((rjw.Reader.GetTypeDefinition(contract).Attributes & TypeAttributes.Interface) != 0);
            foreach (string type in new[] { "rjw.Hediff_NaturalSexPart", "rjw.Hediff_ArtificialSexPart" })
            {
                Assert.Contains(rjw.Reader.GetTypeDefinition(rjw.Type(type)).GetInterfaceImplementations(), handle =>
                    rjw.MemberName(rjw.Reader.GetInterfaceImplementation(handle).Interface) == "rjw.ISexPartHediff");
            }
            Assert.Equal("Verse.Hediff_AddedPart", rjw.MemberName(rjw.Reader.GetTypeDefinition(rjw.Type("rjw.Hediff_ArtificialSexPart")).BaseType));
            var pregnancy = rjw.Reader.GetTypeDefinition(rjw.Type("rjw.Hediff_BasePregnancy"));
            Assert.True((pregnancy.Attributes & TypeAttributes.Abstract) != 0);
            Assert.Equal("Verse.HediffWithComps", rjw.MemberName(pregnancy.BaseType));
            rjw.Method("rjw.ISexPartHediff", "get_AsHediff", Array.Empty<string>(), "Verse.HediffWithComps", 0, false);
        }

        [OptionalModFact("RJW")]
        public void PinnedReference_AnatomySavedFieldsIncludePrivateNullableAndCompState()
        {
            using var rjw = Open();
            foreach (var field in new[]
            {
                ("baseSize", "System.Single", false), ("originalOwnerSize", "System.Single", false),
                ("originalOwnerRace", "System.String", false), ("previousOwner", "System.String", false),
                ("fluidOverride", "rjw.SexFluidDef", true), ("partFluidMultiplier", "System.Single", false),
                ("isTransplant", "System.Boolean", false), ("forcedSize", "System.Nullable`1<System.Single>", true),
                ("discovered", "System.Boolean", false), ("initialised", "System.Boolean", true)
            })
            {
                RequireSavedField(rjw, "rjw.HediffComp_SexPart", field.Item1, field.Item2, field.Item3);
            }
            RequireSavedField(rjw, "rjw.HediffComp_GrowsWithOwner", "lastOwnerSize", "System.Single", false);
            RequireSavedField(rjw, "rjw.HediffComp_Ovipositor", "nextEggTick", "System.Int32", true);
            RequireSavedField(rjw, "rjw.HediffComp_Ovipositor", "eggInterval", "Verse.IntRange", false);
        }

        [OptionalModFact("RJW")]
        public void PinnedReference_CacheAndFallbackBindingsAreExact()
        {
            using var rjw = Open();
            foreach (string name in new[] { "genitals", "breasts", "anus", "torso" })
            {
                RequireField(rjw, "rjw.PawnData", name, "System.Collections.Generic.List`1<Verse.Hediff>", false);
            }
            rjw.Method("rjw.PawnExtensions", "GetRJWPawnData", new[] { "Verse.Pawn" }, "rjw.PawnData", 0, true);
            foreach (string field in new[] { "genitalsDef", "mechgenitalsDef", "breastsDef", "mechbreastsDef", "anusDef", "mechanusDef" })
            {
                RequireField(rjw, "rjw.xxx", field, "Verse.BodyPartDef", false, true);
            }
            rjw.Method("rjw.RaceGroupDef_Helper", "TryGetRaceGroupDef", new[] { "Verse.Pawn", "rjw.RaceGroupDef&" }, "System.Boolean", 0, true);
            rjw.Method("rjw.RacePartDef_Helper", "TryChooseRacePartDef", new[] { "Verse.Pawn", "rjw.RaceGroupDef", "Verse.BodyPartRecord", "rjw.RacePartDef&", "Verse.Gender" }, "System.Boolean", 0, true);
            rjw.Method("rjw.RacePartDef", "TryGetHediffDef", new[] { "Verse.HediffDef&" }, "System.Boolean", 0, false);
            rjw.Method("rjw.RacePartDef", "get_IsNone", Array.Empty<string>(), "System.Boolean", 0, false);
            rjw.Method("rjw.RacePartDef_Helper", "MakePart", new[] { "Verse.HediffDef", "Verse.Pawn", "Verse.BodyPartRecord", "rjw.RacePartDef" }, "Verse.Hediff", 0, true);
            rjw.Method("rjw.LegacySexPartAdder", "AddGenitals", new[] { "Verse.Pawn", "Verse.Pawn", "Verse.Gender", "Verse.BodyPartRecord", "Verse.HediffDef" }, "System.Void", 0, true);
            rjw.Method("rjw.LegacySexPartAdder", "GenderTechLevelCheck", new[] { "Verse.Pawn" }, "System.Double", 0, true);
            foreach (string method in new[] { "AddBreasts", "AddAnus" })
            {
                rjw.Method("rjw.LegacySexPartAdder", method, new[] { "Verse.Pawn", "Verse.BodyPartRecord", "Verse.Pawn" }, "System.Void", 0, true);
            }
        }

        [OptionalModFact("RJW")]
        public void PinnedReference_DynamicSurgeryPatchesMatchActualParametersAndState()
        {
            using var rjw = Open();
            using var eternal = CompiledBuildReferences.OpenProduction();
            const string mutation = "Eternal.Patches.Rjw.Rjw_SurgeryMutation_Patch";
            foreach (string type in new[] { "rjw.Recipe_RemovePart", "rjw.Recipe_InstallPart" })
            {
                var target = rjw.Method(type, "ApplyOnPawn", SurgeryParameters, "System.Void", 0, false);
                Assert.Equal(new[] { "pawn", "part", "billDoer", "ingredients", "bill" }, rjw.ParameterNames(target));
                ValidateCallbacks(eternal, mutation, rjw, target, type);
            }
            var selector = rjw.Method("rjw.LegacySexPartAdder", "GenderTechLevelCheck", new[] { "Verse.Pawn" }, "System.Double", 0, true);
            ValidateCallbacks(eternal, "Eternal.Patches.Rjw.Rjw_LegacyBiology_Patch", rjw, selector, "rjw.LegacySexPartAdder");
        }

        [Fact]
        public void AbsentReference_IsExplicitlyUnavailableOrRequiredFailure()
        {
            string missing = Path.Combine(Path.GetTempPath(), "eternal-rjw-absent-" + Guid.NewGuid().ToString("N"), "RJW.dll");
            var optional = new OptionalModFactAttribute("RJW", missing, false);
            Assert.Contains("RJW unavailable", optional.Skip);
            Assert.Contains("no integration verified", optional.Skip);
            Assert.Null(new OptionalModFactAttribute("RJW", missing, true).Skip);
            Assert.ThrowsAny<Exception>(() => SpaceModSeamPresenceTests.OpenRequiredPath("RJW", missing));
        }

        [Fact]
        public void IncompatibleReference_RejectsMissingTypeFieldAndChangedFieldSignature()
        {
            using var fixture = new CompiledAssemblyContract(new Uri(typeof(RjwReferenceContractTests).Assembly.CodeBase).LocalPath);
            Assert.Contains("missing type rjw.ISexPartHediff", Assert.Throws<InvalidOperationException>(() => fixture.Type("rjw.ISexPartHediff")).Message);
            const string type = "Eternal.Tests.Compatibility.RjwBindingFixture";
            Assert.Contains("missing field", Assert.Throws<InvalidOperationException>(() => RequireField(fixture, type, "absent", "System.Single", false)).Message);
            Assert.Contains("field type mismatch", Assert.Throws<InvalidOperationException>(() => RequireField(fixture, type, "forcedSize", "System.Nullable`1<System.Single>", false)).Message);
            Assert.Contains("field access mismatch", Assert.Throws<InvalidOperationException>(() => RequireField(fixture, type, "forcedSize", "System.Single", true)).Message);
        }

        [Fact]
        public void FinalizerContract_RejectsChangedReturnAndExceptionInjectionTypes()
        {
            using var fixture = new CompiledAssemblyContract(new Uri(typeof(RjwReferenceContractTests).Assembly.CodeBase).LocalPath);
            const string type = "Eternal.Tests.Compatibility.RjwBindingFixture";
            Assert.Throws<Xunit.Sdk.EqualException>(() => ValidateFinalizer(fixture, fixture.Method(type, "WrongReturn"), "System.Object", "System.Object"));
            Assert.Throws<Xunit.Sdk.EqualException>(() => ValidateFinalizer(fixture, fixture.Method(type, "WrongException"), "System.Object", "System.Object"));
        }

        private static CompiledAssemblyContract Open() => SpaceModSeamPresenceTests.OpenRequiredPath("RJW", CompiledBuildReferences.OptionalPath("RJW"));

        private static void ValidateCallbacks(CompiledAssemblyContract patches, string patchType,
            CompiledAssemblyContract targets, MethodDefinitionHandle target, string targetType)
        {
            var stateTypes = new List<string>();
            var definition = patches.Reader.GetTypeDefinition(patches.Type(patchType));
            var callbacks = definition.GetMethods().Where(method =>
            {
                string attributes = patches.AttributeText(patches.Reader.GetMethodDefinition(method).GetCustomAttributes());
                return attributes.Contains("HarmonyLib.HarmonyPrefix..ctor") || attributes.Contains("HarmonyLib.HarmonyPostfix..ctor");
            }).ToArray();
            Assert.NotEmpty(callbacks);
            foreach (var callback in callbacks)
            {
                HarmonyPatchSignatureTests.ValidateParameters(patches, callback, targets, target, targetType, stateTypes);
            }
            Assert.True(stateTypes.Distinct().Count() <= 1, patchType + ": incompatible prefix/postfix __state types");
            var finalizers = definition.GetMethods().Where(method => patches.AttributeText(
                patches.Reader.GetMethodDefinition(method).GetCustomAttributes()).Contains("HarmonyLib.HarmonyFinalizer..ctor")).ToArray();
            foreach (var finalizer in finalizers)
            {
                Assert.Single(stateTypes);
                HarmonyPatchSignatureTests.ValidateParameters(patches, finalizer, targets, target, targetType, new List<string>());
                ValidateFinalizer(patches, finalizer, stateTypes[0]);
            }
        }

        private static void ValidateFinalizer(CompiledAssemblyContract patches, MethodDefinitionHandle finalizer, string expectedState, string expectedPawn = "Verse.Pawn")
        {
            var signature = patches.Signature(finalizer);
            Assert.False(signature.Header.IsInstance);
            Assert.Equal("System.Exception", signature.ReturnType);
            Assert.Equal(new[] { "pawn", "__state", "__exception" }, patches.ParameterNames(finalizer));
            Assert.Equal(new[] { expectedPawn, expectedState, "System.Exception" }, signature.ParameterTypes.ToArray());
        }

        private static FieldDefinitionHandle RequireField(CompiledAssemblyContract assembly, string type, string name, string expectedType, bool isPrivate, bool isStatic = false)
        {
            var fields = assembly.Reader.GetTypeDefinition(assembly.Type(type)).GetFields().Where(handle =>
                assembly.Reader.GetString(assembly.Reader.GetFieldDefinition(handle).Name) == name).ToArray();
            string context = assembly.Path + ": " + type + "." + name;
            if (fields.Length != 1) { throw new InvalidOperationException(context + ": missing field or ambiguous binding"); }
            var field = assembly.Reader.GetFieldDefinition(fields[0]);
            if (field.DecodeSignature(assembly.Names, (object)null) != expectedType)
            {
                throw new InvalidOperationException(context + ": field type mismatch; expected " + expectedType);
            }
            if (((field.Attributes & FieldAttributes.Static) != 0) != isStatic ||
                (field.Attributes & FieldAttributes.FieldAccessMask) != (isPrivate ? FieldAttributes.Private : FieldAttributes.Public))
            {
                throw new InvalidOperationException(context + ": field access mismatch");
            }
            return fields[0];
        }

        private static void RequireSavedField(CompiledAssemblyContract assembly, string type, string name, string fieldType, bool isPrivate)
        {
            var field = RequireField(assembly, type, name, fieldType, isPrivate);
            var expose = assembly.Method(type, "CompExposeData", Array.Empty<string>(), "System.Void", 0, false);
            Assert.Contains(assembly.Instructions(expose), instruction => instruction.MemberHandle == (EntityHandle)field);
        }
    }

    internal sealed class RjwBindingFixture
    {
        public float forcedSize = 1;
        public static string WrongReturn(object pawn, object __state, Exception __exception) => string.Empty;
        public static Exception WrongException(object pawn, object __state, object __exception) => null;
    }
}
