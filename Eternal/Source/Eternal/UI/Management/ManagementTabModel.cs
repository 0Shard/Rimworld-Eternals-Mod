// Relative Path: Eternal/Source/Eternal/UI/Management/ManagementTabModel.cs
// Creation Date: 12-10-2026
// Author: 0Shard
// Description: Acquires one read-only selection snapshot through separate living/corpse paths.
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Eternal.DI;
using Eternal.Extensions;
using Eternal.Healing;
using Eternal.Healing.Scars;
using Eternal.Resources;

namespace Eternal.UI.Management
{
    public sealed class ManagementCondition
    {
        public string Label;
        public float Severity;
        public float Ticks = float.NaN;
        public string Status;
        public string Pricing;
        public string Tooltip;
        public bool IsScar;
    }

    public sealed class ManagementPart
    {
        public string Label;
        public string Phase;
        public float Progress;
        public string Status;
    }

    public sealed class ManagementSnapshot
    {
        public string Name;
        public string Status;
        public float? Debt;
        public float? Capacity;
        public bool Waived;
        public List<ManagementCondition> Conditions = new List<ManagementCondition>();
        public List<ManagementPart> Parts = new List<ManagementPart>();
        public ResurrectionPreviewSnapshot Corpse;
        public string Technical;
        public string ProcessingRate;
        public string ProcessingTooltip;
        public string ScarRate;
        public string ScarTooltip;
        public string Headroom;
        public string CapacityDetails;
        public string RecoveryRate;
    }

    public class ManagementTabModel
    {
        public Pawn Pawn { get; }
        private readonly Verse.Corpse selectedCorpse;

        public ManagementTabModel(Pawn pawn, Verse.Corpse corpse = null)
        {
            Pawn = pawn;
            selectedCorpse = corpse;
        }

        public ManagementSnapshot Capture()
        {
            var snapshot = new ManagementSnapshot { Name = Pawn?.LabelShortCap ?? "Unknown" };
            if (Pawn == null) { snapshot.Status = "Unavailable"; return snapshot; }
            var services = EternalServiceContainer.Instance;
            var settings = Eternal_Mod.GetSettings();
            snapshot.Debt = services.FoodDebtSystem?.GetDebt(Pawn);
            snapshot.Capacity = services.FoodDebtSystem?.GetMaxCapacity(Pawn);
            snapshot.Waived = Pawn.HasFoodNeedDisabled();
            float coefficient = SettingsDefaults.SeverityToNutritionRatio * settings.nutritionCostMultiplier;
            snapshot.ProcessingRate = "Injuries: " + MenuValueCalculator.CompactNumber(MenuValueCalculator.ProcessingHpPerDay(
                settings.baseHealingRate * Pawn.BodySize, settings.normalTickRate)) + " HP/day";
            snapshot.ProcessingTooltip = "Base rate for one ordinary injury on this pawn. Eligible conditions heal in parallel. " +
                "Per-condition settings, stages and available nutrition can change the actual rate.";
            snapshot.ScarRate = "Scars: " + MenuValueCalculator.CompactNumber(MenuValueCalculator.ProcessingHpPerDay(
                settings.baseHealingRate, settings.rareTickRate)) + " severity/day";
            snapshot.ScarTooltip = "The separate scar path uses the global rate and elapsed processing ticks, without body-size scaling or a nutrition charge. " +
                "Each scar progresses independently. Its ETA includes the next application pass and initialization when needed.";
            var debtSystem = services.FoodDebtSystem;
            float? headroom = debtSystem?.GetRemainingCapacity(Pawn);
            snapshot.Headroom = snapshot.Waived ? "Nutrition waived" : "Debt healing budget: " +
                MenuValueCalculator.CompactNumber(headroom.HasValue ? MenuValueCalculator.InjuryHpBudget(headroom.Value, coefficient) : float.NaN) + " injury HP";
            float baseCapacity = FoodDebtPolicy.Capacity(Pawn.needs?.food?.MaxLevel ?? Pawn.BodySize, settings.maxDebtMultiplier, 0f);
            snapshot.CapacityDetails = "Base living cap: " + MenuValueCalculator.Nutrition(debtSystem == null ? (float?)null : baseCapacity) +
                "; resurrection-inclusive capacity: " + MenuValueCalculator.Nutrition(snapshot.Capacity) +
                ". Remaining debt headroom: " + MenuValueCalculator.Nutrition(headroom) +
                ". The HP budget uses the global normal-injury price; condition multipliers and other healing categories have different costs.";
            snapshot.RecoveryRate = snapshot.Waived ? "Repayment waived" : "Food-supported recovery equivalent: " +
                MenuValueCalculator.Number(debtSystem == null ? float.NaN : MenuValueCalculator.RecoveryEquivalentHpPerDay(
                    snapshot.Debt.Value, debtSystem.GetPeakDebt(Pawn), settings.debtRepaymentDays, coefficient)) +
                $" normal-injury HP/day; bounded every {settings.rareTickRate} ticks by debt and food above {settings.foodDrainThreshold:P0}";
            if (selectedCorpse != null || Pawn.Dead)
            {
                CaptureCorpse(snapshot, services, settings);
            }
            else
            {
                CaptureLiving(snapshot, services, settings);
            }

            CaptureParts(snapshot, services);
            return snapshot;
        }

