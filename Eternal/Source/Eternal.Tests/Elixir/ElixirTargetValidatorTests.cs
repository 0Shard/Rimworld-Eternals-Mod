// Relative Path: Eternal/Source/Eternal.Tests/Elixir/ElixirTargetValidatorTests.cs
// Creation Date: 28-09-2026
// Last Edit: 28-09-2026
// Author: 0Shard
// Description: Pure-logic tests for ElixirTargetValidator. Verse-free by design (no Pawn in
//              signatures) so xunit can load it without Assembly-CSharp — mirrors the split
//              used by DebtRepaymentProcessorTests for the same reason. Asserts against the
//              validator's public const key fields (not re-typed string literals) so a key
//              rename fails loudly here instead of silently drifting from the real values.

using Xunit;
using Eternal.Elixir;

namespace Eternal.Tests.Elixir
{
    public class ElixirTargetValidatorTests
    {
        // -----------------------------------------------------------------
        // GetRejectReasonKey — precedence order: dead > no-traits > already-Eternal > cap
        // -----------------------------------------------------------------

        [Fact]
        public void GetRejectReasonKey_Dead_WinsOverEveryOtherBadFlag()
        {
            // Every other flag is also bad (no traits, already Eternal, cap reached) —
            // dead must still be the reported reason.
            string reason = ElixirTargetValidator.GetRejectReasonKey(
                isDead: true, hasTraitStorage: false, isAlreadyEternal: true,
                capEnabled: true, totalEternals: 10, cap: 5);

            Assert.Equal(ElixirTargetValidator.RejectDeadKey, reason);
        }

        [Fact]
        public void GetRejectReasonKey_NotDead_NoTraitStorage_ReturnsNoTraitsKey()
        {
            // Checked before already-Eternal: isAlreadyEternal true here must not surface.
            string reason = ElixirTargetValidator.GetRejectReasonKey(
                isDead: false, hasTraitStorage: false, isAlreadyEternal: true,
                capEnabled: false, totalEternals: 0, cap: 0);

            Assert.Equal(ElixirTargetValidator.RejectNoTraitsKey, reason);
        }

        [Fact]
        public void GetRejectReasonKey_HasStorage_AlreadyEternal_ReturnsAlreadyEternalKey()
        {
            // Checked before cap: cap is also reached here but must not surface.
            string reason = ElixirTargetValidator.GetRejectReasonKey(
                isDead: false, hasTraitStorage: true, isAlreadyEternal: true,
                capEnabled: true, totalEternals: 5, cap: 5);

            Assert.Equal(ElixirTargetValidator.RejectAlreadyEternalKey, reason);
        }

        [Theory]
        [InlineData(5, 5, true)]   // at cap -> reject
        [InlineData(4, 5, false)]  // one below cap -> accept
        [InlineData(6, 5, true)]   // above cap -> reject
        public void GetRejectReasonKey_CapEnabled_MirrorsBoundary(int totalEternals, int cap, bool expectRejected)
        {
            string reason = ElixirTargetValidator.GetRejectReasonKey(
                isDead: false, hasTraitStorage: true, isAlreadyEternal: false,
                capEnabled: true, totalEternals: totalEternals, cap: cap);

            if (expectRejected)
                Assert.Equal(ElixirTargetValidator.RejectCapReachedKey, reason);
            else
                Assert.Null(reason);
        }

        [Fact]
        public void GetRejectReasonKey_CapDisabled_FarAboveNumericCap_ReturnsNull()
        {
            string reason = ElixirTargetValidator.GetRejectReasonKey(
                isDead: false, hasTraitStorage: true, isAlreadyEternal: false,
                capEnabled: false, totalEternals: 999, cap: 5);

            Assert.Null(reason);
        }

        [Fact]
        public void GetRejectReasonKey_AllFlagsClean_ReturnsNull()
        {
            string reason = ElixirTargetValidator.GetRejectReasonKey(
                isDead: false, hasTraitStorage: true, isAlreadyEternal: false,
                capEnabled: false, totalEternals: 0, cap: 0);

            Assert.Null(reason);
        }

        // -----------------------------------------------------------------
        // IsPopulationCapReached — mirrors the same boundary GetRejectReasonKey applies
        // -----------------------------------------------------------------

        [Fact]
        public void IsPopulationCapReached_Disabled_AlwaysFalse()
        {
            Assert.False(ElixirTargetValidator.IsPopulationCapReached(capEnabled: false, totalEternals: 999, cap: 5));
        }

        [Theory]
        [InlineData(5, 5, true)]
        [InlineData(4, 5, false)]
        [InlineData(6, 5, true)]
        public void IsPopulationCapReached_Enabled_MirrorsBoundary(int totalEternals, int cap, bool expected)
        {
            Assert.Equal(expected, ElixirTargetValidator.IsPopulationCapReached(capEnabled: true, totalEternals: totalEternals, cap: cap));
        }
    }
}
