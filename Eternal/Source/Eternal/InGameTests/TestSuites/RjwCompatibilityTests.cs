// Relative Path: Eternal/Source/Eternal/InGameTests/TestSuites/RjwCompatibilityTests.cs
// Author: 0Shard
// Description: Opt-in host integration checks using real RJW 6.2.1 anatomy and surgery workers.
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Eternal.Compatibility;
using Eternal.DI;
using Eternal.InGameTests.Helpers;
using RimWorld;
using Verse;

namespace Eternal.InGameTests.TestSuites
{
    public static class RjwCompatibilityTests
    {
        private const string Suite = "RjwCompatibility";
        private static readonly List<Pawn> OwnedPawns = new List<Pawn>();
        private static readonly string[] MetadataFields =
        {
            "baseSize", "originalOwnerSize", "originalOwnerRace", "previousOwner",
            "partFluidMultiplier", "isTransplant", "forcedSize", "discovered", "initialised", "fluidOverride"
        };

        public static TestSuiteResult RunAll(Verse.Map map)
        {
            var result = new TestSuiteResult(0, 0, new List<string>());
            RunTest(ref result, "ParentLossPreservesSubtreeAndMultiplicity", () => ParentLoss(map));
            RunTest(ref result, "LivingFourPhaseRestoresExactCohort", () => LivingRegrowth(map, false));
            RunTest(ref result, "NoHistoryUsesExactSlotFallback", () => LivingRegrowth(map, true));
            RunTest(ref result, "LegacyPartRestorerUsesNoHistoryFallback", () => LegacyPartRestorerFallback(map));
            RunTest(ref result, "SuccessfulNaturalReplacementBecomesBiology", () => Replacement(map, false));
            RunTest(ref result, "ImplantSurvivesThenLossRestoresBiology", () => Replacement(map, true));
            RunTest(ref result, "MixedBiologyAndImplantSurviveThenRestoreBiology", () => MixedCohort(map));
            RunTest(ref result, "ParentCompletionWaitsForChildRegrowth", () => ChildRegrowth(map));
            RunTest(ref result, "FailedSurgeryKeepsBiology", () => FailedSurgery(map));
            RunTest(ref result, "ImmediateResurrectionRetainsAnatomyAndPregnancy", () => Resurrection(map, false));
            RunTest(ref result, "NormalResurrectionRetainsAnatomyAndPregnancy", () => Resurrection(map, true));
            RunTest(ref result, "ProtectedHediffsAndMedicalDefaults", () => MedicalPolicy(map));
            RunTest(ref result, "RepairedSavedDependencyRetriesExactEstablishedCohort", () => RepairedDependency(map));
            RunTest(ref result, "MidRegrowthSaveLoad", () =>
                throw new TestUnavailableException("Requires an isolated game save/load; follow docs/rjw-compatibility.md. The host runner cannot safely reload the active game."));
            return result;
        }

        private static void RepairedDependency(Verse.Map map)
        {
            var (pawn, part, expected) = SeededFixture(map, "Penis", "Vagina");
            var tracker = EternalServiceContainer.Instance.RjwAnatomy;
            tracker.CaptureBeforeLoss(pawn, part);
            pawn.health.AddHediff(HediffDefOf.MissingBodyPart, part);
            var slot = tracker.Snapshots.Single(s => s.Pawn == pawn && s.Locator.Matches(pawn, part));
            var saved = slot.Parts[0];
            string originalDef = saved.HediffDefName;
            saved.HediffDefName = "Eternal_Debug_UnresolvedSavedAnatomy";
            try
            {
                TestAssert.IsTrue(EternalServiceContainer.Instance.PartRestorer.TryRestorePart(pawn, part), "Native physical slot must restore");
                tracker.RestoreAvailable(pawn);
                TestAssert.IsTrue(slot.Pending && slot.BlockedReason != null && Anatomy(pawn, part).Count == 0,
                    "Missing dependency must retain exact cohort and diagnostic without partial attachment");
            }
            finally { saved.HediffDefName = originalDef; }
            tracker.RestoreAvailable(pawn);
            TestAssert.IsTrue(!slot.Pending && slot.BlockedReason == null, "Successful exact retry clears historical diagnostic");
            AssertCohort(pawn, part, expected);
            tracker.RestoreAvailable(pawn);
            AssertCohort(pawn, part, expected);
        }

