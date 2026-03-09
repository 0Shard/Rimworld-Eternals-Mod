// file path: Eternal/Source/Eternal/UI/Management/ManagementTabView.cs
// Description: View layer for Eternal management tab. Pure UI rendering.

using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace Eternal.UI.Management
{
    /// <summary>
    /// View layer for Eternal management tab.
    /// Handles pure UI rendering, delegates all data access to Presenter.
    /// </summary>
    public class ManagementTabView : ITab
    {
        private readonly ManagementTabPresenter presenter;
        private const float TabWidth = 500f;

        protected virtual float TabHeight => 700f;

        public ManagementTabView(Pawn pawn)
        {
            var model = new ManagementTabModel(pawn);
            presenter = new ManagementTabPresenter(model);
        }

        protected override void FillTab()
        {
            if (presenter.Pawn == null)
                return;

            Rect scrollRect = new Rect(0f, 0f, TabWidth, TabHeight);
            Rect contentRect = new Rect(0f, 0f, TabWidth - 16f, TabHeight * 2f);

            var scrollPos = presenter.ScrollPosition;
            Widgets.BeginScrollView(scrollRect, ref scrollPos, contentRect);
            presenter.ScrollPosition = scrollPos;

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(contentRect);

            DrawPawnInfo(listing);
            DrawHealingStatusOverview(listing);
            DrawRegrowthProgress(listing);
            DrawHediffHealingProgress(listing);
            DrawScarHealingProgress(listing);
            DrawResourceConsumption(listing);
            DrawPerformanceMetrics(listing);
            DrawHealingConfiguration(listing);

            listing.End();
            Widgets.EndScrollView();
        }

        #region Section Drawing

        private void DrawPawnInfo(Listing_Standard listing)
        {
            listing.Label("Pawn Information");
            listing.Label($"Name: {presenter.PawnName}");
            listing.Label($"Age: {presenter.AgeBiologicalYears}");
            listing.Label($"Status: {presenter.PawnStatus}");
            listing.GapLine();
        }

        private void DrawHealingStatusOverview(Listing_Standard listing)
        {
            listing.Label("Healing Status Overview");

            if (presenter.HasHealingProcessor)
            {
                var healingStatus = presenter.GetHealingStatus();
                if (healingStatus.Count > 0)
                {
                    foreach (var kvp in healingStatus)
                    {
                        listing.Label($"{kvp.Key}: {kvp.Value}");
                    }
                }

                listing.Label($"Can Heal: {(presenter.CanPawnHeal() ? "Yes" : "No")}");
                listing.Label($"Food Debt: {presenter.FoodDebtDisplay}");
            }
            else
            {
                listing.Label("Healing system not available");
            }

            listing.GapLine();
        }

        private void DrawRegrowthProgress(Listing_Standard listing)
        {
            listing.Label("Regrowth Progress");

            if (presenter.HasActiveRegrowth)
            {
                listing.Label($"Active Parts: {presenter.ActivePartCount}");

                Rect progressRect = listing.GetRect(20f);
                Widgets.FillableBar(progressRect, presenter.OverallProgress, BaseContent.WhiteTex, Texture2D.linearGrayTexture, true);
                listing.Label($"Overall Progress: {presenter.OverallProgressDisplay}");

                foreach (var partData in presenter.GetPartProgressData())
                {
                    Rect labelRect = listing.GetRect(22f);
                    if (Mouse.IsOver(labelRect))
                    {
                        TooltipHandler.TipRegion(labelRect, partData.Tooltip);
                    }
                    Widgets.Label(labelRect, partData.DisplayText);
                }
            }
            else
            {
                listing.Label("No active regrowth process");
            }

            listing.GapLine();
        }

        private void DrawHediffHealingProgress(Listing_Standard listing)
        {
            listing.Label("Hediff Healing Progress");

            if (!presenter.HasBadHediffs)
            {
                listing.Label("No hediffs requiring healing");
                listing.GapLine();
                return;
            }

            foreach (var kvp in presenter.GetSortedHediffCategories())
            {
                listing.Label($"{kvp.Key} ({kvp.Value.Count}):");

                foreach (var hediff in kvp.Value.Take(5))
                {
                    float severityPercent = hediff.Severity * 100;
                    Rect hediffRect = listing.GetRect(16f);

                    Widgets.FillableBar(hediffRect, hediff.Severity, BaseContent.WhiteTex, Texture2D.grayTexture, true);
                    Widgets.Label(hediffRect, $"{hediff.def.LabelCap}: {severityPercent:F1}%");
                }

                if (kvp.Value.Count > 5)
                {
                    listing.Label($"... and {kvp.Value.Count - 5} more");
                }

                listing.Gap(6f);
            }

            listing.GapLine();
        }

        private void DrawScarHealingProgress(Listing_Standard listing)
        {
            listing.Label("Scar Healing Progress");

            const int displayLimit = 8;
            var records = presenter.GetScarHealingRecords(displayLimit).ToList();

            if (records.Count == 0)
            {
                listing.Label("No scars currently healing");
                listing.GapLine();
                return;
            }

            foreach (var record in records)
            {
                string scarName = record.Scar?.def?.LabelCap ?? "Unknown Scar";
                listing.Label($"{scarName}:");

                Rect progressRect = listing.GetRect(16f);
                Widgets.FillableBar(progressRect, record.HealingProgress, BaseContent.WhiteTex, Texture2D.linearGrayTexture, true);

                string progressLabel = $"{record.HealingProgress:P0} complete";
                if (record.EstimatedHealingTime > 0)
                {
                    double hoursRemaining = record.EstimatedHealingTime / 2500.0;
                    progressLabel += $" (~{hoursRemaining:F1}h)";
                }
                Widgets.Label(progressRect, progressLabel);

                listing.Label($"Severity: {record.InitialSeverity:F2}");
                listing.Gap(6f);
            }

            int extraCount = presenter.ExtraScarCount(displayLimit);
            if (extraCount > 0)
            {
                listing.Label($"... and {extraCount} more scars healing");
            }

            listing.GapLine();
        }

        private void DrawResourceConsumption(Listing_Standard listing)
        {
            listing.Label("Resource Consumption");

            if (presenter.HasNutritionData)
            {
                Rect nutritionRect = listing.GetRect(20f);
                Widgets.FillableBar(nutritionRect, presenter.NutritionPercent, BaseContent.WhiteTex, Texture2D.linearGrayTexture, true);
                listing.Label($"Nutrition: {presenter.NutritionDisplay}");
            }

            listing.Label("Resource consumption handled by unified heal amount");
            listing.GapLine();
        }

        private void DrawPerformanceMetrics(Listing_Standard listing)
        {
            listing.Label("Health Status");
            listing.Label("Simple healing system - no complex monitoring");
            listing.GapLine();
        }

        private void DrawHealingConfiguration(Listing_Standard listing)
        {
            listing.Label("Healing Configuration");
            listing.Label("Healing priorities can be configured in mod settings");

            if (listing.ButtonText("Open Mod Settings"))
            {
                presenter.OnOpenSettings();
            }
        }

        #endregion
    }
}
