// Relative Path: Eternal/Source/Eternal/Patches/SaveOurShip2/SOS2_OrbitingShipPatch.cs
// Creation Date: 25-12-2025
// Last Edit: 13-07-2026
// Author: 0Shard
// Description: Harmony patches for Save Our Ship 2 compatibility.
//              Intercepts WorldObjectOrbitingShip.ShouldRemoveMapNow before SOS2's burn-up
//              branch Bomb-kills every pawn and removes the map. Eternals are routed through
//              SpaceCrashRescueService: killed in place, stripped to a torso by terminal-velocity
//              re-entry, and delivered to a ground crash site for resurrection.

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
    /// Harmony patch for SOS2's WorldObjectOrbitingShip.ShouldRemoveMapNow.
    ///
    /// CRITICAL: SOS2's override kills ALL pawns (Bomb 99999) when ShipMapState == burnUpSet,
    /// then removes the map (corpses vanish with it). This prefix rescues Eternals first.
    /// Covers ship burn-up, graveyard TimedForcedExitShip timeouts, and failed landings.
    /// </summary>
    [HarmonyPatch]
    public static class SOS2_OrbitingShipPatch
    {
        /// <summary>
        /// Harmony's Prepare method - only apply this patch if SOS2 is loaded.
        /// </summary>
        public static bool Prepare()
        {
            return SpaceModDetection.SaveOurShip2Active;
        }

        /// <summary>
        /// Dynamically targets SOS2's WorldObjectOrbitingShip.ShouldRemoveMapNow method.
        /// </summary>
        public static MethodBase TargetMethod()
        {
            var shipType = SpaceModDetection.WorldObjectOrbitingShipType;
            if (shipType == null)
            {
                Log.Warning("[Eternal] Could not find WorldObjectOrbitingShip type for patching");
                return null;
            }

            var method = AccessTools.Method(shipType, "ShouldRemoveMapNow");
            if (method == null)
            {
                Log.Warning("[Eternal] Could not find ShouldRemoveMapNow method on WorldObjectOrbitingShip");
            }

            return method;
        }

        /// <summary>
        /// Prefix: if the ship is burning up, crash-down every Eternal (living and corpse)
        /// before SOS2's kill loop and map removal run.
        /// </summary>
        [HarmonyPrefix]
        public static void RescueEternalsBeforeBurnUp(MapParent __instance)
        {
            try
            {
                if (!SpaceModDetection.IsOrbitingShip(__instance) ||
                    !SpaceModDetection.IsShipBurningUp(__instance) ||
                    !__instance.HasMap)
                {
                    return;
                }

                var living = new List<Pawn>();
                var corpses = new List<CorpseType>();
                SpaceCrashRescueService.CollectEternalsOnMap(__instance.Map, null, living, corpses);

                int rescued = SpaceCrashRescueService.CrashDownEternals(living, corpses, __instance.Tile);
                if (rescued > 0)
                {
                    Log.Message($"[Eternal] Crash-downed {rescued} Eternal(s) from SOS2 ship burn-up");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "RescueEternalsBeforeBurnUp", null, ex);
            }
        }
    }
}