        private static void RunTest(ref TestSuiteResult result, string name, Action test)
        {
            result.RunTest(Suite, name, () =>
            {
                try { test(); }
                finally
                {
                    // Remove only this case's optional-mod records before the shared factory deletes its pawns.
                    foreach (var pawn in OwnedPawns)
                    {
                        EternalServiceContainer.Instance.RjwAnatomy?.Forget(pawn);
                    }

                    OwnedPawns.Clear();
                }
            });
        }

        private static Pawn Own(Pawn pawn)
        {
            OwnedPawns.Add(pawn);
            return pawn;
        }

        private static Pawn Fixture(Verse.Map map)
        {
            RequireRjw();
            var pawn = Own(TestPawnFactory.SpawnEternalPawn(map));
            // Clear only fixture injuries so each case controls the resurrection queue.
            foreach (var injury in pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>().ToList())
            {
                pawn.health.RemoveHediff(injury);
            }

            return pawn;
        }

        private static (Pawn Pawn, BodyPartRecord Slot, string Fingerprint) SeededFixture(Verse.Map map, params string[] definitions)
        {
            var pawn = Fixture(map);
            var slot = Slot(pawn);
            Seed(pawn, slot, definitions);
            return (pawn, slot, Fingerprint(pawn, slot));
        }

        private static void RemoveMissingMarker(Pawn pawn, BodyPartRecord slot)
        {
            var marker = pawn.health.hediffSet.hediffs.OfType<Hediff_MissingPart>().FirstOrDefault(h => h.Part == slot);
            if (marker != null)
            {
                pawn.health.RemoveHediff(marker);
            }
        }

        private static void RunHealingPasses(Pawn pawn)
        {
            for (int i = 0; i < 10; i++)
            {
                EternalServiceContainer.Instance.HealingProcessor.HediffHealer.ProcessHediffHealing(pawn);
            }
        }

        private static void RequireRjw()
        {
            if (!ModsConfig.IsActive("rim.job.world") || RjwType("rjw.SexPartAdder") == null)
            {
                throw new TestUnavailableException("Core RJW must be loaded (validated reference: 6.2.1, rim.job.world).");
            }

            TestAssert.IsNotNull(EternalServiceContainer.Instance.RjwAnatomy, "Session anatomy tracker must be wired");
            TestAssert.IsTrue(EternalServiceContainer.Instance.RjwAnatomy.IsAvailable, "Loaded core RJW bridge must initialize");
        }

        private static BodyPartRecord Slot(Pawn pawn, string defName = "Genitals")
        {
            var slot = pawn.RaceProps.body.AllParts.FirstOrDefault(p => p.def.defName == defName);
            TestAssert.Available(slot, "Core human anatomy slot " + defName);
            return slot;
        }

        private static List<Hediff> Anatomy(Pawn pawn, BodyPartRecord slot)
        {
            var marker = RjwType("rjw.ISexPartHediff");
            TestAssert.Available(marker, "RJW clinical anatomy interface");
            return pawn.health.hediffSet.hediffs.Where(h => h.Part == slot && marker.IsInstanceOfType(h)).ToList();
        }

