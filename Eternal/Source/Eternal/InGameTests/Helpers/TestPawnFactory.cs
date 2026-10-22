// Relative Path: Eternal/Source/Eternal/InGameTests/Helpers/TestPawnFactory.cs
// Author: 0Shard
// Description: Own fixtures from generation onward; observe hooks and clean only owned state.
#if DEBUG
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;
using RimWorld;
using Eternal.DI;

namespace Eternal.InGameTests.Helpers
{
    public static class TestPawnFactory
    {
        private static readonly List<Thing> _spawnedTestThings = new List<Thing>();

        public static Pawn SpawnEternalPawn(Verse.Map map)
        {
            var pawn = GeneratePawn(map);
            TestAssert.Available(EternalDefOf.Eternal_GeneticMarker, "Eternal trait def");
            TestAssert.Available(EternalDefOf.Eternal_Essence, "Eternal Essence def");
            TestAssert.IsFalse(EternalModState.IsDisabled, "Eternal must be enabled for hook tests");
            PrepareNonEternal(pawn);
            pawn.story.traits.GainTrait(new Trait(EternalDefOf.Eternal_GeneticMarker));
            TestAssert.IsTrue(pawn.health.hediffSet.HasHediff(EternalDefOf.Eternal_Essence),
                "GainTrait must add Essence via the actual TraitSet hook");
            Spawn(pawn, map);
            return pawn;
        }

        public static Pawn SpawnNonEternalPawn(Verse.Map map)
        {
            var pawn = GeneratePawn(map);
            PrepareNonEternal(pawn);
            Spawn(pawn, map);
            return pawn;
        }

        private static Pawn GeneratePawn(Verse.Map map)
        {
            TestAssert.Available(map, "Current map");
            var request = new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                PawnGenerationContext.NonPlayer, forceGenerateNewPawn: true);
            var pawn = PawnGenerator.GeneratePawn(request);
            TestAssert.IsNotNull(pawn, "Pawn generation must succeed");
            // Ownership precedes trait, health or map setup, all of which may fail.
            _spawnedTestThings.Add(pawn);
            TestAssert.Available(pawn.story?.traits, "Generated pawn traits");
            TestAssert.Available(pawn.health?.hediffSet, "Generated pawn health");
            return pawn;
        }

        private static void PrepareNonEternal(Pawn pawn)
        {
            // This is input preparation, before observing any tested trait/elixir result.
            TestAssert.Available(EternalDefOf.Eternal_GeneticMarker, "Eternal trait def");
            TestAssert.Available(EternalDefOf.Eternal_Essence, "Eternal Essence def");
            var trait = pawn.story.traits.GetTrait(EternalDefOf.Eternal_GeneticMarker);
            if (trait != null)
            {
                pawn.story.traits.RemoveTrait(trait);
            }

            var essence = pawn.health.hediffSet.GetFirstHediffOfDef(EternalDefOf.Eternal_Essence);
            if (essence != null)
            {
                pawn.health.RemoveHediff(essence);
            }

            TestAssert.IsFalse(pawn.health.hediffSet.HasHediff(EternalDefOf.Eternal_Essence),
                "Non-Eternal input must have no Essence");
        }

        private static void Spawn(Pawn pawn, Verse.Map map)
        {
            GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(map.Center, map, 10), map);
            TestAssert.IsTrue(pawn.Spawned, "Generated fixture must be spawned");
        }

        internal static void OwnThing(Thing thing)
        {
            TestAssert.IsNotNull(thing, "Test thing must exist before ownership registration");
            _spawnedTestThings.Add(thing);
        }

        public static void KillPawn(Pawn pawn)
        {
            TestAssert.IsNotNull(pawn, "KillPawn requires a pawn");
            TestAssert.IsFalse(pawn.Dead, "KillPawn requires a living pawn");
            pawn.Kill(null, null);
        }

        public static Verse.Corpse FindCorpseForPawn(Pawn pawn, Verse.Map map)
        {
            if (pawn == null || map == null)
            {
                return null;
            }

            foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse))
            {
                if (thing is Verse.Corpse corpse && corpse.InnerPawn == pawn)
                {
                    return corpse;
                }
            }

            return null;
        }

        public static void CleanupAll()
        {
            var failures = new List<Exception>();
            foreach (var thing in _spawnedTestThings.ToArray())
            {
                try
                {
                    if (thing is Pawn pawn)
                    {
                        CleanupPawn(pawn);
                    }
                    else if (thing != null && !thing.Destroyed)
                    {
                        thing.Destroy();
                    }

                    _spawnedTestThings.Remove(thing);
                }
                catch (Exception ex) { failures.Add(ex); }
            }
            if (failures.Count > 0)
            {
                throw new AggregateException("Test-owned cleanup failed", failures);
            }
        }

        private static void CleanupPawn(Pawn pawn)
        {
            var services = EternalServiceContainer.Instance;
            // Dead pawns are destroyed Things, but their corpse and ledgers still need cleanup.
            var corpse = pawn.Corpse ?? services.CorpseManager?.GetCorpseData(pawn)?.Corpse;
            var failures = new List<Exception>();
            Action<Action> attempt = action => { try { action(); } catch (Exception ex) { failures.Add(ex); } };
            attempt(() => services.FoodDebtSystem?.UnregisterPawn(pawn));
            attempt(() => services.ThresholdTracker?.ClearPawnThresholds(pawn));
            attempt(() => services.HealingProcessor?.HediffHealer?.ClearPawnHealingProgress(pawn));
            attempt(() =>
            {
                if (services.CorpseHealingProcessor != null)
                {
                    ((HashSet<Pawn>)RequiredField(services.CorpseHealingProcessor, "activeHealingCorpses")).Remove(pawn);
                }
            });
            attempt(() =>
            {
                if (services.CorpseManager?.IsTracked(pawn) == true)
                {
                    services.CorpseManager.UnregisterCorpse(pawn);
                }
            });
            attempt(() =>
            {
                if (corpse != null)
                {
                    services.CorpsePreservation?.UnpreserveCorpse(corpse);
                }
            });
            attempt(() =>
            {
                if (corpse != null && !corpse.Destroyed)
                {
                    corpse.Destroy();
                }

                if (!pawn.Destroyed)
                {
                    pawn.Destroy();
                }
            });
            // Destruction hooks may enqueue the pawn; remove this fixture only after destruction.
            attempt(() =>
            {
                if (Eternal_Component.Current != null)
                {
                    var orchestrator = RequiredField(Eternal_Component.Current, "tickOrchestrator");
                    ((HashSet<Pawn>)RequiredField(orchestrator, "pendingHealthReconciliation")).Remove(pawn);
                }
            });
            attempt(() =>
            {
                TestAssert.IsFalse(services.CorpseManager?.IsTracked(pawn) ?? false, "Owned corpse must be untracked");
                if (services.FoodDebtSystem != null)
                {
                    foreach (var tracked in services.FoodDebtSystem.GetTrackedPawns())
                    {
                        TestAssert.IsFalse(ReferenceEquals(pawn, tracked), "Owned debt pawn must be untracked");
                    }
                }
            });
            if (failures.Count > 0)
            {
                throw new AggregateException($"Cleanup pawn {pawn.thingIDNumber}", failures);
            }
        }

        internal static object RequiredField(object owner, string name)
        {
            TestAssert.IsNotNull(owner, $"Owner of {name}");
            var field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            TestAssert.IsNotNull(field, $"State field {name}");
            var value = field.GetValue(owner);
            TestAssert.IsNotNull(value, $"State value {name}");
            return value;
        }
    }
}
#endif
