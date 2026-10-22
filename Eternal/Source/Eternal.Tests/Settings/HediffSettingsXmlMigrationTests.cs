// Relative Path: Eternal/Source/Eternal.Tests/Settings/HediffSettingsXmlMigrationTests.cs
// Creation Date: 16-07-2026
// Last Edit: 16-07-2026
// Author: 0Shard
// Description: Contracts for recoverable Hediff XML v1-to-v2 migration.
//              The tests prove legacy nutritionCost is discarded and never interpreted
//              as the new noThreshold boolean.

using System.Linq;
using System.Xml.Linq;
using Eternal.Compatibility;
using Xunit;
using Eternal.Settings;
using Eternal.Tests.UI;

namespace Eternal.Tests.Settings
{
    public class HediffSettingsXmlMigrationTests
    {
        [Fact]
        public void NutritionOverrides_RetainFullSlimSaveResetAndBulkCopyWiring()
        {
            foreach (string field in new[] { "nutritionCostMultiplier", "nutritionSeverityRangeOverride" })
            {
                foreach (string method in new[] { "ApplyTo", ".ctor", "ExposeData" })
                {
                    var il = method == ".ctor" ? MenuAssemblyContract.Instructions("Eternal.Settings.HediffSettingSlim", method,
                        "System.String", "Eternal.EternalHediffSetting") :
                        MenuAssemblyContract.Instructions("Eternal.Settings.HediffSettingSlim", method);
                    Assert.Contains(il, i => Equals(i.Operand, field));
                }
                Assert.Contains(MenuAssemblyContract.Instructions("Eternal.EternalHediffSetting", "ResetToDefaults"), i => Equals(i.Operand, field));
                Assert.Contains(MenuAssemblyContract.Instructions("Eternal.EternalHediffManager", "ApplySettingsToGroupWithCount"), i => Equals(i.Operand, field));
            }
            Assert.Equal(1f, SettingsDefaults.NormalizeNutritionMultiplier(float.NaN));
            Assert.Equal(0f, SettingsDefaults.NormalizeNutritionRange(float.PositiveInfinity));
            Assert.Equal(0f, SettingsDefaults.NormalizeNutritionRange(-1f));
        }
        [Theory]
        [InlineData("0.001")]
        [InlineData("0.005")]
        [InlineData("3")]
        [InlineData("-1")]
        public void LegacyMigration_PreservesCustomAndInheritedRateText(string rate)
        {
            var source = XDocument.Parse("<EternalHediffSettings><version>1</version><settings><li>" +
                "<defName>Condition</defName><healingRate>" + rate + "</healingRate>" +
                "</li></settings></EternalHediffSettings>");
            Assert.True(HediffSettingsXmlMigration.TryMigrateV1(source, out XDocument migrated));
            Assert.Equal(rate, (string)migrated.Root.Element("settings").Element("li").Element("healingRate"));
        }
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
            Assert.Equal(new[] { false, true }, entries.Select(entry => (bool)entry.Element("canHeal")));
            Assert.All(entries, entry => Assert.Equal("false", ((string)entry.Element("noThreshold")).ToLowerInvariant()));
            Assert.All(entries, entry => Assert.Null(entry.Element("nutritionCost")));
        }

        [Theory]
        [InlineData("Sterilized", false)]
        [InlineData("DecreasedFertility", false)]
        [InlineData("RJW_IUD", true)]
        [InlineData("ImpregnationBlocker", true)]
        [InlineData("IncreasedFertility", true)]
        [InlineData("FertilityEnhancer", true)]
        public void V1Migration_PreservesExplicitRjwChoiceOppositeContextDefault(string defName, bool canHeal)
        {
            var kind = RjwHealthPolicy.Classify("rim.job.world", defName, null, true);
            Assert.Equal(!canHeal, RjwHealthPolicy.DefaultCanHeal(kind));
            var source = new XDocument(new XElement("EternalHediffSettings",
                new XElement("version", 1), new XElement("settings", new XElement("li",
                    new XElement("defName", defName), new XElement("canHeal", canHeal),
                    new XElement("healingRate", -1), new XElement("nutritionCost", "0.004")))));

            Assert.True(HediffSettingsXmlMigration.TryMigrateV1(source, out XDocument migrated));

            var entry = Assert.Single(migrated.Root.Element("settings").Elements("li"));
            Assert.Equal(defName, (string)entry.Element("defName"));
            Assert.Equal(canHeal, (bool)entry.Element("canHeal"));
            Assert.Equal(-1, (int)entry.Element("healingRate"));
            Assert.Equal(SettingsDefaults.HediffNoThreshold, (bool)entry.Element("noThreshold"));
            Assert.Null(entry.Element("nutritionCost"));
            Assert.Equal(canHeal, (bool)source.Root.Element("settings").Element("li").Element("canHeal"));
            Assert.NotNull(source.Root.Element("settings").Element("li").Element("nutritionCost"));
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
