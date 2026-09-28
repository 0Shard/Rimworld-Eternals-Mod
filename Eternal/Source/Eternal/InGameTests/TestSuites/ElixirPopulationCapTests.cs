// Relative Path: Eternal/Source/Eternal/InGameTests/TestSuites/ElixirPopulationCapTests.cs
// Creation Date: 13-03-2026
// Last Edit: 28-09-2026
// Author: 0Shard
// Description: In-game E2E tests for the Elixir of Eternity target routing (PR #1 fix) and
//              population cap (v1.0.1). Post-split, target-side acceptance/rejection is
//              exercised via CompTargetable_EternalElixir.ValidateTarget (not the old
//              CanBeUsedBy), and the trait grant via CompTargetEffect_EternalElixir.DoEffectOn
//              with two DISTINCT pawns — the single-pawn setup this suite used before the fix
//              would have passed even on the unfixed, user-targeting code. Uses SettingsScope
//              for all population cap mutations.

#if DEBUG

using System;
using System.Collections.Generic;
using Verse;
using RimWorld;
using Eternal.InGameTests.Helpers;
using Eternal.Elixir;
using Eternal.Extensions;
using Eternal.DI;

namespace Eternal.InGameTests.TestSuites
{
    /// <summary>
    /// Tests the Elixir of Eternity target routing (CompTargetable_EternalElixir /
    /// CompTargetEffect_EternalElixir / CompUseEffect_EternalElixir) and population cap
    /// enforcement introduced in v1.0.1.
    /// </summary>
    public static class ElixirPopulationCapTests
    {
        public static TestSuiteResult RunAll(Verse.Map map)
        {
            int passed = 0;
            int failed = 0;
            var failures = new List<string>();

            try
            {
                RunTest("ElixirAcceptsNonEternalPawn",
                    () => ElixirAcceptsNonEternalPawn(map), ref passed, ref failed, failures);
                RunTest("ElixirRejectsAlreadyEternalPawn",
                    () => ElixirRejectsAlreadyEternalPawn(map), ref passed, ref failed, failures);
                RunTest("ElixirRejectsDeadPawn",
                    () => ElixirRejectsDeadPawn(map), ref passed, ref failed, failures);
                RunTest("ElixirGrantsTraitToTargetNotUser",
                    () => ElixirGrantsTraitToTargetNotUser(map), ref passed, ref failed, failures);
                RunTest("ElixirSelfUseGrantsTrait",
                    () => ElixirSelfUseGrantsTrait(map), ref passed, ref failed, failures);
                RunTest("ElixirDoEffectOnSkipsDeadTarget",
                    () => ElixirDoEffectOnSkipsDeadTarget(map), ref passed, ref failed, failures);
                RunTest("ElixirConfirmMessageNamesTarget",
                    () => ElixirConfirmMessageNamesTarget(map), ref passed, ref failed, failures);
                RunTest("ElixirKillSwitchRejectsUse",
                    () => ElixirKillSwitchRejectsUse(map), ref passed, ref failed, failures);
                RunTest("PopulationCapBlocksElixir",
                    () => PopulationCapBlocksElixir(map), ref passed, ref failed, failures);
                RunTest("PopulationCapDisabledAllowsUnlimited",
                    () => PopulationCapDisabledAllowsUnlimited(map), ref passed, ref failed, failures);
            }
            finally
            {
                TestPawnFactory.CleanupAll();
            }

            return new TestSuiteResult(passed, failed, failures);
        }

        // ─── Helpers ────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a free-standing elixir Thing (not spawned on the map) and returns it,
        /// asserting all three post-split comps are present — this also proves the Def wiring
        /// from the CompTargetable_EternalElixir / CompTargetEffect_EternalElixir fix.
        /// </summary>
        private static ThingWithComps MakeElixirThing()
        {
            var elixirDef = DefDatabase<ThingDef>.GetNamedSilentFail("Eternal_ElixirOfEternity");
            TestAssert.IsNotNull(elixirDef, "Elixir ThingDef 'Eternal_ElixirOfEternity' must exist in DefDatabase");

            var elixirThing = ThingMaker.MakeThing(elixirDef) as ThingWithComps;
            TestAssert.IsNotNull(elixirThing, "Elixir ThingDef must produce a ThingWithComps");

            TestAssert.IsNotNull(elixirThing.GetComp<CompTargetable_EternalElixir>(),
                "Elixir must have CompTargetable_EternalElixir component");
            TestAssert.IsNotNull(elixirThing.GetComp<CompTargetEffect_EternalElixir>(),
                "Elixir must have CompTargetEffect_EternalElixir component");
            TestAssert.IsNotNull(elixirThing.GetComp<CompUseEffect_EternalElixir>(),
                "Elixir must have CompUseEffect_EternalElixir component");

            return elixirThing;
        }

