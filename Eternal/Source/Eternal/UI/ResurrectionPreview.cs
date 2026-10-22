// Relative Path: Eternal/Source/Eternal/UI/ResurrectionPreview.cs
// Creation Date: 12-10-2026
// Author: 0Shard
// Description: Read-only authoritative corpse work snapshots and game-independent source selection.
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Eternal.Extensions;
using Eternal.Healing;
using Eternal.Models;
using Eternal.Compat;

namespace Eternal.UI
{
    public enum HealingQueueSource { Unavailable, Active, CapturedAtDeath, Calculated }
    public enum CorpsePreviewStatus { Unavailable, WaitingToStart, Active, Completed, Disabled }
    public enum ResurrectionStartOutcome { Rejected, Active, Completed }

    public sealed class HealingQueueSelection<T>
    {
        public IReadOnlyList<T> Items { get; }
        public HealingQueueSource Source { get; }
        public HealingQueueSelection(IReadOnlyList<T> items, HealingQueueSource source)
        {
            Items = items;
            Source = source;
        }
    }

    // Keep this generic seam independent of Verse so real precedence/purity tests run under Mono.
    public static class HealingQueueSelector
    {
        public static void Compact<T>(List<T> queue, ISet<T> completed) where T : class
        {
            if (queue == null || queue.Count == 0)
            {
                return;
            }

            int writeIndex = 0;
            for (int readIndex = 0; readIndex < queue.Count; readIndex++)
            {
                T item = queue[readIndex];
                if (item == null || completed?.Contains(item) == true)
                {
                    continue;
                }

                queue[writeIndex++] = item;
            }
            if (writeIndex < queue.Count)
            {
                queue.RemoveRange(writeIndex, queue.Count - writeIndex);
            }
        }
        public static HealingQueueSelection<T> Select<T>(bool hydrated, IReadOnlyList<T> active,
            IReadOnlyList<T> captured, Func<IReadOnlyList<T>> fallback)
        {
            if (hydrated && active != null)
            {
                return new HealingQueueSelection<T>(active, HealingQueueSource.Active);
            }

            if (captured != null && captured.Count > 0)
            {
                return new HealingQueueSelection<T>(captured, HealingQueueSource.CapturedAtDeath);
            }

            var calculated = fallback?.Invoke();
            return new HealingQueueSelection<T>(calculated,
                calculated == null ? HealingQueueSource.Unavailable : HealingQueueSource.Calculated);
        }
    }

    public sealed class CorpsePreviewWork
    {
        public string Label { get; set; }
        public float Severity { get; set; }
        public HealingCostProfile Profile { get; set; }
        public bool PricingUnavailable { get; set; }
        public bool IsRegrowth { get; set; }
        public bool ScalarOnly { get; set; }
        public float? RegrowthEffort { get; set; }
        public float? RegrowthTicks { get; set; }
    }

    public sealed class ResurrectionPreviewSnapshot
    {
        public CorpsePreviewStatus Status;
        public HealingQueueSource Source;
        public List<CorpsePreviewWork> Work = new List<CorpsePreviewWork>();
        public float? IncurredDebt;
        public float KnownNutrition;
        public float EstimatedTicks = float.NaN;
        public bool Waived;
        public bool Qualified;
        public string Qualification;
        public int ConditionCount => Enumerable.Count(Work, w => !w.IsRegrowth);
        public int PartCount => Enumerable.Count(Work, w => w.IsRegrowth);
    }

    public static class ResurrectionPreviewValues
    {
        public static bool CanStart(bool validDeadCorpse, bool servicesAvailable, bool active, bool disabled)
            => validDeadCorpse && servicesAvailable && !active && !disabled;

