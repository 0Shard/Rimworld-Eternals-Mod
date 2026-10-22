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
            {
                return scars;
            }

            foreach (var hediff in pawn.health.hediffSet.hediffs)
            {
                if (hediff == null || hediff.def == EternalDefOf.Eternal_Essence)
                {
                    continue;
                }

                if (HediffHealingConfig.CanHealScar(hediff))
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
            => HediffHealingConfig.AllowsGenericHealing(hediff) && hediff.IsPermanentInjuryScar();

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
            {
                return "Unknown";
            }

            if (hediff is Hediff_MissingPart)
            {
                return "Missing Part";
            }

            string defName = hediff.def.defName.ToLower();

            if (defName.Contains("scar"))
            {
                return "Scar";
            }

            if (defName.Contains("chronic"))
            {
                return "Chronic Condition";
            }

            if (defName.Contains("nerve"))
            {
                return "Nerve Damage";
            }

            if (HediffUtility.IsPermanent(hediff))
            {
                return "Permanent Injury";
            }

            return "Other";
        }
    }
}
