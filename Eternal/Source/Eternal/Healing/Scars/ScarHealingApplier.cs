// Relative Path: Eternal/Source/Eternal/Healing/Scars/ScarHealingApplier.cs
// Creation Date: 01-01-2025
// Last Edit: 14-01-2026
// Author: 0Shard
// Description: Applies healing to scars and manages healing records.
//              Uses ScarCostCalculator via DI-compatible Default instance.

using System.Collections.Generic;
using System.Linq;
using Verse;
using Eternal.Infrastructure;
using Eternal.Utils;

namespace Eternal.Healing.Scars
{
    /// <summary>
    /// Applies healing to scars and manages healing records.
    /// </summary>
    public class ScarHealingApplier
    {
        private readonly Dictionary<HealingDictionaryKey, ScarHealingRecord> activeHealingRecords;

        public ScarHealingApplier()
        {
            activeHealingRecords = new Dictionary<HealingDictionaryKey, ScarHealingRecord>();
        }

        #region Record Management

        /// <summary>
        /// Gets a unique key for a scar healing record.
        /// </summary>
        public static HealingDictionaryKey GetRecordKey(Hediff scar, Pawn pawn)
        {
            return new HealingDictionaryKey(pawn, scar);
        }

        /// <summary>
        /// Gets the existing record only while it still belongs to this live scar instance.
        /// </summary>
        public ScarHealingRecord GetHealingRecord(Hediff scar, Pawn pawn)
        {
            if (!IsLiveScar(scar, pawn))
            {
                return null;
            }

            var recordKey = GetRecordKey(scar, pawn);
            return activeHealingRecords.TryGetValue(recordKey, out var record)
                && ReferenceEquals(record.Scar, scar) && ReferenceEquals(record.Pawn, pawn)
                ? record : null;
        }

        /// <summary>
        /// Updates healing records for a pawn's scars.
        /// </summary>
        public void UpdateHealingRecords(Pawn pawn, List<Hediff> scars)
        {
            // Records are session-local: detached instances must never donate elapsed time
            // to a replacement, even if its load ID or body-part description is reused.
            var staleKeys = activeHealingRecords
                .Where(kvp => ReferenceEquals(kvp.Value.Pawn, pawn)
                    && (!IsLiveScar(kvp.Value.Scar, pawn) || !scars.Contains(kvp.Value.Scar)))
                .Select(kvp => kvp.Key).ToList();
            foreach (var key in staleKeys)
            {
                activeHealingRecords.Remove(key);
            }

            foreach (var scar in scars)
            {
                if (!IsLiveScar(scar, pawn) || !HediffHealingConfig.CanHealScar(scar))
                {
                    continue;
                }
                var recordKey = GetRecordKey(scar, pawn);

                var record = GetHealingRecord(scar, pawn);
                if (record == null)
                {
                    // Create new healing record
                    record = new ScarHealingRecord
                    {
                        Scar = scar,
                        Pawn = pawn,
                        HealingProgress = 0f,
                        InitialSeverity = scar.Severity,
                        LastHealedTick = Find.TickManager.TicksGame,
                        IsHealing = true,
                        EstimatedHealingTime = ScarCostCalculator.Default.EstimateHealingTime(scar, pawn)
                    };

                    activeHealingRecords[recordKey] = record;
                    EternalLogger.Debug($"Created healing record for {scar.def.LabelCap} on {pawn.Name?.ToStringShort ?? "Unknown"}");
                }
                else
                {
                    // Update existing record
                    record.IsHealing = scar.Severity > 0f;
                }
            }
        }

        /// <summary>
        /// Cleans up completed healing records.
        /// </summary>
        public void CleanupCompletedHealing()
        {
            var completedRecords = activeHealingRecords
                .Where(kvp => !kvp.Value.IsHealing || !IsLiveScar(kvp.Value.Scar, kvp.Value.Pawn))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in completedRecords)
            {
                activeHealingRecords.Remove(key);
            }
        }

        /// <summary>
        /// Clears all healing records.
        /// </summary>
        public void ClearAllHealingRecords()
        {
            activeHealingRecords.Clear();
            EternalLogger.Info("All scar healing records cleared");
        }

