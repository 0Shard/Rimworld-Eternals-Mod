// file path: Eternal/Source/Eternal/Eternal_Harmony.cs
// Author Name: 0Shard
// Date Created: 28-10-2025
// Date Last Modified: 11-07-2026
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
                // CreateClassProcessor(type).Patch() is exactly what PatchAll does per type;
                // types without Harmony annotations are a no-op.
                int appliedCount = 0;
                var failedClasses = new System.Collections.Generic.List<string>();
                foreach (var type in AccessTools.GetTypesFromAssembly(Assembly.GetExecutingAssembly()))
                {
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
        /// Verifies that critical Harmony patches were applied successfully.
        /// </summary>
        /// <param name="harmony">The Harmony instance to check.</param>
        private static void VerifyCriticalPatches(Harmony harmony)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var patchTypes = assembly.GetTypes()
                    .Where(t => t.GetMethods().Any(m => m.GetCustomAttributes(typeof(HarmonyAttribute), false).Length > 0))
                    .ToList();
                
                foreach (var patchType in patchTypes)
                {
                    var methods = patchType.GetMethods()
                        .Where(m => m.GetCustomAttributes(typeof(HarmonyAttribute), false).Length > 0);
                    
                    foreach (var method in methods)
                    {
                        var patches = Harmony.GetPatchInfo(method);
                        if (patches == null || (patches.Prefixes.Count == 0 && patches.Postfixes.Count == 0 && patches.Transpilers.Count == 0))
                        {
                            Log.Warning($"[Eternal] Patch {method.Name} in {patchType.Name} may not have been applied correctly");
                        }
                    }
                }
                
                Log.Message($"[Eternal] Verified {patchTypes.Count} patch types with {patchTypes.Sum(t => t.GetMethods().Count(m => m.GetCustomAttributes(typeof(HarmonyAttribute), false).Length > 0))} patch methods");
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "VerifyCriticalPatches", null, ex);
            }
        }
    }
}