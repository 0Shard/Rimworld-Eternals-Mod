/*
 * Relative Path: Eternal/Source/Eternal/Infrastructure/HealingDictionaryKey.cs
 * Creation Date: 19-02-2026
 * Last Edit: 19-02-2026
 * Author: 0Shard
 * Description: Composite struct key for healing dictionaries.
 *              Replaces per-tick string allocations in hot healing paths (PERF-04).
 *              Three-field key provides global uniqueness across all pawns:
 *                - PawnThingIDNumber: stable across save/load (assigned at birth, persists in save file)
 *                - HediffDefName: stable XML def name (not loadID which changes per session)
 *                - BodyPartLabel: stable from def, empty string for non-part-specific hediffs
 *              NOT IExposable — readonly struct fields cannot be passed as ref to Scribe_Values.
 *              Dictionaries keyed by this type serialize via parallel-list decomposition at the call site.
 */

using Verse;

namespace Eternal.Infrastructure
{
    /// <summary>
    /// Composite struct key for healing dictionaries.
    /// Replaces string keys to eliminate per-tick allocation in healing hot paths.
    /// Global uniqueness is achieved via (PawnThingIDNumber, HediffDefName, BodyPartLabel).
    /// </summary>
    public readonly struct HealingDictionaryKey : System.IEquatable<HealingDictionaryKey>
    {
        /// <summary>
        /// The pawn's thingIDNumber — assigned at pawn creation and persisted in save files.
        /// Stable across save/load cycles, unlike ThingID (string) or loadID (session-scoped).
        /// </summary>
        public readonly int PawnThingIDNumber;

        /// <summary>
        /// The hediff's def name from XML. Stable and session-independent.
        /// NOT loadID, which is re-assigned each session and cannot be used as a persistent key.
        /// </summary>
        public readonly string HediffDefName;

        /// <summary>
        /// The body part label from the def. Empty string for non-part-specific hediffs.
        /// Using Label (not LabelCap or LabelShort) for maximum stability across localization changes.
        /// </summary>
        public readonly string BodyPartLabel;

        /// <summary>
        /// Primary constructor — builds key directly from game objects.
        /// </summary>
        /// <param name="pawn">The pawn being tracked. Must not be null.</param>
        /// <param name="hediff">The hediff being tracked. Must not be null.</param>
        public HealingDictionaryKey(Pawn pawn, Hediff hediff)
        {
            PawnThingIDNumber = pawn.thingIDNumber;
            HediffDefName = hediff.def.defName;
            BodyPartLabel = hediff.Part?.Label ?? string.Empty;
        }

        /// <summary>
        /// Deserialization constructor — reconstructs key from its component values.
        /// Used by parallel-list deserialization in HediffHealingThresholdTracker.ExposeData().
        /// </summary>
        /// <param name="pawnId">Value from pawn.thingIDNumber saved in the parallel pawnIds list.</param>
        /// <param name="defName">Value from hediff.def.defName saved in the parallel defNames list.</param>
        /// <param name="partLabel">Value from hediff.Part?.Label saved in the parallel partLabels list.</param>
        public HealingDictionaryKey(int pawnId, string defName, string partLabel)
        {
            PawnThingIDNumber = pawnId;
            HediffDefName = defName ?? string.Empty;
            BodyPartLabel = partLabel ?? string.Empty;
        }

        /// <inheritdoc/>
        public bool Equals(HealingDictionaryKey other)
        {
            return PawnThingIDNumber == other.PawnThingIDNumber
                && HediffDefName == other.HediffDefName
                && BodyPartLabel == other.BodyPartLabel;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is HealingDictionaryKey other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = PawnThingIDNumber * 397;
                hash ^= HediffDefName != null ? HediffDefName.GetHashCode() : 0;
                hash ^= BodyPartLabel != null ? BodyPartLabel.GetHashCode() : 0;
                return hash;
            }
        }

        /// <summary>
        /// Returns a grep-friendly string representation: "DefName@PartLabel(PawnThingIDNumber)".
        /// Example: "Cut@LeftArm(42)" or "BloodLoss@(17)" for non-part hediffs.
        /// </summary>
        public override string ToString()
        {
            return $"{HediffDefName}@{BodyPartLabel}({PawnThingIDNumber})";
        }
    }
}
