// file path: Eternal/Source/Eternal/UI/Management/ManagementTabPresenter.cs
// Description: Presenter layer for Eternal management tab. Manages state and coordinates between View and Model.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Eternal.UI.Management
{
    /// <summary>
    /// Presenter layer for Eternal management tab.
    /// Manages scroll state and provides formatted data to the view.
    /// </summary>
    public class ManagementTabPresenter
    {
        private readonly ManagementTabModel model;

        #region UI State

        public Vector2 ScrollPosition { get; set; } = Vector2.zero;

        #endregion

        public ManagementTabPresenter(ManagementTabModel model)
        {
            this.model = model;
        }

        #region Pawn Information

        public Pawn Pawn => model.Pawn;

        public string PawnName => model.Pawn?.Name?.ToStringShort ?? "Unknown";

        public string PawnStatus => model.IsEternal ? "Eternal" : "Not Eternal";

        public int AgeBiologicalYears => model.AgeBiologicalYears;

        #endregion

        #region Healing Status

        public bool CanPawnHeal() => model.CanPawnHeal();

        public float FoodDebt => model.FoodDebt;

        public bool HasFoodDebt => FoodDebt > 0;

        public string FoodDebtDisplay => HasFoodDebt ? $"{FoodDebt:F1} nutrition" : "None";

        /// <summary>
        /// Gets healing status from Eternal hediff if available.
        /// </summary>
        public Dictionary<string, object> GetHealingStatus()
        {
            var eternalHediff = model.EternalHediff;
            if (eternalHediff != null)
            {
                return eternalHediff.GetHealingStatus();
            }
            return new Dictionary<string, object>();
        }

        public bool HasHealingProcessor => model.HealingProcessor != null;

        #endregion

        #region Hediff Data

        /// <summary>
        /// Gets hediff categories sorted by priority.
        /// </summary>
        public IEnumerable<KeyValuePair<string, List<Hediff>>> GetSortedHediffCategories()
        {
            return model.GetHediffsByCategory()
                .OrderBy(x => model.GetCategoryPriority(x.Key));
        }

        public bool HasBadHediffs => GetSortedHediffCategories().Any();

        #endregion

        #region Scar Healing

        /// <summary>
        /// Gets scar healing records (limited for display).
        /// </summary>
        public IEnumerable<ScarHealingRecord> GetScarHealingRecords(int limit = 8)
        {
            return model.GetScarHealingRecords().Take(limit);
        }

        public int TotalScarCount => model.GetScarHealingRecords().Count;

        public int ExtraScarCount(int displayLimit) => TotalScarCount > displayLimit ? TotalScarCount - displayLimit : 0;

        #endregion

        #region Regrowth Data

        public EternalRegrowthState RegrowthState => model.RegrowthState;

        public bool HasActiveRegrowth => RegrowthState != null && RegrowthState.partPhases.Count > 0;

        public int ActivePartCount => RegrowthState?.partPhases.Count ?? 0;

        public float OverallProgress => model.CalculateOverallProgress(RegrowthState);

        public string OverallProgressDisplay => $"{OverallProgress:P0}";

        /// <summary>
        /// Gets part progress data for display.
        /// </summary>
        public IEnumerable<PartProgressData> GetPartProgressData()
        {
            var state = RegrowthState;
            if (state == null)
                yield break;

            foreach (var kvp in state.partPhases)
            {
                var part = kvp.Key;
                var phase = kvp.Value;
                var progress = state.partProgress.ContainsKey(part) ? state.partProgress[part] : 0f;

                yield return new PartProgressData
                {
                    Part = part,
                    Phase = phase,
                    Progress = progress,
                    DisplayText = EternalRegrowthState.GetPhaseDisplayName(phase, progress),
                    Tooltip = model.GetDependencyTooltip(part, phase, state)
                };
            }
        }

        #endregion

        #region Resource Data

        public float NutritionLevel => model.NutritionLevel;

        public float NutritionMax => model.NutritionMax;

        public float NutritionPercent => model.NutritionPercent;

        public string NutritionDisplay => $"{NutritionLevel:F1}/{NutritionMax:F1}";

        public bool HasNutritionData => model.Pawn?.needs?.food != null;

        #endregion

        #region Events

        /// <summary>
        /// Handles open settings button click.
        /// </summary>
        public void OnOpenSettings()
        {
            Log.Message("[Eternal] Mod settings requested - implementation may need update for current RimWorld version");
        }

        #endregion
    }

    /// <summary>
    /// Data transfer object for part progress display.
    /// </summary>
    public class PartProgressData
    {
        public BodyPartRecord Part { get; set; }
        public RegrowthPhase Phase { get; set; }
        public float Progress { get; set; }
        public string DisplayText { get; set; }
        public string Tooltip { get; set; }
    }
}
