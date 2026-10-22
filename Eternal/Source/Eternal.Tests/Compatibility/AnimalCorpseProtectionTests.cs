using Eternal.Corpse;
using Xunit;

namespace Eternal.Tests.Compatibility
{
    public class AnimalCorpseProtectionTests
    {
        [Theory]
        [InlineData(true, true, true, true, true)]
        [InlineData(false, true, true, true, false)]
        [InlineData(true, true, true, false, false)]
        [InlineData(true, false, true, true, false)]
        [InlineData(true, true, false, true, false)]
        [InlineData(false, false, false, false, false)]
        public void OnlyAnimalConsumptionOfDeadEternalCorpsesIsBlocked(bool animal, bool corpse, bool dead, bool eternal, bool expected)
            => Assert.Equal(expected, AnimalCorpseProtectionPolicy.ShouldBlock(animal, corpse, dead, eternal));
    }
}
