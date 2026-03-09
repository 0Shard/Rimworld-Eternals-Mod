// Relative Path: Eternal/Source/Eternal/Eternal_Mod.cs
// Creation Date: 28-10-2025
// Last Edit: 19-02-2026
// Author: 0Shard
// Description: Main mod class for Eternal mod, handles initialization and component registration with enhanced hediff healing system.
//              GetSettings() provides the single guaranteed-non-null entry point for settings access (SAFE-08).

using System;
using UnityEngine;
using Verse;
using RimWorld;
using Eternal.Utils;

namespace Eternal
{
    /// <summary>
    /// Main mod class for Eternal mod.
    /// Handles mod initialization and component registration with enhanced hediff healing system.
    /// </summary>
    public class Eternal_Mod : Mod
    {
        /// <summary>
        /// Mod settings instance.
        /// </summary>
        public static Eternal_Settings settings;

        /// <summary>
        /// Returns the guaranteed non-null settings instance.
        /// The fallback <c>new Eternal_Settings()</c> is a safety net only —
        /// <c>settings</c> is always assigned in the constructor before any game logic runs.
        /// Prefer this over direct <c>Eternal_Mod.settings</c> null-checked access (SAFE-08).
        /// </summary>
        public static Eternal_Settings GetSettings()
        {
            return settings ?? (settings = new Eternal_Settings());
        }

        /// <summary>
        /// Initializes a new instance of Eternal_Mod class.
        /// </summary>
        /// <param name="content">The mod content pack.</param>
        public Eternal_Mod(ModContentPack content) : base(content)
        {
            // Initialize settings
            settings = GetSettings<Eternal_Settings>();

            // Initialize hediff manager if needed
            if (settings.hediffManager == null)
            {
                settings.hediffManager = new EternalHediffManager();
            }

            // Log initialization
            EternalLogger.Info("Eternal mod initialized with enhanced hediff healing system v2.0");
        }

        /// <summary>
        /// Called when mod settings are being saved.
        /// </summary>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            base.DoSettingsWindowContents(inRect);
            settings.DoWindowContents(inRect);
        }

        /// <summary>
        /// Returns settings category name.
        /// </summary>
        /// <returns>The name of settings category.</returns>
        public override string SettingsCategory()
        {
            return "Eternal";
        }

        /// <summary>
        /// Called when settings are loaded from disk.
        /// </summary>
        public void Notify_SettingsLoaded()
        {
            // Base class doesn't have this method in this version

            // Ensure hediff manager is initialized
            if (settings.hediffManager == null)
            {
                settings.hediffManager = new EternalHediffManager();
            }

            EternalLogger.Info("Eternal mod settings loaded successfully");
        }

        /// <summary>
        /// Called when settings are saved to disk.
        /// </summary>
        public override void WriteSettings()
        {
            base.WriteSettings();

            // Save hediff settings AFTER base.WriteSettings() completes
            // This avoids nested Scribe context conflicts - SafeSaver.Save() manages its own Scribe lifecycle,
            // so we must wait until the parent Scribe context (from base.WriteSettings) is closed
            settings?.SaveHediffSettings();

            EternalLogger.Info("Eternal mod settings saved with hediff configurations");
        }
    }
}