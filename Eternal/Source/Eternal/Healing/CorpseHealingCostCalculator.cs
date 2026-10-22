// Relative Path: Eternal/Source/Eternal/Healing/CorpseHealingCostCalculator.cs
// Creation Date: 12-10-2026
// Author: 0Shard
// Description: Bounded corpse injury work, terminal removal and future nutrition accounting.
using System;
using Eternal.UI;

namespace Eternal.Healing
{
    public readonly struct CorpseHealingStep
    {
        public bool IsValid { get; }
        public bool IsComplete { get; }
        public float RemainingSeverity { get; }
        public float ConsumedSeverity { get; }
        public float NutritionCharge { get; }
        internal CorpseHealingStep(bool valid, bool complete, float remaining, float consumed, float charge)
        {
            IsValid = valid;
            IsComplete = complete;
            RemainingSeverity = remaining;
            ConsumedSeverity = consumed;
            NutritionCharge = charge;
        }
    }

    public static class CorpseHealingCostCalculator
    {
        public const float CompletionCutoff = 0.01f;

        public static float RemainingWork(float severity)
            => MenuValueCalculator.IsFinite(severity) && severity >= 0f ? severity : float.NaN;

        public static CorpseHealingStep CalculateStep(float severity, float perPass, float ratio, float multiplier)
            => !MenuValueCalculator.IsFinite(ratio) || ratio < 0f || !MenuValueCalculator.IsFinite(multiplier) || multiplier < 0f
                ? new CorpseHealingStep(false, false, severity, 0f, 0f)
                : CalculateProfileStep(severity, perPass, new HealingCostProfile(ratio * multiplier));

        public static CorpseHealingStep CalculateProfileStep(float severity, float perPass, HealingCostProfile profile)
        {
            float work = RemainingWork(severity);
            if (profile == null || !MenuValueCalculator.IsFinite(work) || !MenuValueCalculator.IsFinite(perPass) || perPass <= 0f)
            {
                return new CorpseHealingStep(false, false, severity, 0f, 0f);
            }

            float remaining = Math.Max(profile.Floor, work - perPass);
            bool complete = remaining <= profile.Floor + CompletionCutoff;
            float consumed = Math.Max(0f, work - (complete ? profile.Floor : remaining));
            float charge = HealingWorkCostCalculator.Cost(profile, work, complete ? profile.Floor : remaining);
            if (!MenuValueCalculator.IsFinite(charge))
            {
                return new CorpseHealingStep(false, false, severity, 0f, 0f);
            }

            return new CorpseHealingStep(true, complete, complete ? profile.Floor : remaining, consumed, charge);
        }

        public static float FutureNutrition(float severity, float ratio, float multiplier, bool waived)
            => waived ? 0f : !MenuValueCalculator.IsFinite(MenuValueCalculator.NutritionCost(severity, ratio, multiplier))
                ? float.NaN : HealingWorkCostCalculator.Cost(new HealingCostProfile(ratio * multiplier), RemainingWork(severity), 0f);

        public static float ProcessingPasses(float severity, float perPass)
        {
            float work = RemainingWork(severity);
            if (!MenuValueCalculator.IsFinite(work) || !MenuValueCalculator.IsFinite(perPass) || perPass < 0f)
            {
                return float.NaN;
            }
            if (work == 0f)
            {
                return 0f;
            }

            if (perPass == 0f)
            {
                return float.PositiveInfinity;
            }

            return (float)Math.Max(1d, Math.Ceiling(((double)work - CompletionCutoff) / perPass - 0.00001d));
        }
    }
}