        private void CaptureCorpse(ManagementSnapshot snapshot, EternalServiceContainer services, Eternal_Settings settings)
        {
            var entry = services.CorpseManager?.GetCorpseData(Pawn);
            snapshot.Corpse = ResurrectionPreview.Capture(Pawn, selectedCorpse ?? Pawn.Corpse, entry, settings,
                services.CorpseHealingProcessor != null, snapshot.Debt);
            snapshot.Status = ManagementTabPresenter.CorpseStatus(snapshot.Corpse.Status);
            foreach (var work in snapshot.Corpse.Work.Where(w => !w.IsRegrowth))
            {
                snapshot.Conditions.Add(new ManagementCondition
                {
                    Label = work.Label,
                    Severity = work.Severity,
                    Ticks = work.Severity > CorpseHealingCostCalculator.CompletionCutoff
                        ? CorpseHealingCostCalculator.ProcessingPasses(work.Severity, settings.baseHealingRate * Pawn.BodySize) * settings.normalTickRate
                        : float.NaN,
                    Status = work.ScalarOnly ? "Saved remaining work" : "Remaining work",
                    Pricing = snapshot.Waived ? "Nutrition waived" : MenuValueCalculator.Pricing(work.Profile, work.Severity) +
                        "; future charge: " + MenuValueCalculator.Nutrition(work.Profile == null ? (float?)null :
                            HealingWorkCostCalculator.Cost(work.Profile, work.Severity, work.Profile.Floor))
                });
            }

            snapshot.Technical = "Corpse healing uses the current global rate and body size on normal passes. " +
                "Regrowth depends on parent parts; estimates exclude unknown waits.";
        }

