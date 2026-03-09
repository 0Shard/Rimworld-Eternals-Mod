// Relative Path: Eternal/Source/Eternal/UI/Management/ManagementTabModel.cs
// Creation Date: 01-01-2026
// Last Edit: 08-01-2026
// Author: 0Shard
// Description: Model layer for Eternal management tab. Handles data access and calculations.

using System.Collections.Generic;
using System.Linq;
using Verse;
using Eternal.DI;
using Eternal.Extensions;
using Eternal.Healing;

namespace Eternal.UI.Management
{
    /// <summary>
    /// Model layer for Eternal management tab.
    /// Handles data access, hediff categorization, and progress calculations.
    /// </summary>
    public class ManagementTabModel
    {
        private readonly Pawn pawn;

        public ManagementTabModel(Pawn pawn)
        {
            this.pawn = pawn;
        }

        #region Pawn Information

        /// <summary>
        /// Gets the pawn being managed.
        /// </summary>
        public Pawn Pawn => pawn;

        /// <summary>
        /// Gets whether the pawn has the Eternal trait (ignoring gene suppression).
        /// </summary>
        public bool IsEternal => pawn.HasTraitIgnoringSuppression(EternalDefOf.Eternal_GeneticMarker);

        /// <summary>
        /// Gets the pawn's biological age.
        /// </summary>
        public int AgeBiologicalYears => pawn?.ageTracker?.AgeBiologicalYears ?? 0;

        #endregion

        #region Healing Status

        /// <summary>
        /// Gets the healing processor instance.
        /// </summary>
        public EternalHealingProcessor HealingProcessor => EternalServiceContainer.Instance.HealingProcessor;

        /// <summary>
        /// Gets the Eternal hediff from the pawn.
        /// </summary>
        public Eternal_Hediff EternalHediff =>
            pawn?.health?.hediffSet?.GetFirstHediffOfDef(EternalDefOf.Eternal_Essence) as Eternal_Hediff;

        /// <summary>
        /// Gets the pawn's current food debt.
        /// </summary>
        public float FoodDebt
        {
            get
            {
                var foodDebtSystem = HealingProcessor?.FoodDebtSystem;
                return foodDebtSystem?.GetDebt(pawn) ?? 0f;
            }
        }

        /// <summary>
        /// Checks if the pawn can currently heal.
        /// </summary>
        public bool CanPawnHeal()
        {
            if (pawn == null || pawn.health == null || pawn.Dead)
                return false;

            if (pawn.needs?.food != null && pawn.needs.food.CurLevel < 0.1f)
                return false;

            if (pawn.health.Downed || pawn.health.InPainShock)
                return false;

            var foodDebtSystem = HealingProcessor?.FoodDebtSystem;
            if (foodDebtSystem?.HasExcessiveDebt(pawn) == true)
                return false;

            return true;
        }

        #endregion

        #region Hediff Data

        /// <summary>
        /// Gets all bad hediffs grouped by category.
        /// </summary>
        public Dictionary<string, List<Hediff>> GetHediffsByCategory()
        {
            var result = new Dictionary<string, List<Hediff>>();

            if (pawn?.health?.hediffSet == null)
                return result;

            var badHediffs = pawn.health.hediffSet.hediffs.Where(h => h.def.isBad).ToList();

            foreach (var hediff in badHediffs)
            {
                string category = GetHediffCategory(hediff);
                if (!result.ContainsKey(category))
                {
                    result[category] = new List<Hediff>();
                }
                result[category].Add(hediff);
            }

            return result;
        }

        /// <summary>
        /// Gets the category name for a hediff.
        /// </summary>
        public string GetHediffCategory(Hediff hediff)
        {
            if (hediff == null)
                return "Unknown";

            if (hediff.def.injuryProps != null)
            {
                if (hediff.IsPermanent())
                    return "Permanent Injuries";
                return "Injuries";
            }

            if (hediff.def.lethalSeverity > 0)
                return "Diseases";

            if (hediff is Hediff_MissingPart)
                return "Missing Parts";

            if (hediff.def.defName.Contains("scar") || hediff.def.defName.Contains("Scar"))
                return "Scars";

            if (hediff.def.tendable)
                return "Treatable Conditions";

            return "Other Conditions";
        }

        /// <summary>
        /// Gets priority value for a hediff category (lower = higher priority).
        /// </summary>
        public int GetCategoryPriority(string category)
        {
            return category switch
            {
                "Diseases" => 0,
                "Permanent Injuries" => 1,
                "Missing Parts" => 2,
                "Injuries" => 3,
                "Scars" => 4,
                "Treatable Conditions" => 5,
                "Other Conditions" => 6,
                _ => 99
            };
        }

        #endregion

        #region Scar Healing

