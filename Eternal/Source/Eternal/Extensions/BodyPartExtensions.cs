// Relative Path: Eternal/Source/Eternal/Extensions/BodyPartExtensions.cs
// Creation Date: 29-10-2025
// Last Edit: 13-01-2026
// Author: 0Shard
// Description: Extension methods for body part classification.

using Verse;
using Eternal.Constants;

namespace Eternal.Extensions
{
    /// <summary>
    /// Extension methods for body part classification.
    /// Uses centralized constants from CriticalPartConstants.
    /// </summary>
    public static class BodyPartExtensions
    {
        /// <summary>
        /// Checks if a body part is critical/vital for survival.
        /// </summary>
        public static bool IsCritical(this BodyPartRecord part)
        {
            return CriticalPartConstants.IsVitalPart(part);
        }

        /// <summary>
        /// Checks if a body part requires sequenced regrowth.
        /// </summary>
        public static bool RequiresSequencedRegrowth(this BodyPartRecord part)
        {
            return CriticalPartConstants.IsSequencedPart(part);
        }
    }
}