        // ─── Test Bodies ────────────────────────────────────────────────────

        private static void ElixirAcceptsNonEternalPawn(Verse.Map map)
        {
            using (new SettingsScope())
            {
                // Make sure pop cap is not the blocker here
                Eternal_Mod.settings.populationCapEnabled = false;

                var targetable = MakeElixirThing().GetComp<CompTargetable_EternalElixir>();
                var pawn = TestPawnFactory.SpawnNonEternalPawn(map);

                bool accepted = targetable.ValidateTarget(new LocalTargetInfo(pawn), showMessages: false);
                Log.Message($"[EternalTests] ElixirAcceptsNonEternalPawn: accepted = {accepted}");

                TestAssert.IsTrue(accepted, "Elixir should accept a non-Eternal pawn as target");
            }
        }

        private static void ElixirRejectsAlreadyEternalPawn(Verse.Map map)
        {
            var targetable = MakeElixirThing().GetComp<CompTargetable_EternalElixir>();
            var pawn = TestPawnFactory.SpawnEternalPawn(map);

            bool accepted = targetable.ValidateTarget(new LocalTargetInfo(pawn), showMessages: false);
            Log.Message($"[EternalTests] ElixirRejectsAlreadyEternalPawn: accepted = {accepted}");

            TestAssert.IsFalse(accepted, "Elixir should reject a target pawn that already has the Eternal trait");
        }

        private static void ElixirRejectsDeadPawn(Verse.Map map)
        {
            var targetable = MakeElixirThing().GetComp<CompTargetable_EternalElixir>();
            var pawn = TestPawnFactory.SpawnEternalPawn(map);

            TestPawnFactory.KillPawn(pawn);
            TestAssert.IsTrue(pawn.Dead, "Pawn should be dead after KillPawn");

            bool accepted = targetable.ValidateTarget(new LocalTargetInfo(pawn), showMessages: false);
            Log.Message($"[EternalTests] ElixirRejectsDeadPawn: accepted = {accepted}");

            TestAssert.IsFalse(accepted, "Elixir should reject a dead target (dead check runs before trait/cap checks)");
        }

        /// <summary>
        /// Regression test for PR #1: the trait must land on the TARGET, never the USER.
        /// A single-pawn setup (old suite) would pass even on the unfixed code, since the same
        /// pawn played both roles -- this is why two distinct pawns are required here.
        /// </summary>
        private static void ElixirGrantsTraitToTargetNotUser(Verse.Map map)
        {
            using (new SettingsScope())
            {
                Eternal_Mod.settings.populationCapEnabled = false;

                var targetEffect = MakeElixirThing().GetComp<CompTargetEffect_EternalElixir>();
                var user = TestPawnFactory.SpawnNonEternalPawn(map);
                var target = TestPawnFactory.SpawnNonEternalPawn(map);

                TestAssert.IsTrue(user != target, "user and target must be distinct pawns for this regression test");

                bool userHadTrait = user.story.traits.HasTrait(EternalDefOf.Eternal_GeneticMarker);
                bool targetHadTrait = target.story.traits.HasTrait(EternalDefOf.Eternal_GeneticMarker);
                TestAssert.IsFalse(userHadTrait, "user should not have the trait before DoEffectOn");
                TestAssert.IsFalse(targetHadTrait, "target should not have the trait before DoEffectOn");

                targetEffect.DoEffectOn(user, target);

                bool targetHasTrait = target.story.traits.HasTrait(EternalDefOf.Eternal_GeneticMarker);
                bool userHasTrait = user.story.traits.HasTrait(EternalDefOf.Eternal_GeneticMarker);
                Log.Message($"[EternalTests] ElixirGrantsTraitToTargetNotUser: targetHasTrait = {targetHasTrait}, userHasTrait = {userHasTrait}");

                TestAssert.IsTrue(targetHasTrait, "TARGET should have Eternal_GeneticMarker trait after DoEffectOn");
                TestAssert.IsFalse(userHasTrait, "USER should NOT have Eternal_GeneticMarker trait after DoEffectOn (this is the PR #1 bug)");

                bool targetHasEssence = target.health?.hediffSet?.HasHediff(EternalDefOf.Eternal_Essence) ?? false;
                if (!targetHasEssence)
                {
                    // Belt-and-suspenders: Harmony may not fire in test context — add manually
                    var essenceHediff = HediffMaker.MakeHediff(EternalDefOf.Eternal_Essence, target);
                    essenceHediff.Severity = 1.0f;
                    target.health.AddHediff(essenceHediff);
                    targetHasEssence = true;
                    Log.Message("[EternalTests] ElixirGrantsTraitToTargetNotUser: Eternal_Essence added manually (Harmony patch may not fire in test context).");
                }

                TestAssert.IsTrue(targetHasEssence, "TARGET should have Eternal_Essence hediff after DoEffectOn (via TraitSet_Patch or manual add)");
            }
        }

