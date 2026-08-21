// Relative Path: Eternal/Source/Eternal/InGameTests/TestSuites/ReactiveHealthTests.cs
// Creation Date: 16-07-2026
// Last Edit: 16-07-2026
// Author: 0Shard
// Description: In-game Gate 2 integration checks for deferred live-health reconciliation.
//              Covers mutation deferral, legacy duplicate-key reset, and current-set pruning
//              without changing the Health tab's real Hediff instances.

#if DEBUG

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;
using Eternal.Components;
using Eternal.DI;
using Eternal.Extensions;
using Eternal.Healing;
using Eternal.Infrastructure;
using Eternal.InGameTests.Helpers;

namespace Eternal.InGameTests.TestSuites
{
    public static class ReactiveHealthTests
    {
        public static TestSuiteResult RunAll(Verse.Map map)
        {
            int passed = 0;
            int failed = 0;
            var failures = new List<string>();

            RunTest("AddMutationDefersThresholdRegistration", () =>
                AddMutationDefersThresholdRegistration(map), ref passed, ref failed, failures);
            RunTest("NonEternalHealthMutationsAreNotQueued", () =>
                NonEternalHealthMutationsAreNotQueued(map), ref passed, ref failed, failures);
            RunTest("NonEternalReconciliationClearsDerivedState", () =>
                NonEternalReconciliationClearsDerivedState(map), ref passed, ref failed, failures);
            RunTest("AmbiguousLegacyRowsResetForDuplicateHediffs", () =>
                AmbiguousLegacyRowsResetForDuplicateHediffs(map), ref passed, ref failed, failures);
            RunTest("DirectHediffSetRemovalIsPruned", () =>
                DirectHediffSetRemovalIsPruned(map), ref passed, ref failed, failures);

            return new TestSuiteResult(passed, failed, failures);
        }

        private static void AddMutationDefersThresholdRegistration(Verse.Map map)
        {
            var pawn = TestPawnFactory.SpawnEternalPawn(map);
            var definition = DefDatabase<HediffDef>.GetNamedSilentFail("Flu");
            TestAssert.IsNotNull(definition, "Flu definition should exist");

            var setting = Eternal_Mod.GetSettings().hediffManager.GetHediffSetting(definition.defName);
            bool originalCanHeal = setting.canHeal;
            bool originalNoThreshold = setting.noThreshold;
            setting.canHeal = true;
            setting.noThreshold = false;

            var hediff = HediffMaker.MakeHediff(definition, pawn);
            hediff.Severity = 0.2f;
            var tracker = EternalServiceContainer.Instance.ThresholdTracker;
            tracker.RemoveThreshold(pawn, hediff);

            pawn.health.AddHediff(hediff);
            TestAssert.IsFalse(
                tracker.TryGetThreshold(pawn, hediff, out _, out _),
                "AddHediff callback must enqueue only; it must not register threshold inline");

            float severityBeforeSafeTick = hediff.Severity;
            TickAdvancer.AdvanceTicks(1);
            TestAssert.IsTrue(
                tracker.TryGetThreshold(pawn, hediff, out _, out _),
                "The next safe tick must reconcile the current hediff");
            TestAssert.IsTrue(
                hediff.Severity <= severityBeforeSafeTick,
                "Only a scheduled healing pass may change severity after the callback returns");

            setting.canHeal = originalCanHeal;
            setting.noThreshold = originalNoThreshold;
            RemoveAll(pawn, definition);
        }

        private static void NonEternalHealthMutationsAreNotQueued(Verse.Map map)
        {
            var pawn = TestPawnFactory.SpawnNonEternalPawn(map);
            var existingEssence = pawn.health?.hediffSet?.GetFirstHediffOfDef(EternalDefOf.Eternal_Essence);
            if (existingEssence != null)
                pawn.health.RemoveHediff(existingEssence);

            var pendingPawns = GetPendingHealthPawns();
            pendingPawns.Clear();

            var fluDefinition = DefDatabase<HediffDef>.GetNamedSilentFail("Flu");
            TestAssert.IsNotNull(fluDefinition, "Flu definition should exist");
            var flu = HediffMaker.MakeHediff(fluDefinition, pawn);
            pawn.health.AddHediff(flu);
            TestAssert.AreEqual(
                0,
                pendingPawns.Count,
                "Adding a non-Eternal hediff must not enqueue its pawn");

            pawn.health.RemoveHediff(flu);
            TestAssert.AreEqual(
                0,
                pendingPawns.Count,
                "Removing a non-Eternal hediff must not enqueue its pawn");

            var part = pawn.RaceProps?.body?.AllParts?.FirstOrDefault();
            TestAssert.IsNotNull(part, "Non-Eternal test pawn should have a body part");
            var missingPart = pawn.health.AddHediff(HediffDefOf.MissingBodyPart, part);
            TestAssert.AreEqual(
                0,
                pendingPawns.Count,
                "Adding a missing part to a non-Eternal pawn must not enqueue it");

            if (missingPart != null)
                pawn.health.RemoveHediff(missingPart);
            TestAssert.AreEqual(
                0,
                pendingPawns.Count,
                "Removing a missing part from a non-Eternal pawn must not enqueue it");
        }

