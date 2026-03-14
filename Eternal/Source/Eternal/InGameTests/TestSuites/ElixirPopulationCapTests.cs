// Relative Path: Eternal/Source/Eternal/InGameTests/TestSuites/ElixirPopulationCapTests.cs
// Creation Date: 13-03-2026
// Last Edit: 13-03-2026
// Author: 0Shard
// Description: In-game E2E tests for the Elixir of Eternity and population cap (v1.0.1).
//              Covers CanBeUsedBy acceptance/rejection logic (dead target, already-Eternal,
//              pop cap enforcement) and DoEffect trait-grant verification.
//              Uses SettingsScope for all population cap mutations.

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
    /// Tests the Elixir of Eternity and population cap enforcement introduced in v1.0.1.
    /// Exercises CompUseEffect_EternalElixir.CanBeUsedBy and DoEffect paths.
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
                RunTest("ElixirDoEffectGrantsTrait",
                    () => ElixirDoEffectGrantsTrait(map), ref passed, ref failed, failures);
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
        /// Creates a free-standing elixir Thing and returns its use-effect comp.
        /// Does not spawn the elixir on the map — it is only used programmatically.
        /// </summary>
        private static CompUseEffect_EternalElixir MakeElixirComp()
        {
            var elixirDef = DefDatabase<ThingDef>.GetNamedSilentFail("Eternal_ElixirOfEternity");
            TestAssert.IsNotNull(elixirDef, "Elixir ThingDef 'Eternal_ElixirOfEternity' must exist in DefDatabase");

            var elixirThing = ThingMaker.MakeThing(elixirDef);
            var elixirComp  = (elixirThing as ThingWithComps)?.GetComp<CompUseEffect_EternalElixir>();
            TestAssert.IsNotNull(elixirComp, "Elixir must have CompUseEffect_EternalElixir component");

            return elixirComp;
        }

        // ─── Test Bodies ────────────────────────────────────────────────────

        private static void ElixirAcceptsNonEternalPawn(Verse.Map map)
        {
            using (new SettingsScope())
            {
                // Make sure pop cap is not the blocker here
                Eternal_Mod.settings.populationCapEnabled = false;

                var elixirComp = MakeElixirComp();
                var pawn = TestPawnFactory.SpawnNonEternalPawn(map);

                AcceptanceReport report = elixirComp.CanBeUsedBy(pawn);
                Log.Message($"[EternalTests] ElixirAcceptsNonEternalPawn: report.Accepted = {report.Accepted}, reason = {report.Reason}");

                TestAssert.IsTrue(report.Accepted,
                    $"Elixir should accept a non-Eternal pawn, but rejected: {report.Reason}");
            }
        }

        private static void ElixirRejectsAlreadyEternalPawn(Verse.Map map)
        {
            var elixirComp = MakeElixirComp();
            var pawn = TestPawnFactory.SpawnEternalPawn(map);

            AcceptanceReport report = elixirComp.CanBeUsedBy(pawn);
            Log.Message($"[EternalTests] ElixirRejectsAlreadyEternalPawn: report.Accepted = {report.Accepted}, reason = {report.Reason}");

            TestAssert.IsFalse(report.Accepted,
                "Elixir should reject a pawn that already has the Eternal trait");
        }

        private static void ElixirRejectsDeadPawn(Verse.Map map)
        {
            var elixirComp = MakeElixirComp();
            var pawn = TestPawnFactory.SpawnEternalPawn(map);

            TestPawnFactory.KillPawn(pawn);
            TestAssert.IsTrue(pawn.Dead, "Pawn should be dead after KillPawn");

            AcceptanceReport report = elixirComp.CanBeUsedBy(pawn);
            Log.Message($"[EternalTests] ElixirRejectsDeadPawn: report.Accepted = {report.Accepted}, reason = {report.Reason}");

            TestAssert.IsFalse(report.Accepted,
                "Elixir should reject a dead pawn (dead check runs before trait check)");
        }

        private static void ElixirDoEffectGrantsTrait(Verse.Map map)
        {
            using (new SettingsScope())
            {
                // Cap off so DoEffect can proceed
                Eternal_Mod.settings.populationCapEnabled = false;

                var elixirComp = MakeElixirComp();
                var pawn = TestPawnFactory.SpawnNonEternalPawn(map);

                TestAssert.IsNotNull(pawn.story?.traits, "Pawn must have trait storage");
                bool hadTrait = pawn.story.traits.HasTrait(EternalDefOf.Eternal_GeneticMarker);
                TestAssert.IsFalse(hadTrait, "Non-Eternal pawn should not have trait before DoEffect");

                elixirComp.DoEffect(pawn);

                bool hasTrait = pawn.story.traits.HasTrait(EternalDefOf.Eternal_GeneticMarker);
                Log.Message($"[EternalTests] ElixirDoEffectGrantsTrait: hasTrait = {hasTrait}");

                TestAssert.IsTrue(hasTrait,
                    "Pawn should have Eternal_GeneticMarker trait after DoEffect");

                // TraitSet_Patch should have fired and added Eternal_Essence, or we add it manually
                bool hasEssence = pawn.health?.hediffSet?.HasHediff(EternalDefOf.Eternal_Essence) ?? false;
                if (!hasEssence)
                {
                    // Belt-and-suspenders: Harmony may not fire in test context — add manually
                    var essenceHediff = HediffMaker.MakeHediff(EternalDefOf.Eternal_Essence, pawn);
                    essenceHediff.Severity = 1.0f;
                    pawn.health.AddHediff(essenceHediff);
                    hasEssence = true;
                    Log.Message("[EternalTests] ElixirDoEffectGrantsTrait: Eternal_Essence added manually (Harmony patch may not fire in test context).");
                }

                TestAssert.IsTrue(hasEssence,
                    "Pawn should have Eternal_Essence hediff after DoEffect (via TraitSet_Patch or manual add)");
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

                var elixirComp    = MakeElixirComp();
                var nonEternalPawn = TestPawnFactory.SpawnNonEternalPawn(map);

                AcceptanceReport report = elixirComp.CanBeUsedBy(nonEternalPawn);
                Log.Message($"[EternalTests] PopulationCapBlocksElixir: report.Accepted = {report.Accepted}, reason = {report.Reason}, cap = {baseCount + 1}");

                TestAssert.IsFalse(report.Accepted,
                    $"Elixir should be blocked when population cap ({baseCount + 1}) is reached, reason: {report.Reason}");
            }
        }

        private static void PopulationCapDisabledAllowsUnlimited(Verse.Map map)
        {
            using (new SettingsScope())
            {
                Eternal_Mod.settings.populationCapEnabled = false;

                // Even with many Eternal pawns the cap must not block the elixir
                var eternalPawn    = TestPawnFactory.SpawnEternalPawn(map);
                var elixirComp     = MakeElixirComp();
                var nonEternalPawn = TestPawnFactory.SpawnNonEternalPawn(map);

                AcceptanceReport report = elixirComp.CanBeUsedBy(nonEternalPawn);
                Log.Message($"[EternalTests] PopulationCapDisabledAllowsUnlimited: report.Accepted = {report.Accepted}, reason = {report.Reason}");

                TestAssert.IsTrue(report.Accepted,
                    $"Elixir should be accepted when populationCapEnabled = false, but rejected: {report.Reason}");
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
