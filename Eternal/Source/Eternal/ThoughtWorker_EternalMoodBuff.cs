// Relative Path: Eternal/Source/Eternal/ThoughtWorker_EternalMoodBuff.cs
// Creation Date: 12-03-2026
// Last Edit: 12-03-2026
// Author: 0Shard
// Description: Custom ThoughtWorker for the Eternal mood buff thought.
//              Returns ThoughtState.Inactive when the mood buff is disabled in settings,
//              so the thought vanishes entirely from the needs tab (no ghost "+0" entry).
//              MoodMultiplier returns the configured moodBuffValue, multiplied by baseMoodEffect=1
//              in the XML def to produce the final mood offset.

using Eternal.Extensions;
using RimWorld;
using Verse;

namespace Eternal
{
    /// <summary>
    /// ThoughtWorker for the Eternal_MoodBuff thought.
    ///
    /// Why a custom ThoughtWorker instead of ThoughtWorker_AlwaysActive:
    /// - ThoughtWorker_AlwaysActive always returns ThoughtState.ActiveAtStage(0), even when disabled.
    ///   This produces a "+0 mood" ghost entry in the needs tab when baseMoodEffect=0.
    /// - Returning ThoughtState.Inactive causes RimWorld to remove the thought entirely,
    ///   giving a clean on/off toggle with no UI artifacts.
    ///
    /// Why MoodMultiplier drives the value instead of baseMoodEffect in XML:
    /// - baseMoodEffect is a static XML value baked at load time; it cannot respond to
    ///   settings changes at runtime.
    /// - MoodMultiplier is called every time the thought's mood offset is evaluated.
    ///   With baseMoodEffect=1 in XML, the final offset = 1 * MoodMultiplier = configuredValue,
    ///   updating live when the player adjusts the slider.
    /// </summary>
    public class ThoughtWorker_EternalMoodBuff : ThoughtWorker
    {
        /// <summary>
        /// Determines whether the Eternal mood buff thought is active for the given pawn.
        ///
        /// Guard order matters:
        /// 1. Null/dead check — corpses and null pawns never receive mood thoughts.
        /// 2. Settings toggle — if disabled, return Inactive immediately (no "+0" entry).
        /// 3. Trait check  — belt-and-suspenders; requiredHediffs in XML already filters
        ///    non-Eternal pawns before this method is called, but an explicit trait check
        ///    prevents edge cases where Eternal_Essence exists without the marker trait.
        /// 4. Hediff check — the thought requires Eternal_Essence to be active on the pawn.
        /// </summary>
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p == null || p.Dead)
                return ThoughtState.Inactive;

            bool moodEnabled = Eternal_Mod.settings?.moodBuffEnabled
                ?? SettingsDefaults.MoodBuffEnabled;

            if (!moodEnabled)
                return ThoughtState.Inactive;

            if (!p.HasTraitIgnoringSuppression(EternalDefOf.Eternal_GeneticMarker))
                return ThoughtState.Inactive;

            if (!(p.health?.hediffSet?.HasHediff(EternalDefOf.Eternal_Essence) ?? false))
                return ThoughtState.Inactive;

            return ThoughtState.ActiveAtStage(0);
        }

        /// <summary>
        /// Returns the configured mood buff value as a float multiplier.
        /// RimWorld computes the final mood offset as: baseMoodEffect * MoodMultiplier.
        /// With baseMoodEffect=1 in the XML def, the offset equals the configured value directly.
        ///
        /// Negative values are clamped to 0 — the requirements spec a non-negative mood bonus only.
        /// </summary>
        public override float MoodMultiplier(Pawn p)
        {
            int moodValue = Eternal_Mod.settings?.moodBuffValue
                ?? SettingsDefaults.MoodBuffValue;

            return moodValue < 0 ? 0f : (float)moodValue;
        }
    }
}
