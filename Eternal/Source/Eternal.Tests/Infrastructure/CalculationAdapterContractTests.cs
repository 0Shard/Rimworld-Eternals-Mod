// Author: 0Shard
// Description: Decoded exact adapter inputs and result consumption; no CLR game-type loading.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using Eternal.Tests.UI;
using Xunit;

namespace Eternal.Tests.Infrastructure
{
    public class CalculationAdapterContractTests
    {
        private const string Healing = "Eternal.Healing.UnifiedHediffHealingCalculator";
        private const string HealingPolicy = "Eternal.Healing.HediffHealingPolicy";
        private const string Processor = "Eternal.Resources.DebtRepaymentProcessor";
        private const string Manager = "Eternal.Resources.UnifiedFoodDebtManager";
        private const string DebtPolicy = "Eternal.Resources.FoodDebtPolicy";

        [Fact]
        public void PawnHealingAdapter_ConsumesBodyFromActualPawn()
            => ExactHealingAdapter_ConsumesRealRateStageBodyAndEligibility("Verse.Pawn");

        [Fact]
        public void ExceptionLogger_BranchesOnProductionSeverityClassification()
        {
            var il = Read("Eternal.Utils.EternalLogger", "HandleException", "Eternal.Exceptions.EternalExceptionCategory",
                "System.String", "Verse.Pawn", "System.Exception");
            int classify = CallIndex(il, "Eternal.Exceptions.EternalExceptionSeverity.IsError");
            Assert.Equal(OpCodes.Ldarg_0, il[classify - 1].Code);
            int branch = classify + 1;
            if (il[branch].Code.FlowControl != FlowControl.Cond_Branch)
            {
                int level = Local(il[branch]);
                Assert.True(level >= 0);
                Assert.Equal(level, Local(il[branch + 1]));
                branch += 2;
                if (il[branch].Code.FlowControl != FlowControl.Cond_Branch)
                {
                    int condition = Local(il[branch]);
                    Assert.True(condition >= 0);
                    Assert.Equal(condition, Local(il[branch + 1]));
                    branch += 2;
                }
            }
            Assert.Equal(FlowControl.Cond_Branch, il[branch].Code.FlowControl);
            Assert.Contains(il.Skip(classify), i => Equals(i.Operand, "Verse.Log.Error"));
            Assert.Contains(il.Skip(classify), i => Equals(i.Operand, "Verse.Log.Warning"));
        }

        private void ExactHealingAdapter_ConsumesRealRateStageBodyAndEligibility(string pawnType)
        {
            var il = Read(Healing, "CalculateHediffHealing", pawnType, "Verse.Hediff", "Eternal.EternalHediffSetting");
            int policy = CallIndex(il, HealingPolicy + ".Calculate");
            Assert.Equal(new[]
            {
                Healing + ".GetEffectiveRate(arg0,arg3)",
                Healing + ".GetStageMultiplier(arg0,arg2)",
                Healing + ".GetBodySizeScaling(arg0,arg1)",
                "Eternal.Extensions.HediffExtensions.IsDebuffWithStages(arg2)"
            }, HealingArguments(il, policy));
            AssertReturned(il, policy);
            Assert.Contains(Read(Healing, "GetBodySizeScaling", pawnType), i =>
                Equals(i.Operand, pawnType + ".get_BodySize"));
            Assert.Contains(Read(Healing, "GetBodySizeScaling", pawnType), i =>
                Equals(i.Operand, HealingPolicy + ".BodySize"));
        }

        [Fact]
        public void HealingStageAndRateAdapters_ReadActualHediffAndSettings()
        {
            var stage = Read(Healing, "GetStageMultiplier", "Verse.Hediff");
            int getStage = CallIndex(stage, "Verse.Hediff.get_CurStageIndex");
            Assert.True(CallIndex(stage, HealingPolicy + ".StageMultiplier") > getStage);
            Assert.Contains(stage, i => Equals(i.Operand, "Eternal.Extensions.HediffExtensions.IsDebuffWithStages"));
            var rate = Read(Healing, "GetEffectiveRate", "Eternal.EternalHediffSetting");
            Assert.Contains(rate, i => Equals(i.Operand, "Eternal.EternalHediffSetting.GetEffectiveHealingRate"));
            Assert.Contains(rate, i => Equals(i.Operand, "Eternal.Interfaces.ISettingsProvider.get_BaseHealingRate"));
            Assert.Contains(rate, i => i.Code == OpCodes.Ldc_R4 && Equals(i.Operand, 1.2f));
        }

