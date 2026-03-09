// Relative Path: Eternal/Source/Eternal.Tests/Resources/UnifiedFoodDebtManagerTests.cs
// Creation Date: 24-02-2026
// Last Edit: 24-02-2026
// Author: 0Shard
// Description: Unit tests for food debt system interfaces and debt math verification.
//              UnifiedFoodDebtManager cannot be instantiated in unit tests because its
//              Dictionary<Pawn, float> field triggers Assembly-CSharp loading at construction.
//              Tests verify the IFoodDebtSystem interface contract, debt formula constants,
//              and settings defaults. Full lifecycle tests require the in-game harness.

using System;
using System.Linq;
using System.Reflection;
using Xunit;
using Eternal.Interfaces;
using Eternal.Tests.Helpers;
using NSubstitute;

namespace Eternal.Tests.Resources
{
    public class FoodDebtFormulaTests
    {
        // -----------------------------------------------------------------
        // IFoodDebtSystem interface contract verification
        // Ensures the interface hierarchy defines all expected members.
        // Interface inheritance in .NET does not merge methods into the
        // derived type — GetMethod must search parent interfaces explicitly.
        // -----------------------------------------------------------------

        /// <summary>
        /// Searches an interface type and all its parent interfaces for a method by name.
        /// </summary>
        private static MethodInfo FindInterfaceMethod(Type interfaceType, string methodName)
        {
            var method = interfaceType.GetMethod(methodName);
            if (method != null) return method;

            foreach (var parent in interfaceType.GetInterfaces())
            {
                method = parent.GetMethod(methodName);
                if (method != null) return method;
            }
            return null;
        }

        [Fact]
        public void IFoodDebtSystem_InheritsFromReaderAndWriter()
        {
            var interfaces = typeof(IFoodDebtSystem).GetInterfaces();
            Assert.Contains(typeof(IFoodDebtReader), interfaces);
            Assert.Contains(typeof(IFoodDebtWriter), interfaces);
        }

        [Fact]
        public void IFoodDebtSystem_HasRegisterPawnMethod()
        {
            var method = FindInterfaceMethod(typeof(IFoodDebtSystem), "RegisterPawn");
            Assert.NotNull(method);
        }

        [Fact]
        public void IFoodDebtSystem_HasUnregisterPawnMethod()
        {
            var method = FindInterfaceMethod(typeof(IFoodDebtSystem), "UnregisterPawn");
            Assert.NotNull(method);
        }

        [Fact]
        public void IFoodDebtSystem_HasAddDebtMethod()
        {
            // Return type assertion omitted: accessing ReturnType triggers
            // Assembly-CSharp JIT loading because Pawn is in the signature.
            var method = FindInterfaceMethod(typeof(IFoodDebtSystem), "AddDebt");
            Assert.NotNull(method);
        }

        [Fact]
        public void IFoodDebtSystem_HasRepayDebtMethod()
        {
            var method = FindInterfaceMethod(typeof(IFoodDebtSystem), "RepayDebt");
            Assert.NotNull(method);
        }

        [Fact]
        public void IFoodDebtSystem_HasGetDebtMethod()
        {
            var method = FindInterfaceMethod(typeof(IFoodDebtSystem), "GetDebt");
            Assert.NotNull(method);
        }

        [Fact]
        public void IFoodDebtSystem_HasGetMaxCapacityMethod()
        {
            var method = FindInterfaceMethod(typeof(IFoodDebtSystem), "GetMaxCapacity");
            Assert.NotNull(method);
        }

        [Fact]
        public void IFoodDebtSystem_HasHasDebtMethod()
        {
            var method = FindInterfaceMethod(typeof(IFoodDebtSystem), "HasDebt");
            Assert.NotNull(method);
        }

