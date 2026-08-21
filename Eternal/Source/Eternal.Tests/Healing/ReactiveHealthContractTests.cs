// Relative Path: Eternal/Source/Eternal.Tests/Healing/ReactiveHealthContractTests.cs
// Creation Date: 16-07-2026
// Last Edit: 16-07-2026
// Author: 0Shard
// Description: RED/GREEN contract for Gate 2's persisted hediff-instance identity.
//              Runtime-only reconciliation and queue contracts are exercised by the DEBUG
//              in-game ReactiveHealth suite because Assembly-CSharp virtual methods cannot
//              execute in the net472 unit runner.

using System;
using System.Reflection;
using Eternal.Infrastructure;
using Xunit;

namespace Eternal.Tests.Healing
{
    public class ReactiveHealthContractTests
    {
        [Fact]
        public void HealingDictionaryKey_ContainsPersistedHediffLoadId()
        {
            FieldInfo loadIdField = typeof(HealingDictionaryKey).GetField(
                "HediffLoadID",
                BindingFlags.Instance | BindingFlags.Public);

            Assert.NotNull(loadIdField);
            Assert.Equal(typeof(int), loadIdField.FieldType);

            // Three public constructors are expected: game-object construction, the new
            // loadID-aware scalar constructor, and the legacy scalar constructor.
            ConstructorInfo[] constructors = typeof(HealingDictionaryKey)
                .GetConstructors(BindingFlags.Instance | BindingFlags.Public);
            Assert.Equal(3, constructors.Length);
        }
    }
}