        private static void Seed(Pawn pawn, BodyPartRecord slot, params string[] definitions)
        {
            foreach (var h in Anatomy(pawn, slot))
            {
                pawn.health.RemoveHediff(h);
            }

            var make = RjwType("rjw.SexPartAdder").GetMethod("MakePart", new[] { typeof(HediffDef), typeof(Pawn), typeof(BodyPartRecord) });
            TestAssert.Available(make, "RJW MakePart clinical helper");
            for (int index = 0; index < definitions.Length; index++)
            {
                var def = DefDatabase<HediffDef>.GetNamedSilentFail(definitions[index]);
                TestAssert.Available(def, "RJW anatomy def " + definitions[index]);
                var hediff = (Hediff)make.Invoke(null, new object[] { def, pawn, slot });
                var comp = SexComp(hediff);
                Field(comp, "baseSize").SetValue(comp, 0.31f + index * 0.13f);
                Field(comp, "originalOwnerSize").SetValue(comp, 1.07f + index * 0.1f);
                Field(comp, "originalOwnerRace").SetValue(comp, "Human");
                Field(comp, "previousOwner").SetValue(comp, "Eternal clinical fixture " + index);
                Field(comp, "partFluidMultiplier").SetValue(comp, 0.83f + index * 0.11f);
                Field(comp, "isTransplant").SetValue(comp, index > 0);
                Field(comp, "forcedSize").SetValue(comp, (float?)(0.47f + index * 0.12f));
                Field(comp, "discovered").SetValue(comp, true);
                comp.GetType().GetMethod("UpdateSeverity").Invoke(comp, new object[] { -1f });
                pawn.health.AddHediff(hediff, slot);
            }
            // Seeding prepares inputs. Capture itself is exercised through subsequent health/surgery hooks.
        }

        private static void ParentLoss(Verse.Map map)
        {
            var pawn = Fixture(map);
            var slots = new[] { Slot(pawn), Slot(pawn, "Chest"), Slot(pawn, "Anus") };
            Seed(pawn, slots[0], "Penis", "Penis", "Vagina");
            Seed(pawn, slots[1], "Breasts");
            Seed(pawn, slots[2], "Anus");
            var expected = slots.Select(s => Fingerprint(pawn, s)).ToArray();
            var parent = slots[0].parent;
            TestAssert.IsNotNull(parent, "Genital slot must have a parent");
            TestAssert.IsTrue(slots.All(s => DescendsFrom(s, parent)), "Core slots must share the tested subtree");
            TestPawnFactory.KillPawn(pawn);
            pawn.health.AddHediff(HediffDefOf.MissingBodyPart, parent);
            TestAssert.IsTrue(slots.All(s => pawn.health.hediffSet.PartIsMissing(s)), "Parent loss must remove descendants");
            TestAssert.IsTrue(slots.All(s => Anatomy(pawn, s).Count == 0), "Anatomy must actually be lost");
            TestAssert.IsTrue(EternalServiceContainer.Instance.PartRestorer.TryRestorePart(pawn, parent), "Actual parent restorer must succeed");
            for (int i = 0; i < slots.Length; i++)
            {
                AssertCohort(pawn, slots[i], expected[i]);
            }
        }

