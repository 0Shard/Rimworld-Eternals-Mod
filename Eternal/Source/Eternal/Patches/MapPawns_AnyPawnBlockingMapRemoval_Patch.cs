// Relative Path: Eternal/Source/Eternal/Patches/MapPawns_AnyPawnBlockingMapRemoval_Patch.cs
// Creation Date: 25-12-2025
// Last Edit: 13-07-2026
// Author: 0Shard
// Description: Harmony patch extending the AnyPawnBlockingMapRemoval check to tracked Eternal
//              corpses and active Eternal anchors. Every MapParent.ShouldRemoveMapNow override
//              (Site, Camp, CaravansBattlefield, Settlement, SpaceMapParent) consults this getter,
//              making it the single effective chokepoint for holding maps open. Always applied
//              (vanilla temporary maps need it as much as gravship/Odyssey scenarios).
//              EXCEPTION: space maps (SpaceMapParent / SOS2 orbiting ships) are never pinned -
//              corpses there would hold the map open forever ("stranded in space"); instead the
//              space-loss patches rescue Eternals to a ground crash site when the map is removed.

using System;
using System.Reflection;
using HarmonyLib;
using RimWorld.Planet;
using Verse;
using Eternal.Compatibility;
using Eternal.Corpse;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Map;
using Eternal.Utils;

// Type alias to resolve namespace shadowing (Eternal.Map shadows Verse.Map)
using MapType = Verse.Map;

namespace Eternal.Patches
{
    /// <summary>
    /// Patches MapPawns.AnyPawnBlockingMapRemoval to include Eternal corpses and anchors.
    /// When a map contains tracked Eternal corpses or active anchors, this patch ensures
    /// the map cannot be automatically removed, protecting corpses from destruction.
    /// </summary>
    /// <remarks>
    /// All MapParent.ShouldRemoveMapNow overrides check this getter first, so it protects
    /// vanilla temporary maps (quest sites, camps, caravan battlefields) as well as
    /// gravship/Odyssey scenarios. The postfix early-returns unless corpses/anchors are tracked.
    /// </remarks>
    [HarmonyPatch(typeof(MapPawns), nameof(MapPawns.AnyPawnBlockingMapRemoval), MethodType.Getter)]
    public static class MapPawns_AnyPawnBlockingMapRemoval_Patch
    {
        // Cached field info for accessing private map field
        private static FieldInfo _mapField;
        private static bool _fieldCacheInitialized = false;

        /// <summary>
        /// Initializes the field cache for accessing the private map field.
        /// </summary>
        private static void EnsureFieldCacheInitialized()
        {
            if (_fieldCacheInitialized)
                return;

            _fieldCacheInitialized = true;

            try
            {
                _mapField = AccessTools.Field(typeof(MapPawns), "map");
                if (_mapField == null)
                {
                    Log.Warning("[Eternal] Could not find MapPawns.map field");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "EnsureFieldCacheInitialized", null, ex);
            }
        }

        /// <summary>
        /// Gets the map from a MapPawns instance using reflection.
        /// </summary>
        private static MapType GetMapFromMapPawns(MapPawns mapPawns)
        {
            EnsureFieldCacheInitialized();

            if (_mapField == null || mapPawns == null)
                return null;

            try
            {
                return _mapField.GetValue(mapPawns) as MapType;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "MapPawns_GetMap", null, ex);
                return null;
            }
        }

        /// <summary>
        /// Postfix that extends the pawn blocking check to include Eternal corpses and anchors.
        /// If the original result is already true, no action needed.
        /// Otherwise, checks tracked Eternal corpses, then active Eternal anchors.
        /// </summary>
        /// <param name="__instance">The MapPawns instance being checked</param>
        /// <param name="__result">The result to potentially modify</param>
        [HarmonyPostfix]
        public static void Postfix(MapPawns __instance, ref bool __result)
        {
            // SAFE-09: skip all processing when mod is disabled due to missing critical defs.
            if (EternalModState.IsDisabled)
            {
                return;
            }

            // If already blocking, no need to check further
            if (__result)
            {
                return;
            }

            try
            {
                // Get the map from the MapPawns instance using reflection
                MapType map = GetMapFromMapPawns(__instance);
                if (map == null)
                {
                    return;
                }

                // Space maps are never pinned: a corpse blocking removal would strand it in
                // orbit forever. The SpaceMapParent/SOS2 rescue patches crash-down Eternals
                // to a ground crash site when these maps close instead.
                if (map.Parent is SpaceMapParent ||
                    SpaceModDetection.IsOrbitingShip(map.Parent))
                {
                    return;
                }

                // Check if this map contains any tracked Eternal corpses
                var corpseManager = EternalServiceContainer.Instance.CorpseManager;
                if (corpseManager != null && corpseManager.HasEternalCorpses(map))
                {
                    __result = true;

                    if (Eternal_Mod.settings?.debugMode == true)
                    {
                        Log.Message($"[Eternal] Map {map} blocked from removal - contains Eternal corpses");
                    }
                    return;
                }

                // Check for active Eternal anchors (previously an unreachable prefix on
                // MapParent.ShouldRemoveMapNow — its overrides never call the patched base)
                var mapManager = map.GetComponent<EternalMapManager>();
                if (mapManager != null && mapManager.ShouldRetainMap())
                {
                    __result = true;

                    if (Eternal_Mod.settings?.debugMode == true)
                    {
                        Log.Message($"[Eternal] Map {map} blocked from removal - {mapManager.GetActiveAnchors().Count} active Eternal anchor(s)");
                    }
                }
            }
            catch (Exception ex)
            {
                // Fail-safe: don't block the original method on error
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "AnyPawnBlockingMapRemoval.Postfix", null, ex);
            }
        }
    }
}
