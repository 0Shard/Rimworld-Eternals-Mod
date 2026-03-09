// Relative Path: Eternal/Source/Eternal/Patches/RimImmortal/RI_SetEnergy_Patch.cs
// Creation Date: 28-12-2025
// Last Edit: 04-03-2026
// Author: 0Shard
// Description: Harmony patch for RimImmortal's SetEnergy method to grant Eternals 5x cultivation speed.
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
    /// Patches Core.Pawn_EnergyTracker.SetEnergy to multiply energy gains by 5x for Eternal pawns.
    /// This boosts cultivation speed for Eternals when using RimImmortal's cultivation system.
    /// </summary>
    [HarmonyPatch]
    public static class RI_SetEnergy_Patch
    {
        /// <summary>
        /// Cultivation speed multiplier for Eternal pawns.
        /// </summary>
        private const float CULTIVATION_MULTIPLIER = 5f;

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
                Log.Message("[Eternal] RimImmortal detected - enabling cultivation speed patch (5x)");
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
        /// Prefix: Multiply positive energy gains by 5x for Eternal pawns.
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

                // Apply 5x cultivation multiplier
                num *= CULTIVATION_MULTIPLIER;

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] RimImmortal: Boosted cultivation for {pawn.Name} " +
                               $"from {originalNum:F2} to {num:F2} ({CULTIVATION_MULTIPLIER}x)");
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
