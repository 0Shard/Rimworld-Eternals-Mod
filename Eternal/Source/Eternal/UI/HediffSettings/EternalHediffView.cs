// Relative Path: Eternal/Source/Eternal/UI/HediffSettings/EternalHediffView.cs
// Creation Date: 09-11-2025
// Last Edit: 21-02-2026
// Author: 0Shard
// Description: View layer for hediff settings UI. Pure UI rendering, delegates to Presenter.
//              SIMPLIFIED: Single compact row per hediff with only 3 options:
//              - Heal toggle
//              - Healing rate slider + input
//              - Nutrition cost slider + input
//              Per-hediff and global reset buttons.
//              PERF-02: Render loop uses GetFilteredHediffsCached() instead of per-frame scan.

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
    public class EternalHediffView : Window
    {
        private readonly EternalHediffPresenter presenter;

        // Layout constants (RimWorld 1.6 modern design - SIMPLIFIED)
        private const float HEADER_HEIGHT = 80f;
        private const float FILTER_HEIGHT = 180f;
        private const float TAB_HEIGHT = 30f;
        private const float ENTRY_HEIGHT = 36f;  // Compact single row

        public EternalHediffView(EternalHediffManager manager)
        {
            if (manager == null)
                throw new ArgumentNullException(nameof(manager));

            var model = new EternalHediffModel(manager);
            presenter = new EternalHediffPresenter(model);

            this.forcePause = true;
            this.doCloseX = true;
            this.absorbInputAroundWindow = true;
            this.closeOnClickedOutside = false;

            this.windowRect = new Rect(
                (float)Screen.width / 2 - 500f,
                (float)Screen.height / 2 - 350f,
                1000f,
                700f
            );
        }

        public override Vector2 InitialSize => new Vector2(1000f, 700f);

        #region Main Drawing

        public override void DoWindowContents(Rect inRect)
        {
            Rect mainRect = inRect;

            // Draw header (fixed at top)
            Rect headerRect = new Rect(mainRect.x, mainRect.y, mainRect.width, HEADER_HEIGHT);
            DrawHeader(headerRect);

            // Draw tabs (fixed below header)
            Rect tabRect = new Rect(mainRect.x, mainRect.y + HEADER_HEIGHT, mainRect.width, TAB_HEIGHT);
            DrawTabs(tabRect);

            // Draw content based on selected tab
            float contentStartY = mainRect.y + HEADER_HEIGHT + TAB_HEIGHT;
            float contentHeight = mainRect.height - HEADER_HEIGHT - TAB_HEIGHT;
            Rect contentRect = new Rect(mainRect.x, contentStartY, mainRect.width, contentHeight);

            switch (presenter.SelectedTabIndex)
            {
                case 0:
                    DrawGeneralTabContent(contentRect);
                    break;
                case 1:
                    DrawAdvancedTabContent(contentRect);
                    break;
                case 2:
                    DrawBulkTabContent(contentRect);
                    break;
            }
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
            Rect titleRect = new Rect(rect.x + 10f, rect.y + 5f, rect.width * 0.4f, 30f);
            Widgets.Label(titleRect, "Eternal Hediff Healing Manager");
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            // Stats (middle)
            Rect statsRect = new Rect(rect.x + rect.width * 0.4f, rect.y + 5f, rect.width * 0.3f, 30f);
            var stats = presenter.GetStatistics();
            string statsText = $"Healing {stats.HealingHediffs} of {stats.TotalHediffs} hediffs";
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(statsRect, statsText);
            Text.Anchor = TextAnchor.UpperLeft;

            // Reset All button (top right)
            Rect resetAllRect = new Rect(rect.width - 110f, rect.y + 5f, 100f, 28f);
            if (Widgets.ButtonText(resetAllRect, "Reset All"))
            {
                Find.WindowStack.Add(new Dialog_MessageBox(
                    "Reset ALL hediff settings to defaults?\n\nThis will clear all customizations and delete saved settings.",
                    "Reset All",
                    () => {
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
            Rect warningRect = new Rect(rect.x + 10f, rect.y + 40f, rect.width - 20f, 35f);
            GUI.color = new Color(0.4f, 0.25f, 0.1f, 1f);  // Dark orange/brown background
            GUI.DrawTexture(warningRect, BaseContent.WhiteTex);
            GUI.color = new Color(1f, 0.8f, 0.3f);  // Orange text
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            string warningText = "⚠ Diseases and conditions require opt-in. Beneficial hediffs (★) won't heal by default.";
            Widgets.Label(new Rect(warningRect.x + 10f, warningRect.y, warningRect.width - 20f, warningRect.height), warningText);
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
                    presenter.OnTabSelected(i);
                    SoundDefOf.RowTabSelect.PlayOneShotOnCamera();
                }
            }
        }

        #endregion

        #region General Tab

        private void DrawGeneralTabContent(Rect rect)
        {
            float curY = rect.y;

            Rect filterRect = new Rect(rect.x, curY, rect.width, FILTER_HEIGHT);
            DrawFilters(filterRect);
            curY += FILTER_HEIGHT + 10f;

            float hediffViewportHeight = rect.height - FILTER_HEIGHT - 10f;
            Rect hediffViewport = new Rect(rect.x, curY, rect.width, hediffViewportHeight);
            DrawHediffListWithInnerScroll(hediffViewport);
        }

        private void DrawHediffListWithInnerScroll(Rect viewport)
        {
            GUI.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            GUI.DrawTexture(viewport, BaseContent.WhiteTex);
            GUI.color = Color.white;

            var scrollPos = presenter.ScrollPosition;
            Widgets.BeginScrollView(viewport, ref scrollPos,
                new Rect(0, 0, viewport.width - 16f, presenter.ScrollViewHeight));
            presenter.ScrollPosition = scrollPos;

            float curY = 0f;
            // PERF-02: Use cached results — FilterToList already sorts by label, no per-frame rescan
            var filteredHediffs = presenter.GetFilteredHediffsCached();

            if (filteredHediffs.Count == 0)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = new Color(0.6f, 0.6f, 0.6f, 1f);
                Widgets.Label(new Rect(0, curY, viewport.width - 16f, 30f), "No matching hediffs");
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
            }
            else
            {
                foreach (var kvp in filteredHediffs)
                {
                    // Simplified: fixed height for all entries (no expanded state)
                    Rect entryRect = new Rect(0, curY, viewport.width - 16f, ENTRY_HEIGHT);
                    DrawHediffEntry(entryRect, kvp.Key, kvp.Value);
                    curY += ENTRY_HEIGHT + 2f;
                }
            }

            presenter.ScrollViewHeight = curY;
            Widgets.EndScrollView();
        }

        private void DrawFilters(Rect rect)
        {
            GUI.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
            GUI.color = Color.white;

            float curY = rect.y + 5f;
            float curX = rect.x + 5f;

            // Row 1: Search bar
            Widgets.Label(new Rect(curX, curY, 60f, 30f), "Search:");
            string newSearch = Widgets.TextField(new Rect(curX + 65f, curY, rect.width - 75f, 30f), presenter.SearchTerm);
            if (newSearch != presenter.SearchTerm)
            {
                presenter.OnSearchTermChanged(newSearch);
            }
            curY += 35f;

            // Row 2: Category filter
            Rect categoryRect = new Rect(curX, curY, 180f, 25f);
            Widgets.Label(categoryRect.LeftPart(0.35f), "Category:");
            if (Widgets.ButtonText(categoryRect.RightPart(0.65f), presenter.SelectedCategory.ToString()))
            {
                ShowCategoryMenu();
            }
            curY += 30f;

            // Row 3: Source filters
            Widgets.Label(new Rect(curX, curY, 60f, 25f), "Source:");
            if (Widgets.ButtonText(new Rect(curX + 65f, curY, 115f, 25f),
                string.IsNullOrEmpty(presenter.ModSourceFilter) ? "All Mods" : presenter.ModSourceFilter))
            {
                ShowModSourceMenu();
            }

            bool baseGame = presenter.FilterBaseGame;
            bool dlc = presenter.FilterDLC;
            bool mods = presenter.FilterMods;

            Rect baseGameRect = new Rect(curX + 190f, curY, 120f, 25f);
            CheckboxLabeledWithTooltip(baseGameRect, "Base Game", ref baseGame,
                "Show only hediffs from the base RimWorld game (core content).");

            Rect dlcRect = new Rect(curX + 320f, curY, 80f, 25f);
            CheckboxLabeledWithTooltip(dlcRect, "DLCs", ref dlc,
                "Show only hediffs from official RimWorld DLCs (Royalty, Ideology, Biotech, Anomaly).");

            Rect modsRect = new Rect(curX + 410f, curY, 80f, 25f);
            CheckboxLabeledWithTooltip(modsRect, "Mods", ref mods,
                "Show only hediffs from installed mods (community content).");

            if (baseGame != presenter.FilterBaseGame || dlc != presenter.FilterDLC || mods != presenter.FilterMods)
            {
                presenter.OnSourceTypeFilterChanged(baseGame, dlc, mods);
            }
            curY += 30f;

            // Row 4: Property filters
            Widgets.Label(new Rect(curX, curY, 80f, 25f), "Properties:");

            bool isBad = presenter.FilterIsBad;
            bool isLethal = presenter.FilterIsLethal;
            bool tendable = presenter.FilterTendable;
            bool chronic = presenter.FilterChronic;

            Rect badRect = new Rect(curX + 85f, curY, 80f, 25f);
            CheckboxLabeledWithTooltip(badRect, "Bad", ref isBad,
                "Show only negative hediffs (injuries, diseases, debuffs). Excludes beneficial effects.");

            Rect lethalRect = new Rect(curX + 175f, curY, 90f, 25f);
            CheckboxLabeledWithTooltip(lethalRect, "Lethal", ref isLethal,
                "Show only hediffs that can kill pawns (infections, bloodloss, heart attacks, etc.).");

            Rect tendableRect = new Rect(curX + 275f, curY, 100f, 25f);
            CheckboxLabeledWithTooltip(tendableRect, "Tendable", ref tendable,
                "Show only hediffs that can be treated with medical care (wounds, infections, diseases).");

            Rect chronicRect = new Rect(curX + 385f, curY, 90f, 25f);
            CheckboxLabeledWithTooltip(chronicRect, "Chronic", ref chronic,
                "Show only chronic conditions that never heal naturally (asthma, bad back, frail, etc.).");

            bool beneficial = presenter.FilterBeneficial;
            Rect beneficialRect = new Rect(curX + 485f, curY, 100f, 25f);
            CheckboxLabeledWithTooltip(beneficialRect, "Beneficial", ref beneficial,
                "Show only beneficial hediffs (stat boosts, immunities, positive effects).");

            if (isBad != presenter.FilterIsBad || isLethal != presenter.FilterIsLethal ||
                tendable != presenter.FilterTendable || chronic != presenter.FilterChronic ||
                beneficial != presenter.FilterBeneficial)
            {
                presenter.OnPropertyFilterChanged(isBad, isLethal, tendable, chronic, beneficial);
            }
            curY += 30f;

            // Row 5: Toggle filters and selection
            bool showEnabled = presenter.ShowOnlyEnabled;
            Rect enabledToggleRect = new Rect(curX, curY, 120f, 25f);
            CheckboxLabeledWithTooltip(enabledToggleRect, "Healing only", ref showEnabled,
                "Show only hediffs that will be healed. Hide hediffs with healing disabled.");
            if (showEnabled != presenter.ShowOnlyEnabled)
            {
                presenter.OnShowOnlyEnabledChanged(showEnabled);
            }

            bool showCustom = presenter.ShowOnlyCustom;
            Rect customToggleRect = new Rect(curX + 130f, curY, 120f, 25f);
            CheckboxLabeledWithTooltip(customToggleRect, "Custom only", ref showCustom,
                "Show only hediffs with customized settings. Hide hediffs using default values.");
            if (showCustom != presenter.ShowOnlyCustom)
            {
                presenter.OnShowOnlyCustomChanged(showCustom);
            }

            // Selection buttons
            Rect selectAllRect = new Rect(curX + 260f, curY, 90f, 25f);
            if (Widgets.ButtonText(selectAllRect, "Select All"))
            {
                presenter.OnSelectAll();
            }

            Rect clearSelectionRect = new Rect(curX + 360f, curY, 100f, 25f);
            if (Widgets.ButtonText(clearSelectionRect, "Clear"))
            {
                presenter.OnClearSelection();
            }

            Rect clearFiltersRect = new Rect(curX + 470f, curY, 100f, 25f);
            if (Widgets.ButtonText(clearFiltersRect, "Clear Filters"))
            {
                presenter.OnClearAllFilters();
            }

            Rect selectionCountRect = new Rect(curX + 580f, curY, 150f, 25f);
            Widgets.Label(selectionCountRect, $"Selected: {presenter.GetSelectedCount()}");
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

            float curX = rect.x + 5f;
            float centerY = rect.y + (rect.height - 24f) / 2f;

            // Selection checkbox
            Rect checkboxRect = new Rect(curX, centerY, 24f, 24f);
            bool selected = presenter.IsHediffSelected(hediffName);
            if (Widgets.ButtonImage(checkboxRect, selected ? Widgets.CheckboxOnTex : Widgets.CheckboxOffTex))
            {
                presenter.OnHediffSelectionToggled(hediffName);
            }
            curX += 28f;

            // Heal toggle
            Rect healRect = new Rect(curX, centerY, 24f, 24f);
            bool canHeal = setting.canHeal;
            Widgets.Checkbox(new Vector2(healRect.x, healRect.y), ref canHeal, 24f, false, true);
            if (canHeal != setting.canHeal)
            {
                setting.canHeal = canHeal;
            }
            TooltipHandler.TipRegion(healRect, "Enable/disable healing for this hediff");
            curX += 28f;

            // Hediff name (with color coding)
            float nameWidth = 200f;
            Rect nameRect = new Rect(curX, rect.y, nameWidth, rect.height);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;

            string displayLabel = presenter.GetHediffDisplayLabel(hediffName);
            GUI.color = presenter.GetHediffTypeColor(setting);
            Widgets.Label(nameRect, displayLabel);
            GUI.color = Color.white;

            // Tooltip with description
            string description = presenter.GetHediffDescription(hediffName);
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

            // Healing rate slider + input (range: 0.0001 - 0.1)
            float rateSliderWidth = 180f;
            Rect rateRect = new Rect(curX, centerY - 2f, rateSliderWidth, 28f);
            float displayRate = setting.HasCustomHealingRate
                ? setting.healingRate
                : Eternal_Mod.GetSettings().baseHealingRate;
            // Show "G:" prefix for global rate, rate value with 4 decimals
            string rateLabel = setting.HasCustomHealingRate ? $"{displayRate:F4}" : $"G:{displayRate:F4}";
            string rateTooltip = setting.HasCustomHealingRate
                ? $"Custom rate: {displayRate:F4} (overrides global)\nCost: 250 severity = 1 nutrition"
                : $"Using global rate: {displayRate:F4}\nCost: 250 severity = 1 nutrition";

            float newRate = DrawCompactSliderWithInput(rateRect, displayRate, 0.0001f, 0.1f, rateLabel, rateTooltip);
            if (Math.Abs(newRate - displayRate) > 0.00001f)
            {
                setting.healingRate = newRate;
            }
            curX += rateSliderWidth + 10f;

            // Nutrition cost slider + input
            float nutritionSliderWidth = 140f;
            Rect nutritionRect = new Rect(curX, centerY - 2f, nutritionSliderWidth, 28f);
            string nutritionLabel = $"{setting.nutritionCostMultiplier:F1}x";
            string nutritionTooltip = $"Nutrition cost multiplier: {setting.nutritionCostMultiplier:F2}x";

            float newNutrition = DrawCompactSliderWithInput(nutritionRect, setting.nutritionCostMultiplier, 0.1f, 5f, nutritionLabel, nutritionTooltip);
            if (Math.Abs(newNutrition - setting.nutritionCostMultiplier) > 0.01f)
            {
                setting.nutritionCostMultiplier = newNutrition;
            }

            // Reset button
            Rect resetRect = new Rect(rect.width - 35f, centerY, 30f, 24f);
            TooltipHandler.TipRegion(resetRect, "Reset to defaults");
            if (Widgets.ButtonText(resetRect, "↺"))
            {
                presenter.OnResetSingleHediff(hediffName);
            }

            Text.Anchor = TextAnchor.UpperLeft;
        }

        /// <summary>
        /// Draws a compact slider with inline numeric input.
        /// Uses 4 decimal places for precision.
        /// </summary>
        private float DrawCompactSliderWithInput(Rect rect, float value, float min, float max, string label, string tooltip)
        {
            float sliderWidth = rect.width * 0.6f;
            float inputWidth = rect.width * 0.35f;

            Rect sliderRect = new Rect(rect.x, rect.y, sliderWidth, rect.height);
            Rect inputRect = new Rect(rect.x + sliderWidth + 5f, rect.y + 2f, inputWidth, rect.height - 4f);

            // Slider
            float sliderValue = Widgets.HorizontalSlider(sliderRect, value, min, max, true, label);

            // Numeric input (4 decimal places for precision)
            string inputText = value.ToString("F4");
            string newInputText = Widgets.TextField(inputRect, inputText);

            float returnValue = value;
            if (newInputText != inputText && float.TryParse(newInputText, out float parsedValue))
            {
                returnValue = Mathf.Clamp(parsedValue, min, max);
            }
            else if (Math.Abs(sliderValue - value) > 0.00001f)
            {
                returnValue = sliderValue;
            }

            if (!string.IsNullOrEmpty(tooltip) && Mouse.IsOver(rect))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }

            return returnValue;
        }

        // NOTE: DrawDetailedSettings removed - replaced by compact single-row layout

        #endregion

        #region Advanced Tab

        private void DrawAdvancedTabContent(Rect rect)
        {
            GUI.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
            GUI.color = Color.white;

            float curY = rect.y + 10f;

            Widgets.Label(new Rect(rect.x + 10f, curY, rect.width - 20f, 25f), "Global Healing Settings:");
            curY += 35f;

            bool autoHeal = Eternal_Settings.instance.autoHealEnabled;
            Rect autoHealRect = new Rect(rect.x + 10f, curY, rect.width - 20f, 25f);
            CheckboxLabeledWithTooltip(autoHealRect, "Auto-heal on Resurrection", ref autoHeal,
                "Automatically heal configured hediffs when an Eternal pawn resurrects.");
            Eternal_Settings.instance.autoHealEnabled = autoHeal;
        }

        #endregion

        #region Bulk Tab

        private void DrawBulkTabContent(Rect rect)
        {
            GUI.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
            GUI.color = Color.white;

            float curY = rect.y + 10f;

            Widgets.Label(new Rect(rect.x + 10f, curY, rect.width - 20f, 25f), "Bulk Operations Template:");
            curY += 35f;

            DrawTemplateSettings(new Rect(rect.x + 10f, curY, rect.width - 20f, 250f));
            curY += 270f;

            Rect applySelectedRect = new Rect(rect.x + 10f, curY, 200f, 30f);
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

            Rect applyAllRect = new Rect(rect.x + 220f, curY, 200f, 30f);
            if (Widgets.ButtonText(applyAllRect, "Apply to All"))
            {
                int count = presenter.OnApplyToAll();
                Messages.Message($"Applied template settings to {count} hediffs.", MessageTypeDefOf.PositiveEvent);
            }

            Rect resetAllRect = new Rect(rect.x + 430f, curY, 150f, 30f);
            if (Widgets.ButtonText(resetAllRect, "Reset All to Default"))
            {
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
            float columnWidth = rect.width / 2;

            // Left column
            Widgets.Label(new Rect(curX, curY, columnWidth, 20f), "Template Settings:");
            curY += 25f;

            // Single "Heal" toggle (simplified from dual Enable + Can Heal)
            Rect healRect = new Rect(curX, curY, columnWidth, 20f);
            CheckboxLabeledWithTooltip(healRect, "Heal", ref template.canHeal,
                "Template setting for enabling healing. When applied, matched hediffs will heal.");
            curY += 25f;

            Rect cureRect = new Rect(curX, curY, columnWidth, 20f);
            CheckboxLabeledWithTooltip(cureRect, "Require Cure", ref template.requireCureToResurrect,
                "Template setting for requiring hediff cure before resurrection.");
            curY += 25f;

            Rect noThresholdRect = new Rect(curX, curY, columnWidth, 20f);
            CheckboxLabeledWithTooltip(noThresholdRect, "Instant Healing", ref template.noThreshold,
                "Template setting for instant healing (no severity threshold).");
            curY += 25f;

            // Template healing rate (always sets a custom value when applied)
            float templateRate = template.HasCustomHealingRate
                ? template.healingRate
                : Eternal_Mod.GetSettings().baseHealingRate;
            string templateRateLabel = template.HasCustomHealingRate
                ? $"Heal Rate: {templateRate:F3}"
                : $"Heal Rate: Global ({templateRate:F3})";

            float templateSliderWidth = template.HasCustomHealingRate ? columnWidth - 70f : columnWidth - 20f;
            Rect healRateRect = new Rect(curX, curY, templateSliderWidth, 20f);
            float newTemplateRate = DrawSliderWithNumericInput(
                healRateRect, templateRate, 0.001f, 0.1f,
                templateRateLabel,
                "Template healing rate. When applied, this rate will override global for each hediff.");

            // If user changed the value, set custom rate
            if (newTemplateRate != templateRate)
                template.healingRate = newTemplateRate;

            // Reset button (only if custom)
            if (template.HasCustomHealingRate)
            {
                Rect resetTemplateRect = new Rect(curX + templateSliderWidth + 5f, curY, 45f, 20f);
                if (Widgets.ButtonText(resetTemplateRect, "Reset"))
                    template.ResetToGlobalRate();
            }

            // Right column
            curX += columnWidth;
            curY = rect.y + 35f;

            Rect permanentRect = new Rect(curX, curY, columnWidth, 20f);
            CheckboxLabeledWithTooltip(permanentRect, "Heal Permanent", ref template.healPermanentInjuries,
                "Template setting for healing permanent injuries.");
            curY += 25f;

            Rect scarsRect = new Rect(curX, curY, columnWidth, 20f);
            CheckboxLabeledWithTooltip(scarsRect, "Heal Scars", ref template.healScars,
                "Template setting for scar removal.");
            curY += 25f;

            Rect nutritionRect = new Rect(curX, curY, columnWidth - 20f, 20f);
            template.nutritionCostMultiplier = DrawSliderWithNumericInput(
                nutritionRect, template.nutritionCostMultiplier, 0.1f, 5f,
                $"Nutrition: {template.nutritionCostMultiplier:F2}x",
                "Template nutrition cost multiplier.");
        }

        #endregion

        #region Helper Methods

        private float DrawSliderWithNumericInput(Rect rect, float value, float min, float max, string label, string tooltip = null)
        {
            float sliderWidth = rect.width * 0.70f;
            float inputWidth = rect.width * 0.25f;
            float gapWidth = rect.width * 0.05f;

            Rect sliderRect = new Rect(rect.x, rect.y, sliderWidth, rect.height);
            Rect inputRect = new Rect(rect.x + sliderWidth + gapWidth, rect.y, inputWidth, rect.height);

            float sliderValue = Widgets.HorizontalSlider(sliderRect, value, min, max, true, label, null, null);

            string inputText = value.ToString("F2");
            string newInputText = Widgets.TextField(inputRect, inputText);

            float returnValue = value;
            if (newInputText != inputText && float.TryParse(newInputText, out float parsedValue))
            {
                returnValue = Mathf.Clamp(parsedValue, min, max);
            }
            else if (Math.Abs(sliderValue - value) > 0.001f)
            {
                returnValue = sliderValue;
            }

            if (!string.IsNullOrEmpty(tooltip) && Mouse.IsOver(rect))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }

            return returnValue;
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
}
