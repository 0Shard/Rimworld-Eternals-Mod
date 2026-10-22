// Relative Path: Eternal/Source/Eternal/UI/Management/ManagementTabView.cs
// Creation Date: 12-10-2026
// Author: 0Shard
// Description: Shared Eternal inspect tab, rebound to the current selection with measured detail height.
using System;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;
using Eternal.Extensions;

namespace Eternal.UI.Management
{
    public class ManagementTabView : ITab
    {
        private Thing boundSelection;
        private ManagementTabPresenter presenter;
        private Vector2 scroll;
        private float contentHeight = 500f;
        private bool conditionsExpanded;
        private bool partsExpanded;

        public ManagementTabView()
        {
            size = new Vector2(560f, 520f);
            labelKey = "Eternal_TabManagement";
        }

        public static Pawn ResolvePawn(Thing selection) => selection as Pawn ?? (selection as Verse.Corpse)?.InnerPawn;
        public static bool IsEligible(Thing selection)
        {
            if (EternalModState.IsDisabled || EternalDefOf.Eternal_GeneticMarker == null)
            {
                return false;
            }

            return ResolvePawn(selection)?.IsValidEternalCorpse() == true;
        }
        public override bool IsVisible => IsEligible(SelThing);

        private void BindSelection(Thing selection)
        {
            if (ReferenceEquals(selection, boundSelection))
            {
                return;
            }

            boundSelection = selection;
            var pawn = ResolvePawn(selection);
            presenter = pawn == null ? null : new ManagementTabPresenter(new ManagementTabModel(pawn, selection as Verse.Corpse));
            ResetSections();
        }

        private void ResetSections()
        {
            scroll = Vector2.zero;
            conditionsExpanded = partsExpanded = false;
            contentHeight = 500f;
        }

        protected override void FillTab()
        {
            BindSelection(SelThing);
            if (presenter == null)
            {
                return;
            }

            var snapshot = presenter.Capture();
            var outer = new Rect(Vector2.zero, size).ContractedBy(10f);
            scroll.y = Mathf.Clamp(scroll.y, 0f, Math.Max(0f, contentHeight - outer.height));
            var content = new Rect(0f, 0f, outer.width - 16f, contentHeight);
            Widgets.BeginScrollView(outer, ref scroll, content);
            try
            {
                var listing = new Listing_Standard { maxOneColumn = true };
                listing.Begin(content);
                try
                {
                    DrawSnapshot(listing, snapshot);
                    contentHeight = listing.CurHeight + 20f;
                }
                finally
                {
                    listing.End();
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
            scroll.y = Mathf.Clamp(scroll.y, 0f, Math.Max(0f, contentHeight - outer.height));
        }

        private void DrawSnapshot(Listing_Standard listing, ManagementSnapshot snapshot)
        {
            listing.Label(snapshot.Name);
            LabelWithTooltip(listing, snapshot.Status, snapshot.Technical);
            listing.Label($"Conditions: {snapshot.Conditions.Count}    Regrowing parts: {snapshot.Parts.Count}");
            LabelWithTooltip(listing, "Food debt: " + presenter.Debt(snapshot),
                "Current debt: " + MenuValueCalculator.Nutrition(snapshot.Debt) + ". " + snapshot.CapacityDetails + "\n" + snapshot.RecoveryRate);
            string rates = snapshot.ProcessingRate;
            string rateTooltip = snapshot.ProcessingTooltip;
            if (snapshot.Conditions.Any(condition => condition.IsScar))
            {
                rates += "  |  " + snapshot.ScarRate;
                rateTooltip += "\n" + snapshot.ScarTooltip;
            }
            LabelWithTooltip(listing, rates, rateTooltip);
            if (!snapshot.Waived)
            {
                LabelWithTooltip(listing, snapshot.Headroom, snapshot.CapacityDetails);
            }
            if (snapshot.Corpse != null)
            {
                string qualification = snapshot.Corpse.Qualification +
                    $"\nQueued conditions: {snapshot.Corpse.ConditionCount}; queued part groups: {snapshot.Corpse.PartCount}";
                LabelWithTooltip(listing, "Resurrection cost: " + (snapshot.Corpse.Source == HealingQueueSource.Unavailable ? "Unavailable" : snapshot.Waived ? "Waived" :
                    (snapshot.Corpse.Qualified ? "Known estimate: " : "") + MenuValueCalculator.Nutrition(snapshot.Corpse.KnownNutrition)), qualification);
                LabelWithTooltip(listing, "Remaining time: " + MenuValueCalculator.Time(snapshot.Corpse.EstimatedTicks), qualification);
            }
            listing.GapLine();
            if (listing.ButtonText((conditionsExpanded ? "▼ " : "▶ ") + "Conditions and scars"))
            {
                conditionsExpanded = !conditionsExpanded;
            }

            if (conditionsExpanded)
            {
                if (snapshot.Conditions.Count == 0)
                {
                    listing.Label("No known conditions requiring healing");
                }

                foreach (var condition in snapshot.Conditions)
                {
                    LabelWithTooltip(listing, presenter.Condition(condition), presenter.ConditionTooltip(condition));
                }
            }
            if (listing.ButtonText((partsExpanded ? "▼ " : "▶ ") + "Body part regrowth"))
            {
                partsExpanded = !partsExpanded;
            }

            if (partsExpanded)
            {
                if (snapshot.Parts.Count == 0)
                {
                    listing.Label("No tracked regrowth progress");
                }

                foreach (var part in snapshot.Parts)
                {
                    listing.Label(presenter.Part(part));
                }

                if (snapshot.Corpse != null && snapshot.Parts.Count == 0)
                {
                    foreach (var work in snapshot.Corpse.Work)
                    {
                        if (work.IsRegrowth)
                        {
                            listing.Label(work.Label + " — waiting to start; progress unavailable");
                        }
                    }
                }
            }
        }

        private static void LabelWithTooltip(Listing_Standard listing, string text, string tooltip)
        {
            Rect rect = listing.Label(text);
            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }
        }
    }
}
