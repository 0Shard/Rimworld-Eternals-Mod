// Author: 0Shard
// Description: Actual compiled invalid patches must fail exact target/injection contracts.
using System;
using System.Collections.Generic;
using Eternal.Tests.Infrastructure;
using HarmonyLib;
using Xunit;
using Xunit.Sdk;

namespace Eternal.Tests.Compatibility
{
    public class HarmonyNegativeFixtureTests
    {
        [Theory]
        [InlineData("WrongOverloadPatch", "right")]
        [InlineData("WrongArgumentTypePatch", "System.String")]
        [InlineData("VoidResultPatch", "System.Void")]
        [InlineData("RefResultMissingReferencePatch", "__result")]
        [InlineData("RefResultWrongTypePatch", "__result")]
        [InlineData("WrongInstancePatch", "__instance")]
        [InlineData("WrongFieldPatch", "___Number")]
        [InlineData("WrongFieldReferencePatch", "___Number")]
        [InlineData("WrongBoxedFieldPatch", "___Number")]
        [InlineData("WrongRunOriginalReferencePatch", "__runOriginal")]
        [InlineData("WrongArgsReferencePatch", "__args")]
        [InlineData("WrongOriginalMethodReferencePatch", "__originalMethod")]
        [InlineData("WrongStatePatch", "__state")]
        [InlineData("WrongOriginalMethodPatch", "__originalMethod")]
        [InlineData("MissingPassthroughPatch", "passthrough")]
        [InlineData("WrongPassthroughTypePatch", "passthrough")]
        [InlineData("WrongPassthroughReferencePatch", "passthrough")]
        public void InvalidPatchFailsWithSpecificContext(string patch, string reason)
        {
            using var assembly = new CompiledAssemblyContract(new Uri(typeof(HarmonyNegativeFixtureTests).Assembly.CodeBase).LocalPath);
            var dynamic = new List<string>();
            var failure = Assert.ThrowsAny<XunitException>(() => HarmonyPatchSignatureTests.Validate(assembly, assembly, dynamic,
                name => name == "Eternal.Tests.Compatibility." + patch));
            Assert.Contains(reason, failure.Message);
            Assert.Empty(dynamic);
        }

        [Fact]
        public void SupportedByrefReturnAndBoxedResultStillPass()
        {
            using var assembly = new CompiledAssemblyContract(new Uri(typeof(HarmonyNegativeFixtureTests).Assembly.CodeBase).LocalPath);
            var dynamic = new List<string>();
            Assert.Equal(2, HarmonyPatchSignatureTests.Validate(assembly, assembly, dynamic, name =>
                name == "Eternal.Tests.Compatibility.ValidRefResultPatch" || name == "Eternal.Tests.Compatibility.ValidBoxedResultPatch"));
            Assert.Empty(dynamic);
        }

        [Fact]
        public void OrdinaryNamedPassthroughAndSubsequentInjectionsPass()
        {
            using var assembly = new CompiledAssemblyContract(new Uri(typeof(HarmonyNegativeFixtureTests).Assembly.CodeBase).LocalPath);
            var dynamic = new List<string>();
            Assert.Equal(2, HarmonyPatchSignatureTests.Validate(assembly, assembly, dynamic, name =>
                name == "Eternal.Tests.Compatibility.ValidPassthroughPatch" || name == "Eternal.Tests.Compatibility.ValidReferencePassthroughPatch"));
            Assert.Empty(dynamic);
        }
    }

    internal class InjectionFixture
    {
        public int Number = 3;
        private static int result;
        public static void Nothing() { }
        public static ref int ByReference() => ref result;
        public int Read() => Number;
    }
    [HarmonyPatch(typeof(MetadataFixture), "Choose", new[] { typeof(int) })]
    internal static class WrongOverloadPatch { public static void Prefix(int right) { } }
    [HarmonyPatch(typeof(MetadataFixture), "Choose", new[] { typeof(int) })]
    internal static class WrongArgumentTypePatch { public static void Prefix(string left) { } }
    [HarmonyPatch(typeof(InjectionFixture), "Nothing")]
    internal static class VoidResultPatch { public static void Postfix(ref int __result) { } }
    [HarmonyPatch(typeof(InjectionFixture), "ByReference")]
    internal static class RefResultMissingReferencePatch { public static void Postfix(int __result) { } }
    [HarmonyPatch(typeof(InjectionFixture), "ByReference")]
    internal static class RefResultWrongTypePatch { public static void Postfix(ref string __result) { } }
    [HarmonyPatch(typeof(InjectionFixture), "Read")]
    internal static class WrongInstancePatch { public static void Prefix(string __instance) { } }
    [HarmonyPatch(typeof(InjectionFixture), "Read")]
    internal static class WrongFieldPatch { public static void Prefix(string ___Number) { } }
    [HarmonyPatch(typeof(InjectionFixture), "Read")]
    internal static class WrongFieldReferencePatch { public static void Prefix(ref object ___Number) { } }
    [HarmonyPatch(typeof(InjectionFixture), "Read")]
    internal static class WrongBoxedFieldPatch { public static void Prefix(object ___Number) { } }
    [HarmonyPatch(typeof(InjectionFixture), "Read")]
    internal static class WrongRunOriginalReferencePatch { public static void Prefix(ref bool __runOriginal) { } }
    [HarmonyPatch(typeof(InjectionFixture), "Read")]
    internal static class WrongArgsReferencePatch { public static void Prefix(ref object[] __args) { } }
    [HarmonyPatch(typeof(InjectionFixture), "Read")]
    internal static class WrongOriginalMethodReferencePatch { public static void Prefix(ref System.Reflection.MethodBase __originalMethod) { } }
    [HarmonyPatch(typeof(InjectionFixture), "Read")]
    internal static class WrongStatePatch
    {
        public static void Prefix(out int __state) { __state = 1; }
        public static void Postfix(string __state) { }
    }
    [HarmonyPatch(typeof(InjectionFixture), MethodType.Constructor)]
    internal static class WrongOriginalMethodPatch { public static void Prefix(System.Reflection.MethodInfo __originalMethod) { } }
    [HarmonyPatch(typeof(InjectionFixture), "ByReference")]
    internal static class ValidRefResultPatch { public static void Postfix(ref int __result) { } }
    [HarmonyPatch(typeof(MetadataFixture), "Choose", new[] { typeof(int) })]
    internal static class ValidBoxedResultPatch { public static void Postfix(object __result) { } }
    [HarmonyPatch(typeof(InjectionFixture), "Read")]
    internal static class MissingPassthroughPatch { public static int Postfix() => 1; }
    [HarmonyPatch(typeof(InjectionFixture), "Read")]
    internal static class WrongPassthroughTypePatch { public static int Postfix(string __state) => 1; }
    [HarmonyPatch(typeof(InjectionFixture), "Read")]
    internal static class WrongPassthroughReferencePatch { public static int Postfix(ref int __result) => __result; }
    [HarmonyPatch(typeof(InjectionFixture), "Read")]
    internal static class ValidPassthroughPatch { public static int Postfix(int previous, InjectionFixture __instance) => previous + __instance.Number; }
    [HarmonyPatch(typeof(InjectionFixture), "ByReference")]
    internal static class ValidReferencePassthroughPatch { public static ref int Postfix(ref int previous) => ref previous; }
}