        private void CaptureLiving(ManagementSnapshot snapshot, EternalServiceContainer services, Eternal_Settings settings)
        {
            bool available = services.HealingProcessor != null && services.HediffHealingCalculator != null &&
                services.FoodDebtSystem != null && services.FoodCostProcessor != null;
            bool resources = MenuValueCalculator.ResourcesPermitHealing(snapshot.Waived,
                services.FoodCostProcessor?.CanDrainFood(Pawn) == true,
                services.FoodDebtSystem?.GetRemainingCapacity(Pawn) > 0f);
            snapshot.Status = EternalModState.IsDisabled ? "Eternal is disabled" : !available ? "Healing system unavailable" : "No eligible healing work";
            float budget = services.FoodCostProcessor?.GetAvailableNutrition(Pawn) ?? (snapshot.Waived ? float.PositiveInfinity : 0f);
            bool anyProgress = false;
            bool resourceBlocked = false;
            foreach (var hediff in Pawn.health?.hediffSet?.hediffs ?? new List<Hediff>())
            {
                if (hediff?.def == null)
                {
                    continue;
                }

                var setting = settings.hediffManager?.Store?.GetExisting(hediff.def.defName)
                    ?? HediffHealingConfig.CreateDefaultSetting(hediff);
                bool scar = hediff.IsPermanentInjuryScar();
                bool modOwned = hediff.def == EternalDefOf.Eternal_Essence ||
                    hediff is Eternal.Hediffs.MetabolicRecovery_Hediff || hediff.def == EternalDefOf.Eternal_MetabolicRecovery
                    || HediffHealingConfig.IsRegrowthManaged(hediff.def);
                if (!MenuValueCalculator.IncludeLivingCondition(hediff.def.isBad, setting?.canHeal == true,
                    scar, modOwned, hediff is Hediff_MissingPart))
                {
                    continue;
                }

                bool? normalEligible = GetNormalHealingEligibility(hediff, setting, services);
                var condition = new ManagementCondition
                {
                    Label = hediff.LabelCap + (hediff.Part == null ? "" : " (" + hediff.Part.Label + ")"),
                    Severity = hediff.Severity,
                    IsScar = scar
                };
                var profile = HealingCostProfiles.Resolve(hediff.def, setting, settings.nutritionCostMultiplier, snapshot.Waived);
                float rate = available ? services.HediffHealingCalculator.CalculateHediffHealing(Pawn, hediff, setting) : 0f;
                float factor = TypeSpecificHealing.SeverityFactor(EternalHealingPriority.GetHealingType(hediff));
                condition.Pricing = scar || snapshot.Waived ?
                    (snapshot.Waived ? "Nutrition waived" : "Living permanent scar: no nutrition charge") :
                    MenuValueCalculator.Pricing(profile, hediff.Severity) + "; remaining charge: " +
                    MenuValueCalculator.Nutrition(profile == null ? (float?)null : HealingWorkCostCalculator.Cost(profile, hediff.Severity, profile.Floor));
                if (EternalModState.IsDisabled)
                {
                    condition.Status = "Eternal is disabled";
                }
                else if (!available)
                {
                    condition.Status = "Healing system unavailable";
                }
                else if (setting?.canHeal == false)
                {
                    condition.Status = "Healing disabled for this condition";
                }
                else if (scar)
                {
                    var timing = CaptureScarTiming(hediff, services);
                    float remaining = timing.HasValue && timing.Value.HasEstimate ? timing.Value.RemainingTicks : float.NaN;
                    var display = MenuValueCalculator.DescribeLivingScar(hediff.def.injuryProps != null && hediff.IsPermanent(),
                        setting?.canHeal, normalEligible, resources, snapshot.Waived, remaining);
                    condition.Tooltip = display.Status + "\n" + DescribeScarTiming(timing);
                    condition.Status = timing.HasValue && timing.Value.HasEstimate ? "Scar healing" :
                        timing?.State == ScarHealingTimingState.PositiveSeverityFloor ? "Scar path blocked by minimum severity" :
                        timing?.State == ScarHealingTimingState.NoProgress ? "Scar healing stalled" :
                        timing?.State == ScarHealingTimingState.ProjectionLimit ? "Long scar healing; ETA unavailable" :
                        timing?.State == ScarHealingTimingState.NoMapTrigger || timing?.State == ScarHealingTimingState.PawnUnavailable
                            ? "Scar healing paused" : "Scar time unavailable";
                    condition.Ticks = display.RemainingTicks;
                    anyProgress |= timing.HasValue && timing.Value.HasEstimate;
                }
                else if (normalEligible != true)
                {
                    condition.Status = !normalEligible.HasValue ? "Normal healing eligibility unavailable; waiting for tracked threshold" :
                        HediffHealingConfig.IsThresholdGated(hediff, setting) ? "Waiting for activation threshold" : "Not eligible for healing";
                }
                else if (!LivingHealingProjection.CanAdvance(profile, hediff.Severity, rate * factor, budget, snapshot.Waived))
                {
                    resourceBlocked |= rate > 0f && profile?.IsValid == true;
                    condition.Status = rate > 0f ? "Waiting for food or debt capacity" : "Healing rate unavailable";
                }
                else
                {
                    anyProgress = true;
                    condition.Status = HealingWorkCostCalculator.IsAtHealingFloor(profile, hediff.Severity)
                        ? "At healing floor; removal on the next scheduled pass" : "Eligible on scheduled passes";
                    int? last = Eternal_Component.Current?.LastNormalHealTick;
                    if (Current.Game != null && last.HasValue && Find.TickManager != null)
                    {
                        int next = LivingHealingProjection.NextPass(Find.TickManager.TicksGame, last.Value, settings.normalTickRate);
                        condition.Ticks = LivingHealingProjection.Ticks(profile, hediff.Severity, rate, factor,
                            settings.normalTickRate, next);
                    }
                    else { condition.Status += "; schedule unavailable"; }
                    float affordable = HealingWorkCostCalculator.AffordableSeverity(profile, hediff.Severity, rate * factor, budget);
                    if (affordable < Math.Min(rate * factor, Math.Max(0f, hediff.Severity - profile.Floor)))
                    {
                        condition.Status += "; current nutrition budget limits progress";
                    }
                    if (HealingWorkCostCalculator.Cost(profile, hediff.Severity, profile.Floor) > budget)
                    {
                        condition.Status += "; full-rate estimate assumes continued nutrition";
                    }
                    if (hediff.def.injuryProps != null)
                    {
                        condition.Status += "; " + MenuValueCalculator.Number(MenuValueCalculator.ProcessingHpPerDay(rate * factor, settings.normalTickRate)) + " HP/day for this injury";
                    }
                    if (hediff.IsDebuffWithStages())
                    {
                        condition.Status += "; current-stage estimate";
                    }
                }
                snapshot.Conditions.Add(condition);
            }
            if (!EternalModState.IsDisabled && available)
            {
                snapshot.Status = anyProgress ? resourceBlocked ? "Healing available; some conditions await nutrition" : "Healing available" :
                    resourceBlocked ? "Waiting for food or debt capacity" : "No eligible healing work";
            }
            snapshot.Technical = "Times assume current settings, severity stage and uninterrupted full-rate affordability; eligible conditions compete for the same budget. " +
                "Permanent scars use their separate, uncharged rate. Hover a condition for its cost and timing qualifications.";
        }

