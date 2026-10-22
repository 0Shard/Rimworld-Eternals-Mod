// Author: 0Shard
// Description: Resolves legacy scalar healing identities without choosing among duplicate instances.
using System;
using System.Collections.Generic;

namespace Eternal.Infrastructure
{
    internal static class LegacyHealingCandidateLookup
    {
        internal static Dictionary<TKey, TKey?> BuildUniqueLookup<TKey>(
            IEnumerable<TKey> liveKeys, Func<TKey, TKey> toLegacyKey)
            where TKey : struct
        {
            var candidates = new Dictionary<TKey, TKey?>();
            foreach (var liveKey in liveKeys)
            {
                var legacyKey = toLegacyKey(liveKey);

                // Keep ambiguity after every additional match, including a repeated input key.
                candidates[legacyKey] = candidates.ContainsKey(legacyKey)
                    ? (TKey?)null
                    : liveKey;
            }

            return candidates;
        }
    }
}
