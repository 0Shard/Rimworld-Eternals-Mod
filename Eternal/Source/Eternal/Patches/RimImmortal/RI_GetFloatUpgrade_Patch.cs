// Relative Path: Eternal/Source/Eternal/Patches/RimImmortal/RI_GetFloatUpgrade_Patch.cs
// Creation Date: 28-12-2025
// Last Edit: 04-03-2026
// Author: 0Shard
// Description: Harmony patch for RimImmortal's GetFloatUpgrade method to grant Eternals 5x breakthrough chance.
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
    /// Patches RIRitualFramework.MessageDialog.GetFloatUpgrade to multiply breakthrough chance by 5x for Eternal pawns.
    /// The breakthrough chance is capped at 100% after multiplication.
    /// </summary>
    [HarmonyPatch]
    public static class RI_GetFloatUpgrade_Patch
    {
        /// <summary>
        /// Breakthrough chance multiplier for Eternal pawns.
        /// </summary>
        private const float BREAKTHROUGH_MULTIPLIER = 5f;

        /// <summary>
        /// Maximum breakthrough chance (100%).
        /// </summary>
        private const float MAX_SUCCESS_RATE = 100f;

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
                    Log.Message("[Eternal] Skipping RimImmortal GetFloatUpgrade patch — mod not active");
                }
                return false;
            }

            if (Eternal_Mod.settings?.debugMode == true)
            {
                Log.Message("[Eternal] RimImmortal detected - enabling breakthrough chance patch (5x)");
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
            var method = RimImmortalDetection.GetFloatUpgradeMethod;

            if (method == null)
            {
                Log.Warning("[Eternal] Skipping RimImmortal GetFloatUpgrade patch — method not found (mod absent or API changed)");
            }

            return method;
        }

        /// <summary>
        /// Postfix: Multiply the final breakthrough chance by 5x for Eternal pawns.
        /// The original method calculates the rate and caps it at 100%, so we read
        /// the final value, multiply, and re-cap.
        /// </summary>
        /// <param name="__instance">The MessageDialog instance</param>
        [HarmonyPostfix]
        public static void Postfix(object __instance)
        {
            try
            {
                // Get the pawn from the MessageDialog
                Pawn pawn = RimImmortalDetection.GetPawnFromMessageDialog(__instance);

                if (pawn == null)
                    return;

                // Check if this pawn is an Eternal
                if (!pawn.IsValidEternal())
                    return;

                // Read current success rate (already in percentage form, 0-100)
                float currentRate = RimImmortalDetection.GetSucessRate(__instance);

                // Apply 5x multiplier
                float boostedRate = currentRate * BREAKTHROUGH_MULTIPLIER;

                // Cap at 100%
                if (boostedRate > MAX_SUCCESS_RATE)
                {
                    boostedRate = MAX_SUCCESS_RATE;
                }

                // Write back the boosted rate
                RimImmortalDetection.SetSucessRate(__instance, boostedRate);

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] RimImmortal: Boosted breakthrough chance for {pawn.Name} " +
                               $"from {currentRate:F1}% to {boostedRate:F1}% ({BREAKTHROUGH_MULTIPLIER}x)");
                }
            }
            catch (Exception ex)
            {
                // Graceful degradation - if patch fails, original breakthrough chance still applies
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "RI_GetFloatUpgrade_Patch.Postfix", null, ex);
            }
        }
    }
}
