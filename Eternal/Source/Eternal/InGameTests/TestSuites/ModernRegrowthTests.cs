// Relative Path: Eternal/Source/Eternal/InGameTests/TestSuites/ModernRegrowthTests.cs
// Creation Date: 13-10-2026
// Author: 0Shard
// Description: Live regrowth projection and staged-child checks using owned pawns without advancing ticks.
#if DEBUG
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using Eternal.DI;
using Eternal.InGameTests.Helpers;

namespace Eternal.InGameTests.TestSuites
{
    public static class ModernRegrowthTests
    {
        public static TestSuiteResult RunAll(Verse.Map map)
        {
            var result = new TestSuiteResult(0, 0, new List<string>());
            result.RunTest("ModernRegrowth", "ExactPhaseBoundaries", () => ExactPhaseBoundaries(map));
            result.RunTest("ModernRegrowth", "UiEligibilityAndCacheRefresh", () => UiEligibilityAndCacheRefresh(map));
            result.RunTest("ModernRegrowth", "ChildStartAndCompletionGates", () => ChildStartAndCompletionGates(map));
            result.RunTest("ModernRegrowth", "CapturedBatchCompletesBeforeReleasedChildren",
                () => CapturedBatchCompletesBeforeReleasedChildren(map));
            return result;
        }

        private static EternalRegrowthManager Manager()
        {
            var manager = EternalServiceContainer.Instance.RegrowthManager;
            TestAssert.Available(manager, "Initialized regrowth manager");
            TestAssert.Available(EternalDefOf.Eternal_Regrowing, "Regrowth hediff def");
            return manager;
        }

        private static BodyPartRecord RemovePart(Pawn pawn, params string[] names)
        {
            var part = pawn.health.hediffSet.GetNotMissingParts()
                .FirstOrDefault(p => names.Contains(p.def.defName));
            TestAssert.Available(part, "Available fixture body part: " + string.Join(" or ", names));
            pawn.health.AddHediff(HediffDefOf.MissingBodyPart, part);
            TestAssert.IsTrue(pawn.health.hediffSet.PartIsMissing(part), "Fixture part must actually be missing");
            return part;
        }

        private static EternalRegrowing_Hediff Regrowth(EternalRegrowthManager manager, Pawn pawn, BodyPartRecord part)
        {
            var hediff = manager.GetRegrowingHediffs(pawn).SingleOrDefault(h => h.forPart == part);
            TestAssert.IsNotNull(hediff, "Production manager must create regrowth for the exact fixture part");
            return hediff;
        }

        private static void ExactPhaseBoundaries(Verse.Map map)
        {
            var manager = Manager();
            var pawn = TestPawnFactory.SpawnNonEternalPawn(map);
            var part = RemovePart(pawn, "Finger", "Toe");
            manager.StartRegrowth(pawn);
            var hediff = Regrowth(manager, pawn, part);
            var cases = new[]
            {
                (0f, RegrowthPhase.InitialFormation, 0f, "Initial Formation"),
                (0.249f, RegrowthPhase.InitialFormation, 0.996f, "Initial Formation"),
                (0.25f, RegrowthPhase.TissueDevelopment, 0f, "Tissue Development"),
                (0.499f, RegrowthPhase.TissueDevelopment, 0.996f, "Tissue Development"),
                (0.50f, RegrowthPhase.NerveIntegration, 0f, "Nerve Integration"),
                (0.749f, RegrowthPhase.NerveIntegration, 0.996f, "Nerve Integration"),
                (0.75f, RegrowthPhase.FunctionalCompletion, 0f, "Functional Completion"),
                (0.999f, RegrowthPhase.FunctionalCompletion, 0.996f, "Functional Completion"),
                (1f, RegrowthPhase.Complete, 1f, "Complete")
            };
            foreach (var item in cases)
            {
                hediff.Severity = item.Item1;
                var state = manager.GetRegrowthState(pawn);
                TestAssert.IsNotNull(state, "Existing regrowth must produce a projection");
                TestAssert.AreEqual(item.Item2, state.partPhases[part], "Boundary phase at " + item.Item1);
                TestAssert.InRange(state.partProgress[part], item.Item3 - 0.00001f, item.Item3 + 0.00001f,
                    "Progress within phase at " + item.Item1);
                TestAssert.AreEqual(item.Item4, EternalRegrowthState.GetPhaseSimpleName(item.Item2), "UI phase label");
            }
            TestAssert.AreEqual("Unknown Phase", EternalRegrowthState.GetPhaseSimpleName((RegrowthPhase)99),
                "Unknown enum remains safe to display");
        }

