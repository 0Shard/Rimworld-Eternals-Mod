using System;
using System.Collections.Generic;
using System.Linq;

namespace Eternal.Healing
{
    public sealed class HealingCostProfile
    {
        public float Floor { get; }
        public float NutritionPerSeverity { get; }
        public bool IsValid => HealingWorkCostCalculator.Finite(NutritionPerSeverity) && NutritionPerSeverity >= 0f;
        public string Category { get; }
        public string RangeBasis { get; }
        public float Range { get; }
        public IReadOnlyList<float> Stages { get; }
        public HealingCostProfile(float coefficient, float floor = 0f)
            : this(coefficient, floor, "Injury HP / regrowth work", 1f, "per work unit", null) { }

        internal HealingCostProfile(float coefficient, float floor, string category, float range,
            string rangeBasis, IEnumerable<float> stages)
        {
            Floor = HealingWorkCostCalculator.Nonnegative(floor);
            NutritionPerSeverity = coefficient;
            Category = category;
            Range = range;
            RangeBasis = rangeBasis;
            Stages = Array.AsReadOnly((stages ?? Enumerable.Empty<float>())
                .Where(s => HealingWorkCostCalculator.Finite(s) && s >= 0f).Distinct().OrderBy(s => s).ToArray());
        }

        public float StagePremium(float severity)
        {
            int index = -1;
            foreach (float threshold in Stages)
            {
                if (threshold > severity)
                {
                    break;
                }
                index++;
            }
            return Math.Min(SettingsDefaults.CostStageMaximum, 1f + Math.Max(0, index) * SettingsDefaults.CostStageIncrement);
        }
    }

    public static class HealingWorkCostCalculator
    {
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static float Nonnegative(float value) => Finite(value) ? Math.Max(0f, value) : 0f;

        public static bool IsAtHealingFloor(HealingCostProfile profile, float severity)
            => profile != null && profile.IsValid && Finite(severity) && severity <= profile.Floor;

        public static HealingCostProfile CreateProfile(bool injury, bool chronic, float floor,
            float maxSeverity, float lethalSeverity, float rangeOverride, float multiplier,
            float globalMultiplier, IEnumerable<float> stages = null, float injuryCoefficient = SettingsDefaults.SeverityToNutritionRatio)
        {
            floor = Nonnegative(floor);
            float range = SettingsDefaults.NormalizeNutritionRange(rangeOverride);
            string basis = "override severity range";
            if (range == 0f)
            {
                float terminal = Finite(lethalSeverity) && lethalSeverity > floor ? lethalSeverity : maxSeverity;
                range = Finite(terminal) && terminal < float.MaxValue && terminal > floor ? terminal - floor : 0f;
                basis = range > 0f ? "definition severity range" : "per-severity fallback (range unknown)";
            }
            float coefficient = injury ? Nonnegative(injuryCoefficient) :
                (chronic ? SettingsDefaults.ChronicFullRangeNutrition : SettingsDefaults.IllnessFullRangeNutrition) / (range > 0f ? range : 1f);
            float global = Finite(globalMultiplier) && globalMultiplier >= 0f ? globalMultiplier : SettingsDefaults.NutritionCostMultiplier;
            coefficient *= SettingsDefaults.NormalizeNutritionMultiplier(multiplier) * global;
            return new HealingCostProfile(coefficient, floor, injury ? "Injury HP / regrowth work" : chronic ? "Chronic" : "Illness / condition",
                range, injury ? "per work unit" : basis, injury ? null : stages);
        }

        public static float Cost(HealingCostProfile profile, float before, float after)
        {
            if (profile == null || !profile.IsValid || !Finite(before) || !Finite(after))
            {
                return float.NaN;
            }
            double low = Math.Max(profile.Floor, after);
            double high = Math.Max(low, before);
            double cost = 0d;
            foreach (float boundary in profile.Stages)
            {
                if (boundary <= low || boundary >= high)
                {
                    continue;
                }
                cost += (boundary - low) * profile.NutritionPerSeverity * profile.StagePremium((float)low);
                low = boundary;
            }
            cost += (high - low) * profile.NutritionPerSeverity * profile.StagePremium((float)low);
            return cost <= float.MaxValue ? (float)cost : float.NaN;
        }

        public static float AffordableSeverity(HealingCostProfile profile, float before, float requested, float budget)
        {
            if (profile == null || !profile.IsValid || !Finite(before) || !Finite(requested) || requested <= 0f || float.IsNaN(budget))
            {
                return 0f;
            }
            float work = Math.Min(requested, Math.Max(0f, before - profile.Floor));
            float fullCost = Cost(profile, before, before - work);
            if (fullCost <= budget || profile.NutritionPerSeverity == 0f)
            {
                return work;
            }
            if (budget <= 0f || !Finite(fullCost))
            {
                return 0f;
            }
            double low = 0d;
            double high = work;
            // Invert the very same monotone integral. Returning the lower bound keeps
            // rounding from authorizing more work than the available nutrition buys.
            for (int i = 0; i < 48; i++)
            {
                double middle = (low + high) / 2d;
                if (Cost(profile, before, before - (float)middle) <= budget)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }
            return (float)low;
        }
    }

    public readonly struct RegrowthBatchResult
    {
        public float ConsumedEffort { get; }
        public float NutritionCharge { get; }
        public RegrowthBatchResult(float effort, float charge)
        {
            ConsumedEffort = effort;
            NutritionCharge = charge;
        }
    }

    public static class RegrowthWorkAccounting
    {
        public static float RemainingEffort(float progress, float partHp)
            => HealingWorkCostCalculator.Nonnegative(1f - HealingWorkCostCalculator.Nonnegative(progress))
                * HealingWorkCostCalculator.Nonnegative(partHp) * SettingsDefaults.RegrowthWorkPerPartHP;

        public static float ConsumedEffort(float before, float after, float partHp)
            => Math.Max(0f, RemainingEffort(before, partHp) - RemainingEffort(after, partHp));
    }
}
