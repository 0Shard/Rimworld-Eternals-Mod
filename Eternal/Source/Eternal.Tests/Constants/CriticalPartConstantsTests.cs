// Relative Path: Eternal/Source/Eternal.Tests/Constants/CriticalPartConstantsTests.cs
// Creation Date: 24-02-2026
// Last Edit: 24-02-2026
// Author: 0Shard
// Description: Unit tests for CriticalPartConstants static class.
//              Verifies regrowth sequence order, vital/sequenced/sensory part collections,
//              GetNextInSequence traversal, no-deadlock property, and collection contents.
//              CompareRegrowthPriority tests requiring BodyPartRecord are deferred to RimTest.

using System;
using System.Linq;
using Xunit;
using Eternal.Constants;

namespace Eternal.Tests.Constants
{
    public class CriticalPartConstantsTests
    {
        // -----------------------------------------------------------------
        // RegrowthSequence
        // -----------------------------------------------------------------

        [Fact]
        public void RegrowthSequence_ContainsExactlyFourElements()
        {
            Assert.Equal(4, CriticalPartConstants.RegrowthSequence.Count);
        }

        [Fact]
        public void RegrowthSequence_OrderIsNeckHeadSkullBrain()
        {
            var expected = TestData.CriticalPartSequence;
            var actual = CriticalPartConstants.RegrowthSequence.ToArray();
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void RegrowthSequence_ElementsArePascalCase()
        {
            foreach (var part in CriticalPartConstants.RegrowthSequence)
            {
                Assert.True(char.IsUpper(part[0]),
                    $"Expected PascalCase for '{part}' — first char should be uppercase");
            }
        }

        // -----------------------------------------------------------------
        // VitalParts
        // -----------------------------------------------------------------

        [Fact]
        public void VitalParts_ContainsExactlyNineElements()
        {
            Assert.Equal(9, CriticalPartConstants.VitalParts.Count);
        }

        [Theory]
        [InlineData("Brain")]
        [InlineData("Heart")]
        [InlineData("Lung")]
        [InlineData("Kidney")]
        [InlineData("Liver")]
        [InlineData("Stomach")]
        [InlineData("Head")]
        [InlineData("Neck")]
        [InlineData("Spine")]
        public void VitalParts_ContainsExpectedPart(string partName)
        {
            Assert.Contains(partName, CriticalPartConstants.VitalParts);
        }

        // -----------------------------------------------------------------
        // SequencedParts
        // -----------------------------------------------------------------

        [Fact]
        public void SequencedParts_ContainsExactlyFourElements()
        {
            Assert.Equal(4, CriticalPartConstants.SequencedParts.Count);
        }

        [Theory]
        [InlineData("Neck")]
        [InlineData("Head")]
        [InlineData("Skull")]
        [InlineData("Brain")]
        public void SequencedParts_ContainsExpectedPart(string partName)
        {
            Assert.Contains(partName, CriticalPartConstants.SequencedParts);
        }

        [Fact]
        public void SequencedParts_SkullNotInVitalParts()
        {
            // Skull is sequenced (protects Brain) but not classified as vital organ
            Assert.Contains("Skull", CriticalPartConstants.SequencedParts);
            Assert.DoesNotContain("Skull", CriticalPartConstants.VitalParts);
        }

        // -----------------------------------------------------------------
        // SensoryParts
        // -----------------------------------------------------------------

        [Fact]
        public void SensoryParts_ContainsExactlyEightElements()
        {
            Assert.Equal(8, CriticalPartConstants.SensoryParts.Count);
        }

        [Theory]
        [InlineData("Eye")]
        [InlineData("LeftEye")]
        [InlineData("RightEye")]
        [InlineData("Ear")]
        [InlineData("LeftEar")]
        [InlineData("RightEar")]
        [InlineData("Nose")]
        [InlineData("Jaw")]
        public void SensoryParts_ContainsExpectedPart(string partName)
        {
            Assert.Contains(partName, CriticalPartConstants.SensoryParts);
        }

        // -----------------------------------------------------------------
        // GetNextInSequence
        // -----------------------------------------------------------------

        [Theory]
        [InlineData("Neck", "Head")]
        [InlineData("Head", "Skull")]
        [InlineData("Skull", "Brain")]
        public void GetNextInSequence_SequencedPart_ReturnsCorrectNext(string current, string expected)
        {
            string result = CriticalPartConstants.GetNextInSequence(current);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void GetNextInSequence_Brain_ReturnsNull()
        {
            string result = CriticalPartConstants.GetNextInSequence("Brain");
            Assert.Null(result);
        }

        [Fact]
        public void GetNextInSequence_Null_ReturnsNeck()
        {
            string result = CriticalPartConstants.GetNextInSequence(null);
            Assert.Equal("Neck", result);
        }

        [Fact]
        public void GetNextInSequence_EmptyString_ReturnsNeck()
        {
            string result = CriticalPartConstants.GetNextInSequence(string.Empty);
            Assert.Equal("Neck", result);
        }

        [Fact]
        public void GetNextInSequence_NonSequencedPart_ReturnsNull()
        {
            // "Arm" is not in the sequence — returns null (not found, index < 0)
            string result = CriticalPartConstants.GetNextInSequence("Arm");
            Assert.Null(result);
        }

        [Fact]
        public void GetNextInSequence_CaseInsensitive_ReturnsNext()
        {
            // Uses StringComparison.OrdinalIgnoreCase
            string result = CriticalPartConstants.GetNextInSequence("neck");
            Assert.Equal("Head", result);
        }

        [Fact]
        public void GetNextInSequence_UpperCase_ReturnsNext()
        {
            string result = CriticalPartConstants.GetNextInSequence("SKULL");
            Assert.Equal("Brain", result);
        }

        // -----------------------------------------------------------------
        // No-deadlock verification
        // -----------------------------------------------------------------

        [Fact]
        public void NoDeadlock_NoPartRequiresItself()
        {
            foreach (var part in CriticalPartConstants.RegrowthSequence)
            {
                string next = CriticalPartConstants.GetNextInSequence(part);
                Assert.NotEqual(part, next);
            }
        }

        [Fact]
        public void NoDeadlock_SequenceIsAcyclic_NeckReachesNullInThreeSteps()
        {
            string current = "Neck";
            int steps = 0;

            while (current != null && steps < 10) // safety limit
            {
                current = CriticalPartConstants.GetNextInSequence(current);
                steps++;
            }

            // Neck -> Head -> Skull -> Brain -> null = 4 steps
            // But Brain->null is the last call, so after 3 next-calls we're at Brain,
            // 4th call returns null
            Assert.Null(current);
            Assert.Equal(4, steps); // Neck->Head(1), Head->Skull(2), Skull->Brain(3), Brain->null(4)
        }

        [Fact]
        public void NoDeadlock_FullTraversal_VisitsAllSequencedParts()
        {
            var visited = new System.Collections.Generic.List<string>();
            string current = CriticalPartConstants.GetNextInSequence(null); // starts at Neck

            while (current != null)
            {
                visited.Add(current);
                current = CriticalPartConstants.GetNextInSequence(current);
            }

            Assert.Equal(TestData.CriticalPartSequence, visited.ToArray());
        }

        // -----------------------------------------------------------------
        // Sequence index ordering
        // -----------------------------------------------------------------

        [Fact]
        public void SequenceIndex_NeckBeforeHead()
        {
            int neckIdx = CriticalPartConstants.RegrowthSequence.ToList().IndexOf("Neck");
            int headIdx = CriticalPartConstants.RegrowthSequence.ToList().IndexOf("Head");
            Assert.True(neckIdx < headIdx);
        }

        [Fact]
        public void SequenceIndex_HeadBeforeSkull()
        {
            int headIdx = CriticalPartConstants.RegrowthSequence.ToList().IndexOf("Head");
            int skullIdx = CriticalPartConstants.RegrowthSequence.ToList().IndexOf("Skull");
            Assert.True(headIdx < skullIdx);
        }

        [Fact]
        public void SequenceIndex_SkullBeforeBrain()
        {
            int skullIdx = CriticalPartConstants.RegrowthSequence.ToList().IndexOf("Skull");
            int brainIdx = CriticalPartConstants.RegrowthSequence.ToList().IndexOf("Brain");
            Assert.True(skullIdx < brainIdx);
        }
    }
}
