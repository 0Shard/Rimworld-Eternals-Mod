/*
 * Relative Path: Eternal/Source/Eternal/Elixir/CompTargetable_EternalElixir.cs
 * Creation Date: 28-09-2026
 * Last Edit: 28-09-2026
 * Author: 0Shard
 * Description: CompTargetable_SinglePawn subclass for the Elixir of Eternity. Validates the
 *              player-chosen recipient at target-selection time (dead / no-trait-storage /
 *              already-Eternal / population-cap), routed through the pure ElixirTargetValidator.
 *              Also exposes the resolved target via reflection (SelectedTargetPawn) and the
 *              population-cap snapshot (TryGetPopulationState) so the sibling
 *              CompUseEffect_EternalElixir can build a target-named ConfirmMessage — vanilla's
 *              CompTargetable.selectedTarget field is private and OrderForceTarget is
 *              non-virtual, so no subclass can read it without reflection (verified against
 *              RimWorld-Decompiled/RimWorld/CompTargetable.cs).
 */

using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Extensions;
using Eternal.Utils;

namespace Eternal.Elixir
{
    /// <summary>
    /// Target-side validation for the Elixir of Eternity. Fires from
    /// <c>Targeter.ProcessInputEvents</c> (hover, and on click with <c>showMessages: true</c>)
    /// before <c>CompTargetable.OrderForceTarget</c> commits the selection — this is the
    /// correct gate point for reject messaging, not <c>CompUseEffect.CanBeUsedBy</c>, which only
    /// ever receives the administering pawn.
    /// </summary>
    public class CompTargetable_EternalElixir : CompTargetable_SinglePawn
    {
        /// <summary>
        /// Cached reflection accessor for the private <see cref="CompTargetable.selectedTarget"/>
        /// field. Reflection is the only way to read it: the field is private on the abstract
        /// vanilla base and <c>OrderForceTarget</c> is a non-virtual <c>ITargetingSource</c>
        /// implementation, so no subclass — of <see cref="CompTargetable"/> or of the sibling
        /// <see cref="CompUseEffect_EternalElixir"/> — can intercept or shadow it.
        /// </summary>
        internal static readonly AccessTools.FieldRef<CompTargetable, Thing> SelectedTargetRef =
            AccessTools.FieldRefAccess<CompTargetable, Thing>("selectedTarget");

        /// <summary>
        /// The pawn currently selected as the elixir's recipient, or null when unresolved
        /// (no selection yet, selection is not a Pawn, or the reflection read failed — e.g. a
        /// future RimWorld version renames the field). Callers must treat null as "fall back to
        /// a target-agnostic message," never as an error condition.
        /// </summary>
        internal Pawn SelectedTargetPawn
        {
            get
            {
                try
                {
                    return SelectedTargetRef(this) as Pawn;
                }
                catch (Exception ex)
                {
                    EternalLogger.HandleException(
                        EternalExceptionCategory.ConfigurationError,
                        "CompTargetable_EternalElixir.SelectedTargetPawn", null, ex);
                    return null;
                }
            }
        }

        /// <summary>
        /// Reads the population-cap snapshot in one place so <see cref="ValidateTarget"/> and
        /// <see cref="CompUseEffect_EternalElixir.ConfirmMessage"/> agree on the same numbers.
        /// On any failure, fails open (<paramref name="capEnabled"/> = false) — matching the
        /// pre-fix behavior where a broken settings snapshot never blocked elixir use.
        /// </summary>
        /// <returns>True when the snapshot was read successfully; false on failure.</returns>
        internal static bool TryGetPopulationState(out bool capEnabled, out int totalEternals, out int cap)
        {
            capEnabled = false;
            totalEternals = 0;
            cap = 0;

            try
            {
                var snapshot = Eternal_Mod.settings?.CreateSnapshot();
                if (snapshot == null)
                    return false;

                capEnabled = snapshot.Value.Effects.PopulationCapEnabled;
                cap = snapshot.Value.Effects.PopulationCap;

                int livingCount = PawnExtensions.GetAllLivingEternalPawnsCached()?.Count ?? 0;
                int healingCount = EternalServiceContainer.Instance?.CorpseManager?.GetHealingCorpseCount() ?? 0;
                totalEternals = livingCount + healingCount;

                return true;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(
                    EternalExceptionCategory.ConfigurationError,
                    "CompTargetable_EternalElixir.TryGetPopulationState", null, ex);
                capEnabled = false;
                return false;
            }
        }

        /// <summary>
        /// Rejects dead / no-trait-storage / already-Eternal / cap-reached targets at
        /// selection time (red cursor on hover, RejectInput message on click). Never throws —
        /// this runs every frame via <c>CompTargetable.OnGUI</c> with <c>showMessages: false</c>.
        /// </summary>
        public override bool ValidateTarget(LocalTargetInfo target, bool showMessages = true)
        {
            if (!base.ValidateTarget(target, showMessages))
                return false;

            if (!(target.Thing is Pawn pawn))
                return false;

            TryGetPopulationState(out bool capEnabled, out int totalEternals, out int cap);

            string reasonKey = ElixirTargetValidator.GetRejectReasonKey(
                pawn.Dead,
                pawn.story?.traits != null,
                !pawn.Dead && pawn.IsValidEternal(),
                capEnabled,
                totalEternals,
                cap);

            if (reasonKey == null)
                return true;

            if (showMessages)
            {
                string reasonText = reasonKey == ElixirTargetValidator.RejectCapReachedKey
                    ? reasonKey.Translate(totalEternals, cap)
                    : reasonKey.Translate();

                Messages.Message(
                    "Eternal_Elixir_RejectMessage".Translate(parent.LabelShort, pawn.LabelShort, reasonText),
                    pawn, MessageTypeDefOf.RejectInput, historical: false);
            }

            return false;
        }
    }
}
