// Author: 0Shard
// Description: Optional RJW health classification shared by healing and settings UI.
using System;
using System.Collections.Generic;
using Verse;

namespace Eternal.Compatibility
{
    public enum RjwHealthKind
    {
        Unowned,
        Anatomy,
        Pregnancy,
        FertilityImpairment,
        FertilityControl
    }

    public static class RjwHealthPolicy
    {
        public const string PackageId = "rim.job.world";

        // Legacy generation selects manufactured anatomy above its natural thresholds.
        // Preserve the normal random draw while keeping Eternal regrowth below every such branch.
        internal static double LimitLegacySelector(double current, bool fallbackActive)
            => fallbackActive ? Math.Min(current, 0.69) : current;

        // Pure policy entry points avoid touching the game during offline verification.
        public static RjwHealthKind Classify(string packageId, string defName, Type hediffClass, bool rjwActive)
        {
            var typeKind = ClassifyType(hediffClass);
            if (typeKind != RjwHealthKind.Unowned)
            {
                return typeKind;
            }

            // RJW can use the normal pregnancy implementation. Only core/Biotech defs are included;
            // another mod's similarly named condition keeps its original semantics.
            if (rjwActive && (string.Equals(packageId, "ludeon.rimworld", StringComparison.OrdinalIgnoreCase)
                || string.Equals(packageId, "ludeon.rimworld.biotech", StringComparison.OrdinalIgnoreCase))
                && hediffClass?.FullName == "Verse.Hediff_Pregnant"
                && hediffClass.Assembly.GetName().Name == "Assembly-CSharp")
            {
                return RjwHealthKind.Pregnancy;
            }

            if (!string.Equals(packageId, PackageId, StringComparison.OrdinalIgnoreCase))
            {
                return RjwHealthKind.Unowned;
            }

            switch (defName)
            {
                case "Sterilized":
                case "DecreasedFertility":
                    return RjwHealthKind.FertilityImpairment;
                case "RJW_IUD":
                case "ImpregnationBlocker":
                case "IncreasedFertility":
                case "FertilityEnhancer":
                    return RjwHealthKind.FertilityControl;
                default:
                    return RjwHealthKind.Unowned;
            }
        }

        public static bool? DefaultCanHeal(RjwHealthKind kind)
        {
            switch (kind)
            {
                case RjwHealthKind.Anatomy:
                case RjwHealthKind.Pregnancy:
                case RjwHealthKind.FertilityControl:
                    return false;
                case RjwHealthKind.FertilityImpairment:
                    return true;
                default:
                    return null;
            }
        }

        public static bool? ResolveHealingDecision(RjwHealthKind kind, bool? configuredCanHeal)
        {
            if (kind == RjwHealthKind.Anatomy || kind == RjwHealthKind.Pregnancy)
            {
                return false;
            }

            return kind == RjwHealthKind.Unowned ? (bool?)null : configuredCanHeal ?? DefaultCanHeal(kind);
        }

        public static bool IsAnatomy(HediffDef def) => GetKind(def) == RjwHealthKind.Anatomy;

        public static bool IsProtected(HediffDef def)
        {
            var kind = GetKind(def);
            return kind == RjwHealthKind.Anatomy || kind == RjwHealthKind.Pregnancy;
        }

        public static bool IsFertilityCondition(HediffDef def)
        {
            var kind = GetKind(def);
            return kind == RjwHealthKind.FertilityImpairment || kind == RjwHealthKind.FertilityControl;
        }

        public static bool? GetDefaultCanHeal(HediffDef def) => DefaultCanHeal(GetKind(def));

        public static bool? GetHealingDecision(Hediff hediff, EternalHediffSetting setting)
            => GetHealingDecisionForDef(hediff?.def, setting);

        public static bool? GetHealingDecisionForDef(HediffDef def, EternalHediffSetting setting)
            => ResolveHealingDecision(GetKind(def), setting?.canHeal);

        public static bool? GetCorpseHealingDecision(HediffDef def)
        {
            var kind = GetKind(def);
            if (kind == RjwHealthKind.Unowned)
            {
                return null;
            }
            if (kind == RjwHealthKind.Anatomy || kind == RjwHealthKind.Pregnancy)
            {
                return false;
            }

            var setting = Eternal_Mod.GetSettings().hediffManager?.GetHediffSetting(def);
            return ResolveHealingDecision(kind, setting?.canHeal);
        }

        public static bool EffectiveCanHeal(HediffDef def, EternalHediffSetting setting)
            => ResolveHealingDecision(GetKind(def), setting?.canHeal) ?? setting?.canHeal ?? false;

        public static string ProtectionReason(HediffDef def)
        {
            switch (GetKind(def))
            {
                case RjwHealthKind.Anatomy:
                    return "Eternal_RjwProtectedAnatomy".Translate();
                case RjwHealthKind.Pregnancy:
                    return "Eternal_RjwProtectedPregnancy".Translate();
                default:
                    return string.Empty;
            }
        }

        private static RjwHealthKind GetKind(HediffDef def)
        {
            if (def == null)
            {
                return RjwHealthKind.Unowned;
            }

            if (!RuntimeCache.DefKinds.TryGetValue(def, out var kind))
            {
                kind = Classify(def.modContentPack?.PackageIdPlayerFacing, def.defName, def.hediffClass,
                    ModsConfig.IsActive(PackageId));
                RuntimeCache.DefKinds.Add(def, kind);
            }

            return kind;
        }

        private static RjwHealthKind ClassifyType(Type type)
        {
            if (type == null)
            {
                return RjwHealthKind.Unowned;
            }

            if (TypeCache.Kinds.TryGetValue(type, out var kind))
            {
                return kind;
            }

            kind = RjwHealthKind.Unowned;
            foreach (var contract in type.GetInterfaces())
            {
                if (IsRjwType(contract, "rjw.ISexPartHediff"))
                {
                    kind = RjwHealthKind.Anatomy;
                    break;
                }
            }

            if (kind == RjwHealthKind.Unowned)
            {
                for (var ancestor = type; ancestor != null; ancestor = ancestor.BaseType)
                {
                    if (IsRjwType(ancestor, "rjw.Hediff_BasePregnancy"))
                    {
                        kind = RjwHealthKind.Pregnancy;
                        break;
                    }
                }
            }

            TypeCache.Kinds.Add(type, kind);
            return kind;
        }

        private static bool IsRjwType(Type type, string fullName)
            => type.FullName == fullName && type.Assembly.GetName().Name == "RJW";

        private static class TypeCache
        {
            internal static readonly Dictionary<Type, RjwHealthKind> Kinds = new Dictionary<Type, RjwHealthKind>();
        }

        private static class RuntimeCache
        {
            internal static readonly Dictionary<HediffDef, RjwHealthKind> DefKinds = new Dictionary<HediffDef, RjwHealthKind>();
        }
    }
}
