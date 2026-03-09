// file path: Eternal/Source/Eternal/Models/PawnAssignmentSnapshot.cs
// Author Name: 0Shard
// Date Created: 06-12-2025
// Date Last Modified: 20-02-2026
// Description: Immutable snapshot of pawn work priorities, policies, and schedule for preservation
//              across resurrection. Supports both vanilla RimWorld and Work Tab mod's extended
//              priority system with WorkGiver subtypes and 24-hour scheduling.
//              Hardened with comprehensive try-catch blocks for WorkTab integration resilience.
//              Per-entry try/catch in RestorePolicies() with null-safe database access.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Eternal.Exceptions;
using Eternal.Utils;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Eternal.Models
{
    /// <summary>
    /// Captures and restores pawn work assignments, policies, and schedules across resurrection.
    /// Supports both vanilla RimWorld work priorities and Work Tab's extended WorkGiver-level
    /// priorities with 24-hour scheduling.
    /// </summary>
    public class PawnAssignmentSnapshot : IExposable
    {
        #region Fields

        /// <summary>
        /// Vanilla work priorities: WorkTypeDef.defName -> priority (0-4).
        /// Always captured as a fallback, even when Work Tab is loaded.
        /// </summary>
        private Dictionary<string, int> workTypePriorities;

        /// <summary>
        /// Work Tab priorities: WorkGiverDef.defName -> int[24] (hourly priorities 0-9).
        /// Only populated when Work Tab mod is detected.
        /// </summary>
        private Dictionary<string, int[]> workGiverPriorities;

        /// <summary>
        /// Flag indicating whether Work Tab data was captured.
        /// Used during restore to determine which system to use.
        /// </summary>
        private bool hasWorkTabData;

        /// <summary>
        /// Outfit/Apparel policy label.
        /// </summary>
        private string outfitPolicyName;

        /// <summary>
        /// Drug policy label.
        /// </summary>
        private string drugPolicyName;

        /// <summary>
        /// Food restriction policy label.
        /// </summary>
        private string foodPolicyName;

        /// <summary>
        /// Schedule: 24 entries of TimeAssignmentDef.defName, one per hour.
        /// </summary>
        private List<string> hourlySchedule;

        /// <summary>
        /// Game tick when this snapshot was captured. Used for debugging.
        /// </summary>
        private int capturedAtTick;

        #endregion

        #region Static Fields for Work Tab Reflection

        /// <summary>
        /// Cached flag indicating whether Work Tab is loaded.
        /// </summary>
        private static bool? workTabLoaded;

        /// <summary>
        /// Cached PropertyInfo for PriorityManager.Get static property.
        /// </summary>
        private static PropertyInfo priorityManagerGetProperty;

        /// <summary>
        /// Cached MethodInfo for PriorityTracker indexer getter (by pawn).
        /// </summary>
        private static MethodInfo priorityTrackerIndexerGetter;

        /// <summary>
        /// Cached MethodInfo for PriorityTracker.WorkGiver indexer getter.
        /// </summary>
        private static MethodInfo workGiverIndexerGetter;

        /// <summary>
        /// Cached PropertyInfo for WorkPriority.Priorities (int[24]).
        /// </summary>
        private static PropertyInfo workPriorityPrioritiesProperty;

        /// <summary>
        /// Cached MethodInfo for PriorityTracker.SetPriority(WorkGiverDef, int, List{int}).
        /// </summary>
        private static MethodInfo setWorkGiverPriorityMethod;

        #endregion

        #region Constructors

        /// <summary>
        /// Parameterless constructor for Scribe deserialization.
        /// </summary>
        public PawnAssignmentSnapshot()
        {
            workTypePriorities = new Dictionary<string, int>();
            workGiverPriorities = new Dictionary<string, int[]>();
            hourlySchedule = new List<string>();
        }

        #endregion

        #region Static Methods

        /// <summary>
        /// Captures current assignments from a living pawn.
        /// </summary>
        /// <param name="pawn">The pawn to capture assignments from.</param>
        /// <returns>A new snapshot, or null if the pawn is null.</returns>
        public static PawnAssignmentSnapshot CaptureFrom(Pawn pawn)
        {
            if (pawn == null)
            {
                Log.Warning("[Eternal] PawnAssignmentSnapshot.CaptureFrom called with null pawn");
                return null;
            }

            var snapshot = new PawnAssignmentSnapshot();
            snapshot.capturedAtTick = Find.TickManager?.TicksGame ?? 0;

            try
            {
                // Always capture vanilla priorities as fallback
                snapshot.CaptureVanillaWorkPriorities(pawn);

                // Capture Work Tab priorities if loaded
                if (IsWorkTabLoaded())
                {
                    snapshot.CaptureWorkTabPriorities(pawn);
                }

                // Capture policies
                snapshot.CapturePolicies(pawn);

                // Capture schedule
                snapshot.CaptureSchedule(pawn);

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Captured assignment snapshot for {pawn.Name} at tick {snapshot.capturedAtTick}. " +
                        $"WorkTypes: {snapshot.workTypePriorities.Count}, " +
                        $"WorkGivers: {snapshot.workGiverPriorities.Count}, " +
                        $"HasWorkTab: {snapshot.hasWorkTabData}");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Snapshot,
                    "CaptureFrom", pawn, ex);
            }

            return snapshot;
        }

        /// <summary>
        /// Checks if Work Tab mod is loaded.
        /// </summary>
        /// <returns>True if Work Tab is loaded.</returns>
        public static bool IsWorkTabLoaded()
        {
            if (workTabLoaded.HasValue)
            {
                return workTabLoaded.Value;
            }

            try
            {
                var priorityManagerType = AccessTools.TypeByName("WorkTab.PriorityManager");
                workTabLoaded = priorityManagerType != null;

                if (workTabLoaded.Value)
                {
                    // Cache reflection info for Work Tab
                    CacheWorkTabReflection(priorityManagerType);
                    Log.Message("[Eternal] Work Tab mod detected - extended priority support enabled");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Snapshot,
                    "IsWorkTabLoaded", null, ex);
                workTabLoaded = false;
            }

            return workTabLoaded.Value;
        }

        /// <summary>
        /// Caches reflection info for Work Tab classes.
        /// </summary>
        private static void CacheWorkTabReflection(Type priorityManagerType)
        {
            try
            {
                // PriorityManager.Get static property
                priorityManagerGetProperty = AccessTools.Property(priorityManagerType, "Get");

                // PriorityTracker type and indexer
                var priorityTrackerType = AccessTools.TypeByName("WorkTab.PriorityTracker");
                if (priorityTrackerType != null)
                {
                    // PriorityTracker[Pawn] indexer - this is on PriorityManager, not PriorityTracker
                    var indexerProperty = priorityManagerType.GetProperty("Item", new[] { typeof(Pawn) });
                    if (indexerProperty != null)
                    {
                        priorityTrackerIndexerGetter = indexerProperty.GetGetMethod();
                    }

                    // PriorityTracker[WorkGiverDef] indexer
                    var workGiverIndexer = priorityTrackerType.GetProperty("Item", new[] { typeof(WorkGiverDef) });
                    if (workGiverIndexer != null)
                    {
                        workGiverIndexerGetter = workGiverIndexer.GetGetMethod();
                    }

                    // SetPriority(WorkGiverDef, int, List<int>) method
                    setWorkGiverPriorityMethod = AccessTools.Method(priorityTrackerType, "SetPriority",
                        new[] { typeof(WorkGiverDef), typeof(int), typeof(List<int>) });
                }

                // WorkPriority.Priorities property
                var workPriorityType = AccessTools.TypeByName("WorkTab.WorkPriority");
                if (workPriorityType != null)
                {
                    workPriorityPrioritiesProperty = AccessTools.Property(workPriorityType, "Priorities");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Snapshot,
                    "CacheWorkTabReflection", null, ex);
            }
        }

        #endregion

        #region Instance Methods

        /// <summary>
        /// Applies saved assignments to a resurrected pawn.
        /// Skips disabled work types and missing policies.
        /// </summary>
        /// <param name="pawn">The pawn to restore assignments to.</param>
        public void ApplyTo(Pawn pawn)
        {
            if (pawn == null)
            {
                Log.Warning("[Eternal] PawnAssignmentSnapshot.ApplyTo called with null pawn");
                return;
            }

            try
            {
                // Restore Work Tab priorities if available and Work Tab is still loaded
                if (hasWorkTabData && IsWorkTabLoaded())
                {
                    RestoreWorkTabPriorities(pawn);
                }
                else
                {
                    // Fallback to vanilla priorities
                    RestoreVanillaWorkPriorities(pawn);
                }

                // Restore policies
                RestorePolicies(pawn);

                // Restore schedule
                RestoreSchedule(pawn);

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Restored assignments for {pawn.Name} from snapshot captured at tick {capturedAtTick}");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Snapshot,
                    "ApplyTo", pawn, ex);
            }
        }

        #endregion

        #region Capture Methods

        /// <summary>
        /// Captures vanilla work priorities (WorkTypeDef level).
        /// </summary>
        private void CaptureVanillaWorkPriorities(Pawn pawn)
        {
            if (pawn.workSettings == null)
            {
                return;
            }

            workTypePriorities = new Dictionary<string, int>();

            foreach (var workType in DefDatabase<WorkTypeDef>.AllDefs)
            {
                // Only capture if pawn can do this work type
                if (!pawn.WorkTypeIsDisabled(workType))
                {
                    int priority = pawn.workSettings.GetPriority(workType);
                    workTypePriorities[workType.defName] = priority;
                }
            }
        }

        /// <summary>
        /// Captures Work Tab priorities (WorkGiverDef level with 24-hour arrays).
        /// Hardened with comprehensive null checks and try-catch blocks.
        /// Falls back gracefully to vanilla priorities on any error.
        /// </summary>
        private void CaptureWorkTabPriorities(Pawn pawn)
        {
            // Early null check on all required reflection members
            if (priorityManagerGetProperty == null || priorityTrackerIndexerGetter == null ||
                workGiverIndexerGetter == null || workPriorityPrioritiesProperty == null)
            {
                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Warning("[Eternal] Work Tab reflection not properly initialized - using vanilla fallback");
                }
                hasWorkTabData = false;
                return;
            }

            try
            {
                // Get PriorityManager.Get
                object priorityManager;
                try
                {
                    priorityManager = priorityManagerGetProperty.GetValue(null);
                }
                catch (Exception ex)
                {
                    EternalLogger.HandleException(EternalExceptionCategory.Snapshot,
                        "CaptureWorkTabPriorities_GetManager", pawn, ex);
                    hasWorkTabData = false;
                    return;
                }

                if (priorityManager == null)
                {
                    hasWorkTabData = false;
                    return;
                }

                // Get PriorityTracker for this pawn: priorityManager[pawn]
                object tracker;
                try
                {
                    tracker = priorityTrackerIndexerGetter.Invoke(priorityManager, new object[] { pawn });
                }
                catch (Exception ex)
                {
                    EternalLogger.HandleException(EternalExceptionCategory.Snapshot,
                        "CaptureWorkTabPriorities_GetTracker", pawn, ex);
                    hasWorkTabData = false;
                    return;
                }

                if (tracker == null)
                {
                    hasWorkTabData = false;
                    return;
                }

                workGiverPriorities = new Dictionary<string, int[]>();

                foreach (var workGiver in DefDatabase<WorkGiverDef>.AllDefs)
                {
                    try
                    {
                        // Get WorkPriority: tracker[workGiver]
                        var workPriority = workGiverIndexerGetter.Invoke(tracker, new object[] { workGiver });
                        if (workPriority == null)
                        {
                            continue;
                        }

                        // Get Priorities (int[24])
                        var priorities = workPriorityPrioritiesProperty.GetValue(workPriority) as int[];
                        if (priorities != null && priorities.Length == 24)
                        {
                            // Clone the array to avoid reference issues
                            workGiverPriorities[workGiver.defName] = (int[])priorities.Clone();
                        }
                    }
                    catch (Exception ex)
                    {
                        // Individual workgiver failure shouldn't stop the whole capture
                        if (Eternal_Mod.settings?.debugMode == true)
                        {
                            EternalLogger.HandleException(EternalExceptionCategory.Snapshot,
                                "CaptureWorkTabPriorities_WorkGiver", pawn, ex);
                        }
                    }
                }

                hasWorkTabData = workGiverPriorities.Count > 0;

                if (Eternal_Mod.settings?.debugMode == true && hasWorkTabData)
                {
                    Log.Message($"[Eternal] Successfully captured {workGiverPriorities.Count} Work Tab priorities for {pawn.Name}");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Snapshot,
                    "CaptureWorkTabPriorities", pawn, ex);
                hasWorkTabData = false;
                workGiverPriorities?.Clear();
            }
        }

        /// <summary>
        /// Captures outfit, drug, and food policies.
        /// </summary>
        private void CapturePolicies(Pawn pawn)
        {
            // Outfit policy
            if (pawn.outfits?.CurrentApparelPolicy != null)
            {
                outfitPolicyName = pawn.outfits.CurrentApparelPolicy.label;
            }

            // Drug policy
            if (pawn.drugs?.CurrentPolicy != null)
            {
                drugPolicyName = pawn.drugs.CurrentPolicy.label;
            }

            // Food restriction
            if (pawn.foodRestriction?.CurrentFoodPolicy != null)
            {
                foodPolicyName = pawn.foodRestriction.CurrentFoodPolicy.label;
            }
        }

        /// <summary>
        /// Captures the pawn's 24-hour schedule.
        /// </summary>
        private void CaptureSchedule(Pawn pawn)
        {
            hourlySchedule = new List<string>(24);

            if (pawn.timetable == null)
            {
                // Fill with default "Anything" for 24 hours
                for (int hour = 0; hour < 24; hour++)
                {
                    hourlySchedule.Add("Anything");
                }
                return;
            }

            for (int hour = 0; hour < 24; hour++)
            {
                var assignment = pawn.timetable.GetAssignment(hour);
                hourlySchedule.Add(assignment?.defName ?? "Anything");
            }
        }

        #endregion

        #region Restore Methods

        /// <summary>
        /// Restores vanilla work priorities (WorkTypeDef level).
        /// </summary>
        private void RestoreVanillaWorkPriorities(Pawn pawn)
        {
            if (pawn.workSettings == null || workTypePriorities == null)
            {
                return;
            }

            foreach (var kvp in workTypePriorities)
            {
                var workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(kvp.Key);
                if (workType == null)
                {
                    continue; // Work type was removed
                }

                // Skip if work type is now disabled for this pawn
                if (pawn.WorkTypeIsDisabled(workType))
                {
                    continue;
                }

                pawn.workSettings.SetPriority(workType, kvp.Value);
            }
        }

        /// <summary>
        /// Restores Work Tab priorities (WorkGiverDef level with 24-hour arrays).
        /// Hardened with comprehensive try-catch blocks and automatic fallback to vanilla.
        /// </summary>
        private void RestoreWorkTabPriorities(Pawn pawn)
        {
            // Validate that we have Work Tab data to restore
            if (workGiverPriorities == null || workGiverPriorities.Count == 0)
            {
                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] No Work Tab data to restore for {pawn.Name}, using vanilla");
                }
                RestoreVanillaWorkPriorities(pawn);
                return;
            }

            // Early null check on required reflection members
            if (priorityManagerGetProperty == null || priorityTrackerIndexerGetter == null)
            {
                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Warning("[Eternal] Work Tab reflection not available for restore - using vanilla fallback");
                }
                RestoreVanillaWorkPriorities(pawn);
                return;
            }

            try
            {
                // Get PriorityManager.Get
                object priorityManager;
                try
                {
                    priorityManager = priorityManagerGetProperty.GetValue(null);
                }
                catch (Exception ex)
                {
                    EternalLogger.HandleException(EternalExceptionCategory.Snapshot,
                        "RestoreWorkTabPriorities_GetManager", pawn, ex);
                    RestoreVanillaWorkPriorities(pawn);
                    return;
                }

                if (priorityManager == null)
                {
                    RestoreVanillaWorkPriorities(pawn);
                    return;
                }

                // Get PriorityTracker for this pawn
                object tracker;
                try
                {
                    tracker = priorityTrackerIndexerGetter.Invoke(priorityManager, new object[] { pawn });
                }
                catch (Exception ex)
                {
                    EternalLogger.HandleException(EternalExceptionCategory.Snapshot,
                        "RestoreWorkTabPriorities_GetTracker", pawn, ex);
                    RestoreVanillaWorkPriorities(pawn);
                    return;
                }

                if (tracker == null)
                {
                    RestoreVanillaWorkPriorities(pawn);
                    return;
                }

                // Cache the SetPriority method once
                MethodInfo setPriorityHourMethod = null;
                MethodInfo invalidateCacheMethod = null;
                try
                {
                    setPriorityHourMethod = AccessTools.Method(
                        tracker.GetType(), "SetPriority",
                        new[] { typeof(WorkGiverDef), typeof(int), typeof(int), typeof(bool) });

                    invalidateCacheMethod = AccessTools.Method(
                        tracker.GetType(), "InvalidateCache",
                        new[] { typeof(WorkGiverDef), typeof(bool) });
                }
                catch (Exception ex)
                {
                    EternalLogger.HandleException(EternalExceptionCategory.Snapshot,
                        "RestoreWorkTabPriorities_CacheMethods", pawn, ex);
                    RestoreVanillaWorkPriorities(pawn);
                    return;
                }

                if (setPriorityHourMethod == null)
                {
                    Log.Warning("[Eternal] Work Tab SetPriority method not found - using vanilla fallback");
                    RestoreVanillaWorkPriorities(pawn);
                    return;
                }

                int restoredCount = 0;
                foreach (var kvp in workGiverPriorities)
                {
                    var workGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(kvp.Key);
                    if (workGiver == null)
                    {
                        continue; // WorkGiver was removed
                    }

                    // Skip if work type is disabled
                    if (pawn.WorkTypeIsDisabled(workGiver.workType))
                    {
                        continue;
                    }

                    var priorities = kvp.Value;
                    if (priorities == null || priorities.Length != 24)
                    {
                        continue;
                    }

                    try
                    {
                        // Set each hour's priority
                        for (int hour = 0; hour < 24; hour++)
                        {
                            setPriorityHourMethod.Invoke(tracker, new object[] { workGiver, priorities[hour], hour, false });
                        }

                        // Call InvalidateCache to update Work Tab's internal state
                        if (invalidateCacheMethod != null)
                        {
                            invalidateCacheMethod.Invoke(tracker, new object[] { workGiver, true });
                        }

                        restoredCount++;
                    }
                    catch (Exception ex)
                    {
                        if (Eternal_Mod.settings?.debugMode == true)
                        {
                            EternalLogger.HandleException(EternalExceptionCategory.Snapshot,
                                "RestoreWorkTabPriorities_WorkGiver", pawn, ex);
                        }
                    }
                }

                // Notify work settings changed
                try
                {
                    pawn.workSettings?.Notify_UseWorkPrioritiesChanged();
                }
                catch (Exception ex)
                {
                    EternalLogger.HandleException(EternalExceptionCategory.Snapshot,
                        "RestoreWorkTabPriorities_NotifyChanged", pawn, ex);
                }

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Restored {restoredCount}/{workGiverPriorities.Count} Work Tab priorities for {pawn.Name}");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Snapshot,
                    "RestoreWorkTabPriorities", pawn, ex);
                RestoreVanillaWorkPriorities(pawn);
            }
        }

        /// <summary>
        /// Restores outfit, drug, and food policies.
        /// Each policy restoration is isolated in its own try/catch so one failure
        /// does not prevent the others from being restored. Database access uses
        /// null-safe ?. operators throughout.
        /// </summary>
        private void RestorePolicies(Pawn pawn)
        {
            // Outfit policy
            if (!string.IsNullOrEmpty(outfitPolicyName) && pawn.outfits != null)
            {
                try
                {
                    var policy = Current.Game?.outfitDatabase?.AllOutfits
                        ?.FirstOrDefault(p => p.label == outfitPolicyName);
                    if (policy != null)
                    {
                        pawn.outfits.CurrentApparelPolicy = policy;
                    }
                }
                catch (Exception ex)
                {
                    EternalLogger.HandleException(EternalExceptionCategory.Snapshot,
                        "RestorePolicies_Outfit", pawn, ex);
                }
            }

            // Drug policy
            if (!string.IsNullOrEmpty(drugPolicyName) && pawn.drugs != null)
            {
                try
                {
                    var policy = Current.Game?.drugPolicyDatabase?.AllPolicies
                        ?.FirstOrDefault(p => p.label == drugPolicyName);
                    if (policy != null)
                    {
                        pawn.drugs.CurrentPolicy = policy;
                    }
                }
                catch (Exception ex)
                {
                    EternalLogger.HandleException(EternalExceptionCategory.Snapshot,
                        "RestorePolicies_Drug", pawn, ex);
                }
            }

            // Food restriction
            if (!string.IsNullOrEmpty(foodPolicyName) && pawn.foodRestriction != null)
            {
                try
                {
                    var policy = Current.Game?.foodRestrictionDatabase?.AllFoodRestrictions
                        ?.FirstOrDefault(p => p.label == foodPolicyName);
                    if (policy != null)
                    {
                        pawn.foodRestriction.CurrentFoodPolicy = policy;
                    }
                }
                catch (Exception ex)
                {
                    EternalLogger.HandleException(EternalExceptionCategory.Snapshot,
                        "RestorePolicies_Food", pawn, ex);
                }
            }
        }

        /// <summary>
        /// Restores the pawn's 24-hour schedule.
        /// </summary>
        private void RestoreSchedule(Pawn pawn)
        {
            if (hourlySchedule == null || hourlySchedule.Count != 24 || pawn.timetable == null)
            {
                return;
            }

            for (int hour = 0; hour < 24; hour++)
            {
                var assignmentDef = DefDatabase<TimeAssignmentDef>.GetNamedSilentFail(hourlySchedule[hour]);
                if (assignmentDef != null)
                {
                    pawn.timetable.SetAssignment(hour, assignmentDef);
                }
            }
        }

        #endregion

        #region IExposable Implementation

        /// <summary>
        /// Serializes/deserializes snapshot data for save/load.
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref capturedAtTick, "capturedAtTick", 0);
            Scribe_Values.Look(ref hasWorkTabData, "hasWorkTabData", false);
            Scribe_Values.Look(ref outfitPolicyName, "outfitPolicyName");
            Scribe_Values.Look(ref drugPolicyName, "drugPolicyName");
            Scribe_Values.Look(ref foodPolicyName, "foodPolicyName");

            // Vanilla work priorities as parallel lists
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                var workTypeDefNames = workTypePriorities?.Keys.ToList() ?? new List<string>();
                var workTypePriorityValues = workTypePriorities?.Values.ToList() ?? new List<int>();
                Scribe_Collections.Look(ref workTypeDefNames, "workTypeDefNames", LookMode.Value);
                Scribe_Collections.Look(ref workTypePriorityValues, "workTypePriorities", LookMode.Value);
            }
            else
            {
                var workTypeDefNames = new List<string>();
                var workTypePriorityValues = new List<int>();
                Scribe_Collections.Look(ref workTypeDefNames, "workTypeDefNames", LookMode.Value);
                Scribe_Collections.Look(ref workTypePriorityValues, "workTypePriorities", LookMode.Value);

                workTypePriorities = new Dictionary<string, int>();
                if (workTypeDefNames != null && workTypePriorityValues != null)
                {
                    for (int i = 0; i < Math.Min(workTypeDefNames.Count, workTypePriorityValues.Count); i++)
                    {
                        if (!string.IsNullOrEmpty(workTypeDefNames[i]))
                        {
                            workTypePriorities[workTypeDefNames[i]] = workTypePriorityValues[i];
                        }
                    }
                }
            }

            // Work Tab priorities as parallel lists (defName, serialized priorities string)
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                var workGiverDefNames = workGiverPriorities?.Keys.ToList() ?? new List<string>();
                // Serialize int[24] as comma-separated string
                var workGiverPriorityStrings = workGiverPriorities?.Values
                    .Select(arr => arr != null ? string.Join(",", arr) : "")
                    .ToList() ?? new List<string>();

                Scribe_Collections.Look(ref workGiverDefNames, "workGiverDefNames", LookMode.Value);
                Scribe_Collections.Look(ref workGiverPriorityStrings, "workGiverPriorities", LookMode.Value);
            }
            else
            {
                var workGiverDefNames = new List<string>();
                var workGiverPriorityStrings = new List<string>();
                Scribe_Collections.Look(ref workGiverDefNames, "workGiverDefNames", LookMode.Value);
                Scribe_Collections.Look(ref workGiverPriorityStrings, "workGiverPriorities", LookMode.Value);

                workGiverPriorities = new Dictionary<string, int[]>();
                if (workGiverDefNames != null && workGiverPriorityStrings != null)
                {
                    for (int i = 0; i < Math.Min(workGiverDefNames.Count, workGiverPriorityStrings.Count); i++)
                    {
                        if (!string.IsNullOrEmpty(workGiverDefNames[i]) && !string.IsNullOrEmpty(workGiverPriorityStrings[i]))
                        {
                            try
                            {
                                var priorities = workGiverPriorityStrings[i].Split(',')
                                    .Select(s => int.TryParse(s, out int v) ? v : 0)
                                    .ToArray();
                                if (priorities.Length == 24)
                                {
                                    workGiverPriorities[workGiverDefNames[i]] = priorities;
                                }
                            }
                            catch (Exception ex)
                            {
                                EternalLogger.HandleException(EternalExceptionCategory.Snapshot,
                                    "ExposeData_WorkGiverDeserialization", null, ex);
                            }
                        }
                    }
                }
            }

            // Schedule
            Scribe_Collections.Look(ref hourlySchedule, "hourlySchedule", LookMode.Value);

            // Ensure hourlySchedule is initialized
            if (hourlySchedule == null)
            {
                hourlySchedule = new List<string>();
            }
        }

        #endregion
    }
}
