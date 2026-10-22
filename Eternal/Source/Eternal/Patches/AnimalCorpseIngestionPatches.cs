using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Eternal.Corpse;

namespace Eternal.Patches
{
    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.WillEat), new[] { typeof(Pawn), typeof(Thing), typeof(Pawn), typeof(bool), typeof(bool) })]
    public static class AnimalCorpseWillEatPatch
    {
        public static void Postfix(Pawn p, Thing food, ref bool __result)
        {
            if (__result && AnimalCorpseProtection.ShouldBlock(p, food))
            {
                __result = false;
            }
        }
    }

    internal static class AnimalCorpseToilProtection
    {
        private static bool Blocked(Pawn eater, TargetIndex target)
            => AnimalCorpseProtection.ShouldBlock(eater, eater?.CurJob?.GetTarget(target).Thing);

        public static void Attach(Toil toil, Pawn eater, TargetIndex target)
        {
            if (toil == null)
            {
                return;
            }
            toil.AddFailCondition(() => Blocked(eater, target));
            Action original = toil.initAction;
            toil.initAction = () =>
            {
                if (Blocked(eater, target))
                {
                    eater.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }
                original?.Invoke();
            };
        }
    }

    [HarmonyPatch(typeof(Toils_Ingest), nameof(Toils_Ingest.ChewIngestible), new[] { typeof(Pawn), typeof(float), typeof(TargetIndex), typeof(TargetIndex) })]
    public static class AnimalCorpseChewPatch
    {
        public static void Postfix(Pawn chewer, TargetIndex ingestibleInd, Toil __result)
            => AnimalCorpseToilProtection.Attach(__result, chewer, ingestibleInd);
    }

    [HarmonyPatch(typeof(Toils_Ingest), nameof(Toils_Ingest.FinalizeIngest), new[] { typeof(Pawn), typeof(TargetIndex) })]
    public static class AnimalCorpseFinalizePatch
    {
        public static void Postfix(Pawn ingester, TargetIndex ingestibleInd, Toil __result)
            => AnimalCorpseToilProtection.Attach(__result, ingester, ingestibleInd);
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.Ingested), new[] { typeof(Pawn), typeof(float) })]
    public static class AnimalCorpseIngestedPatch
    {
        public static bool Prefix(Thing __instance, Pawn ingester, ref float __result)
        {
            if (!AnimalCorpseProtection.ShouldBlock(ingester, __instance))
            {
                return true;
            }
            __result = 0f;
            return false;
        }
    }
}
