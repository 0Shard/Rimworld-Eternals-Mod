// Relative Path: Eternal/Source/Eternal/InGameTests/Helpers/TestPawnFactory.cs
// Creation Date: 24-02-2026
// Last Edit: 24-02-2026
// Author: 0Shard
// Description: Factory for spawning and managing test Eternal pawns in-game.
//              Handles pawn generation, trait assignment, killing, and cleanup.
//              RC1-FIX: Eternal_Essence is now ALWAYS added unconditionally after GainTrait()
//              to eliminate any dependency on Harmony TraitSet_Patch firing in the test context.
//              If Harmony already added it, the existing one is removed first to ensure clean state.
//              Note: Verse.Corpse and Verse.Map used fully-qualified to avoid Eternal.Corpse/Eternal.Map conflicts.

#if DEBUG

using System;
using System.Collections.Generic;
using Verse;
using RimWorld;

namespace Eternal.InGameTests.Helpers
{
    /// <summary>
    /// Creates and manages Eternal test pawns for in-game integration tests.
    /// </summary>
    public static class TestPawnFactory
    {
        private static readonly List<Thing> _spawnedTestThings = new List<Thing>();

        /// <summary>
        /// Spawns a new colonist pawn with the Eternal_GeneticMarker trait on the given map.
        /// The trait assignment triggers TraitSet_Patch which auto-adds Eternal_Essence hediff.
        /// </summary>
        public static Pawn SpawnEternalPawn(Verse.Map map)
        {
            var request = new PawnGenerationRequest(
                PawnKindDefOf.Colonist,
                Faction.OfPlayer,
                PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true);

            var pawn = PawnGenerator.GeneratePawn(request);

            // Add the Eternal trait (triggers Eternal_Essence via Harmony patch)
            if (pawn.story?.traits != null && EternalDefOf.Eternal_GeneticMarker != null)
            {
                Log.Message($"[EternalTests] EternalModState.IsDisabled = {EternalModState.IsDisabled}");
                Log.Message($"[EternalTests] EternalDefOf.Eternal_Essence = {(EternalDefOf.Eternal_Essence != null ? "OK" : "NULL")}");
                Log.Message($"[EternalTests] EternalDefOf.Eternal_GeneticMarker = {(EternalDefOf.Eternal_GeneticMarker != null ? "OK" : "NULL")}");

                pawn.story.traits.GainTrait(new Trait(EternalDefOf.Eternal_GeneticMarker));

                // RC1-FIX: ALWAYS add Eternal_Essence unconditionally to eliminate Harmony timing dependency.
                // If Harmony TraitSet_Patch already fired and added it, remove the existing one first to
                // ensure a clean, properly-initialized hediff with the correct severity.
                bool hasEssence = pawn.health?.hediffSet?.HasHediff(EternalDefOf.Eternal_Essence) ?? false;
                Log.Message($"[EternalTests] After GainTrait: HasEternalEssence = {hasEssence} (will re-add unconditionally)");

                if (EternalDefOf.Eternal_Essence != null)
                {
                    // Remove any existing Eternal_Essence first (in case Harmony added one)
                    if (hasEssence)
                    {
                        var existingEssence = pawn.health.hediffSet.GetFirstHediffOfDef(EternalDefOf.Eternal_Essence);
                        if (existingEssence != null)
                        {
                            pawn.health.RemoveHediff(existingEssence);
                            Log.Message("[EternalTests] Removed Harmony-added Eternal_Essence to ensure clean re-add.");
                        }
                    }

                    // Always add a fresh Eternal_Essence with correct severity
                    var hediff = HediffMaker.MakeHediff(EternalDefOf.Eternal_Essence, pawn);
                    hediff.Severity = 1.0f;
                    pawn.health.AddHediff(hediff);
                    Log.Message("[EternalTests] Eternal_Essence added unconditionally for reliable test state.");
                }
                else
                {
                    Log.Error("[EternalTests] EternalDefOf.Eternal_Essence is null — cannot add hediff. Test will likely fail.");
                }
            }
            else
            {
                Log.Error($"[EternalTests] Cannot add trait: story.traits={pawn.story?.traits != null}, GeneticMarker={EternalDefOf.Eternal_GeneticMarker != null}");
            }

            // Spawn on map at a walkable cell near center
            var cell = CellFinder.RandomClosewalkCellNear(map.Center, map, 10);
            GenSpawn.Spawn(pawn, cell, map);

            _spawnedTestThings.Add(pawn);

            Log.Message($"[EternalTests] Spawned Eternal pawn: {pawn.LabelShort} at {cell}");
            return pawn;
        }

        /// <summary>
        /// Kills the given pawn, triggering the death -> corpse registration flow.
        /// </summary>
        public static void KillPawn(Pawn pawn)
        {
            if (pawn == null || pawn.Dead)
                return;

            Log.Message($"[EternalTests] Killing pawn: {pawn.LabelShort}");
            pawn.Kill(null, null);
        }

        /// <summary>
        /// Finds the corpse for a dead pawn on the given map.
        /// </summary>
        public static Verse.Corpse FindCorpseForPawn(Pawn pawn, Verse.Map map)
        {
            if (pawn == null || map == null)
                return null;

            var corpseGroup = ThingRequestGroup.Corpse;
            foreach (var thing in map.listerThings.ThingsInGroup(corpseGroup))
            {
                var corpse = thing as Verse.Corpse;
                if (corpse != null && corpse.InnerPawn == pawn)
                {
                    return corpse;
                }
            }
            return null;
        }

        /// <summary>
        /// Cleans up all test pawns and corpses spawned during the test run.
        /// </summary>
        public static void CleanupAll()
        {
            Log.Message($"[EternalTests] Cleaning up {_spawnedTestThings.Count} test things");

            foreach (var thing in _spawnedTestThings)
            {
                try
                {
                    if (thing != null && !thing.Destroyed)
                    {
                        if (thing is Pawn pawn && pawn.Dead)
                        {
                            var corpse = pawn.Corpse;
                            if (corpse != null && !corpse.Destroyed)
                            {
                                corpse.Destroy();
                            }
                        }
                        else if (!thing.Destroyed)
                        {
                            thing.Destroy();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[EternalTests] Cleanup failed for {thing}: {ex.Message}");
                }
            }

            _spawnedTestThings.Clear();
        }
    }
}

#endif