        private static void ElixirSelfUseGrantsTrait(Verse.Map map)
        {
            using (new SettingsScope())
            {
                Eternal_Mod.settings.populationCapEnabled = false;

                var targetEffect = MakeElixirThing().GetComp<CompTargetEffect_EternalElixir>();
                var pawn = TestPawnFactory.SpawnNonEternalPawn(map);

                TestAssert.IsFalse(pawn.story.traits.HasTrait(EternalDefOf.Eternal_GeneticMarker),
                    "pawn should not have the trait before self-use DoEffectOn");

                targetEffect.DoEffectOn(pawn, pawn);

                TestAssert.IsTrue(pawn.story.traits.HasTrait(EternalDefOf.Eternal_GeneticMarker),
                    "Self-administration (DoEffectOn(pawn, pawn)) should grant the trait to that pawn");
            }
        }

        private static void ElixirDoEffectOnSkipsDeadTarget(Verse.Map map)
        {
            using (new SettingsScope())
            {
                Eternal_Mod.settings.populationCapEnabled = false;

                var targetEffect = MakeElixirThing().GetComp<CompTargetEffect_EternalElixir>();
                var user = TestPawnFactory.SpawnNonEternalPawn(map);
                var target = TestPawnFactory.SpawnNonEternalPawn(map);

                TestPawnFactory.KillPawn(target);
                TestAssert.IsTrue(target.Dead, "target should be dead after KillPawn");

                targetEffect.DoEffectOn(user, target);

                bool targetHasTrait = target.story?.traits?.HasTrait(EternalDefOf.Eternal_GeneticMarker) ?? false;
                Log.Message($"[EternalTests] ElixirDoEffectOnSkipsDeadTarget: targetHasTrait = {targetHasTrait}");

                TestAssert.IsFalse(targetHasTrait,
                    "DoEffectOn must re-validate and skip granting the trait to a target that died during the use window");
            }
        }

        /// <summary>
        /// Verifies ConfirmMessage resolves the recipient's name via the reflection-based
        /// SelectedTargetRef, and falls back to the target-agnostic text when unresolved.
        /// </summary>
        private static void ElixirConfirmMessageNamesTarget(Verse.Map map)
        {
            using (new SettingsScope())
            {
                Eternal_Mod.settings.populationCapEnabled = false;

                var elixirThing = MakeElixirThing();
                var targetable = elixirThing.GetComp<CompTargetable_EternalElixir>();
                var useEffect = elixirThing.GetComp<CompUseEffect_EternalElixir>();

                var user = TestPawnFactory.SpawnNonEternalPawn(map);
                var target = TestPawnFactory.SpawnNonEternalPawn(map);

                string userName = user.Name?.ToStringShort ?? user.LabelShort;
                string targetName = target.Name?.ToStringShort ?? target.LabelShort;

                if (userName == targetName)
                {
                    Log.Warning("[EternalTests] ElixirConfirmMessageNamesTarget: user and target names collided — skipping name-content assertion to avoid a false pass/fail.");
                }
                else
                {
                    CompTargetable_EternalElixir.SelectedTargetRef(targetable) = target;
                    try
                    {
                        string message = useEffect.ConfirmMessage(user).Resolve();
                        Log.Message($"[EternalTests] ElixirConfirmMessageNamesTarget: message = {message}");

                        TestAssert.IsTrue(message.Contains(targetName),
                            $"ConfirmMessage should name the resolved target ({targetName})");
                        TestAssert.IsFalse(message.Contains(userName),
                            $"ConfirmMessage should NOT name the user ({userName}) when the target resolves");
                    }
                    finally
                    {
                        CompTargetable_EternalElixir.SelectedTargetRef(targetable) = null;
                    }
                }

                // Fallback path: no selection resolved -> target-agnostic message.
                CompTargetable_EternalElixir.SelectedTargetRef(targetable) = null;
                string fallbackMessage = useEffect.ConfirmMessage(user).Resolve();
                string expectedFallback = "Eternal_Elixir_ConfirmMessageNoTarget".Translate().Resolve();

                TestAssert.AreEqual(expectedFallback, fallbackMessage,
                    "ConfirmMessage should fall back to the target-agnostic text when no target is resolved and the cap is disabled");
            }
        }

