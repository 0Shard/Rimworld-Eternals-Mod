// Relative Path: Eternal/Source/Eternal.Tests/Infrastructure/HarmonyAnnotationFilterTests.cs
// Creation Date: 13-07-2026
// Last Edit: 13-07-2026
// Author: 0Shard
// Description: Regression test for the bootstrap annotation filter. Harmony's class processor
//              discovers auxiliary hooks (Prepare/Cleanup/TargetMethod) by METHOD NAME, so
//              feeding an unannotated class with a public instance Cleanup() method to
//              CreateClassProcessor threw "Non-static method requires a target" on every
//              startup (EternalHealingProcessor). The filter must skip such classes while
//              keeping every genuinely annotated patch class.

using HarmonyLib;
using Xunit;

namespace Eternal.Tests.Infrastructure
{
    /// <summary>
    /// Validates HarmonyAnnotationFilter.HasHarmonyAnnotation against the patterns that
    /// matter for the bootstrap loop.
    /// </summary>
    public class HarmonyAnnotationFilterTests
    {
        /// <summary>Mirror of the EternalHealingProcessor trap: no attributes, auxiliary-named method.</summary>
        private class PlainClassWithCleanupMethod
        {
            public void Cleanup() { }
        }

        [HarmonyPatch]
        private static class ClassLevelAnnotatedPatch
        {
            public static void Postfix() { }
        }

        private static class MethodLevelAnnotatedPatch
        {
            [HarmonyPostfix]
            public static void Postfix() { }
        }

        [Fact]
        public void PlainClassWithAuxiliaryNamedMethod_IsSkipped()
        {
            Assert.False(HarmonyAnnotationFilter.HasHarmonyAnnotation(typeof(PlainClassWithCleanupMethod)));
        }

        [Fact]
        public void ClassLevelAnnotation_IsKept()
        {
            Assert.True(HarmonyAnnotationFilter.HasHarmonyAnnotation(typeof(ClassLevelAnnotatedPatch)));
        }

        [Fact]
        public void MethodLevelAnnotation_IsKept()
        {
            Assert.True(HarmonyAnnotationFilter.HasHarmonyAnnotation(typeof(MethodLevelAnnotatedPatch)));
        }

        // NOTE: no direct assertion on EternalHealingProcessor here - scanning its method
        // attributes requires the real Assembly-CSharp at runtime (Krafs ref assemblies are
        // compile-time only), which Mono cannot load. PlainClassWithCleanupMethod mirrors
        // its exact shape (unannotated class, public instance Cleanup()).
    }
}