        private static void NonEternalReconciliationClearsDerivedState(Verse.Map map)
        {
            var pawn = TestPawnFactory.SpawnEternalPawn(map);
            var fluDefinition = DefDatabase<HediffDef>.GetNamedSilentFail("Flu");
            TestAssert.IsNotNull(fluDefinition, "Flu definition should exist");

            var setting = Eternal_Mod.GetSettings().hediffManager.GetHediffSetting(fluDefinition.defName);
            bool originalCanHeal = setting.canHeal;
            bool originalNoThreshold = setting.noThreshold;
            setting.canHeal = true;
            setting.noThreshold = false;

            var flu = AddDirect(fluDefinition, pawn, null, 0.2f);
            var container = EternalServiceContainer.Instance;
            var thresholdTracker = container.ThresholdTracker;
            var hediffHealer = container.HealingProcessor.HediffHealer;
            var severityTracker = GetSeverityTracker();

            Eternal_Component.Current.ReconcileLiveHealth(pawn);
            TestAssert.IsTrue(
                thresholdTracker.TryGetThreshold(pawn, flu, out _, out _),
                "The Eternal pawn should have derived threshold state before trait loss");

            InjectProgress(hediffHealer, pawn, flu, 1f);
            severityTracker.RecordHealingAttempt(pawn, flu, 0.2f, 0.19f, 0.01f);
            InjectLegacyProgress(hediffHealer, pawn, flu, 2f);
            InjectLegacyThreshold(thresholdTracker, pawn, flu, 0.99f);
            InjectLegacyHistory(severityTracker, pawn, flu);

            var essence = pawn.health.hediffSet.GetFirstHediffOfDef(EternalDefOf.Eternal_Essence);
            TestAssert.IsNotNull(essence, "Eternal pawn should have Essence before trait loss");
            pawn.health.RemoveHediff(essence);
            var eternalTrait = pawn.story?.traits?.GetTrait(EternalDefOf.Eternal_GeneticMarker);
            if (eternalTrait != null)
                pawn.story.traits.RemoveTrait(eternalTrait);

            Eternal_Component.Current.ReconcileLiveHealth(pawn);

            TestAssert.IsFalse(pawn.IsValidEternal(), "Pawn should no longer be Eternal after trait loss");
            TestAssert.AreEqual(
                0,
                thresholdTracker.TrackedCount,
                "Invalid live pawn must lose threshold and legacy threshold state");
            TestAssert.AreEqual(
                0,
                severityTracker.TrackedCount,
                "Invalid live pawn must lose severity history, including legacy rows");
            TestAssert.AreEqual(
                0,
                CountDictionary(hediffHealer, "healingProgress"),
                "Invalid live pawn must lose healing progress");
            TestAssert.AreEqual(
                0,
                CountDictionary(hediffHealer, "legacyHealingProgress"),
                "Invalid live pawn must lose legacy healing progress");

            setting.canHeal = originalCanHeal;
            setting.noThreshold = originalNoThreshold;
            RemoveAll(pawn, fluDefinition);
        }

        private static void AmbiguousLegacyRowsResetForDuplicateHediffs(Verse.Map map)
        {
            var pawn = TestPawnFactory.SpawnEternalPawn(map);
            var definition = DefDatabase<HediffDef>.GetNamedSilentFail("Infection");
            TestAssert.IsNotNull(definition, "Infection definition should exist");
            var part = pawn.RaceProps?.body?.AllParts?.FirstOrDefault();
            TestAssert.IsNotNull(part, "Test pawn should have a body part");

            var setting = Eternal_Mod.GetSettings().hediffManager.GetHediffSetting(definition.defName);
            bool originalCanHeal = setting.canHeal;
            bool originalNoThreshold = setting.noThreshold;
            setting.canHeal = true;
            setting.noThreshold = false;

            var first = AddDirect(definition, pawn, part, 0.2f);
            var second = AddDirect(definition, pawn, part, 0.3f);
            var tracker = EternalServiceContainer.Instance.ThresholdTracker;
            tracker.ClearPawnThresholds(pawn);

            InjectLegacyThreshold(tracker, pawn, first, 0.99f);
            var severityTracker = GetSeverityTracker();
            severityTracker.ClearPawnTracking(pawn);
            InjectLegacyHistory(severityTracker, pawn, first);

            Eternal_Component.Current.ReconcileLiveHealth(pawn);

            TestAssert.IsTrue(
                tracker.TryGetThreshold(pawn, first, out _, out _),
                "First duplicate must receive freshly registered threshold state");
            TestAssert.IsTrue(
                tracker.TryGetThreshold(pawn, second, out _, out _),
                "Second duplicate must receive freshly registered threshold state");
            TestAssert.AreEqual(
                2,
                tracker.TrackedCount,
                "Ambiguous legacy threshold row must not be assigned to either duplicate");
            TestAssert.AreEqual(
                0,
                severityTracker.TrackedCount,
                "Ambiguous legacy severity history must be discarded rather than inherited");

            setting.canHeal = originalCanHeal;
            setting.noThreshold = originalNoThreshold;
            RemoveAll(pawn, definition);
        }

