// Relative Path: Eternal/Source/Eternal/Settings/HediffDefTextCache.cs
// Creation Date: 10-07-2026
// Last Edit: 10-07-2026
// Author: 0Shard
// Description: Caches hediff def labels and descriptions by defName. Defs are immutable at
//              runtime, so entries never invalidate. Replaces per-frame/per-rebuild
//              DefDatabase lookups + LabelCap translations in the hediff settings UI
//              (row drawing, sort, and search filter), which caused visible lag.

using System.Collections.Generic;
using Verse;

namespace Eternal.Settings
{
    /// <summary>
    /// Lazy per-defName cache for hediff display labels and descriptions.
    /// </summary>
    public static class HediffDefTextCache
    {
        private static readonly Dictionary<string, string> labels = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> descriptions = new Dictionary<string, string>();

        /// <summary>
        /// Human-readable label (LabelCap). Falls back to defName for unresolvable defs.
        /// </summary>
        public static string GetLabel(string defName)
        {
            if (string.IsNullOrEmpty(defName))
                return string.Empty;

            if (labels.TryGetValue(defName, out var cachedLabel))
                return cachedLabel;

            var hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
            string label = hediffDef != null ? hediffDef.LabelCap.ToString() : defName;
            labels[defName] = label;
            return label;
        }

        /// <summary>
        /// Def description, or empty string for unresolvable defs.
        /// </summary>
        public static string GetDescription(string defName)
        {
            if (string.IsNullOrEmpty(defName))
                return string.Empty;

            if (descriptions.TryGetValue(defName, out var cachedDescription))
                return cachedDescription;

            var hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
            string description = hediffDef?.description ?? string.Empty;
            descriptions[defName] = description;
            return description;
        }
    }
}
