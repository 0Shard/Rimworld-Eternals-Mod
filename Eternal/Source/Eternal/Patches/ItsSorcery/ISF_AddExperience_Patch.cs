// Relative Path: Eternal/Source/Eternal/Patches/ItsSorcery/ISF_AddExperience_Patch.cs
// Creation Date: 13-07-2026
// Last Edit: 13-07-2026
// Author: 0Shard
// Description: Harmony patch on ItsSorceryFramework.ProgressTracker_Level.AddExperience so any
// exp gain on an Eternal pawn's sorcery schema immediately force-levels it to the class cap.
// Eternals master techniques instantly. Postfix runs after the normal exp/level-up processing;
// ForceLevelUp never calls AddExperience, so there is no recursion. Also covers schemas learned
// before this patch existed (existing saves) — they max on their next exp tick.

using System;
using System.Reflection;
using HarmonyLib;
using Verse;
using Eternal.Compatibility;
using Eternal.Exceptions;
using Eternal.Extensions;
using Eternal.Utils;

namespace Eternal.Patches.ItsSorcery
{
    /// <summary>
    /// Patches ItsSorceryFramework.ProgressTracker_Level.AddExperience to immediately
    /// force-level Eternal pawns' sorcery schemas to their class cap.
    ///
    /// Mechanism: whenever an Eternal pawn gains experience in a schema (via AddExperience),
    /// the postfix checks if the pawn is a valid Eternal and if the schema is not already maxed.
    /// If so, it calls MaxOutSchema to force-level to the cap. No recursion risk since ForceLevelUp
    /// does not invoke AddExperience.
    /// </summary>
    [HarmonyPatch]
    public static class ISF_AddExperience_Patch
    {
        /// <summary>
        /// Guard: only apply this patch when It's Sorcery is active.
        /// Returning false skips the patch entirely (Harmony will not call TargetMethod).
        /// </summary>
        public static bool Prepare() => CompatibilityBinding.Prepare(ItsSorceryDetection.ItsSorceryActive,
            "[Eternal] Skipping It's Sorcery AddExperience patch — mod not active",
            "[Eternal] It's Sorcery detected - enabling schema insta-max for Eternals");

        /// <summary>
        /// Resolve the target method via reflection. This is the ProgressTracker_Level OVERRIDE —
        /// patching the base virtual would never fire. Returns null to skip patching if the method
        /// cannot be found (API change).
        /// </summary>
        public static MethodBase TargetMethod()
        {
            var method = ItsSorceryDetection.AddExperienceMethod;

            return CompatibilityBinding.WarnMissing(method,
                "[Eternal] Skipping It's Sorcery AddExperience patch — method not found (mod absent or API changed)");
        }

        /// <summary>
        /// Postfix: for Eternal pawns, force-level the schema to its cap after experience is applied.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix(object __instance)
        {
            try
            {
                Pawn pawn = ItsSorceryDetection.PawnField?.GetValue(__instance) as Pawn;
                if (pawn == null || !pawn.IsValidEternal())
                {
                    return;
                }

                if (ItsSorceryDetection.IsMaxed(__instance))
                {
                    return;
                }

                ItsSorceryDetection.MaxOutSchema(__instance);

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] ISF: maxed schema for {pawn.Name?.ToStringShort ?? "Pawn"} on exp gain");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "ISF_AddExperience_Patch.Postfix", null, ex);
            }
        }
    }
}
