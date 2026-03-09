// Relative Path: Eternal/Source/Eternal/Settings/HediffSettingsXmlStore.cs
// Creation Date: 03-01-2026
// Last Edit: 21-02-2026
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
using System.Xml.Linq;
using Verse;
using Eternal.Exceptions;
using Eternal.Utils;

namespace Eternal.Settings
{
    /// <summary>
    /// Handles XML file I/O for hediff settings.
    /// Uses SafeSaver for atomic writes and performs a post-load validation
    /// pass that clamps out-of-bounds values and writes corrections back to disk.
    /// </summary>
    public static class HediffSettingsXmlStore
    {
        private const string FILENAME = "Eternal_HediffSettings.xml";
        private const string ROOT_ELEMENT = "EternalHediffSettings";
        private const int CURRENT_VERSION = 1;

        /// <summary>
        /// XML element names as they appear in the saved file (matching ExposeData labels).
        /// Used for unknown-element typo detection.
        /// </summary>
        private static readonly string[] KnownHediffSlimElements =
        {
            "defName",
            "canHeal",
            "healingRate",
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
        /// Only saves settings that have been customized (non-default values).
        /// </summary>
        /// <param name="settings">Dictionary of defName -> slim setting</param>
        public static void Save(Dictionary<string, HediffSettingSlim> settings)
        {
            if (settings == null)
            {
                Log.Warning("[Eternal] Attempted to save null settings dictionary");
                return;
            }

            string path = GetFilePath();

            try
            {
                // Filter to only save settings that are actually customized
                var customizedSettings = new List<HediffSettingSlim>();
                foreach (var kvp in settings)
                {
                    if (kvp.Value != null && IsCustomized(kvp.Value))
                    {
                        customizedSettings.Add(kvp.Value);
                    }
                }

                SafeSaver.Save(path, ROOT_ELEMENT, () =>
                {
                    int version = CURRENT_VERSION;
                    Scribe_Values.Look(ref version, "version", CURRENT_VERSION);

                    Scribe_Collections.Look(ref customizedSettings, "settings", LookMode.Deep);
                });

                Log.Message($"[Eternal] Saved {customizedSettings.Count} customized hediff settings to {path}");
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.ConfigurationError,
                    "HediffSettingsXmlStore.Save", null, ex);
            }
        }

        /// <summary>
        /// Loads hediff settings from XML file.
        /// After loading, performs a validation pass that clamps out-of-bounds values
        /// and writes corrections back to disk. Unknown XML elements are detected via
        /// a pre-load XDocument pass and reported with typo suggestions.
        /// Returns empty dictionary if file doesn't exist or loading fails.
        /// </summary>
        public static Dictionary<string, HediffSettingSlim> Load()
        {
            var result = new Dictionary<string, HediffSettingSlim>();
            string path = GetFilePath();

            if (!FileExists())
            {
                Log.Message("[Eternal] No hediff settings file found, using defaults");
                return result;
            }

            // Pre-load pass: detect unknown elements before the Scribe context opens.
            // This is non-blocking — warnings are emitted but loading continues.
            var unknownElementWarnings = DetectUnknownElements(path);
            foreach (var w in unknownElementWarnings)
            {
                Log.Warning($"[Eternal] HediffSettings.xml: {w}");
            }

            int missingCount = 0;

            try
            {
                Scribe.loader.InitLoading(path);

                try
                {
                    int version = 1;
                    Scribe_Values.Look(ref version, "version", 1);

                    // Handle version migrations if needed in the future
                    if (version > CURRENT_VERSION)
                    {
                        Log.Warning($"[Eternal] Settings file version {version} is newer than supported version {CURRENT_VERSION}");
                    }

                    List<HediffSettingSlim> settingsList = null;
                    Scribe_Collections.Look(ref settingsList, "settings", LookMode.Deep);

                    if (settingsList != null)
                    {
                        foreach (var setting in settingsList)
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

                    Log.Message($"[Eternal] Loaded {result.Count} customized hediff settings from {path}");
                }
                finally
                {
                    // FinalizeLoading MUST complete before any Save() call (Pitfall 1).
                    Scribe.loader.FinalizeLoading();
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.ConfigurationError,
                    "HediffSettingsXmlStore.Load", null, ex);
                Scribe.ForceStop();
                return result;
            }

            // Validation pass — runs AFTER FinalizeLoading() to avoid Scribe context corruption.
            bool anyCorrection = false;
            int clampedFieldCount = 0;

            foreach (var setting in result.Values)
            {
                var fieldWarnings = new List<string>();
                bool corrected = setting.Validate(fieldWarnings);

                foreach (var w in fieldWarnings)
                {
                    Log.Warning($"[Eternal] HediffSettings.xml: {w}");
                }

                if (corrected)
                {
                    anyCorrection = true;
                    clampedFieldCount += fieldWarnings.Count;
                }
            }

            // Summary log — only emitted when there is something to report.
            if (anyCorrection || missingCount > 0)
            {
                Log.Warning(
                    $"[Eternal] HediffSettings.xml: {clampedFieldCount} field(s) clamped, " +
                    $"{missingCount} missing/invalid entry(s) skipped.");
            }

            // Write-back — only if corrections were made. Safe because FinalizeLoading is done.
            if (anyCorrection)
            {
                Save(result);
            }

            return result;
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
                return null;

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
            if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
            if (string.IsNullOrEmpty(b)) return a.Length;

            int[] prev = new int[b.Length + 1];
            int[] curr = new int[b.Length + 1];

            for (int j = 0; j <= b.Length; j++)
                prev[j] = j;

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

        /// <summary>
        /// Checks if a slim setting has been customized (differs from defaults).
        /// </summary>
        private static bool IsCustomized(HediffSettingSlim setting)
        {
            // A setting is customized if:
            // - It has a custom healing rate (not using global)
            // - Or it has a custom nutrition cost
            // - Or canHeal differs from the hediff's default
            // Note: We can't easily check canHeal default here, so we save it if ANY field is non-default
            return setting.HasCustomHealingRate ||
                   setting.nutritionCostMultiplier != 1.0f ||
                   !setting.canHeal; // Save if canHeal is false (since default varies, we conservatively save disabled hediffs)
        }
    }
}