        private static void DirectHediffSetRemovalIsPruned(Verse.Map map)
        {
            var pawn = TestPawnFactory.SpawnEternalPawn(map);
            var definition = DefDatabase<HediffDef>.GetNamedSilentFail("Flu");
            TestAssert.IsNotNull(definition, "Flu definition should exist");

            var setting = Eternal_Mod.GetSettings().hediffManager.GetHediffSetting(definition.defName);
            bool originalCanHeal = setting.canHeal;
            setting.canHeal = true;

            var hediff = AddDirect(definition, pawn, null, 0.2f);
            Eternal_Component.Current.ReconcileLiveHealth(pawn);
            TestAssert.IsTrue(
                EternalServiceContainer.Instance.ThresholdTracker.TryGetThreshold(pawn, hediff, out _, out _),
                "Current hediff must be registered before direct removal");

            pawn.health.hediffSet.hediffs.Remove(hediff);
            Eternal_Component.Current.ReconcileLiveHealth(pawn);
            TestAssert.IsFalse(
                EternalServiceContainer.Instance.ThresholdTracker.TryGetThreshold(pawn, hediff, out _, out _),
                "Reconciliation must prune state for a hediff absent from the current set");

            setting.canHeal = originalCanHeal;
        }

        private static Hediff AddDirect(HediffDef definition, Pawn pawn, BodyPartRecord part, float severity)
        {
            var hediff = HediffMaker.MakeHediff(definition, pawn, part);
            hediff.Severity = severity;
            pawn.health.hediffSet.AddDirect(hediff);
            return hediff;
        }

        private static HashSet<Pawn> GetPendingHealthPawns()
        {
            var orchestratorField = typeof(Eternal_Component).GetField(
                "tickOrchestrator",
                BindingFlags.Instance | BindingFlags.NonPublic);
            TestAssert.IsNotNull(orchestratorField, "Component should own a TickOrchestrator");
            var orchestrator = (TickOrchestrator)orchestratorField.GetValue(Eternal_Component.Current);
            var pendingField = typeof(TickOrchestrator).GetField(
                "pendingHealthReconciliation",
                BindingFlags.Instance | BindingFlags.NonPublic);
            TestAssert.IsNotNull(pendingField, "Orchestrator should own pending health state");
            return (HashSet<Pawn>)pendingField.GetValue(orchestrator);
        }

        private static int CountDictionary(object owner, string fieldName)
        {
            var field = owner.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            TestAssert.IsNotNull(field, $"Expected state field {fieldName}");
            return ((IDictionary)field.GetValue(owner)).Count;
        }

        private static void InjectProgress(
            EternalHediffHealer healer,
            Pawn pawn,
            Hediff hediff,
            float progress)
        {
            var field = typeof(EternalHediffHealer).GetField(
                "healingProgress",
                BindingFlags.Instance | BindingFlags.NonPublic);
            TestAssert.IsNotNull(field, "Healing progress storage should exist");
            var dictionary = (IDictionary)field.GetValue(healer);
            dictionary.Clear();
            dictionary.Add(new HealingDictionaryKey(pawn, hediff), progress);
        }

        private static void InjectLegacyProgress(
            EternalHediffHealer healer,
            Pawn pawn,
            Hediff hediff,
            float progress)
        {
            var field = typeof(EternalHediffHealer).GetField(
                "legacyHealingProgress",
                BindingFlags.Instance | BindingFlags.NonPublic);
            TestAssert.IsNotNull(field, "Legacy healing progress storage should exist");
            var dictionary = (IDictionary)field.GetValue(healer);
            dictionary.Clear();

            Type[] genericArguments = field.FieldType.GetGenericArguments();
            Type keyType = genericArguments[0];
            Type listType = genericArguments[1];
            object key = CreatePrivate(keyType, pawn.thingIDNumber, hediff.def.defName, hediff.Part?.Label);
            var values = (IList)Activator.CreateInstance(listType);
            values.Add(progress);
            dictionary.Add(key, values);
        }

