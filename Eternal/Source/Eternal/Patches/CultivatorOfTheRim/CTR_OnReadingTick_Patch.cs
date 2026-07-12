// Relative Path: Eternal/Source/Eternal/Patches/CultivatorOfTheRim/CTR_OnReadingTick_Patch.cs
// Creation Date: 12-07-2026
// Last Edit: 12-07-2026
// Author: 0Shard
// Description: Harmony patch for Cultivator of the Rim's technique-manual reading so Eternal
// readers learn techniques 1000x faster. CTR has no exp system — OnReadingTick rolls a learn
// chance every 250 ticks (binary hediff grant), so the boost multiplies that chance by 1000
// (clamped to 1.0 = guaranteed learn on the first roll). Only active when the mod is loaded.
// Uses reflection to avoid compile-time dependency.

using System;
using System.Reflection;
using HarmonyLib;
using Verse;
using Eternal.Compatibility;
using Eternal.Exceptions;
using Eternal.Extensions;
using Eternal.Utils;

namespace Eternal.Patches.CultivatorOfTheRim
{
    /// <summary>
    /// Patches CultivatorOfTheRim.BookOutcomeDoerTechniqueManual.OnReadingTick(Pawn reader, float factor)
    /// to boost the technique learn chance 1000x for Eternal readers.
    ///
    /// Mechanism: the doer caches its learn chance in the public field chanceCached
    /// (learnChance × book-quality curve, computed lazily by the finalChance property).
    /// The prefix saves the original chance to __state and overwrites the cache with
    /// min(1, original × 1000); the postfix restores it so non-Eternal readers of the
    /// same book are unaffected.
    /// </summary>
    [HarmonyPatch]
    public static class CTR_OnReadingTick_Patch
    {
        /// <summary>
        /// Learn-chance multiplier for Eternal pawns. 1000x clamps to a guaranteed
        /// learn on the first 250-tick reading roll for any realistic base chance.
        /// </summary>
        private const float ETERNAL_LEARN_CHANCE_MULTIPLIER = 1000f;

        /// <summary>
        /// Sentinel for "prefix did not modify the chance" — postfix skips restoration.
        /// </summary>
        private const float NO_BOOST = -1f;

        /// <summary>
        /// Guard: only apply this patch when Cultivator of the Rim is active.
        /// Returning false skips the patch entirely (Harmony will not call TargetMethod).
        /// </summary>
        public static bool Prepare()
        {
            bool isActive = CultivatorOfTheRimDetection.CultivatorOfTheRimActive;

            if (!isActive)
            {
                // Only log at debug level — absence of the mod is the normal case
                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message("[Eternal] Skipping Cultivator of the Rim OnReadingTick patch — mod not active");
                }
                return false;
            }

            if (Eternal_Mod.settings?.debugMode == true)
            {
                Log.Message("[Eternal] Cultivator of the Rim detected - enabling technique manual learn boost (1000x)");
            }

            return true;
        }

        /// <summary>
        /// Resolve the target method via reflection.
        /// Returns null to skip patching if the method cannot be found (API change).
        /// </summary>
        public static MethodBase TargetMethod()
        {
            var method = CultivatorOfTheRimDetection.OnReadingTickMethod;

            if (method == null)
            {
                Log.Warning("[Eternal] Skipping Cultivator of the Rim OnReadingTick patch — method not found (mod absent or API changed)");
            }

            return method;
        }

        /// <summary>
        /// Prefix: for Eternal readers, boost the cached learn chance 1000x (clamped to 1.0)
        /// before the original rolls against it. The original chance goes to __state.
        /// Mirrors the original's 250-tick gate so the boost only applies on roll ticks.
        /// </summary>
        /// <param name="__instance">The BookOutcomeDoerTechniqueManual instance</param>
        /// <param name="reader">The pawn reading the manual</param>
        /// <param name="__state">Original cached chance, for postfix restoration</param>
        [HarmonyPrefix]
        public static void Prefix(object __instance, Pawn reader, out float __state)
        {
            __state = NO_BOOST;

            try
            {
                if (reader == null || !reader.IsValidEternal())
                    return;

                // The original only rolls on this interval — no point boosting other ticks
                if (!reader.IsHashIntervalTick(250))
                    return;

                float originalChance = CultivatorOfTheRimDetection.GetFinalChance(__instance);
                if (originalChance <= 0f)
                    return;

                __state = originalChance;
                float boostedChance = Math.Min(1f, originalChance * ETERNAL_LEARN_CHANCE_MULTIPLIER);
                CultivatorOfTheRimDetection.SetChanceCached(__instance, boostedChance);

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] CTR: Boosted technique learn chance for {reader.Name?.ToStringShort ?? "Pawn"} " +
                               $"from {originalChance:P2} to {boostedChance:P2}");
                }
            }
            catch (Exception ex)
            {
                // Graceful degradation - if patch fails, original learn chance still applies
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "CTR_OnReadingTick_Patch.Prefix", null, ex);
            }
        }

        /// <summary>
        /// Postfix: restore the original cached chance so non-Eternal readers of the same
        /// book instance keep the unboosted learn chance.
        /// </summary>
        /// <param name="__instance">The BookOutcomeDoerTechniqueManual instance</param>
        /// <param name="__state">Original cached chance saved by the prefix</param>
        [HarmonyPostfix]
        public static void Postfix(object __instance, float __state)
        {
            try
            {
                if (__state < 0f)
                    return; // NO_BOOST — prefix left the chance untouched

                CultivatorOfTheRimDetection.SetChanceCached(__instance, __state);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "CTR_OnReadingTick_Patch.Postfix", null, ex);
            }
        }
    }
}
