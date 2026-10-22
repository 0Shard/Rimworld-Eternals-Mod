// Relative Path: Eternal/Source/Eternal/Patches/ItsSorcery/ISF_ApplyOptions_Patch.cs
// Creation Date: 13-07-2026
// Last Edit: 13-07-2026
// Author: 0Shard
// Description: Harmony patch on ItsSorceryFramework.ProgressTracker.ApplyOptions that auto-picks
// level-up perk options for Eternal pawns instead of queueing a Dialog_ProgressLevelOptions per
// level. Required because force-maxing a schema (up to 200 levels) would otherwise stack dozens
// of unskippable, game-pausing dialogs. Picks by fixed priority (muscle-labeled > energy recovery
// > tribulation reduction > cultivation speed > random) and applies the option through the same
// public AdjustModifiers/AdjustAbilities/AdjustHediffs + points calls the mod's own non-player
// branch and dialog Confirm button use. Non-Eternal pawns keep the vanilla dialog flow.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Eternal.Compatibility;
using Eternal.Exceptions;
using Eternal.Extensions;
using Eternal.Utils;

namespace Eternal.Patches.ItsSorcery
{
    /// <summary>
    /// Patches ItsSorceryFramework.ProgressTracker.ApplyOptions(ProgressLevelModifier modifier,
    /// ref List&lt;Window&gt; windows, ref ProgressDiffClassLedger classLedger)
    /// to auto-pick perk options for Eternal pawns, preventing stacked unskippable dialogs
    /// when force-maxing schemas.
    ///
    /// Mechanism: when an Eternal pawn levels up in a sorcery schema, instead of queuing
    /// a Dialog_ProgressLevelOptions, the patch evaluates all available perk options using
    /// a priority tier system (muscle-labeled > energy recovery > tribulation reduction >
    /// cultivation speed > random), applies the top-ranked option, and returns false to skip
    /// the vanilla dialog flow.
    /// </summary>
    [HarmonyPatch]
    public static class ISF_ApplyOptions_Patch
    {
        /// <summary>
        /// Guard: only apply this patch when It's Sorcery is active.
        /// Returning false skips the patch entirely (Harmony will not call TargetMethod).
        /// </summary>
        public static bool Prepare() => CompatibilityBinding.Prepare(ItsSorceryDetection.ItsSorceryActive,
            "[Eternal] Skipping It's Sorcery ApplyOptions patch — mod not active",
            "[Eternal] It's Sorcery detected - enabling perk auto-pick for Eternals");

        /// <summary>
        /// Resolve the target method via reflection.
        /// Returns null to skip patching if the method cannot be found (API change).
        /// </summary>
        public static MethodBase TargetMethod()
        {
            var method = ItsSorceryDetection.ApplyOptionsMethod;

            return CompatibilityBinding.WarnMissing(method,
                "[Eternal] Skipping It's Sorcery ApplyOptions patch — method not found (mod absent or API changed)");
        }

        /// <summary>
        /// Prefix: for Eternal pawns, auto-pick the best perk option and skip the vanilla dialog.
        /// Uses __args to handle by-ref parameters (can't name them directly).
        /// </summary>
        /// <param name="__instance">The ProgressTracker instance</param>
        /// <param name="__args">Array: [0] = ProgressLevelModifier modifier, [1] = ref List&lt;Window&gt; windows, [2] = ref ProgressDiffClassLedger classLedger</param>
        [HarmonyPrefix]
        public static bool Prefix(object __instance, object[] __args)
        {
            try
            {
                if (__instance == null || __args == null || __args.Length < 3)
                {
                    return true;
                }

                Pawn pawn = ItsSorceryDetection.PawnField?.GetValue(__instance) as Pawn;
                if (pawn == null || !pawn.IsValidEternal())
                {
                    return true; // vanilla flow (dialog) for everyone else
                }

                object modifier = __args[0];
                object ledger = __args[2];

                if (modifier == null || ledger == null)
                {
                    return true;
                }

                // Bail to vanilla when any reflection handle is missing — never half-apply.
                if (ItsSorceryDetection.OptionsField == null || ItsSorceryDetection.OptionChoicesField == null ||
                    ItsSorceryDetection.AdjustModifiersOptionMethod == null || ItsSorceryDetection.AdjustAbilitiesOptionMethod == null ||
                    ItsSorceryDetection.AdjustHediffsOptionMethod == null || ItsSorceryDetection.PointsField == null ||
                    ItsSorceryDetection.OptionPointGainField == null)
                {
                    return true;
                }

                var options = ItsSorceryDetection.OptionsField.GetValue(modifier) as IList;
                int optionChoices = (int)ItsSorceryDetection.OptionChoicesField.GetValue(modifier);

                // Original handles these cases without a dialog (empty/zero → early return; single option → applied directly).
                if (options == null || options.Count <= 1 || optionChoices == 0)
                {
                    return true;
                }

                int chosenIndex = PickBestOptionIndex(BuildScoreEntries(options));
                if (chosenIndex < 0)
                {
                    chosenIndex = Rand.Range(0, options.Count);
                }

                object chosen = options[chosenIndex];

                var adjustArgs = new object[] { chosen, ledger };
                ItsSorceryDetection.AdjustModifiersOptionMethod.Invoke(__instance, adjustArgs);
                ItsSorceryDetection.AdjustAbilitiesOptionMethod.Invoke(__instance, adjustArgs);
                ItsSorceryDetection.AdjustHediffsOptionMethod.Invoke(__instance, adjustArgs);
                __args[2] = adjustArgs[1]; // propagate any ref reassignment back to the caller

                int points = (int)ItsSorceryDetection.PointsField.GetValue(__instance);
                int pointGain = (int)ItsSorceryDetection.OptionPointGainField.GetValue(chosen);
                ItsSorceryDetection.PointsField.SetValue(__instance, points + pointGain);

                // debug log gated on Eternal_Mod.settings?.debugMode:
                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] ISF: auto-picked perk option {chosenIndex} for {pawn.Name?.ToStringShort ?? "Pawn"}");
                }

