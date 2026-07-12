// Relative Path: Eternal/Source/Eternal/Patches/KidnapAIUtility_Patch.cs
// Creation Date: 13-07-2026
// Last Edit: 13-07-2026
// Author: 0Shard
// Description: Harmony patch vetoing Eternal pawns as kidnap victims. Feeds Eternals into
//              TryFindGoodKidnapVictim's own disallowed list so raiders transparently pick
//              the next-best non-Eternal victim instead of aborting the kidnap behavior.

using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Eternal.Exceptions;
using Eternal.Extensions;
using Eternal.Utils;

namespace Eternal.Patches
{
    /// <summary>
    /// Prevents raiders from ever selecting an Eternal pawn as a kidnap victim.
    /// The vanilla victim validator rejects any pawn contained in the disallowed list,
    /// so prepending Eternals there vetoes them without touching the validator lambda.
    /// Known gap: the passive map-closure kidnap (MapDeiniter) does not run this method.
    /// </summary>
    [HarmonyPatch(typeof(KidnapAIUtility))]
    [HarmonyPatch(nameof(KidnapAIUtility.TryFindGoodKidnapVictim))]
    public static class KidnapAIUtility_TryFindGoodKidnapVictim_Patch
    {
        /// <summary>
        /// Adds every spawned Eternal on the kidnapper's map to the disallowed list.
        /// Parameter names MUST match the target method's parameters
        /// (TryFindGoodKidnapVictim(Pawn kidnapper, float maxDist, out Pawn victim,
        /// List&lt;Thing&gt; disallowed)) — Harmony binds by name and throws at patch time
        /// on a mismatch, which aborts patching for this class.
        /// </summary>
        [HarmonyPrefix]
        public static void Prefix(Pawn kidnapper, ref List<Thing> disallowed)
        {
            try
            {
                if (EternalModState.IsDisabled)
                    return;

                var mapPawns = kidnapper?.Map?.mapPawns?.AllPawnsSpawned;
                if (mapPawns == null)
                    return;

                for (int i = 0; i < mapPawns.Count; i++)
                {
                    Pawn candidate = mapPawns[i];
                    if (!candidate.IsValidEternal())
                        continue;

                    if (disallowed == null)
                        disallowed = new List<Thing>();

                    if (!disallowed.Contains(candidate))
                        disallowed.Add(candidate);
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "KidnapAIUtility_TryFindGoodKidnapVictim_Patch.Prefix", null, ex);
            }
        }
    }
}
