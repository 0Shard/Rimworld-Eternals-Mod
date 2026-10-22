// Author: 0Shard
// Description: Recover ground-map corpses before native map or parent teardown can mutate ownership.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Eternal.Corpse;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Utils;
using Eternal.World;
using HarmonyLib;
using RimWorld.Planet;
using Verse;
using MapType = Verse.Map;

namespace Eternal.Patches
{
    internal static class GroundMapRemovalGuard
    {
        internal sealed class State
        {
            public MapType Map;
            public EternalMapProtection Protection;
            public WorldObject_EternalCrashSite Site;
        }

        internal static bool Begin(MapType map, WorldObject owner, out State state)
        {
            state = null;
            var site = owner as WorldObject_EternalCrashSite;
            if (SpaceCrashRescueService.IsSourceSiteInDelivery(site))
            {
                return false;
            }
            if (EternalModState.IsDisabled || (map == null && site == null))
            {
                return true;
            }

            try
            {
                var protection = EternalServiceContainer.Instance?.MapProtection;
                if (protection == null || (map != null && !EternalMapProtection.IsGroundMap(map)))
                {
                    return true;
                }

                state = new State { Map = map, Protection = protection, Site = site };
                if (site != null && !protection.BeginRecoverySiteRemoval(site))
                {
                    return false;
                }

                return map == null || protection.BeginMapRemoval(map);
            }
            catch (Exception ex)
            {
                // Unlike the late callback, this is still before native destruction and can
                // safely refuse teardown if recovery cannot establish a surviving owner.
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "GroundMapRemovalGuard.Begin", null, ex);
                return false;
            }
        }

        internal static Exception End(State state, Exception exception)
        {
            try
            {
                try { state?.Protection.EndMapRemoval(state.Map); }
                finally
                {
                    if (state?.Site != null)
                    {
                        state.Protection.EndRecoverySiteRemoval(state.Site);
                    }
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "GroundMapRemovalGuard.End", null, ex);
            }
            return exception;
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.DeinitAndRemoveMap), new[] { typeof(MapType), typeof(bool) })]
    internal static class Game_DeinitAndRemoveMap_Patch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        internal static bool Prefix(MapType __0, out GroundMapRemovalGuard.State __state)
            => GroundMapRemovalGuard.Begin(__0, __0?.Parent, out __state);

        [HarmonyFinalizer]
        internal static Exception Finalizer(GroundMapRemovalGuard.State __state, Exception __exception)
            => GroundMapRemovalGuard.End(__state, __exception);
    }

    [HarmonyPatch(typeof(WorldObjectsHolder), nameof(WorldObjectsHolder.Remove), new[] { typeof(WorldObject) })]
    internal static class WorldObjectsHolder_Remove_Patch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        internal static bool Prefix(WorldObject __0, out GroundMapRemovalGuard.State __state)
            => GroundMapRemovalGuard.Begin((__0 as MapParent)?.Map, __0, out __state);

        [HarmonyFinalizer]
        internal static Exception Finalizer(GroundMapRemovalGuard.State __state, Exception __exception)
            => GroundMapRemovalGuard.End(__state, __exception);
    }

    [HarmonyPatch]
    internal static class GroundMapParent_Destroy_Patch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            // Guard every native declared override, including Site.Destroy, before its
            // pre-base/post-base side effects. A base-only prefix cannot cancel those.
            return typeof(WorldObject).Assembly.GetTypes()
                .Where(type => type == typeof(WorldObject) || typeof(MapParent).IsAssignableFrom(type))
                .Select(type => type.GetMethod(nameof(WorldObject.Destroy),
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                    null, Type.EmptyTypes, null))
                .Where(method => method != null && !method.IsAbstract && method.ReturnType == typeof(void));
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        internal static bool Prefix(WorldObject __instance, out GroundMapRemovalGuard.State __state)
            => GroundMapRemovalGuard.Begin((__instance as MapParent)?.Map, __instance, out __state);

        [HarmonyFinalizer]
        internal static Exception Finalizer(GroundMapRemovalGuard.State __state, Exception __exception)
            => GroundMapRemovalGuard.End(__state, __exception);
    }
}
