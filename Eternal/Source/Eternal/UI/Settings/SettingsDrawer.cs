// Relative Path: Eternal/Source/Eternal/UI/Settings/SettingsDrawer.cs
// Creation Date: 01-01-2025
// Last Edit: 12-03-2026
// Author: 0Shard
// Description: UI drawing methods for Eternal mod settings. Features tab-based layout,
//              styled section headers with reset buttons, info boxes, live preview stats,
//              inline-editable slider values (click to type), and comprehensive tooltips.
//              Healing rate displayed in ratio format: 250 : 1 (severity : nutrition).
//              v1.0.1: Added Effects tab with consciousness buff, mood buff, population cap sections.

using System;
using UnityEngine;
using Verse;
using Eternal.UI.HediffSettings;
using Eternal.Extensions;
using Eternal.DI;

namespace Eternal.UI.Settings
{
    /// <summary>
    /// Enumeration of settings tabs for navigation.
    /// </summary>
    public enum SettingsTab
    {
        Core,        // General + Healing
        Economy,     // Resources + Food Debt
        Effects,     // Consciousness buff, mood buff, population cap
        Performance, // Tick rates and intervals
        Advanced,    // Hediff Manager + Map Protection
        Status       // Calculated statistics & previews
    }

    /// <summary>
    /// Helper class for calculating live preview values from settings.
    /// </summary>
    public static class SettingsPreview
    {
        public static string TickInterval(int ticks) => $"~{ticks / 60f:F1} seconds";
        public static string MapProtection(int ticks) => $"{ticks / 60f:F1}s after resurrection";

        /// <summary>
        /// Calculates in-game time to fully heal a body part.
        /// Formula: healingTicks = 1.0 / baseRate, then convert to in-game time.
        /// The body part HP cancels out due to severity scaling, so all body parts
        /// at the same damage percentage heal in the same time.
        /// </summary>
        public static string HealingTime(float baseRate, int normalTickRate)
        {
            // Ticks needed to heal 100% damage (severity ratio of 1.0)
            float healingCycles = 1.0f / baseRate;
            float gameTicks = healingCycles * normalTickRate;

            // Convert to in-game time (2500 ticks = 1 hour)
            float hours = gameTicks / 2500f;

            if (hours < 1f)
            {
                float minutes = hours * 60f;
                return $"Full heal: ~{minutes:F0} minutes";
            }
            else if (hours < 24f)
            {
                return $"Full heal: ~{hours:F1} hours";
            }
            else
            {
                float days = hours / 24f;
                return $"Full heal: ~{days:F1} days";
            }
        }
    }

    /// <summary>
    /// Calculates status values based on current settings for the Status tab.
    /// Formulas match actual healing code in EternalHediffHealer.cs and ScarCostCalculator.cs.
    /// </summary>
    public static class StatusCalculator
    {
        #region Healing Times

        /// <summary>
        /// Injury heal time: (1.0 / baseRate) × normalTickRate / 2500 hours
        /// Source: EternalHediffHealer.cs:154 - healingAmount = effectiveRate (baseHealingRate)
        /// </summary>
        public static string InjuryHealTime(float baseRate, int normalTickRate)
        {
            // Unified formula: cycles = severity / baseRate
            float cycles = 1.0f / baseRate;
            float gameTicks = cycles * normalTickRate;
            float hours = gameTicks / 2500f;
            return FormatTime(hours);
        }

        /// <summary>
        /// Scar heal time: (1.0 / baseRate) × rareTickRate / 2500 hours
        /// Source: ScarCostCalculator.cs - unified formula matching injury healing
        /// Scars heal at the same rate as injuries, just on rareTickRate instead of normalTickRate.
        /// </summary>
        public static string ScarHealTime(float baseRate, int rareTickRate)
        {
            // Unified formula: same as injury, just different tick interval
            float cycles = 1.0f / baseRate;
            float gameTicks = cycles * rareTickRate;
            float hours = gameTicks / 2500f;
            return FormatTime(hours);
        }

        /// <summary>
        /// Disease heal time with stage multiplier.
        /// Source: HealingConstants.cs - STAGE_SPEED_MULTIPLIERS
        /// </summary>
        public static string DiseaseHealTime(float baseRate, int normalTickRate, int stage)
        {
            float[] stageMultipliers = { 1.0f, 0.8f, 0.6f, 0.4f, 0.2f };
            float mult = stageMultipliers[Math.Min(stage, 4)];
            // Unified formula: cycles = severity / (baseRate × stageMultiplier)
            float cycles = 1.0f / (baseRate * mult);
            float gameTicks = cycles * normalTickRate;
            float hours = gameTicks / 2500f;
            return FormatTime(hours);
        }

        /// <summary>
        /// Regrowth phase time. Each phase progresses from 0→1.0 using baseRate per rare tick.
        /// Formula: 1.0 / baseRate rare ticks per phase.
        /// </summary>
        public static string RegrowthPhaseTime(float baseRate, int rareTickRate)
        {
            // Each phase requires progress from 0 to 1.0
            // At baseRate per rare tick, cycles = 1.0 / baseRate
            float rareTicks = 1.0f / baseRate;
            float gameTicks = rareTicks * rareTickRate;
            float hours = gameTicks / 2500f;
            return FormatTime(hours);
        }

        /// <summary>
        /// Full limb regrowth = 4 phases × (1.0 / baseRate) each.
        /// </summary>
        public static string FullLimbRegrowth(float baseRate, int rareTickRate)
        {
            // 4 phases, each requiring 1.0 / baseRate rare ticks
            float rareTicks = 4.0f / baseRate;
            float gameTicks = rareTicks * rareTickRate;
            float hours = gameTicks / 2500f;
            return FormatTime(hours);
        }

