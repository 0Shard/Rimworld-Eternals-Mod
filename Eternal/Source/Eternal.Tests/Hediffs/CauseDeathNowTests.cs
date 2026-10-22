// Author: 0Shard
// Description: Every decoded CauseDeathNow return is false; game execution is separately pending.
using Eternal.Tests.Infrastructure;
using Xunit;

namespace Eternal.Tests.Hediffs
{
    public class CauseDeathNowTests
    {
        [Theory]
        [InlineData("Eternal.EternalRegrowing_Hediff")]
        [InlineData("Eternal.Hediffs.MetabolicRecovery_Hediff")]
        public void CauseDeathNow_DeclaredExactOverride_ReturnsFalseOnEveryPath(string type)
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var method = assembly.Method(type, "CauseDeathNow", new string[0], "System.Boolean", 0, false);
            Assert.True(assembly.ReturnsConstantInt32(method, 0), "Every decoded return must be false for " + type);
        }
    }
}
