// Author: 0Shard
// Description: Scoped development measurements around real production methods, shared by baseline and candidate builds.
#if DEBUG
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Xml.Linq;
using HarmonyLib;

namespace Eternal.InGameTests.Helpers
{
    public sealed class ProductionProfiler : IDisposable
    {
        private const string Owner = "Eternal.Tests.ProductionProfiler";
        private static readonly Func<long> AllocationCounter = FindAllocationCounter();
        private static ProductionProfiler active;
        private readonly Harmony harmony = new Harmony(Owner);
        private readonly Dictionary<MethodBase, Sample> samples = new Dictionary<MethodBase, Sample>();
        private readonly int threadId = Thread.CurrentThread.ManagedThreadId;
        private readonly int[] collections = new int[3];
        private long started;
        private long allocated;

        public ProductionProfiler()
        {
            TestAssert.IsNull(active, "Production profiling scopes cannot nest");
            active = this;
            try
            {
                foreach (var target in Targets)
                {
                    var type = typeof(Eternal_Component).Assembly.GetType(target.Item1, throwOnError: true);
                    string methodName = target.Item2;
                    // Baseline and candidate use the same profiler. The optimized coordinated
                    // path bypasses public validation wrappers, so measure that owner entry.
                    if (methodName == "ReconcileLiveHealth" && type != typeof(EternalHealingProcessor)
                        && type.GetMethod("ReconcileValidatedHealth", BindingFlags.Instance | BindingFlags.NonPublic) != null)
                    {
                        methodName = "ReconcileValidatedHealth";
                    }
                    var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                        .Where(method => method.DeclaringType == type && method.Name == methodName).ToArray();
                    TestAssert.IsTrue(methods.Length > 0, "Production profiling target must exist: " + target.Item1 + "." + target.Item2);
                    foreach (var method in methods)
                    {
                        if (samples.ContainsKey(method)) { continue; }
                        samples.Add(method, new Sample(method.DeclaringType.FullName + "." + method.Name + "(" +
                            string.Join(",", method.GetParameters().Select(parameter => parameter.ParameterType.Name)) + ")"));
                        harmony.Patch(method,
                            prefix: new HarmonyMethod(typeof(ProductionProfiler), nameof(Before)),
                            finalizer: new HarmonyMethod(typeof(ProductionProfiler), nameof(After)));
                    }
                }
                Reset();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public static bool SupportsAllocationMeasurement => AllocationCounter != null;
        public static long ReadAllocatedBytes() => AllocationCounter == null ? -1 : AllocationCounter();

        public void Reset()
        {
            foreach (var sample in samples.Values) { sample.Clear(); }
            for (int generation = 0; generation < collections.Length; generation++)
            {
                collections[generation] = GC.CollectionCount(generation);
            }
            allocated = ReadAllocatedBytes();
            started = Stopwatch.GetTimestamp();
        }

        public XElement Snapshot()
        {
            long elapsed = Stopwatch.GetTimestamp() - started;
            long allocationNow = ReadAllocatedBytes();
            var result = new XElement("productionProfile",
                new XAttribute("clockFrequency", Stopwatch.Frequency),
                new XAttribute("elapsedTimestampTicks", elapsed),
                new XAttribute("timing", "inclusive elapsed time; nested methods overlap"),
                new XAttribute("allocatedBytes", allocated >= 0 && allocationNow >= allocated ? (allocationNow - allocated).ToString() : "unavailable"));
            for (int generation = 0; generation < collections.Length; generation++)
            {
                result.Add(new XElement("gc", new XAttribute("generation", generation),
                    new XAttribute("collections", GC.CollectionCount(generation) - collections[generation])));
            }
            foreach (var sample in samples.Values.OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                result.Add(new XElement("method", new XAttribute("name", sample.Name),
                    new XAttribute("calls", sample.Calls), new XAttribute("timestampTicks", sample.Elapsed),
                    new XAttribute("maxTimestampTicks", sample.Maximum), new XAttribute("exceptions", sample.Exceptions),
                    new XAttribute("allocatedBytes", AllocationCounter == null ? "unavailable" : sample.Allocated.ToString())));
            }
            return result;
        }

        private static void Before(MethodBase __originalMethod, out Measurement __state)
        {
            var profiler = active;
            if (profiler == null || profiler.threadId != Thread.CurrentThread.ManagedThreadId
                || !profiler.samples.TryGetValue(__originalMethod, out var sample))
            {
                __state = default;
                return;
            }
            __state = new Measurement { Sample = sample, Allocated = ReadAllocatedBytes(), Started = Stopwatch.GetTimestamp() };
        }

        private static Exception After(Exception __exception, Measurement __state)
        {
            if (__state.Sample != null)
            {
                long elapsed = Stopwatch.GetTimestamp() - __state.Started;
                long now = ReadAllocatedBytes();
                var sample = __state.Sample;
                sample.Calls++;
                sample.Elapsed += elapsed;
                sample.Maximum = Math.Max(sample.Maximum, elapsed);
                if (__exception != null) { sample.Exceptions++; }
                if (__state.Allocated >= 0 && now >= __state.Allocated) { sample.Allocated += now - __state.Allocated; }
            }
            // Profiling observes exceptions; the original caller still receives them.
            return __exception;
        }

        public void Dispose()
        {
            try
            {
                foreach (var method in samples.Keys) { harmony.Unpatch(method, HarmonyPatchType.All, Owner); }
            }
            finally
            {
                if (ReferenceEquals(active, this)) { active = null; }
            }
        }

        private static Func<long> FindAllocationCounter()
        {
            // Reflection preserves the net472 production target while using a real counter on supporting runtimes.
            var method = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Public | BindingFlags.Static,
                null, Type.EmptyTypes, null);
            if (method == null || method.ReturnType != typeof(long)) { return null; }
            try
            {
                var counter = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), method);
                long first = counter();
                return first >= 0 && counter() >= first ? counter : null;
            }
            catch (Exception) { return null; }
        }

