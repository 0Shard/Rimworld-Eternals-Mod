// Relative Path: Eternal/Source/Eternal/Healing/EternalScarHealing.cs
// Creation Date: 01-01-2025
// Last Edit: 29-12-2025
// Author: 0Shard
// Description: Accelerated scar healing system coordinator. Delegates to focused components.

using System.Collections.Generic;
using System.Linq;
using Verse;
using Eternal.Extensions;
using Eternal.Healing.Scars;
using Eternal.Utils;

namespace Eternal
{
    /// <summary>
    /// Information about a scar being healed.
    /// </summary>
    public class ScarHealingRecord
    {
        public Hediff Scar { get; set; }
        public float HealingProgress { get; set; }
        public int LastHealedTick { get; set; }
        public float InitialSeverity { get; set; }
        public bool IsHealing { get; set; }
        public Pawn Pawn { get; set; }
        public float EstimatedHealingTime { get; set; }
    }

    /// <summary>
    /// Accelerated scar healing system coordinator.
    /// Delegates to focused components: ScarIdentifier, ScarCostCalculator, ScarHealingApplier.
    /// </summary>
    public class EternalScarHealing
    {
        private readonly ScarHealingApplier applier;
        private int lastProcessingTick = 0;

        public EternalScarHealing()
        {
            applier = new ScarHealingApplier();
            EternalLogger.Info("EternalScarHealing initialized with component-based architecture");
        }

        #region Main Processing

        /// <summary>
        /// Processes scar healing for all Eternal pawns.
        /// </summary>
        public void ProcessScarHealing()
        {
            var currentTick = Find.TickManager.TicksGame;

            // Check if it's time to process (every 60 ticks)
            if (currentTick - lastProcessingTick < 60)
                return;

            lastProcessingTick = currentTick;

            // Get all living Eternal pawns
            var livingEternals = GetAllEternalPawns().Where(p => !p.Dead).ToList();

            foreach (var pawn in livingEternals)
            {
                ProcessPawnScarHealing(pawn);
            }

            applier.CleanupCompletedHealing();
        }

        /// <summary>
        /// Processes scar healing for a specific pawn.
        /// </summary>
        public void ProcessPawnScarHealing(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null)
                return;

            // Get all scars using identifier
            var scars = ScarIdentifier.GetPawnScars(pawn);
            if (scars.Count == 0)
                return;

            // Update healing records
            applier.UpdateHealingRecords(pawn, scars);

            // Sort scars by priority (cost-based)
            // Uses ScarCostCalculator.Default for DI-compatible instance access
            var sortedScars = scars
                .Select(scar => applier.GetHealingRecord(scar, pawn))
                .Where(record => record != null)
                .OrderBy(record => ScarCostCalculator.Default.GetScarCost(record))
                .ThenBy(record => record.InitialSeverity)
                .ToList();

            // Process healing by priority
            ProcessScarHealingByPriority(pawn, sortedScars);
        }

        /// <summary>
        /// Processes scar healing by priority order.
        /// All scars heal at the configured rate - no throttling.
        /// </summary>
        private void ProcessScarHealingByPriority(Pawn pawn, List<ScarHealingRecord> sortedScars)
        {
            foreach (var record in sortedScars)
            {
                if (record?.Scar == null || !record.IsHealing)
                    continue;

                applier.HealScar(record);
            }
        }

        #endregion

        #region Pawn Discovery

        /// <summary>
        /// Gets all Eternal pawns (maps + caravans).
        /// </summary>
        private List<Pawn> GetAllEternalPawns()
        {
            var pawns = PawnExtensions.GetAllEternalPawnsAllMaps().ToList();

            foreach (var caravan in Find.WorldObjects.Caravans)
            {
                pawns.AddRange(caravan.PawnsListForReading.Where(p => p.IsValidEternal()));
            }

            return pawns;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Forces immediate healing of all scars for a pawn.
        /// </summary>
        public void EmergencyHealAllScars(Pawn pawn)
        {
            applier.EmergencyHealAllScars(pawn);
        }

        /// <summary>
        /// Clears all healing records.
        /// </summary>
        public void ClearAllHealingRecords()
        {
            applier.ClearAllHealingRecords();
        }

        /// <summary>
        /// Clears healing records for a specific pawn.
        /// </summary>
        public void ClearPawnHealingRecords(Pawn pawn)
        {
            applier.ClearPawnHealingRecords(pawn);
        }

        #endregion

        #region Statistics

        /// <summary>
        /// Gets scar healing statistics for all Eternals.
        /// </summary>
        public Dictionary<string, object> GetScarHealingStatistics()
        {
            var records = applier.GetAllRecords().ToList();

            return new Dictionary<string, object>
            {
                ["Active Healing Records"] = applier.ActiveRecordCount,
                ["Total Scars Being Healed"] = records.Count(r => r.IsHealing),
                ["Critical Body Part Scars"] = records.Count(r => ScarIdentifier.IsOnCriticalPart(r.Scar)),
                ["High Severity Scars"] = records.Count(r => ScarIdentifier.IsHighSeverityScar(r.Scar)),
                ["Average Healing Progress"] = records.Count > 0 ? records.Average(r => r.HealingProgress) : 0f
            };
        }

        /// <summary>
        /// Gets scar healing statistics for a specific pawn.
        /// </summary>
        public Dictionary<string, object> GetPawnScarStatistics(Pawn pawn)
        {
            var pawnRecords = applier.GetPawnRecords(pawn).ToList();

            var stats = new Dictionary<string, object>
            {
                ["Total Scars"] = pawnRecords.Count,
                ["Healing Scars"] = pawnRecords.Count(r => r.IsHealing),
                ["Critical Body Part Scars"] = pawnRecords.Count(r => ScarIdentifier.IsOnCriticalPart(r.Scar)),
                ["High Severity Scars"] = pawnRecords.Count(r => ScarIdentifier.IsHighSeverityScar(r.Scar)),
                ["Average Progress"] = pawnRecords.Count > 0 ? pawnRecords.Average(r => r.HealingProgress) : 0f
            };

            stats["Scar Details"] = pawnRecords.Select(r => new Dictionary<string, object>
            {
                ["Name"] = r.Scar?.def?.LabelCap ?? "Unknown",
                ["Body Part"] = r.Scar?.Part?.def?.LabelCap ?? "Unknown",
                ["Category"] = ScarIdentifier.GetScarCategory(r.Scar),
                ["Severity"] = r.Scar?.Severity ?? 0f,
                ["Progress"] = r.HealingProgress,
                ["Is Critical"] = ScarIdentifier.IsOnCriticalPart(r.Scar).ToString(),
                ["Estimated Time"] = r.EstimatedHealingTime
            }).ToList();

            return stats;
        }

        #endregion
    }
}