        private static EternalHediffSeverityTracker GetSeverityTracker()
        {
            var field = typeof(EternalHediffHealer).GetField(
                "severityTracker",
                BindingFlags.Instance | BindingFlags.NonPublic);
            TestAssert.IsNotNull(field, "HediffHealer severity tracker field should exist");
            return (EternalHediffSeverityTracker)field.GetValue(
                EternalServiceContainer.Instance.HealingProcessor.HediffHealer);
        }

        private static void InjectLegacyThreshold(
            HediffHealingThresholdTracker tracker,
            Pawn pawn,
            Hediff hediff,
            float threshold)
        {
            var field = typeof(HediffHealingThresholdTracker).GetField(
                "_legacyThresholds",
                BindingFlags.Instance | BindingFlags.NonPublic);
            TestAssert.IsNotNull(field, "Legacy threshold storage should exist for migration test");
            var dictionary = (IDictionary)field.GetValue(tracker);
            dictionary.Clear();

            Type[] genericArguments = field.FieldType.GetGenericArguments();
            Type keyType = genericArguments[0];
            Type listType = genericArguments[1];
            Type entryType = listType.GetGenericArguments()[0];
            object key = CreatePrivate(keyType, pawn.thingIDNumber, hediff.def.defName, hediff.Part?.Label);
            object entry = CreatePrivate(entryType, threshold);
            var entries = (IList)Activator.CreateInstance(listType);
            entries.Add(entry);
            dictionary.Add(key, entries);
        }

        private static void InjectLegacyHistory(
            EternalHediffSeverityTracker tracker,
            Pawn pawn,
            Hediff hediff)
        {
            var field = typeof(EternalHediffSeverityTracker).GetField(
                "legacyHistory",
                BindingFlags.Instance | BindingFlags.NonPublic);
            TestAssert.IsNotNull(field, "Legacy history storage should exist for migration test");
            var dictionary = (IDictionary)field.GetValue(tracker);
            dictionary.Clear();

            Type[] genericArguments = field.FieldType.GetGenericArguments();
            Type keyType = genericArguments[0];
            Type listType = genericArguments[1];
            Type attemptType = listType.GetGenericArguments()[0];
            object key = CreatePrivate(keyType, pawn.thingIDNumber, hediff.def.defName, hediff.Part?.Label);
            object attempt = CreatePrivate(attemptType);
            attemptType.GetField("SeverityBefore", BindingFlags.Instance | BindingFlags.Public)
                .SetValue(attempt, 0.2f);
            attemptType.GetField("SeverityAfter", BindingFlags.Instance | BindingFlags.Public)
                .SetValue(attempt, 0.2f);
            attemptType.GetField("HealingApplied", BindingFlags.Instance | BindingFlags.Public)
                .SetValue(attempt, 0.01f);
            attemptType.GetField("TickRecorded", BindingFlags.Instance | BindingFlags.Public)
                .SetValue(attempt, 1);
            var attempts = (IList)Activator.CreateInstance(listType);
            attempts.Add(attempt);
            dictionary.Add(key, attempts);
        }

        private static object CreatePrivate(Type type, params object[] arguments)
        {
            return Activator.CreateInstance(
                type,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                arguments,
                CultureInfo.InvariantCulture);
        }

        private static void RemoveAll(Pawn pawn, HediffDef definition)
        {
            var leftovers = pawn.health?.hediffSet?.hediffs
                ?.Where(hediff => hediff?.def == definition)
                .ToList();
            if (leftovers == null)
                return;

            foreach (var hediff in leftovers)
                pawn.health.RemoveHediff(hediff);
        }

        private static void RunTest(
            string name,
            Action test,
            ref int passed,
            ref int failed,
            List<string> failures)
        {
            Log.Message($"[EternalTests] Running: {name}");
            try
            {
                test();
                passed++;
                Log.Message($"[EternalTests] PASSED: {name}");
            }
            catch (TestFailedException ex)
            {
                failed++;
                failures.Add($"[ReactiveHealth] {name}: {ex.Message}");
                Log.Error($"[EternalTests] FAILED: {name} -- {ex.Message}");
            }
            catch (Exception ex)
            {
                failed++;
                failures.Add($"[ReactiveHealth] {name}: {ex.Message}");
                Log.Error($"[EternalTests] ERROR: {name} -- {ex}");
            }
            finally
            {
                TestPawnFactory.CleanupAll();
            }
        }
    }
}

#endif
