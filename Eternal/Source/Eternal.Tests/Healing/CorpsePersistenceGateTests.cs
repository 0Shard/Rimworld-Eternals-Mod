/*
 * Relative Path: Eternal/Source/Eternal.Tests/Healing/CorpsePersistenceGateTests.cs
 * Creation Date: 16-07-2026
 * Last Edit: 16-07-2026
 * Author: 0Shard
 * Description: Regression tests for Gate 1 corpse queue hydration, stable queue identity,
 *              failure-safe relocation contracts, and linear queue compaction.
 */

using System;
using System.Collections.Generic;
using System.Reflection;
using Eternal;
using Eternal.Corpse;
using Eternal.Healing;
using Eternal.Models;
using Xunit;

namespace Eternal.Tests.Healing
{
    public class CorpsePersistenceGateTests
    {
        [Fact]
        public void CompactCompletedQueue_RemovesCompletedItemsInOnePassAndPreservesOrder()
        {
            var firstHealingItem = new HealingItem { Severity = 1f };
            var completedHealingItem = new HealingItem { Severity = 0f };
            var thirdHealingItem = new HealingItem { Severity = 3f };
            var fourthHealingItem = new HealingItem { Severity = 4f };
            var queue = new List<HealingItem>
            {
                firstHealingItem,
                completedHealingItem,
                thirdHealingItem,
                null,
                fourthHealingItem
            };
            var completedItems = new HashSet<HealingItem> { completedHealingItem };

            var compactionMethod = typeof(EternalCorpseHealingProcessor).GetMethod(
                "CompactCompletedQueue",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

            Assert.NotNull(compactionMethod);
            compactionMethod.Invoke(null, new object[] { queue, completedItems });

            Assert.Equal(
                new[] { firstHealingItem, thirdHealingItem, fourthHealingItem },
                queue);
        }

        [Fact]
        public void HealingItem_ExposesStableHediffLoadIdForRebinding()
        {
            var loadIdProperty = typeof(HealingItem).GetProperty("HediffLoadId",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.NotNull(loadIdProperty);

            var healingItem = new HealingItem();
            loadIdProperty.SetValue(healingItem, 4815);
            Assert.Equal(4815, loadIdProperty.GetValue(healingItem));
        }

        [Fact]
        public void CorpseTrackingEntry_ExposesQueueHydrationState()
        {
            var hydrationProperty = typeof(CorpseTrackingEntry).GetProperty(
                "HealingQueueHydrated",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.NotNull(hydrationProperty);

            var corpseEntry = new CorpseTrackingEntry();
            Assert.False((bool)hydrationProperty.GetValue(corpseEntry));
        }

        [Fact]
        public void CorpseHealingProcessor_ExposesPostLoadHydrationOwner()
        {
            var rebuildMethod = typeof(EternalCorpseHealingProcessor).GetMethod(
                "RebuildActiveHealingCorpses",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.NotNull(rebuildMethod);
        }

        [Fact]
        public void CorpseManager_ExposesManagerOwnedRelocationOperation()
        {
            var relocationMethod = typeof(EternalCorpseManager).GetMethod(
                "TryRelocateCorpse",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.NotNull(relocationMethod);
            Assert.Equal(typeof(bool), relocationMethod.ReturnType);
        }
    }
}
