// file path: Eternal/Source/Eternal/Patches/MapParent_Patch.cs
// Author Name: 0Shard
// Date Created: 12-11-2025
// Date Last Modified: 11-07-2026
// Description: Harmony debug patch for MapParent map removal logging.
// Map retention itself lives in MapPawns_AnyPawnBlockingMapRemoval_Patch: every
// ShouldRemoveMapNow override consults that getter, while the base virtual this file
// used to prefix returns false unconditionally and is never reached by the overrides.

using System;
using HarmonyLib;
using RimWorld.Planet;
using Verse;
using Eternal;
using Eternal.Exceptions;
using Eternal.Utils;

namespace Eternal.Patches
{
    /// <summary>
    /// Debug-logging patch for MapParent map removal.
    /// </summary>
    [HarmonyPatch(typeof(MapParent))]
    public static class MapParent_Patch
    {
        /// <summary>
        /// Prefix capturing whether the world object had a map before the removal check.
        /// CheckRemoveMapNow returns void, so removal is detected via the HasMap transition
        /// in the postfix (a bool __result parameter here throws at patch time and aborts PatchAll).
        /// </summary>
        [HarmonyPatch("CheckRemoveMapNow")]
        [HarmonyPrefix]
        public static void CheckRemoveMapNow_Prefix(MapParent __instance, ref bool __state)
        {
            __state = __instance.HasMap;
        }

        /// <summary>
        /// Postfix patch to log when maps are actually removed (for debugging).
        /// Helps identify if maps are being removed unexpectedly despite protections.
        /// </summary>
        [HarmonyPatch("CheckRemoveMapNow")]
        [HarmonyPostfix]
        public static void CheckRemoveMapNow_Postfix(MapParent __instance, bool __state)
        {
            try
            {
                // SAFE-09: skip when mod is disabled.
                if (EternalModState.IsDisabled)
                    return;

                if (Eternal_Mod.settings?.debugMode == true && __state && !__instance.HasMap)
                {
                    // Map was actually removed
                    Log.Message($"[Eternal] Map was removed from world object '{__instance.Label}'");
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
