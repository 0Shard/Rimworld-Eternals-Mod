// Author: 0Shard
// Description: Observe native pause causes without overriding engine gates or dismissing errors.
#if DEBUG
using System.Linq;
using System.Xml.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Eternal.InGameTests.Performance
{
    internal static class NativePauseDiagnostics
    {
        internal static void RequireRunnable(string stage, int frame)
        {
            if (Find.TickManager.Paused)
            {
                throw new TestFailedException("Native pipeline unexpectedly paused before Eternal callback: stage=" + stage
                    + ", frame=" + frame + ", " + Capture().ToString(SaveOptions.DisableFormatting));
            }
        }

        internal static XElement Capture()
        {
            var manager = Find.TickManager;
            var windows = Find.WindowStack?.Windows;
            var controller = Find.GravshipController;
            var ender = Find.GameEnder;
            return new XElement("nativePause", new XAttribute("T", manager?.TicksGame ?? -1),
                new XAttribute("speed", manager?.CurTimeSpeed.ToString() ?? "unavailable"),
                new XAttribute("paused", manager?.Paused.ToString() ?? "unavailable"),
                new XAttribute("forcePaused", manager?.ForcePaused.ToString() ?? "unavailable"),
                new XAttribute("playerHasControl", Current.Game?.PlayerHasControl.ToString() ?? "unavailable"),
                new XAttribute("screenFading", ScreenFader.IsFading()),
                new XAttribute("windowsForcePause", Find.WindowStack?.WindowsForcePause.ToString() ?? "unavailable"),
                new XAttribute("longEventForcePause", LongEventHandler.ForcePause),
                new XAttribute("tilePickerActive", Find.TilePicker.Active),
                new XAttribute("gravshipCutscene", WorldComponent_GravshipController.CutsceneInProgress),
                new XAttribute("landingConfirmation", controller?.LandingAreaConfirmationInProgress.ToString() ?? "unavailable"),
                new XAttribute("mapGeneratorDebugMode", MapGenerator.debugMode),
                new XAttribute("automaticPauseMode", Prefs.AutomaticPauseMode),
                new XAttribute("gameEnding", ender?.gameEnding.ToString() ?? "unavailable"),
                new XAttribute("gameOverCountdown", ender == null ? "unavailable" :
                    AccessTools.Field(typeof(GameEnder), "ticksToGameOver")?.GetValue(ender)?.ToString() ?? "unavailable"),
                new XElement("windows", windows?.Select(window => new XElement("window",
                    new XAttribute("type", window.GetType().FullName), new XAttribute("forcePause", window.forcePause)))),
                new XElement("letters", Find.LetterStack?.LettersListForReading.Select(letter => new XElement("letter",
                    new XAttribute("type", letter.GetType().FullName), new XAttribute("def", letter.def?.defName ?? "null"),
                    new XAttribute("arrivalT", letter.arrivalTick), new XAttribute("pauseMode", letter.def?.pauseMode.ToString() ?? "unavailable")))));
        }
    }
}
#endif
