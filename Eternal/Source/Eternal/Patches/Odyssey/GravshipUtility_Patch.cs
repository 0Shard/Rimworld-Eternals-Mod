// file path: Eternal/Source/Eternal/Patches/Odyssey/GravshipUtility_Patch.cs
// Author Name: 0Shard
// Date Created: 06-12-2025
// Date Last Modified: 13-07-2026
// Description: Harmony patches for Odyssey DLC compatibility.
//              Prevents Eternal pawns AND corpses from being destroyed when the gravship
//              leaves without them (GravshipUtility.AbandonMap despawns/destroys everything).
//              Ground abandonment: survivors go to a crash site at the abandoned tile.
//              Space-map abandonment: victims re-enter at terminal velocity and arrive at a
//              ground crash site as torso-only corpses (SpaceCrashRescueService).
//              Guards on __runOriginal - SOS2's NoSpaceMapDestruction prefix cancels
//              AbandonMap on its own space maps and nothing must be rescued then.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Eternal.Exceptions;
using Eternal.Extensions;
using Eternal.Utils;
using Eternal.World;

// Type aliases to resolve namespace shadowing (Eternal.Map/Corpse shadows Verse types)
using MapType = Verse.Map;
using CorpseType = Verse.Corpse;

namespace Eternal.Patches.Odyssey
{
    /// <summary>
    /// Data structure to safely pass data between Prefix and Postfix.
    /// Created fresh for each method call to prevent state corruption.
    /// Must be public for Harmony's __state parameter to work correctly.
    /// </summary>
    public class AbandonMapContext
    {
        public List<Pawn> EternalsToSave { get; } = new List<Pawn>();
        public List<CorpseType> CorpsesToSave { get; } = new List<CorpseType>();
        public int SavedWorldTile { get; set; } = -1;
        public bool WasSpaceMap { get; set; }
        public bool PrefixSucceeded { get; set; }
    }

    /// <summary>
    /// Harmony patches for GravshipUtility.AbandonMap to save Eternal pawns and corpses
    /// from being destroyed when the grav ship leaves without them.
    /// Uses reflection-based conditional patching to avoid TypeLoadException when
    /// the Odyssey DLC is absent. The patch is skipped entirely if Odyssey is not active.
    /// </summary>
    [HarmonyPatch]
    public static class GravshipUtility_AbandonMap_Patch
    {
        /// <summary>
        /// Guard: only apply this patch when the Odyssey DLC is active.
        /// </summary>
        public static bool Prepare() => ModsConfig.OdysseyActive;

        /// <summary>
        /// Resolve the target method via reflection so we never reference GravshipUtility
        /// at compile time. Returns null to skip patching if the method cannot be found.
        /// </summary>
        public static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("RimWorld.GravshipUtility");
            if (type == null)
            {
                Log.Warning("[Eternal] Skipping GravshipUtility.AbandonMap patch — type not found (Odyssey absent or API changed)");
                return null;
            }

            var method = AccessTools.Method(type, "AbandonMap");
            if (method == null)
            {
                Log.Warning("[Eternal] Skipping GravshipUtility.AbandonMap patch — method not found (Odyssey API changed)");
            }

            return method;
        }

        /// <summary>
        /// Prefix: Identify and despawn Eternal pawns and corpses before map abandonment
        /// destroys them. Runs at Low priority so SOS2's cancelling prefix (space ship maps)
        /// is reflected in __runOriginal first.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Low)]
        public static void SaveEternalsBeforeAbandonment(MapType map, bool __runOriginal, out AbandonMapContext __state)
        {
            __state = new AbandonMapContext();

            if (!ModsConfig.OdysseyActive || !__runOriginal || map == null)
            {
                return;
            }

            try
            {
                __state.SavedWorldTile = map.Tile;
                __state.WasSpaceMap = map.Tile.LayerDef?.isSpace == true;

                SpaceCrashRescueService.CollectEternalsOnMap(map, null,
                    __state.EternalsToSave, __state.CorpsesToSave);

                // Despawn BEFORE the original loop destroys them (WillReplace preserves the objects)
                foreach (var pawn in __state.EternalsToSave)
                {
                    if (pawn.Spawned)
                    {
                        pawn.DeSpawn(DestroyMode.WillReplace);
                    }
                    Log.Message($"[Eternal] Saving {pawn.Name} from gravship abandonment");
                }

                foreach (var corpse in __state.CorpsesToSave)
                {
                    if (corpse.Spawned)
                    {
                        corpse.DeSpawn(DestroyMode.WillReplace);
                    }
                    Log.Message($"[Eternal] Saving corpse of {corpse.InnerPawn?.Name} from gravship abandonment");
                }

                __state.PrefixSucceeded = true;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "SaveEternalsBeforeAbandonment", null, ex);
                __state.PrefixSucceeded = false;
            }
        }

        /// <summary>
        /// Postfix: deliver the saved Eternals. Space maps route through the terminal-velocity
        /// crash-down (torso corpses on the surface below); ground maps keep the classic
        /// behavior - a crash site at the abandoned tile with fall damage for the living.
        /// </summary>
        [HarmonyPostfix]
        public static void SpawnEternalsAtCrashSite(MapType map, bool __runOriginal, AbandonMapContext __state)
        {
            if (!ModsConfig.OdysseyActive || !__runOriginal ||
                __state == null || !__state.PrefixSucceeded)
            {
                return;
            }

            if (__state.EternalsToSave.Count == 0 && __state.CorpsesToSave.Count == 0)
            {
                return;
            }

            try
            {
                if (__state.WasSpaceMap)
                {
                    int rescued = SpaceCrashRescueService.CrashDownEternals(
                        __state.EternalsToSave, __state.CorpsesToSave, __state.SavedWorldTile);
                    Log.Message($"[Eternal] Crash-downed {rescued} Eternal(s) abandoned in space");
                    return;
                }

                var crashSite = SpaceCrashRescueService.CreateOrGetCrashSite(__state.SavedWorldTile);
                if (crashSite == null)
                {
                    SpaceCrashRescueService.SpawnAtHomeColony(
                        __state.EternalsToSave.Concat(__state.CorpsesToSave.Select(c => c.InnerPawn)));
                    return;
                }

                foreach (var eternal in __state.EternalsToSave)
                {
                    if (eternal == null || eternal.Destroyed)
                    {
                        continue;
                    }

                    // Severe but non-lethal fall damage - Eternals regenerate
                    SpaceCrashRescueService.ApplyFallDamage(eternal);
                    crashSite.AddPawn(eternal);
                    Log.Message($"[Eternal] {eternal.Name} fell to the surface at crash site");
                }

                foreach (var corpse in __state.CorpsesToSave)
                {
                    if (corpse != null && !corpse.Destroyed)
                    {
                        crashSite.AddCorpse(corpse);
                    }
                }

                Find.LetterStack.ReceiveLetter(
                    "EternalFellFromSpace".Translate(),
                    "EternalFellFromSpaceDesc".Translate(
                        __state.EternalsToSave.Count + __state.CorpsesToSave.Count),
                    LetterDefOf.NegativeEvent,
                    crashSite);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "SpawnEternalsAtCrashSite", null, ex);
                SpaceCrashRescueService.SpawnAtHomeColony(
                    __state.EternalsToSave.Concat(__state.CorpsesToSave.Select(c => c.InnerPawn)));
            }
        }
    }
}
