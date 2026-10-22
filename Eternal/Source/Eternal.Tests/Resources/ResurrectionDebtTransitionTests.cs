using System.Collections.Generic;
using System.Linq;
using Eternal.Resources;
using Eternal.Tests.UI;
using Xunit;

namespace Eternal.Tests.Resources
{
    public class ResurrectionDebtTransitionTests
    {
        [Theory]
        [InlineData(100000000f, false, false)]
        [InlineData(100000000f, true, false)]
        [InlineData(100000000f, false, true)]
        [InlineData(100000000f, true, true)]
        [InlineData(float.MaxValue, false, false)]
        [InlineData(float.MaxValue, true, false)]
        [InlineData(float.MaxValue, false, true)]
        [InlineData(float.MaxValue, true, true)]
        public void RegistrationPreservesExactEpisodeAcrossCompletionAndResume(float firstCharge, bool partiallyRepaid, bool saved)
        {
            const string pawn = "same-pawn";
            var debt = NutritionAmount.FromFloat(firstCharge) +
                NutritionAmount.FromFloat(firstCharge == float.MaxValue ? firstCharge : 0.25f);
            var peak = debt;
            if (partiallyRepaid)
            {
                Assert.Equal(0.85f, FoodDebtPolicy.RepayExact(ref debt, 0.85f));
            }
            var debts = new Dictionary<string, NutritionAmount> { [pawn] = debt };
            var baselines = new Dictionary<string, NutritionAmount> { [pawn] = debt };
            var peaks = new Dictionary<string, NutritionAmount> { [pawn] = peak };
            float legacyDisplay = debt.ToFloat();
            if (saved)
            {
                debts = Restore(debts);
                baselines = Restore(baselines);
                peaks = Restore(peaks);
            }

            // This is the production registration kernel called before corpse removal by
            // both resurrection modes. Repeated registration must not re-import the view.
            FoodDebtPolicy.EnsureDebtRecord(pawn, debts, baselines, peaks, legacyDisplay, false);
            FoodDebtPolicy.EnsureDebtRecord(pawn, debts, baselines, peaks, float.MaxValue, false);
            Assert.Equal(debt, debts[pawn]);
            Assert.Equal(debt, baselines[pawn]);
            Assert.Equal(peak, peaks[pawn]);
            Assert.Equal(5f, NutritionAmount.Difference(baselines[pawn] + NutritionAmount.FromFloat(5f), debts[pawn]).ToFloat());

            float foodAfter = FoodDebtPolicy.FoodAfterPayment(1f, 0.85f, debts[pawn].ToFloat(), 0.15f);
            float observedFood = 1f - foodAfter;
            var remaining = debts[pawn];
            Assert.Equal(observedFood, FoodDebtPolicy.RepayExact(ref remaining, observedFood));
            Assert.Equal(NutritionAmount.FromFloat(observedFood), NutritionAmount.Difference(debts[pawn], remaining));
        }

        [Fact]
        public void LegacyCorpseOnlyDebtIsImportedOnceButAnAuthoritativeZeroIsNotReplaced()
        {
            var debts = new Dictionary<string, NutritionAmount>();
            var baselines = new Dictionary<string, NutritionAmount>();
            var peaks = new Dictionary<string, NutritionAmount>();
            FoodDebtPolicy.EnsureDebtRecord("legacy", debts, baselines, peaks, 6.25f, false);
            FoodDebtPolicy.EnsureDebtRecord("legacy", debts, baselines, peaks, 100f, false);
            Assert.Equal(NutritionAmount.FromFloat(6.25f), debts["legacy"]);
            Assert.Equal(debts["legacy"], baselines["legacy"]);
            Assert.Equal(debts["legacy"], peaks["legacy"]);
            debts["zero"] = default;
            FoodDebtPolicy.EnsureDebtRecord("zero", debts, baselines, peaks, 100f, false);
            Assert.True(debts["zero"].IsZero);
            Assert.False(baselines.ContainsKey("zero"));
            Assert.False(peaks.ContainsKey("zero"));
        }

        [Fact]
        public void FoodNeedWaiverClearsTheWholeEpisodeWithoutReimportingLegacyDebt()
        {
            var amount = NutritionAmount.FromFloat(float.MaxValue) + NutritionAmount.FromFloat(float.MaxValue);
            var debts = new Dictionary<string, NutritionAmount> { ["pawn"] = amount };
            var baselines = new Dictionary<string, NutritionAmount> { ["pawn"] = amount };
            var peaks = new Dictionary<string, NutritionAmount> { ["pawn"] = amount };
            FoodDebtPolicy.EnsureDebtRecord("pawn", debts, baselines, peaks, float.MaxValue, true);
            FoodDebtPolicy.EnsureDebtRecord("pawn", debts, baselines, peaks, float.MaxValue, false);
            Assert.True(debts["pawn"].IsZero);
            Assert.Empty(baselines);
            Assert.Empty(peaks);
        }

