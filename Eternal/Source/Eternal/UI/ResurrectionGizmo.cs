// Relative Path: Eternal/Source/Eternal/UI/ResurrectionGizmo.cs
// Creation Date: 12-10-2026
// Author: 0Shard
// Description: Current corpse confirmation with lazy detail, action-boundary recovery and truthful outcomes.
using System;
using UnityEngine;
using Verse;
using RimWorld;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Extensions;
using Eternal.Models;
using Eternal.Utils;
using Eternal.Healing;

namespace Eternal.UI
{
    public class ResurrectionGizmo : Command
    {
        private readonly Pawn originalPawn;
        private readonly Verse.Corpse corpse;
        private ResurrectionPreviewSnapshot snapshot;

        public ResurrectionGizmo(Pawn pawn, Verse.Corpse corpse = null)
        {
            originalPawn = pawn;
            this.corpse = corpse ?? pawn?.Corpse;
            defaultLabel = "Resurrect Eternal";
            defaultDesc = "Begin Eternal resurrection.";
            hotKey = KeyBindingDefOf.Misc1;
            Order = 1000f;
            icon = ContentFinder<Texture2D>.Get("UI/Gizmos/Eternal_Resurrection", false);
            Refresh();
        }

        public override bool Visible => !EternalModState.IsDisabled && originalPawn?.Dead == true &&
            corpse != null && !corpse.Destroyed && originalPawn.IsValidEternalCorpse();
        public override string Label => snapshot?.Status == CorpsePreviewStatus.Active ? "Resurrecting..." : defaultLabel;
        public override string Desc => snapshot == null ? "Resurrection information unavailable" : Summary(snapshot);

        protected override GizmoResult GizmoOnGUIInt(Rect butRect, GizmoRenderParms parms)
        {
            Refresh();
            return base.GizmoOnGUIInt(butRect, parms);
        }

        private ResurrectionPreviewSnapshot Refresh()
        {
            try
            {
                var services = EternalServiceContainer.Instance;
                var entry = originalPawn == null ? null : services.CorpseManager?.GetCorpseData(originalPawn);
                snapshot = ResurrectionPreview.Capture(originalPawn, corpse, entry, Eternal_Mod.GetSettings(),
                    services.CorpseHealingProcessor != null, originalPawn == null ? null : services.FoodDebtSystem?.GetDebt(originalPawn));
                bool available = originalPawn?.Dead == true && corpse != null && !corpse.Destroyed && corpse.InnerPawn == originalPawn;
                bool valid = available && EternalCorpseHealingProcessor.SupportsResurrection(corpse);
                disabled = !ResurrectionPreviewValues.CanStart(valid, services.CorpseManager != null && services.CorpseHealingProcessor != null,
                    entry?.IsHealingActive == true, EternalModState.IsDisabled);
                disabledReason = !available ? "Corpse is no longer available" : !valid ? "This corpse does not support resurrection" :
                    EternalModState.IsDisabled ? "Eternal is disabled" :
                    entry?.IsHealingActive == true ? "Resurrection already in progress" : "Resurrection system unavailable";
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection, "ResurrectionPreview", originalPawn, ex);
                snapshot = new ResurrectionPreviewSnapshot { Status = CorpsePreviewStatus.Unavailable };
                disabled = true;
                disabledReason = "Resurrection information unavailable";
            }
            return snapshot;
        }

        internal static string Summary(ResurrectionPreviewSnapshot current)
        {
            string cost = current.Waived ? "Waived (no food need)" : current.Source == HealingQueueSource.Unavailable ? "Unavailable" :
                (current.Qualified ? "Approximate known work: " : "") + MenuValueCalculator.Nutrition(current.KnownNutrition);
            return $"Remaining conditions: {current.ConditionCount}; regrowth groups: {current.PartCount}\n" +
                "Additional nutrition: " + cost + "\n" +
                "Incurred debt: " + MenuValueCalculator.Nutrition(current.IncurredDebt) + "\n" +
                "Remaining time: " + (current.Source == HealingQueueSource.Unavailable ? "Unavailable" : MenuValueCalculator.Time(current.EstimatedTicks)) +
                (current.Qualified ? "\n" + current.Qualification : "") +
                "\nAfter resurrection, debt repays through extra food-bar drain above the configured floor, given sufficient food.";
        }

        public override void ProcessInput(Event ev)
        {
            Refresh();
            if (disabled) { Failure(disabledReason); return; }
            Find.WindowStack.Add(new ResurrectionConfirmation(this));
        }