        #endregion

        #region Nutrition Costs

        /// <summary>
        /// Nutrition cost per normal tick (injuries).
        /// Source: EternalHediffHealer.cs - cost proportional to healing performed
        /// </summary>
        public static string NormalTickCost(float baseRate, float nutritionMult)
        {
            // Cost = healing amount × nutritionMultiplier
            float cost = baseRate * nutritionMult;
            return $"{cost:F4} nutrition";
        }

        /// <summary>
        /// Nutrition cost per rare tick (scars, regrowth).
        /// Same formula as injuries (unified system).
        /// </summary>
        public static string RareTickCost(float baseRate, float nutritionMult)
        {
            // Unified formula: same cost calculation as injuries
            float cost = baseRate * nutritionMult;
            return $"{cost:F4} nutrition";
        }

        /// <summary>
        /// Full injury heal cost (severity 1.0).
        /// </summary>
        public static string FullInjuryCost(float baseRate, float nutritionMult)
        {
            // Cost per tick × number of ticks
            float costPerTick = baseRate * nutritionMult;
            float cycles = 1.0f / baseRate;
            float total = cycles * costPerTick;
            return $"~{total:F2} nutrition";
        }

        /// <summary>
        /// Full scar heal cost (severity 1.0).
        /// Same as injury (unified system).
        /// </summary>
        public static string FullScarCost(float baseRate, float nutritionMult)
        {
            // Unified formula: same cost calculation as injuries
            float costPerTick = baseRate * nutritionMult;
            float cycles = 1.0f / baseRate;
            float total = cycles * costPerTick;
            return $"~{total:F2} nutrition";
        }

        /// <summary>
        /// Resurrection cost - capped at 2.0 scaled with pawn nutrition or body size.
        /// </summary>
        public static string ResurrectionCost(float pawnNutritionCap)
        {
            float maxCost = 2.0f * pawnNutritionCap;
            return $"<={maxCost:F2} nutrition";
        }

        #endregion

        #region Food Debt

        /// <summary>
        /// Maximum debt capacity.
        /// Source: UnifiedFoodDebtManager.cs:260
        /// </summary>
        public static string MaxDebt(float nutritionCap, float maxDebtMult)
        {
            return $"{nutritionCap * maxDebtMult:F1} nutrition";
        }

        /// <summary>
        /// Full injuries that can be covered by max debt.
        /// </summary>
        public static string InjuriesCoveredByDebt(float maxDebt, float nutritionMult)
        {
            float costPerInjury = 1.0f * nutritionMult;
            float count = maxDebt / costPerInjury;
            return $"~{count:F0} injuries";
        }

        /// <summary>
        /// Full scars that can be covered by max debt.
        /// </summary>
        public static string ScarsCoveredByDebt(float maxDebt, float nutritionMult)
        {
            float costPerScar = 1.0f * nutritionMult;
            float count = maxDebt / costPerScar;
            return $"~{count:F0} scars";
        }

        #endregion

        #region Helpers

        private static string FormatTime(float hours)
        {
            if (hours < 1f) return $"~{hours * 60f:F0} minutes";
            if (hours < 24f) return $"~{hours:F1} hours";
            return $"~{hours / 24f:F1} days";
        }

        #endregion
    }

    /// <summary>
    /// Handles all UI drawing for Eternal mod settings.
    /// Features tab-based navigation, per-section reset buttons, and live preview stats.
    /// Separated from Eternal_Settings to follow single responsibility principle.
    /// </summary>
    public class SettingsDrawer
    {
        private readonly Eternal_Settings settings;
        private Vector2 scrollPosition = Vector2.zero;
        private SettingsTab currentTab = SettingsTab.Core;

        // Inline edit state for slider values
        private string activeEditControlName = null;  // null = no slider in edit mode
        private string editBuffer = "";               // Current text being edited

        public SettingsDrawer(Eternal_Settings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #region Main Drawing

        /// <summary>
        /// Draws the settings window contents with tab-based navigation.
        /// </summary>
        public void DoWindowContents(Rect inRect)
        {
            const float tabHeight = 35f;
            const float tabGap = 10f;

            // Draw tabs at top
            Rect tabsRect = new Rect(inRect.x, inRect.y, inRect.width, tabHeight);
            DrawSettingsTabs(tabsRect);

            // Content area below tabs
            Rect contentRect = new Rect(inRect.x, inRect.y + tabHeight + tabGap, inRect.width, inRect.height - tabHeight - tabGap);

            float contentHeight = CalculateTabContentHeight(currentTab);
            Rect viewRect = new Rect(0, 0, contentRect.width - 16f, contentHeight);

            Widgets.BeginScrollView(contentRect, ref scrollPosition, viewRect);

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);

            DrawCurrentTabContent(listing);

            listing.End();
            Widgets.EndScrollView();

            SettingsValidator.ValidateSettings(settings);
        }

        /// <summary>
        /// Draws the tab buttons at the top of the settings window.
        /// </summary>
        private void DrawSettingsTabs(Rect rect)
        {
            float tabWidth = rect.width / 6f;
            float x = rect.x;

            DrawTabButton(new Rect(x, rect.y, tabWidth, rect.height), "Core", SettingsTab.Core);
            x += tabWidth;
            DrawTabButton(new Rect(x, rect.y, tabWidth, rect.height), "Economy", SettingsTab.Economy);
            x += tabWidth;
            DrawTabButton(new Rect(x, rect.y, tabWidth, rect.height), "Effects", SettingsTab.Effects);
            x += tabWidth;
            DrawTabButton(new Rect(x, rect.y, tabWidth, rect.height), "Performance", SettingsTab.Performance);
            x += tabWidth;
            DrawTabButton(new Rect(x, rect.y, tabWidth, rect.height), "Advanced", SettingsTab.Advanced);
            x += tabWidth;
            DrawTabButton(new Rect(x, rect.y, tabWidth, rect.height), "Status", SettingsTab.Status);
        }

