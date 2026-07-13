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
		public static bool Prepare()
		{
			bool isActive = ItsSorceryDetection.ItsSorceryActive;

			if (!isActive)
			{
				// Only log at debug level — absence of the mod is the normal case
				if (Eternal_Mod.settings?.debugMode == true)
				{
					Log.Message("[Eternal] Skipping It's Sorcery ApplyOptions patch — mod not active");
				}
				return false;
			}

			if (Eternal_Mod.settings?.debugMode == true)
			{
				Log.Message("[Eternal] It's Sorcery detected - enabling perk auto-pick for Eternals");
			}

			return true;
		}

		/// <summary>
		/// Resolve the target method via reflection.
		/// Returns null to skip patching if the method cannot be found (API change).
		/// </summary>
		public static MethodBase TargetMethod()
		{
			var method = ItsSorceryDetection.ApplyOptionsMethod;

			if (method == null)
			{
				Log.Warning("[Eternal] Skipping It's Sorcery ApplyOptions patch — method not found (mod absent or API changed)");
			}

			return method;
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
					return true;

				Pawn pawn = ItsSorceryDetection.PawnField?.GetValue(__instance) as Pawn;
				if (pawn == null || !pawn.IsValidEternal())
					return true; // vanilla flow (dialog) for everyone else

				object modifier = __args[0];
				object ledger = __args[2];

				if (modifier == null || ledger == null)
					return true;

				// Bail to vanilla when any reflection handle is missing — never half-apply.
				if (ItsSorceryDetection.OptionsField == null || ItsSorceryDetection.OptionChoicesField == null ||
					ItsSorceryDetection.AdjustModifiersOptionMethod == null || ItsSorceryDetection.AdjustAbilitiesOptionMethod == null ||
					ItsSorceryDetection.AdjustHediffsOptionMethod == null || ItsSorceryDetection.PointsField == null ||
					ItsSorceryDetection.OptionPointGainField == null)
					return true;

				var options = ItsSorceryDetection.OptionsField.GetValue(modifier) as IList;
				int optionChoices = (int)ItsSorceryDetection.OptionChoicesField.GetValue(modifier);

				// Original handles these cases without a dialog (empty/zero → early return; single option → applied directly).
				if (options == null || options.Count <= 1 || optionChoices == 0)
					return true;

				int chosenIndex = PickBestOptionIndex(BuildScoreEntries(options));
				if (chosenIndex < 0)
					chosenIndex = Rand.Range(0, options.Count);

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
			if (options == null || options.Count == 0)
				return -1;

			// Priority tiers: each tier is a predicate function
			var tiers = new Func<OptionScoreEntry, bool>[]
			{
				// Tier 1: Label != null && Label.IndexOf("muscle", StringComparison.OrdinalIgnoreCase) >= 0
				opt => opt.Label != null && opt.Label.IndexOf("muscle", StringComparison.OrdinalIgnoreCase) >= 0,

				// Tier 2: any StatEntries with Key.Contains("EnergyRecovery")
				opt => HasStatEntryContaining(opt, "EnergyRecovery"),

				// Tier 3: any StatEntries with Key == "TribulationChance" && Value < 0f
				opt => HasNegativeStatEntry(opt, "TribulationChance"),

				// Tier 4: any StatEntries with Key == "CultivationSpeed"
				opt => HasStatEntry(opt, "CultivationSpeed")
			};

			// Double loop: tier outer, option inner
			foreach (var tierPredicate in tiers)
			{
				for (int i = 0; i < options.Count; i++)
				{
					if (tierPredicate(options[i]))
						return i;
				}
			}

			return -1; // no match — caller will pick random
		}

		/// <summary>
		/// Check if an option has a stat entry matching the exact defName.
		/// </summary>
		private static bool HasStatEntry(OptionScoreEntry option, string statDefName)
		{
			if (option?.StatEntries == null || statDefName == null)
				return false;

			foreach (var entry in option.StatEntries)
			{
				if (entry.Key == statDefName)
					return true;
			}
			return false;
		}

		/// <summary>
		/// Check if an option has a stat entry matching the exact defName with negative value.
		/// </summary>
		private static bool HasNegativeStatEntry(OptionScoreEntry option, string statDefName)
		{
			if (option?.StatEntries == null || statDefName == null)
				return false;

			foreach (var entry in option.StatEntries)
			{
				if (entry.Key == statDefName && entry.Value < 0f)
					return true;
			}
			return false;
		}

		/// <summary>
		/// Check if an option has a stat entry whose defName contains the substring.
		/// </summary>
		private static bool HasStatEntryContaining(OptionScoreEntry option, string substring)
		{
			if (option?.StatEntries == null || substring == null)
				return false;

			foreach (var entry in option.StatEntries)
			{
				if (entry.Key != null && entry.Key.Contains(substring))
					return true;
			}
			return false;
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
				return entries;

			foreach (object option in options)
			{
				var entry = new OptionScoreEntry();

				// Read label
				if (ItsSorceryDetection.OptionLabelField != null)
				{
					entry.Label = ItsSorceryDetection.OptionLabelField.GetValue(option) as string ?? string.Empty;
				}

				// Read statOffsets (List of StatModifier)
				if (ItsSorceryDetection.OptionStatOffsetsField != null)
				{
					var offsets = ItsSorceryDetection.OptionStatOffsetsField.GetValue(option) as IList;
					if (offsets != null)
					{
						foreach (object sm in offsets)
						{
							var statModifier = sm as StatModifier;
							if (statModifier?.stat != null)
							{
								entry.StatEntries.Add(new KeyValuePair<string, float>(statModifier.stat.defName, statModifier.value));
							}
						}
					}
				}

				// Read statFactorOffsets (List of StatModifier)
				if (ItsSorceryDetection.OptionStatFactorOffsetsField != null)
				{
					var factors = ItsSorceryDetection.OptionStatFactorOffsetsField.GetValue(option) as IList;
					if (factors != null)
					{
						foreach (object sm in factors)
						{
							var statModifier = sm as StatModifier;
							if (statModifier?.stat != null)
							{
								entry.StatEntries.Add(new KeyValuePair<string, float>(statModifier.stat.defName, statModifier.value));
							}
						}
					}
				}

				entries.Add(entry);
			}

			return entries;
		}
	}
}
