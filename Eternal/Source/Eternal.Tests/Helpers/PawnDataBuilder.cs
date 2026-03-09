// Relative Path: Eternal/Source/Eternal.Tests/Helpers/PawnDataBuilder.cs
// Creation Date: 24-02-2026
// Last Edit: 24-02-2026
// Author: 0Shard
// Description: NSubstitute-based IPawnData mock factory for tests.
//              Provides pre-configured defaults and convenience factories for
//              common test scenarios (custom BodySize, specific traits).

using NSubstitute;
using Eternal.Interfaces;

namespace Eternal.Tests.Helpers
{
    /// <summary>
    /// Factory for NSubstitute-based <see cref="IPawnData"/> mocks.
    /// Each factory method returns a fresh substitute for test isolation.
    /// </summary>
    public static class PawnDataBuilder
    {
        /// <summary>
        /// Returns an <see cref="IPawnData"/> substitute with default values:
        /// BodySize=1.0f, IsValid=true, HasTrait returns false for all traits.
        /// </summary>
        public static IPawnData Default()
        {
            var pawnData = Substitute.For<IPawnData>();
            pawnData.BodySize.Returns(TestData.DefaultBodySize);
            pawnData.IsValid.Returns(true);
            pawnData.HasTrait(Arg.Any<string>()).Returns(false);
            return pawnData;
        }

        /// <summary>
        /// Returns a mock with the specified <paramref name="bodySize"/>.
        /// Other properties use defaults (IsValid=true, no traits).
        /// </summary>
        public static IPawnData WithBodySize(float bodySize)
        {
            var pawnData = Default();
            pawnData.BodySize.Returns(bodySize);
            return pawnData;
        }

        /// <summary>
        /// Returns a mock where <see cref="IPawnData.HasTrait"/> returns true
        /// for the specified <paramref name="traitDefName"/> only.
        /// </summary>
        public static IPawnData WithTrait(string traitDefName)
        {
            var pawnData = Default();
            pawnData.HasTrait(traitDefName).Returns(true);
            return pawnData;
        }
    }
}
