// Relative Path: Eternal/Source/Eternal/Patches/PawnGenerator_Patch.cs
// Creation Date: 10-07-2026
// Last Edit: 10-07-2026
// Author: 0Shard
// Description: Harmony patch preventing NEWLY GENERATED non-player pawns (raiders, visitors,
//              quest pawns) from carrying the Eternal_GeneticMarker trait. The trait has
//              commonality 0 + allowOnHostileSpawn false, so vanilla never rolls it — this
//              guards against third-party mods that grant traits ignoring commonality.
//              Redressed world pawns (ex-colonist Eternals reused by the storyteller) are
//              intentionally NOT touched: GenerateNewPawnInternal only runs for new pawns.

using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Eternal.Exceptions;
using Eternal.Utils;

namespace Eternal.Patches
{
    /// <summary>
    /// Strips the Eternal trait (and its auto-added Eternal_Essence hediff) from newly
    /// generated pawns that do not belong to the player faction, logging the pawn kind and
    /// faction so the source of the illegitimate grant can be identified.
    /// </summary>
    [HarmonyPatch(typeof(PawnGenerator))]
    [HarmonyPatch("GenerateNewPawnInternal")]
    public static class PawnGenerator_GenerateNewPawnInternal_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __result)
        {
            try
            {
                // SAFE-09: skip all processing when mod is disabled due to missing critical defs.
                if (EternalModState.IsDisabled)
                    return;

                if (__result?.story?.traits == null || EternalDefOf.Eternal_GeneticMarker == null)
                    return;

                // Player-faction pawns may legitimately carry the trait (scenario forced
                // traits, dev tools); everything else spawning with it is a leak.
                if (__result.Faction != null && __result.Faction == Faction.OfPlayerSilentFail)
                    return;

                var eternalTrait = __result.story.traits.GetTrait(EternalDefOf.Eternal_GeneticMarker);
                if (eternalTrait == null)
                    return;

                __result.story.traits.RemoveTrait(eternalTrait);

                // TraitSet_GainTrait_Patch auto-adds the essence hediff on GainTrait — remove it too
                var essenceHediff = EternalDefOf.Eternal_Essence != null
                    ? __result.health?.hediffSet?.GetFirstHediffOfDef(EternalDefOf.Eternal_Essence)
                    : null;
                if (essenceHediff != null)
                {
                    __result.health.RemoveHediff(essenceHediff);
                }

                Log.Warning($"[Eternal] Stripped Eternal_GeneticMarker from newly generated pawn " +
                    $"'{__result.Name?.ToStringShort ?? "Unknown"}' (kind: {__result.kindDef?.defName ?? "null"}, " +
                    $"faction: {__result.Faction?.Name ?? "none"}). The trait has commonality 0 — " +
                    $"another mod likely granted it while ignoring commonality.");
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "PawnGenerator_GenerateNewPawnInternal_Patch.Postfix", null, ex);
            }
        }
    }
}
