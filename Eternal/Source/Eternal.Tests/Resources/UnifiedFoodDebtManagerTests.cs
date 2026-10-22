// Relative Path: Eternal/Source/Eternal.Tests/Resources/UnifiedFoodDebtManagerTests.cs
// Creation Date: 24-02-2026
// Last Edit: 12-07-2026
// Author: 0Shard
// Description: Unit tests for food debt system interfaces and debt math verification.
//              UnifiedFoodDebtManager cannot be instantiated in unit tests because its
//              Dictionary<Pawn, float> field triggers Assembly-CSharp loading at construction.
//              Tests verify the IFoodDebtSystem interface contract, debt formula constants,
//              and settings defaults. Full lifecycle tests require the in-game harness.

using System;
using System.Linq;
using Eternal.Tests.Infrastructure;
using Xunit;
using Eternal.Resources;
using Eternal.Healing;

namespace Eternal.Tests.Resources
{
    public class FoodDebtFormulaTests
    {
        [Fact]
        public void IFoodDebtSystem_InheritsFromReaderAndWriter()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var interfaces = assembly.Reader.GetTypeDefinition(assembly.Type("Eternal.Interfaces.IFoodDebtSystem"))
                .GetInterfaceImplementations().Select(h => assembly.MemberName(assembly.Reader.GetInterfaceImplementation(h).Interface));
            Assert.Contains("Eternal.Interfaces.IFoodDebtReader", interfaces);
            Assert.Contains("Eternal.Interfaces.IFoodDebtWriter", interfaces);
            Assert.Empty(assembly.Methods("Eternal.Interfaces.IDebtAccumulator", "AddDebtWithResurrectionCap"));
        }

