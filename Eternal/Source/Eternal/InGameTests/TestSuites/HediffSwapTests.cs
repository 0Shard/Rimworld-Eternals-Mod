// Relative Path: Eternal/Source/Eternal/InGameTests/TestSuites/HediffSwapTests.cs
// Author: 0Shard
// Description: Real healed/immediate lifecycle, tracker identity, debt history and failure windows.
#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Eternal.DI;
using Eternal.Extensions;
using Eternal.InGameTests.Helpers;

namespace Eternal.InGameTests.TestSuites
{
    public static class HediffSwapTests
    {
        public static TestSuiteResult RunAll(Verse.Map map)
        {
            var result = new TestSuiteResult(0, 0, new List<string>());
            foreach (bool immediate in new[] { false, true })
            {
                bool selectedMode = immediate;
                string mode = immediate ? "Immediate" : "Healed";
                result.RunTest("HediffSwap", mode + "PreservesTrackersAndDebtHistory",
                    () => VerifyLifecycle(map, selectedMode, FailureWindow.None, false));
                result.RunTest("HediffSwap", mode + "AlivePostfixExceptionCompletes",
                    () => VerifyLifecycle(map, selectedMode, FailureWindow.AlivePostfix, false));
                result.RunTest("HediffSwap", mode + "DeadPrefixExceptionRestores",
                    () => VerifyLifecycle(map, selectedMode, FailureWindow.DeadPrefix, false));
                result.RunTest("HediffSwap", mode + "SecondTrackerExceptionRestores",
                    () => VerifyLifecycle(map, selectedMode, FailureWindow.SecondTracker, false));
                result.RunTest("HediffSwap", mode + "StoredCorpseResurrects",
                    () => VerifyLifecycle(map, selectedMode, FailureWindow.None, true));
            }
            return result;
        }