        /// <summary>
        /// Gets scar healing records for the pawn.
        /// </summary>
        public List<ScarHealingRecord> GetScarHealingRecords()
        {
            var records = new List<ScarHealingRecord>();

            if (pawn?.health?.hediffSet == null)
                return records;

            var scarsAndInjuries = pawn.health.hediffSet.hediffs
                .Where(h => (h.def.injuryProps != null && h.IsPermanent()) ||
                           (h.def.defName.Contains("scar") || h.def.defName.Contains("Scar")))
                .ToList();

            foreach (var scar in scarsAndInjuries)
            {
                var record = new ScarHealingRecord
                {
                    Scar = scar,
                    Pawn = pawn,
                    InitialSeverity = scar.Severity,
                    HealingProgress = 0f,
                    IsHealing = true,
                    LastHealedTick = Find.TickManager.TicksGame,
                    EstimatedHealingTime = CalculateEstimatedHealingTime(scar)
                };

                records.Add(record);
            }

            return records.OrderByDescending(r => r.InitialSeverity).ToList();
        }

        /// <summary>
        /// Calculates estimated healing time for a hediff.
        /// </summary>
        public float CalculateEstimatedHealingTime(Hediff hediff)
        {
            if (hediff == null)
                return 0f;

            float baseTime = 60000f; // 1 day in ticks
            float severityMultiplier = hediff.Severity * 2f;
            float complexityMultiplier = 1f;

            if (hediff.def.injuryProps != null)
            {
                complexityMultiplier = hediff.IsPermanent() ? 3f : 1.5f;
            }

            return baseTime * severityMultiplier * complexityMultiplier;
        }

        #endregion

        #region Regrowth Data

        /// <summary>
        /// Gets the regrowth state for the pawn.
        /// </summary>
        public EternalRegrowthState RegrowthState =>
            Eternal_Component.Instance?.RegrowthManager?.GetRegrowthState(pawn);

        /// <summary>
        /// Calculates overall regrowth progress.
        /// </summary>
        public float CalculateOverallProgress(EternalRegrowthState state)
        {
            if (state == null || state.partPhases.Count == 0)
                return 1.0f;

            float totalProgress = 0f;
            foreach (var kvp in state.partPhases)
            {
                var part = kvp.Key;
                var phase = kvp.Value;
                var progress = state.partProgress.ContainsKey(part) ? state.partProgress[part] : 0f;

                float phaseProgress = phase switch
                {
                    RegrowthPhase.InitialFormation => progress * 0.25f,
                    RegrowthPhase.TissueDevelopment => 0.25f + (progress * 0.25f),
                    RegrowthPhase.NerveIntegration => 0.50f + (progress * 0.25f),
                    RegrowthPhase.FunctionalCompletion => 0.75f + (progress * 0.25f),
                    RegrowthPhase.Complete => 1.0f,
                    _ => 0f
                };

                totalProgress += phaseProgress;
            }

            return totalProgress / state.partPhases.Count;
        }

        /// <summary>
        /// Gets tooltip text explaining dependency status.
        /// </summary>
        public string GetDependencyTooltip(BodyPartRecord part, RegrowthPhase phase, EternalRegrowthState state)
        {
            var tooltipText = EternalRegrowthState.GetPhaseSimpleName(phase) + "\n";

            if (phase == RegrowthPhase.Complete)
            {
                tooltipText += "Status: Complete\n";
                tooltipText += "This body part has been fully restored.";
            }
            else
            {
                if (!state.CanUpdatePart(part))
                {
                    if (part.parent != null && state.partPhases.ContainsKey(part.parent))
                    {
                        tooltipText += "Status: Waiting for parent part\n";
                        tooltipText += "Must wait for " + part.parent.def.defName + " to complete regrowth.";
                    }
                    else if (part.parent != null && state.pawn.health.hediffSet.PartIsMissing(part.parent))
                    {
                        tooltipText += "Status: Parent part missing\n";
                        tooltipText += "Cannot begin regrowth until parent part is restored.";
                    }
                }
                else
                {
                    tooltipText += "Status: Actively regrowing\n";
                    tooltipText += "This part is currently in " + EternalRegrowthState.GetPhaseSimpleName(phase) + ".";
                }
            }

            tooltipText += "\nProgress: " + (state.partProgress.TryGetValue(part, out float prog) ? prog.ToString("P0") : "0%");

            return tooltipText;
        }

        #endregion

        #region Resource Data

        /// <summary>
        /// Gets current nutrition level.
        /// </summary>
        public float NutritionLevel => pawn?.needs?.food?.CurLevel ?? 0f;

        /// <summary>
        /// Gets maximum nutrition level.
        /// </summary>
        public float NutritionMax => pawn?.needs?.food?.MaxLevel ?? 1f;

        /// <summary>
        /// Gets nutrition percentage.
        /// </summary>
        public float NutritionPercent => NutritionLevel / NutritionMax;

        #endregion
    }
}