        [Theory]
        [InlineData("IFoodDebtWriter", "RegisterPawn", "", "System.Void")]
        [InlineData("IFoodDebtWriter", "UnregisterPawn", "", "System.Void")]
        [InlineData("IFoodDebtWriter", "AddDebt", ",System.Single", "System.Boolean")]
        [InlineData("IFoodDebtWriter", "RepayDebt", ",System.Single", "System.Single")]
        [InlineData("IFoodDebtWriter", "AddResurrectionDebt", ",System.Single", "System.Boolean")]
        [InlineData("IFoodDebtReader", "GetDebt", "", "System.Single")]
        [InlineData("IFoodDebtReader", "GetMaxCapacity", "", "System.Single")]
        [InlineData("IFoodDebtReader", "HasDebt", "", "System.Boolean")]
        [InlineData("IFoodDebtReader", "HasExcessiveDebt", "", "System.Boolean")]
        [InlineData("IFoodDebtReader", "GetPeakDebt", "", "System.Single")]
        [InlineData("IDebtAccumulator", "AddResurrectionDebt", ",System.Single", "System.Boolean")]
        public void DebtInterfaces_DeclareExactOwnerArgumentsAndReturn(string owner, string method, string extraArguments, string returns)
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            assembly.Method("Eternal.Interfaces." + owner, method, ("Verse.Pawn" + extraArguments).Split(','), returns, isStatic: false);
        }

        // -----------------------------------------------------------------
        // Debt formula constants verification
        // Ensures TestData constants match production SettingsDefaults
        // -----------------------------------------------------------------

        [Fact]
        public void DebtFormula_MaxDebtMultiplier_Matches()
        {
            Assert.Equal(5.0f, TestData.DefaultMaxDebtMultiplier, TestData.FloatPrecision);
        }

        [Fact]
        public void DebtFormula_SeverityToNutritionRatio_Is1200To1()
        {
            // Apex-entry benchmark: one nutrition buys 1200 normal-injury HP.
            float ratio = TestData.DefaultSeverityToNutritionRatio;
            int inverseRatio = (int)Math.Round(1.0f / ratio);
            Assert.Equal(1200, inverseRatio);
        }

        [Fact]
        public void DebtFormula_NutritionCostMultiplier_IsOne()
        {
            Assert.Equal(1.0f, TestData.DefaultNutritionCostMultiplier, TestData.FloatPrecision);
        }

        [Fact]
        public void DebtFormula_FoodDrainThreshold_Is15Percent()
        {
            Assert.Equal(0.15f, TestData.DefaultFoodDrainThreshold, TestData.FloatPrecision);
        }

        [Theory]
        [InlineData(1f, 5f, 0f, 5f)]
        [InlineData(2f, 5f, 0f, 10f)]
        [InlineData(1.4f, 3f, 2f, 6.2f)]
        [InlineData(1f, 5f, 6f, 11f)]
        public void Capacity_UsesFoodOrFallbackSizeMultiplierAndBaseline(float size, float multiplier, float baseline, float expected)
            => Assert.Equal(expected, FoodDebtPolicy.Capacity(size, multiplier, baseline), 6);

        [Theory]
        [InlineData(4.5f, 1f, 5f, 5f)]
        [InlineData(3f, 1f, 5f, 4f)]
        [InlineData(6f, 8f, 11f, 11f)]
        public void OrdinaryAddition_ClampsInsteadOfRejecting(float debt, float add, float cap, float expected)
            => Assert.Equal(expected, FoodDebtPolicy.Add(debt, add, cap));

        [Fact]
        public void ResurrectionAddition_IsUncappedAndCreatesHeadroom()
        {
            float debt = FoodDebtPolicy.AddResurrection(2f, 6f);
            Assert.Equal(8f, debt);
            Assert.Equal(13f, FoodDebtPolicy.Capacity(1f, 5f, debt));
            Assert.Equal(5f, FoodDebtPolicy.RemainingCapacity(13f, debt));
        }

        [Fact]
        public void PartialRepayment_ShrinksBaselineAndPreservesEpisodePeak()
        {
            float repaid = FoodDebtPolicy.Repayment(6f, 2f);
            Assert.Equal(2f, repaid);
            Assert.Equal(4f, 6f - repaid);
            Assert.Equal(4f, FoodDebtPolicy.BaselineAfterRepayment(6f, 4f));
            Assert.Equal(6f, FoodDebtPolicy.PeakAfterRepayment(6f, 4f));
            Assert.Equal(2f, FoodDebtPolicy.BaselineAfterRepayment(2f, 4f));
        }

        [Fact]
        public void FullRepayment_ClampsToDebtAndResetsBaselineAndPeak()
        {
            Assert.Equal(2f, FoodDebtPolicy.Repayment(2f, 5f));
            Assert.Equal(0f, FoodDebtPolicy.BaselineAfterRepayment(2f, 0f));
            Assert.Equal(0f, FoodDebtPolicy.PeakAfterRepayment(6f, 0f));
            Assert.Equal(7f, FoodDebtPolicy.PeakAfterAddition(6f, 7f));
            Assert.Equal(6f, FoodDebtPolicy.PeakAfterAddition(6f, 4f));
        }

        [Fact]
        public void CapacityStatus_UsesInclusiveLimitAndNonnegativeHeadroom()
        {
            Assert.True(FoodDebtPolicy.IsExcessive(5f, 5f));
            Assert.False(FoodDebtPolicy.IsExcessive(4.99f, 5f));
            Assert.Equal(0f, FoodDebtPolicy.RemainingCapacity(5f, 6f));
        }

        [Fact]
        public void CorpseNutrition_UsesActualCorpseCostRoute()
        {
            Assert.Equal(1f, CorpseHealingCostCalculator.FutureNutrition(250f, 0.004f, 1f, false), 6);
            Assert.Equal(0.004f, CorpseHealingCostCalculator.FutureNutrition(1f, 0.004f, 1f, false), 7);
            Assert.Equal(0.018f, CorpseHealingCostCalculator.FutureNutrition(3f, 0.003f, 2f, false), 7);
        }

        // -----------------------------------------------------------------
        // DEFERRED TO IN-GAME HARNESS (plan 07-04):
        // - UnifiedFoodDebtManager instantiation (Dictionary<Pawn, float> triggers Assembly-CSharp)
        // - All null-pawn guard tests on concrete methods
        // - Full lifecycle: Register -> AddDebt -> RepayDebt -> ClearDebt with real Pawn
        // - HasFoodNeedDisabled waiver behavior
        // - SyncCorpseData integration
        // - GetDebtStatusString formatting
        // - ExposeData save/load round-trip
        // -----------------------------------------------------------------
    }
}