        private void StartResurrection()
        {
            try
            {
                Refresh();
                if (disabled || !Visible) { Failure(disabledReason); return; }
                var services = EternalServiceContainer.Instance;
                var manager = services.CorpseManager;
                var processor = services.CorpseHealingProcessor;
                var entry = manager?.GetCorpseData(originalPawn);
                // Recovery belongs to the explicit action boundary, never a repaint or tooltip.
                if (entry == null && manager != null)
                {
                    manager.RegisterCorpse(corpse, originalPawn, PawnAssignmentSnapshot.CaptureFrom(originalPawn));
                    entry = manager.GetCorpseData(originalPawn);
                }
                if (entry == null || processor == null || entry.OriginalPawn != originalPawn || entry.Corpse != corpse ||
                    !ResurrectionPreviewValues.CanStart(originalPawn.Dead && !corpse.Destroyed && corpse.InnerPawn == originalPawn,
                        true, entry.IsHealingActive, EternalModState.IsDisabled))
                {
                    Failure("Corpse or resurrection state changed; unable to start");
                    return;
                }
                bool started = processor.StartCorpseHealing(entry);
                var outcome = ResurrectionPreviewValues.StartOutcome(started, originalPawn.Dead, entry.IsHealingActive);
                if (outcome == ResurrectionStartOutcome.Rejected)
                {
                    Failure("Resurrection could not be started");
                }
                else
                {
                    Messages.Message(new Message(outcome == ResurrectionStartOutcome.Completed
                        ? originalPawn.LabelShortCap + " has resurrected."
                        : originalPawn.LabelShortCap + " has begun Eternal resurrection.", MessageTypeDefOf.PositiveEvent));
                }

                Refresh();
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection, "StartResurrection", originalPawn, ex);
                Failure("Failed to start resurrection; see the game log for details");
            }
        }

        private static void Failure(string reason) => Messages.Message(new Message(reason, MessageTypeDefOf.NegativeEvent));

        private sealed class ResurrectionConfirmation : Window
        {
            private readonly ResurrectionGizmo owner;
            private bool detailed;
            private Vector2 scroll;
            private float contentHeight = 500f;
            public override Vector2 InitialSize => new Vector2(580f, 460f);
            public ResurrectionConfirmation(ResurrectionGizmo owner)
            {
                this.owner = owner;
                doCloseX = true;
                absorbInputAroundWindow = true;
                forcePause = true;
            }

            public override void DoWindowContents(Rect inRect)
            {
                var current = owner.Refresh();
                var outer = new Rect(inRect.x, inRect.y, inRect.width, inRect.height - 45f);
                scroll.y = Mathf.Clamp(scroll.y, 0f, Math.Max(0f, contentHeight - outer.height));
                var content = new Rect(0f, 0f, outer.width - 16f, contentHeight);
                Widgets.BeginScrollView(outer, ref scroll, content);
                var listing = new Listing_Standard { maxOneColumn = true };
                listing.Begin(content);
                listing.Label("Resurrect " + (owner.originalPawn?.LabelShortCap ?? "Eternal") + "?");
                listing.Label(Summary(current));
                if (current.Source == HealingQueueSource.Unavailable && !owner.disabled)
                {
                    listing.Label("Tracking will be recovered when you confirm; a cost estimate is currently unavailable.");
                }

                if (owner.disabled)
                {
                    listing.Label(owner.disabledReason);
                }

                if (listing.ButtonText((detailed ? "▼ " : "▶ ") + "Remaining work details"))
                {
                    detailed = !detailed;
                }

                if (detailed)
                {
                    foreach (var work in current.Work)
                    {
                        listing.Label(work.Label + (work.IsRegrowth ? " — regrowth group" : " — " + MenuValueCalculator.Number(work.Severity) + " severity remaining"));
                        listing.Label(MenuValueCalculator.Pricing(work.Profile, work.Severity));
                        if (work.RegrowthEffort.HasValue)
                        {
                            listing.Label("Work estimate: " + MenuValueCalculator.Number(work.RegrowthEffort.Value) +
                                "; dependent subtree estimate: " + MenuValueCalculator.Time(work.RegrowthTicks ?? float.NaN));
                        }

                        if (work.ScalarOnly)
                        {
                            listing.Label("Saved remaining work; condition or part details unavailable");
                        }
                    }
                    if (current.Work.Count == 0)
                    {
                        listing.Label(current.Source == HealingQueueSource.Unavailable ?
                        "Remaining work unavailable" : "No remaining queued work");
                    }
                }
                contentHeight = listing.CurHeight + 20f;
                listing.End();
                Widgets.EndScrollView();
                scroll.y = Mathf.Clamp(scroll.y, 0f, Math.Max(0f, contentHeight - outer.height));
                GUI.enabled = !owner.disabled;
                if (Widgets.ButtonText(new Rect(inRect.x, inRect.yMax - 35f, inRect.width * 0.5f - 5f, 30f), "Begin resurrection"))
                {
                    owner.StartResurrection();
                    Close();
                }
                GUI.enabled = true;
                if (Widgets.ButtonText(new Rect(inRect.x + inRect.width * 0.5f, inRect.yMax - 35f, inRect.width * 0.25f - 5f, 30f), "Back"))
                {
                    GoBack();
                }
                if (Widgets.ButtonText(new Rect(inRect.x + inRect.width * 0.75f, inRect.yMax - 35f, inRect.width * 0.25f, 30f), "Cancel"))
                {
                    Close();
                }
            }

            private void GoBack()
            {
                Close();
            }
        }
    }
}
