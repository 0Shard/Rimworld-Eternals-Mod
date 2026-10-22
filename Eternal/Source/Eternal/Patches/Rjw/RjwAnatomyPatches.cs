// Author: 0Shard
// Description: Capture optional RJW anatomy before removal and reconcile complete surgery operations.
using System;
using System.Collections.Generic;
using System.Reflection;
using Eternal.Compatibility;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Utils;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Eternal.Patches.Rjw
{
    internal static class RjwPatchAccess
    {
        internal static RjwAnatomyTracker TrackerFor(Pawn pawn)
        {
            if (EternalModState.IsDisabled || Eternal_Component.Instance == null
                || !Eternal_Component.ShouldQueueHealthReconciliation(pawn))
            {
                return null;
            }

            var tracker = EternalServiceContainer.Instance.RjwAnatomy;
            return tracker?.IsAvailable == true ? tracker : null;
        }

        internal static MethodInfo SurgeryMethod(string typeName, string methodName, Type[] arguments)
        {
            var type = AccessTools.TypeByName(typeName);
            return type == null ? null : AccessTools.DeclaredMethod(type, methodName, arguments);
        }

        internal static void Guard(Pawn pawn, string operation, Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure, operation, pawn, exception);
            }
        }
    }

    [HarmonyPatch(typeof(Hediff_MissingPart), nameof(Hediff_MissingPart.PostAdd), new Type[] { typeof(DamageInfo?) })]
    internal static class Rjw_MissingPartCapture_Patch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        public static void Capture(Hediff_MissingPart __instance)
        {
            RjwPatchAccess.Guard(__instance.pawn, nameof(Rjw_MissingPartCapture_Patch),
                () => RjwPatchAccess.TrackerFor(__instance.pawn)?.CaptureBeforeLoss(__instance.pawn, __instance.Part));
        }
    }

    [HarmonyPatch(typeof(HediffSet), nameof(HediffSet.AddDirect), new Type[] { typeof(Hediff), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) })]
    internal static class Rjw_AnatomyAdded_Patch
    {
        [HarmonyPrefix]
        public static bool AllowAddition(Hediff hediff, Pawn ___pawn)
        {
            // The legacy race generator can add manufactured or unrelated anatomy. Limit
            // only Eternal's scoped fallback call; ordinary game additions remain untouched.
            return RjwPatchAccess.TrackerFor(___pawn)?.AllowLegacyAddition(___pawn, hediff?.def, hediff?.Part) ?? true;
        }

        [HarmonyPostfix]
        public static void RecordAddition(HediffSet __instance, Hediff hediff)
        {
            try
            {
                if (hediff != null && RjwHealthPolicy.IsAnatomy(hediff.def) && __instance.hediffs.Contains(hediff))
                {
                    RjwPatchAccess.TrackerFor(hediff.pawn)?.RecordCurrentAnatomy(hediff.pawn);
                }
            }
            catch (Exception exception)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    nameof(Rjw_AnatomyAdded_Patch), hediff?.pawn, exception);
            }
        }
    }

    [HarmonyPatch(typeof(HediffWithComps), nameof(HediffWithComps.PostRemoved))]
    internal static class Rjw_AnatomyRemoved_Patch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        public static void CaptureRemoval(HediffWithComps __instance)
        {
            try
            {
                if (RjwHealthPolicy.IsAnatomy(__instance.def))
                {
                    RjwPatchAccess.TrackerFor(__instance.pawn)?.CaptureRemoved(__instance);
                }
            }
            catch (Exception exception)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    nameof(Rjw_AnatomyRemoved_Patch), __instance.pawn, exception);
            }
        }
    }

    [HarmonyPatch]
    internal static class Rjw_LegacyBiology_Patch
    {
        public static bool Prepare() => ModsConfig.IsActive(RjwHealthPolicy.PackageId) && TargetMethod() != null;

        public static MethodBase TargetMethod() => RjwPatchAccess.SurgeryMethod("rjw.LegacySexPartAdder", "GenderTechLevelCheck",
            new[] { typeof(Pawn) });

        [HarmonyPostfix]
        public static void KeepBiology(Pawn pawn, ref double __result)
        {
            bool fallbackActive = RjwPatchAccess.TrackerFor(pawn)?.IsLegacyFallbackActive(pawn) == true;
            __result = RjwHealthPolicy.LimitLegacySelector(__result, fallbackActive);
        }
    }

    [HarmonyPatch]
    internal static class Rjw_SurgeryMutation_Patch
    {
        public static bool Prepare() => ModsConfig.IsActive(RjwHealthPolicy.PackageId)
            && RjwPatchAccess.SurgeryMethod("rjw.Recipe_RemovePart", "ApplyOnPawn", Arguments) != null
            && RjwPatchAccess.SurgeryMethod("rjw.Recipe_InstallPart", "ApplyOnPawn", Arguments) != null;

        private static Type[] Arguments => new[] { typeof(Pawn), typeof(BodyPartRecord), typeof(Pawn), typeof(List<Thing>), typeof(Bill) };

        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return RjwPatchAccess.SurgeryMethod("rjw.Recipe_RemovePart", "ApplyOnPawn", Arguments);
            yield return RjwPatchAccess.SurgeryMethod("rjw.Recipe_InstallPart", "ApplyOnPawn", Arguments);
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        public static void Begin(Pawn pawn, out RjwAnatomyTracker __state)
        {
            RjwAnatomyTracker state = null;
            RjwPatchAccess.Guard(pawn, nameof(Rjw_SurgeryMutation_Patch), () =>
            {
                var tracker = RjwPatchAccess.TrackerFor(pawn);
                tracker?.BeginMutation(pawn);
                state = tracker;
            });
            __state = state;
        }

        [HarmonyFinalizer]
        public static Exception End(Pawn pawn, RjwAnatomyTracker __state, Exception __exception)
        {
            RjwPatchAccess.Guard(pawn, nameof(Rjw_SurgeryMutation_Patch),
                () => __state?.EndMutation(pawn, __exception == null));
            return __exception;
        }
    }
}
