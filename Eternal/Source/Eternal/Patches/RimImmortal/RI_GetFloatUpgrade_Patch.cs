// Relative Path: Eternal/Source/Eternal/Patches/RimImmortal/RI_GetFloatUpgrade_Patch.cs
// Creation Date: 28-12-2025
// Last Edit: 11-07-2026
// Author: 0Shard
// Description: Harmony patch for RimImmortal's GetFloatUpgrade method to grant Eternals a flat 99.99% breakthrough chance.
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
    /// Patches RIRitualFramework.MessageDialog.GetFloatUpgrade to set a flat 99.99% breakthrough
    /// chance for Eternal pawns, regardless of the computed base rate.
    /// The success roll is Rand.Range(0, 99) against successRate, so 99.99 always succeeds.
    /// </summary>
    [HarmonyPatch]
    public static class RI_GetFloatUpgrade_Patch
    {
        /// <summary>
        /// Breakthrough chance for Eternal pawns (percentage form, 0-100).
        /// </summary>
        private const float ETERNAL_SUCCESS_RATE = 99.99f;

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
                Log.Message("[Eternal] RimImmortal detected - enabling breakthrough chance patch (99.99%)");
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
        /// Postfix: Set the final breakthrough chance to a flat 99.99% for Eternal pawns.
        /// The original method computes and caps the rate; we overwrite it after the fact.
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

                // Read current success rate for debug logging (percentage form, 0-100)
                float currentRate = RimImmortalDetection.GetSucessRate(__instance);

                // Overwrite with the flat Eternal rate
                RimImmortalDetection.SetSucessRate(__instance, ETERNAL_SUCCESS_RATE);

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] RimImmortal: Set breakthrough chance for {pawn.Name} " +
                               $"from {currentRate:F1}% to {ETERNAL_SUCCESS_RATE}%");
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
