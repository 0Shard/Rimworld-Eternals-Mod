// Relative Path: Eternal/Source/Eternal/Patches/Odyssey/SpaceMapParent_Patch.cs
// Creation Date: 13-07-2026
// Last Edit: 13-07-2026
// Author: 0Shard
// Description: Harmony patch for vanilla Odyssey SpaceMapParent.ShouldRemoveMapNow.
//              Covers every space map built on this parent (vanilla orbital sites, LAO asteroid
//              and item-stash quest maps - LAO's AtmosphereMapParent derives from it). Quest
//              timeouts (QuestPart_DestroyWorldObject) only set forceRemoveWorldObjectWhenMapRemoved,
//              so this check remains the single removal gate. When removal is approved, Eternals
//              still on the map are crash-downed to a ground crash site as torso-only corpses
//              (the AnyPawnBlockingMapRemoval postfix deliberately does not pin space maps).

using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld.Planet;
using Verse;
using Eternal.Exceptions;
using Eternal.Utils;
using Eternal.World;

// Type aliases to resolve namespace shadowing (Eternal.Map/Corpse shadows Verse types)
using MapType = Verse.Map;
using CorpseType = Verse.Corpse;

namespace Eternal.Patches.Odyssey
{
    /// <summary>
    /// Rescues Eternals from a vanilla/LAO space map the moment its removal is approved.
    /// </summary>
    [HarmonyPatch(typeof(SpaceMapParent), nameof(SpaceMapParent.ShouldRemoveMapNow))]
    public static class SpaceMapParent_ShouldRemoveMapNow_Patch
    {
        /// <summary>
        /// Only apply when the Odyssey DLC (which owns SpaceMapParent) is active.
        /// </summary>
        public static bool Prepare() => ModsConfig.OdysseyActive;

        /// <summary>
        /// Postfix: when the original approved removal, crash-down every Eternal
        /// (living and corpse) still on the map before it is torn down.
        /// </summary>
        [HarmonyPostfix]
        public static void RescueEternalsBeforeRemoval(SpaceMapParent __instance, bool __result)
        {
            if (!__result)
            {
                return;
            }

            try
            {
                if (!__instance.HasMap)
                {
                    return;
                }

                var living = new List<Pawn>();
                var corpses = new List<CorpseType>();
                SpaceCrashRescueService.CollectEternalsOnMap(__instance.Map, null, living, corpses);

                int rescued = SpaceCrashRescueService.CrashDownEternals(living, corpses, __instance.Tile);
                if (rescued > 0)
                {
                    Log.Message($"[Eternal] Crash-downed {rescued} Eternal(s) from closing space map {__instance.Label}");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.MapProtection,
                    "SpaceMapParent.RescueEternalsBeforeRemoval", null, ex);
            }
        }
    }
}
