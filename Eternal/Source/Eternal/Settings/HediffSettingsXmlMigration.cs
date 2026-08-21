// Relative Path: Eternal/Source/Eternal/Settings/HediffSettingsXmlMigration.cs
// Creation Date: 16-07-2026
// Last Edit: 16-07-2026
// Author: 0Shard
// Description: Pure, recoverable transformation for Hediff settings XML v1 to v2.
//              It copies only supported healing fields, explicitly reads and discards
//              legacy float nutritionCost, and creates a separate false noThreshold flag.

using System.Globalization;
using System.Xml.Linq;

namespace Eternal.Settings
{
    /// <summary>
    /// Converts the legacy Hediff settings document into the v2 schema before any v2
    /// deserialization can see the data. Any invalid row makes the pass non-recoverable,
    /// so write-back cannot discard an unrecognized legacy entry.
    /// </summary>
    internal static class HediffSettingsXmlMigration
    {
        private const string RootElementName = "EternalHediffSettings";
        private const string SettingsElementName = "settings";
        private const string ItemElementName = "li";
        private const int SourceVersion = 1;
        private const int TargetVersion = 2;

        /// <summary>
        /// Migrates a v1 document to an in-memory v2 document.
        /// </summary>
        /// <returns>False when the document cannot be safely recognized or transformed.</returns>
        public static bool TryMigrateV1(XDocument source, out XDocument migrated)
        {
            migrated = null;

            if (source?.Root == null || source.Root.Name.LocalName != RootElementName)
                return false;

            XElement versionElement = source.Root.Element("version");
            if (versionElement != null
                && (!int.TryParse(versionElement.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int version)
                    || version != SourceVersion))
            {
                return false;
            }

            XElement sourceSettings = source.Root.Element(SettingsElementName);
            if (sourceSettings == null)
                return false;

            var targetSettings = new XElement(SettingsElementName);
            bool allEntriesValid = true;
            foreach (XElement sourceEntry in sourceSettings.Elements(ItemElementName))
            {
                if (!TryMigrateEntry(sourceEntry, out XElement targetEntry))
                {
                    allEntriesValid = false;
                    continue;
                }

                targetSettings.Add(targetEntry);
            }

            if (!allEntriesValid)
                return false;

            migrated = new XDocument(
                new XElement(
                    RootElementName,
                    new XElement("version", TargetVersion),
                    targetSettings));
            return true;
        }

        private static bool TryMigrateEntry(XElement sourceEntry, out XElement targetEntry)
        {
            targetEntry = null;
            if (sourceEntry == null)
                return false;

            string defName = sourceEntry.Element("defName")?.Value?.Trim();
            if (string.IsNullOrEmpty(defName))
                return false;

            bool canHeal = SettingsDefaults.HediffCanHeal;
            string canHealText = sourceEntry.Element("canHeal")?.Value?.Trim();
            if (!string.IsNullOrEmpty(canHealText)
                && !bool.TryParse(canHealText, out canHeal))
            {
                return false;
            }

            string healingRateText = sourceEntry.Element("healingRate")?.Value?.Trim();
            if (string.IsNullOrEmpty(healingRateText))
            {
                healingRateText = SettingsDefaults.HediffHealingRateUseGlobal
                    .ToString(CultureInfo.InvariantCulture);
            }

            if (!float.TryParse(
                    healingRateText,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float healingRate)
                || float.IsNaN(healingRate)
                || float.IsInfinity(healingRate))
            {
                return false;
            }

            // This is intentionally a float read, never a bool alias. The v1 value is
            // obsolete and is discarded after being recognized as legacy input.
            string legacyNutritionCost = sourceEntry.Element("nutritionCost")?.Value?.Trim();
            if (!string.IsNullOrEmpty(legacyNutritionCost))
            {
                bool legacyNutritionCostValid = float.TryParse(
                    legacyNutritionCost,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float discardedNutritionCost);
                if (!legacyNutritionCostValid
                    || float.IsNaN(discardedNutritionCost)
                    || float.IsInfinity(discardedNutritionCost))
                {
                    // A malformed legacy float is not recoverable; leave the source untouched.
                    return false;
                }
            }

            targetEntry = new XElement(
                ItemElementName,
                new XElement("defName", defName),
                new XElement("canHeal", canHeal),
                new XElement("healingRate", healingRateText),
                new XElement("noThreshold", SettingsDefaults.HediffNoThreshold));
            return true;
        }
    }
}
