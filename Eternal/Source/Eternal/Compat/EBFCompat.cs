// Relative Path: Eternal/Source/Eternal/Compat/EBFCompat.cs
// Creation Date: 29-12-2025
// Last Edit: 20-02-2026
// Author: 0Shard
// Description: Soft dependency for Elite Bionics Framework (EBF). Uses multi-method detection
//              (PackageId with StartsWith for Steam suffix handling, plus assembly fallback) and
//              direct reflection for calling EBF methods. Fixes the EBF protocol violation by using
//              BodyPartRecord instead of BodyPartDef for max health.

using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;
using Eternal.Exceptions;
using Eternal.Utils;

namespace Eternal.Compat
{
    /// <summary>
    /// Provides soft dependency support for Elite Bionics Framework (EBF).
    /// Uses direct reflection for EBF method calls with lazy initialization.
    /// </summary>
    /// <remarks>
    /// The EBF protocol requires using BodyPartRecord (not BodyPartDef) when calculating
    /// max health, as the same BodyPartDef can have different max HP values depending on
    /// installed bionics. This class provides a unified method that uses EBF when available
    /// and falls back to vanilla otherwise.
    ///
    /// Detection uses two methods:
    /// 1. PackageId with StartsWith (handles Steam's _steam suffix)
    /// 2. Assembly scanning fallback (catches unusual installations)
    ///
    /// Method invocation uses cached reflection with AccessTools for reliable method lookup.
    /// </remarks>
    public static class EBFCompat
    {
        // Use lowercase for StartsWith comparison
        private const string EBF_PACKAGE_ID_PREFIX = "v1024.ebframework";
        private const string EBF_ASSEMBLY_NAME = "EliteBionicsFramework";

        private static bool? _ebfAvailable;
        private static bool _detectionLogged;

        // Cached reflection for EBF method calls
        private static MethodInfo _ebfGetMaxHealth;
        private static MethodInfo _ebfSuppressWarning;
        private static bool _methodLookedUp;

        /// <summary>
        /// Gets whether EBF is loaded using robust multi-method detection.
        /// </summary>
        public static bool IsEBFAvailable
        {
            get
            {
                if (_ebfAvailable.HasValue)
                    return _ebfAvailable.Value;

                // Debug logging to help identify EBF's actual PackageId/Assembly name
                if (Eternal_Mod.settings?.debugMode == true && !_detectionLogged)
                {
                    Log.Message("[Eternal] EBF Detection - Scanning loaded mods:");
                    foreach (var pack in LoadedModManager.RunningMods)
                    {
                        if (pack.PackageId?.ToLowerInvariant()?.Contains("ebf") == true ||
                            pack.PackageId?.ToLowerInvariant()?.Contains("bionic") == true ||
                            pack.PackageId?.ToLowerInvariant()?.Contains("elite") == true)
                        {
                            Log.Message($"[Eternal]   Potential EBF match: {pack.PackageId}");
                        }
                    }
                }

                // Method 1: PackageId with StartsWith (handles _steam suffix)
                var ebfMod = LoadedModManager.RunningMods
                    .FirstOrDefault(pack => pack.PackageId?.ToLowerInvariant()?.StartsWith(EBF_PACKAGE_ID_PREFIX) == true);

                if (ebfMod != null)
                {
                    _ebfAvailable = true;
                    LogDetection($"PackageId match: {ebfMod.PackageId}");
                    return true;
                }

                // Method 2: Assembly scanning fallback
                var ebfAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(asm => asm.GetName().Name.Equals(EBF_ASSEMBLY_NAME, StringComparison.OrdinalIgnoreCase));

                if (ebfAssembly != null)
                {
                    _ebfAvailable = true;
                    LogDetection($"Assembly scan: {ebfAssembly.GetName().Name}");
                    return true;
                }

                // EBF not found
                _ebfAvailable = false;
                LogDetection(null);
                return false;
            }
        }

        /// <summary>
        /// Logs the detection result once.
        /// </summary>
        private static void LogDetection(string method)
        {
            if (_detectionLogged)
                return;

            _detectionLogged = true;

            if (method != null)
                Log.Message($"[Eternal] EBF detected via {method}");
            else
                Log.Message("[Eternal] EBF not detected (checked PackageId and assemblies)");
        }

        /// <summary>
        /// Gets the max health for a body part, using EBF if available.
        /// </summary>
        /// <param name="part">The body part record (not def)</param>
        /// <param name="pawn">The pawn owning the body part</param>
        /// <returns>Max health value, accounting for bionics if EBF is loaded</returns>
        public static float GetMaxHealth(BodyPartRecord part, Pawn pawn)
        {
            if (part == null || pawn == null)
                return 1f;

            // Fast path: EBF not available
            if (!IsEBFAvailable)
                return part.def.GetMaxHealth(pawn);

            // Lazy lookup of EBF methods (one-time cost)
            if (!_methodLookedUp)
            {
                _methodLookedUp = true;
                _ebfGetMaxHealth = AccessTools.Method("EBF.EBFEndpoints:GetMaxHealthWithEBF");
                _ebfSuppressWarning = AccessTools.Method("EBF.Patches.PostFix_BodyPart_GetMaxHealth:SuppressNextWarning");

                if (_ebfGetMaxHealth != null)
                    Log.Message("[Eternal] EBF integration: GetMaxHealthWithEBF method found");
                else
                    Log.Warning("[Eternal] EBF integration: GetMaxHealthWithEBF method not found, using vanilla fallback");
            }

            // Try EBF method via reflection
            if (_ebfGetMaxHealth != null)
            {
                try
                {
                    return (float)_ebfGetMaxHealth.Invoke(null, new object[] { part, pawn, true });
                }
                catch (Exception ex)
                {
                    EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                        "EBFCompat.GetMaxHealth", pawn, ex);
                    return part.def.GetMaxHealth(pawn);
                }
            }

            // Fallback - suppress EBF warning before calling vanilla
            _ebfSuppressWarning?.Invoke(null, null);
            return part.def.GetMaxHealth(pawn);
        }
    }
}
