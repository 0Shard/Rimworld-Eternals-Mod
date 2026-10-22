using System.Linq;
using Verse;

namespace Eternal.Healing
{
    public static class HealingCostProfiles
    {
        public static HealingCostProfile Resolve(HediffDef def, EternalHediffSetting setting, float globalMultiplier, bool waived)
        {
            if (def == null)
            {
                return null;
            }
            bool regrowth = def.defName == "Eternal_Regrowing";
            bool workUnits = def.injuryProps != null || (def.hediffClass != null && typeof(Hediff_Injury).IsAssignableFrom(def.hediffClass)) || regrowth;
            return HealingWorkCostCalculator.CreateProfile(workUnits, def.chronic, regrowth ? 0f : def.minSeverity,
                def.maxSeverity, def.lethalSeverity,
                setting?.nutritionSeverityRangeOverride ?? SettingsDefaults.HediffNutritionSeverityRangeOverride,
                setting?.nutritionCostMultiplier ?? SettingsDefaults.HediffNutritionCostMultiplier,
                waived ? 0f : globalMultiplier, def.stages?.Select(s => s.minSeverity));
        }

        public static HealingCostProfile ForItem(HealingItem item, float multiplier, bool waived)
        {
            var def = item.Hediff?.def ?? (item.HediffDefName == null ? null : DefDatabase<HediffDef>.GetNamedSilentFail(item.HediffDefName));
            var setting = Eternal_Mod.GetSettings().hediffManager?.Store.GetExisting(def?.defName);
            return Resolve(def, setting, multiplier, waived);
        }
    }
}