        private ScarHealingTimingResult? CaptureScarTiming(Hediff scar, EternalServiceContainer services)
        {
            int? lastRare = Eternal_Component.Current?.LastRareHealTick;
            if (Current.Game == null || !lastRare.HasValue || services.HealingProcessor?.ScarHealing == null)
            {
                return null;
            }

            return services.HealingProcessor.ScarHealing.GetHealingTiming(Pawn, scar, Find.TickManager.TicksGame, lastRare.Value);
        }

        private static string DescribeScarTiming(ScarHealingTimingResult? timing)
        {
            if (!timing.HasValue)
            {
                return "Scar scheduling is unavailable.";
            }

            switch (timing.Value.State)
            {
                case ScarHealingTimingState.Estimated: return "Includes unapplied elapsed healing and the next scheduled pass. The estimate updates if severity or settings change.";
                case ScarHealingTimingState.AwaitingInitialization: return "Includes the first pass that initializes this scar's timing record. Save/load starts a new record.";
                case ScarHealingTimingState.PositiveSeverityFloor: return "The separate scar path cannot remove this condition at its positive minimum severity. Normal healing may still remove it.";
                case ScarHealingTimingState.NoProgress: return "At this severity and rate, a scar pass cannot reduce the stored severity. Normal healing may still help; the separate scar path needs a lower severity or higher rate to progress.";
                case ScarHealingTimingState.ProjectionLimit: return "Scar healing continues at the displayed rate, but an exact countdown is unavailable for this long projection.";
                case ScarHealingTimingState.NoMapTrigger: return "Scar processing is waiting for a living Eternal on a map to trigger it.";
                case ScarHealingTimingState.Disabled: return "Eternal is disabled.";
                case ScarHealingTimingState.PawnUnavailable: return "This pawn cannot currently receive living scar healing.";
                case ScarHealingTimingState.Ineligible: return "This condition is not in the separate scar-healing path.";
                default: return "Scar timing is unavailable for the current settings or scheduler.";
            }
        }