        /// <summary>
        /// Clears healing records for a specific pawn.
        /// </summary>
        public void ClearPawnHealingRecords(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            var recordsToRemove = activeHealingRecords
                .Where(kvp => kvp.Value.Pawn == pawn)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in recordsToRemove)
            {
                activeHealingRecords.Remove(key);
            }
        }

        #endregion

        private static bool IsLiveScar(Hediff scar, Pawn pawn)
        {
            return scar != null && pawn?.health?.hediffSet != null
                && ReferenceEquals(scar.pawn, pawn) && pawn.health.hediffSet.hediffs.Contains(scar);
        }

        #region Healing Application

        /// <summary>
        /// Heals a specific scar using the global baseHealingRate.
        /// </summary>
        public void HealScar(ScarHealingRecord record)
        {
            if (record?.Scar == null || record.Pawn == null)
            {
                return;
            }
            if (!IsLiveScar(record.Scar, record.Pawn) || !HediffHealingConfig.CanHealScar(record.Scar))
            {
                record.IsHealing = false;
                activeHealingRecords.Remove(GetRecordKey(record.Scar, record.Pawn));
                return;
            }

            var currentTick = Find.TickManager.TicksGame;

            // Calculate time since last healing
            float deltaTime = currentTick - record.LastHealedTick;
            if (deltaTime <= 0f)
            {
                return;
            }

            // Calculate healing amount (no throttling - uses global baseHealingRate)
            float healingAmount = ScarCostCalculator.Default.CalculateHealingAmount(
                deltaTime,
                record.Scar);

            // Apply healing
            ApplyScarHealing(record, healingAmount);

            // Update record
            record.HealingProgress += healingAmount;
            record.LastHealedTick = currentTick;

            if (Eternal_Mod.settings?.debugMode == true)
            {
                float baseRate = ScarCostCalculator.Default.GetBaseHealingRate(record.Scar);
                EternalLogger.Debug($"Scar healing: {record.Pawn.Name?.ToStringShort ?? "Unknown"}'s {record.Scar.def.LabelCap} healed by {healingAmount:F3} (rate: {baseRate:F3}, Progress: {record.HealingProgress:F2})");
            }
        }

        /// <summary>
        /// Applies healing to a scar.
        /// </summary>
        private void ApplyScarHealing(ScarHealingRecord record, float healingAmount)
        {
            if (record?.Scar == null)
            {
                return;
            }

            var scar = record.Scar;

            // Reduce severity
            scar.Severity -= healingAmount;

            // Remove scar if fully healed
            if (scar.Severity <= 0f)
            {
                scar.pawn.health.RemoveHediff(scar);
                record.IsHealing = false;

                EternalLogger.Info($"Scar healed: {scar.def.LabelCap} on {scar.pawn.Name?.ToStringShort ?? "Unknown"}");
            }
        }

        /// <summary>
        /// Forces immediate healing of all scars for a pawn.
        /// </summary>
        public void EmergencyHealAllScars(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null)
            {
                return;
            }

            var scars = ScarIdentifier.GetPawnScars(pawn);
            int healedCount = 0;

            foreach (var scar in scars)
            {
                pawn.health.RemoveHediff(scar);
                healedCount++;

                var recordKey = GetRecordKey(scar, pawn);
                activeHealingRecords.Remove(recordKey);
            }

            EternalLogger.Info($"Emergency healed {healedCount} scars for {pawn.Name?.ToStringShort ?? "Unknown"}");
        }

        #endregion

        #region Statistics

        /// <summary>
        /// Gets the count of active healing records.
        /// </summary>
        public int ActiveRecordCount => activeHealingRecords.Count;

        /// <summary>
        /// Gets all active healing records.
        /// </summary>
        public IEnumerable<ScarHealingRecord> GetAllRecords()
        {
            return activeHealingRecords.Values;
        }

        /// <summary>
        /// Gets healing records for a specific pawn.
        /// </summary>
        public IEnumerable<ScarHealingRecord> GetPawnRecords(Pawn pawn)
        {
            return activeHealingRecords.Values.Where(r => r.Pawn == pawn);
        }

        #endregion
    }
}