        private static void LivingRegrowth(Verse.Map map, bool noHistory)
        {
            RequireRjw();
            var pawn = noHistory ? Own(TestPawnFactory.SpawnNonEternalPawn(map)) : Fixture(map);
            var slot = Slot(pawn);
            if (!noHistory)
            {
                Seed(pawn, slot, "Penis", "Penis", "Vagina");
            }

            string expected = Fingerprint(pawn, slot);
            if (noHistory)
            {
                // Simulate an old save: anatomy was already lost before Eternal could observe it.
                foreach (var h in Anatomy(pawn, slot))
                {
                    pawn.health.hediffSet.hediffs.Remove(h);
                }

                pawn.health.AddHediff(HediffDefOf.MissingBodyPart, slot);
                pawn.story.traits.GainTrait(new Trait(EternalDefOf.Eternal_GeneticMarker));
                TestAssert.IsFalse(EternalServiceContainer.Instance.RjwAnatomy.Snapshots.Any(s =>
                    s.Pawn == pawn && s.Locator.Matches(pawn, slot) && s.HasBaseline), "Fallback fixture must have no captured biology");
            }
            var unrelated = Fingerprint(pawn, Slot(pawn, "Anus"));
            if (!noHistory)
            {
                pawn.health.AddHediff(HediffDefOf.MissingBodyPart, slot);
            }

            var manager = EternalServiceContainer.Instance.RegrowthManager;
            TestAssert.IsNotNull(manager, "Regrowth service must exist");
            manager.StartRegrowth(pawn);
            var regrowing = pawn.health.hediffSet.hediffs.FirstOrDefault(h => h.def == EternalDefOf.Eternal_Regrowing && h.Part == slot);
            TestAssert.IsNotNull(regrowing, "Actual living regrowth must start");
            // Exercise migrated/externally changed health state where the marker disappears before completion.
            RemoveMissingMarker(pawn, slot);

            TestAssert.IsFalse(pawn.health.hediffSet.PartIsMissing(slot), "Fixture missing marker must be cleared");
            EternalServiceContainer.Instance.RjwAnatomy.RestoreAvailable(pawn);
            TestAssert.AreEqual(0, Anatomy(pawn, slot).Count, "Cleared missing marker must not restore biology during formation");
            float effort = Eternal.Compat.EBFCompat.GetMaxHealth(slot, pawn) * SettingsDefaults.RegrowthWorkPerPartHP;
            // Drive the existing manager at each phase boundary rather than advancing unrelated map pawns.
            foreach (float phase in new[] { 0.26f, 0.51f, 0.76f })
            {
                manager.ProgressRegrowth(pawn, effort * (phase - regrowing.Severity));
                TestAssert.InRange(regrowing.Severity, phase - 0.001f, phase + 0.001f, "Existing four-phase progression");
                EternalServiceContainer.Instance.RjwAnatomy.RestoreAvailable(pawn);
                TestAssert.AreEqual(0, Anatomy(pawn, slot).Count, "Biology waits until part completion");
            }
            manager.ProgressRegrowth(pawn, effort * 1.1f);
            TestAssert.IsFalse(pawn.health.hediffSet.PartIsMissing(slot), "Exact slot must complete");
            if (noHistory)
            {
                TestAssert.IsTrue(Anatomy(pawn, slot).Count > 0, "Absent history must use core race fallback");
            }
            else
            {
                AssertCohort(pawn, slot, expected);
            }

            TestAssert.AreEqual(unrelated, Fingerprint(pawn, Slot(pawn, "Anus")), "Fallback must leave unrelated slots unchanged");
            AssertCache(pawn, slot);
        }

        private static void Replacement(Verse.Map map, bool implant)
        {
            var (pawn, slot, original) = SeededFixture(map, "Penis");
            var recipe = InstallRecipe(slot, implant);
            ApplySurgery(map, pawn, slot, recipe, true);
            var installed = Anatomy(pawn, slot);
            TestAssert.IsTrue(installed.Any(h => h.def == recipe.addsHediff), "Actual surgery must install its target");
            string replacement = Fingerprint(pawn, slot);
            TestAssert.IsFalse(original == replacement, "Fixture surgery must change the cohort");
            EternalServiceContainer.Instance.RjwAnatomy.RestoreAvailable(pawn);
            TestAssert.AreEqual(replacement, Fingerprint(pawn, slot), "Surviving replacement must remain untouched");
            pawn.health.AddHediff(HediffDefOf.MissingBodyPart, slot);
            TestAssert.IsTrue(EternalServiceContainer.Instance.PartRestorer.TryRestorePart(pawn, slot), "Actual part restoration must succeed");
            AssertCohort(pawn, slot, implant ? original : replacement);
        }

        private static void FailedSurgery(Verse.Map map)
        {
            var (pawn, slot, expected) = SeededFixture(map, "Penis", "Vagina");
            var recipe = InstallRecipe(slot, true);
            ApplySurgery(map, pawn, slot, recipe, false);
            TestAssert.IsFalse(Anatomy(pawn, slot).Any(h => h.def == recipe.addsHediff), "Failed surgery must not install target");
            // Failure may damage/remove the slot. The remembered biological cohort must still restore.
            if (pawn.health.hediffSet.PartIsMissing(slot))
            {
                TestAssert.IsTrue(EternalServiceContainer.Instance.PartRestorer.TryRestorePart(pawn, slot), "Restore surgery damage");
            }

            EternalServiceContainer.Instance.RjwAnatomy.RestoreAvailable(pawn);
            AssertCohort(pawn, slot, expected);
        }

