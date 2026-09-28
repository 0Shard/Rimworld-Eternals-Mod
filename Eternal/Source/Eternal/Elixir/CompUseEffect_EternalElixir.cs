/*
 * Relative Path: Eternal/Source/Eternal/Elixir/CompUseEffect_EternalElixir.cs
 * Creation Date: 12-03-2026
 * Last Edit: 28-09-2026
 * Author: 0Shard
 * Description: CompUseEffect subclass for the Elixir of Eternity. Slimmed to the two checks
 *              that fire on the USER pawn before targeting begins: the kill-switch reject
 *              (CanBeUsedBy) and a target-named confirmation dialog (ConfirmMessage). Target-side
 *              validation (dead / no-trait-storage / already-Eternal / population cap) moved to
 *              CompTargetable_EternalElixir.ValidateTarget, and the trait grant itself moved to
 *              CompTargetEffect_EternalElixir.DoEffectOn — fixing PR #1's bug where the trait
 *              landed on the administering pawn instead of the chosen recipient, because a plain
 *              CompUseEffect always receives the USER from CompUsable.UsedBy, never the target.
 */

using RimWorld;
using Verse;

namespace Eternal.Elixir
{
    /// <summary>
    /// User-side gate and confirmation dialog for the Elixir of Eternity. Both
    /// <see cref="CanBeUsedBy"/> and <see cref="ConfirmMessage"/> fire with the administering
    /// pawn, not the recipient (<c>CompUsable.CanBeUsedBy</c> / <c>TryStartUseJob</c> both pass
    /// the user) — see <see cref="CompTargetable_EternalElixir"/> for recipient-side validation
    /// and <see cref="CompTargetEffect_EternalElixir"/> for the trait grant itself.
    /// </summary>
    public class CompUseEffect_EternalElixir : CompUseEffect
    {
        /// <summary>
        /// Gates use of the item at all. `pawn` here is the USER (CompUsable.CanBeUsedBy always
        /// passes the administering pawn), which is why target-only checks (dead,
        /// already-Eternal, no-trait-storage, population cap) live in
        /// CompTargetable_EternalElixir.ValidateTarget instead of here — leaving them here was
        /// itself a smaller instance of the same user/target confusion this fix addresses.
        /// </summary>
        public override AcceptanceReport CanBeUsedBy(Pawn pawn)
        {
            if (EternalModState.IsDisabled)
                return "Eternal_Elixir_RejectModDisabled".Translate();

            return true;
        }

        /// <summary>
        /// Returns a confirmation dialog message shown before the use job starts. `pawn` here is
        /// the USER — the displayed recipient name is resolved via the sibling
        /// CompTargetable_EternalElixir, whose selectedTarget field is already set by
        /// CompTargetable.OrderForceTarget before CompUsable.TryStartUseJob calls this method.
        /// Falls back to a target-agnostic message when the target cannot be resolved.
        /// </summary>
        public override TaggedString ConfirmMessage(Pawn pawn)
        {
            Pawn targetPawn = parent.GetComp<CompTargetable_EternalElixir>()?.SelectedTargetPawn;

            CompTargetable_EternalElixir.TryGetPopulationState(out bool capEnabled, out int totalEternals, out int cap);

            if (targetPawn != null)
            {
                string targetName = targetPawn.Name?.ToStringShort ?? targetPawn.LabelShort;

                return capEnabled
                    ? "Eternal_Elixir_ConfirmMessageWithCap".Translate(targetName, targetName, totalEternals, cap)
                    : "Eternal_Elixir_ConfirmMessage".Translate(targetName, targetName);
            }

            return capEnabled
                ? "Eternal_Elixir_ConfirmMessageNoTargetWithCap".Translate(totalEternals, cap)
                : "Eternal_Elixir_ConfirmMessageNoTarget".Translate();
        }
    }
}
