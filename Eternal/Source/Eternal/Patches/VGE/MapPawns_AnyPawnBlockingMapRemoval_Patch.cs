// Relative Path: Eternal/Source/Eternal/Patches/VGE/MapPawns_AnyPawnBlockingMapRemoval_Patch.cs
// Creation Date: 25-12-2025
// Last Edit: 20-02-2026
// Author: 0Shard
// Description: Harmony patch for VGE compatibility. Extends the AnyPawnBlockingMapRemoval check
//              to include tracked Eternal corpses, preventing map removal when Eternal corpses
//              are present. This ensures corpses aren't lost when gravships leave or maps are abandoned.

using System;
using System.Reflection;
using HarmonyLib;
using Verse;
using Eternal.Compatibility;
using Eternal.Corpse;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Utils;

// Type alias to resolve namespace shadowing (Eternal.Map shadows Verse.Map)
using MapType = Verse.Map;

namespace Eternal.Patches.VGE
{
    /// <summary>
    /// Patches MapPawns.AnyPawnBlockingMapRemoval to include Eternal corpses.
    /// When a map contains tracked Eternal corpses, this patch ensures the map
    /// cannot be automatically removed, protecting corpses from destruction.
    /// </summary>
    /// <remarks>
    /// This patch is specifically designed for VGE (Vanilla Gravship Expanded) compatibility,
    /// where gravship operations can trigger map removal that would destroy corpses.
    /// The patch only activates when VGE or Odyssey is present to minimize overhead.
    /// </remarks>
    [HarmonyPatch(typeof(MapPawns), nameof(MapPawns.AnyPawnBlockingMapRemoval), MethodType.Getter)]
    public static class MapPawns_AnyPawnBlockingMapRemoval_Patch
    {
        // Cached field info for accessing private map field
        private static FieldInfo _mapField;
        private static bool _fieldCacheInitialized = false;

        /// <summary>
        /// Determines if this patch should be applied.
        /// Only patches when VGE or Odyssey is active (gravship functionality present).
        /// </summary>
        public static bool Prepare()
        {
            bool shouldPatch = SpaceModDetection.VanillaGravshipExpandedActive ||
                               SpaceModDetection.OdysseyActive;

            if (shouldPatch && Eternal_Mod.settings?.debugMode == true)
            {
                Log.Message("[Eternal] MapPawns_AnyPawnBlockingMapRemoval_Patch enabled for VGE/Odyssey compatibility");
            }

            return shouldPatch;
        }

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
        /// Postfix that extends the pawn blocking check to include Eternal corpses.
        /// If the original result is already true, no action needed.
        /// Otherwise, checks if the map contains tracked Eternal corpses.
        /// </summary>
        /// <param name="__instance">The MapPawns instance being checked</param>
        /// <param name="__result">The result to potentially modify</param>
        [HarmonyPostfix]
        public static void Postfix(MapPawns __instance, ref bool __result)
        {
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

                // Check if this map contains any tracked Eternal corpses
                var corpseManager = EternalServiceContainer.Instance.CorpseManager;
                if (corpseManager == null)
                {
                    return;
                }

                if (corpseManager.HasEternalCorpses(map))
                {
                    __result = true;

                    if (Eternal_Mod.settings?.debugMode == true)
                    {
                        Log.Message($"[Eternal] Map {map} blocked from removal - contains Eternal corpses");
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
