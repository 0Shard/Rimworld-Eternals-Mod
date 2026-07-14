// Relative Path: Eternal/Source/Eternal/Patches/SaveOurShip2/SOS2_ShipDestroyPatch.cs
// Creation Date: 25-12-2025
// Last Edit: 14-07-2026
// Author: 0Shard
// Description: Harmony patch for SOS2's WorldObjectOrbitingShip.Destroy.
//              When an orbiting ship world object is destroyed while its map still exists
//              (abandon gizmo, dev removal), Eternals aboard are crash-downed to a ground
//              crash site as torso-only corpses via SpaceCrashRescueService.
//              Burn-up removal is handled earlier by SOS2_OrbitingShipPatch; by the time
//              Destroy runs in that flow the map is already gone, so this is a no-op there.

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld.Planet;
using Verse;
using Eternal.Compatibility;
using Eternal.Exceptions;
using Eternal.Utils;
using Eternal.World;

// Type aliases to resolve namespace shadowing (Eternal.Map/Corpse shadows Verse types)
using MapType = Verse.Map;
using CorpseType = Verse.Corpse;

namespace Eternal.Patches.SaveOurShip2
{
    /// <summary>
    /// Harmony patch for SOS2's WorldObjectOrbitingShip.Destroy.
    /// Rescues Eternals from a ship map that is being destroyed outside the burn-up flow.
    /// </summary>
    [HarmonyPatch]
    public static class SOS2_ShipDestroyPatch
    {
        /// <summary>
        /// Only apply this patch if SOS2 is loaded.
        /// </summary>
        public static bool Prepare()
        {
            return SpaceModDetection.SaveOurShip2Active;
        }

        /// <summary>
        /// Dynamically targets SOS2's WorldObjectOrbitingShip.Destroy method.
        /// Resolves the type via AccessTools.TypeByName directly: TargetMethod runs at startup
        /// patching, before EternalServiceContainer/SOS2ReflectionCache exist (FinalizeInit),
        /// so SpaceModDetection.WorldObjectOrbitingShipType is always null here.
        /// </summary>
        public static MethodBase TargetMethod()
        {
            var shipType = AccessTools.TypeByName("SaveOurShip2.WorldObjectOrbitingShip");
            if (shipType == null)
            {
                return null;
            }

            var method = AccessTools.Method(shipType, "Destroy");
            if (method == null)
            {
                Log.Warning("[Eternal] Could not find Destroy method on WorldObjectOrbitingShip");
            }

            return method;
        }

        /// <summary>
        /// Prefix: crash-down every Eternal on the ship map before the world object
        /// (and with it the map) is destroyed.
        /// </summary>
        [HarmonyPrefix]
        public static void SaveEternalsBeforeDestroy(WorldObject __instance)
        {
            try
            {
                if (!(__instance is MapParent mapParent) ||
                    !SpaceModDetection.IsOrbitingShip(mapParent) ||
                    !mapParent.HasMap)
                {
                    return;
                }

                var living = new List<Pawn>();
                var corpses = new List<CorpseType>();
                SpaceCrashRescueService.CollectEternalsOnMap(mapParent.Map, null, living, corpses);

                int rescued = SpaceCrashRescueService.CrashDownEternals(living, corpses, __instance.Tile);
                if (rescued > 0)
                {
                    Log.Message($"[Eternal] Crash-downed {rescued} Eternal(s) from SOS2 ship destruction");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "SaveEternalsBeforeDestroy", null, ex);
            }
        }
    }
}
