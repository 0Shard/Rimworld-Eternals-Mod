// Author: 0Shard
// Description: A single-call batch of validated hediffs and scalar identities shared by live state owners.
using System.Collections.Generic;
using Verse;

namespace Eternal.Infrastructure
{
    internal sealed class LiveHealthReconciliationSnapshot
    {
        internal readonly List<Hediff> Hediffs = new List<Hediff>();
        private readonly List<HealingDictionaryKey> keys = new List<HealingDictionaryKey>();
        internal readonly HashSet<HealingDictionaryKey> CurrentKeys = new HashSet<HealingDictionaryKey>();
        internal readonly HashSet<HealingDictionaryKey> EligibleThresholdKeys = new HashSet<HealingDictionaryKey>();
        private Dictionary<HealingDictionaryKey, HealingDictionaryKey?> legacyCandidates;

        internal void Add(Hediff hediff, HealingDictionaryKey key)
        {
            Hediffs.Add(hediff);
            keys.Add(key);
            CurrentKeys.Add(key);
        }

        internal HealingDictionaryKey KeyAt(int index) => keys[index];

        internal Dictionary<HealingDictionaryKey, HealingDictionaryKey?> LegacyCandidates
        {
            get
            {
                if (legacyCandidates == null)
                {
                    // Use the captured sequence rather than the set: repeated input remains
                    // ambiguous, and callbacks cannot change the identity of this batch.
                    legacyCandidates = LegacyHealingCandidateLookup.BuildUniqueLookup(
                        keys,
                        key => new HealingDictionaryKey(key.PawnThingIDNumber, key.HediffDefName, key.BodyPartLabel));
                }

                return legacyCandidates;
            }
        }
    }
}
