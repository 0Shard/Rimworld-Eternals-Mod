// Author: 0Shard
// Description: Owned-pawn native transfer faults and scoped destination observations for map-removal tests.
#if DEBUG
using System;
using System.Collections.Generic;
using System.Reflection;
using Eternal.Corpse;
using Eternal.DI;
using Eternal.World;
using HarmonyLib;
using RimWorld.Planet;
using Verse;
using MapType = Verse.Map;
using CorpseType = Verse.Corpse;

namespace Eternal.InGameTests.FailureScenarios
{
    internal sealed class MapRemovalObservation : IDisposable
    {
        private static MapRemovalObservation active;
        private readonly Harmony harmony = new Harmony("Eternal.Debug.MapRemovalFailures");
        private readonly List<MethodBase> patched = new List<MethodBase>();
        private readonly Pawn faultPawn;
        private readonly MapType source;
        private readonly HashSet<MapType> allowedHomes;
        private readonly bool failTransfers;
        private readonly List<string> transferTrace = new List<string>();
        public string TransferTrace => string.Join("; ", transferTrace);
        public int HomeFaults { get; private set; }
        public int SiteFaults { get; private set; }
        public int SitePostDestroyCalls { get; private set; }

        public MapRemovalObservation(MapType source, IEnumerable<MapType> allowedHomes,
            Pawn faultPawn = null, bool failTransfers = false)
        {
            TestAssert.IsNull(active, "Map-removal instrumentation cannot nest");
            this.source = source;
            this.allowedHomes = new HashSet<MapType>(allowedHomes);
            this.faultPawn = faultPawn;
            this.failTransfers = failTransfers;
            active = this;
            try
            {
                Patch(AccessTools.PropertyGetter(typeof(MapType), nameof(MapType.IsPlayerHome)), nameof(HomePostfix), false);
                Patch(AccessTools.Method(typeof(CorpseType), nameof(CorpseType.SpawnSetup),
                    new[] { typeof(MapType), typeof(bool) }), nameof(BeforeSpawn), true);
                Patch(AccessTools.Method(typeof(CorpseType), nameof(CorpseType.SpawnSetup),
                    new[] { typeof(MapType), typeof(bool) }), nameof(AfterSpawn), false);
                foreach (var method in new[] { nameof(EternalCorpseManager.TryRelocateCorpse),
                    nameof(EternalCorpseManager.TryReleaseCorpseToUnspawnedOwner) })
                {
                    Patch(AccessTools.Method(typeof(EternalCorpseManager), method), nameof(BeforeTransfer), true);
                    Patch(AccessTools.Method(typeof(EternalCorpseManager), method), nameof(AfterTransfer), false);
                }
                Patch(AccessTools.Method(typeof(WorldObject_EternalCrashSite), nameof(WorldObject_EternalCrashSite.AddCorpse)),
                    nameof(BeforeSiteAcceptance), true);
                Patch(AccessTools.Method(typeof(SitePart), nameof(SitePart.PostDestroy)), nameof(ObserveSiteDestroy), true);
            }
            catch { Dispose(); throw; }
        }

        private void Patch(MethodBase method, string hook, bool prefix)
        {
            TestAssert.IsNotNull(method, "Actual map-removal boundary " + hook);
            patched.Add(method);
            var callback = new HarmonyMethod(typeof(MapRemovalObservation), hook);
            harmony.Patch(method, prefix: prefix ? callback : null, postfix: prefix ? null : callback);
        }

        private static void HomePostfix(MapType __instance, ref bool __result)
        {
            // Restrict only this synchronous fixture call; no live map setting is mutated.
            if (active != null)
            {
                __result = active.allowedHomes.Contains(__instance);
            }
        }

        private static void BeforeSpawn(CorpseType __instance, MapType __0)
        {
            var observation = active;
            if (observation != null && __instance.InnerPawn == observation.faultPawn)
            {
                observation.transferTrace.Add("SpawnSetup " + (__0 == observation.source ? "source" : "target"));
            }

            if (observation?.failTransfers == true && __instance.InnerPawn == observation.faultPawn
                && __0 != observation.source)
            {
                observation.HomeFaults++;
                throw new InvalidOperationException("Owned corpse target SpawnSetup failure");
            }
        }

        private static void AfterSpawn(CorpseType __instance, MapType __0)
        {
            var observation = active;
            if (observation != null && __instance.InnerPawn == observation.faultPawn)
            {
                observation.TraceOwnership("SpawnSetup returned " + (__0 == observation.source ? "source" : "target"));
            }
        }

        private static void BeforeTransfer(Pawn __0, MethodBase __originalMethod)
        {
            if (active != null && __0 == active.faultPawn)
            {
                active.TraceOwnership(__originalMethod.Name + " before");
            }
        }

        private static void AfterTransfer(Pawn __0, MethodBase __originalMethod, bool __result)
        {
            if (active != null && __0 == active.faultPawn)
            {
                active.TraceOwnership(__originalMethod.Name + " returned " + __result);
            }
        }

        private void TraceOwnership(string stage)
        {
            var entry = EternalServiceContainer.Instance.CorpseManager.GetCorpseData(faultPawn);
            var corpse = entry?.Corpse;
            transferTrace.Add(stage + ": spawned=" + corpse?.Spawned + ", destroyed=" + corpse?.Destroyed
                + ", sourceMap=" + (corpse?.Map == source) + ", sourceHeldMap=" + (corpse?.MapHeld == source)
                + ", sourceIndex=" + (entry?.CurrentMap == source) + ", owner=" + corpse?.holdingOwner?.GetType().Name);
        }

        private static void BeforeSiteAcceptance(CorpseType __0)
        {
            var observation = active;
            if (observation?.failTransfers == true && __0.InnerPawn == observation.faultPawn)
            {
                observation.SiteFaults++;
                throw new InvalidOperationException("Owned corpse recovery-site acceptance failure");
            }
        }

        private static void ObserveSiteDestroy(SitePart __instance)
        {
            if (active != null && ReferenceEquals(__instance.site, active.source.Parent))
            {
                active.SitePostDestroyCalls++;
            }
        }

        public void Dispose()
        {
            try
            {
                foreach (var method in patched)
                {
                    harmony.Unpatch(method, HarmonyPatchType.All, harmony.Id);
                }
            }
            finally { active = null; }
        }
    }
}
#endif