        [Fact]
        public void DebtProcessor_PassesCurrentPeakWindowCadenceAndFoodToPolicyThenWritesSameDrain()
        {
            var rate = Read(Processor, "CalculateDrainRate", "Verse.Pawn", "System.Single");
            int ratePolicy = CallIndex(rate, DebtPolicy + ".DrainRate");
            Assert.Equal(OpCodes.Ldarg_2, rate[ratePolicy - 8].Code);
            Assert.Equal("Eternal.Interfaces.IFoodDebtReader.GetPeakDebt", rate[ratePolicy - 4].Operand);
            Assert.Equal("Eternal.Interfaces.ISettingsProvider.get_DebtRepaymentDays", rate[ratePolicy - 1].Operand);
            AssertReturned(rate, ratePolicy);

            var il = Read(Processor, "ProcessPawnDebtRepayment", "Verse.Pawn");
            int debt = ResultLocal(il, "Eternal.Interfaces.IFoodDebtReader.GetDebt");
            int drainRate = ResultLocal(il, Processor + ".CalculateDrainRate");
            int drain = CallIndex(il, DebtPolicy + ".Drain");
            Assert.Equal(debt, Local(il[drain - 12]));
            Assert.Equal(drainRate, Local(il[drain - 11]));
            Assert.Equal("Eternal.Interfaces.ISettingsProvider.get_RareTickRate", il[drain - 8].Operand);
            Assert.Equal("RimWorld.Need.get_CurLevel", il[drain - 6].Operand);
            Assert.Equal("RimWorld.Need.get_MaxLevel", il[drain - 4].Operand);
            Assert.Equal("Eternal.Interfaces.ISettingsProvider.get_FoodDrainThreshold", il[drain - 1].Operand);
            int amount = Local(il[drain + 1]);
            Assert.True(amount >= 0);
            int foodWrite = CallIndex(il, "RimWorld.Need.set_CurLevel");
            int bounded = CallIndex(il, DebtPolicy + ".FoodAfterPayment");
            Assert.Equal(amount, Local(il[bounded - 3]));
            Assert.Equal(debt, Local(il[bounded - 2]));
            Assert.Equal(bounded, foodWrite - 1);
            int repay = CallIndex(il, "Eternal.Interfaces.IFoodDebtWriter.RepayDebt");
            Assert.Equal(amount, Local(il[repay - 1]));
        }

        [Theory]
        [InlineData("AddDebt", false)]
        [InlineData("AddResurrectionDebt", true)]
        public void Manager_AdditionsWritePolicyTotalToOwnedLedger(string method, bool resurrection)
        {
            var wrapper = Read(Manager, method, "Verse.Pawn", "System.Single");
            int core = CallIndex(wrapper, Manager + ".AddDebtCore");
            Assert.Equal(OpCodes.Ldarg_1, wrapper[core - 3].Code);
            Assert.Equal(OpCodes.Ldarg_2, wrapper[core - 2].Code);
            Assert.Equal(resurrection ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0, wrapper[core - 1].Code);
            var il = Read(Manager, "AddDebtCore", "Verse.Pawn", "System.Single", "System.Boolean");
            int policy = CallIndex(il, DebtPolicy + ".AddExact");
            int local = Local(il[policy - 5]);
            Assert.True(local >= 0);
            Assert.Equal(OpCodes.Ldarg_2, il[policy - 1].Code);
            Assert.Equal(Manager + ".GetExactCapacity", il[policy - 2].Operand);
            int ledger = il.FindIndex(policy + 1, i => i.Code == OpCodes.Ldfld && Equals(i.Operand, "_debtTracker"));
            Assert.True(ledger > policy);
            Assert.Equal(OpCodes.Ldarg_1, il[ledger + 1].Code);
            Assert.Equal(local, Local(il[ledger + 2]));
            Assert.True((il[ledger + 3].Operand as string)?.EndsWith(".set_Item") == true);
            int peak = CallIndex(il, Manager + ".UpdatePeakDebt");
            Assert.Equal(local, Local(il[peak - 1]));
            if (resurrection)
            {
                int addition = CallIndex(il, "Eternal.Resources.NutritionAmount.op_Addition");
                Assert.Equal(local, Local(il[addition + 1]));
                int baseline = il.FindIndex(addition + 1, i => i.Code == OpCodes.Ldfld && Equals(i.Operand, "_resurrectionDebtBaseline"));
                Assert.Equal(local, Local(il[baseline + 2]));
            }
            Assert.Contains(il, i => Equals(i.Operand, "Eternal.Extensions.PawnExtensions.HasFoodNeedDisabled"));
            Assert.Contains(il, i => Equals(i.Operand, Manager + ".SyncCorpseData"));
        }

