// Author: 0Shard
// Description: Scalar healing arithmetic shared by the live calculator and its tests.
namespace Eternal.Healing
{
    internal static class HediffHealingPolicy
    {
        internal const float DebuffRateFactor = 0.01f;
        internal const float SeverityScaling = 1f;

        internal static float StageMultiplier(int stage) => stage switch
        {
            0 => 1f,
            1 => 0.8f,
            2 => 0.6f,
            3 => 0.4f,
            _ => 0.2f
        };

        internal static float BodySize(float? size) => size ?? 1f;

        internal static float Calculate(float effectiveRate, float stageMultiplier, float bodySize, bool stagedDebuff)
        {
            return effectiveRate * stageMultiplier * bodySize * (stagedDebuff ? DebuffRateFactor : 1f);
        }
    }
}
