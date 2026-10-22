// Author: 0Shard
// Description: RJW-independent anatomy save records; no RJW objects are deep-scribed.
using System.Collections.Generic;
using Verse;

namespace Eternal.Compatibility
{
    internal static class RjwAnatomyRetryPolicy
    {
        internal static bool CanRevalidate(bool hasBaseline, bool supportedSchema, string blockedReason)
            => supportedSchema && (blockedReason == null || hasBaseline);
    }

    internal sealed class RjwBodyPartLocator : IExposable
    {
        public string BodyDefName;
        public List<int> ChildIndices = new List<int>();
        public List<string> DefNames = new List<string>();

        public RjwBodyPartLocator() { }

        internal static RjwBodyPartLocator Create(Pawn pawn, BodyPartRecord part)
        {
            var body = pawn?.RaceProps?.body;
            if (body == null || part == null)
            {
                return null;
            }

            var chain = new List<BodyPartRecord>();
            for (var current = part; current != null; current = current.parent)
            {
                chain.Add(current);
            }

            chain.Reverse();
            if (chain.Count == 0 || !ReferenceEquals(chain[0], body.corePart))
            {
                return null;
            }

            var locator = new RjwBodyPartLocator { BodyDefName = body.defName };
            for (int i = 0; i < chain.Count; i++)
            {
                locator.DefNames.Add(chain[i].def.defName);
                if (i > 0)
                {
                    locator.ChildIndices.Add(chain[i - 1].parts.IndexOf(chain[i]));
                }
            }
            return locator;
        }

        internal BodyPartRecord Resolve(Pawn pawn)
        {
            var body = pawn?.RaceProps?.body;
            if (body == null || body.defName != BodyDefName || DefNames == null || ChildIndices == null ||
                DefNames.Count == 0 || ChildIndices.Count != DefNames.Count - 1)
            {
                return null;
            }

            var part = body.corePart;
            for (int i = 0; i < DefNames.Count; i++)
            {
                if (part?.def?.defName != DefNames[i])
                {
                    return null;
                }

                if (i < ChildIndices.Count)
                {
                    int index = ChildIndices[i];
                    if (index < 0 || index >= part.parts.Count)
                    {
                        return null;
                    }

                    part = part.parts[index];
                }
            }
            return part;
        }

        internal bool Matches(Pawn pawn, BodyPartRecord part) => ReferenceEquals(Resolve(pawn), part);

        public void ExposeData()
        {
            Scribe_Values.Look(ref BodyDefName, "bodyDefName");
            Scribe_Collections.Look(ref ChildIndices, "childIndices", LookMode.Value);
            Scribe_Collections.Look(ref DefNames, "defNames", LookMode.Value);
        }
    }

    internal sealed class RjwAnatomySlot : IExposable
    {
        public Pawn Pawn;
        public RjwBodyPartLocator Locator;
        public bool HasBaseline;
        public bool Pending;
        public string BlockedReason;
        public List<RjwAnatomySnapshot> Parts = new List<RjwAnatomySnapshot>();

        public RjwAnatomySlot() { }

        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "pawn");
            Scribe_Deep.Look(ref Locator, "locator");
            Scribe_Values.Look(ref HasBaseline, "hasBaseline");
            Scribe_Values.Look(ref Pending, "pending");
            Scribe_Values.Look(ref BlockedReason, "blockedReason");
            Scribe_Collections.Look(ref Parts, "parts", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && Parts == null)
            {
                Parts = new List<RjwAnatomySnapshot>();
            }
        }
    }

    internal sealed class RjwAnatomySnapshot : IExposable
    {
        public string HediffDefName;
        public string HediffClassName;
        public int OriginalLoadId;
        public int AttachedLoadId;
        public List<RjwSnapshotValue> Fields = new List<RjwSnapshotValue>();
        public List<RjwSnapshotComp> Comps = new List<RjwSnapshotComp>();
        public Verse.WeakReference<LogEntry> CombatLogEntry;

        public RjwAnatomySnapshot() { }

        public void ExposeData()
        {
            Scribe_Values.Look(ref HediffDefName, "hediffDefName");
            Scribe_Values.Look(ref HediffClassName, "hediffClassName");
            Scribe_Values.Look(ref OriginalLoadId, "originalLoadId", -1);
            Scribe_Values.Look(ref AttachedLoadId, "attachedLoadId", -1);
            Scribe_Collections.Look(ref Fields, "fields", LookMode.Deep);
            Scribe_Collections.Look(ref Comps, "comps", LookMode.Deep);
            Scribe_References.Look(ref CombatLogEntry, "combatLogEntry");
        }
    }

    internal sealed class RjwSnapshotComp : IExposable
    {
        public string TypeName;
        public List<RjwSnapshotValue> Fields = new List<RjwSnapshotValue>();
        public RjwSnapshotComp() { }
        public void ExposeData()
        {
            Scribe_Values.Look(ref TypeName, "typeName");
            Scribe_Collections.Look(ref Fields, "fields", LookMode.Deep);
        }
    }

    // Null flags preserve nullable forcedSize and fluidOverride independently of effective getters.
    internal sealed class RjwSnapshotValue : IExposable
    {
        public string Name;
        public string TypeName;
        public bool IsNull;
        public string Text;
        public float Number;
        public int Integer;
        public bool Boolean;
        public int RangeMin;
        public int RangeMax;
        public RjwSnapshotValue() { }
        public void ExposeData()
        {
            Scribe_Values.Look(ref Name, "name");
            Scribe_Values.Look(ref TypeName, "typeName");
            Scribe_Values.Look(ref IsNull, "isNull");
            Scribe_Values.Look(ref Text, "text");
            Scribe_Values.Look(ref Number, "number");
            Scribe_Values.Look(ref Integer, "integer");
            Scribe_Values.Look(ref Boolean, "boolean");
            Scribe_Values.Look(ref RangeMin, "rangeMin");
            Scribe_Values.Look(ref RangeMax, "rangeMax");
        }
    }
}