        [Fact]
        public void Manager_RepaymentCapacityBaselineAndPeakRemainProductionPolicyConsumers()
        {
            var repayment = Read(Manager, "RepayDebt", "Verse.Pawn", "System.Single");
            int paid = ResultLocal(repayment, DebtPolicy + ".RepayExact");
            Assert.True(paid >= 0);
            int settle = CallIndex(repayment, DebtPolicy + ".RepayExact");
            int remaining = Local(repayment[settle - 2]);
            Assert.True(remaining >= 0);
            int ledger = repayment.FindIndex(settle, i => i.Code == OpCodes.Ldfld && Equals(i.Operand, "_debtTracker"));
            Assert.Equal(remaining, Local(repayment[ledger + 2]));
            Assert.True((repayment[ledger + 3].Operand as string)?.EndsWith(".set_Item") == true);
            int baseline = CallIndex(repayment, "Eternal.Resources.NutritionAmount.Min");
            Assert.Equal(remaining, Local(repayment[baseline - 1]));
            Assert.True((repayment[baseline + 1].Operand as string)?.EndsWith(".set_Item") == true);
            Assert.Contains(repayment, i => Equals(i.Operand, "Eternal.Resources.NutritionAmount.get_IsZero"));
            var cap = Read(Manager, "GetExactCapacity", "Verse.Pawn");
            Assert.Contains(cap, i => Equals(i.Operand, "RimWorld.Need.get_MaxLevel"));
            Assert.Contains(cap, i => Equals(i.Operand, "Verse.Pawn.get_BodySize"));
            Assert.Contains(cap, i => Equals(i.Operand, Manager + ".get_MaxDebtMultiplier"));
            Assert.Contains(cap, i => Equals(i.Operand, Manager + ".GetResurrectionDebtBaseline"));
            int capacity = CallIndex(cap, "Eternal.Resources.NutritionAmount.op_Addition");
            Assert.Contains(cap, i => Equals(i.Operand, "Eternal.Resources.NutritionAmount.Product"));
            AssertReturned(cap, capacity);
        }

        private static List<CompiledInstruction> Read(string type, string method, params string[] signature)
            => MenuAssemblyContract.Instructions(type, method, signature).Where(i => i.Code != OpCodes.Nop).ToList();

        private static int CallIndex(List<CompiledInstruction> il, string name)
        {
            int index = il.FindIndex(i => Equals(i.Operand, name) && (i.Code == OpCodes.Call || i.Code == OpCodes.Callvirt));
            Assert.True(index >= 0, "Missing decoded call: " + name);
            return index;
        }

        private static string[] HealingArguments(List<CompiledInstruction> il, int policy)
        {
            // This bounded slice has no branches: follow stack/local provenance in Debug and Release.
            // Unknown instructions fail closed rather than guessing at compiler optimizations.
            int start = CallIndex(il, Healing + ".GetEffectiveRate") - 2;
            var stack = new Stack<string>();
            var locals = new Dictionary<int, string>();
            for (int index = start; index <= policy; index++)
            {
                var instruction = il[index];
                if (instruction.Code == OpCodes.Ldarg_0)
                {
                    stack.Push("arg0");
                }
                else if (instruction.Code == OpCodes.Ldarg_1)
                {
                    stack.Push("arg1");
                }
                else if (instruction.Code == OpCodes.Ldarg_2)
                {
                    stack.Push("arg2");
                }
                else if (instruction.Code == OpCodes.Ldarg_3)
                {
                    stack.Push("arg3");
                }
                else if (instruction.Code == OpCodes.Ldc_R4)
                {
                    stack.Push("constant:" + instruction.Operand);
                }
                else if (instruction.Code.Name.StartsWith("stloc", StringComparison.Ordinal))
                {
                    locals[Local(instruction)] = stack.Pop();
                }
                else if (instruction.Code.Name.StartsWith("ldloc", StringComparison.Ordinal))
                {
                    stack.Push(locals[Local(instruction)]);
                }
                else if (instruction.Code == OpCodes.Call || instruction.Code == OpCodes.Callvirt)
                {
                    string name = instruction.Operand as string;
                    Assert.True(instruction.CallSignature.HasValue, "Undecoded call in healing input slice: " + name);
                    var signature = instruction.CallSignature.Value;
                    int count = signature.ParameterTypes.Length + (signature.Header.IsInstance ? 1 : 0);
                    var args = new string[count];
                    for (int i = count - 1; i >= 0; i--)
                    {
                        args[i] = stack.Pop();
                    }

                    if (index == policy)
                    {
                        Assert.Empty(stack);
                        return args;
                    }
                    stack.Push(name + "(" + string.Join(",", args) + ")");
                }
                else
                {
                    Assert.True(false, "Unsupported instruction in healing input slice: " + instruction.Code);
                }
            }
            throw new InvalidOperationException("Policy call not reached");
        }

        private static int ResultLocal(List<CompiledInstruction> il, string call)
        {
            int index = CallIndex(il, call);
            int local = Local(il[index + 1]);
            Assert.True(local >= 0, "Call result must be stored: " + call);
            return local;
        }

        private static void AssertReturned(List<CompiledInstruction> il, int call)
        {
            // Release may return directly; Debug stores, branches to and loads the return local.
            if (il[call + 1].Code == OpCodes.Ret)
            {
                return;
            }

            int local = Local(il[call + 1]);
            Assert.True(local >= 0);
            int ret = il.FindIndex(call + 1, i => i.Code == OpCodes.Ret);
            Assert.True(ret > call);
            Assert.Equal(local, Local(il[ret - 1]));
            Assert.All(il.Skip(call + 2).Take(ret - call - 3), i =>
                Assert.True(i.Code == OpCodes.Br || i.Code == OpCodes.Br_S, "Policy result must reach return unchanged"));
        }

        internal static int Local(CompiledInstruction instruction)
        {
            return CompiledAssemblyContract.Local(instruction);
        }
    }
}
