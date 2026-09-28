/*
 * Relative Path: Eternal/Source/Eternal/Elixir/CompTargetEffect_EternalElixir.cs
 * Creation Date: 28-09-2026
 * Last Edit: 28-09-2026
 * Author: 0Shard
 * Description: CompTargetEffect subclass for the Elixir of Eternity. This is what vanilla
 *              CompTargetable.DoEffect actually invokes with the player-chosen recipient
 *              (RimWorld-Decompiled/RimWorld/CompTargetable.cs:64-69) — the fix for PR #1's bug,
 *              where the previous plain CompUseEffect received the USER pawn from
 *              CompUsable.UsedBy instead. Grants Eternal_GeneticMarker to the target, not the
 *              user. TraitSet_Patch auto-adds Eternal_Essence hediff on trait gain.
 */

using System;
using RimWorld;
using Verse;
using Eternal.Exceptions;
using Eternal.Extensions;
using Eternal.Utils;

namespace Eternal.Elixir
{
    /// <summary>
    /// Grants the Eternal_GeneticMarker trait to the elixir's target pawn. Re-validates
    /// dead / already-Eternal / trait-storage state at effect time as defense-in-depth: the
    /// 600-tick use duration means state can change between selection (ValidateTarget) and
    /// effect (DoEffectOn). Population cap is intentionally NOT re-checked here — it was already
    /// enforced at selection time and a mid-use cap change should not silently waste a
    /// materialized vial.
    /// </summary>
    public class CompTargetEffect_EternalElixir : CompTargetEffect
    {
        public override void DoEffectOn(Pawn user, Thing target)
        {
            var pawn = target as Pawn;
            if (pawn == null)
            {
                // CompTargetable_SinglePawn.GetTargets always yields the player-chosen Thing;
                // ValidateTarget already gates non-Pawn targets, so this should be unreachable
                // outside a future vanilla behavior change.
                Log.Warning("[Eternal] CompTargetEffect_EternalElixir.DoEffectOn: target is not a Pawn — nothing to grant.");
                return;
            }

            try
            {
                string reasonKey = ElixirTargetValidator.GetRejectReasonKey(
                    pawn.Dead,
                    pawn.story?.traits != null,
                    !pawn.Dead && pawn.IsValidEternal(),
                    capEnabled: false, totalEternals: 0, cap: 0);

                if (reasonKey != null)
                {
                    Log.Warning($"[Eternal] Elixir DoEffectOn skipped for {pawn.LabelShort} (used by {user.LabelShort}) — {reasonKey} (state changed during the 600-tick use window).");
                    return;
                }

                Trait eternalTrait = new Trait(EternalDefOf.Eternal_GeneticMarker);
                pawn.story.traits.GainTrait(eternalTrait);

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Elixir of Eternity used by {user.LabelShort} on {pawn.LabelShort} — Eternal_GeneticMarker trait granted. TraitSet_Patch will auto-add Eternal_Essence hediff.");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(
                    EternalExceptionCategory.Resurrection,
                    "CompTargetEffect_EternalElixir.DoEffectOn", pawn, ex);
            }
        }
    }
}
