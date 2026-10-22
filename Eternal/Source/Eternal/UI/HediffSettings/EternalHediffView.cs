// Relative Path: Eternal/Source/Eternal/UI/HediffSettings/EternalHediffView.cs
// Creation Date: 09-11-2025
// Last Edit: 16-07-2026
// Author: 0Shard
// Description: View layer for hediff settings UI. Pure UI rendering, delegates to Presenter.
//              12-07: Advanced/Bulk tab visual cleanup — capped CheckboxLabeled widths (toggle
//              beside label, not at the far window edge), one content-fitted background
//              treatment for both tabs, 30f slider rows so HorizontalSlider labels fit.
//              PERF: List is virtualized — only rows inside the viewport (plus a small preload
//              buffer) are drawn each frame; off-screen row Y offsets are pure arithmetic.
//              SIMPLIFIED: Single compact row per hediff with only 3 options:
//              - Heal toggle
//              - Healing rate slider + input
//              - Threshold-aware healing controls
//              Per-hediff and global reset buttons.
//              PERF-02: Render loop uses GetFilteredHediffsCached() instead of per-frame scan.

using Eternal.Compatibility;
using Eternal.Healing;
using Eternal.UI.Settings;
using System.Globalization;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimWorld;

namespace Eternal.UI.HediffSettings
{
    /// <summary>
    /// View layer for hediff settings UI.
    /// Handles pure UI rendering, delegates all state and events to Presenter.
    /// </summary>
    public class EternalHediffView
    {
        private readonly EternalHediffPresenter presenter;
        private (float Width, HediffCategory Category, string Source, int Selected) filterLayoutKey;
        private Rect[] filterControls;
        private float measuredFilterHeight;
        private readonly Dictionary<string, RateEditBuffer> rateInputs = new Dictionary<string, RateEditBuffer>();
        private IReadOnlyList<KeyValuePair<string, EternalHediffSetting>> rateRows;

        private RateEditBuffer RateInput(string key)
        {
            if (!rateInputs.TryGetValue(key, out var state))
            {
                state = new RateEditBuffer();
                rateInputs[key] = state;
            }
            return state;
        }

        // Layout constants (RimWorld 1.6 modern design - SIMPLIFIED)
        private const float TAB_HEIGHT = 30f;
        private const float ENTRY_HEIGHT = 128f;
        private const float CHECKBOX_ROW_WIDTH = 280f;

        public EternalHediffView(EternalHediffManager manager)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            var model = new EternalHediffModel(manager);
            presenter = new EternalHediffPresenter(model);
        }

        private const string WarningText = "⚠ Diseases and conditions require opt-in. Beneficial hediffs (★) won't heal by default.";
        private const string TitleText = "Eternal Hediff Healing Manager";

        private float TitleHeight(float width)
        {
            Text.Font = GameFont.Medium;
            float height = Math.Max(30f, Text.CalcHeight(TitleText, Math.Max(1f, width - 130f)));
            Text.Font = GameFont.Small;
            return height;
        }

        private string StatisticsText()
        {
            var stats = presenter.GetStatistics();
            return $"Healing {stats.HealingHediffs} of {stats.TotalHediffs} hediffs";
        }

        private float HeaderHeight(float width)
        {
            Text.Font = GameFont.Small;
            return TitleHeight(width) + Math.Max(25f, Text.CalcHeight(StatisticsText(), Math.Max(1f, width - 20f)))
                + Math.Max(35f, Text.CalcHeight(WarningText, Math.Max(1f, width - 40f))) + 20f;
        }

        public float GetContentHeight(float width)
        {
            float filterHeight = 0f;
            if (presenter.SelectedTabIndex == 0)
            {
                MeasureFilters(width, out filterHeight);
            }
            return HeaderHeight(width) + TAB_HEIGHT + (presenter.SelectedTabIndex == 0
                ? filterHeight + 10f + EmbeddedHediffLayout.ListHeight(presenter.GetFilteredHediffsCached().Count, ENTRY_HEIGHT + 2f)
                : BulkHeight(width));
        }

        #region Main Drawing

        public bool Draw(Rect inRect, Rect visiblePage)
        {
            Rect mainRect = inRect;
            float headerHeight = HeaderHeight(mainRect.width);

            // Draw header (fixed at top)
            Rect headerRect = new Rect(mainRect.x, mainRect.y, mainRect.width, headerHeight);
            DrawHeader(headerRect);

            // Draw tabs (fixed below header)
            Rect tabRect = new Rect(mainRect.x, mainRect.y + headerHeight, mainRect.width, TAB_HEIGHT);
            DrawTabs(tabRect);

            // Draw content based on selected tab
            float contentStartY = mainRect.y + headerHeight + TAB_HEIGHT;
            float contentHeight = mainRect.height - headerHeight - TAB_HEIGHT;
            Rect contentRect = new Rect(mainRect.x, contentStartY, mainRect.width, contentHeight);

            switch (presenter.SelectedTabIndex)
            {
                case 0:
                    DrawGeneralTabContent(contentRect, visiblePage);
                    break;
                case 1:
                    DrawBulkTabContent(contentRect);
                    break;
            }
            return mainRect.y < visiblePage.yMax && mainRect.y + GetContentHeight(mainRect.width) > visiblePage.y;
        }