                return false; // skip the original — no dialog queued
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "ISF_ApplyOptions_Patch.Prefix", null, ex);
                return true; // graceful degradation: vanilla dialog flow
            }
        }

        /// <summary>
        /// One perk option flattened for scoring: (stat defName, value) pairs from statOffsets
        /// + statFactorOffsets, plus the label. Pure data container for unit testing under Mono.
        /// </summary>
        internal sealed class OptionScoreEntry
        {
            public List<KeyValuePair<string, float>> StatEntries = new List<KeyValuePair<string, float>>();
            public string Label = string.Empty;
        }

        /// <summary>
        /// Priority pick: first tier with any matching option wins; first matching option within the tier.
        /// Tiers: 1) label contains "muscle" (case-insensitive)  2) defName contains "EnergyRecovery"
        /// 3) TribulationChance with negative value  4) CultivationSpeed. Returns -1 if nothing
        /// matches (caller picks random).
        /// </summary>
        internal static int PickBestOptionIndex(IReadOnlyList<OptionScoreEntry> options)
        {
            int bestIndex = -1;
            int bestPriority = 4;
            if (options == null)
            {
                return bestIndex;
            }

            for (int i = 0; i < options.Count; i++)
            {
                int priority = GetOptionPriority(options[i]);
                if (priority < bestPriority)
                {
                    bestIndex = i;
                    bestPriority = priority;
                    if (priority == 0)
                    {
                        break; // The first muscle option wins over every lower tier.
                    }
                }
            }

            return bestIndex;
        }

        private static int GetOptionPriority(OptionScoreEntry option)
        {
            if (option?.Label?.IndexOf("muscle", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 0;
            }

            int priority = 4;
            if (option?.StatEntries != null)
            {
                foreach (var entry in option.StatEntries)
                {
                    if (entry.Key != null && entry.Key.Contains("EnergyRecovery"))
                    {
                        return 1;
                    }
                    if (entry.Key == "TribulationChance" && entry.Value < 0f)
                    {
                        priority = 2;
                    }
                    else if (entry.Key == "CultivationSpeed" && priority > 3)
                    {
                        priority = 3;
                    }
                }
            }

            return priority;
        }

        /// <summary>
        /// Builds score entries from raw option objects by reading reflection fields.
        /// For each option object, reads OptionStatOffsetsField and OptionStatFactorOffsetsField
        /// (each an IList whose elements are RimWorld.StatModifier with stat.defName and value),
        /// and OptionLabelField (string). Null lists are fine — skipped. Returns
        /// List&lt;OptionScoreEntry&gt; aligned by index with the input options.
        /// </summary>
        private static List<OptionScoreEntry> BuildScoreEntries(IList options)
        {
            var entries = new List<OptionScoreEntry>();

            if (options == null)
            {
                return entries;
            }

            foreach (object option in options)
            {
                var entry = new OptionScoreEntry();

                if (option != null)
                {
                    entry.Label = ItsSorceryDetection.OptionLabelField?.GetValue(option) as string ?? string.Empty;
                    AddStatEntries(option, ItsSorceryDetection.OptionStatOffsetsField, entry);
                    AddStatEntries(option, ItsSorceryDetection.OptionStatFactorOffsetsField, entry);
                }

                entries.Add(entry);
            }

            return entries;
        }

        private static void AddStatEntries(object option, FieldInfo field, OptionScoreEntry entry)
        {
            if (!(field?.GetValue(option) is IList modifiers))
            {
                return;
            }

            foreach (object modifier in modifiers)
            {
                if (modifier is StatModifier statModifier && statModifier.stat != null)
                {
                    entry.StatEntries.Add(new KeyValuePair<string, float>(statModifier.stat.defName, statModifier.value));
                }
            }
        }
    }
}
