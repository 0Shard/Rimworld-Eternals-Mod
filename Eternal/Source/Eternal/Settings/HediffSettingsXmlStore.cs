// Relative Path: Eternal/Source/Eternal/Settings/HediffSettingsXmlStore.cs
// Creation Date: 03-01-2026
// Last Edit: 16-07-2026
// Author: 0Shard
// Description: XML file I/O for hediff settings using RimWorld's SafeSaver.
//              Stores settings in Config folder as Eternal_HediffSettings.xml.
//              Uses atomic writes with backup (.old file) for data safety.
//              Includes validation pass (bounds clamping), unknown element detection
//              with Levenshtein typo suggestions, and auto-fix write-back.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Xml.Linq;
using Verse;
using Eternal.Exceptions;
using Eternal.Utils;

namespace Eternal.Settings
{
    public enum HediffSettingsLoadOutcome { Missing, Valid, Rejected }

    public sealed class HediffSettingsLoadResult
    {
        public HediffSettingsLoadOutcome Outcome { get; }
        public Dictionary<string, HediffSettingSlim> Settings { get; }

        internal HediffSettingsLoadResult(HediffSettingsLoadOutcome outcome, Dictionary<string, HediffSettingSlim> settings = null)
        {
            Outcome = outcome;
            Settings = settings ?? new Dictionary<string, HediffSettingSlim>();
        }
    }

    /// <summary>
    /// Handles XML file I/O for hediff settings.
    /// Uses SafeSaver for atomic writes and performs a post-load validation
    /// pass that clamps out-of-bounds values and writes corrections back to disk.
    /// </summary>
    public static class HediffSettingsXmlStore
    {
        private const string FILENAME = "Eternal_HediffSettings.xml";
        private const string ROOT_ELEMENT = "EternalHediffSettings";
        internal const int CURRENT_VERSION = 2;

        /// <summary>
        /// XML element names as they appear in the saved file (matching ExposeData labels).
        /// Used for unknown-element typo detection.
        /// </summary>
        private static readonly string[] KnownHediffSlimElements =
        {
            "defName",
            "canHeal",
            "healingRate",
            "noThreshold",
            "nutritionCostMultiplier",
            "nutritionSeverityRangeOverride",
            // Accepted only as a legacy v1 input and discarded during migration.
            "nutritionCost",
        };

        /// <summary>
        /// Gets the full path to the settings file.
        /// </summary>
        public static string GetFilePath()
        {
            return Path.Combine(GenFilePaths.ConfigFolderPath, FILENAME);
        }

        /// <summary>
        /// Checks if the settings file exists.
        /// </summary>
        public static bool FileExists()
        {
            return File.Exists(GetFilePath());
        }

        /// <summary>
        /// Saves hediff settings to XML file.
        /// Preserves every non-null entry supplied by the context-aware settings store
        /// or migration. Those callers own selection; slim values alone cannot determine
        /// whether enabling healing differs from a particular hediff's default.
        /// </summary>
        /// <param name="settings">Dictionary of defName -> slim setting</param>
        /// <returns>True only when SafeSaver completes and the target file exists.</returns>
        public static bool Save(Dictionary<string, HediffSettingSlim> settings)
        {
            if (settings == null)
            {
                Log.Warning("[Eternal] Attempted to save null settings dictionary");
                return false;
            }

            string path = GetFilePath();

            try
            {
                // Preserve caller-selected entries, including healing enabled for default-off defs.
                var customizedSettings = new List<HediffSettingSlim>();
                foreach (var kvp in settings)
                {
                    if (kvp.Value != null)
                    {
                        customizedSettings.Add(kvp.Value);
                    }
                }

                SafeSaver.Save(path, ROOT_ELEMENT, () =>
                {
                    int version = CURRENT_VERSION;
                    Scribe_Values.Look(ref version, "version", CURRENT_VERSION, forceSave: true);

                    Scribe_Collections.Look(ref customizedSettings, "settings", LookMode.Deep);
                });

                if (!File.Exists(path))
                {
                    Log.Warning($"[Eternal] Hediff settings save did not produce {path}");
                    return false;
                }

                Log.Message($"[Eternal] Saved {customizedSettings.Count} customized hediff settings to {path}");
                return true;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.ConfigurationError,
                    "HediffSettingsXmlStore.Save", null, ex);
                return false;
            }
        }