        public static ResurrectionStartOutcome StartOutcome(bool started, bool dead, bool active)
            => !started ? ResurrectionStartOutcome.Rejected : !dead ? ResurrectionStartOutcome.Completed :
                active ? ResurrectionStartOutcome.Active : ResurrectionStartOutcome.Rejected;
        public static CorpsePreviewStatus Status(bool dead, bool corpseValid, bool trackingAvailable,
            bool processorAvailable, bool active, bool disabled)
        {
            if (!dead || !corpseValid)
            {
                return CorpsePreviewStatus.Completed;
            }

            if (disabled)
            {
                return CorpsePreviewStatus.Disabled;
            }

            if (!trackingAvailable || !processorAvailable)
            {
                return CorpsePreviewStatus.Unavailable;
            }

            return active ? CorpsePreviewStatus.Active : CorpsePreviewStatus.WaitingToStart;
        }

        public static void Estimate(ResurrectionPreviewSnapshot snapshot, float rate, float bodySize,
            int normalInterval, float ratio, float multiplier)
        {
            float longest = 0f;
            float cost = 0f;
            snapshot.Qualified = false;
            snapshot.Qualification = null;
            foreach (var item in snapshot.Work)
            {
                if (item.IsRegrowth)
                {
                    snapshot.Qualified = true;
                    snapshot.Qualification = item.RegrowthEffort.HasValue
                        ? "Remaining regrowth work uses current part HP and progress; dependent completion time is unavailable."
                        : "Regrowth cost and dependent time are unavailable; shown nutrition covers only known condition work.";
                    if (snapshot.Waived)
                    {
                        snapshot.Qualification = "Regrowth work/time is unavailable or approximate; nutrition is waived.";
                    }

                    if (item.RegrowthEffort.HasValue)
                    {
                        float regrowthCost = snapshot.Waived ? 0f :
                            item.Profile != null ? HealingWorkCostCalculator.Cost(item.Profile, item.RegrowthEffort.Value, 0f) :
                            MenuValueCalculator.NutritionCost(item.RegrowthEffort.Value, ratio, multiplier);
                        cost = MenuValueCalculator.IsFinite(regrowthCost) ? cost + regrowthCost : float.NaN;
                    }
                    continue;
                }
                float passes = CorpseHealingCostCalculator.ProcessingPasses(Math.Max(0f, item.Severity - (item.Profile?.Floor ?? 0f)), rate * bodySize);
                float ticks = normalInterval > 0 && MenuValueCalculator.IsFinite(bodySize) && bodySize > 0f
                    ? passes * normalInterval : float.NaN;
                float itemCost = snapshot.Waived ? 0f : item.PricingUnavailable ? float.NaN : item.Profile != null
                    ? HealingWorkCostCalculator.Cost(item.Profile, item.Severity, item.Profile.Floor)
                    : CorpseHealingCostCalculator.FutureNutrition(item.Severity, ratio, multiplier, false);
                if (!MenuValueCalculator.IsFinite(ticks) || !MenuValueCalculator.IsFinite(itemCost))
                {
                    snapshot.Qualified = true;
                    snapshot.Qualification = "Current work or calculation inputs are unavailable; a reliable estimate cannot be given.";
                    longest = float.NaN;
                    if (!MenuValueCalculator.IsFinite(itemCost))
                    {
                        cost = float.NaN;
                    }
                }
                else
                {
                    longest = Math.Max(longest, ticks);
                    cost += itemCost;
                }
            }
            snapshot.KnownNutrition = snapshot.Waived ? 0f : cost;
            if (!MenuValueCalculator.IsFinite(snapshot.KnownNutrition))
            {
                snapshot.Qualified = true;
                snapshot.Qualification = "Additional nutrition is unavailable for the current work or calculation inputs.";
            }
            snapshot.EstimatedTicks = snapshot.Qualified ? float.NaN : longest;
        }
    }

    public static class ResurrectionPreview
    {
        public static HealingQueueSelection<HealingItem> SelectQueue(CorpseTrackingEntry entry, Pawn pawn)
        {
            if (entry == null)
            {
                return new HealingQueueSelection<HealingItem>(null, HealingQueueSource.Unavailable);
            }

            return HealingQueueSelector.Select(entry.HealingQueueHydrated, entry.HealingQueue,
                entry.PreCalculatedHealingQueue, () => new EternalResurrectionCalculator().CalculateHealingQueue(pawn));
        }