        /// <summary>
        /// Draws a single tab button.
        /// </summary>
        private void DrawTabButton(Rect rect, string label, SettingsTab tab)
        {
            bool isSelected = currentTab == tab;

            // Background color based on selection
            if (isSelected)
            {
                GUI.color = new Color(0.3f, 0.3f, 0.3f, 1f);
                GUI.DrawTexture(rect, BaseContent.WhiteTex);
                GUI.color = Color.white;
            }
            else
            {
                GUI.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
                GUI.DrawTexture(rect, BaseContent.WhiteTex);
                GUI.color = Color.white;
            }

            // Border
            GUI.color = isSelected ? new Color(0.9f, 0.85f, 0.7f) : Color.gray;
            Widgets.DrawBox(rect, 1);
            GUI.color = Color.white;

            // Label
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = isSelected ? new Color(0.9f, 0.85f, 0.7f) : Color.white;
            Widgets.Label(rect, label);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            // Click detection
            if (Widgets.ButtonInvisible(rect))
            {
                currentTab = tab;
                scrollPosition = Vector2.zero; // Reset scroll on tab change
                activeEditControlName = null;  // Exit any inline edit mode
                editBuffer = "";
            }
        }

        /// <summary>
        /// Draws content for the currently selected tab.
        /// </summary>
        private void DrawCurrentTabContent(Listing_Standard listing)
        {
            switch (currentTab)
            {
                case SettingsTab.Core:
                    DrawGeneralSettings(listing);
                    DrawHealingSettings(listing);
                    break;
                case SettingsTab.Economy:
                    DrawResourceSettings(listing);
                    DrawFoodDebtSettings(listing);
                    break;
                case SettingsTab.Effects:
                    DrawEffectsTab(listing);
                    break;
                case SettingsTab.Performance:
                    DrawPerformanceSettings(listing);
                    break;
                case SettingsTab.Advanced:
                    DrawAdvancedHediffSettings(listing);
                    DrawMapProtectionSettings(listing);
                    break;
                case SettingsTab.Status:
                    DrawStatusTab(listing);
                    break;
            }
        }

        #endregion

        #region Section Drawing

        private void DrawGeneralSettings(Listing_Standard listing)
        {
            DrawSectionHeader(listing, "General", () => settings.ResetGeneralSettings());

            CheckboxLabeledWithTooltip(listing, "Enable Eternal Mod", ref settings.modEnabled,
                "Master switch to enable or disable all Eternal mod features.");
            CheckboxLabeledWithTooltip(listing, "Debug Mode", ref settings.debugMode,
                "Enable debug mode for detailed logging and diagnostic information.");
            SliderWithInlineEditInt(listing, "Logging Level", ref settings.loggingLevel, 0, 3,
                "Set logging verbosity:\n\n• 0 = Errors only\n• 1 = Warnings (default)\n• 2 = Info\n• 3 = Debug (very verbose)");

            listing.GapLine();
        }

        private void DrawHealingSettings(Listing_Standard listing)
        {
            DrawSectionHeader(listing, "Healing", () => settings.ResetHealingSettings());

            // Healing rate with ratio format display
            DrawHealingRateWithRatio(listing, ref settings.baseHealingRate);

            // Live preview for healing rate - shows in-game time and cost
            DrawPreviewLabel(listing, SettingsPreview.HealingTime(settings.baseHealingRate, settings.normalTickRate));
            DrawPreviewLabel(listing, $"A severity 50 wound costs {50f * settings.severityToNutritionRatio * settings.nutritionCostMultiplier:F2} nutrition");

            listing.Gap();
            CheckboxLabeledWithTooltip(listing, "Show Regrowth Effects", ref settings.showRegrowthEffects,
                "Display visual effects during body part regrowth.");
            CheckboxLabeledWithTooltip(listing, "Show Regrowth Progress", ref settings.showRegrowthProgress,
                "Show progress bars and notifications for ongoing regrowth.");

            listing.GapLine();
        }

        /// <summary>
        /// Draws the healing rate slider with ratio format display.
        /// Uses the working SliderWithInlineEdit pattern for proper text field handling.
        /// Shows cost ratio from SettingsDefaults.GetSeverityToNutritionRatioDisplay().
        /// </summary>
        private void DrawHealingRateWithRatio(Listing_Standard listing, ref float healingRate)
        {
            // Tooltip for healing rate
            string tooltip = "Severity reduced per healing tick.\n\n" +
                $"Cost formula: {SettingsDefaults.GetSeverityToNutritionRatioDisplay()} (severity : nutrition)\n\n" +
                "• 0.01 = Very slow\n" +
                "• 0.5 = Moderate\n" +
                "• 1.8 = Fast (default)\n" +
                "• 3.0 = Very fast\n\n" +
                "Individual hediffs can override this.";

            // Use the working SliderWithInlineEdit pattern
            SliderWithInlineEdit(listing, "Base Healing Rate", ref healingRate, 0.01f, 3.0f, tooltip);

            // Display cost ratio on separate line
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.6f, 0.8f, 0.6f); // Light green for emphasis
            listing.Label($"    Cost ratio: {SettingsDefaults.GetSeverityToNutritionRatioDisplay()} (severity : nutrition)");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawResourceSettings(Listing_Standard listing)
        {
            DrawSectionHeader(listing, "Resources", () => settings.ResetResourceSettings());

            SliderWithInlineEdit(listing, "Nutrition Cost Multiplier", ref settings.nutritionCostMultiplier, 0.1f, 5.0f,
                "Multiplier for nutrition cost of healing and regrowth.\n1.0 = normal cost, 2.0 = twice as expensive.");

            CheckboxLabeledWithTooltip(listing, "Pause on Resource Depletion", ref settings.pauseOnResourceDepletion,
                "Automatically pause healing when nutrition drops below threshold.");

            SliderWithInlineEdit(listing, "Min Nutrition Threshold", ref settings.minimumNutritionThreshold, 0.01f, 1.0f,
                "Minimum nutrition level before pausing healing/regrowth.");

            CheckboxLabeledWithTooltip(listing, "Allow Resource Borrowing", ref settings.allowResourceBorrowing,
                "Allow pawns to temporarily go into food debt for critical healing.");

            listing.GapLine();
        }

