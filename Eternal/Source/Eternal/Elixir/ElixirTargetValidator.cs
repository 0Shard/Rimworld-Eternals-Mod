/*
 * Relative Path: Eternal/Source/Eternal/Elixir/ElixirTargetValidator.cs
 * Creation Date: 28-09-2026
 * Last Edit: 28-09-2026
 * Author: 0Shard
 * Description: Pure, Verse-free reject-reason predicate for the Elixir of Eternity's target
 *              validation. Deliberately contains no `using Verse`/`using RimWorld` and no
 *              Pawn parameters so xunit can load and exercise it without pulling in
 *              Assembly-CSharp — same split used by DebtRepaymentProcessor for its drain-rate
 *              math. CompTargetable_EternalElixir.ValidateTarget and
 *              CompTargetEffect_EternalElixir.DoEffectOn both call GetRejectReasonKey with
 *              pawn state already reduced to primitives.
 */

namespace Eternal.Elixir
{
    /// <summary>
    /// Pure predicate logic for whether the Elixir of Eternity can be applied to a given
    /// target. Evaluation order is dead -> no-trait-storage -> already-Eternal -> population
    /// cap, matching the precedence the original CanBeUsedBy used (dead first avoids calling
    /// trait/hediff checks on a corpse; no-traits is new and prevents consuming the vial on a
    /// target whose story.traits is null, e.g. an animal or mechanoid).
    /// </summary>
    public static class ElixirTargetValidator
    {
        /// <summary>Target is dead — cannot receive the elixir.</summary>
        public const string RejectDeadKey = "Eternal_Elixir_RejectDead";

        /// <summary>Target has no trait storage (story.traits is null) — cannot receive traits at all.</summary>
        public const string RejectNoTraitsKey = "Eternal_Elixir_RejectNoTraits";

        /// <summary>Target already carries the Eternal_GeneticMarker trait.</summary>
        public const string RejectAlreadyEternalKey = "Eternal_Elixir_RejectAlreadyEternal";

        /// <summary>Population cap is enabled and reached.</summary>
        public const string RejectCapReachedKey = "Eternal_Elixir_RejectCapReached";

        /// <summary>
        /// Returns true when the population cap is enabled and the current Eternal count
        /// (living + healing) has reached or exceeded the configured cap. Disabled cap means
        /// unlimited — always returns false regardless of how high totalEternals is.
        /// </summary>
        public static bool IsPopulationCapReached(bool capEnabled, int totalEternals, int cap)
        {
            return capEnabled && totalEternals >= cap;
        }

        /// <summary>
        /// Returns the localization key for the first applicable rejection reason, or null when
        /// the target is accepted. Order matters: dead wins over every other flag (a dead pawn
        /// should never trigger a trait/cap message), no-trait-storage is checked next (prevents
        /// consuming the vial on a target that can never receive a trait), already-Eternal next,
        /// and population cap last (a cap message on an otherwise-invalid target would be
        /// misleading).
        /// </summary>
        public static string GetRejectReasonKey(
            bool isDead,
            bool hasTraitStorage,
            bool isAlreadyEternal,
            bool capEnabled,
            int totalEternals,
            int cap)
        {
            if (isDead)
                return RejectDeadKey;

            if (!hasTraitStorage)
                return RejectNoTraitsKey;

            if (isAlreadyEternal)
                return RejectAlreadyEternalKey;

            if (IsPopulationCapReached(capEnabled, totalEternals, cap))
                return RejectCapReachedKey;

            return null;
        }
    }
}