        private static void LegacyPartRestorerFallback(Verse.Map map)
        {
            RequireRjw();
            var pawn = Own(TestPawnFactory.SpawnNonEternalPawn(map));
            var slot = Slot(pawn);
            Seed(pawn, slot, "Penis", "Vagina");
            string unrelated = Fingerprint(pawn, Slot(pawn, "Anus"));
            // Old-save input: physical loss occurred before Eternal could capture its biology.
            pawn.health.AddHediff(HediffDefOf.MissingBodyPart, slot);
            TestAssert.IsTrue(pawn.health.hediffSet.PartIsMissing(slot), "Legacy fixture must start physically missing");
            TestAssert.AreEqual(0, Anatomy(pawn, slot).Count, "Actual physical loss must remove seeded biology");
            pawn.story.traits.GainTrait(new Trait(EternalDefOf.Eternal_GeneticMarker));
            TestAssert.IsFalse(EternalServiceContainer.Instance.RjwAnatomy.Snapshots.Any(s =>
                s.Pawn == pawn && s.Locator.Matches(pawn, slot) && s.HasBaseline),
                "Pre-Eternal loss must not acquire an empty biological baseline");
            TestAssert.IsTrue(EternalServiceContainer.Instance.PartRestorer.TryRestorePart(pawn, slot),
                "Actual legacy/corpse part-restorer path must restore the slot");
            TestAssert.IsFalse(pawn.health.hediffSet.PartIsMissing(slot), "Legacy exact slot must become physically available");
            var restored = Anatomy(pawn, slot);
            TestAssert.IsTrue(restored.Count > 0, "Part-restorer capture must preserve missing history and allow natural race fallback");
            var naturalType = RjwType("rjw.Hediff_NaturalSexPart");
            TestAssert.Available(naturalType, "Core natural anatomy type");
            TestAssert.IsTrue(restored.All(h => naturalType.IsInstanceOfType(h) && ReferenceEquals(h.Part, slot)),
                "Legacy fallback must attach natural biology only to the exact restored record");
            TestAssert.AreEqual(unrelated, Fingerprint(pawn, Slot(pawn, "Anus")), "Legacy fallback must preserve unrelated anatomy");
            AssertCache(pawn, slot);
        }

        private static void MixedCohort(Verse.Map map)
        {
            var (pawn, slot, biological) = SeededFixture(map, "Penis", "Penis", "Vagina");
            var recipe = InstallRecipe(slot, true, true);
            ApplySurgery(map, pawn, slot, recipe, true);
            var mixed = Anatomy(pawn, slot);
            TestAssert.AreEqual(4, mixed.Count, "Real additive surgery preserves three natural members and adds one implant");
            TestAssert.AreEqual(1, mixed.Count(h => h is Hediff_AddedPart), "Mixed fixture must have exactly one actual implant");
            string surviving = Fingerprint(pawn, slot);
            EternalServiceContainer.Instance.RjwAnatomy.RestoreAvailable(pawn);
            TestAssert.AreEqual(surviving, Fingerprint(pawn, slot), "Surviving mixed anatomy must remain intact");
            pawn.health.AddHediff(HediffDefOf.MissingBodyPart, slot);
            TestAssert.IsTrue(EternalServiceContainer.Instance.PartRestorer.TryRestorePart(pawn, slot), "Restore lost mixed slot");
            AssertCohort(pawn, slot, biological);
            TestAssert.IsFalse(Anatomy(pawn, slot).Any(h => h is Hediff_AddedPart), "Physical loss must not manufacture a replacement implant");
            AssertCache(pawn, slot);
        }

