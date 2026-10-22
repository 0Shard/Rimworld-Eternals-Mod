// Author: 0Shard
// Description: Preserves natural RJW anatomy cohorts across physical loss without serializing optional-mod objects.
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Eternal.Compatibility
{
    internal sealed class RjwAnatomyTracker : IExposable
    {
        internal const int CurrentSnapshotVersion = 1;
        private int snapshotVersion = CurrentSnapshotVersion;
        private List<RjwAnatomySlot> snapshots = new List<RjwAnatomySlot>();
        private readonly RjwReflectionCache reflection = new RjwReflectionCache();
        private readonly Dictionary<Pawn, PawnOperationState> operations = new Dictionary<Pawn, PawnOperationState>();
        private readonly HashSet<string> warnings = new HashSet<string>();

        private sealed class PawnOperationState
        {
            public int MutationDepth;
            public bool MutationFailed;
            public bool Restoring;
            public BodyPartRecord LegacyPart;
            public bool LegacyRejected;
            public bool Suppressed => MutationDepth > 0 || Restoring;
        }

        internal IReadOnlyList<RjwAnatomySlot> Snapshots => snapshots;
        internal int SnapshotVersion => snapshotVersion;
        public bool IsAvailable { get { Initialize(); return reflection.IsAvailable && snapshotVersion == CurrentSnapshotVersion; } }
        public RjwAnatomyTracker() { }
        public void Initialize()
        {
            reflection.Initialize();
            if (snapshotVersion != CurrentSnapshotVersion)
            {
                Warn(null, "Unsupported RJW anatomy snapshot version " + snapshotVersion + "; saved state was retained and restoration disabled.");
            }
        }

        internal void Forget(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            snapshots.RemoveAll(slot => slot.Pawn == pawn);
            operations.Remove(pawn);
        }

        public void BeginMutation(Pawn pawn)
        {
            if (pawn == null || !IsAvailable)
            {
                return;
            }

            var operation = Operation(pawn, true);
            if (operation.MutationDepth == 0)
            {
                RecordCurrentAnatomy(pawn);
            }

            operation.MutationDepth++;
        }

        public void EndMutation(Pawn pawn, bool successful)
        {
            var operation = Operation(pawn);
            if (operation == null || operation.MutationDepth == 0)
            {
                return;
            }

            operation.MutationFailed |= !successful;
            if (--operation.MutationDepth > 0)
            {
                return;
            }

            bool failed = operation.MutationFailed;
            operation.MutationFailed = false;
            ReleaseOperation(pawn, operation);
            if (!failed)
            {
                RecordCurrentAnatomy(pawn);
            }

            RefreshCaches(pawn);
        }

        public void RecordCurrentAnatomy(Pawn pawn)
        {
            if (!CanTrack(pawn))
            {
                return;
            }

            foreach (var part in RelevantParts(pawn))
            {
                var slot = GetSlot(pawn, part);
                if (slot == null)
                {
                    continue;
                }

                if (!IsPartAvailable(pawn, part)) { slot.Pending = true; continue; }
                var cohort = Attached(pawn, part);
                var natural = cohort.Where(reflection.IsNatural).ToList();
                // Defensive capture can mark a partly removed cohort pending. An empty intermediate
                // slot must not erase its established biology before the missing marker arrives.
                if (natural.Count == 0 && (cohort.Any(reflection.IsArtificial) || slot.Pending))
                {
                    continue;
                }

                CaptureCohort(slot, natural);
            }
        }

        public void CaptureBeforeLoss(Pawn pawn, BodyPartRecord part)
        {
            if (!CanTrack(pawn) || part == null)
            {
                return;
            }

            foreach (var target in RelevantParts(pawn).Where(p => IsInSubtree(p, part)))
            {
                var slot = GetSlot(pawn, target);
                if (slot == null)
                {
                    continue;
                }

                var cohort = Attached(pawn, target);
                var natural = cohort.Where(reflection.IsNatural).ToList();
                // MissingPart.PostAdd already has its marker in the set, so present natural
                // anatomy remains capturable there. An empty unavailable slot, however, proves
                // neither an established None baseline nor what an old save originally contained.
                if (!slot.Pending && (natural.Count > 0 ||
                    (!cohort.Any(reflection.IsArtificial) && IsPartAvailable(pawn, target))))
                {
                    CaptureCohort(slot, natural);
                }

                slot.Pending = true;
            }
        }

        // HediffSet removes from its list before PostRemoved. Read the departing object directly
        // and retain previously removed siblings by their saved identity, rather than recapturing a shrinking list.
        public void CaptureRemoved(Hediff hediff)
        {
            var pawn = hediff?.pawn;
            if (!CanTrack(pawn) || hediff.Part == null ||
                (!reflection.IsNatural(hediff) && !reflection.IsArtificial(hediff)))
            {
                return;
            }

            var slot = GetSlot(pawn, hediff.Part);
            if (slot == null)
            {
                return;
            }

            if (reflection.IsNatural(hediff))
            {
                try
                {
                    if (!slot.HasBaseline)
                    {
                        CaptureCohort(slot, Attached(pawn, hediff.Part).Where(reflection.IsNatural).ToList());
                    }

                    var saved = reflection.Capture(hediff);
                    int index = slot.Parts.FindIndex(s => s.AttachedLoadId == hediff.loadID || s.OriginalLoadId == hediff.loadID);
                    if (index >= 0)
                    {
                        saved.OriginalLoadId = slot.Parts[index].OriginalLoadId;
                        slot.Parts[index] = saved;
                    }
                    else
                    {
                        slot.Parts.Add(saved);
                    }

                    slot.HasBaseline = true;
                }
                catch (Exception ex) { Block(slot, ex); }
            }
            slot.Pending = true;
        }

        public void RestorePart(Pawn pawn, BodyPartRecord part)
        {
            if (!CanTrack(pawn) || part == null)
            {
                return;
            }
            // Old saves may have no snapshot. Only the exact known RJW slots in the restored subtree
            // are eligible for RJW's race fallback; arbitrary empty add-on body slots are not inferred.
            foreach (var target in RelevantParts(pawn).Where(p => IsInSubtree(p, part)))
            {
                var slot = GetSlot(pawn, target);
                if (slot != null && !slot.HasBaseline)
                {
                    slot.Pending = true;
                }
            }
            RestoreSlots(pawn, s => IsInSubtree(s.Locator.Resolve(pawn), part));
        }

        public void RestoreAvailable(Pawn pawn)
        {
            if (!CanTrack(pawn))
            {
                return;
            }

            RestoreSlots(pawn, s => true);
        }

        public void RefreshCaches(Pawn pawn)
        {
            if (!CanProcess(pawn))
            {
                return;
            }

            try { reflection.RefreshCaches(pawn); }
            catch (Exception ex) { Warn(pawn, "Could not refresh RJW anatomy caches: " + RootMessage(ex)); }
        }

        // Called by the central AddDirect prefix. It is inert for every ordinary RJW operation.
        internal bool IsLegacyFallbackActive(Pawn pawn) => Operation(pawn)?.LegacyPart != null;

        internal bool AllowLegacyAddition(Pawn pawn, HediffDef def, BodyPartRecord part)
        {
            var operation = Operation(pawn);
            if (operation?.LegacyPart == null)
            {
                return true;
            }

            bool allowed = ReferenceEquals(part, operation.LegacyPart) && reflection.SupportsSnapshotDef(def);
            operation.LegacyRejected |= !allowed;
            return allowed;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref snapshotVersion, "rjwAnatomySnapshotVersion", CurrentSnapshotVersion);
            Scribe_Collections.Look(ref snapshots, "rjwAnatomySnapshots", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (snapshots == null)
                {
                    snapshots = new List<RjwAnatomySlot>();
                }

                snapshots.RemoveAll(slot => slot == null || slot.Pawn == null);
                operations.Clear();
                // Never resolve RJW definitions while deserializing: absent-mod snapshots remain intact.
            }
        }

        private void RestoreSlots(Pawn pawn, Func<RjwAnatomySlot, bool> inScope)
        {
            var operation = Operation(pawn, true);
            operation.Restoring = true;
            try
            {
                foreach (var slot in snapshots.Where(s => s.Pawn == pawn && s.Pending).ToList())
                {
                    var part = slot.Locator?.Resolve(pawn);
                    if (part == null) { Warn(pawn, "Saved RJW body locator cannot be resolved; anatomy remains pending."); continue; }
                    if (!inScope(slot) || !IsPartAvailable(pawn, part) || Attached(pawn, part).Any(reflection.IsArtificial))
                    {
                        continue;
                    }

                    try
                    {
                        if (!RjwAnatomyRetryPolicy.CanRevalidate(slot.HasBaseline,
                            snapshotVersion == CurrentSnapshotVersion, slot.BlockedReason))
                        { Warn(pawn, slot.BlockedReason); continue; }
                        if (slot.HasBaseline)
                        {
                            RestoreCohort(pawn, part, slot);
                        }
                        else
                        {
                            if (snapshots.Any(s => s.Pawn == pawn && s.Locator?.Resolve(pawn) == null))
                            {
                                throw new InvalidOperationException("An unresolved body snapshot prevents a new fallback selection");
                            }

                            CreateFallback(pawn, part, slot);
                        }
                    }
                    catch (Exception ex) { Block(slot, ex); }
                }
            }
            finally
            {
                operation.Restoring = false;
                ReleaseOperation(pawn, operation);
                RefreshCaches(pawn);
            }
        }

        private void RestoreCohort(Pawn pawn, BodyPartRecord part, RjwAnatomySlot slot)
        {
            if (slot.Parts.Select(s => s.OriginalLoadId).Distinct().Count() != slot.Parts.Count)
            {
                throw new InvalidOperationException("Saved anatomy cohort has duplicate identities");
            }

            var present = Attached(pawn, part).Where(reflection.IsNatural).ToList();
            var matched = new HashSet<Hediff>();
            var missing = new List<RjwAnatomySnapshot>();
            foreach (var saved in slot.Parts)
            {
                var survivor = present.FirstOrDefault(h => !matched.Contains(h) && h.def.defName == saved.HediffDefName &&
                    h.GetType().FullName == saved.HediffClassName && (h.loadID == saved.AttachedLoadId || h.loadID == saved.OriginalLoadId));
                if (survivor != null)
                {
                    matched.Add(survivor);
                }
                else
                {
                    missing.Add(saved);
                }
            }
            if (present.Any(h => !matched.Contains(h)))
            {
                throw new InvalidOperationException("Anatomy has an unrecognized surviving identity; saved cohort was not duplicated or overwritten");
            }
            // Validate all missing records before attaching any one of them. A missing definition or
            // changed comp schema must not leave a partially reconstructed cohort.
            var made = missing.Select(saved => new { Saved = saved, Hediff = reflection.MakeSnapshot(pawn, part, saved) }).ToList();
            foreach (var entry in made)
            {
                pawn.health.AddHediff(entry.Hediff, part);
                if (!pawn.health.hediffSet.hediffs.Contains(entry.Hediff))
                {
                    throw new InvalidOperationException("RJW anatomy did not attach to its saved body slot");
                }
                // AddDirect resets age and tickAdded and PostAdd can adjust comp state.
                reflection.ApplySnapshot(entry.Hediff, entry.Saved, false);
                entry.Saved.AttachedLoadId = entry.Hediff.loadID;
            }
            slot.Pending = false;
            slot.BlockedReason = null;
        }

        private void CreateFallback(Pawn pawn, BodyPartRecord part, RjwAnatomySlot slot)
        {
            if (Attached(pawn, part).Count != 0)
            {
                throw new InvalidOperationException("Unknown-baseline fallback requires an empty exact body slot");
            }

            if (reflection.TryCreateRaceFallback(pawn, part, out var hediff))
            {
                if (hediff != null)
                {
                    // Capture before attachment also diagnoses unsupported component schemas.
                    reflection.Capture(hediff);
                    pawn.health.AddHediff(hediff, part);
                    if (!pawn.health.hediffSet.hediffs.Contains(hediff))
                    {
                        throw new InvalidOperationException("Race fallback anatomy was not attached");
                    }
                }
            }
            else
            {
                var kind = reflection.SlotKind(part);
                if (kind == null)
                {
                    throw new InvalidOperationException("No exact-slot fallback exists for this anatomy slot");
                }

                var operation = Operation(pawn);
                operation.LegacyPart = part;
                operation.LegacyRejected = false;
                try { reflection.AddLegacyFallback(pawn, part, kind); }
                finally { operation.LegacyPart = null; }
                if (operation.LegacyRejected && !Attached(pawn, part).Any(reflection.IsNatural))
                {
                    throw new InvalidOperationException("Legacy fallback produced no attached natural anatomy; no manufactured replacement or reroll was allowed");
                }
            }

            CaptureCohort(slot, Attached(pawn, part).Where(reflection.IsNatural).ToList());
        }

        private void CaptureCohort(RjwAnatomySlot slot, List<Hediff> cohort)
        {
            try
            {
                var captured = cohort.Select(reflection.Capture).ToList();
                foreach (var saved in captured)
                {
                    var prior = slot.Parts.FirstOrDefault(s => s.AttachedLoadId == saved.AttachedLoadId);
                    if (prior != null)
                    {
                        saved.OriginalLoadId = prior.OriginalLoadId;
                    }
                }
                slot.Parts = captured;
                slot.HasBaseline = true;
                slot.BlockedReason = null;
                slot.Pending = false;
            }
            catch (Exception ex) { Block(slot, ex); }
        }

        private IEnumerable<BodyPartRecord> RelevantParts(Pawn pawn)
        {
            return pawn.RaceProps.body.AllParts.Where(part => reflection.SlotKind(part) != null ||
                Attached(pawn, part).Count != 0 || snapshots.Any(s => s.Pawn == pawn && s.Locator?.Matches(pawn, part) == true));
        }

        private List<Hediff> Attached(Pawn pawn, BodyPartRecord part) => pawn.health.hediffSet.hediffs
            .Where(h => h.Part == part && (reflection.IsNatural(h) || reflection.IsArtificial(h))).ToList();

        private RjwAnatomySlot GetSlot(Pawn pawn, BodyPartRecord part)
        {
            var found = snapshots.FirstOrDefault(s => s.Pawn == pawn && s.Locator?.Matches(pawn, part) == true);
            if (found != null)
            {
                return found;
            }

            var locator = RjwBodyPartLocator.Create(pawn, part);
            if (locator == null) { Warn(pawn, "RJW anatomy has an invalid body locator; capture was skipped."); return null; }
            found = new RjwAnatomySlot { Pawn = pawn, Locator = locator };
            snapshots.Add(found);
            return found;
        }

        private bool CanProcess(Pawn pawn) => IsAvailable && pawn?.health?.hediffSet != null && pawn.RaceProps?.body != null;
        private bool CanTrack(Pawn pawn) => CanProcess(pawn) && Operation(pawn)?.Suppressed != true;
        private PawnOperationState Operation(Pawn pawn, bool create = false)
        {
            if (pawn == null)
            {
                return null;
            }

            if (!operations.TryGetValue(pawn, out var operation) && create)
            {
                operation = new PawnOperationState();
                operations.Add(pawn, operation);
            }
            return operation;
        }
        private void ReleaseOperation(Pawn pawn, PawnOperationState operation)
        {
            if (!operation.Suppressed && operation.LegacyPart == null)
            {
                operations.Remove(pawn);
            }
        }
        internal static bool IsPartAvailable(Pawn pawn, BodyPartRecord part)
        {
            if (part == null || pawn?.health?.hediffSet == null || pawn.health.hediffSet.PartIsMissing(part))
            {
                return false;
            }
            // Missing markers are removed at regrowth start. Their absence alone does not mean
            // a slot (or its parent) has completed its four biological phases.
            return !pawn.health.hediffSet.hediffs.OfType<EternalRegrowing_Hediff>().Any(h =>
                h.Severity < 1f && IsInSubtree(part, h.forPart ?? h.Part));
        }
        internal static bool IsInSubtree(BodyPartRecord candidate, BodyPartRecord root)
        {
            if (candidate == null || root == null)
            {
                return false;
            }

            for (var part = candidate; part != null; part = part.parent)
            {
                if (ReferenceEquals(part, root))
                {
                    return true;
                }
            }

            return false;
        }
        private void Block(RjwAnatomySlot slot, Exception exception)
        {
            slot.BlockedReason = "RJW anatomy remains pending: " + RootMessage(exception);
            slot.Pending = true;
            Warn(slot.Pawn, slot.BlockedReason);
        }
        private static string RootMessage(Exception exception) => exception.GetBaseException().Message;
        private void Warn(Pawn pawn, string message)
        {
            if (warnings.Add((pawn?.thingIDNumber ?? -1) + ":" + message))
            {
                Log.Warning("[Eternal] " + message);
            }
        }
    }
}
