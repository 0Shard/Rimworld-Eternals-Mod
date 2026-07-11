// Relative Path: Eternal/Source/Eternal/Patches/RimImmortal/RI_SetEnergy_Patch.cs
// Creation Date: 28-12-2025
// Last Edit: 11-07-2026
// Author: 0Shard
// Description: Harmony patch for RimImmortal's SetEnergy method to instantly fill Eternals' cultivation energy.
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
    /// Patches Core.Pawn_EnergyTracker.SetEnergy so any positive energy gain instantly fills
    /// an Eternal pawn's cultivation energy to the current level's MaxEnergy.
    /// SetEnergy adds the delta then caps at CurrentDef.MaxEnergy, so passing MaxEnergy as the
    /// delta guarantees the cap is reached regardless of the current energy value.
    /// </summary>
    [HarmonyPatch]
    public static class RI_SetEnergy_Patch
    {
        /// <summary>
        /// Fallback delta when the MaxEnergy reflection chain fails; SetEnergy's own cap
        /// clamps the result to the level's MaxEnergy either way.
        /// </summary>
        private const float FALLBACK_ENERGY_DELTA = 1_000_000f;

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
                    Log.Message("[Eternal] Skipping RimImmortal SetEnergy patch — mod not active");
                }
                return false;
            }

            if (Eternal_Mod.settings?.debugMode == true)
            {
                Log.Message("[Eternal] RimImmortal detected - enabling instant-max cultivation energy patch");
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
            var method = RimImmortalDetection.SetEnergyMethod;

            if (method == null)
            {
                Log.Warning("[Eternal] Skipping RimImmortal SetEnergy patch — method not found (mod absent or API changed)");
            }

            return method;
        }

        /// <summary>
        /// Prefix: Replace positive energy gains with the level's MaxEnergy delta for Eternal pawns,
        /// so a single gain tick fills the energy bar. Negative deltas (consumption, including the
        /// meridian-damage punishment branch) are left untouched.
        /// </summary>
        /// <param name="__instance">The Pawn_EnergyTracker instance</param>
        /// <param name="num">The energy delta (passed by ref to allow modification)</param>
        [HarmonyPrefix]
        public static void Prefix(object __instance, ref float num)
        {
            // Only boost positive energy gains (cultivation), not consumption
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

                // Jump straight to the current level's MaxEnergy (SetEnergy caps the sum there)
                float maxEnergy = RimImmortalDetection.GetMaxEnergy(__instance);
                num = maxEnergy > 0f ? maxEnergy : FALLBACK_ENERGY_DELTA;

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] RimImmortal: Filled cultivation energy for {pawn.Name} " +
                               $"(gain {originalNum:F2} replaced by delta {num:F2})");
                }
            }
            catch (Exception ex)
            {
                // Graceful degradation - if patch fails, original energy gain still applies
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "RI_SetEnergy_Patch.Prefix", null, ex);
            }
        }
    }
}
