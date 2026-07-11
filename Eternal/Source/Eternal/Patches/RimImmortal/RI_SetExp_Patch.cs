// Relative Path: Eternal/Source/Eternal/Patches/RimImmortal/RI_SetExp_Patch.cs
// Creation Date: 11-07-2026
// Last Edit: 11-07-2026
// Author: 0Shard
// Description: Harmony patch for RimImmortal's SetExp method to instantly fill Eternals' cultivation progress.
// Only active when RimImmortal mod is loaded. Uses reflection to avoid compile-time dependency.

using System;
using System.Reflection;
using HarmonyLib;
using Verse;
using Eternal.Compatibility;
using Eternal.Exceptions;
using Eternal.Extensions;
using Eternal.Utils;

namespace Eternal.Patches.RimImmortal
{
    /// <summary>
    /// Patches Core.Pawn_EnergyTracker.SetExp so any positive exp gain instantly fills
    /// an Eternal pawn's cultivation progress to the current realm's EXP cap.
    /// SetExp adds the delta then caps at CurrentDef.EXP, so passing a huge delta
    /// guarantees the cap is reached regardless of the current exp value.
    /// </summary>
    [HarmonyPatch]
    public static class RI_SetExp_Patch
    {
        /// <summary>
        /// Delta large enough to exceed any realm's EXP cap; SetExp's own clamp
        /// (CurrentDef.EXP) enforces the exact per-realm limit.
        /// </summary>
        private const float EXP_FILL_DELTA = 1_000_000f;

        /// <summary>
        /// Guard: only apply this patch when RimImmortal is active.
        /// Uses ModsConfig.IsActive() via RimImmortalDetection — multiple mod ID variants are checked.
        /// Returning false skips the patch entirely (Harmony will not call TargetMethod).
        /// </summary>
        public static bool Prepare()
        {
            bool isActive = RimImmortalDetection.RimImmortalActive;

            if (!isActive)
            {
                // Only log at debug level — absence of RimImmortal is the normal case
                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message("[Eternal] Skipping RimImmortal SetExp patch — mod not active");
                }
                return false;
            }

            if (Eternal_Mod.settings?.debugMode == true)
            {
                Log.Message("[Eternal] RimImmortal detected - enabling instant-max cultivation progress patch");
            }

            return true;
        }

        /// <summary>
        /// Resolve the target method via reflection.
        /// Returns null to skip patching if the method cannot be found (API change).
        /// Harmony skips the patch entirely when TargetMethod returns null.
        /// </summary>
        public static MethodBase TargetMethod()
        {
            var method = RimImmortalDetection.SetExpMethod;

            if (method == null)
            {
                Log.Warning("[Eternal] Skipping RimImmortal SetExp patch — method not found (mod absent or API changed)");
            }

            return method;
        }

        /// <summary>
        /// Prefix: Replace positive exp gains with a cap-exceeding delta for Eternal pawns,
        /// so a single gain tick fills the realm progress bar. Non-positive deltas are
        /// left untouched (SetExp ignores them anyway).
        /// </summary>
        /// <param name="__instance">The Pawn_EnergyTracker instance</param>
        /// <param name="num">The exp delta (passed by ref to allow modification)</param>
        [HarmonyPrefix]
        public static void Prefix(object __instance, ref float num)
        {
            // Only boost positive exp gains
            if (num <= 0f)
                return;

            try
            {
                // Get the pawn from the energy tracker
                Pawn pawn = RimImmortalDetection.GetPawnFromEnergyTracker(__instance);

                if (pawn == null)
                    return;

                // Check if this pawn is an Eternal
                if (!pawn.IsValidEternal())
                    return;

                // Store original value for debug logging
                float originalNum = num;

                // Jump straight to the current realm's EXP cap (SetExp clamps the sum there)
                num = EXP_FILL_DELTA;

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] RimImmortal: Filled cultivation progress for {pawn.Name} " +
                               $"(gain {originalNum:F2} replaced by delta {num:F2})");
                }
            }
            catch (Exception ex)
            {
                // Graceful degradation - if patch fails, original exp gain still applies
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "RI_SetExp_Patch.Prefix", null, ex);
            }
        }
    }
}