        private static void UiEligibilityAndCacheRefresh(Verse.Map map)
        {
            var manager = Manager();
            var pawn = TestPawnFactory.SpawnNonEternalPawn(map);
            var part = RemovePart(pawn, "Finger", "Toe");
            manager.StartRegrowth(pawn);
            var state = manager.GetRegrowthState(pawn);
            TestAssert.IsNotNull(state, "Missing part must produce a projection");
            TestAssert.IsTrue(state.isActive, "Captured regrowth is active");
            TestAssert.IsTrue(ReferenceEquals(pawn, state.pawn), "Projection retains its owning pawn");
            TestAssert.IsTrue(state.CanUpdatePart(part), "Active captured part is eligible");
            TestAssert.IsFalse(state.CanUpdatePart(null), "Null part is ineligible");
            TestAssert.IsFalse(state.CanUpdatePart(part.parent), "An untracked part is ineligible");
            var withoutHealth = new EternalRegrowthState(null, manager.GetRegrowingHediffs(pawn).ToList());
            TestAssert.IsFalse(withoutHealth.CanUpdatePart(part), "Missing pawn health is ineligible");
            TestAssert.AreEqual(RegrowthPhase.InitialFormation, state.partPhases[part], "Prime the phase cache");
            TestAssert.InRange(state.partProgress[part], 0f, 0.1f, "Prime the progress cache");
            state.ApplyHealing(float.MaxValue);
            TestAssert.IsFalse(pawn.health.hediffSet.PartIsMissing(part), "Delegated healing restores the exact part");
            TestAssert.AreEqual(RegrowthPhase.Complete, state.partPhases[part], "Healing refreshes cached phase");
            TestAssert.AreEqual(1f, state.partProgress[part], "Healing refreshes cached progress");
            TestAssert.IsFalse(state.CanUpdatePart(part), "Completed captured part is ineligible");
            TestAssert.IsTrue(state.IsRegrowthComplete(), "Completed captured batch is complete");
            var empty = new EternalRegrowthState(pawn, new List<EternalRegrowing_Hediff>());
            TestAssert.IsFalse(empty.isActive, "Empty batch is inactive");
            TestAssert.IsTrue(empty.IsRegrowthComplete(), "Empty batch is complete");
            TestAssert.AreEqual(0, empty.partPhases.Count, "Empty batch has no UI rows");
        }

        private static void ChildStartAndCompletionGates(Verse.Map map)
        {
            var manager = Manager();
            var pawn = TestPawnFactory.SpawnNonEternalPawn(map);
            var arm = RemovePart(pawn, "Arm");
            var hand = arm.parts.FirstOrDefault(p => p.def.defName == "Hand");
            TestAssert.Available(hand, "Hand directly below the removed Arm");
            TestAssert.IsTrue(pawn.health.hediffSet.PartIsMissing(hand), "Removing Arm must remove its Hand");
            TestAssert.IsTrue(pawn.health.hediffSet.hediffs.OfType<Hediff_MissingPart>().Any(h => h.Part == hand),
                "Native arm removal must retain an explicit missing record for the exact Hand");
            manager.StartRegrowth(pawn);
            var parent = Regrowth(manager, pawn, arm);
            float childStartThreshold = SettingsDefaults.RegrowthChildStartThreshold;
            parent.Severity = childStartThreshold - 0.001f;
            manager.StartRegrowth(pawn);
            TestAssert.IsFalse(manager.HasRegrowthHediff(pawn, hand), "Child cannot start below the configured release threshold");
            TestAssert.IsTrue(pawn.health.hediffSet.hediffs.OfType<Hediff_MissingPart>().Any(h => h.Part == hand),
                "Blocked child must retain its native missing record before release");
            parent.Severity = childStartThreshold;
            manager.StartRegrowth(pawn);
            var child = Regrowth(manager, pawn, hand);
            TestAssert.IsTrue(manager.GetRegrowthState(pawn).CanUpdatePart(hand), "Released child is eligible in UI");
            child.Severity = 1f;
            manager.ProgressRegrowth(pawn, 0.001f);
            TestAssert.IsTrue(manager.HasRegrowthHediff(pawn, hand), "Child holds at completion while parent is missing");
            TestAssert.IsTrue(pawn.health.hediffSet.PartIsMissing(hand), "Completed child remains missing under its parent");
            TestAssert.IsFalse(manager.GetRegrowthState(pawn).CanUpdatePart(hand), "Held 100% child is ineligible in UI");
            TestAssert.IsFalse(manager.IsRegrowthComplete(pawn), "Parent gate preserves active manager state");
        }

        private static void CapturedBatchCompletesBeforeReleasedChildren(Verse.Map map)
        {
            var manager = Manager();
            var pawn = TestPawnFactory.SpawnNonEternalPawn(map);
            var arm = RemovePart(pawn, "Arm");
            var hand = arm.parts.FirstOrDefault(p => p.def.defName == "Hand");
            TestAssert.Available(hand, "Hand directly below the removed Arm");
            manager.StartRegrowth(pawn);
            var state = manager.GetRegrowthState(pawn);
            TestAssert.IsNotNull(state, "Initial parent regrowth must produce a projection");
            TestAssert.IsFalse(state.IsRegrowthComplete(), "Parent batch starts incomplete");
            TestAssert.IsFalse(manager.HasRegrowthHediff(pawn, hand), "Hand is absent from the captured batch");
            TestAssert.IsFalse(state.partPhases.ContainsKey(hand), "Prime initial batch before children start");
            state.ApplyHealing(float.MaxValue);
            TestAssert.IsFalse(pawn.health.hediffSet.PartIsMissing(arm), "Parent completes through actual delegated healing");
            var child = Regrowth(manager, pawn, hand);
            TestAssert.InRange(child.Severity, 0f, 0.25f, "Newly released child starts in phase one");
            TestAssert.IsTrue(state.IsRegrowthComplete(), "Captured batch completes even with newly released children");
            TestAssert.IsFalse(state.partPhases.ContainsKey(hand), "Refreshing the view preserves captured batch membership");
            TestAssert.IsFalse(manager.IsRegrowthComplete(pawn), "Manager still tracks the newly released child batch");
            TestAssert.IsTrue(manager.GetRegrowthState(pawn).CanUpdatePart(hand), "Fresh UI view includes the new child");
        }
    }
}
#endif