        public static ResurrectionPreviewSnapshot Capture(Pawn pawn, Verse.Corpse corpse, CorpseTrackingEntry entry,
            Eternal_Settings settings, bool processorAvailable, float? debt)
        {
            var snapshot = new ResurrectionPreviewSnapshot
            {
                Status = ResurrectionPreviewValues.Status(pawn?.Dead == true, corpse != null && !corpse.Destroyed,
                    entry != null, processorAvailable, entry?.IsHealingActive == true, EternalModState.IsDisabled),
                IncurredDebt = debt,
                Waived = pawn != null && pawn.HasFoodNeedDisabled()
            };
            if (snapshot.Status == CorpsePreviewStatus.Completed || entry == null || pawn == null || settings == null)
            {
                return snapshot;
            }

            var selection = SelectQueue(entry, pawn);
            snapshot.Source = selection.Source;
            if (selection.Items == null) { snapshot.Status = CorpsePreviewStatus.Unavailable; return snapshot; }
            var active = pawn.health?.hediffSet?.hediffs.OfType<EternalRegrowing_Hediff>()
                .Where(h => h.forPart != null).GroupBy(h => h.forPart).ToDictionary(g => g.Key, g => g.First());
            var parts = new HashSet<BodyPartRecord>();
            foreach (var item in selection.Items)
            {
                if (item == null)
                {
                    continue;
                }
                var def = item.Hediff?.def ?? (item.HediffDefName == null ? null : DefDatabase<HediffDef>.GetNamedSilentFail(item.HediffDefName));
                if (item.Type == HealingType.Regrowth)
                {
                    if (item.Hediff?.Part != null)
                    {
                        AddSubtree(item.Hediff.Part, parts);
                    }
                    else
                    {
                        snapshot.Work.Add(new CorpsePreviewWork { Label = "Unidentified regrowth", IsRegrowth = true });
                    }
                    continue;
                }
                var setting = settings.hediffManager?.Store.GetExisting(def?.defName);
                var profile = HealingCostProfiles.Resolve(def, setting, settings.nutritionCostMultiplier, snapshot.Waived);
                snapshot.Work.Add(new CorpsePreviewWork
                {
                    Label = item.Hediff?.LabelCap.ToString() ?? item.HediffDefName ?? item.Type.ToString(),
                    Severity = item.Hediff?.Severity ?? item.Severity,
                    Profile = profile,
                    PricingUnavailable = profile == null,
                    ScalarOnly = item.Hediff == null
                });
            }
            if (active != null)
            {
                foreach (var part in active.Keys)
                {
                    AddSubtree(part, parts);
                }
            }
            var regrowthSetting = settings.hediffManager?.Store.GetExisting(EternalDefOf.Eternal_Regrowing?.defName);
            var regrowthProfile = HealingCostProfiles.Resolve(EternalDefOf.Eternal_Regrowing, regrowthSetting, settings.nutritionCostMultiplier, snapshot.Waived);
            foreach (var part in parts)
            {
                EternalRegrowing_Hediff hediff = null;
                active?.TryGetValue(part, out hediff);
                if (hediff == null && pawn.health?.hediffSet?.PartIsMissing(part) != true)
                {
                    continue;
                }
                snapshot.Work.Add(new CorpsePreviewWork
                {
                    Label = part.Label,
                    IsRegrowth = true,
                    Profile = regrowthProfile,
                    RegrowthEffort = regrowthProfile == null ? (float?)null :
                        RegrowthWorkAccounting.RemainingEffort(hediff?.Severity ?? SettingsDefaults.RegrowthInitialProgress, EBFCompat.GetMaxHealth(part, pawn))
                });
            }
            ResurrectionPreviewValues.Estimate(snapshot, settings.baseHealingRate, pawn.BodySize,
                settings.normalTickRate, SettingsDefaults.SeverityToNutritionRatio, settings.nutritionCostMultiplier);
            return snapshot;
        }

        private static void AddSubtree(BodyPartRecord part, HashSet<BodyPartRecord> parts)
        {
            if (!parts.Add(part))
            {
                return;
            }
            foreach (var child in part.parts)
            {
                AddSubtree(child, parts);
            }
        }
    }
}