        private static void ChildRegrowth(Verse.Map map)
        {
            var (pawn, slot, biological) = SeededFixture(map, "Penis", "Vagina");
            var parent = slot.parent;
            TestAssert.IsNotNull(parent, "Core child anatomy must have a parent");
            TestPawnFactory.KillPawn(pawn);
            pawn.health.AddHediff(HediffDefOf.MissingBodyPart, parent);
            var manager = EternalServiceContainer.Instance.RegrowthManager;
            manager.StartRegrowth(pawn);
            var parentGrowth = pawn.health.hediffSet.hediffs.OfType<EternalRegrowing_Hediff>()
                .FirstOrDefault(h => h.forPart == parent);
            TestAssert.IsNotNull(parentGrowth, "Actual parent regrowth must start");
            float parentEffort = Eternal.Compat.EBFCompat.GetMaxHealth(parent, pawn) * SettingsDefaults.RegrowthWorkPerPartHP;
            manager.ProgressRegrowth(pawn, parentEffort * 0.76f);
            var childGrowth = pawn.health.hediffSet.hediffs.OfType<EternalRegrowing_Hediff>()
                .FirstOrDefault(h => h.forPart == slot);
            TestAssert.IsNotNull(childGrowth, "Actual parent progression must release the exact anatomy child");
            // Stage a parent at completion while its child is still developing; no mock completion callback.
            childGrowth.Severity = 0.4f;
            RemoveMissingMarker(pawn, slot);

            parentGrowth.Severity = 1f;
            manager.RemoveCompletedRegrowth(pawn);
            TestAssert.IsFalse(manager.HasRegrowthHediff(pawn, parent), "Actual completion must finish the parent");
            TestAssert.IsTrue(manager.HasRegrowthHediff(pawn, slot), "Child must remain in development after parent completion");
            TestAssert.AreEqual(0, Anatomy(pawn, slot).Count, "Parent restoration must not restore partial child anatomy");
            childGrowth.Severity = 1f;
            manager.RemoveCompletedRegrowth(pawn);
            AssertCohort(pawn, slot, biological);
        }

        private static RecipeDef InstallRecipe(BodyPartRecord slot, bool artificial, bool additive = false)
        {
            var type = RjwType("rjw.Recipe_InstallPart");
            var recipe = DefDatabase<RecipeDef>.AllDefsListForReading.FirstOrDefault(r =>
                r.workerClass != null && type.IsAssignableFrom(r.workerClass) &&
                ((r.workerClass.FullName == "rjw.Recipe_AddMultiPart") == additive) &&
                r.addsHediff != null && r.addsHediff.defName != "Penis" &&
                r.appliedOnFixedBodyParts?.Contains(slot.def) == true &&
                (typeof(Hediff_AddedPart).IsAssignableFrom(r.addsHediff.hediffClass) == artificial));
            TestAssert.Available(recipe, "Core RJW " + (artificial ? "implant" : "natural replacement") + " recipe");
            return recipe;
        }

        private static void ApplySurgery(Verse.Map map, Pawn patient, BodyPartRecord slot, RecipeDef recipe, bool success)
        {
            var surgeon = Own(TestPawnFactory.SpawnNonEternalPawn(map));
            var ingredientDef = DefDatabase<ThingDef>.GetNamedSilentFail(recipe.addsHediff.defName);
            TestAssert.Available(ingredientDef, "Surgery anatomy item");
            var ingredient = ThingMaker.MakeThing(ingredientDef);
            TestPawnFactory.OwnThing(ingredient);
            var ingredients = new List<Thing> { ingredient };
            var existing = new HashSet<Thing>(map.listerThings.AllThings);
            float savedFactor = recipe.surgerySuccessChanceFactor;
            Rand.PushState(34157);
            try
            {
                recipe.surgerySuccessChanceFactor = success ? 99999f : 0f;
                recipe.Worker.ApplyOnPawn(patient, slot, surgeon, ingredients, new Bill_Medical(recipe, null));
            }
            finally
            {
                recipe.surgerySuccessChanceFactor = savedFactor;
                Rand.PopState();
                // RJW surgery can spawn removed anatomy items. Own only new item Things, never other pawns.
                foreach (var thing in map.listerThings.AllThings.Where(t => !existing.Contains(t) && t.def.category == ThingCategory.Item).ToList())
                {
                    TestPawnFactory.OwnThing(thing);
                }
            }
        }