        private void DrawFoodDebtSettings(Listing_Standard listing)
        {
            DrawSectionHeader(listing, "Food Debt", () => settings.ResetFoodDebtSettings());

            // Info box explaining the new concept
            DrawInfoBox(listing, "Healing drains food bar directly until threshold. Additional costs go to debt. Debt gradually drains food bar until repaid.");

            // Food drain threshold
            SliderWithInlineEdit(listing, "Food Drain Threshold", ref settings.foodDrainThreshold, 0.05f, 0.5f,
                "Food level below which healing costs go to debt instead of draining food.\n\n" +
                "• 0.15 = UrgentlyHungry level (default)\n• 0.25 = Hungry level\n• 0.05 = Nearly starving\n\n" +
                "Protects pawns from starving during healing.");
            DrawPreviewLabel(listing, $"Stop draining at {settings.foodDrainThreshold * 100:F0}% food");

            listing.Gap();

            // Max debt multiplier
            SliderWithInlineEdit(listing, "Max Debt Multiplier", ref settings.maxDebtMultiplier, 1.0f, 10.0f,
                "Maximum debt as a multiplier of pawn's nutrition capacity.\n\n" +
                "• 5.0 = Default (can owe 5 meals worth)\n• 2.0 = Conservative\n• 10.0 = Very generous");
            DrawPreviewLabel(listing, $"Max debt: {settings.maxDebtMultiplier:F0}× nutrition capacity");

            listing.Gap();
            DrawSubsectionLabel(listing, "Debt Repayment (Tick-Based Drain)");

            // Drain rate range
            SliderWithInlineEdit(listing, "Min Drain Rate", ref settings.minDebtDrainRate, 0.00001f, 0.001f,
                "Nutrition drained per tick when debt is low (0%).\nLower = slower repayment at low debt.", 5);

            SliderWithInlineEdit(listing, "Max Drain Rate", ref settings.maxDebtDrainRate, 0.0001f, 0.01f,
                "Nutrition drained per tick when debt is high (100%).\nHigher = faster repayment at max debt.", 4);

            // Preview for drain rates
            float hourlyDrainMin = settings.minDebtDrainRate * 2500f;
            float hourlyDrainMax = settings.maxDebtDrainRate * 2500f;
            DrawPreviewLabel(listing, $"Drain range: {hourlyDrainMin:F2} - {hourlyDrainMax:F2} nutrition/hour");

            listing.GapLine();
        }

        private void DrawPerformanceSettings(Listing_Standard listing)
        {
            DrawSectionHeader(listing, "Performance", () => settings.ResetPerformanceSettings());

            SliderWithInlineEditInt(listing, "Normal Tick Rate", ref settings.normalTickRate, 30, 250,
                "Ticks between healing checks for injuries.\n\n• Lower = faster healing, more CPU\n• Higher = slower healing, better performance\n\n60 ticks ≈ 1 second. Default: 60");

            // Live preview for normal tick rate
            DrawPreviewLabel(listing, SettingsPreview.TickInterval(settings.normalTickRate));

            SliderWithInlineEditInt(listing, "Rare Tick Rate", ref settings.rareTickRate, 100, 1000,
                "Ticks between regrowth/scar healing checks.\n\n• Lower = faster regrowth, more CPU\n• Higher = slower regrowth, better performance\n\n250 ticks ≈ 4 seconds. Default: 250");

            // Live preview for rare tick rate
            DrawPreviewLabel(listing, SettingsPreview.TickInterval(settings.rareTickRate));

            listing.Gap();
            DrawSubsectionLabel(listing, "System Check Intervals");

            SliderWithInlineEditInt(listing, "Trait Check", ref settings.traitCheckInterval, 1000, 15000,
                "How often to verify trait-hediff consistency.\n5000 = default (~83 seconds)");
            SliderWithInlineEditInt(listing, "Corpse Check", ref settings.corpseCheckInterval, 250, 5000,
                "How often to check corpse preservation.\n1000 = default (~17 seconds)");
            SliderWithInlineEditInt(listing, "Map Check", ref settings.mapCheckInterval, 100, 2000,
                "How often to check map protection.\n500 = default (~8 seconds)");

            listing.GapLine();
        }