        /// <summary>
        /// Loads hediff settings from XML. Version 1 is deserialized into an explicit legacy
        /// shape first, then converted to the v2 shape. Write-back happens only after
        /// FinalizeLoading and a successful recoverable migration.
        /// </summary>
        public static HediffSettingsLoadResult Load()
        {
            var result = new Dictionary<string, HediffSettingSlim>();
            string path = GetFilePath();

            if (!FileExists())
            {
                Log.Message("[Eternal] No hediff settings file found, using defaults");
                return new HediffSettingsLoadResult(HediffSettingsLoadOutcome.Missing);
            }

            XDocument sourceDocument;
            try
            {
                sourceDocument = XDocument.Load(path);
            }
            catch (Exception ex)
            {
                Log.Warning($"[Eternal] HediffSettings.xml schema preflight failed: {ex.Message}");
                return new HediffSettingsLoadResult(HediffSettingsLoadOutcome.Rejected);
            }

            if (sourceDocument.Root?.Name != ROOT_ELEMENT)
            {
                Log.Warning("[Eternal] HediffSettings.xml has an invalid root; keeping the source unchanged.");
                return new HediffSettingsLoadResult(HediffSettingsLoadOutcome.Rejected);
            }

            int detectedVersion = ReadVersion(sourceDocument);
            bool migratingV1 = detectedVersion <= 1;
            if (migratingV1 && sourceDocument != null
                && !HediffSettingsXmlMigration.TryMigrateV1(sourceDocument, out _))
            {
                Log.Warning("[Eternal] HediffSettings.xml v1 migration was not recoverable; keeping the source file unchanged.");
                return new HediffSettingsLoadResult(HediffSettingsLoadOutcome.Rejected);
            }
            if (!migratingV1 && !HasReadableFields(sourceDocument))
            {
                Log.Warning("[Eternal] HediffSettings.xml contains unreadable fields; keeping the source unchanged.");
                return new HediffSettingsLoadResult(HediffSettingsLoadOutcome.Rejected);
            }

            // Pre-load pass: detect unknown elements before the Scribe context opens.
            foreach (string warning in DetectUnknownElements(path))
            {
                Log.Warning($"[Eternal] HediffSettings.xml: {warning}");
            }

            int missingCount = 0;
            bool migrationSucceeded = !migratingV1;

            try
            {
                Scribe.loader.InitLoading(path);

                try
                {
                    int version = detectedVersion;
                    Scribe_Values.Look(ref version, "version", 1);

                    if (version > CURRENT_VERSION)
                    {
                        Log.Warning($"[Eternal] Settings file version {version} is newer than supported version {CURRENT_VERSION}");
                    }

                    if (version <= 1)
                    {
                        // Explicit v1 DTO: nutritionCost is read as a float and discarded.
                        // It can never be consumed by the v2 noThreshold bool.
                        List<HediffSettingV1> legacySettings = null;
                        Scribe_Collections.Look(ref legacySettings, "settings", LookMode.Deep);

                        if (legacySettings != null)
                        {
                            foreach (HediffSettingV1 legacySetting in legacySettings)
                            {
                                if (legacySetting != null && !string.IsNullOrEmpty(legacySetting.defName))
                                {
                                    result[legacySetting.defName] = new HediffSettingSlim
                                    {
                                        defName = legacySetting.defName,
                                        canHeal = legacySetting.canHeal,
                                        healingRate = legacySetting.healingRate,
                                        noThreshold = SettingsDefaults.HediffNoThreshold
                                    };
                                }
                                else
                                {
                                    missingCount++;
                                }
                            }
                        }

                        migrationSucceeded = true;
                    }
                    else
                    {
                        List<HediffSettingSlim> settingsList = null;
                        Scribe_Collections.Look(ref settingsList, "settings", LookMode.Deep);

                        if (settingsList != null)
                        {
                            foreach (HediffSettingSlim setting in settingsList)
                            {
                                if (setting != null && !string.IsNullOrEmpty(setting.defName))
                                {
                                    result[setting.defName] = setting;
                                }
                                else
                                {
                                    missingCount++;
                                }
                            }
                        }
                    }

                    Log.Message($"[Eternal] Loaded {result.Count} customized hediff settings from {path}");
                }
                finally
                {
                    // FinalizeLoading MUST complete before any SafeSaver write-back.
                    Scribe.loader.FinalizeLoading();
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.ConfigurationError,
                    "HediffSettingsXmlStore.Load", null, ex);
                Scribe.ForceStop();
                return new HediffSettingsLoadResult(HediffSettingsLoadOutcome.Rejected);
            }

            bool anyCorrection = false;
            int clampedFieldCount = 0;
            foreach (HediffSettingSlim setting in result.Values)
            {
                var fieldWarnings = new List<string>();
                bool corrected = setting.Validate(fieldWarnings);

                foreach (string warning in fieldWarnings)
                {
                    Log.Warning($"[Eternal] HediffSettings.xml: {warning}");
                }

                if (corrected)
                {
                    anyCorrection = true;
                    clampedFieldCount += fieldWarnings.Count;
                }
            }

            if (anyCorrection || missingCount > 0)
            {
                Log.Warning(
                    $"[Eternal] HediffSettings.xml: {clampedFieldCount} field(s) clamped, " +
                    $"{missingCount} missing/invalid entry(s) skipped.");
            }

            // SafeSaver writes only after the legacy DTO has loaded and Scribe finalized.
            if (migrationSucceeded && (migratingV1 || anyCorrection))
            {
                Save(result);
            }

            return new HediffSettingsLoadResult(HediffSettingsLoadOutcome.Valid, result);
        }