        private bool? GetNormalHealingEligibility(Hediff hediff, EternalHediffSetting setting, EternalServiceContainer services)
        {
            if (setting?.canHeal == false)
            {
                return false;
            }
            // Bypass the engine's mutating threshold check, then inspect only already-tracked
            // state. A menu repaint must never roll or latch a gameplay threshold.
            if (!HediffHealingConfig.ShouldHealByDefault(hediff, setting, true))
            {
                return false;
            }

            if (!HediffHealingConfig.IsThresholdGated(hediff, setting))
            {
                return true;
            }

            float threshold = 0f;
            bool reached = false;
            bool recorded = services.ThresholdTracker != null &&
                services.ThresholdTracker.TryGetThreshold(Pawn, hediff, out threshold, out reached);
            return MenuValueCalculator.TrackedThresholdEligibility(recorded, reached, hediff.Severity, threshold);
        }

        private void CaptureParts(ManagementSnapshot snapshot, EternalServiceContainer services)
        {
            var state = services.RegrowthManager?.GetRegrowthState(Pawn);
            if (state == null)
            {
                return;
            }

            var phases = state.partPhases;
            var progress = state.partProgress;
            foreach (var item in phases)
            {
                float current = progress.TryGetValue(item.Key, out float value) ? value : 0f;
                snapshot.Parts.Add(new ManagementPart
                {
                    Label = item.Key?.Label ?? "Unknown part",
                    Phase = EternalRegrowthState.GetPhaseSimpleName(item.Value),
                    Progress = MenuValueCalculator.IsFinite(current) ? Math.Max(0f, Math.Min(1f, current)) : 0f,
                    Status = MenuValueCalculator.PartStatus(item.Value == RegrowthPhase.Complete,
                        Pawn.health?.hediffSet?.PartIsMissing(item.Key), services.RegrowthManager.HasRegrowthHediff(Pawn, item.Key),
                        Pawn.health?.hediffSet == null ? (bool?)null : services.RegrowthManager.CanCompleteRegrowth(Pawn, item.Key),
                        state.CanUpdatePart(item.Key))
                });
            }
        }
    }

    internal static class LivingHealingProjection
    {
        internal static bool CanAdvance(HealingCostProfile profile, float severity, float requested, float budget, bool waived)
            => MenuValueCalculator.IsFinite(requested) && requested > 0f &&
                (HealingWorkCostCalculator.IsAtHealingFloor(profile, severity) ||
                    HealingWorkCostCalculator.AffordableSeverity(profile, severity, requested,
                        waived ? float.PositiveInfinity : budget) > 0f);

        internal static float Ticks(HealingCostProfile profile, float severity, float rate, float typeFactor,
            int interval, int nextPass)
        {
            if (profile?.IsValid != true || !MenuValueCalculator.IsFinite(severity) ||
                !MenuValueCalculator.IsFinite(rate) || rate <= 0f || !MenuValueCalculator.IsFinite(typeFactor) ||
                typeFactor <= 0f || interval <= 0 || nextPass < 1)
            {
                return float.NaN;
            }
            float work = Math.Max(0f, severity - profile.Floor);
            if (work == 0f) { return nextPass; }
            float full = MenuValueCalculator.ProcessingTicks(work, rate * typeFactor, interval);
            return full - interval + nextPass;
        }

        internal static int NextPass(int currentTick, int lastTick, int interval)
            => interval <= 0 || currentTick < 0 || lastTick < 0 || lastTick > currentTick ? 0 :
                (int)Math.Max(1L, (long)lastTick + interval - currentTick);
    }
}