        private void DrawAdvancedHediffSettings(Listing_Standard listing)
        {
            DrawSectionHeader(listing, "Advanced Hediff Healing", () => settings.ResetAdvancedHediffSettings());

            CheckboxLabeledWithTooltip(listing, "Enable Individual Hediff Control", ref settings.enableIndividualHediffControl,
                "Enable per-hediff configuration system for fine-grained healing control.");
            CheckboxLabeledWithTooltip(listing, "Auto-heal on Resurrection", ref settings.autoHealEnabled,
                "Automatically heal configured hediffs when Eternal pawns resurrect.");

            listing.Gap();

            // Show hediff manager summary
            // configuredCount = hediffs with custom settings, totalCount = all hediffs in game
            int configuredCount = settings.hediffManager?.GetConfiguredCount() ?? 0;
            int totalCount = settings.hediffManager?.GetTotalCount() ?? 0;
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            listing.Label($"    Configured: {configuredCount} / {totalCount} hediffs");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            Rect buttonRect = listing.GetRect(30f);
            if (Widgets.ButtonText(buttonRect, "Open Hediff Manager"))
            {
                Find.WindowStack.Add(new EternalHediffView(settings.hediffManager));
            }
            if (Mouse.IsOver(buttonRect))
            {
                TooltipHandler.TipRegion(buttonRect,
                    "Configure individual hediff healing behavior, speed, and conditions.");
            }
            listing.Gap(listing.verticalSpacing);

            listing.GapLine();
        }

        private void DrawMapProtectionSettings(Listing_Standard listing)
        {
            DrawSectionHeader(listing, "Map Protection", () => settings.ResetMapProtectionSettings());

            // Info box explaining the feature
            DrawInfoBox(listing, "Prevents temporary maps (raids, caravans, Odyssey ships) from closing while Eternal pawns are dead or resurrecting.");

            CheckboxLabeledWithTooltip(listing, "Enable Map Protection", ref settings.enableMapAnchors,
                "Prevents temporary maps from closing when Eternal pawns die.\nRequired for resurrection on temporary maps.");

            SliderWithInlineEditInt(listing, "Protection Duration", ref settings.anchorGracePeriodTicks, 60, 1200,
                "How long to keep temporary maps open after Eternal pawn resurrection.\n\n• 60 = 1 second (minimum)\n• 300 = 5 seconds (default)\n• 1200 = 20 seconds (maximum)");

            // Live preview for protection duration
            DrawPreviewLabel(listing, SettingsPreview.MapProtection(settings.anchorGracePeriodTicks));

            listing.Gap();
            DrawSubsectionLabel(listing, "Roof Collapse Protection");

            CheckboxLabeledWithTooltip(listing, "Enable Roof Collapse Protection", ref settings.enableRoofCollapseProtection,
                "Teleports Eternal corpses to safety when a mountain roof collapses.\nPrevents permanent loss of Eternal pawns in mines or collapsing structures.");
        }

        private void DrawStatusTab(Listing_Standard listing)
        {
            // Assume human baseline (1.0 nutrition cap, 1.0 body size)
            const float humanNutritionCap = 1.0f;
            const float humanBodySize = 1.0f;
            float maxDebt = humanNutritionCap * settings.maxDebtMultiplier;

            // Section 1: Healing Times
            DrawSectionHeaderNoReset(listing, "Healing Times");
            DrawStatusRow(listing, "Injury (100% damage)",
                StatusCalculator.InjuryHealTime(settings.baseHealingRate, settings.normalTickRate));
            DrawStatusRow(listing, "Scar (100% severity)",
                StatusCalculator.ScarHealTime(settings.baseHealingRate, settings.rareTickRate));
            DrawStatusRow(listing, "Disease (stage 0)",
                StatusCalculator.DiseaseHealTime(settings.baseHealingRate, settings.normalTickRate, 0));
            DrawStatusRow(listing, "Disease (stage 3)",
                StatusCalculator.DiseaseHealTime(settings.baseHealingRate, settings.normalTickRate, 3));
            DrawStatusRow(listing, "Regrowth (per phase)",
                StatusCalculator.RegrowthPhaseTime(settings.baseHealingRate, settings.rareTickRate));
            DrawStatusRow(listing, "Full Limb Regrowth",
                StatusCalculator.FullLimbRegrowth(settings.baseHealingRate, settings.rareTickRate));

            listing.GapLine();

            // Section 2: Nutrition Costs
            DrawSectionHeaderNoReset(listing, "Nutrition Costs");
            DrawStatusRow(listing, "Per normal tick",
                StatusCalculator.NormalTickCost(settings.baseHealingRate, settings.nutritionCostMultiplier));
            DrawStatusRow(listing, "Per rare tick",
                StatusCalculator.RareTickCost(settings.baseHealingRate, settings.nutritionCostMultiplier));
            DrawStatusRow(listing, "Full injury heal",
                StatusCalculator.FullInjuryCost(settings.baseHealingRate, settings.nutritionCostMultiplier));
            DrawStatusRow(listing, "Full scar heal",
                StatusCalculator.FullScarCost(settings.baseHealingRate, settings.nutritionCostMultiplier));
            DrawStatusRow(listing, "Full resurrection",
                StatusCalculator.ResurrectionCost(humanNutritionCap));

            listing.GapLine();

            // Section 3: Food Debt Capacity (for human pawn)
            DrawSectionHeaderNoReset(listing, $"Food Debt Capacity (Human, {humanBodySize:F1} body size)");
            DrawStatusRow(listing, "Pawn nutrition cap",
                $"{humanNutritionCap:F1} nutrition");
            DrawStatusRow(listing, $"Maximum debt (x{settings.maxDebtMultiplier:F0})",
                StatusCalculator.MaxDebt(humanNutritionCap, settings.maxDebtMultiplier));
            DrawStatusRow(listing, "Can heal injuries",
                StatusCalculator.InjuriesCoveredByDebt(maxDebt, settings.nutritionCostMultiplier));
            DrawStatusRow(listing, "Can heal scars",
                StatusCalculator.ScarsCoveredByDebt(maxDebt, settings.nutritionCostMultiplier));

        }

