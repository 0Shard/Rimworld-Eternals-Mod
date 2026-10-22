// Author: 0Shard
// Description: Compiled debt save/load and dead-owner validation contracts; native persistence is checked by the game fixture.
using System.Linq;
using System.Reflection.Emit;
using Eternal.Tests.Infrastructure;
using Xunit;

namespace Eternal.Tests.Resources
{
    public class DebtPersistenceContractTests
    {
        private const string Manager = "Eternal.Resources.UnifiedFoodDebtManager";

        [Theory]
        [InlineData("debtPawns", "scribedDebtPawns")]
        [InlineData("resurrectionBaselinePawns", "scribedBaselinePawns")]
        [InlineData("peakDebtPawns", "scribedPeakPawns")]
        public void PawnReferenceListsRetainDestroyedPawnIdsInStableFieldBuffers(string label, string field)
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var il = assembly.Instructions(assembly.Method(Manager, "ExposeData"));
            int key = il.FindIndex(i => i.Code == OpCodes.Ldstr && Equals(i.Operand, label));
            Assert.True(key > 0);
            Assert.Equal(OpCodes.Ldflda, il[key - 1].Code);
            Assert.Equal(field, il[key - 1].Operand);
            Assert.Equal(OpCodes.Ldc_I4_1, il[key + 1].Code);
            var call = il.Skip(key + 2).First(i => i.Code == OpCodes.Call && Equals(i.Operand, "Verse.Scribe_Collections.Look"));
            Assert.Equal("Verse.Scribe_Collections.Look", call.Operand);
            Assert.Equal("System.Boolean", call.CallSignature.Value.ParameterTypes[2]);
            Assert.Equal("Verse.LookMode", call.CallSignature.Value.ParameterTypes[3]);
        }

        [Fact]
        public void StableAmountNodesAndNullIdentityWarningsRemainInProduction()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var expose = assembly.Instructions(assembly.Method(Manager, "ExposeData"));
            foreach (string label in new[] { "debtAmounts", "resurrectionBaselineAmounts", "peakDebtAmounts",
                "debtExactUnits", "resurrectionBaselineExactUnits", "peakDebtExactUnits" })
            {
                Assert.Single(expose.Where(i => i.Code == OpCodes.Ldstr && Equals(i.Operand, label)));
            }

            Assert.Equal(3, expose.Count(i => Equals(i.Operand, Manager + ".LoadPawnFloatPairs")));
            var load = assembly.Instructions(assembly.Method(Manager, "LoadPawnFloatPairs"));
            Assert.Contains(load, i => (i.Operand as string)?.Contains("no recoverable pawn identity") == true);
            Assert.Contains(load, i => Equals(i.Operand, "Verse.Log.Warning"));
            Assert.Contains(load, i => Equals(i.Operand, "System.Math.Min"));
            Assert.Contains(load, i => Equals(i.Operand, "Eternal.Resources.NutritionAmount.TryParse"));
            Assert.Contains(load, i => Equals(i.Operand, "Eternal.Resources.NutritionAmount.FromFloat"));
        }

        [Fact]
        public void RegistrationAndPeriodicCleanupUseTheSameDeadAwareValidation()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            foreach (string method in new[] { "RegisterPawn", "CleanupInvalidEntries" })
            {
                var body = assembly.Instructions(assembly.Method(Manager, method));
                Assert.Contains(body, i => Equals(i.Operand, Manager + ".IsValidDebtPawn"));
                Assert.DoesNotContain(body, i => Equals(i.Operand, "Verse.Thing.get_Destroyed"));
            }
            var validation = assembly.Instructions(assembly.Method(Manager, "IsValidDebtPawn"));
            Assert.Contains(validation, i => Equals(i.Operand, "Verse.Pawn.get_Dead"));
            Assert.Contains(validation, i => Equals(i.Operand, "Eternal.Extensions.PawnExtensions.IsValidEternalCorpse"));
            Assert.Contains(validation, i => Equals(i.Operand, "Eternal.Extensions.PawnExtensions.IsValidEternal"));
            Assert.DoesNotContain(validation, i => Equals(i.Operand, "Verse.Thing.get_Destroyed"));
        }
    }
}