        private struct Measurement
        {
            public Sample Sample;
            public long Started;
            public long Allocated;
        }

        private sealed class Sample
        {
            public readonly string Name;
            public long Calls;
            public long Elapsed;
            public long Maximum;
            public long Allocated;
            public long Exceptions;
            public Sample(string name) { Name = name; }
            public void Clear() { Calls = Elapsed = Maximum = Allocated = Exceptions = 0; }
        }

        private static readonly (string, string)[] Targets =
        {
            ("Eternal.Components.TickOrchestrator", "ProcessTick"),
            ("Eternal.Components.TickOrchestrator", "DrainPendingHealthReconciliation"),
            ("Eternal.Components.TickOrchestrator", "ProcessNormalTick"),
            ("Eternal.Components.TickOrchestrator", "ProcessRareTick"),
            ("Eternal.Components.TickOrchestrator", "ProcessDebtRepaymentAndSync"),
            ("Eternal.EternalHealingProcessor", "ReconcileLiveHealth"),
            ("Eternal.Healing.HediffHealingThresholdTracker", "ReconcileLiveHealth"),
            ("Eternal.EternalHediffHealer", "ReconcileLiveHealth"),
            ("Eternal.EternalHediffSeverityTracker", "ReconcileLiveHealth"),
            ("Eternal.EternalRegrowthManager", "StartRegrowth"),
            ("Eternal.Resources.DebtRepaymentProcessor", "ProcessDebtRepayment"),
            ("Eternal.Resources.UnifiedFoodDebtManager", "GetTrackedPawns"),
            ("Eternal.Extensions.PawnExtensions", "RefreshCacheIfNeeded"),
            ("Eternal.Map.EternalMapManager", "MapComponentTick"),
            ("Eternal.Corpse.EternalCorpseComponent", "CompTick"),
            ("Eternal.Compatibility.RjwAnatomyTracker", "RecordCurrentAnatomy")
        };
    }
}
#endif
