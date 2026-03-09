// Relative Path: Eternal/Source/Eternal/Hediffs/EternalRegrowing_Hediff.cs
// Creation Date: 28-10-2025
// Last Edit: 07-01-2026
// Author: 0Shard
// Description: Hediff for tracking body part regrowth using the Immortals pattern.
//              Stores forPart and partMaxHp, severity progresses 0->1, uses partEfficiencyOffset stages.

using System;
using Verse;
using Eternal.Compat;

namespace Eternal
{
    /// <summary>
    /// Simplified regrowth hediff following Immortals mod pattern.
    /// Severity progresses from 0 to 1, with 4 stages having partEfficiencyOffset.
    /// When severity >= 1.0, the hediff is removed and part becomes functional.
    /// </summary>
    /// <remarks>
    /// Key design decisions:
    /// - Extends Hediff (not HediffWithComps) for simplicity
    /// - NO Tick override - regrowth progression is driven externally by EternalRegrowthManager
    /// - Uses Scribe_BodyParts.Look for proper save/load of BodyPartRecord
    /// - partEfficiencyOffset stages in XML make the part non-functional during regrowth
    /// </remarks>
    public class EternalRegrowing_Hediff : Hediff
    {
        /// <summary>The body part being regrown.</summary>
        public BodyPartRecord forPart;

        /// <summary>Max HP for this part (used for severity label display).</summary>
        public float partMaxHp;

        /// <summary>
        /// Shows regrowth progress as "15.0/30 (Left Arm)"
        /// </summary>
        public override string SeverityLabel
        {
            get
            {
                if (forPart != null)
                {
                    float currentHp = severityInt * partMaxHp;
                    return $"{currentHp:F1}/{partMaxHp:F0} ({forPart.Label})";
                }
                return $"{severityInt:P0}";
            }
        }

        /// <summary>
        /// Visible on living pawns always, on corpses only if Eternal essence > 0.5
        /// </summary>
        public override bool Visible
        {
            get
            {
                if (!pawn.Dead) return true;

                var essenceHediff = pawn.health?.hediffSet?.GetFirstHediffOfDef(EternalDefOf.Eternal_Essence);
                return essenceHediff != null && essenceHediff.Severity > 0.5f;
            }
        }

        /// <summary>
        /// Initialize with the body part and calculate max HP.
        /// Call this AFTER adding the hediff to set up forPart and partMaxHp.
        /// </summary>
        /// <param name="part">The body part being regrown</param>
        /// <param name="ownerPawn">The pawn owning this body part</param>
        public void Initialize(BodyPartRecord part, Pawn ownerPawn)
        {
            forPart = part;
            partMaxHp = EBFCompat.GetMaxHealth(part, ownerPawn);
            if (partMaxHp <= 0f) partMaxHp = 1f;
        }

        /// <summary>
        /// Save/load forPart and partMaxHp.
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref partMaxHp, "partMaxHp", 1f);
            Scribe_BodyParts.Look(ref forPart, "forPart", null);
        }

        /// <summary>
        /// Never merge regrowth hediffs - each tracks a specific body part.
        /// </summary>
        /// <param name="other">The hediff attempting to merge</param>
        /// <returns>Always false</returns>
        public override bool TryMergeWith(Hediff other)
        {
            return false;
        }
    }
}