        internal static bool BackupRejectedSource()
        {
            string path = GetFilePath();
            if (!File.Exists(path)) { return true; }
            try
            {
                string backup = path + ".rejected-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + "-" + Guid.NewGuid().ToString("N") + ".bak";
                File.Copy(path, backup, false);
                Log.Message($"[Eternal] Preserved rejected hediff settings at {backup}");
                return true;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.ConfigurationError,
                    "HediffSettingsXmlStore.BackupRejectedSource", null, ex);
                return false;
            }
        }

        /// <summary>
        /// Deletes the settings file (for global reset).
        /// </summary>
        public static void Delete()
        {
            string path = GetFilePath();

            if (FileExists())
            {
                try
                {
                    File.Delete(path);
                    Log.Message($"[Eternal] Deleted hediff settings file: {path}");

                    // Also delete backup files if they exist
                    string oldPath = path + ".old";
                    if (File.Exists(oldPath))
                    {
                        File.Delete(oldPath);
                    }
                }
                catch (Exception ex)
                {
                    EternalLogger.HandleException(EternalExceptionCategory.ConfigurationError,
                        "HediffSettingsXmlStore.Delete", null, ex);
                }
            }
        }

        // -------------------------------------------------------------------------
        // Private helpers
        // -------------------------------------------------------------------------

        private static bool HasReadableFields(XDocument document)
        {
            var rows = document.Root?.Element("settings");
            if (rows == null) { return false; }
            foreach (var row in rows.Elements("li"))
            {
                if (string.IsNullOrWhiteSpace(row.Element("defName")?.Value)) { return false; }
                foreach (string field in new[] { "canHeal", "noThreshold" })
                {
                    var element = row.Element(field);
                    if (element != null && !bool.TryParse(element.Value, out _)) { return false; }
                }
                foreach (string field in new[] { "healingRate", "nutritionCostMultiplier", "nutritionSeverityRangeOverride" })
                {
                    var element = row.Element(field);
                    if (element != null && !float.TryParse(element.Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out _)) { return false; }
                }
            }
            return true;
        }

        private static int ReadVersion(XDocument sourceDocument)
        {
            if (sourceDocument?.Root == null)
            {
                return 1;
            }

            return int.TryParse(sourceDocument.Root.Element("version")?.Value, out int version)
                ? version
                : 1;
        }

        /// <summary>
        /// Legacy v1 DTO. The obsolete nutritionCost is intentionally a float so malformed
        /// legacy values cannot be interpreted as the v2 noThreshold flag.
        /// </summary>
        private sealed class HediffSettingV1 : IExposable
        {
            public string defName = "";
            public bool canHeal = SettingsDefaults.HediffCanHeal;
            public float healingRate = SettingsDefaults.HediffHealingRateUseGlobal;
            public float nutritionCost;

            public void ExposeData()
            {
                Scribe_Values.Look(ref defName, "defName", "");
                Scribe_Values.Look(ref canHeal, "canHeal", SettingsDefaults.HediffCanHeal);
                Scribe_Values.Look(ref healingRate, "healingRate", SettingsDefaults.HediffHealingRateUseGlobal);
                Scribe_Values.Look(ref nutritionCost, "nutritionCost", 0f);
            }
        }

        /// <summary>
        /// Performs a pre-load XDocument pass over the settings file to detect XML
        /// element names that are not recognised by HediffSettingSlim.ExposeData().
        /// Each unknown element is reported with a "did you mean?" suggestion when the
        /// Levenshtein edit distance to the closest known field name is three or fewer.
        /// Failures are non-blocking — a failed XDocument.Load returns an empty list.
        /// </summary>
        private static List<string> DetectUnknownElements(string path)
        {
            var warnings = new List<string>();

            try
            {
                XDocument doc = XDocument.Load(path);

                // Navigate: root -> <settings> -> each <li>
                IEnumerable<XElement> items = doc.Root
                    ?.Element("settings")
                    ?.Elements("li")
                    ?? Enumerable.Empty<XElement>();

                foreach (XElement li in items)
                {
                    foreach (XElement child in li.Elements())
                    {
                        string name = child.Name.LocalName;
                        bool known = KnownHediffSlimElements
                            .Any(k => string.Equals(k, name, StringComparison.Ordinal));

                        if (!known)
                        {
                            string suggestion = FindClosestField(name, KnownHediffSlimElements);
                            string msg = suggestion != null
                                ? $"Unknown element '{name}' — did you mean '{suggestion}'?"
                                : $"Unknown element '{name}'.";
                            warnings.Add(msg);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Non-blocking: log at message level, not warning, so players are not alarmed.
                Log.Message($"[Eternal] HediffSettings.xml pre-load scan failed (non-critical): {ex.Message}");
            }

            return warnings;
        }

        /// <summary>
        /// Returns the closest field name from <paramref name="knownFields"/> when the
        /// Levenshtein distance from <paramref name="unknown"/> is three or fewer AND the
        /// unknown string is at least four characters long (avoids spurious matches for
        /// very short strings). Returns null if no suitable suggestion exists.
        /// </summary>
        private static string FindClosestField(string unknown, string[] knownFields)
        {
            if (unknown == null || unknown.Length < 4)
            {
                return null;
            }

            string best = null;
            int bestDist = int.MaxValue;

            foreach (string known in knownFields)
            {
                int dist = LevenshteinDistance(unknown, known);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = known;
                }
            }

            return bestDist <= 3 ? best : null;
        }

        /// <summary>
        /// Standard iterative Levenshtein distance between two strings.
        /// Case-sensitive. Allocates a single (n+1)-element int array per call.
        /// </summary>
        private static int LevenshteinDistance(string a, string b)
        {
            if (string.IsNullOrEmpty(a))
            {
                return b?.Length ?? 0;
            }

            if (string.IsNullOrEmpty(b))
            {
                return a.Length;
            }

            int[] prev = new int[b.Length + 1];
            int[] curr = new int[b.Length + 1];

            for (int j = 0; j <= b.Length; j++)
            {
                prev[j] = j;
            }

            for (int i = 1; i <= a.Length; i++)
            {
                curr[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(
                        Math.Min(curr[j - 1] + 1, prev[j] + 1),
                        prev[j - 1] + cost);
                }

                // Swap rows to avoid re-allocation
                int[] tmp = prev;
                prev = curr;
                curr = tmp;
            }

            return prev[b.Length];
        }
    }
}
