// Relative Path: Eternal/Source/Eternal/Settings/HediffSettingSlim.cs
// Creation Date: 03-01-2026
// Last Edit: 16-07-2026
// Author: 0Shard
// Description: Minimal v2 Hediff settings record for XML persistence. It stores only the
//              active per-hediff controls: defName, canHeal, healingRate, and noThreshold.
//              Legacy nutritionCost input is handled and discarded by the v1 migrator.

using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Eternal.Settings
{
    /// <summary>
    /// Minimal v2 Hediff setting for XML persistence.
    /// </summary>
    public class HediffSettingSlim : IExposable
    {
        /// <summary>
        /// Sentinel value indicating that the global baseHealingRate is used.
        /// </summary>
        public const float USE_GLOBAL_RATE = SettingsDefaults.HediffHealingRateUseGlobal;

        /// <summary>
        /// The defName of the hediff this setting applies to.
        /// </summary>
        public string defName = "";

        /// <summary>
        /// Whether this hediff should be healed by Eternals.
        /// </summary>
        public bool canHeal = SettingsDefaults.HediffCanHeal;

        /// <summary>
        /// Custom healing rate; USE_GLOBAL_RATE means use the global setting.
        /// </summary>
        public float healingRate = USE_GLOBAL_RATE;

        /// <summary>
        /// Whether this hediff bypasses the activation threshold.
        /// </summary>
        public bool noThreshold = SettingsDefaults.HediffNoThreshold;

        public HediffSettingSlim()
        {
        }

        /// <summary>
        /// Creates a v2 record from the full in-memory setting.
        /// </summary>
        public HediffSettingSlim(string defName, EternalHediffSetting fullSetting)
        {
            this.defName = defName;
            canHeal = fullSetting.canHeal;
            healingRate = fullSetting.healingRate;
            noThreshold = fullSetting.noThreshold;
        }

        /// <summary>
        /// Applies the v2 record to the full in-memory setting.
        /// </summary>
        public void ApplyTo(EternalHediffSetting fullSetting)
        {
            fullSetting.canHeal = canHeal;
            fullSetting.healingRate = healingRate;
            fullSetting.noThreshold = noThreshold;
        }

        public bool HasCustomHealingRate => healingRate != USE_GLOBAL_RATE;

        /// <summary>
        /// Validates the custom rate while preserving the global-rate sentinel.
        /// </summary>
        public bool Validate(List<string> warnings)
        {
            if (healingRate == USE_GLOBAL_RATE)
                return false;

            float clamped = Mathf.Clamp(
                healingRate,
                SettingsDefaults.HediffHealingRateMin,
                SettingsDefaults.HediffHealingRateMax);
            if (clamped == healingRate)
                return false;

            warnings?.Add(
                $"'{defName}'.healingRate: {healingRate} clamped to {clamped} " +
                $"(valid: {SettingsDefaults.HediffHealingRateMin}-{SettingsDefaults.HediffHealingRateMax})");
            healingRate = clamped;
            return true;
        }

        /// <summary>
        /// Serializes the v2 schema. There is intentionally no nutritionCost field here.
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref defName, "defName", "");
            Scribe_Values.Look(ref canHeal, "canHeal", SettingsDefaults.HediffCanHeal);
            Scribe_Values.Look(ref healingRate, "healingRate", SettingsDefaults.HediffHealingRateUseGlobal);
            Scribe_Values.Look(ref noThreshold, "noThreshold", SettingsDefaults.HediffNoThreshold);
        }

        public override string ToString()
        {
            return $"HediffSettingSlim({defName}): canHeal={canHeal}, rate={healingRate}, noThreshold={noThreshold}";
        }
    }
}
