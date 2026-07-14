// Relative Path: Eternal/Source/Eternal.Tests/Patches/CorpseDestroyGuardTests.cs
// Creation Date: 14-07-2026
// Last Edit: 14-07-2026
// Author: 0Shard
// Description: Full-matrix unit test of Corpse_Destroy_Patch.ShouldRescueFromDestroy — the pure
//              guard predicate deciding when an Eternal corpse Destroy call is a silent container
//              sweep to block versus a legitimate destroy to let through. Bool-only inputs by
//              design: Verse types (DestroyMode) cannot load under the Mono runner without
//              Assembly-CSharp, so the enum comparison lives in the Harmony prefix and the
//              predicate is tested over its full 16-cell boolean matrix.

using Eternal.Patches;
using Xunit;

namespace Eternal.Tests.Patches
{
    /// <summary>
    /// The guard must rescue in exactly one cell of the matrix: tracked current corpse,
    /// unspawned, Vanish mode, not an expected (mod-controlled) destruction.
    /// Every other combination must fall through to vanilla destruction.
    /// </summary>
    public class CorpseDestroyGuardTests
    {
        [Fact]
        public void Rescues_OnlyInTheSingleGuardedCell()
        {
            Assert.True(Corpse_Destroy_Patch.ShouldRescueFromDestroy(
                isTrackedCurrentCorpse: true, spawned: false,
                isVanishMode: true, expectedDestruction: false));
        }

        [Fact]
        public void PassesThrough_InAllOtherFifteenCells()
        {
            foreach (bool isTrackedCurrentCorpse in new[] { false, true })
            foreach (bool spawned in new[] { false, true })
            foreach (bool isVanishMode in new[] { false, true })
            foreach (bool expectedDestruction in new[] { false, true })
            {
                bool isGuardedCell = isTrackedCurrentCorpse && !spawned && isVanishMode && !expectedDestruction;
                if (isGuardedCell)
                    continue;

                Assert.False(
                    Corpse_Destroy_Patch.ShouldRescueFromDestroy(
                        isTrackedCurrentCorpse, spawned, isVanishMode, expectedDestruction),
                    $"Guard must pass through for tracked={isTrackedCurrentCorpse}, spawned={spawned}, " +
                    $"vanish={isVanishMode}, expected={expectedDestruction}");
            }
        }
    }
}
