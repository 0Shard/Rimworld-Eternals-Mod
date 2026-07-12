// Relative Path: Eternal/Source/Eternal/Patches/HealthCardUtility_Patch.cs
// Creation Date: 12-07-2026
// Last Edit: 12-07-2026
// Author: 0Shard
// Description: Harmony patch for HealthCardUtility.VisibleHediffs to hide the
//              "missing body part" entry while a visible Eternal_Regrowing hediff
//              covers the same part. Display-only: the Hediff_MissingPart stays in
//              the pawn's health state until CompleteRegrowth() removes it.

using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Eternal.Patches
{
    /// <summary>
    /// Hides Hediff_MissingPart rows in the health tab while the part is actively
    /// regrowing, so the part row shows only "Regrowing (Stage X)".
    /// </summary>
    /// <remarks>
    /// VisibleHediffs yields missing parts from GetMissingPartsCommonAncestors()
    /// unconditionally (it never consults Hediff.Visible for them), so a pass-through
    /// postfix on the iterator is the only display-only seam.
    /// The regrowing hediff must itself be Visible: on corpses it hides itself below
    /// 0.5 essence, and hiding the missing part then would make the part look healthy.
    /// </remarks>
    [HarmonyPatch(typeof(HealthCardUtility), "VisibleHediffs")]
    public static class HealthCardUtility_VisibleHediffs_Patch
    {
        [HarmonyPostfix]
        public static IEnumerable<Hediff> HideMissingPartWhileRegrowing(IEnumerable<Hediff> __result, Pawn pawn)
        {
            List<Hediff> pawnHediffs = pawn?.health?.hediffSet?.hediffs;
            foreach (Hediff hediff in __result)
            {
                if (hediff is Hediff_MissingPart missingPart && pawnHediffs != null
                    && IsPartRegrowing(pawnHediffs, missingPart.Part))
                {
                    continue;
                }
                yield return hediff;
            }
        }

        private static bool IsPartRegrowing(List<Hediff> pawnHediffs, BodyPartRecord part)
        {
            if (part == null)
                return false;

            for (int i = 0; i < pawnHediffs.Count; i++)
            {
                if (pawnHediffs[i] is EternalRegrowing_Hediff regrowing
                    && (regrowing.Part == part || regrowing.forPart == part)
                    && regrowing.Visible)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