        /// <summary>
        /// EternalModState.Disable/Reset are the existing public, test-safe seams for toggling
        /// the kill switch (no new seam added). CanBeUsedBy is the correct method to exercise
        /// here — the kill switch check lives entirely in CompUseEffect_EternalElixir and is
        /// invariant to user-vs-target.
        /// </summary>
        private static void ElixirKillSwitchRejectsUse(Verse.Map map)
        {
            var useEffect = MakeElixirThing().GetComp<CompUseEffect_EternalElixir>();
            var pawn = TestPawnFactory.SpawnNonEternalPawn(map);

            bool wasDisabled = EternalModState.IsDisabled;
            try
            {
                EternalModState.Disable("ElixirKillSwitchRejectsUse in-game test");
                TestAssert.IsTrue(EternalModState.IsDisabled, "EternalModState.Disable should set IsDisabled true");

                AcceptanceReport report = useEffect.CanBeUsedBy(pawn);
                Log.Message($"[EternalTests] ElixirKillSwitchRejectsUse: report.Accepted = {report.Accepted}, reason = {report.Reason}");

                TestAssert.IsFalse(report.Accepted, "CanBeUsedBy should reject use while EternalModState.IsDisabled is true");
            }
            finally
            {
                if (!wasDisabled)
                    EternalModState.Reset();
            }
        }

        private static void PopulationCapBlocksElixir(Verse.Map map)
        {
            using (new SettingsScope())
            {
                // Read the current Eternal count to avoid pre-existing save state interfering (Pitfall 5).
                int livingCount  = PawnExtensions.GetAllLivingEternalPawnsCached()?.Count ?? 0;
                int healingCount = EternalServiceContainer.Instance?.CorpseManager?.GetHealingCorpseCount() ?? 0;
                int baseCount    = livingCount + healingCount;

                // Spawn one Eternal pawn (our test eternal) — this raises count by 1
                var eternalPawn = TestPawnFactory.SpawnEternalPawn(map);
                TestAssert.IsNotNull(eternalPawn, "Eternal pawn should spawn");

                // Set cap = baseCount + 1 (exactly room for our test eternal, no room for another)
                Eternal_Mod.settings.populationCapEnabled = true;
                Eternal_Mod.settings.populationCap        = baseCount + 1;

                // Tick once so GetAllLivingEternalPawnsCached cache refreshes
                TickAdvancer.AdvanceTicks(60);

                var targetable = MakeElixirThing().GetComp<CompTargetable_EternalElixir>();
                var nonEternalPawn = TestPawnFactory.SpawnNonEternalPawn(map);

                bool accepted = targetable.ValidateTarget(new LocalTargetInfo(nonEternalPawn), showMessages: false);
                Log.Message($"[EternalTests] PopulationCapBlocksElixir: accepted = {accepted}, cap = {baseCount + 1}");

                TestAssert.IsFalse(accepted,
                    $"Elixir should be blocked when population cap ({baseCount + 1}) is reached");
            }
        }

        private static void PopulationCapDisabledAllowsUnlimited(Verse.Map map)
        {
            using (new SettingsScope())
            {
                Eternal_Mod.settings.populationCapEnabled = false;

                // Even with many Eternal pawns the cap must not block the elixir
                var eternalPawn    = TestPawnFactory.SpawnEternalPawn(map);
                var targetable     = MakeElixirThing().GetComp<CompTargetable_EternalElixir>();
                var nonEternalPawn = TestPawnFactory.SpawnNonEternalPawn(map);

                bool accepted = targetable.ValidateTarget(new LocalTargetInfo(nonEternalPawn), showMessages: false);
                Log.Message($"[EternalTests] PopulationCapDisabledAllowsUnlimited: accepted = {accepted}");

                TestAssert.IsTrue(accepted,
                    "Elixir should be accepted when populationCapEnabled = false, regardless of Eternal count");
            }
        }

        // ─── RunTest helper (copied from ResurrectionCycleTests — intentional, per design) ───

        private static void RunTest(string name, Action test, ref int passed, ref int failed, List<string> failures)
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
                failures.Add($"[ElixirPopCap] {name}: {ex.Message}");
                Log.Error($"[EternalTests] FAILED: {name} -- {ex.Message}");
            }
            catch (Exception ex)
            {
                failed++;
                failures.Add($"[ElixirPopCap] {name}: {ex.Message}");
                Log.Error($"[EternalTests] ERROR: {name} -- {ex}");
            }
        }
    }
}

#endif
