// Author: 0Shard
// Description: Compiled Harmony finalizer fixtures verify exception injection and shared mutation state.
using System;
using System.Collections.Generic;
using Eternal.Tests.Infrastructure;
using HarmonyLib;
using Xunit;
using Xunit.Sdk;

namespace Eternal.Tests.Compatibility
{
    public class RjwHarmonyFinalizerContractTests
    {
        [Fact]
        public void VoidReturningRefExceptionAndAttributedFinalizers_AreChecked()
        {
            using var assembly = Open();
            foreach (var fixture in new[] { ("RjwVoidFinalizerFixture", 1), ("RjwRefFinalizerFixture", 1), ("RjwStateFinalizerFixture", 2) })
            {
                var dynamic = new List<string>();
                Assert.Equal(fixture.Item2, HarmonyPatchSignatureTests.Validate(assembly, assembly, dynamic,
                    name => name == "Eternal.Tests.Compatibility." + fixture.Item1));
                Assert.Empty(dynamic);
            }
        }

        [Theory]
        [InlineData("RjwWrongFinalizerReturnFixture", "Finalizer return")]
        [InlineData("RjwWrongFinalizerExceptionFixture", "__exception")]
        [InlineData("RjwWrongRefFinalizerExceptionFixture", "__exception")]
        [InlineData("RjwPrefixExceptionFixture", "requires a finalizer")]
        [InlineData("RjwPostfixExceptionFixture", "requires a finalizer")]
        [InlineData("RjwWrongFinalizerStateFixture", "__state")]
        public void InvalidFinalizerOrMisplacedExceptionInjection_FailsWithContext(string fixture, string reason)
        {
            using var assembly = Open();
            var failure = Assert.ThrowsAny<XunitException>(() => HarmonyPatchSignatureTests.Validate(assembly, assembly, new List<string>(),
                name => name == "Eternal.Tests.Compatibility." + fixture));
            Assert.Contains(reason, failure.Message);
        }

        private static CompiledAssemblyContract Open() => new CompiledAssemblyContract(new Uri(typeof(RjwHarmonyFinalizerContractTests).Assembly.CodeBase).LocalPath);
    }

    [HarmonyPatch(typeof(InjectionFixture), "Nothing")]
    internal static class RjwVoidFinalizerFixture { public static void Finalizer(Exception __exception) { } }
    [HarmonyPatch(typeof(InjectionFixture), "Nothing")]
    internal static class RjwRefFinalizerFixture { public static Exception Finalizer(ref Exception __exception) => __exception; }
    [HarmonyPatch(typeof(InjectionFixture), "Nothing")]
    internal static class RjwStateFinalizerFixture
    {
        [HarmonyPrefix] public static void Begin(out int __state) { __state = 1; }
        [HarmonyFinalizer] public static Exception End(int __state, Exception __exception) => __exception;
    }
    [HarmonyPatch(typeof(InjectionFixture), "Nothing")]
    internal static class RjwWrongFinalizerReturnFixture { public static int Finalizer(Exception __exception) => 1; }
    [HarmonyPatch(typeof(InjectionFixture), "Nothing")]
    internal static class RjwWrongFinalizerExceptionFixture { public static void Finalizer(string __exception) { } }
    [HarmonyPatch(typeof(InjectionFixture), "Nothing")]
    internal static class RjwWrongRefFinalizerExceptionFixture { public static void Finalizer(ref object __exception) { } }
    [HarmonyPatch(typeof(InjectionFixture), "Nothing")]
    internal static class RjwPrefixExceptionFixture { public static void Prefix(Exception __exception) { } }
    [HarmonyPatch(typeof(InjectionFixture), "Nothing")]
    internal static class RjwPostfixExceptionFixture { public static void Postfix(Exception __exception) { } }
    [HarmonyPatch(typeof(InjectionFixture), "Nothing")]
    internal static class RjwWrongFinalizerStateFixture
    {
        public static void Prefix(out int __state) { __state = 1; }
        public static void Finalizer(string __state) { }
    }
}
