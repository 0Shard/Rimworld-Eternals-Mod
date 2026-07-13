// file path: Eternal/Source/Eternal/Eternal_Harmony.cs
// Author Name: 0Shard
// Date Created: 28-10-2025
// Date Last Modified: 13-07-2026
// Description: Harmony patch initialization and management for Eternal mod.

using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;
using Eternal.Patches;
using Eternal.Compatibility;
using Eternal.Compat;
using Eternal.Exceptions;
using Eternal.Utils;

namespace Eternal
{
    /// <summary>
    /// Harmony patch initialization and management for Eternal mod.
    /// Automatically applies all Harmony patches in mod.
    /// 
    /// Current patches include:
    /// - Pawn_HealthTracker_Patch: Handles Eternal death and resurrection
    /// - MapParent_Patch: Prevents temporary map closure when Eternal anchors are active
    /// - GameComponentRegistration_Patch: Registers Eternal_Component in RimWorld 1.6
    /// - TraitSet_Patch: Handles Eternal trait management
    /// </summary>
    [StaticConstructorOnStartup]
    public static class Eternal_Harmony
    {
        static Eternal_Harmony()
        {
            try
            {
                // Create Harmony instance for Eternal mod
                var harmony = new Harmony("EternalTeam.Eternal");

                // Apply patches per class instead of a single PatchAll: one invalid patch class
                // must not abort patching for every class after it in metadata order.
                // CreateClassProcessor(type).Patch() is exactly what PatchAll does per type.
                // Only annotated classes are fed to the processor: Harmony ALSO discovers
                // auxiliary hooks (Prepare/Cleanup/TargetMethod) by METHOD NAME, so a plain
                // class with a public instance method named Cleanup would be invoked as a
                // static [HarmonyCleanup] hook and throw "Non-static method requires a target"
                // (this happened with EternalHealingProcessor.Cleanup()).
                int appliedCount = 0;
                var failedClasses = new System.Collections.Generic.List<string>();
                foreach (var type in AccessTools.GetTypesFromAssembly(Assembly.GetExecutingAssembly()))
                {
                    if (!HarmonyAnnotationFilter.HasHarmonyAnnotation(type))
                    {
                        continue;
                    }

                    try
                    {
                        harmony.CreateClassProcessor(type).Patch();
                        appliedCount++;
                    }
                    catch (Exception ex)
                    {
                        failedClasses.Add(type.FullName);
                        Log.Error($"[Eternal] Failed to apply patch class {type.FullName}: {ex}");
                    }
                }

                if (failedClasses.Count == 0)
                {
                    Log.Message("[Eternal] Harmony patches initialized successfully");
                }
                else
                {
                    Log.Error($"[Eternal] Harmony patching partial failure: {appliedCount} classes processed, " +
                        $"{failedClasses.Count} failed: {string.Join(", ", failedClasses)}");
                }

                // Verify critical patches were applied
                VerifyCriticalPatches(harmony);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "Eternal_Harmony static ctor", null, ex);
            }
        }
        
        /// <summary>
        /// Verifies that Harmony patching took effect at all. The previous implementation
        /// queried GetPatchInfo on the PATCH methods (always empty - it expects the patched
        /// target), producing false "may not have been applied" warnings on every startup.
        /// </summary>
        /// <param name="harmony">The Harmony instance to check.</param>
        private static void VerifyCriticalPatches(Harmony harmony)
        {
            try
            {
                var patchedTargets = harmony.GetPatchedMethods().ToList();
                if (patchedTargets.Count == 0)
                {
                    Log.Error("[Eternal] No Harmony patches were applied at all - the mod will not function");
                    return;
                }

                Log.Message($"[Eternal] Verified {patchedTargets.Count} game methods patched by Eternal");
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "VerifyCriticalPatches", null, ex);
            }
        }
    }

    /// <summary>
    /// Predicate for the bootstrap loop, kept off the [StaticConstructorOnStartup] class so
    /// tests can call it without triggering game patching.
    /// </summary>
    public static class HarmonyAnnotationFilter
    {
        /// <summary>
        /// True when Harmony's class processor has anything to do with this type: a
        /// Harmony attribute on the class itself or on any of its methods.
        /// </summary>
        public static bool HasHarmonyAnnotation(Type type)
        {
            if (type.GetCustomAttributes(typeof(HarmonyAttribute), inherit: true).Length > 0)
            {
                return true;
            }

            // Method-role attributes (HarmonyPrefix/Postfix/Transpiler/Finalizer) derive from
            // plain Attribute, NOT HarmonyAttribute - match any HarmonyLib attribute instead
            return type.GetMethods(AccessTools.all)
                .Any(m => m.GetCustomAttributes(false)
                    .Any(attribute => attribute.GetType().Namespace == nameof(HarmonyLib)));
        }
    }
}