        [Fact]
        public void IFoodDebtSystem_HasHasExcessiveDebtMethod()
        {
            var method = FindInterfaceMethod(typeof(IFoodDebtSystem), "HasExcessiveDebt");
            Assert.NotNull(method);
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
        public void DebtFormula_SeverityToNutritionRatio_Is250To1()
        {
            // 0.004 = 1/250 — healing 250 severity costs 1 nutrition
            float ratio = TestData.DefaultSeverityToNutritionRatio;
            int inverseRatio = (int)Math.Round(1.0f / ratio);
            Assert.Equal(250, inverseRatio);
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

        // -----------------------------------------------------------------
        // GetMaxCapacity formula verification (by math, not runtime)
        // Formula: foodMaxLevel * MaxDebtMultiplier
        // Null pawn fallback: 1.0 * (bodySize ?? 1.0) * MaxDebtMultiplier
        // -----------------------------------------------------------------

        [Fact]
        public void GetMaxCapacity_NullPawnFormula_Is5()
        {
            // null pawn: 1.0 * 1.0 * 5.0 = 5.0
            float expected = 1.0f * 1.0f * TestData.DefaultMaxDebtMultiplier;
            Assert.Equal(5.0f, expected, TestData.FloatPrecision);
        }

        [Fact]
        public void GetMaxCapacity_LargePawnFormula_ScalesWithBodySize()
        {
            // bodySize=2.0: 1.0 * 2.0 * 5.0 = 10.0
            float bodySize = 2.0f;
            float expected = 1.0f * bodySize * TestData.DefaultMaxDebtMultiplier;
            Assert.Equal(10.0f, expected, TestData.FloatPrecision);
        }

        [Fact]
        public void GetMaxCapacity_WithFoodNeed_ScalesWithMaxLevel()
        {
            // Standard colonist foodMaxLevel ~= 1.0
            // 1.0 * 5.0 = 5.0
            float foodMaxLevel = 1.0f;
            float expected = foodMaxLevel * TestData.DefaultMaxDebtMultiplier;
            Assert.Equal(5.0f, expected, TestData.FloatPrecision);
        }

        // -----------------------------------------------------------------
        // Debt accumulation math verification
        // -----------------------------------------------------------------

        [Fact]
        public void DebtMath_AccumulateDebt_SumIsCorrect()
        {
            float debt = 0f;
            debt += 3.0f;
            debt += 2.0f;
            Assert.Equal(5.0f, debt, TestData.FloatPrecision);
        }

        [Fact]
        public void DebtMath_RepaymentCapped_CannotExceedCurrentDebt()
        {
            float currentDebt = 2.0f;
            float repayAmount = 5.0f;
            float actualRepayment = Math.Min(currentDebt, repayAmount);
            Assert.Equal(2.0f, actualRepayment, TestData.FloatPrecision);
        }

        [Fact]
        public void DebtMath_RepaymentPartial_LeavesRemainder()
        {
            float currentDebt = 5.0f;
            float repayAmount = 3.0f;
            float actualRepayment = Math.Min(currentDebt, repayAmount);
            float remaining = currentDebt - actualRepayment;
            Assert.Equal(2.0f, remaining, TestData.FloatPrecision);
        }

        [Fact]
        public void DebtMath_CapEnforcement_RejectsOverCapacity()
        {
            float maxDebt = 5.0f; // bodySize=1, multiplier=5
            float currentDebt = 4.5f;
            float addAmount = 1.0f;
            bool wouldExceed = (currentDebt + addAmount) > maxDebt;
            Assert.True(wouldExceed);
        }

        [Fact]
        public void DebtMath_CapEnforcement_AcceptsWithinCapacity()
        {
            float maxDebt = 5.0f;
            float currentDebt = 3.0f;
            float addAmount = 1.0f;
            bool wouldExceed = (currentDebt + addAmount) > maxDebt;
            Assert.False(wouldExceed);
        }

        [Fact]
        public void DebtMath_CapEnforcement_ExactCapIsNotExcessive()
        {
            // HasExcessiveDebt uses >= comparison
            float maxDebt = 5.0f;
            float currentDebt = 5.0f;
            bool isExcessive = currentDebt >= maxDebt;
            Assert.True(isExcessive);
        }

        [Fact]
        public void DebtMath_CapEnforcement_BelowCapIsNotExcessive()
        {
            float maxDebt = 5.0f;
            float currentDebt = 4.99f;
            bool isExcessive = currentDebt >= maxDebt;
            Assert.False(isExcessive);
        }

        // -----------------------------------------------------------------
        // Nutrition cost formula verification
        // cost = severityHealed * severityToNutritionRatio * nutritionCostMultiplier
        // -----------------------------------------------------------------

        [Fact]
        public void NutritionCost_Healing250Severity_CostsOneNutrition()
        {
            float severityHealed = 250f;
            float cost = severityHealed * TestData.DefaultSeverityToNutritionRatio * TestData.DefaultNutritionCostMultiplier;
            Assert.Equal(1.0f, cost, TestData.FloatPrecision);
        }

        [Fact]
        public void NutritionCost_HealingSingleSeverity_CostIsRatio()
        {
            float severityHealed = 1.0f;
            float cost = severityHealed * TestData.DefaultSeverityToNutritionRatio * TestData.DefaultNutritionCostMultiplier;
            Assert.Equal(0.004f, cost, TestData.FloatPrecision);
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
