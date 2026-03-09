// Relative Path: Eternal/Source/Eternal/Patches/Pawn_HealthTracker_AddHediff_Patch.cs
// Creation Date: 29-12-2025
// Last Edit: 21-02-2026
// Author: 0Shard
// Description: Harmony patch for Pawn_HealthTracker.AddHediff to register random healing
//              thresholds for debuff hediffs on living Eternal pawns.
//              When an infection, disease, blood loss, or similar hediff is added,
//              a random threshold (1-99% of maxSeverity) is generated. The healing
//              system will wait until the hediff reaches this severity before healing.

using System;
using HarmonyLib;
using Verse;
using Eternal;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Extensions;
using Eternal.Healing;
using Eternal.Utils;

namespace Eternal.Patches
{
    /// <summary>
    /// Harmony patch for Pawn_HealthTracker.AddHediff.
    /// Registers healing activation thresholds for debuff hediffs on living Eternal pawns.
    /// </summary>
    /// <remarks>
    /// This patch intercepts hediff addition to generate random thresholds for debuffs.
    /// Only affects living Eternal pawns and debuff hediffs with stages that would be healed.
    /// </remarks>
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.AddHediff))]
    [HarmonyPatch(new Type[] { typeof(Hediff), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) })]
    public static class Pawn_HealthTracker_AddHediff_Patch
    {
        /// <summary>
        /// Postfix patch that registers a random healing threshold after a hediff is added.
        /// Only applies to living Eternal pawns with debuff hediffs that would actually be healed.
        /// </summary>
        /// <param name="hediff">The hediff that was added</param>
        [HarmonyPostfix]
        public static void RegisterThresholdForDebuff(Hediff hediff)
        {
            try
            {
                // SAFE-09: skip when mod is disabled.
                if (EternalModState.IsDisabled)
                    return;

                // Validate input - hediff.pawn is set after AddHediff completes
                if (hediff?.pawn == null)
                    return;

                Pawn pawn = hediff.pawn;

                // Only living pawns (dead pawns don't progress hediffs)
                if (pawn.Dead)
                    return;

                // Only Eternal pawns
                if (!pawn.IsValidEternal())
                    return;

                // Only debuffs with stages (infections, diseases, blood loss, parasites, etc.)
                if (!hediff.IsDebuffWithStages())
                    return;

                // Don't register threshold for hediffs that bypass it
                // (bloodloss, injuries, scars, regrowth - these ALWAYS heal instantly)
                if (hediff.ShouldBypassThreshold())
                    return;

                // Only track hediffs that would actually be healed (custom or default eligible)
                // This saves memory by not tracking hediffs that won't be healed anyway
                var hediffManager = Eternal_Mod.GetSettings().hediffManager;
                var setting = hediffManager?.GetHediffSetting(hediff.def.defName);

                // Don't register threshold if user has enabled "Instant Healing" for this hediff
                if (setting?.noThreshold == true)
                    return;
                if (!HediffHealingConfig.IsEligibleForHealing(hediff, setting))
                    return;

                // Get the tracker from DI container
                var tracker = EternalServiceContainer.Instance?.ThresholdTracker;
                if (tracker == null)
                    return;

                // Calculate random threshold (1-99% of maxSeverity)
                float maxSev = hediff.def.maxSeverity;
                if (float.IsInfinity(maxSev) || maxSev <= 0f)
                    maxSev = 1.0f;

                // Generate random threshold between 1% and 99% of max severity
                float threshold = Rand.Range(0.01f, 0.99f) * maxSev;

                // Register the threshold
                tracker.RegisterThreshold(pawn, hediff, threshold);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "Pawn_HealthTracker_AddHediff_Patch", hediff?.pawn, ex);
            }
        }
    }
}