        private static void Resurrection(Verse.Map map, bool normal)
        {
            var (pawn, slot, expected) = SeededFixture(map, "Penis", "Vagina");
            var pregnancyDef = DefDatabase<HediffDef>.GetNamedSilentFail(ModsConfig.BiotechActive ? "PregnantHuman" : "Pregnant");
            TestAssert.Available(pregnancyDef, "Vanilla normal pregnancy");
            var pregnancy = pawn.health.AddHediff(pregnancyDef);
            pregnancy.Severity = 0.2f;
            AssertCache(pawn, slot); // Populate the RJW cache before the set swap/loss.
            if (normal)
            {
                pawn.health.AddHediff(HediffDefOf.MissingBodyPart, slot);
            }

            TestPawnFactory.KillPawn(pawn);
            var services = EternalServiceContainer.Instance;
            var data = services.CorpseManager.GetCorpseData(pawn);
            TestAssert.IsNotNull(data, "Actual death registration");
            TestAssert.IsTrue(services.CorpseHealingProcessor.StartCorpseHealing(data), "Actual healing start");
            if (normal)
            {
                TickAdvancer.AdvanceUntil(() => !pawn.Dead, 100000, "RJW normal resurrection timed out");
            }
            else
            {
                TestAssert.IsFalse(pawn.Dead, "Empty queue must use immediate resurrection");
            }

            TestAssert.IsFalse(pawn.Dead, "Pawn must resurrect");
            AssertCohort(pawn, slot, expected);
            TestAssert.IsTrue(pawn.health.hediffSet.hediffs.Contains(pregnancy), "Same normal pregnancy instance must survive swap");
            TestAssert.IsTrue(RjwHealthPolicy.IsProtected(pregnancy.def), "Normal pregnancy must be protected with RJW loaded");
            AssertCache(pawn, slot);
        }

        private static void MedicalPolicy(Verse.Map map)
        {
            var (pawn, slot, expected) = SeededFixture(map, "Penis");
            var anatomy = Anatomy(pawn, slot)[0];
            var forceEnabled = new EternalHediffSetting(anatomy.def) { canHeal = true, noThreshold = true };
            TestAssert.IsFalse(RjwHealthPolicy.EffectiveCanHeal(anatomy.def, forceEnabled), "Anatomy remains protected against user force-enable");
            using (new HealingChoiceScope(anatomy.def, true))
            {
                RunHealingPasses(pawn);

                AssertCohort(pawn, slot, expected);
            }
            foreach (var pair in new[] { ("Sterilized", true), ("DecreasedFertility", true), ("RJW_IUD", false),
                ("ImpregnationBlocker", false), ("IncreasedFertility", false), ("FertilityEnhancer", false) })
            {
                var def = DefDatabase<HediffDef>.GetNamedSilentFail(pair.Item1);
                TestAssert.Available(def, "Core medical def " + pair.Item1);
                var setting = new EternalHediffSetting(def);
                TestAssert.AreEqual(pair.Item2, setting.canHeal, "New-entry medical default " + pair.Item1);
                setting.canHeal = !pair.Item2;
                setting.InitializeFromHediffDef(def);
                TestAssert.AreEqual(!pair.Item2, setting.canHeal, "Loading metadata must preserve saved explicit choice");
                TestAssert.AreEqual(!pair.Item2, RjwHealthPolicy.EffectiveCanHeal(def, setting), "Explicit choice must govern policy");
                setting.ResetToDefaults();
                TestAssert.AreEqual(pair.Item2, setting.canHeal, "Reset medical default " + pair.Item1);
                var medical = pawn.health.AddHediff(def);
                using (new HealingChoiceScope(def, false))
                {
                    float severity = medical.Severity;
                    RunHealingPasses(pawn);

                    TestAssert.IsTrue(pawn.health.hediffSet.hediffs.Contains(medical), "Disabled medical condition remains attached");
                    TestAssert.AreEqual(severity, medical.Severity, "Disabled medical severity remains unchanged");
                }
                pawn.health.RemoveHediff(medical);
            }
            var pregnancy = DefDatabase<HediffDef>.GetNamedSilentFail("RJW_pregnancy");
            TestAssert.Available(pregnancy, "Core RJW pregnancy def");
            TestAssert.IsTrue(RjwHealthPolicy.IsProtected(pregnancy), "RJW pregnancy lifecycle must be protected");
        }

