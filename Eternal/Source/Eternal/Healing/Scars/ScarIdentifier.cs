// file path: Eternal/Source/Eternal/Healing/Scars/ScarIdentifier.cs
// Description: Identifies and classifies scars and permanent injuries.

using System.Collections.Generic;
using Verse;
using Eternal.Extensions;

namespace Eternal.Healing.Scars
{
    /// <summary>
    /// Identifies and classifies scars and permanent injuries for healing.
    /// </summary>
    public static class ScarIdentifier
    {
        /// <summary>
        /// Gets all scars and permanent injuries for a pawn.
        /// </summary>
        public static List<Hediff> GetPawnScars(Pawn pawn)
        {
            var scars = new List<Hediff>();

            if (pawn?.health?.hediffSet == null)
                return scars;

            foreach (var hediff in pawn.health.hediffSet.hediffs)
            {
                if (hediff == null || hediff.def == EternalDefOf.Eternal_Essence)
                    continue;

                if (HediffUtility.IsPermanent(hediff) || IsScarHediff(hediff))
                {
                    scars.Add(hediff);
                }
            }

            return scars;
        }

        /// <summary>
        /// Checks if a hediff is considered a scar for healing purposes.
        /// </summary>
        public static bool IsScarHediff(Hediff hediff)
        {
            if (hediff == null)
                return false;

            string defName = hediff.def.defName.ToLower();

            // Check for common scar hediffs
            if (defName.Contains("scar") || defName.Contains("permanent"))
                return true;

            // Check for old wounds and injuries
            if (defName.Contains("old") && defName.Contains("wound"))
                return true;

            // Check for chronic pain and nerve damage
            if (defName.Contains("chronic") || defName.Contains("nerve"))
                return true;

            // Check for missing body parts (permanent injuries)
            if (hediff is Hediff_MissingPart)
                return true;

            return false;
        }

        /// <summary>
        /// Checks if a hediff is a high-severity scar.
        /// </summary>
        public static bool IsHighSeverityScar(Hediff hediff, float threshold = 0.8f)
        {
            return hediff?.Severity >= threshold;
        }

        /// <summary>
        /// Checks if a scar is on a critical body part.
        /// </summary>
        public static bool IsOnCriticalPart(Hediff hediff)
        {
            return hediff?.Part != null && hediff.Part.IsCritical();
        }

        /// <summary>
        /// Gets the category of a scar for display purposes.
        /// </summary>
        public static string GetScarCategory(Hediff hediff)
        {
            if (hediff == null)
                return "Unknown";

            if (hediff is Hediff_MissingPart)
                return "Missing Part";

            string defName = hediff.def.defName.ToLower();

            if (defName.Contains("scar"))
                return "Scar";

            if (defName.Contains("chronic"))
                return "Chronic Condition";

            if (defName.Contains("nerve"))
                return "Nerve Damage";

            if (HediffUtility.IsPermanent(hediff))
                return "Permanent Injury";

            return "Other";
        }
    }
}
