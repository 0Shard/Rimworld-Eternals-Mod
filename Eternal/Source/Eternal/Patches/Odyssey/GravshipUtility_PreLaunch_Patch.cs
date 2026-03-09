// file path: Eternal/Source/Eternal/Patches/Odyssey/GravshipUtility_PreLaunch_Patch.cs
// Author Name: 0Shard
// Date Created: 06-12-2025
// Date Last Modified: 04-03-2026
// Description: Adds a warning to the grav ship launch confirmation dialog
//              about Eternal pawns that will be left behind and fall to the planet.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Eternal.Exceptions;
using Eternal.Extensions;
using Eternal.Utils;

namespace Eternal.Patches.Odyssey
{
    /// <summary>
    /// Harmony patch to add warning about Eternal pawns in the launch confirmation dialog.
    /// Uses reflection-based conditional patching to avoid TypeLoadException when the
    /// Odyssey DLC is absent. The patch is skipped entirely if Odyssey is not active.
    /// </summary>
    [HarmonyPatch]
    public static class GravshipUtility_PreLaunchConfirmation_Patch
    {
        /// <summary>
        /// Guard: only apply this patch when the Odyssey DLC is active.
        /// Harmony calls Prepare() before applying the patch. Returning false skips it.
        /// </summary>
        public static bool Prepare() => ModsConfig.OdysseyActive;

        /// <summary>
        /// Resolve the target method via reflection so we never reference GravshipUtility
        /// at compile time. This prevents TypeLoadException when Odyssey is absent.
        /// Returns null to skip patching if the type or method cannot be found.
        /// </summary>
        public static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("RimWorld.GravshipUtility");
            if (type == null)
            {
                Log.Warning("[Eternal] Skipping GravshipUtility.PreLaunchConfirmation patch — type not found (Odyssey absent or API changed)");
                return null;
            }

            var method = AccessTools.Method(type, "PreLaunchConfirmation");
            if (method == null)
            {
                Log.Warning("[Eternal] Skipping GravshipUtility.PreLaunchConfirmation patch — method not found (Odyssey API changed)");
            }

            return method;
        }

        /// <summary>
        /// Postfix to add Eternal warning after the normal confirmation setup.
        /// We use reflection to modify the dialog that's about to be shown.
        /// </summary>
        [HarmonyPostfix]
        public static void AddEternalWarning(Building_GravEngine engine)
        {
            // Only run if Odyssey DLC is active
            if (!ModsConfig.OdysseyActive)
            {
                return;
            }

            try
            {
                // Find Eternals that will be left behind
                var eternalsLeftBehind = engine.Map.mapPawns.AllPawns
                    .Where(p => p.IsValidEternal() &&
                               p.Faction == Faction.OfPlayer &&
                               !engine.ValidSubstructureAt(p.PositionHeld))
                    .ToList();

                if (eternalsLeftBehind.Count == 0)
                {
                    return;
                }

                // Find the dialog that was just added to the window stack
                var dialog = Find.WindowStack.Windows
                    .OfType<Dialog_MessageBox>()
                    .LastOrDefault();

                if (dialog == null)
                {
                    Log.Warning("[Eternal] Could not find launch confirmation dialog to add warning");
                    return;
                }

                // Build warning text
                TaggedString warning = "\n\n" +
                    ("EternalWarning".Translate() + ": ").Colorize(ColorLibrary.Gold) +
                    "EternalWillFallToPlanet".Translate().Resolve() + ":\n" +
                    eternalsLeftBehind
                        .Select(p => "  - " + p.NameFullColored.Resolve())
                        .ToLineList("", false);

                // Access the text field via reflection and append our warning
                var textField = typeof(Dialog_MessageBox).GetField("text", BindingFlags.NonPublic | BindingFlags.Instance);
                if (textField != null)
                {
                    string currentText = textField.GetValue(dialog) as string ?? "";
                    textField.SetValue(dialog, currentText + warning);
                }
                else
                {
                    Log.Warning("[Eternal] Could not access Dialog_MessageBox.text field");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "AddEternalWarning", null, ex);
            }
        }
    }
}
