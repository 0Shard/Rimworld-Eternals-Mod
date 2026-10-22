// Author: 0Shard
// Description: Production exception severity classification without game-host dependencies.
namespace Eternal.Exceptions
{
    internal static class EternalExceptionSeverity
    {
        internal static bool IsError(EternalExceptionCategory category)
        {
            return category == EternalExceptionCategory.DataInconsistency
                || category == EternalExceptionCategory.GameStateInvalid
                || category == EternalExceptionCategory.InternalError
                || category == EternalExceptionCategory.HediffSwap
                || category == EternalExceptionCategory.Resurrection
                || category == EternalExceptionCategory.CorpseTracking;
        }
    }
}
