// Relative Path: Eternal/Source/Eternal/UI/MenuValueCalculator.cs
// Creation Date: 12-10-2026
// Author: 0Shard
// Description: Game-independent menu calculations with explicit work, cadence and units.

using System;
using System.Globalization;

namespace Eternal.UI
{
    public readonly struct LivingScarDisplay
    {
        public string Status { get; }
        public float RemainingTicks { get; }
        public LivingScarDisplay(string status, float remainingTicks = float.NaN)
        {
            Status = status;
            RemainingTicks = remainingTicks;
        }
    }

    public static class MenuValueCalculator
    {
        public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public static float ProcessingHpPerDay(float effectivePerPass, int interval)
            => IsFinite(effectivePerPass) && effectivePerPass >= 0f && interval > 0
                ? effectivePerPass * 60000f / interval : float.NaN;

        public static float InjuryHpBudget(float nutrition, float coefficient)
            => IsFinite(nutrition) && nutrition >= 0f && IsFinite(coefficient) && coefficient > 0f
                ? nutrition / coefficient : float.NaN;

        public static float RecoveryEquivalentHpPerDay(float debt, float peak, float days, float coefficient)
            => IsFinite(debt) && debt >= 0f && IsFinite(peak) && peak >= 0f && IsFinite(days) && days > 0f
                ? InjuryHpBudget(debt == 0f ? 0f : 60000f * Resources.FoodDebtPolicy.DrainRate(debt, peak, days), coefficient) : float.NaN;

        public static string Pricing(Healing.HealingCostProfile profile, float severity)
            => profile == null ? "Pricing unavailable" :
                $"{profile.Category}; {profile.RangeBasis}" + (profile.Range > 0f ? $" {Number(profile.Range)}" : "") +
                $"; current stage price ×{Number(profile.StagePremium(severity))}; {Number(profile.NutritionPerSeverity)} nutrition/unit before stage premium";

        public static float ProcessingTicks(float work, float perPass, int interval)
        {
            if (!IsFinite(work) || work < 0f || !IsFinite(perPass) || perPass < 0f || interval <= 0)
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
            // Decimal configuration values arrive as floats; a near-integral quotient
            // must not gain a whole pass merely from representation noise.
            double ticks = Math.Max(1d, Math.Ceiling((double)work / perPass - 0.00001d)) * interval;
            return ticks > float.MaxValue ? float.NaN : (float)ticks;
        }

        public static float StageFactor(int stage)
        {
            return Healing.HediffHealingPolicy.StageMultiplier(stage);
        }

        public static float ConditionTicks(float severity, float rate, float bodySize, int interval, int? stage = null)
        {
            if (!IsFinite(bodySize) || bodySize <= 0f)
            {
                return float.NaN;
            }

            float perPass = rate * bodySize;
            if (stage.HasValue)
            {
                perPass *= StageFactor(stage.Value) * Healing.UnifiedHediffHealingCalculator.DEBUFF_RATE_FACTOR;
            }

            return ProcessingTicks(severity, perPass, interval);
        }

        public static float NutritionCost(float work, float ratio, float multiplier)
        {
            if (!IsFinite(work) || work < 0f || !IsFinite(ratio) || ratio < 0f || !IsFinite(multiplier) || multiplier < 0f)
            {
                return float.NaN;
            }

            float cost = work * ratio * multiplier;
            return IsFinite(cost) ? cost : float.NaN;
        }

        public static string EffectiveConversion(float ratio, float multiplier)
        {
            float cost = NutritionCost(1f, ratio, multiplier);
            if (!IsFinite(cost))
            {
                return "Unavailable";
            }

            if (cost == 0f)
            {
                return "No nutrition cost";
            }

            return $"{Number(1f / cost)} severity : 1 nutrition";
        }