        private void DrawEffectsTab(Listing_Standard listing)
        {
            // Reset All Effects button at the top for quick access
            Rect resetAllRect = listing.GetRect(30f);
            if (Widgets.ButtonText(resetAllRect, "Reset All Effects"))
            {
                settings.ResetEffectsSettings();
            }
            listing.Gap(8f);

            // --- Consciousness Buff ---
            DrawSectionHeader(listing, "Consciousness Buff", () => settings.ResetConsciousnessBuffSettings());
            DrawInfoBox(listing, "Multiplies consciousness capacity for Eternal pawns. Minimum 1.0x ensures no debuff is possible.");

            CheckboxLabeledWithTooltip(listing, "Enable Consciousness Buff", ref settings.consciousnessBuffEnabled,
                "Toggle the consciousness capacity multiplier for all Eternal pawns.");

            GUI.enabled = settings.consciousnessBuffEnabled;
            SliderWithInlineEdit(listing, "Consciousness Multiplier",
                ref settings.consciousnessMultiplier, 1.0f, 10.0f,
                "Consciousness capacity multiplier. 3.0x = triple consciousness. Steps: 0.5x.");
            if (settings.consciousnessBuffEnabled)
            {
                // Round to nearest 0.5 step so slider snaps cleanly (1.0, 1.5, 2.0 ... 10.0)
                settings.consciousnessMultiplier = Mathf.Round(settings.consciousnessMultiplier * 2f) / 2f;
            }
            GUI.enabled = true;

            // --- Mood Buff ---
            DrawSectionHeader(listing, "Mood Buff", () => settings.ResetMoodBuffSettings());
            DrawInfoBox(listing, "Grants a permanent mood bonus to all Eternal pawns. The thought appears in the Needs tab when enabled.");

            CheckboxLabeledWithTooltip(listing, "Enable Mood Buff", ref settings.moodBuffEnabled,
                "Toggle the permanent mood bonus for all Eternal pawns.");

            GUI.enabled = settings.moodBuffEnabled;
            SliderWithInlineEditInt(listing, "Mood Buff Value",
                ref settings.moodBuffValue, 1, 200,
                "Mood bonus amount. Default: 40. Higher values make Eternals happier.");
            GUI.enabled = true;

            // --- Population Cap ---
            DrawSectionHeader(listing, "Population Cap", () => settings.ResetPopulationCapSettings());
            DrawInfoBox(listing, "Counts living Eternals and corpses being healed toward the cap. When the cap is reached, new Eternal Elixir use is blocked.");

            CheckboxLabeledWithTooltip(listing, "Enable Population Cap", ref settings.populationCapEnabled,
                "Toggle the maximum number of Eternals allowed.");

            GUI.enabled = settings.populationCapEnabled;
            int currentCount = GetCurrentEternalCount();
            // Dynamic minimum: cannot set cap below current Eternal count when a game is loaded
            int sliderMin = Math.Max(1, currentCount);
            SliderWithInlineEditInt(listing, "Maximum Eternals",
                ref settings.populationCap, sliderMin, 30,
                "Maximum number of Eternals allowed. Cannot be set below current count.");
            GUI.enabled = true;

            // Live count display
            if (Current.Game != null)
            {
                int livingCount = PawnExtensions.GetAllLivingEternalPawnsCached()?.Count ?? 0;
                int healingCount = EternalServiceContainer.Instance?.CorpseManager?.GetHealingCorpseCount() ?? 0;
                int totalCount = livingCount + healingCount;
                listing.Label($"Current: {totalCount} / {settings.populationCap} ({livingCount} living, {healingCount} healing)");
            }
            else
            {
                GUI.color = Color.gray;
                listing.Label("Current: N/A (no active game)");
                GUI.color = Color.white;
            }
        }

        /// <summary>
        /// Returns the total count of living Eternals plus actively-healing corpses.
        /// Returns 0 when no game is loaded (main menu) to keep the population cap slider unclamped.
        /// </summary>
        private int GetCurrentEternalCount()
        {
            if (Current.Game == null) return 0;
            int livingCount = PawnExtensions.GetAllLivingEternalPawnsCached()?.Count ?? 0;
            int healingCount = EternalServiceContainer.Instance?.CorpseManager?.GetHealingCorpseCount() ?? 0;
            return livingCount + healingCount;
        }

        #endregion

        #region Helper Methods

        private void CheckboxLabeledWithTooltip(Listing_Standard listing, string label, ref bool checkOn, string tooltip)
        {
            Rect rect = listing.GetRect(Text.LineHeight);
            Widgets.CheckboxLabeled(rect, label, ref checkOn);

            if (!string.IsNullOrEmpty(tooltip) && Mouse.IsOver(rect))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }

            listing.Gap(listing.verticalSpacing);
        }

        #region Inline Edit Slider Controls

        /// <summary>
        /// Draws a clickable value display. Clicking enters edit mode.
        /// </summary>
        private void DrawDisplayMode(Rect rect, string controlName, string displayText)
        {
            // Dark background to indicate clickability
            GUI.color = new Color(0.25f, 0.25f, 0.25f, 0.8f);
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
            GUI.color = Color.white;

            // Gold border on hover
            if (Mouse.IsOver(rect))
            {
                GUI.color = new Color(0.9f, 0.85f, 0.7f);
                Widgets.DrawBox(rect, 1);
                GUI.color = Color.white;
            }

            // Centered value text
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, displayText);
            Text.Anchor = TextAnchor.UpperLeft;