        private sealed class HealingChoiceScope : IDisposable
        {
            private readonly EternalHediffSetting setting;
            private readonly bool canHeal;
            private readonly bool noThreshold;
            private readonly float rate;

            internal HealingChoiceScope(HediffDef def, bool choice)
            {
                setting = Eternal_Mod.GetSettings().hediffManager.GetHediffSetting(def);
                TestAssert.IsNotNull(setting, "Loaded host hediff setting " + def.defName);
                canHeal = setting.canHeal;
                noThreshold = setting.noThreshold;
                rate = setting.healingRate;
                setting.canHeal = choice;
                setting.noThreshold = true;
                setting.healingRate = 0.1f;
            }

            public void Dispose()
            {
                setting.canHeal = canHeal;
                setting.noThreshold = noThreshold;
                setting.healingRate = rate;
            }
        }

        private static string Fingerprint(Pawn pawn, BodyPartRecord slot)
        {
            return string.Join("\n", Anatomy(pawn, slot).Select(h => h.def.defName + ":" + h.Severity.ToString("R", CultureInfo.InvariantCulture) + ":" +
                string.Join(";", MetadataFields.Select(name => name + "=" + Value(Field(SexComp(h), name).GetValue(SexComp(h))))))
                .OrderBy(s => s, StringComparer.Ordinal));
        }

        private static void AssertCohort(Pawn pawn, BodyPartRecord slot, string expected)
        {
            TestAssert.AreEqual(expected, Fingerprint(pawn, slot), "Exact definitions, duplicates, severity and saved metadata in " + slot.def.defName);
            TestAssert.IsTrue(Anatomy(pawn, slot).All(h => ReferenceEquals(h.Part, slot)), "Anatomy must attach to the exact record");
        }

        private static void AssertCache(Pawn pawn, BodyPartRecord slot)
        {
            var method = RjwType("rjw.PawnExtensions").GetMethod("GetGenitalsList", new[] { typeof(Pawn) });
            TestAssert.Available(method, "RJW cached anatomy accessor");
            var cached = ((IEnumerable)method.Invoke(null, new object[] { pawn })).Cast<Hediff>().ToList();
            var actual = Anatomy(pawn, slot);
            TestAssert.AreEqual(actual.Count, cached.Count, "RJW cache cardinality after actual restoration/swap");
            TestAssert.IsTrue(cached.All(actual.Contains), "RJW cache must reference current HediffSet members");
        }

        private static object SexComp(Hediff hediff)
        {
            var comp = (hediff as HediffWithComps)?.comps?.FirstOrDefault(c => c.GetType().FullName == "rjw.HediffComp_SexPart");
            TestAssert.IsNotNull(comp, "Real RJW anatomy metadata component");
            return comp;
        }

        private static FieldInfo Field(object owner, string name)
        {
            var field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            TestAssert.IsNotNull(field, "Pinned RJW saved metadata field " + name);
            return field;
        }

        private static string Value(object value) => value is Def def ? def.defName :
            value is IFormattable number ? number.ToString(null, CultureInfo.InvariantCulture) : value?.ToString() ?? "<null>";

        private static Type RjwType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null);

        private static bool DescendsFrom(BodyPartRecord part, BodyPartRecord ancestor)
        {
            for (var current = part; current != null; current = current.parent)
            {
                if (ReferenceEquals(current, ancestor))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
#endif
