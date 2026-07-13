// Relative Path: Eternal/Source/Eternal/Patches/CultivatorOfTheRim/CTR_BodyCultivationTick_Patch.cs
// Creation Date: 13-07-2026
// Last Edit: 13-07-2026
// Author: 0Shard
// Description: Harmony patch adding passive + meditation body-cultivation progress for Eternal
// pawns in Cultivator of the Rim. Natively body realm severity rises only from melee damage;
// this postfix on HediffComp_BodyCultivation.CompPostTickInterval grants qi-parity severity
// every 2500 ticks (full rate while meditating, 25% passively), multiplied by the pawn's
// CultivationSpeed stat (which includes the Eternal trait's 25x factor).

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
    /// Patches CultivatorOfTheRim.HediffComp_BodyCultivation.CompPostTickInterval(int delta)
    /// to grant passive and meditation body-cultivation severity gains for Eternal pawns.
    ///
    /// Mechanism: the body realm requires melee damage to accumulate severity natively. This
    /// postfix adds qi-parity severity every 2500 ticks (matching the qi-meditation cadence),
    /// scaled by the pawn's CultivationSpeed stat and meditation status. The Eternal trait's
    /// 25x CultivationSpeed factor ensures substantial cultivation progression.
    /// </summary>
    [HarmonyPatch]
    public static class CTR_BodyCultivationTick_Patch
    {
        /// <summary>
        /// Tick interval matching Cultivator of the Rim qi-realm meditation cadence.
        /// </summary>
        private const int BODY_TRIGGER_INTERVAL = 2500;

        /// <summary>
        /// Minimum body severity gain per trigger (meditating).
        /// Matches qi-realm Cultivator_RealmBase.xml severityPerTriggerRange lower bound.
        /// </summary>
        private const float MEDITATION_SEVERITY_MIN = 0.001f;

        /// <summary>
        /// Maximum body severity gain per trigger (meditating).
        /// Matches qi-realm Cultivator_RealmBase.xml severityPerTriggerRange upper bound.
        /// </summary>
        private const float MEDITATION_SEVERITY_MAX = 0.005f;

        /// <summary>
        /// Passive severity gain as a fraction of the meditation gain.
        /// </summary>
        private const float PASSIVE_FRACTION = 0.25f;

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
                    Log.Message("[Eternal] Skipping Cultivator of the Rim BodyCultivationTick patch — mod not active");
                }
                return false;
            }

            if (Eternal_Mod.settings?.debugMode == true)
            {
                Log.Message("[Eternal] Cultivator of the Rim detected - enabling body-cultivation passive gain");
            }

            return true;
        }

        /// <summary>
        /// Resolve the target method via reflection.
        /// Returns null to skip patching if the method cannot be found (API change).
        /// </summary>
        public static MethodBase TargetMethod()
        {
            var method = CultivatorOfTheRimDetection.CompPostTickIntervalMethod;

            if (method == null)
            {
                Log.Warning("[Eternal] Skipping Cultivator of the Rim BodyCultivationTick patch — method not found (mod absent or API changed)");
            }

            return method;
        }

        /// <summary>
        /// Postfix: for Eternal pawns, grant periodic passive body-cultivation severity
        /// scaled by meditation status and the pawn's CultivationSpeed stat.
        /// </summary>
        /// <param name="__instance">The HediffComp_BodyCultivation instance</param>
        /// <param name="delta">Tick delta (passed to the original, not renamed)</param>
        [HarmonyPostfix]
        public static void Postfix(HediffComp __instance, int delta)
        {
            try
            {
                Pawn pawn = __instance?.Pawn;
                if (pawn == null || !pawn.IsValidEternal())
                    return;

                if (!pawn.IsHashIntervalTick(BODY_TRIGGER_INTERVAL, delta))
                    return;

                float cultivationSpeed = CultivatorOfTheRimDetection.GetBodyCultivationSpeed(__instance);
                if (cultivationSpeed <= 0f)
                    return; // reflection failure or zeroed stat — no gain

                bool meditating = pawn.psychicEntropy?.IsCurrentlyMeditating == true;
                float gain = Rand.Range(MEDITATION_SEVERITY_MIN, MEDITATION_SEVERITY_MAX)
                           * cultivationSpeed
                           * (meditating ? 1f : PASSIVE_FRACTION);

                __instance.parent.Severity += gain;

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] CTR body cultivation: +{gain:F5} severity for {pawn.Name?.ToStringShort ?? "Pawn"} (meditating={meditating})");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "CTR_BodyCultivationTick_Patch.Postfix", null, ex);
            }
        }
    }
}
