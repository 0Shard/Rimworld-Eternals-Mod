/*
 * Relative Path: Eternal/Source/Eternal/Elixir/CompUseEffect_EternalElixir.cs
 * Creation Date: 12-03-2026
 * Last Edit: 12-03-2026
 * Author: 0Shard
 * Description: CompUseEffect subclass for the Elixir of Eternity. Grants the
 *              Eternal_GeneticMarker trait to a target pawn with population cap
 *              enforcement, dead/duplicate rejection, and a confirmation dialog
 *              that shows the current Eternal count. Follows vanilla mech serum
 *              comp stack pattern (CompUsable + CompTargetable + CompUseEffect).
 *              TraitSet_Patch auto-adds Eternal_Essence hediff on trait gain.
 */

using System;
using RimWorld;
using Verse;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Extensions;
using Eternal.Utils;

namespace Eternal.Elixir
{
    /// <summary>
    /// Grants the Eternal_GeneticMarker trait to a target pawn when the Elixir of Eternity
    /// is used. Validates against kill-switch, dead targets, duplicate trait, and population
    /// cap before allowing use. Returns localized rejection reasons via AcceptanceReport.
    /// </summary>
    public class CompUseEffect_EternalElixir : CompUseEffect
    {
        /// <summary>
        /// Fire before CompUseEffect_DestroySelf so the trait is added before item consumption.
        /// DestroySelf defaults to 0f; 10f guarantees this comp runs first in CompUsable.UsedBy().
        /// </summary>
        public override float OrderPriority => 10f;

        /// <summary>
        /// Validates whether the elixir can be used on the target pawn.
        /// Returns AcceptanceReport with a reason string on rejection — CompUsable formats
        /// this as "Cannot: [reason]" in both float menus and gizmo targeting feedback.
        /// </summary>
        public override AcceptanceReport CanBeUsedBy(Pawn pawn)
        {
            // Kill-switch: mod disabled due to missing critical defs
            if (EternalModState.IsDisabled)
                return "Eternal_Elixir_RejectModDisabled".Translate();

            // ELIX-03: dead pawns cannot receive the elixir
            if (pawn.Dead)
                return "Eternal_Elixir_RejectDead".Translate();

            // ELIX-04: already-Eternal pawns are rejected
            if (pawn.IsValidEternal())
                return "Eternal_Elixir_RejectAlreadyEternal".Translate();

            // ELIX-05, POP-02, POP-03: population cap enforcement
            if (IsPopulationCapReached(out int totalEternals, out int cap))
                return "Eternal_Elixir_RejectCapReached".Translate(totalEternals, cap);

            return true;
        }

        /// <summary>
        /// Returns a confirmation dialog message shown before the use job starts.
        /// When population cap is enabled, includes current Eternal count.
        /// CompUsable wraps this in Dialog_MessageBox.CreateConfirmation().
        /// </summary>
        public override TaggedString ConfirmMessage(Pawn pawn)
        {
            string pawnName = pawn.Name?.ToStringShort ?? pawn.LabelShort;

            try
            {
                var snapshot = Eternal_Mod.settings?.CreateSnapshot();
                if (snapshot?.Effects.PopulationCapEnabled == true)
                {
                    int livingCount = PawnExtensions.GetAllLivingEternalPawnsCached()?.Count ?? 0;
                    int healingCount = EternalServiceContainer.Instance?.CorpseManager?.GetHealingCorpseCount() ?? 0;
                    int totalEternals = livingCount + healingCount;
                    int cap = snapshot.Value.Effects.PopulationCap;

                    return "Eternal_Elixir_ConfirmMessageWithCap".Translate(
                        pawnName, pawnName, totalEternals, cap);
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(
                    EternalExceptionCategory.ConfigurationError,
                    "CompUseEffect_EternalElixir.ConfirmMessage", null, ex);
            }

            return "Eternal_Elixir_ConfirmMessage".Translate(pawnName, pawnName);
        }

        /// <summary>
        /// Grants the Eternal_GeneticMarker trait to the target pawn.
        /// TraitSet_Patch.Postfix automatically adds the Eternal_Essence hediff
        /// when this trait is gained — no additional hediff code needed here.
        /// </summary>
        public override void DoEffect(Pawn usedBy)
        {
            base.DoEffect(usedBy);

            try
            {
                if (usedBy.story?.traits == null)
                {
                    Log.Error($"[Eternal] Cannot grant Eternal trait to {usedBy.LabelShort} — pawn has no trait storage (story.traits is null).");
                    return;
                }

                // Belt-and-suspenders: re-check in case state changed between CanBeUsedBy and DoEffect
                if (usedBy.IsValidEternal())
                {
                    Log.Warning($"[Eternal] {usedBy.LabelShort} already has Eternal trait at DoEffect time — skipping duplicate trait addition.");
                    return;
                }

                Trait eternalTrait = new Trait(EternalDefOf.Eternal_GeneticMarker);
                usedBy.story.traits.GainTrait(eternalTrait);

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Elixir of Eternity used on {usedBy.LabelShort} — Eternal_GeneticMarker trait granted. TraitSet_Patch will auto-add Eternal_Essence hediff.");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(
                    EternalExceptionCategory.Resurrection,
                    "CompUseEffect_EternalElixir.DoEffect", usedBy, ex);
            }
        }

        /// <summary>
        /// Checks whether the population cap is enabled and currently reached.
        /// Count includes both living Eternals and corpses being healed (POP-02).
        /// </summary>
        private bool IsPopulationCapReached(out int totalEternals, out int cap)
        {
            totalEternals = 0;
            cap = 0;

            try
            {
                var snapshot = Eternal_Mod.settings?.CreateSnapshot();
                if (snapshot == null || !snapshot.Value.Effects.PopulationCapEnabled)
                    return false;

                cap = snapshot.Value.Effects.PopulationCap;
                int livingCount = PawnExtensions.GetAllLivingEternalPawnsCached()?.Count ?? 0;
                int healingCount = EternalServiceContainer.Instance?.CorpseManager?.GetHealingCorpseCount() ?? 0;
                totalEternals = livingCount + healingCount;

                return totalEternals >= cap;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(
                    EternalExceptionCategory.ConfigurationError,
                    "CompUseEffect_EternalElixir.IsPopulationCapReached", null, ex);
                return false;
            }
        }
    }
}
