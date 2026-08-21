/*
 * Relative Path: Eternal/Source/Eternal/Infrastructure/HealingDictionaryKey.cs
 * Creation Date: 19-02-2026
 * Last Edit: 16-07-2026
 * Author: 0Shard
 * Description: Composite struct key for healing dictionaries.
 *              Replaces per-tick string allocations in hot healing paths (PERF-04).
 *              Four-field identity preserves duplicate same-def/same-part hediff instances:
 *                - PawnThingIDNumber: stable across save/load (assigned at birth, persists in save file)
 *                - HediffDefName: stable XML def name
 *                - BodyPartLabel: stable from def, empty string for non-part-specific hediffs
 *                - HediffLoadID: RimWorld's persisted per-instance hediff identity
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
        /// </summary>
        public readonly string HediffDefName;

        /// <summary>
        /// The body part label from the def. Empty string for non-part-specific hediffs.
        /// Using Label (not LabelCap or LabelShort) for maximum stability across localization changes.
        /// </summary>
        public readonly string BodyPartLabel;

        /// <summary>
        /// RimWorld's persisted identity for this hediff instance. A negative value marks a
        /// legacy key that predates instance-aware healing state.
        /// </summary>
        public readonly int HediffLoadID;

        /// <summary>
        /// C#-style alias retained for callers that use Id rather than ID naming.
        /// </summary>
        public int HediffLoadId => HediffLoadID;

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
            HediffLoadID = hediff.loadID;
        }

        /// <summary>
        /// Deserialization constructor — reconstructs key from its component values.
        /// Used by parallel-list deserialization in HediffHealingThresholdTracker.ExposeData().
        /// </summary>
        /// <param name="pawnId">Value from pawn.thingIDNumber saved in the parallel pawnIds list.</param>
        /// <param name="defName">Value from hediff.def.defName saved in the parallel defNames list.</param>
        /// <param name="partLabel">Value from hediff.Part?.Label saved in the parallel partLabels list.</param>
        /// <param name="hediffLoadId">Value from Hediff.loadID saved in the parallel load-ID list.</param>
        public HealingDictionaryKey(int pawnId, string defName, string partLabel, int hediffLoadId)
        {
            PawnThingIDNumber = pawnId;
            HediffDefName = defName ?? string.Empty;
            BodyPartLabel = partLabel ?? string.Empty;
            HediffLoadID = hediffLoadId;
        }

        /// <summary>
        /// Legacy deserialization constructor. The negative load ID deliberately remains
        /// instance-ambiguous and must be resolved or discarded during live reconciliation.
        /// </summary>
        public HealingDictionaryKey(int pawnId, string defName, string partLabel)
            : this(pawnId, defName, partLabel, -1)
        {
        }

        /// <inheritdoc/>
        public bool Equals(HealingDictionaryKey other)
        {
            return PawnThingIDNumber == other.PawnThingIDNumber
                && HediffDefName == other.HediffDefName
                && BodyPartLabel == other.BodyPartLabel
                && HediffLoadID == other.HediffLoadID;
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
                hash = (hash * 397) ^ HediffLoadID;
                return hash;
            }
        }

        /// <summary>
        /// Returns a grep-friendly string representation: "DefName@PartLabel#HediffLoadID(PawnThingIDNumber)".
        /// Example: "Cut@LeftArm#101(42)" or "BloodLoss@#102(17)" for non-part hediffs.
        /// </summary>
        public override string ToString()
        {
            return $"{HediffDefName}@{BodyPartLabel}#{HediffLoadID}({PawnThingIDNumber})";
        }
    }
}
