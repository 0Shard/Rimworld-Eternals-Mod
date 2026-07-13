// Relative Path: Eternal/Source/Eternal/Patches/ItsSorcery/ISF_SchemaUse_Patch.cs
// Creation Date: 13-07-2026
// Last Edit: 13-07-2026
// Author: 0Shard
// Description: Harmony patch on ItsSorceryFramework.CompUseEffect_Schema.DoEffect so an
// Eternal pawn that uses a schema item (Cultivator of the Rim technique manual) has the
// granted schema force-leveled to its cap immediately at learn time. If the progress hediff
// is not yet initialized, MaxOutSchema no-ops safely and the AddExperience postfix maxes the
// schema on its first exp gain instead.

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
	/// Patches ItsSorceryFramework.CompUseEffect_Schema.DoEffect to immediately force-level
	/// Eternal pawns' newly learned sorcery schemas to their class cap.
	///
	/// Mechanism: whenever an Eternal pawn uses a schema item (e.g. technique manual from
	/// Cultivator of the Rim), the postfix extracts the schema definition from the component
	/// properties, finds the pawn's progress tracker for that schema, and force-levels it if
	/// it exists. If the progress tracker is not yet initialized, MaxOutSchema safely no-ops
	/// and the AddExperience postfix will max it on the first exp gain.
	/// </summary>
	[HarmonyPatch]
	public static class ISF_SchemaUse_Patch
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
					Log.Message("[Eternal] Skipping It's Sorcery SchemaUse patch — mod not active");
				}
				return false;
			}

			if (Eternal_Mod.settings?.debugMode == true)
			{
				Log.Message("[Eternal] It's Sorcery detected - enabling schema insta-max on learning");
			}

			return true;
		}

		/// <summary>
		/// Resolve the target method via inline reflection. This method is not cached in
		/// ItsSorceryDetection, so we resolve it here. Returns null to skip patching if
		/// the method cannot be found (API change).
		/// </summary>
		public static MethodBase TargetMethod()
		{
			var compType = AccessTools.TypeByName("ItsSorceryFramework.CompUseEffect_Schema");
			var method = compType != null ? AccessTools.Method(compType, "DoEffect") : null;

			if (method == null)
			{
				Log.Warning("[Eternal] Skipping It's Sorcery SchemaUse patch — CompUseEffect_Schema.DoEffect not found (mod absent or API changed)");
			}

			return method;
		}

		/// <summary>
		/// Postfix: for Eternal pawns, force-level newly learned schemas to their cap.
		/// </summary>
		[HarmonyPostfix]
		public static void Postfix(ThingComp __instance, Pawn usedBy)
		{
			try
			{
				if (usedBy == null || !usedBy.IsValidEternal())
					return;

				object schemaDef = ItsSorceryDetection.SchemaDefField?.GetValue(__instance?.props);
				if (schemaDef == null)
					return;

				object progressTracker = ItsSorceryDetection.FindSchemaProgressTracker(usedBy, schemaDef);
				if (progressTracker == null)
					return;

				ItsSorceryDetection.MaxOutSchema(progressTracker);

				if (Eternal_Mod.settings?.debugMode == true)
				{
					Log.Message($"[Eternal] ISF: maxed newly learned schema for {usedBy.Name?.ToStringShort ?? "Pawn"}");
				}
			}
			catch (Exception ex)
			{
				EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
					"ISF_SchemaUse_Patch.Postfix", null, ex);
			}
		}
	}
}
