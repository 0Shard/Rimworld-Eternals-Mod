// Relative Path: Eternal/Source/Eternal/Patches/Corpse_Destroy_Patch.cs
// Creation Date: 14-07-2026
// Last Edit: 14-07-2026
// Author: 0Shard
// Description: Harmony prefix on Verse.Corpse.Destroy — safety net against silent loss of tracked
//              Eternal corpses via container sweeps (ClearAndDestroyContentsOrPassToWorld from
//              pod arrivals, SOS2 ship deletion, other mods). An unspawned tracked corpse being
//              Destroy(Vanish)ed without an expected-destroy mark is rescued to a crash site
//              instead of destroyed. Spawned-corpse destroys (butcher/cremate — deliberate player
//              actions) pass through via the Spawned check; the mod's own corpse consumption
//              (ResurrectionUtility.TryResurrect) passes through via MarkExpectedDestruction.
//              Container sweeps call Remove(at) after DestroyOrPassToWorld, so returning false
//              does not infinite-loop; DeliverCorpsesToCrashSite re-homes the corpse itself.

using System;
using HarmonyLib;
using Verse;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Utils;
using Eternal.World;

// Type alias to resolve namespace shadowing (Eternal.Corpse shadows Verse.Corpse)
using CorpseType = Verse.Corpse;

namespace Eternal.Patches
{
    /// <summary>
    /// Last-line guard: no code path may silently vaporize a tracked Eternal corpse.
    /// Blocks unexpected Vanish-destroys of unspawned tracked corpses and rescues them
    /// to a crash site at the corpse's best-known world tile.
    /// </summary>
    [HarmonyPatch(typeof(CorpseType), nameof(CorpseType.Destroy))]
    public static class Corpse_Destroy_Patch
    {
        /// <summary>
        /// Pure guard predicate, extracted for unit testing. Rescue exactly when the manager
        /// tracks THIS corpse instance, it is unspawned (spawned destroys are deliberate player
        /// actions), the mode is the silent Vanish sweep, and the mod did not mark the destroy
        /// as expected (resurrection consuming the corpse). Bool-only signature by design:
        /// the Mono test runner cannot load Verse types (no Assembly-CSharp at test time),
        /// so the DestroyMode comparison stays in the prefix.
        /// </summary>
        public static bool ShouldRescueFromDestroy(bool isTrackedCurrentCorpse, bool spawned,
            bool isVanishMode, bool expectedDestruction)
        {
            return isTrackedCurrentCorpse && !spawned && isVanishMode && !expectedDestruction;
        }

        [HarmonyPrefix]
        public static bool Prefix(CorpseType __instance, DestroyMode mode)
        {
            try
            {
                if (EternalModState.IsDisabled)
                {
                    return true;
                }

                var innerPawn = __instance?.InnerPawn;
                var corpseManager = EternalServiceContainer.Instance?.CorpseManager;
                if (innerPawn == null || corpseManager == null)
                {
                    return true;
                }

                // Corpses are keyed by original pawn — verify the entry points at THIS corpse
                // instance so a stale entry cannot block destruction of an unrelated corpse.
                var trackingEntry = corpseManager.GetCorpseData(innerPawn);
                bool isTrackedCurrentCorpse = trackingEntry != null && trackingEntry.Corpse == __instance;

                if (!ShouldRescueFromDestroy(isTrackedCurrentCorpse, __instance.Spawned,
                    mode == DestroyMode.Vanish, corpseManager.IsExpectedDestruction(__instance)))
                {
                    return true;
                }

                Log.Warning($"[Eternal] Blocked silent Vanish-destroy of tracked Eternal corpse " +
                    $"({innerPawn.Name}) — rescuing to a crash site. Destruction source:\n{Environment.StackTrace}");

                SpaceCrashRescueService.DeliverCorpsesToCrashSite(
                    new[] { __instance }, __instance.Tile);

                return false;
            }
            catch (Exception ex)
            {
                // Fail-open: never block vanilla destruction on a guard error.
                EternalLogger.HandleException(EternalExceptionCategory.CorpseTracking,
                    "Corpse_Destroy.Prefix", null, ex);
                return true;
            }
        }
    }
}