            // Click to enter edit mode
            if (Widgets.ButtonInvisible(rect))
            {
                activeEditControlName = controlName;
                editBuffer = displayText;
            }
        }

        /// <summary>
        /// Draws an editable text field. Returns true when edit should be applied.
        /// </summary>
        private bool DrawEditMode(Rect rect, string controlName)
        {
            string textFieldName = controlName + "_tf";
            GUI.SetNextControlName(textFieldName);
            editBuffer = Widgets.TextField(rect, editBuffer);

            if (GUI.GetNameOfFocusedControl() != textFieldName)
                GUI.FocusControl(textFieldName);

            // Enter = confirm, Escape = cancel
            if (Event.current.type == EventType.KeyDown)
            {
                if (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                {
                    activeEditControlName = null;
                    Event.current.Use();
                    return true;  // Apply value
                }
                if (Event.current.keyCode == KeyCode.Escape)
                {
                    activeEditControlName = null;
                    Event.current.Use();
                    return false; // Cancel
                }
            }

            // Click outside = confirm
            if (Event.current.type == EventType.MouseDown && !rect.Contains(Event.current.mousePosition))
            {
                activeEditControlName = null;
                return true;
            }

            return false; // Still editing
        }

        /// <summary>
        /// Draws a float slider with inline-editable value display.
        /// Click the value to type directly; press Enter or click elsewhere to confirm.
        /// </summary>
        private void SliderWithInlineEdit(Listing_Standard listing, string label, ref float value,
            float min, float max, string tooltip, int decimals = 2)
        {
            string format = "F" + decimals;
            string controlName = $"Slider_{label}";
            bool isEditing = (activeEditControlName == controlName);

            Rect fullRect = listing.GetRect(22f);
            Rect labelRect = new Rect(fullRect.x, fullRect.y, 150f, fullRect.height);
            Rect valueRect = new Rect(fullRect.xMax - 80f, fullRect.y, 80f, fullRect.height);
            Rect sliderRect = new Rect(fullRect.x + 160f, fullRect.y, fullRect.width - 250f, fullRect.height);

            Widgets.Label(labelRect, label);

            float newVal = Widgets.HorizontalSlider(sliderRect, value, min, max, true);
            if (Math.Abs(newVal - value) > 0.0001f)
            {
                value = newVal;
                if (isEditing) activeEditControlName = null;
            }

            if (isEditing)
            {
                if (DrawEditMode(valueRect, controlName))
                    if (float.TryParse(editBuffer, out float parsed))
                        value = Mathf.Clamp(parsed, min, max);
            }
            else
            {
                DrawDisplayMode(valueRect, controlName, value.ToString(format));
            }

            if (!string.IsNullOrEmpty(tooltip) && Mouse.IsOver(fullRect))
                TooltipHandler.TipRegion(fullRect, tooltip);

            listing.Gap(listing.verticalSpacing);
        }

        /// <summary>
        /// Draws an int slider with inline-editable value display.
        /// Click the value to type directly; press Enter or click elsewhere to confirm.
        /// </summary>
        private void SliderWithInlineEditInt(Listing_Standard listing, string label, ref int value,
            int min, int max, string tooltip)
        {
            string controlName = $"SliderInt_{label}";
            bool isEditing = (activeEditControlName == controlName);

            Rect fullRect = listing.GetRect(22f);
            Rect labelRect = new Rect(fullRect.x, fullRect.y, 150f, fullRect.height);
            Rect valueRect = new Rect(fullRect.xMax - 80f, fullRect.y, 80f, fullRect.height);
            Rect sliderRect = new Rect(fullRect.x + 160f, fullRect.y, fullRect.width - 250f, fullRect.height);

            Widgets.Label(labelRect, label);

            int newVal = (int)Widgets.HorizontalSlider(sliderRect, value, min, max, true);
            if (newVal != value)
            {
                value = newVal;
                if (isEditing) activeEditControlName = null;
            }

            if (isEditing)
            {
                if (DrawEditMode(valueRect, controlName))
                    if (int.TryParse(editBuffer, out int parsed))
                        value = Mathf.Clamp(parsed, min, max);
            }
            else
            {
                DrawDisplayMode(valueRect, controlName, value.ToString());
            }

            if (!string.IsNullOrEmpty(tooltip) && Mouse.IsOver(fullRect))
                TooltipHandler.TipRegion(fullRect, tooltip);

            listing.Gap(listing.verticalSpacing);
        }

        #endregion

        /// <summary>
        /// Draws a styled section header with warm gold color and reset button.
        /// </summary>
        private void DrawSectionHeader(Listing_Standard listing, string title, Action resetAction)
        {
            listing.Gap(8f);
            Rect headerRect = listing.GetRect(Text.LineHeight + 4f);

            // Section title (left side, warm gold)
            Text.Font = GameFont.Medium;
            GUI.color = new Color(0.9f, 0.85f, 0.7f);
            Widgets.Label(headerRect.LeftPart(0.85f), title);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            // Reset button (right side, subtle gray)
            Rect resetRect = new Rect(headerRect.xMax - 50f, headerRect.y + 2f, 45f, headerRect.height - 4f);
            Text.Font = GameFont.Tiny;
            if (Widgets.ButtonText(resetRect, "Reset"))
            {
                resetAction?.Invoke();
            }
            if (Mouse.IsOver(resetRect))
            {
                TooltipHandler.TipRegion(resetRect, $"Reset {title} settings to defaults");
            }
            Text.Font = GameFont.Small;

            listing.Gap(4f);
        }

        /// <summary>
        /// Draws an indented subsection label.
        /// </summary>
        private void DrawSubsectionLabel(Listing_Standard listing, string title)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            listing.Label("    " + title);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            listing.Gap(2f);
        }

        /// <summary>
        /// Draws an info box with explanatory text for complex sections.
        /// </summary>
        private void DrawInfoBox(Listing_Standard listing, string text)
        {
            Rect boxRect = listing.GetRect(Text.CalcHeight(text, listing.ColumnWidth - 20f) + 10f);
            GUI.color = new Color(0.15f, 0.15f, 0.15f, 0.8f);
            GUI.DrawTexture(boxRect, BaseContent.WhiteTex);
            GUI.color = Color.white;

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.8f, 0.8f, 0.8f);
            Rect textRect = boxRect.ContractedBy(5f);
            Widgets.Label(textRect, text);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            listing.Gap(4f);
        }

        /// <summary>
        /// Draws a live preview label showing calculated values (light green, indented).
        /// </summary>
        private void DrawPreviewLabel(Listing_Standard listing, string preview)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.6f, 0.8f, 0.6f); // Light green
            listing.Label($"    → {preview}");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        /// <summary>
        /// Draws a section header without reset button (for read-only sections like Status).
        /// </summary>
        private void DrawSectionHeaderNoReset(Listing_Standard listing, string title)
        {
            listing.Gap(8f);
            Text.Font = GameFont.Medium;
            GUI.color = new Color(0.9f, 0.85f, 0.7f);
            listing.Label(title);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            listing.Gap(4f);
        }

        /// <summary>
        /// Draws a status row with label (gray) and value (light green).
        /// </summary>
        private void DrawStatusRow(Listing_Standard listing, string label, string value)
        {
            Rect rowRect = listing.GetRect(Text.LineHeight + 4f);

            // Label (left, gray)
            GUI.color = Color.gray;
            Widgets.Label(rowRect.LeftHalf(), label);

            // Value (right, light green)
            GUI.color = new Color(0.6f, 0.8f, 0.6f);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(rowRect.RightHalf(), value);
            Text.Anchor = TextAnchor.UpperLeft;

            GUI.color = Color.white;
        }

        #endregion

        #region Content Height Calculation

        /// <summary>
        /// Calculates content height for the specified tab.
        /// </summary>
        private float CalculateTabContentHeight(SettingsTab tab)
        {
            return tab switch
            {
                SettingsTab.Core => CalculateCoreTabHeight(),
                SettingsTab.Economy => CalculateEconomyTabHeight(),
                SettingsTab.Effects => CalculateEffectsTabHeight(),
                SettingsTab.Performance => CalculatePerformanceTabHeight(),
                SettingsTab.Advanced => CalculateAdvancedTabHeight(),
                SettingsTab.Status => CalculateStatusTabHeight(),
                _ => 500f
            };
        }

        private float CalculateCoreTabHeight()
        {
            float height = 0f;

            // General Settings (header + 3 controls + gap)
            height += 40f + 30f + 30f + 50f + 20f;

            // Healing Settings (header + slider + preview + gap + 2 checkboxes + gap)
            height += 40f + 70f + 20f + 10f + 30f + 30f + 20f;

            // Padding
            height += 30f;

            return height;
        }

        private float CalculateEconomyTabHeight()
        {
            float height = 0f;

            // Resource Settings (header + slider + checkbox + slider + checkbox + gap)
            height += 40f + 50f + 30f + 50f + 30f + 20f;

            // Food Debt Settings (header + info box + slider + preview + slider + preview + gap)
            height += 40f + 50f + 70f + 20f + 10f + 70f + 20f + 20f;

            // Padding
            height += 30f;

            return height;
        }

        private float CalculatePerformanceTabHeight()
        {
            float height = 0f;

            // Performance Settings (header + 2 sliders with previews + subsection + 3 sliders + gap)
            height += 40f + 50f + 20f + 50f + 20f + 30f + 50f + 50f + 50f + 20f;

            // Padding
            height += 30f;

            return height;
        }

        private float CalculateAdvancedTabHeight()
        {
            float height = 0f;

            // Advanced Hediff Healing (header + 2 checkboxes + summary + button + gap)
            height += 40f + 30f + 30f + 10f + 25f + 35f + 20f;

            // Map Protection Settings (header + info box + checkbox + slider + preview + gap + subsection + checkbox)
            height += 40f + 50f + 30f + 50f + 20f + 10f + 20f + 30f;

            // Padding
            height += 30f;

            return height;
        }

        private float CalculateStatusTabHeight()
        {
            float height = 0f;

            // Healing Times (header + 6 rows + gap)
            height += 40f + (6 * 25f) + 20f;

            // Nutrition Costs (header + 5 rows + gap)
            height += 40f + (5 * 25f) + 20f;

            // Food Debt Capacity (header + 4 rows)
            height += 40f + (4 * 25f);

            // Padding
            height += 30f;

            return height;
        }

        private float CalculateEffectsTabHeight()
        {
            float height = 0f;

            // Reset All Effects button + gap
            height += 30f + 8f;

            // Consciousness Buff (header + info box + checkbox + slider + gap)
            height += 40f + 50f + 30f + 50f + 10f;

            // Mood Buff (header + info box + checkbox + slider + gap)
            height += 40f + 50f + 30f + 50f + 10f;

            // Population Cap (header + info box + checkbox + slider + live count label + gap)
            height += 40f + 50f + 30f + 50f + 25f + 10f;

            // Padding
            height += 30f;

            return height;
        }

        #endregion
    }
}
