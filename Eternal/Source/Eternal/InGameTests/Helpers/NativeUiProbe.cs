// Author: 0Shard
// Description: Isolated DEBUG probe of actual native settings, hediff and patched inspect-tab rendering.
#if DEBUG
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Eternal.InGameTests.Performance;
using Eternal.UI.Management;
using Eternal.UI.Settings;
using Eternal.UI.HediffSettings;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Eternal.InGameTests.Helpers
{
    internal sealed class NativeUiProbe : IDisposable
    {
        private const int RequiredRenderedFrames = 3;
        private const int MaximumFrames = 600;
        private static NativeUiProbe active;
        private readonly Surface[] surfaces = { new Surface("modSettings"), new Surface("hediffView"), new Surface("managementTab"), new Surface("editorInput") };
        private readonly List<string> errors = new List<string>();
        private readonly Stopwatch elapsed = new Stopwatch();
        private TestMapScope maps;
        private Harmony harmony;
        private Pawn pawn;
        private ManagementTabView management;
        private Window ownedWindow;
        private SettingsDrawer settingsDrawer;
        private (SettingsTab Tab, bool Expanded, Vector2 Scroll) priorDrawerNavigation;
        private bool drawerNavigationChanged;
        private object[] priorSelection;
        private MainButtonDef priorMainTab;
        private Type priorInspectTab;
        private WorldRenderMode priorWorldRenderMode;
        private XElement beforeManagementSelection;
        private XElement managementOpenedState;
        private int stage;
        private int startFrame;
        private bool started;
        private bool disposed;
        private bool selectionChanged;
        private bool inspectOpened;
        private XElement result;
        private bool interactionsComplete;
        private readonly List<XElement> inputEvidence = new List<XElement>();

        internal void Start()
        {
            if (started || disposed || active != null)
            {
                throw new InvalidOperationException("A native UI probe is already active or disposed");
            }

            RequireIsolatedSession();
            started = true;
            active = this;
            startFrame = Time.frameCount;
            elapsed.Start();
            try
            {
                priorSelection = Find.Selector.SelectedObjects.ToArray();
                priorMainTab = Find.MainTabsRoot.OpenTab;
                priorInspectTab = ((MainTabWindow_Inspect)MainButtonDefOf.Inspect.TabWindow).OpenTabType;
                priorWorldRenderMode = Find.World.renderer.wantedMode;
                maps = new TestMapScope();
                var map = maps.Create(75, home: true);
                pawn = maps.SpawnEternalPawn(map);
                foreach (var hediff in pawn.health.hediffSet.hediffs.Where(h => h.def.isBad).ToArray())
                {
                    pawn.health.RemoveHediff(hediff);
                }

                TestAssert.IsFalse(pawn.Dead || pawn.Downed, "Native UI actor must be healthy and alive");
                TestAssert.IsFalse(pawn.health.hediffSet.hediffs.Any(h => h.def.isBad), "Native UI actor must have no harmful conditions");
                selectionChanged = true;
                Current.Game.CurrentMap = map;
                harmony = new Harmony("Eternal.Debug.NativeUiProbe." + Guid.NewGuid().ToString("N"));
                harmony.Patch(AccessTools.Method(typeof(ManagementTabView), "FillTab"),
                    prefix: new HarmonyMethod(typeof(NativeUiProbe), nameof(BeforeManagementDraw)),
                    postfix: new HarmonyMethod(typeof(NativeUiProbe), nameof(AfterManagementDraw)),
                    finalizer: new HarmonyMethod(typeof(NativeUiProbe), nameof(ManagementDrawFailure)));
                ownedWindow = new SettingsWindow(this, LoadedModManager.GetMod<Eternal_Mod>());
                Find.WindowStack.Add(ownedWindow);
            }
            catch (Exception exception) { errors.Add(exception.ToString()); Complete(); }
        }

        // Call from real GameComponentUpdate frames, never from a synchronous busy loop.
        // A non-null result means all owned fixtures have already been cleaned up.
        internal XElement Poll()
        {
            if (!started)
            {
                throw new InvalidOperationException("Start the native UI probe before polling");
            }

            if (result != null)
            {
                return result;
            }

            if (disposed)
            {
                throw new ObjectDisposedException(nameof(NativeUiProbe));
            }

            try
            {
                if (errors.Count != 0 || surfaces.Any(surface => surface.Errors.Count != 0))
                {
                    Complete();
                }
                else if (elapsed.Elapsed.TotalSeconds >= 30 || Time.frameCount - startFrame >= MaximumFrames)
                {
                    errors.Add("Native GUI deadline reached at surface " + surfaces[stage].Name);
                    Complete();
                }
                else if (surfaces[stage].RenderedFrames >= RequiredRenderedFrames && (stage != 3 || interactionsComplete))
                {
                    if (ownedWindow != null) { Find.WindowStack.TryRemove(ownedWindow, false); ownedWindow = null; }
                    stage++;
                    if (stage == surfaces.Length)
                    {
                        Complete();
                    }
                    else
                    {
                        OpenStage();
                    }
                }
            }
            catch (Exception exception) { errors.Add(exception.ToString()); Complete(); }
            return result;
        }

        private void OpenStage()
        {
            if (stage == 3)
            {
                ownedWindow = new InputWindow(this);
                Find.WindowStack.Add(ownedWindow);
            }
            else if (stage == 1)
            {
                settingsDrawer = (SettingsDrawer)AccessTools.Field(typeof(Eternal_Settings), "drawer")
                    .GetValue(Eternal_Mod.GetSettings());
                TestAssert.IsNotNull(settingsDrawer, "Native settings draw must create the retained drawer");
                priorDrawerNavigation = settingsDrawer.CaptureNavigation();
                drawerNavigationChanged = true;
                settingsDrawer.SelectTab(SettingsTab.Advanced);
                settingsDrawer.SetHediffExpanded(true);
                settingsDrawer.ScrollToHediffSettings();
                ownedWindow = new SettingsWindow(this, LoadedModManager.GetMod<Eternal_Mod>());
                Find.WindowStack.Add(ownedWindow);
            }
            else
            {
                selectionChanged = true;
                beforeManagementSelection = CaptureInspectorState("beforeNativeSelection");
                inspectOpened = WorldRendererUtility.WorldSelected || Find.MainTabsRoot.OpenTab != MainButtonDefOf.Inspect;
                // Native map removal leaves wantedMode=Planet, and merely assigning
                // CurrentMap does not hide it. Use the same map-selection path as the
                // player so the engine does not auto-close Inspect on the next GUI event.
                CameraJumper.TrySelect(new GlobalTargetInfo(pawn), skipTargetAdjustment: true);
                TestAssert.IsTrue(ReferenceEquals(Find.Selector.SingleSelectedThing, pawn)
                    && ReferenceEquals(Find.CurrentMap, pawn.Map) && !WorldRendererUtility.WorldSelected,
                    "Native camera selection must expose the owned pawn's map");
                management = pawn.GetInspectTabs().OfType<ManagementTabView>().SingleOrDefault();
                TestAssert.IsNotNull(management, "Actual Thing.GetInspectTabs patch must attach the Eternal tab");
                TestAssert.IsTrue(management.IsVisible, "Selected healthy Eternal must expose its actual management tab");
                Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Inspect, false);
                TestAssert.IsTrue(ReferenceEquals(management, InspectPaneUtility.OpenTab(typeof(ManagementTabView))),
                    "Native inspector must open the actual patched tab");
                managementOpenedState = CaptureInspectorState("opened");
                var pane = (MainTabWindow_Inspect)MainButtonDefOf.Inspect.TabWindow;
                TestAssert.IsTrue(pane.IsOpen && pane.OpenTabType == typeof(ManagementTabView)
                    && pane.CurTabs?.Contains(management) == true,
                    "Native WindowStack must retain the opened management inspector");
            }
        }

        private void Draw(int index, Action draw)
        {
            var surface = surfaces[index];
            surface.Begin();
            try
            {
                draw();
                if (index != 1 || settingsDrawer.HediffSectionRendered)
                {
                    surface.Success();
                }
                else
                {
                    settingsDrawer.ScrollToHediffSettings();
                }
            }
            catch (Exception exception) { surface.Errors.Add(exception.ToString()); throw; }
        }

        private static void BeforeManagementDraw(ManagementTabView __instance, out Surface __state)
        {
            __state = active != null && active.stage == 2 && ReferenceEquals(__instance, active.management)
                && ReferenceEquals(Find.Selector.SingleSelectedThing, active.pawn) ? active.surfaces[2] : null;
            __state?.Begin();
        }
        private static void AfterManagementDraw(Surface __state) { __state?.Success(); }
        private static Exception ManagementDrawFailure(Exception __exception, Surface __state)
        {
            if (__exception != null)
            {
                __state?.Errors.Add(__exception.ToString());
            }

            return __exception;
        }

        private void Complete()
        {
            XElement finalInspectorState = null;
            try { finalInspectorState = CaptureInspectorState("beforeCleanup"); }
            catch (Exception exception) { errors.Add("Native inspector diagnostics failed: " + exception); }
            Dispose();
            result = new XElement("nativeUi", new XAttribute("success", errors.Count == 0
                    && surfaces.All(surface => surface.Errors.Count == 0 && surface.RenderedFrames >= RequiredRenderedFrames)),
                new XAttribute("elapsedMs", elapsed.ElapsedMilliseconds),
                new XAttribute("requiredRenderedFrames", RequiredRenderedFrames),
                surfaces.Select(surface => new XElement("surface", new XAttribute("name", surface.Name),
                    new XAttribute("callbacks", surface.Callbacks), new XAttribute("renderedFrames", surface.RenderedFrames),
                    new XAttribute("success", surface.Errors.Count == 0 && surface.RenderedFrames >= RequiredRenderedFrames),
                    surface.Errors.Select(error => new XElement("error", error)))),
                errors.Select(error => new XElement("error", error)),
                new XElement("inputInteractions", new XAttribute("completed", interactionsComplete), inputEvidence),
                new XElement("inspectorDiagnostics", beforeManagementSelection, managementOpenedState, finalInspectorState));
        }

        private XElement CaptureInspectorState(string when)
        {
            var pane = (MainTabWindow_Inspect)MainButtonDefOf.Inspect.TabWindow;
            return new XElement("state", new XAttribute("when", when), new XAttribute("frame", Time.frameCount),
                new XAttribute("worldSelected", WorldRendererUtility.WorldSelected),
                new XAttribute("wantedWorldMode", Find.World.renderer.wantedMode),
                new XAttribute("currentMapId", Find.CurrentMap?.uniqueID ?? -1),
                new XAttribute("pawnMapId", pawn?.Map?.uniqueID ?? -1),
                new XAttribute("pawnId", pawn?.thingIDNumber ?? -1),
                new XAttribute("selectedThingId", Find.Selector.SingleSelectedThing?.thingIDNumber ?? -1),
                new XAttribute("selectionCount", Find.Selector.NumSelected),
                new XAttribute("mainTab", Find.MainTabsRoot.OpenTab?.defName ?? "none"),
                new XAttribute("inspectWindowOpen", pane.IsOpen),
                new XAttribute("inspectTab", pane.OpenTabType?.FullName ?? "none"),
                new XAttribute("managementVisible", management?.IsVisible ?? false),
                new XAttribute("managementInCurrentTabs", management != null && pane.CurTabs?.Contains(management) == true),
                new XAttribute("screenshotMode", Find.ScreenshotModeHandler.Active),
                new XElement("windows", Find.WindowStack.Windows.Select(window => new XElement("window", window.GetType().FullName))));
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Action<Action> attempt = action => { try { action(); } catch (Exception exception) { errors.Add(exception.ToString()); } };
            if (ownedWindow != null)
            {
                attempt(() => Find.WindowStack.TryRemove(ownedWindow, false));
            }
            if (drawerNavigationChanged)
            {
                attempt(() => settingsDrawer.RestoreNavigation(priorDrawerNavigation));
            }

            attempt(() => harmony?.UnpatchAll(harmony.Id));
            if (selectionChanged)
            {
                if (management != null)
                {
                    attempt(() => ((MainTabWindow_Inspect)MainButtonDefOf.Inspect.TabWindow).CloseOpenTab());
                }

                if (inspectOpened && Find.MainTabsRoot.OpenTab == MainButtonDefOf.Inspect)
                {
                    attempt(() => Find.WindowStack.TryRemove(MainButtonDefOf.Inspect.TabWindow, false));
                }

                attempt(() => Find.Selector.ClearSelection());
            }
            attempt(() => maps?.Dispose());
            if (selectionChanged && priorSelection != null)
            {
                foreach (var selected in priorSelection)
                {
                    if (!(selected is Thing thing) || (!thing.Destroyed && thing.Spawned && thing.Map == Current.Game.CurrentMap))
                    {
                        attempt(() => Find.Selector.Select(selected, false, false));
                    }
                }

                attempt(() => Find.MainTabsRoot.SetCurrentTab(priorMainTab, false));
                attempt(() => ((MainTabWindow_Inspect)MainButtonDefOf.Inspect.TabWindow).OpenTabType = priorInspectTab);
            }
            if (started && priorSelection != null)
            {
                attempt(() => Find.World.renderer.wantedMode = priorWorldRenderMode);
            }
            elapsed.Stop();
            if (ReferenceEquals(active, this))
            {
                active = null;
            }
        }

        private static void RequireIsolatedSession()
        {
            string session, data, mode;
            if (!GenCommandLine.TryGetCommandLineArg("eternal-test-session", out session)
                || !GenCommandLine.TryGetCommandLineArg("savedatafolder", out data)
                || !GenCommandLine.TryGetCommandLineArg("eternal-test-mode", out mode) || mode != "smoke")
            {
                throw new InvalidOperationException("Native UI probe requires the isolated smoke host");
            }

            session = Path.GetFullPath(session).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            data = Path.GetFullPath(data).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (session.Length < 5 || !Directory.Exists(session)
                || !string.Equals(data, Path.Combine(session, "savedata"), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(data, Path.GetFullPath(GenFilePaths.SaveDataFolderPath).TrimEnd(Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                || Current.ProgramState != ProgramState.Playing || Current.Game?.PlayerHasControl != true)
            {
                throw new InvalidOperationException("Native UI probe requires its own active session/data folder");
            }
        }

        private sealed class Surface
        {
            internal readonly string Name;
            internal readonly List<string> Errors = new List<string>();
            internal int Callbacks;
            internal int RenderedFrames;
            private int lastFrame = -1;
            private bool repaint;
            internal Surface(string name) { Name = name; }
            internal void Begin()
            {
                repaint = Event.current != null && Event.current.type == EventType.Repaint;
                if (Event.current == null)
                {
                    Errors.Add("Draw callback has no native GUI event");
                }
                else
                {
                    Callbacks++;
                }
            }
            internal void Success()
            {
                if (repaint && lastFrame != Time.frameCount) { lastFrame = Time.frameCount; RenderedFrames++; }
            }
        }

        private sealed class SettingsWindow : Dialog_ModSettings
        {
            private readonly NativeUiProbe owner;
            internal SettingsWindow(NativeUiProbe owner, Mod mod) : base(mod) { this.owner = owner; }
            // Closing a smoke fixture must not invoke Dialog_ModSettings' persistence side effect.
            public override void PreClose() { }
            public override void DoWindowContents(Rect rect) { owner.Draw(owner.stage, () => base.DoWindowContents(rect)); }
        }

        // Synthetic input goes through real Unity IMGUI callbacks and the production
        // controls. A repaint-only host cannot satisfy this separate surface.
        private sealed class InputWindow : Window
        {
            private readonly NativeUiProbe owner;
            private readonly SettingsDrawer drawer;
            private readonly EternalHediffView view;
            private readonly EternalHediffManager manager;
            private readonly string pricingDef;
            private IReadOnlyList<KeyValuePair<string, EternalHediffSetting>> pricingRows;
            private int phase;
            private int lastFrame = -1;
            private float rate = 1.2f;
            private int disabledValue = 40;

            internal InputWindow(NativeUiProbe owner)
            {
                this.owner = owner;
                var priorInstance = Eternal_Settings.instance;
                var settings = new Eternal_Settings();
                Eternal_Settings.instance = priorInstance;
                drawer = new SettingsDrawer(settings);
                manager = settings.hediffManager;
                pricingDef = manager.GetFilteredHediffsCached().First().Key;
                view = new EternalHediffView(manager);
                doCloseX = false;
                closeOnClickedOutside = false;
            }

            public override Vector2 InitialSize => new Vector2(650f, 280f);

            public override void DoWindowContents(Rect rect) => owner.Draw(3, () => DrawInteractions(rect));

            private void DrawInteractions(Rect rect)
            {
                if (Event.current.type != EventType.Repaint || lastFrame == Time.frameCount || owner.interactionsComplete) { return; }
                lastFrame = Time.frameCount;
                Event originalEvent = Event.current;
                bool originalEnabled = GUI.enabled;
                try
                {
                    if (phase < 3) { DrawDisabled(rect); }
                    else if (phase < 27) { DrawRate(rect, phase >= 15, (phase - 3) % 12); }
                    else { DrawPricing(rect, phase >= 34, (phase - 27) % 7); }
                    owner.inputEvidence.Add(new XElement("input", new XAttribute("phase", phase),
                        new XAttribute("frame", Time.frameCount), new XAttribute("disabledValue", disabledValue),
                        new XAttribute("rate", rate), new XAttribute("focused", GUI.GetNameOfFocusedControl() ?? "")));
                    phase++;
                    owner.interactionsComplete = phase == 41;
                }
                finally { Event.current = originalEvent; GUI.enabled = originalEnabled; }
            }

            private void DrawDisabled(Rect rect)
            {
                GUI.enabled = false;
                Event.current = new Event
                {
                    type = phase == 0 ? EventType.MouseDown : phase == 1 ? EventType.MouseDrag : EventType.KeyDown,
                    mousePosition = new Vector2(rect.x + 300f, rect.y + 10f),
                    button = 0,
                    keyCode = phase == 2 ? KeyCode.Return : KeyCode.None
                };
                if (phase == 2)
                {
                    AccessTools.Field(typeof(SettingsDrawer), "activeEditControlName").SetValue(drawer, "SliderInt_probe");
                    AccessTools.Field(typeof(SettingsDrawer), "editBuffer").SetValue(drawer, "99");
                }
                var listing = new Listing_Standard();
                listing.Begin(new Rect(rect.x, rect.y, 600f, 100f));
                object[] arguments = { listing, "probe", disabledValue, 0, 100, "disabled input probe" };
                try { AccessTools.Method(typeof(SettingsDrawer), "SliderWithInlineEditInt").Invoke(drawer, arguments); }
                finally { listing.End(); }
                disabledValue = (int)arguments[2];
                TestAssert.AreEqual(40, disabledValue, "Actual disabled rail and pending editor cannot assign");
                if (phase == 2)
                {
                    TestAssert.IsNull(AccessTools.Field(typeof(SettingsDrawer), "activeEditControlName").GetValue(drawer),
                        "Disabling the feature cancels its pending editor");
                }
            }

            private void DrawRate(Rect rect, bool bulk, int step)
            {
                string key = bulk ? "EternalRate_native_bulk" : "EternalRate_native_def";
                if (step == 0) { rate = 1.2f; view.ReleaseEditorFocus(); }
                Event injected = new Event { type = EventType.Repaint };
                if (step == 1) { injected = new Event { type = EventType.KeyDown, keyCode = KeyCode.A, modifiers = EventModifiers.Control }; }
                if (step == 2) { injected = new Event { type = EventType.KeyDown, keyCode = KeyCode.Backspace }; }
                if (step == 4 || step == 5 || step == 7)
                {
                    injected = new Event { type = EventType.KeyDown, character = step == 4 ? '0' : step == 5 ? '.' : '2' };
                }
                if (step == 8)
                {
                    injected = bulk ? new Event { type = EventType.MouseDown, mousePosition = new Vector2(rect.x, rect.y), button = 0 }
                        : new Event { type = EventType.KeyDown, keyCode = KeyCode.Return };
                }
                if (step == 9) { rate = 1.2f; }
                if (step == 10)
                {
                    view.ReleaseEditorFocus();
                    key += "_filtered_successor";
                }
                Event.current = injected;
                var method = AccessTools.Method(typeof(EternalHediffView), bulk ? "DrawSliderWithNumericInput" : "DrawCompactSliderWithInput");
                rate = (float)method.Invoke(view, new object[] { new Rect(rect.x, rect.y + 120f, 550f, 30f), key, rate, 0.01f, 3f, "native rate", "input probe" });
                if (step == 0) { GUI.FocusControl(key); }
                var buffer = (RateEditBuffer)AccessTools.Method(typeof(EternalHediffView), "RateInput").Invoke(view, new object[] { key });
                if (step == 2 || step == 3)
                {
                    TestAssert.AreEqual("", buffer.Text, "Actual clear and following native frame preserve empty input");
                    TestAssert.AreEqual(1.2f, rate, "Partial editing cannot commit");
                }
                if (step == 6) { TestAssert.AreEqual("0.", buffer.Text, "Actual type/pause retains partial decimal"); }
                if (step == 8) { TestAssert.IsTrue(Math.Abs(rate - 0.2f) < 0.00001f, "Native Enter/blur commits the intended parsed rate"); }
                if (step >= 9) { TestAssert.AreEqual(1.2f, rate, "Reset/filter successor cannot inherit prior input"); }
            }

            private void DrawPricing(Rect rect, bool range, int step)
            {
                string key = pricingDef + (range ? "-range" : "-nutrition");
                if (step == 0)
                {
                    view.ReleaseEditorFocus();
                    manager.SetSearchFilter(pricingDef);
                    pricingRows = manager.GetFilteredHediffsCached();
                    TestAssert.AreEqual(pricingDef, pricingRows[0].Key, "Focused pricing row is the first drawn native row");
                }
                if (step == 6) { manager.SetSearchFilter("Eternal_Debug_NoMatchingPricingRow"); }
                Event.current = step == 1
                    ? new Event { type = EventType.KeyDown, keyCode = KeyCode.A, modifiers = EventModifiers.Control }
                    : step == 2 || step == 4
                        ? new Event { type = EventType.KeyDown, character = step == 2 ? '2' : '5' }
                        : new Event { type = EventType.Repaint };
                if (step == 3 || step == 5)
                {
                    var rebuilt = manager.GetFilteredHediffsCached();
                    TestAssert.IsFalse(ReferenceEquals(pricingRows, rebuilt), "Pricing mutation actually dirtied and rebuilt the production filter cache");
                    TestAssert.IsTrue(pricingRows.Select(row => row.Key).SequenceEqual(rebuilt.Select(row => row.Key)),
                        "Value-only rebuild preserves ordered def names");
                    pricingRows = rebuilt;
                }
                AccessTools.Method(typeof(EternalHediffView), "DrawHediffList").Invoke(view,
                    new object[] { new Rect(rect.x, rect.y, 550f, pricingRows.Count * 130f), rect });
                if (step == 0) { GUI.FocusControl(key); }
                if (step == 3 || step == 5)
                {
                    var buffers = (Dictionary<string, string>)AccessTools.Field(typeof(EternalHediffView), "nutritionInputs").GetValue(view);
                    TestAssert.AreEqual(key, GUI.GetNameOfFocusedControl(), "Value-only redraw retains the actual native pricing control focus");
                    TestAssert.AreEqual(step == 3 ? "2" : "25", buffers[key], "Separate valid keystrokes retain the pricing buffer across dirty-cache redraw");
                    var setting = manager.Store.GetExisting(pricingDef);
                    TestAssert.AreEqual(step == 3 ? 2f : 25f, range ? setting.nutritionSeverityRangeOverride : setting.nutritionCostMultiplier,
                        "Production pricing assignment accepts both separate keystrokes");
                }
                if (step == 6)
                {
                    TestAssert.IsTrue(string.IsNullOrEmpty(GUI.GetNameOfFocusedControl()) && GUIUtility.keyboardControl == 0,
                        "Removing the focused row releases its native numeric control identity");
                }
            }
        }
    }
}
#endif
