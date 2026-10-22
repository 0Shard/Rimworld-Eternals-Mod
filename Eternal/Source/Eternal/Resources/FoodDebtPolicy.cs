// Author: 0Shard
// Description: Existing debt arithmetic; services retain guards, ownership and persistence.
using System;
using System.Collections.Generic;

namespace Eternal.Resources
{
    internal static class FoodDebtPolicy
    {
        internal const float MinDrainNutritionPerDay = 1f;
        private const float TicksPerDay = 60000f;

        internal static float DrainRate(float debt, float peak, float days)
            => Math.Max(Math.Max(peak, debt) / (TicksPerDay * Math.Max(days, 0.01f)), MinDrainNutritionPerDay / TicksPerDay);

        internal static float Drain(float debt, float rate, int cadence, float food, float foodMax, float floor)
        {
            float threshold = foodMax * floor;
            if (debt <= 0f || food <= threshold)
            {
                return 0f;
            }

            return Math.Min(Math.Min(rate * cadence, food - threshold), debt);
        }

        internal static float Capacity(float foodMaxOrBodySize, float multiplier, float baseline)
            => foodMaxOrBodySize * multiplier + baseline;

        internal static float Add(float current, float amount, float capacity)
        {
            float total = current + amount;
            return total > capacity ? capacity : total;
        }

        internal static float AddResurrection(float current, float amount) => current + amount;
        internal static float Repayment(float debt, float amount) => Math.Min(debt, amount);
        internal static float BaselineAfterRepayment(float baseline, float debt) => baseline > debt ? debt : baseline;
        internal static float PeakAfterRepayment(float peak, float debt) => debt <= 0f ? 0f : peak;
        internal static float PeakAfterAddition(float peak, float debt) => debt > peak ? debt : peak;
        internal static float RemainingCapacity(float capacity, float debt) => Math.Max(0f, capacity - debt);
        internal static bool IsExcessive(float debt, float capacity) => debt >= capacity;

        internal static void EnsureDebtRecord<TOwner>(TOwner owner,
            IDictionary<TOwner, NutritionAmount> debts, IDictionary<TOwner, NutritionAmount> baselines,
            IDictionary<TOwner, NutritionAmount> peaks, float legacyCorpseDebt, bool waived)
        {
            if (waived)
            {
                debts[owner] = default;
                baselines.Remove(owner);
                peaks.Remove(owner);
                return;
            }
            // Presence, including an authoritative zero, decides ownership. A corpse float
            // must never replace or be added to an existing exact episode during registration.
            if (debts.ContainsKey(owner))
            {
                return;
            }
            var recovered = NutritionAmount.FromFloat(legacyCorpseDebt);
            debts[owner] = recovered;
            if (!recovered.IsZero)
            {
                baselines[owner] = recovered;
                peaks[owner] = recovered;
            }
        }

        internal static float AddExact(ref NutritionAmount debt, NutritionAmount capacity, float amount)
        {
            float accepted = NutritionAmount.Min(NutritionAmount.FromFloat(amount),
                NutritionAmount.Difference(capacity, debt)).ToFloat();
            debt += NutritionAmount.FromFloat(accepted);
            return accepted;
        }

        internal static float RepayExact(ref NutritionAmount debt, float amount)
        {
            float accepted = NutritionAmount.Min(debt, NutritionAmount.FromFloat(amount)).ToFloat();
            debt = NutritionAmount.Difference(debt, NutritionAmount.FromFloat(accepted));
            return accepted;
        }

        // This float is strictly below 1e-6 nutrition. A write-off must also be below
        // the smallest food step in the ENTIRE permitted range, not just today's food ULP.
        private const float TerminalDustCap = 0.000001f;

        internal static bool TryCloseTerminalDust(ref NutritionAmount debt, float foodMax, float floor)
        {
            if (debt.IsZero || !NutritionAmount.IsSupported(foodMax) || foodMax <= 0f
                || !NutritionAmount.IsSupported(floor) || floor >= 1f)
            {
                return false;
            }
            float threshold = foodMax * floor;
            int bits = BitConverter.ToInt32(BitConverter.GetBytes(threshold), 0);
            float next = BitConverter.ToSingle(BitConverter.GetBytes(bits + 1), 0);
            if (!NutritionAmount.IsSupported(next) || next > foodMax)
            {
                return false;
            }
            var minimumStep = NutritionAmount.Difference(NutritionAmount.FromFloat(next), NutritionAmount.FromFloat(threshold));
            var bound = NutritionAmount.Min(minimumStep, NutritionAmount.FromFloat(TerminalDustCap));
            if (debt.Equals(bound) || !NutritionAmount.Min(debt, bound).Equals(debt))
            {
                return false;
            }
            debt = default;
            return true;
        }

        internal static float FoodAfterPayment(float before, float requested, float maximum, float floor = 0f)
        {
            float after = Math.Max(floor, before - Math.Min(requested, maximum));
            if (after < before && before - after > maximum)
            {
                int bits = BitConverter.ToInt32(BitConverter.GetBytes(after), 0);
                after = BitConverter.ToSingle(BitConverter.GetBytes(bits + 1), 0);
            }
            return after;
        }
    }
}
