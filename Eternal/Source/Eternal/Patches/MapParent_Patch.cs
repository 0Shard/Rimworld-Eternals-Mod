// file path: Eternal/Source/Eternal/Patches/MapParent_Patch.cs
// Author Name: 0Shard
// Date Created: 12-11-2025
// Date Last Modified: 21-02-2026
// Description: Harmony patches for MapParent to prevent temporary map closure when Eternal anchors are active.
// This is the critical patch that makes the anchor system actually work by intercepting RimWorld's map removal logic.
// Uses low priority and HarmonyAfter to run after other mods (SOS2, LAO) that also patch this method.

using System;
using HarmonyLib;
using RimWorld.Planet;
using Verse;
using Eternal;
using Eternal.Exceptions;
using Eternal.Map;
using Eternal.Utils;

namespace Eternal.Patches
{
    /// <summary>
    /// Harmony patches for MapParent to prevent temporary map closure when Eternal pawns need resurrection.
    /// Without this patch, the EternalAnchor system would be ineffective as RimWorld would still close maps.
    /// </summary>
    [HarmonyPatch(typeof(MapParent))]
    public static class MapParent_Patch
    {
        /// <summary>
        /// Prefix patch for ShouldRemoveMapNow to prevent map removal when Eternal anchors are active.
        /// This is the core of the map retention system - it intercepts RimWorld's decision to remove a map.
        ///
        /// Uses Priority.Low to run AFTER other mods' patches (SOS2, LayeredAtmosphereOrbit).
        /// We only BLOCK removal when we have anchors - we never force removal.
        /// This ensures cooperative behavior with other map-retention systems.
        /// </summary>
        [HarmonyPatch("ShouldRemoveMapNow")]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Low)]  // Run after other mods' patches
        [HarmonyAfter("kentington.saveourship2")]  // Explicit ordering after SOS2
        public static bool ShouldRemoveMapNow_Prefix(MapParent __instance, ref bool __result)
        {
            try
            {
                // SAFE-09: when mod is disabled, do not intervene in map removal logic.
                if (EternalModState.IsDisabled)
                    return true; // run original method

                // Check if this map parent has an active map
                if (!__instance.HasMap)
                {
                    return true; // No map to protect, run original logic
                }

                Verse.Map map = __instance.Map;
                if (map == null)
                {
                    return true; // Safety check, run original logic
                }

                // Get the EternalMapManager component for this map
                var mapManager = map.GetComponent<EternalMapManager>();
                if (mapManager == null)
                {
                    return true; // No map manager, run original logic
                }

                // Check if map should be retained due to active Eternal anchors
                if (mapManager.ShouldRetainMap())
                {
                    // Map has active anchors - prevent removal
                    __result = false;

                    // Log in debug mode
                    if (Eternal_Mod.settings?.debugMode == true)
                    {
                        int anchorCount = mapManager.GetActiveAnchors().Count;
                        Log.Message($"[Eternal] Prevented removal of map '{map}' due to {anchorCount} active Eternal anchor(s)");
                    }

                    // Skip original method - we've made the decision
                    return false;
                }

                // No anchors or map doesn't need retention, run original logic
                return true;
            }
            catch (Exception ex)
            {
                // Log error but allow original method to run to prevent breaking the game
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "ShouldRemoveMapNow_Prefix", null, ex);
                return true; // Run original method on error
            }
        }

        /// <summary>
        /// Postfix patch to log when maps are actually removed (for debugging).
        /// Helps identify if maps are being removed unexpectedly despite anchors.
        /// </summary>
        [HarmonyPatch("CheckRemoveMapNow")]
        [HarmonyPostfix]
        public static void CheckRemoveMapNow_Postfix(MapParent __instance, bool __result)
        {
            try
            {
                // SAFE-09: skip when mod is disabled.
                if (EternalModState.IsDisabled)
                    return;

                if (Eternal_Mod.settings?.debugMode == true && __result)
                {
                    // Map was actually removed
                    string mapName = __instance.HasMap ? __instance.Map.ToString() : "unknown";
                    Log.Message($"[Eternal] Map '{mapName}' was removed from world object '{__instance.Label}'");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "CheckRemoveMapNow_Postfix", null, ex);
            }
        }
    }
}