        [Fact]
        public void CompletionKeepsAuthoritativeOwnerBeforeCorpseCleanupWithoutFloatTransfer()
        {
            const string processor = "Eternal.Healing.EternalCorpseHealingProcessor";
            var calls = MenuAssemblyContract.Calls(processor, "Resurrect").ToList();
            int register = calls.IndexOf("Eternal.Interfaces.IFoodDebtWriter.RegisterPawn");
            int cleanup = calls.IndexOf(processor + ".ReleaseResurrectionTracking");
            Assert.True(register >= 0 && register < cleanup,
                "Keep or initialize the same pawn debt owner before removing its legacy corpse fallback.");
            Assert.DoesNotContain("Eternal.Interfaces.IFoodDebtWriter.UnregisterPawn", calls);
            Assert.DoesNotContain("Eternal.Interfaces.IFoodDebtWriter.AddResurrectionDebt", calls);
            Assert.True(register < calls.IndexOf("RimWorld.ResurrectionUtility.TryResurrect"),
                "A legacy-only episode must already have an authoritative owner before native resurrection or later post-work can fail.");
            var release = MenuAssemblyContract.Calls(processor, "ReleaseResurrectionTracking");
            Assert.Contains("Eternal.Corpse.EternalCorpseManager.UnregisterCorpse", release);
            Assert.DoesNotContain("Eternal.Interfaces.IFoodDebtWriter.UnregisterPawn", release);
            Assert.DoesNotContain("Eternal.Interfaces.IFoodDebtWriter.AddResurrectionDebt", release);
        }

        [Fact]
        public void ResourceOwnerAndResumedCorpsePathsUseTheSameRecordPolicyBeforeSync()
        {
            const string manager = "Eternal.Resources.UnifiedFoodDebtManager";
            var registration = MenuAssemblyContract.Calls(manager, "RegisterPawn").ToList();
            Assert.True(registration.IndexOf(manager + ".EnsureDebtRecord") < registration.IndexOf(manager + ".SyncCorpseData"));
            var ensure = MenuAssemblyContract.Calls(manager, "EnsureDebtRecord");
            Assert.Contains("Eternal.Resources.FoodDebtPolicy.EnsureDebtRecord", ensure);
            Assert.Contains("Eternal.Corpse.EternalCorpseManager.GetCorpseData", ensure);
            foreach (string field in new[] { "_debtTracker", "_resurrectionDebtBaseline", "_peakDebt" })
            {
                Assert.Contains(MenuAssemblyContract.Instructions(manager, "EnsureDebtRecord"), i => Equals(i.Operand, field));
            }
            var resumed = MenuAssemblyContract.Calls("Eternal.Healing.EternalCorpseHealingProcessor", "RebuildActiveHealingCorpses").ToList();
            Assert.True(resumed.IndexOf("Eternal.Interfaces.IFoodDebtWriter.RegisterPawn") >= 0);
            Assert.True(resumed.IndexOf("Eternal.Interfaces.IFoodDebtWriter.RegisterPawn") <
                resumed.IndexOf("Eternal.Healing.EternalCorpseHealingProcessor.EnsureHealingQueueHydrated"));
            foreach (string method in new[] { "UnregisterCorpse" })
            {
                Assert.DoesNotContain(MenuAssemblyContract.Calls("Eternal.Corpse.EternalCorpseManager", method),
                    c => c != null && (c.EndsWith(".UnregisterPawn") || c.EndsWith(".ClearDebt")));
            }
            Assert.DoesNotContain(MenuAssemblyContract.Calls("Eternal.Healing.EternalCorpseHealingProcessor", "AttemptHediffRestore"),
                c => c != null && (c.EndsWith(".UnregisterPawn") || c.EndsWith(".AddResurrectionDebt")));
        }

        private static Dictionary<string, NutritionAmount> Restore(Dictionary<string, NutritionAmount> saved)
        {
            var restored = new Dictionary<string, NutritionAmount>();
            foreach (var pair in saved)
            {
                Assert.True(NutritionAmount.TryParse(pair.Value.Serialize(), out var amount));
                restored[pair.Key] = amount;
            }
            return restored;
        }
    }
}