        #endregion

        #region Header and Tabs

        private void DrawHeader(Rect rect)
        {
            GUI.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
            GUI.color = Color.white;

            // Title
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            float titleHeight = TitleHeight(rect.width);
            Text.Font = GameFont.Medium;
            Rect titleRect = new Rect(rect.x + 10f, rect.y + 5f, rect.width - 130f, titleHeight);
            Widgets.Label(titleRect, TitleText);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            // Stats (middle)
            string statsText = StatisticsText();
            float statsHeight = Math.Max(25f, Text.CalcHeight(statsText, Math.Max(1f, rect.width - 20f)));
            Rect statsRect = new Rect(rect.x + 10f, titleRect.yMax, rect.width - 20f, statsHeight);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(statsRect, statsText);
            Text.Anchor = TextAnchor.UpperLeft;

            // Reset All button (top right)
            Rect resetAllRect = new Rect(rect.xMax - 110f, rect.y + 5f, 100f, 28f);
            if (Widgets.ButtonText(resetAllRect, "Reset All"))
            {
                Find.WindowStack.Add(new Dialog_MessageBox(
                    "Reset ALL hediff settings to defaults?\n\nThis will clear all customizations and delete saved settings.",
                    "Reset All",
                    () =>
                    {
                        ReleaseEditorFocus();
                        presenter.OnResetAllToDefaults();
                        Messages.Message("All hediff settings reset to defaults.", MessageTypeDefOf.PositiveEvent);
                    },
                    "Cancel",
                    null,
                    null,
                    false,
                    null,
                    null,
                    WindowLayer.Dialog));
            }
            TooltipHandler.TipRegion(resetAllRect, "Reset all hediff settings to their default values and delete saved customizations");

            // Warning banner (below title)
            Rect warningRect = new Rect(rect.x + 10f, statsRect.yMax + 5f, rect.width - 20f,
                rect.height - titleHeight - statsHeight - 20f);
            GUI.color = new Color(0.4f, 0.25f, 0.1f, 1f);  // Dark orange/brown background
            GUI.DrawTexture(warningRect, BaseContent.WhiteTex);
            GUI.color = new Color(1f, 0.8f, 0.3f);  // Orange text
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(warningRect.x + 10f, warningRect.y, warningRect.width - 20f, warningRect.height), WarningText);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawTabs(Rect rect)
        {
            float tabWidth = rect.width / presenter.TabLabels.Length;

            for (int i = 0; i < presenter.TabLabels.Length; i++)
            {
                Rect tabRect = new Rect(rect.x + i * tabWidth, rect.y, tabWidth, rect.height);

                if (Widgets.ButtonText(tabRect, presenter.TabLabels[i], presenter.SelectedTabIndex == i))
                {
                    SelectTab(i);
                    SoundDefOf.RowTabSelect.PlayOneShotOnCamera();
                }
            }
        }

        private void SelectTab(int index)
        {
            if (presenter.SelectedTabIndex != index)
            {
                GUI.FocusControl(null);
                GUIUtility.keyboardControl = 0;
                ReleaseEditorFocus();
                presenter.OnTabSelected(index);
            }
        }

        #endregion

        #region General Tab

        private void DrawGeneralTabContent(Rect rect, Rect visiblePage)
        {
            float curY = rect.y;

            MeasureFilters(rect.width, out float filterHeight);
            Rect filterRect = new Rect(rect.x, curY, rect.width, filterHeight);
            DrawFilters(filterRect);
            curY += filterHeight + 10f;

            Rect listRect = new Rect(rect.x, curY, rect.width,
                EmbeddedHediffLayout.ListHeight(presenter.GetFilteredHediffsCached().Count, ENTRY_HEIGHT + 2f));
            DrawHediffList(listRect, visiblePage);
        }

        /// <summary>Rows pre-drawn above/below the viewport so fast scrolling never reveals a gap.</summary>
        private const int VIRTUALIZATION_BUFFER_ROWS = 5;

