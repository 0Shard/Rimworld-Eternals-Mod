// Author: 0Shard
// Description: Exact Single snapshot comparison without mutating native reports or aliasing identifiers.
#if DEBUG
using System.Collections.Generic;
using System.Xml.Linq;
using Eternal.InGameTests.FailureScenarios;
using Xunit;

namespace Eternal.Tests.Infrastructure
{
    public class PersistenceFloatSnapshotTests
    {
        [Fact]
        public void CrossRuntimeDecimalSpellingsWithIdenticalSingleBitsCompareEqual()
        {
            var expected = XElement.Parse("<actor id='16777217'><corpse totalCost='0.0600000024'><queue><item loadId='42' energy='0.0600000024'/></queue><capturedQueue><item loadId='42' energy='0.0600000024'/></capturedQueue></corpse></actor>");
            var actual = XElement.Parse("<actor id='16777217'><corpse totalCost='0.060000002'><queue><item loadId='42' energy='0.060000002'/></queue><capturedQueue><item loadId='42' energy='0.060000002'/></capturedQueue></corpse></actor>");
            string sourceText = expected.ToString();
            string actualText = actual.ToString();
            Assert.True(Equal(expected, actual));
            Assert.Equal(sourceText, expected.ToString());
            Assert.Equal(actualText, actual.ToString());
        }

        [Fact]
        public void HealthDebtRegrowthAndQueueFieldsAllUseExactSingleBits()
        {
            string source = "<actor id='16777217' food='0.0600000024'><health><hediff loadId='16777217' severity='0.0600000024' partMaxHp='0.0600000024' phase='2'/></health>" +
                "<debt amount='0.0600000024' peak='0.0600000024' baseline='0.0600000024' owners='1'/>" +
                "<corpse id='16777217' progress='0.0600000024' foodDebt='0.0600000024' totalCost='0.0600000024'><queue>" +
                "<item loadId='16777217' severity='0.0600000024' priority='0.0600000024' time='0.0600000024' energy='0.0600000024' type='Injury'/></queue></corpse></actor>";
            Assert.True(Equal(XElement.Parse(source), XElement.Parse(source.Replace("0.0600000024", "0.060000002"))));
        }

        [Fact]
        public void OneSingleBitDifferenceHasNoTolerance()
        {
            Assert.False(Equal(XElement.Parse("<debt amount='0.0600000024'/>"), XElement.Parse("<debt amount='0.060000006'/>")));
            Assert.False(Equal(XElement.Parse("<debt amount='0'/>"), XElement.Parse("<debt amount='-0'/>")));
        }

        [Theory]
        [InlineData("actor", "id")]
        [InlineData("corpse", "id")]
        [InlineData("hediff", "loadId")]
        [InlineData("timing", "normal")]
        [InlineData("debt", "owners")]
        [InlineData("hediff", "phase")]
        [InlineData("counter", "value")]
        public void LargeIdsTicksCountersAndEnumsDoNotAliasThroughSingle(string element, string attribute)
        {
            Assert.False(Equal(new XElement(element, new XAttribute(attribute, "16777216")),
                new XElement(element, new XAttribute(attribute, "16777217"))));
        }

        [Fact]
        public void SettingsNormalizeOnlyExplicitSingleFields()
        {
            var fields = new HashSet<string> { "baseHealingRate" };
            Assert.True(PersistenceSnapshotComparer.AreEqual(
                XElement.Parse("<settings><field name='baseHealingRate' value='0.0600000024'/></settings>"),
                XElement.Parse("<settings><field name='baseHealingRate' value='0.060000002'/></settings>"), fields));
            Assert.False(PersistenceSnapshotComparer.AreEqual(
                XElement.Parse("<settings><field name='normalTickRate' value='16777216'/></settings>"),
                XElement.Parse("<settings><field name='normalTickRate' value='16777217'/></settings>"), fields));
            Assert.True(Equal(
                XElement.Parse("<commonHediffSettings><setting def='Flu' healingRate='0.0600000024'/></commonHediffSettings>"),
                XElement.Parse("<commonHediffSettings><setting def='Flu' healingRate='0.060000002'/></commonHediffSettings>")));
        }

        [Fact]
        public void NativeFoodMarkerAndPartStringsRemainExact()
        {
            Assert.True(Equal(XElement.Parse("<actor food='disabled'/>"), XElement.Parse("<actor food='disabled'/>")));
            Assert.False(Equal(XElement.Parse("<hediff part='16777216'/>"), XElement.Parse("<hediff part='16777217'/>")));
        }

        private static bool Equal(XElement expected, XElement actual)
            => PersistenceSnapshotComparer.AreEqual(expected, actual, new HashSet<string>());
    }
}
#endif