        public static string Number(float value)
        {
            if (!IsFinite(value))
            {
                return "Unavailable";
            }

            if (value != 0f && Math.Abs(value) < 0.0001f)
            {
                return value.ToString("0.###E+0", CultureInfo.InvariantCulture);
            }

            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        public static string Nutrition(float? value)
        {
            if (!value.HasValue || !IsFinite(value.Value) || value < 0f)
            {
                return "Unavailable";
            }

            return value == 0f ? "None" : Number(value.Value) + " nutrition";
        }

        public static string CompactNumber(float? value)
        {
            if (!value.HasValue || !IsFinite(value.Value) || value.Value < 0f)
            {
                return "Unavailable";
            }

            return value.Value > 0f && value.Value < 0.001f ? "<0.001" :
                value.Value.ToString("#,0.###", CultureInfo.InvariantCulture);
        }

        public static string ExampleNutrition(float value, int precision)
        {
            if (!IsFinite(value) || value < 0f)
            {
                return "Unavailable";
            }

            return value > 0f && value < Math.Pow(10d, -precision)
                ? Number(value) + " nutrition"
                : value.ToString("F" + precision, CultureInfo.InvariantCulture) + " nutrition";
        }

        public static string Time(float ticks)
        {
            if (float.IsPositiveInfinity(ticks))
            {
                return "Paused";
            }

            if (!IsFinite(ticks) || ticks < 0f)
            {
                return "Unavailable";
            }

            if (ticks == 0f)
            {
                return "No work";
            }

            if (ticks < 2500f)
            {
                return $"~{Math.Max(1d, Math.Ceiling(ticks * 60d / 2500d)):0} minutes";
            }

            if (ticks < 60000f)
            {
                return $"~{ticks / 2500f:F1} hours";
            }

            return $"~{ticks / 60000f:F1} days";
        }

        // A repaint may clamp the slider's visual thumb, but must not overwrite a legacy value.
        public static float EditedRate(float existing, float candidate, bool intentional)
        {
            if (!intentional || !IsFinite(candidate))
            {
                return existing;
            }

            return Math.Max(SettingsDefaults.BaseHealingRateMin, Math.Min(SettingsDefaults.BaseHealingRateMax, candidate));
        }

        public static bool ResourcesPermitHealing(bool waived, bool canDrainFood, bool hasDebtCapacity)
            => waived || canDrainFood || hasDebtCapacity;

        public static bool IncludeLivingCondition(bool isBad, bool explicitlyHealed, bool scar, bool modOwned, bool missingPart)
            => !modOwned && !missingPart && (isBad || explicitlyHealed || scar);

        public static LivingScarDisplay DescribeLivingScar(bool permanentInjury, bool? normalEnabled,
            bool? normalEligible, bool resourcesAvailable, bool waived, float remainingTicks = float.NaN)
        {
            const string scarPath = "Separate scar healing is uncharged; ";
            string timing = IsFinite(remainingTicks) ? "time follows the scheduled scar-healing passes" : "elapsed timing unavailable";
            if (permanentInjury)
            {
                return new LivingScarDisplay("Permanent injury: " + scarPath + timing, remainingTicks);
            }

            if (normalEnabled == false)
            {
                return new LivingScarDisplay(scarPath + "normal healing is disabled; " + timing, remainingTicks);
            }

            if (!normalEnabled.HasValue)
            {
                return new LivingScarDisplay(scarPath + "normal healing eligibility is unavailable; " + timing, remainingTicks);
            }

            string charge = waived ? "normal healing nutrition is waived" : "normal healing may consume nutrition";
            string state = !normalEligible.HasValue ? "; normal healing eligibility is unavailable" :
                normalEligible == false ? " when eligible" : !resourcesAvailable ? "; normal healing is waiting for food or debt capacity" : "";
            return new LivingScarDisplay(scarPath + charge + state + (IsFinite(remainingTicks)
                ? "; displayed time is the separate scar path; normal healing may finish sooner"
                : "; combined remaining time unavailable"), remainingTicks);
        }

        public static bool? TrackedThresholdEligibility(bool recorded, bool reached, float severity, float threshold)
        {
            if (!recorded || !IsFinite(severity) || !IsFinite(threshold))
            {
                return null;
            }

            return reached || severity >= threshold;
        }

        public static string PartStatus(bool progressComplete, bool? partMissing, bool pendingRegrowth,
            bool? parentAllowsCompletion, bool canUpdate)
        {
            if (!partMissing.HasValue || !parentAllowsCompletion.HasValue)
            {
                return "Restoration status unavailable";
            }

            if (!partMissing.Value && !pendingRegrowth)
            {
                return "Restored";
            }

            if (progressComplete)
            {
                return parentAllowsCompletion.Value ? "Progress complete; awaiting restoration" :
                    "Progress complete; waiting for parent restoration";
            }

            if (!canUpdate)
            {
                return parentAllowsCompletion.Value ? "Waiting for critical part" : "Waiting for parent or critical part";
            }

            return parentAllowsCompletion.Value ? "Regrowing" : "Regrowing; parent restoration pending";
        }
    }
}
