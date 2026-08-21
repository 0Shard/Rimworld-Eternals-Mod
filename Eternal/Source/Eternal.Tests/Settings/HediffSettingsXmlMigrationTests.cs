// Relative Path: Eternal/Source/Eternal.Tests/Settings/HediffSettingsXmlMigrationTests.cs
// Creation Date: 16-07-2026
// Last Edit: 16-07-2026
// Author: 0Shard
// Description: Contracts for recoverable Hediff XML v1-to-v2 migration.
//              The tests prove legacy nutritionCost is discarded and never interpreted
//              as the new noThreshold boolean.

using System.Linq;
using System.Xml.Linq;
using Xunit;
using Eternal.Settings;

namespace Eternal.Tests.Settings
{
    public class HediffSettingsXmlMigrationTests
    {
        [Fact]
        public void CurrentVersion_IsTheV2Schema()
        {
            Assert.Equal(2, HediffSettingsXmlStore.CURRENT_VERSION);
        }

        [Fact]
        public void SparseV1Migration_UsesDefaultsForMissingVersionAndFields()
        {
            var source = XDocument.Parse(
                "<EternalHediffSettings><settings>" +
                "<li><defName>SparseCondition</defName></li>" +
                "</settings></EternalHediffSettings>");

            Assert.True(HediffSettingsXmlMigration.TryMigrateV1(source, out XDocument result));

            XElement entry = result.Root.Element("settings").Element("li");
            Assert.Equal(2, (int)result.Root.Element("version"));
            Assert.Equal("SparseCondition", (string)entry.Element("defName"));
            Assert.Equal("true", ((string)entry.Element("canHeal")).ToLowerInvariant());
            Assert.Equal("-1", (string)entry.Element("healingRate"));
            Assert.Equal("false", ((string)entry.Element("noThreshold")).ToLowerInvariant());
            Assert.Null(entry.Element("nutritionCost"));
        }

        [Fact]
        public void V1Migration_PreservesHealingFieldsAndAddsFalseThresholdFlag()
        {
            var source = XDocument.Parse(
                "<EternalHediffSettings><version>1</version><settings>" +
                "<li><defName>Plague</defName><canHeal>True</canHeal>" +
                "<healingRate>0.025</healingRate><nutritionCost>7.5</nutritionCost></li>" +
                "</settings></EternalHediffSettings>");

            bool migrated = HediffSettingsXmlMigration.TryMigrateV1(source, out XDocument result);
            XElement entry = result.Root.Element("settings").Element("li");

            Assert.True(migrated);
            Assert.Equal(2, (int)result.Root.Element("version"));
            Assert.Equal("Plague", (string)entry.Element("defName"));
            Assert.Equal("true", ((string)entry.Element("canHeal")).ToLowerInvariant());
            Assert.Equal("0.025", (string)entry.Element("healingRate"));
            Assert.Equal("false", ((string)entry.Element("noThreshold")).ToLowerInvariant());
            Assert.Null(entry.Element("nutritionCost"));
        }

        [Fact]
        public void V1Migration_DoesNotAliasLegacyFloatNutritionCostToNoThreshold()
        {
            var source = XDocument.Parse(
                "<EternalHediffSettings><version>1</version><settings>" +
                "<li><defName>Infection</defName><canHeal>False</canHeal>" +
                "<healingRate>-1</healingRate><nutritionCost>0</nutritionCost></li>" +
                "<li><defName>BadRate</defName><canHeal>True</canHeal>" +
                "<healingRate>0.1</healingRate><nutritionCost>1</nutritionCost></li>" +
                "</settings></EternalHediffSettings>");

            Assert.True(HediffSettingsXmlMigration.TryMigrateV1(source, out XDocument result));

            var entries = result.Root.Element("settings").Elements("li").ToArray();
            Assert.All(entries, entry => Assert.Equal("false", ((string)entry.Element("noThreshold")).ToLowerInvariant()));
            Assert.All(entries, entry => Assert.Null(entry.Element("nutritionCost")));
        }

        [Fact]
        public void V1Migration_RejectsAnInvalidRootWithoutProducingWriteBackData()
        {
            var source = XDocument.Parse("<OtherSettings><version>1</version></OtherSettings>");

            Assert.False(HediffSettingsXmlMigration.TryMigrateV1(source, out XDocument result));
            Assert.Null(result);
        }

        [Fact]
        public void V1Migration_RejectsMalformedLegacyNutritionCost()
        {
            var source = XDocument.Parse(
                "<EternalHediffSettings><version>1</version><settings>" +
                "<li><defName>BadCost</defName><canHeal>true</canHeal>" +
                "<healingRate>0.01</healingRate><nutritionCost>not-a-float</nutritionCost></li>" +
                "</settings></EternalHediffSettings>");

            Assert.False(HediffSettingsXmlMigration.TryMigrateV1(source, out XDocument result));
            Assert.Null(result);
        }

    }
}