        private static void VerifyLifecycle(Verse.Map map, bool immediate, FailureWindow failure, bool stored)
        {
            var pawn = TestPawnFactory.SpawnEternalPawn(map);
            TestPawnFactory.KillPawn(pawn);
            var services = EternalServiceContainer.Instance;
            var corpseData = services.CorpseManager.GetCorpseData(pawn);
            TestAssert.IsNotNull(corpseData, "Death hook must register the owned corpse");
            var corpse = corpseData.Corpse;
            var savedSet = pawn.health.hediffSet;
            var savedImmunity = pawn.health.immunity;
            var essence = savedSet.GetFirstHediffOfDef(EternalDefOf.Eternal_Essence);
            TestAssert.IsNotNull(essence, "Owned pawn must have Essence before the swap");

            // Prepare the completed-corpse state. We call the retained lifecycle entry points
            // directly so tick timing and unrelated healing do not obscure their mode contract.
            services.FoodDebtSystem.RegisterPawn(pawn);
            bool waived = pawn.HasFoodNeedDisabled();
            services.FoodDebtSystem.AddResurrectionDebt(pawn, 2f);
            float priorDebt = services.FoodDebtSystem.GetDebt(pawn);
            corpseData.FoodDebt = 3f;
            var carrier = stored ? TestPawnFactory.SpawnNonEternalPawn(map) : null;
            if (stored)
            {
                corpse.DeSpawn(DestroyMode.WillReplace);
                TestAssert.IsTrue(carrier.inventory.innerContainer.TryAdd(corpse, false),
                    "Owned carrier inventory must accept the corpse");
                TestAssert.IsFalse(corpse.Spawned, "Stored fixture must enter the unspawned-corpse branch");
            }

            using (var observation = new LifecycleObservation(pawn, savedSet, failure))
            {
                var entryPoint = services.CorpseHealingProcessor.GetType().GetMethod(
                    immediate ? "ResurrectImmediately" : "CompleteResurrection",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                TestAssert.IsNotNull(entryPoint, "Retained lifecycle entry point must exist");
                entryPoint.Invoke(services.CorpseHealingProcessor, new object[] { corpseData });

                bool hardFailure = failure == FailureWindow.DeadPrefix || failure == FailureWindow.SecondTracker;
                TestAssert.AreEqual(hardFailure, pawn.Dead, "Only a pre-resurrection exception should leave pawn dead");
                TestAssert.IsTrue(ReferenceEquals(savedSet, pawn.health.hediffSet), "Exact saved HediffSet must return");
                TestAssert.IsTrue(ReferenceEquals(savedImmunity, pawn.health.immunity), "Exact saved immunity must return");
                TestAssert.IsTrue(ReferenceEquals(essence, pawn.health.hediffSet.GetFirstHediffOfDef(EternalDefOf.Eternal_Essence)),
                    "The original Essence instance must survive the swap");
                TestAssert.IsNotNull(pawn.story.traits.GetTrait(EternalDefOf.Eternal_GeneticMarker),
                    "Eternal trait must survive resurrection");
                TestAssert.IsFalse(services.CorpseManager.IsExpectedDestruction(corpse),
                    "Expected-destruction marker must clear on every outcome");
                TestAssert.AreEqual(failure != FailureWindow.None, observation.Injected,
                    "The requested exception window must actually execute");

                if (hardFailure)
                {
                    TestAssert.IsTrue(services.CorpseManager.IsTracked(pawn), "Failed resurrection must retain corpse tracking");
                    TestAssert.AreEqual(0, observation.DebtCalls.Count, "Failed swap must not transfer or reset debt");
                    TestAssert.AreEqual(priorDebt, services.FoodDebtSystem.GetDebt(pawn), "Failed swap preserves debt");
                    TestAssert.IsFalse(corpse.Destroyed, "Failed resurrection must preserve corpse");
                }
                else
                {
                    TestAssert.IsFalse(services.CorpseManager.IsTracked(pawn), "Successful resurrection must untrack corpse");
                    TestAssert.IsTrue(corpse.Destroyed, "Successful resurrection consumes corpse");
                    string expected = immediate ? "RegisterPawn,AddResurrectionDebt" : "UnregisterPawn,RegisterPawn,AddResurrectionDebt";
                    TestAssert.AreEqual(expected, string.Join(",", observation.DebtCalls), "Mode must retain actual debt-call history");
                    TestAssert.AreEqual(waived ? 0f : (immediate ? priorDebt + 3f : 3f),
                        services.FoodDebtSystem.GetDebt(pawn), "Captured debt must transfer exactly once");
                    if (stored)
                    {
                        TestAssert.IsFalse(carrier.inventory.innerContainer.Contains(corpse), "Consumed corpse must leave its owner");
                    }
                }
            }
        }

        private enum FailureWindow { None, DeadPrefix, AlivePostfix, SecondTracker }

        // Hooks observe real game and debt methods. They affect only this fixture pawn,
        // never replace Verse behavior, and are removed even when an assertion fails.
        private sealed class LifecycleObservation : IDisposable
        {
            private const string Owner = "Eternal.Tests.ResurrectionLifecycle";
            private static LifecycleObservation active;
            private readonly Harmony harmony = new Harmony(Owner);
            private readonly List<MethodBase> patched = new List<MethodBase>();
            private readonly Pawn pawn;
            private readonly HediffSet savedSet;
            private readonly FailureWindow failure;
            public readonly List<string> DebtCalls = new List<string>();
            public bool Injected { get; private set; }

            public LifecycleObservation(Pawn pawn, HediffSet savedSet, FailureWindow failure)
            {
                TestAssert.IsNull(active, "Lifecycle instrumentation cannot nest");
                this.pawn = pawn;
                this.savedSet = savedSet;
                this.failure = failure;
                active = this;
                try
                {
                    var resurrect = typeof(ResurrectionUtility).GetMethods(BindingFlags.Static | BindingFlags.Public)
                        .Single(m => m.Name == "TryResurrect" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(Pawn));
                    Patch(resurrect, nameof(BeforeResurrection), nameof(AfterResurrection));
                    Patch(typeof(ImmunityHandler).GetConstructor(new[] { typeof(Pawn) }), nameof(BeforeImmunity), null);
                    var debtType = EternalServiceContainer.Instance.FoodDebtSystem.GetType();
                    foreach (string method in new[] { "RegisterPawn", "UnregisterPawn", "AddResurrectionDebt" })
                    {
                        Patch(debtType.GetMethod(method), nameof(ObserveDebt), null);
                    }
                }
                catch { Dispose(); throw; }
            }

            private void Patch(MethodBase method, string prefix, string postfix)
            {
                TestAssert.IsNotNull(method, "Actual lifecycle method to instrument");
                patched.Add(method);
                harmony.Patch(method, prefix == null ? null : new HarmonyMethod(typeof(LifecycleObservation), prefix),
                    postfix == null ? null : new HarmonyMethod(typeof(LifecycleObservation), postfix));
            }

            private static void BeforeResurrection(Pawn __0)
            {
                var observation = active;
                if (observation == null || !ReferenceEquals(observation.pawn, __0))
                {
                    return;
                }

                TestAssert.IsTrue(EternalServiceContainer.Instance.CorpseManager.IsExpectedDestruction(__0.Corpse),
                    "Resurrection must enter game utility with expected destruction armed");
                if (observation.failure == FailureWindow.DeadPrefix)
                {
                    observation.ThrowInjected();
                }
            }

            private static void AfterResurrection(Pawn __0)
            {
                var observation = active;
                if (observation != null && ReferenceEquals(observation.pawn, __0) && observation.failure == FailureWindow.AlivePostfix)
                {
                    TestAssert.IsFalse(__0.Dead, "Soft exception must happen after actual resurrection");
                    observation.ThrowInjected();
                }
            }

            private static void BeforeImmunity(Pawn __0)
            {
                var observation = active;
                if (observation != null && ReferenceEquals(observation.pawn, __0) && observation.failure == FailureWindow.SecondTracker &&
                    !ReferenceEquals(__0.health.hediffSet, observation.savedSet))
                {
                    observation.ThrowInjected();
                }
            }

            private static void ObserveDebt(Pawn __0, MethodBase __originalMethod)
            {
                if (active != null && ReferenceEquals(active.pawn, __0))
                {
                    active.DebtCalls.Add(__originalMethod.Name);
                }
            }

            private void ThrowInjected()
            {
                Injected = true;
                throw new InvalidOperationException("Owned resurrection lifecycle exception fixture");
            }

            public void Dispose()
            {
                try
                {
                    foreach (var method in patched)
                    {
                        harmony.Unpatch(method, HarmonyPatchType.All, Owner);
                    }
                }
                finally { active = null; }
            }
        }
    }
}
#endif
