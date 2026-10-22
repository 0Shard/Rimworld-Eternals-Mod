using Verse;
using Eternal.Extensions;

namespace Eternal.Corpse
{
    public static class AnimalCorpseProtectionPolicy
    {
        public static bool ShouldBlock(bool animal, bool corpse, bool dead, bool eternal)
        {
            return animal && corpse && dead && eternal;
        }
    }

    public static class AnimalCorpseProtection
    {
        public static bool ShouldBlock(Pawn eater, Thing target)
        {
            var corpse = target as Verse.Corpse;
            var pawn = corpse?.InnerPawn;
            return AnimalCorpseProtectionPolicy.ShouldBlock(eater?.RaceProps?.Animal == true, corpse != null,
                pawn?.Dead == true, EternalDefOf.Eternal_GeneticMarker != null &&
                pawn.HasTraitIgnoringSuppression(EternalDefOf.Eternal_GeneticMarker));
        }
    }
}
