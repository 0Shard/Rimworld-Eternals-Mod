// file path: Eternal/Source/Eternal/Constants/CriticalPartConstants.cs
// Description: Centralized constants for critical body parts and regrowth sequences.

using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Eternal.Constants
{
    /// <summary>
    /// Defines critical body parts that must regrow in a specific sequence
    /// to prevent pawn death during resurrection.
    /// </summary>
    public static class CriticalPartConstants
    {
        #region Critical Part Sequence

        /// <summary>
        /// Critical body parts in required regrowth order.
        /// Each part must fully regrow before the next can begin.
        /// </summary>
        /// <remarks>
        /// Order rationale:
        /// 1. Neck - Connects head to body, must exist first
        /// 2. Head - Container for skull and brain
        /// 3. Skull - Protective structure for brain
        /// 4. Brain - Final part, pawn dies without it
        /// </remarks>
        public static readonly IReadOnlyList<string> RegrowthSequence = new[]
        {
            "Neck",
            "Head",
            "Skull",
            "Brain"
        };

        #endregion

        #region Vital Parts

        /// <summary>
        /// Parts that are critical for survival (loss causes death or severe impairment).
        /// </summary>
        public static readonly IReadOnlyCollection<string> VitalParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Brain",
            "Heart",
            "Lung",
            "Kidney",
            "Liver",
            "Stomach",
            "Head",
            "Neck",
            "Spine"
        };

        /// <summary>
        /// Parts that require sequenced regrowth to prevent death loops.
        /// </summary>
        public static readonly IReadOnlyCollection<string> SequencedParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Neck",
            "Head",
            "Skull",
            "Brain"
        };

        /// <summary>
        /// Facial/sensory parts that regrow after brain restoration.
        /// </summary>
        public static readonly IReadOnlyCollection<string> SensoryParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Eye",
            "LeftEye",
            "RightEye",
            "Ear",
            "LeftEar",
            "RightEar",
            "Nose",
            "Jaw"
        };

        #endregion

        #region Query Methods

        /// <summary>
        /// Check if a body part is vital for survival.
        /// </summary>
        public static bool IsVitalPart(BodyPartRecord part)
        {
            if (part?.def?.defName == null)
                return false;
            return VitalParts.Contains(part.def.defName);
        }

        /// <summary>
        /// Check if a body part requires sequenced regrowth.
        /// </summary>
        public static bool IsSequencedPart(BodyPartRecord part)
        {
            if (part?.def?.defName == null)
                return false;
            return SequencedParts.Contains(part.def.defName);
        }

        /// <summary>
        /// Check if a body part is a sensory organ.
        /// </summary>
        public static bool IsSensoryPart(BodyPartRecord part)
        {
            if (part?.def?.defName == null)
                return false;

            string defName = part.def.defName;
            return SensoryParts.Contains(defName) ||
                   defName.Contains("Eye") ||
                   defName.Contains("Ear");
        }

        /// <summary>
        /// Get the sequence index of a critical part (-1 if not in sequence).
        /// Lower index means it must regrow first.
        /// </summary>
        public static int GetSequenceIndex(BodyPartRecord part)
        {
            if (part?.def?.defName == null)
                return -1;

            for (int i = 0; i < RegrowthSequence.Count; i++)
            {
                if (RegrowthSequence[i].Equals(part.def.defName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Compare two parts for regrowth priority.
        /// Returns negative if part1 should regrow first, positive if part2 should, 0 if equal.
        /// </summary>
        public static int CompareRegrowthPriority(BodyPartRecord part1, BodyPartRecord part2)
        {
            int index1 = GetSequenceIndex(part1);
            int index2 = GetSequenceIndex(part2);

            // Non-sequenced parts have lower priority (regrow after critical parts)
            if (index1 < 0 && index2 < 0)
                return 0; // Both non-sequenced, equal priority
            if (index1 < 0)
                return 1; // part1 is non-sequenced, regrows after
            if (index2 < 0)
                return -1; // part2 is non-sequenced, part1 regrows first

            return index1.CompareTo(index2);
        }

        /// <summary>
        /// Get the next part in sequence that should be regrown.
        /// </summary>
        public static string GetNextInSequence(string currentPart)
        {
            if (string.IsNullOrEmpty(currentPart))
                return RegrowthSequence.FirstOrDefault();

            int currentIndex = -1;
            for (int i = 0; i < RegrowthSequence.Count; i++)
            {
                if (RegrowthSequence[i].Equals(currentPart, StringComparison.OrdinalIgnoreCase))
                {
                    currentIndex = i;
                    break;
                }
            }

            if (currentIndex >= 0 && currentIndex < RegrowthSequence.Count - 1)
                return RegrowthSequence[currentIndex + 1];

            return null; // At end of sequence or not found
        }

        #endregion
    }
}