        private void DrawHediffList(Rect viewport, Rect visiblePage)
        {
            GUI.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            GUI.DrawTexture(viewport, BaseContent.WhiteTex);
            GUI.color = Color.white;

            // PERF-02: Use cached results — FilterToList already sorts by label, no per-frame rescan
            var filteredHediffs = presenter.GetFilteredHediffsCached();
            if (!HediffEditorScopePolicy.HasSameRows(rateRows, filteredHediffs))
            {
                string focused = GUI.GetNameOfFocusedControl();
                if ((focused?.StartsWith("EternalRate_", StringComparison.Ordinal) == true) ||
                    (focused != null && nutritionInputs.ContainsKey(focused)))
                {
                    ReleaseEditorFocus();
                }
                else { rateInputs.Clear(); nutritionInputs.Clear(); }
            }
            rateRows = filteredHediffs;

            // Full-height view rect keeps the scrollbar proportional even though only
            // the visible slice of rows is actually drawn (virtualization)
            const float rowStride = ENTRY_HEIGHT + 2f;

            if (filteredHediffs.Count == 0)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = new Color(0.6f, 0.6f, 0.6f, 1f);
                Widgets.Label(new Rect(viewport.x, viewport.y, viewport.width, 30f), "No matching hediffs");
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
            }
            else
            {
                // Draw only the rows inside the viewport plus a preload buffer —
                // off-screen rows cost nothing (their Y offsets are pure arithmetic)
                var (first, last) = EmbeddedHediffLayout.GetVisibleRange(visiblePage.y, visiblePage.height,
                    viewport.y, rowStride, filteredHediffs.Count, VIRTUALIZATION_BUFFER_ROWS);

                // Unity reuses numeric control IDs as virtual rows move. Release a hidden
                // def's focus before another row can receive that ID and its keystrokes.
                string focused = GUI.GetNameOfFocusedControl();
                if (focused?.StartsWith("EternalRate_", StringComparison.Ordinal) == true ||
                    (focused != null && nutritionInputs.ContainsKey(focused)))
                {
                    bool stillDrawn = false;
                    for (int i = first; i <= last; i++)
                    {
                        stillDrawn |= focused == "EternalRate_" + filteredHediffs[i].Key;
                        stillDrawn |= focused == filteredHediffs[i].Key + "-nutrition" || focused == filteredHediffs[i].Key + "-range";
                    }
                    if (!stillDrawn) { ReleaseEditorFocus(); }
                }

                for (int i = first; i <= last; i++)
                {
                    Rect entryRect = new Rect(viewport.x, viewport.y + i * rowStride, viewport.width, ENTRY_HEIGHT);
                    DrawHediffEntry(entryRect, filteredHediffs[i].Key, filteredHediffs[i].Value);
                }
            }
        }

        private void DrawFilters(Rect rect)
        {
            GUI.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
            GUI.color = Color.white;

            var controls = MeasureFilters(rect.width, out _);
            Widgets.BeginGroup(rect);

            // Row 1: Search bar
            Widgets.Label(controls[0], "Search:");
            string newSearch = Widgets.TextField(controls[1], presenter.SearchTerm);
            if (newSearch != presenter.SearchTerm)
            {
                presenter.OnSearchTermChanged(newSearch);
            }

            // Row 2: Category filter
            Widgets.Label(controls[2], "Category:");
            if (Widgets.ButtonText(controls[3], presenter.SelectedCategory.ToString()))
            {
                ShowCategoryMenu();
            }

            // Row 3: Source filters
            Widgets.Label(controls[4], "Source:");
            if (Widgets.ButtonText(controls[5],
                string.IsNullOrEmpty(presenter.ModSourceFilter) ? "All Mods" : presenter.ModSourceFilter))
            {
                ShowModSourceMenu();
            }

            bool baseGame = presenter.FilterBaseGame;
            bool dlc = presenter.FilterDLC;
            bool mods = presenter.FilterMods;

            Rect baseGameRect = controls[6];
            CheckboxLabeledWithTooltip(baseGameRect, "Base Game", ref baseGame,
                "Show only hediffs from the base RimWorld game (core content).");

            Rect dlcRect = controls[7];
            CheckboxLabeledWithTooltip(dlcRect, "DLCs", ref dlc,
                "Show only hediffs from official RimWorld DLCs (Royalty, Ideology, Biotech, Anomaly).");

            Rect modsRect = controls[8];
            CheckboxLabeledWithTooltip(modsRect, "Mods", ref mods,
                "Show only hediffs from installed mods (community content).");

            if (baseGame != presenter.FilterBaseGame || dlc != presenter.FilterDLC || mods != presenter.FilterMods)
            {
                presenter.OnSourceTypeFilterChanged(baseGame, dlc, mods);
            }

            // Row 4: Property filters
            Widgets.Label(controls[9], "Properties:");

            bool isBad = presenter.FilterIsBad;
            bool isLethal = presenter.FilterIsLethal;
            bool tendable = presenter.FilterTendable;
            bool chronic = presenter.FilterChronic;

            Rect badRect = controls[10];
            CheckboxLabeledWithTooltip(badRect, "Bad", ref isBad,
                "Show only negative hediffs (injuries, diseases, debuffs). Excludes beneficial effects.");

            Rect lethalRect = controls[11];
            CheckboxLabeledWithTooltip(lethalRect, "Lethal", ref isLethal,
                "Show only hediffs that can kill pawns (infections, bloodloss, heart attacks, etc.).");

            Rect tendableRect = controls[12];
            CheckboxLabeledWithTooltip(tendableRect, "Tendable", ref tendable,
                "Show only hediffs that can be treated with medical care (wounds, infections, diseases).");

            Rect chronicRect = controls[13];
            CheckboxLabeledWithTooltip(chronicRect, "Chronic", ref chronic,
                "Show only chronic conditions that never heal naturally (asthma, bad back, frail, etc.).");

            bool beneficial = presenter.FilterBeneficial;
            Rect beneficialRect = controls[14];
            CheckboxLabeledWithTooltip(beneficialRect, "Beneficial", ref beneficial,
                "Show only beneficial hediffs (stat boosts, immunities, positive effects).");

            if (isBad != presenter.FilterIsBad || isLethal != presenter.FilterIsLethal ||
                tendable != presenter.FilterTendable || chronic != presenter.FilterChronic ||
                beneficial != presenter.FilterBeneficial)
            {
                presenter.OnPropertyFilterChanged(isBad, isLethal, tendable, chronic, beneficial);
            }

            // Row 5: Toggle filters and selection
            bool showEnabled = presenter.ShowOnlyEnabled;
            Rect enabledToggleRect = controls[15];
            CheckboxLabeledWithTooltip(enabledToggleRect, "Healing only", ref showEnabled,
                "Show only hediffs that will be healed. Hide hediffs with healing disabled.");
            if (showEnabled != presenter.ShowOnlyEnabled)
            {
                presenter.OnShowOnlyEnabledChanged(showEnabled);
            }

            bool showCustom = presenter.ShowOnlyCustom;
            Rect customToggleRect = controls[16];
            CheckboxLabeledWithTooltip(customToggleRect, "Custom only", ref showCustom,
                "Show only hediffs with customized settings. Hide hediffs using default values.");
            if (showCustom != presenter.ShowOnlyCustom)
            {
                presenter.OnShowOnlyCustomChanged(showCustom);
            }

            // Selection buttons
            Rect selectAllRect = controls[17];
            if (Widgets.ButtonText(selectAllRect, "Select All"))
            {
                ReleaseEditorFocus();
                presenter.OnSelectAll();
            }

            Rect clearSelectionRect = controls[18];
            if (Widgets.ButtonText(clearSelectionRect, "Clear"))
            {
                ReleaseEditorFocus();
                presenter.OnClearSelection();
            }

            Rect clearFiltersRect = controls[19];
            if (Widgets.ButtonText(clearFiltersRect, "Clear Filters"))
            {
                presenter.OnClearAllFilters();
            }

            Rect selectionCountRect = controls[20];
            Widgets.Label(selectionCountRect, $"Selected: {presenter.GetSelectedCount()}");
            Widgets.EndGroup();
        }

        // Measurement and rendering share the same rectangles; measuring never invokes a control.
        private Rect[] MeasureFilters(float width, out float height)
        {
            var key = (width, presenter.SelectedCategory, presenter.ModSourceFilter, presenter.GetSelectedCount());
            if (filterControls != null && filterLayoutKey.Equals(key))
            {
                height = measuredFilterHeight;
                return filterControls;
            }

            Text.Font = GameFont.Small;
            var result = new List<Rect>();
            float y = 5f;
            void Row(float[] widths, string[] labels, bool[] checkboxes = null)
            {
                float x = 5f;
                float rowHeight = 30f;
                for (int i = 0; i < widths.Length; i++)
                {
                    float controlWidth = Math.Min(widths[i], Math.Max(1f, width - 10f));
                    if (x > 5f && x + controlWidth > width - 5f)
                    {
                        y += rowHeight + 5f;
                        x = 5f;
                        rowHeight = 30f;
                    }

                    float labelWidth = controlWidth - (checkboxes != null && checkboxes[i] ? 24f : 0f);
                    float controlHeight = Math.Max(30f, Text.CalcHeight(labels[i], Math.Max(1f, labelWidth)));
                    result.Add(new Rect(x, y, controlWidth, controlHeight));
                    rowHeight = Math.Max(rowHeight, controlHeight);
                    x += controlWidth + 5f;
                }
                y += rowHeight + 5f;
            }

            Row(new[] { 60f, Math.Max(1f, width - 75f) }, new[] { "Search:", "" });
            Row(new[] { 65f, 180f }, new[] { "Category:", presenter.SelectedCategory.ToString() });
            Row(new[] { 60f, 160f, 120f, 80f, 80f },
                new[] { "Source:", string.IsNullOrEmpty(presenter.ModSourceFilter) ? "All Mods" : presenter.ModSourceFilter, "Base Game", "DLCs", "Mods" },
                new[] { false, false, true, true, true });
            Row(new[] { 80f, 80f, 90f, 100f, 90f, 100f },
                new[] { "Properties:", "Bad", "Lethal", "Tendable", "Chronic", "Beneficial" },
                new[] { false, true, true, true, true, true });
            Row(new[] { 120f, 120f, 90f, 100f, 100f, 150f },
                new[] { "Healing only", "Custom only", "Select All", "Clear", "Clear Filters", $"Selected: {presenter.GetSelectedCount()}" },
                new[] { true, true, false, false, false, false });
            height = y;
            measuredFilterHeight = height;
            filterLayoutKey = key;
            filterControls = result.ToArray();
            return filterControls;
        }

        private void ShowModSourceMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            options.Add(new FloatMenuOption("All Mods", () =>
            {
                presenter.OnModSourceChanged("");
            }));

            var modSources = presenter.GetAvailableModSources();
            foreach (var modSource in modSources)
            {
                options.Add(new FloatMenuOption(modSource, () =>
                {
                    presenter.OnModSourceChanged(modSource);
                }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void ShowCategoryMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            foreach (HediffCategory category in Enum.GetValues(typeof(HediffCategory)))
            {
                options.Add(new FloatMenuOption(category.ToString(), () =>
                {
                    presenter.OnCategoryChanged(category);
                }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        #endregion

        #region Hediff Entry Drawing

        private void DrawHediffEntry(Rect rect, string hediffName, EternalHediffSetting setting)
        {
            // Background
            if (presenter.IsHediffSelected(hediffName))
            {
                GUI.DrawTexture(rect, TexUI.HighlightTex);
            }
            else
            {
                GUI.color = new Color(0.15f, 0.15f, 0.15f, 1f);
                GUI.DrawTexture(rect, BaseContent.WhiteTex);
                GUI.color = Color.white;
            }

            var def = DefDatabase<HediffDef>.GetNamedSilentFail(hediffName);
            bool isProtected = RjwHealthPolicy.IsProtected(def);
            string protectionReason = RjwHealthPolicy.ProtectionReason(def);
            bool originalGuiEnabled = GUI.enabled;

            float curX = rect.x + 5f;
            float centerY = rect.y + 6f;

            // Selection checkbox
            Rect checkboxRect = new Rect(curX, centerY, 24f, 24f);
            bool selected = presenter.IsHediffSelected(hediffName);
            if (Widgets.ButtonImage(checkboxRect, selected ? Widgets.CheckboxOnTex : Widgets.CheckboxOffTex))
            {
                ReleaseEditorFocus();
                presenter.OnHediffSelectionToggled(hediffName);
            }
            curX += 28f;

            // Heal toggle
            Rect healRect = new Rect(curX, centerY, 24f, 24f);
            bool canHeal = RjwHealthPolicy.EffectiveCanHeal(def, setting);
            bool regrowthManaged = Eternal.Healing.HediffHealingConfig.IsRegrowthManaged(def);
            if (regrowthManaged) { canHeal = false; }
            Widgets.Checkbox(new Vector2(healRect.x, healRect.y), ref canHeal, 24f, isProtected || regrowthManaged, true);
            if (!isProtected && !regrowthManaged && canHeal != setting.canHeal)
            {
                setting.canHeal = canHeal;
                presenter.NotifyHediffSettingChanged();
            }
            TooltipHandler.TipRegion(healRect, regrowthManaged ? "Progress is managed by part regrowth; pricing remains editable."
                : isProtected ? protectionReason : "Enable/disable healing for this hediff");
            curX += 28f;

            // Hediff name (with color coding)
            float nameWidth = Math.Min(260f, rect.width * 0.35f);
            Rect nameRect = new Rect(curX, rect.y, nameWidth, 36f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;

            string displayLabel = presenter.GetHediffDisplayLabel(hediffName);
            GUI.color = presenter.GetHediffTypeColor(setting);
            Widgets.Label(nameRect, displayLabel);
            GUI.color = Color.white;

            // Tooltip with description
            string description = presenter.GetHediffDescription(hediffName);
            if (isProtected)
            {
                description += "\n\n" + protectionReason;
            }
            if (!string.IsNullOrEmpty(description) && Mouse.IsOver(nameRect))
            {
                TooltipHandler.TipRegion(nameRect, description);
            }
            curX += nameWidth + 10f;

            // Beneficial indicator (compact)
            if (setting.isBeneficial)
            {
                Rect beneficialRect = new Rect(curX, rect.y, 20f, rect.height);
                GUI.color = new Color(0.3f, 0.9f, 0.3f);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(beneficialRect, "★");
                TooltipHandler.TipRegion(beneficialRect, "Beneficial hediff (not harmful)");
                GUI.color = Color.white;
                curX += 25f;
            }
            else
            {
                curX += 25f;  // Keep alignment consistent
            }

            GUI.enabled = originalGuiEnabled && !isProtected;

            // Healing rate slider + input (range comes from SettingsDefaults).
            float rateSliderWidth = Math.Max(80f, rect.xMax - 80f - curX);
            Rect rateRect = new Rect(curX, centerY - 2f, rateSliderWidth, 28f);
            float displayRate = setting.HasCustomHealingRate
                ? setting.healingRate
                : Eternal_Mod.GetSettings().baseHealingRate;
            // Show "G:" prefix for global rate, rate value with 4 decimals
            string rateLabel = setting.HasCustomHealingRate ? $"{displayRate:F4}" : $"G:{displayRate:F4}";
            string rateTooltip = setting.HasCustomHealingRate
                ? $"Custom rate: {displayRate:F4} (overrides global)\n{MenuValueCalculator.Pricing(HealingCostProfiles.Resolve(def, setting, Eternal_Mod.GetSettings().nutritionCostMultiplier, false), def?.initialSeverity ?? 0f)}"
                : $"Using global rate: {displayRate:F4}\n{MenuValueCalculator.Pricing(HealingCostProfiles.Resolve(def, setting, Eternal_Mod.GetSettings().nutritionCostMultiplier, false), def?.initialSeverity ?? 0f)}";

            float newRate = DrawCompactSliderWithInput(
                rateRect,
                "EternalRate_" + hediffName,
                displayRate,
                SettingsDefaults.HediffHealingRateMin,
                SettingsDefaults.HediffHealingRateMax,
                rateLabel,
                rateTooltip);
            if (!isProtected && Math.Abs(newRate - displayRate) > 0.00001f)
            {
                setting.healingRate = newRate;
                presenter.NotifyHediffSettingChanged();
            }

            Rect globalRect = new Rect(rect.xMax - 70f, centerY, 30f, 24f);
            TooltipHandler.TipRegion(globalRect, "Use global healing rate");
            if (setting.HasCustomHealingRate && Widgets.ButtonText(globalRect, "G"))
            {
                ReleaseEditorFocus();
                setting.ResetToGlobalRate();
                presenter.NotifyHediffSettingChanged();
            }

            // Reset button
            Rect resetRect = new Rect(rect.xMax - 35f, centerY, 30f, 24f);
            TooltipHandler.TipRegion(resetRect, "Reset to defaults");
            if (Widgets.ButtonText(resetRect, "↺"))
            {
                ReleaseEditorFocus();
                presenter.OnResetSingleHediff(hediffName);
            }

            DrawPricingControls(new Rect(rect.x + 5f, rect.y + 36f, rect.width - 10f, 88f), setting, def, true);
            GUI.enabled = originalGuiEnabled;
            if (isProtected)
            {
                TooltipHandler.TipRegion(rateRect, protectionReason);
                TooltipHandler.TipRegion(globalRect, protectionReason);
                TooltipHandler.TipRegion(resetRect, protectionReason);
            }
            Text.Anchor = TextAnchor.UpperLeft;
        }

        /// <summary>
        /// Draws a compact slider with inline numeric input.
        /// Uses 4 decimal places for precision.
        /// </summary>
        private float DrawCompactSliderWithInput(Rect rect, string key, float value, float min, float max, string label, string tooltip)
        {
            float sliderWidth = rect.width * 0.6f;
            float inputWidth = rect.width * 0.35f;

            Rect sliderRect = new Rect(rect.x, rect.y, sliderWidth, rect.height);
            Rect inputRect = new Rect(rect.x + sliderWidth + 5f, rect.y + 2f, inputWidth, rect.height - 4f);

            return DrawRateInput(rect, sliderRect, inputRect, key, value, min, max, label, tooltip);
        }

        // NOTE: DrawDetailedSettings removed - replaced by compact single-row layout

        #endregion



        #region Bulk Tab

        private float BulkActionHeight(float width)
        {
            Text.Font = GameFont.Small;
            float actionWidth = Math.Max(1f, (width - 60f) / 3f);
            return Math.Max(30f, Math.Max(Text.CalcHeight("Apply to Selected", actionWidth),
                Text.CalcHeight("Reset All to Default", actionWidth)));
        }

        private float BulkHeight(float width) => 340f + BulkActionHeight(width);

        private void DrawBulkTabContent(Rect rect)
        {
            float curY = rect.y + 10f;

            // Content-fitted background (same treatment as the Advanced tab) instead of
            // painting the entire tab.
            Rect bgRect = new Rect(rect.x + 10f, curY, rect.width - 20f, BulkHeight(rect.width) - 20f);
            GUI.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            GUI.DrawTexture(bgRect, BaseContent.WhiteTex);
            GUI.color = Color.white;

            curY += 10f;
            Widgets.Label(new Rect(rect.x + 20f, curY, rect.width - 40f, 25f), "Bulk Operations Template:");
            curY += 30f;

            DrawTemplateSettings(new Rect(rect.x + 20f, curY, rect.width - 40f, 245f));
            curY += 260f;

            float actionWidth = Math.Max(1f, (rect.width - 60f) / 3f);
            float actionHeight = BulkActionHeight(rect.width);
            Rect applySelectedRect = new Rect(rect.x + 20f, curY, actionWidth, actionHeight);
            if (Widgets.ButtonText(applySelectedRect, "Apply to Selected"))
            {
                int count = presenter.OnApplyToSelected();
                if (count > 0)
                {
                    Messages.Message($"Applied template settings to {count} hediffs.", MessageTypeDefOf.PositiveEvent);
                }
                else
                {
                    Messages.Message("No hediffs selected.", MessageTypeDefOf.RejectInput);
                }
            }

            Rect applyAllRect = new Rect(applySelectedRect.xMax + 10f, curY, actionWidth, actionHeight);
            if (Widgets.ButtonText(applyAllRect, "Apply to All"))
            {
                int count = presenter.OnApplyToAll();
                Messages.Message($"Applied template settings to {count} hediffs.", MessageTypeDefOf.PositiveEvent);
            }

            Rect resetAllRect = new Rect(applyAllRect.xMax + 10f, curY, actionWidth, actionHeight);
            if (Widgets.ButtonText(resetAllRect, "Reset All to Default"))
            {
                ReleaseEditorFocus();
                presenter.OnResetAllToDefaults();
                Messages.Message("Reset all hediff settings to defaults.", MessageTypeDefOf.PositiveEvent);
            }
        }

        private void DrawTemplateSettings(Rect rect)
        {
            GUI.DrawTexture(rect, TexUI.HighlightTex);

            var template = presenter.BulkTemplate;
            float curX = rect.x + 10f;
            float curY = rect.y + 10f;
            float columnWidth = rect.width - 20f;

            Widgets.Label(new Rect(curX, curY, columnWidth, 20f), "Template Settings:");
            curY += 25f;

            // Single "Heal" toggle (simplified from dual Enable + Can Heal)
            Rect healRect = new Rect(curX, curY, CHECKBOX_ROW_WIDTH, 20f);
            CheckboxLabeledWithTooltip(healRect, "Heal", ref template.canHeal,
                "Template setting for enabling healing. When applied, matched hediffs will heal.");
            curY += 25f;

            Rect noThresholdRect = new Rect(curX, curY, CHECKBOX_ROW_WIDTH, 20f);
            CheckboxLabeledWithTooltip(noThresholdRect, "Bypass activation threshold", ref template.noThreshold,
                "Eligible without waiting for a severity threshold; healing still runs on scheduled passes.");
            curY += 25f;

            // Template healing rate (always sets a custom value when applied)
            float templateRate = template.HasCustomHealingRate
                ? template.healingRate
                : Eternal_Mod.GetSettings().baseHealingRate;
            string templateRateLabel = template.HasCustomHealingRate
                ? $"Heal Rate: {templateRate:F3}"
                : $"Heal Rate: Global ({templateRate:F3})";

            // 30f-tall slider rows: HorizontalSlider draws its label above the rail
            // and clips when the rect is shorter.
            float templateSliderWidth = template.HasCustomHealingRate ? columnWidth - 70f : columnWidth - 20f;
            Rect healRateRect = new Rect(curX, curY, templateSliderWidth, 30f);
            float newTemplateRate = DrawSliderWithNumericInput(
                healRateRect,
                "EternalRate_bulk",
                templateRate,
                SettingsDefaults.HediffHealingRateMin,
                SettingsDefaults.HediffHealingRateMax,
                templateRateLabel,
                "Template healing rate. When applied, this rate will override global for each hediff.");

            // If user changed the value, set custom rate
            if (newTemplateRate != templateRate)
            {
                template.healingRate = newTemplateRate;
            }
            DrawPricingControls(new Rect(rect.x + 10f, curY + 40f, rect.width - 20f, 88f), template, null, false);

            // Reset button (only if custom)
            if (template.HasCustomHealingRate)
            {
                Rect resetTemplateRect = new Rect(curX + templateSliderWidth + 5f, curY + 5f, 45f, 20f);
                if (Widgets.ButtonText(resetTemplateRect, "Reset"))
                {
                    ReleaseEditorFocus();
                    template.ResetToGlobalRate();
                }
            }
        }

        #endregion

        private readonly Dictionary<string, string> nutritionInputs = new Dictionary<string, string>();

        public void ReleaseEditorFocus()
        {
            GUI.FocusControl(null);
            GUIUtility.keyboardControl = 0;
            nutritionInputs.Clear();
            rateInputs.Clear();
        }

        private void DrawPricingControls(Rect rect, EternalHediffSetting setting, HediffDef def, bool notify)
        {
            float multiplier = NutritionInput(new Rect(rect.x, rect.y, rect.width, 24f),
                (def?.defName ?? "bulk") + "-nutrition", "Nutrition cost multiplier", setting.nutritionCostMultiplier,
                SettingsDefaults.HediffNutritionCostMultiplier, SettingsDefaults.HediffNutritionCostMultiplierMax, "Default");
            float range = NutritionInput(new Rect(rect.x, rect.y + 26f, rect.width, 24f),
                (def?.defName ?? "bulk") + "-range", "Severity range (0 = auto)", setting.nutritionSeverityRangeOverride,
                SettingsDefaults.HediffNutritionSeverityRangeOverride, SettingsDefaults.HediffNutritionSeverityRangeMax, "Auto");
            if (multiplier != setting.nutritionCostMultiplier || range != setting.nutritionSeverityRangeOverride)
            {
                setting.nutritionCostMultiplier = SettingsDefaults.NormalizeNutritionMultiplier(multiplier);
                setting.nutritionSeverityRangeOverride = SettingsDefaults.NormalizeNutritionRange(range);
                if (notify)
                {
                    presenter.NotifyHediffSettingChanged();
                }
            }
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(rect.x, rect.y + 52f, rect.width, 36f), def == null ?
                "Category and stage prices follow each target definition; this template changes cost controls only." :
                MenuValueCalculator.Pricing(HealingCostProfiles.Resolve(def, setting,
                    Eternal_Mod.GetSettings().nutritionCostMultiplier, false), def.initialSeverity));
            Text.Font = GameFont.Small;
        }

        private float NutritionInput(Rect rect, string key, string label, float value, float defaultValue, float maximum, string reset)
        {
            float labelWidth = Math.Max(1f, rect.width - 165f);
            Widgets.Label(new Rect(rect.x, rect.y, labelWidth, rect.height), label);
            if (!nutritionInputs.TryGetValue(key, out string buffer) || GUI.GetNameOfFocusedControl() != key)
            {
                buffer = value.ToString("0.####", CultureInfo.InvariantCulture);
            }
            GUI.SetNextControlName(key);
            string edited = Widgets.TextField(new Rect(rect.x + labelWidth + 5f, rect.y, 80f, rect.height), buffer);
            nutritionInputs[key] = edited;
            if (GUI.enabled && edited != buffer && float.TryParse(edited, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) && MenuValueCalculator.IsFinite(parsed))
            {
                value = Math.Max(0f, Math.Min(maximum, parsed));
            }
            if (Widgets.ButtonText(new Rect(rect.xMax - 70f, rect.y, 70f, rect.height), reset))
            {
                nutritionInputs.Remove(key);
                value = defaultValue;
            }
            return value;
        }

        #region Helper Methods

        private float DrawSliderWithNumericInput(Rect rect, string key, float value, float min, float max, string label, string tooltip = null)
        {
            float sliderWidth = rect.width * 0.70f;
            float inputWidth = rect.width * 0.25f;
            float gapWidth = rect.width * 0.05f;

            Rect sliderRect = new Rect(rect.x, rect.y, sliderWidth, rect.height);
            Rect inputRect = new Rect(rect.x + sliderWidth + gapWidth, rect.y, inputWidth, rect.height);

            return DrawRateInput(rect, sliderRect, inputRect, key, value, min, max, label, tooltip);
        }

        private float DrawRateInput(Rect rect, Rect sliderRect, Rect inputRect, string key,
            float value, float min, float max, string label, string tooltip)
        {
            bool enabled = GUI.enabled;
            var state = RateInput(key);
            bool focused = GUI.GetNameOfFocusedControl() == key;
            bool enter = Event.current.type == EventType.KeyDown && focused &&
                (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            bool cancel = Event.current.type == EventType.KeyDown && focused && Event.current.keyCode == KeyCode.Escape;
            bool outside = Event.current.type == EventType.MouseDown && !inputRect.Contains(Event.current.mousePosition);
            if (!enabled)
            {
                state.Reset();
                if (focused) { GUI.FocusControl(null); GUIUtility.keyboardControl = 0; }
            }
            bool sliderEdited = enabled && Mouse.IsOver(sliderRect) &&
                (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseDrag);
            float sliderValue = value;
            if (enabled)
            {
                sliderValue = Widgets.HorizontalSlider(sliderRect, value, min, max, true, label);
            }
            else
            {
                SettingsDrawer.DrawDisabledSlider(sliderRect, value, min, max);
                Widgets.Label(sliderRect, label);
            }
            string buffer = state.Prepare(value, focused);
            GUI.SetNextControlName(key);
            string edited = Widgets.TextField(inputRect, buffer);
            if (enabled) { state.Update(edited); }

            bool blur = state.HasEdit && GUI.GetNameOfFocusedControl() != key;
            float result = value;
            if (sliderEdited)
            {
                result = MenuValueCalculator.EditedRate(value, sliderValue, true);
                state.Reset();
                if (focused) { GUI.FocusControl(null); GUIUtility.keyboardControl = 0; }
            }
            else if (cancel || enter || outside || blur || !enabled)
            {
                result = state.Commit(value, enabled && !cancel);
                if (focused && (cancel || enter || outside)) { GUI.FocusControl(null); GUIUtility.keyboardControl = 0; }
                if (enter || cancel) { Event.current.Use(); }
            }
            if (!string.IsNullOrEmpty(tooltip) && Mouse.IsOver(rect)) { TooltipHandler.TipRegion(rect, tooltip); }
            return result;
        }

        private void CheckboxLabeledWithTooltip(Rect rect, string label, ref bool checkOn, string tooltip)
        {
            Widgets.CheckboxLabeled(rect, label, ref checkOn);

            if (!string.IsNullOrEmpty(tooltip) && Mouse.IsOver(rect))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }
        }

        #endregion
    }

    internal sealed class RateEditBuffer
    {
        internal string Text { get; private set; }
        internal bool HasEdit { get; private set; }
        private float source;

        internal string Prepare(float value, bool focused)
        {
            if (Text == null || source != value || (!focused && !HasEdit))
            {
                Text = value.ToString("F4", CultureInfo.InvariantCulture);
                HasEdit = false;
                source = value;
            }
            return Text;
        }

        internal void Update(string text)
        {
            HasEdit |= Text != text;
            Text = text;
        }

        internal float Commit(float value, bool enabled)
        {
            float result = enabled && HasEdit && float.TryParse(Text, NumberStyles.Float,
                CultureInfo.InvariantCulture, out float parsed) ? MenuValueCalculator.EditedRate(value, parsed, true) : value;
            Reset();
            return result;
        }

        internal void Reset() { Text = null; HasEdit = false; }
    }
    internal static class HediffEditorScopePolicy
    {
        internal static bool HasSameRows<T>(IReadOnlyList<KeyValuePair<string, T>> previous,
            IReadOnlyList<KeyValuePair<string, T>> current)
        {
            if (ReferenceEquals(previous, current)) { return true; }
            if (previous == null || current == null || previous.Count != current.Count) { return false; }
            // Values dirty the filter cache too. Stable ordered def names retain the
            // same controls and their in-progress text even when the list is rebuilt.
            for (int i = 0; i < previous.Count; i++)
            {
                if (!string.Equals(previous[i].Key, current[i].Key, StringComparison.Ordinal)) { return false; }
            }
            return true;
        }
    }
}
