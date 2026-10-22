// Relative Path: Eternal/Source/Eternal.Tests/Exceptions/EternalExceptionCategoryTests.cs
// Creation Date: 24-02-2026
// Last Edit: 24-02-2026
// Author: 0Shard
// Description: Unit tests for EternalExceptionCategory enum taxonomy.
//              Verifies all 11 categories exist, severity classification is correct
//              (error-level vs warning-level), and no phantom values exist.

using System;
using System.Linq;
using Xunit;
using Eternal.Exceptions;

namespace Eternal.Tests.Exceptions
{
    public class EternalExceptionCategoryTests
    {
        // -----------------------------------------------------------------
        // Enum completeness
        // -----------------------------------------------------------------

        [Fact]
        public void EnumValues_ExactlyElevenCategories()
        {
            var values = Enum.GetValues(typeof(EternalExceptionCategory));
            Assert.Equal(TestData.ExceptionCategoryCount, values.Length);
        }

        [Theory]
        [InlineData(nameof(EternalExceptionCategory.DataInconsistency))]
        [InlineData(nameof(EternalExceptionCategory.CompatibilityFailure))]
        [InlineData(nameof(EternalExceptionCategory.GameStateInvalid))]
        [InlineData(nameof(EternalExceptionCategory.InternalError))]
        [InlineData(nameof(EternalExceptionCategory.ConfigurationError))]
        [InlineData(nameof(EternalExceptionCategory.HediffSwap))]
        [InlineData(nameof(EternalExceptionCategory.Resurrection))]
        [InlineData(nameof(EternalExceptionCategory.Regrowth))]
        [InlineData(nameof(EternalExceptionCategory.Snapshot))]
        [InlineData(nameof(EternalExceptionCategory.CorpseTracking))]
        [InlineData(nameof(EternalExceptionCategory.MapProtection))]
        public void EnumValues_NamedCategoryExists(string categoryName)
        {
            Assert.True(Enum.IsDefined(typeof(EternalExceptionCategory), categoryName));
        }

        // -----------------------------------------------------------------
        // Error-level classification
        // -----------------------------------------------------------------

        [Theory]
        [InlineData(EternalExceptionCategory.DataInconsistency)]
        [InlineData(EternalExceptionCategory.GameStateInvalid)]
        [InlineData(EternalExceptionCategory.InternalError)]
        [InlineData(EternalExceptionCategory.HediffSwap)]
        [InlineData(EternalExceptionCategory.Resurrection)]
        [InlineData(EternalExceptionCategory.CorpseTracking)]
        public void ErrorLevelCategories_UseProductionClassification(EternalExceptionCategory category)
        {
            Assert.True(EternalExceptionSeverity.IsError(category));
        }

        [Fact]
        public void ErrorLevelCategories_ExactlySix()
        {
            Assert.Equal(6, Enum.GetValues(typeof(EternalExceptionCategory)).Cast<EternalExceptionCategory>()
                .Count(EternalExceptionSeverity.IsError));
        }

        // -----------------------------------------------------------------
        // Warning-level classification
        // -----------------------------------------------------------------

        [Theory]
        [InlineData(EternalExceptionCategory.CompatibilityFailure)]
        [InlineData(EternalExceptionCategory.ConfigurationError)]
        [InlineData(EternalExceptionCategory.Regrowth)]
        [InlineData(EternalExceptionCategory.Snapshot)]
        [InlineData(EternalExceptionCategory.MapProtection)]
        public void WarningLevelCategories_UseProductionClassification(EternalExceptionCategory category)
        {
            Assert.False(EternalExceptionSeverity.IsError(category));
        }

        [Fact]
        public void WarningLevelCategories_ExactlyFive()
        {
            Assert.Equal(5, Enum.GetValues(typeof(EternalExceptionCategory)).Cast<EternalExceptionCategory>()
                .Count(c => !EternalExceptionSeverity.IsError(c)));
        }

        // -----------------------------------------------------------------
        // Coverage completeness — every enum value is classified
        // -----------------------------------------------------------------

        [Fact]
        public void CorpseTracking_IsClassifiedAsError()
        {
            Assert.True(EternalExceptionSeverity.IsError(EternalExceptionCategory.CorpseTracking));
        }

        [Fact]
        public void UnknownCategory_PreservesWarningFallback()
        {
            Assert.False(EternalExceptionSeverity.IsError((EternalExceptionCategory)999));
        }

        [Fact]
        public void CompatibilityFailure_IsClassifiedAsWarning()
        {
            Assert.False(EternalExceptionSeverity.IsError(EternalExceptionCategory.CompatibilityFailure));
        }
    }
}
