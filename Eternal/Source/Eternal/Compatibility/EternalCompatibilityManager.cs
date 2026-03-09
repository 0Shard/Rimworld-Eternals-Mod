// Relative Path: Eternal/Source/Eternal/Compatibility/EternalCompatibilityManager.cs
// Creation Date: 28-10-2025
// Last Edit: 20-02-2026
// Author: 0Shard
// Description: Manages compatibility checks for common medical mods. Detects conflicts
//              and logs warnings for potentially problematic mod combinations.

using System;
using System.Collections.Generic;
using System.Linq;
using Eternal.Exceptions;
using Eternal.Utils;
using Verse;

namespace Eternal.Compatibility
{
    /// <summary>
    /// Manages compatibility checks for common medical mods.
    /// Ensures Eternal mod works correctly with other medical-related mods.
    /// </summary>
    public static class EternalCompatibilityManager
    {
        private static readonly Dictionary<string, bool> detectedMods = new Dictionary<string, bool>();
        private static readonly List<string> conflictingMods = new List<string>();
        private static readonly List<string> compatibleMods = new List<string>();
         
        static EternalCompatibilityManager()
        {
            InitializeCompatibilityLists();
            CheckForConflictingMods();
        }
         
        /// <summary>
        /// Initializes lists of known compatible and conflicting mods.
        /// </summary>
        private static void InitializeCompatibilityLists()
        {
            // Known compatible medical mods
            compatibleMods.Add("JecsTools"); // JecsTools framework
            compatibleMods.Add("Harmony"); // Harmony framework
            compatibleMods.Add("Giddy-up"); // Giddy-up series
            compatibleMods.Add("Hospitality"); // Hospitality mod
            compatibleMods.Add("AllowTool"); // Allow Tool
            compatibleMods.Add("Dubs Bad Hygiene"); // Dubs Bad Hygiene
            compatibleMods.Add("Rimatomics"); // Rimatomics
            compatibleMods.Add("Combat Extended"); // Combat Extended
             
            // Known conflicting mods (May need patches)
            conflictingMods.Add("Immortals"); // Original Immortals mod
            conflictingMods.Add("Regrowth"); // Other regrowth mods
            conflictingMods.Add("Resurrection"); // Other resurrection mods
            conflictingMods.Add("Undead"); // Undead mods
        }
         
        /// <summary>
        /// Checks for conflicting mods and logs warnings.
        /// </summary>
        private static void CheckForConflictingMods()
        {
            var loadedMods = LoadedModManager.RunningMods.ToList();
             
            foreach (var mod in loadedMods)
            {
                string modName = mod.Name;
                // Try to get the identifier - property name may vary between RimWorld versions
                string modIdentifier = "";
                try
                {
                    modIdentifier = mod.PackageId; // Use PackageId which is more consistent across versions
                }
                catch (Exception ex)
                {
                    EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                        "GetModPackageId", null, ex);
                    // In newer versions of RimWorld, Identifier may not exist
                    // Use reflection to check if it exists
                    var identifierProp = mod.GetType().GetProperty("Identifier");
                    if (identifierProp != null)
                    {
                        modIdentifier = identifierProp.GetValue(mod) as string ?? mod.Name;
                    }
                    else
                    {
                        modIdentifier = mod.Name; // Fallback to name if both fail
                    }
                }
                 
                detectedMods[modIdentifier] = true;
                 
                // Check for conflicting mods
                if (IsConflictingMod(modName, modIdentifier))
                {
                    Log.Warning($"[Eternal] Detected potentially conflicting mod: {modName} ({modIdentifier})");
                    Log.Warning("[Eternal] This may cause issues with Eternal regrowth mechanics.");
                }
                 
                // Check for compatible mods
                if (IsCompatibleMod(modName, modIdentifier))
                {
                    Log.Message($"[Eternal] Detected compatible mod: {modName} ({modIdentifier})");
                }
            }
             
            // Log compatibility summary
            LogCompatibilitySummary();
        }
        
        /// <summary>
        /// Checks if a mod is known to conflict with Eternal.
        /// </summary>
        /// <param name="modName">The display name of the mod.</param>
        /// <param name="modIdentifier">The identifier of the mod.</param>
        /// <returns>True if the mod is known to conflict, false otherwise.</returns>
        private static bool IsConflictingMod(string modName, string modIdentifier)
        {
            return conflictingMods.Any(conflict => 
                modName.Contains(conflict) || modIdentifier.Contains(conflict));
        }
        
        /// <summary>
        /// Checks if a mod is known to be compatible with Eternal.
        /// </summary>
        /// <param name="modName">The display name of the mod.</param>
        /// <param name="modIdentifier">The identifier of the mod.</param>
        /// <returns>True if the mod is known to be compatible, false otherwise.</returns>
        private static bool IsCompatibleMod(string modName, string modIdentifier)
        {
            return compatibleMods.Any(compatible => 
                modName.Contains(compatible) || modIdentifier.Contains(compatible));
        }
        
        /// <summary>
        /// Logs a summary of compatibility checks.
        /// </summary>
        private static void LogCompatibilitySummary()
        {
            int conflictCount = detectedMods.Keys.Count(id => IsConflictingMod("", id));
            int compatibleCount = detectedMods.Keys.Count(id => IsCompatibleMod("", id));

            Log.Message($"[Eternal] Compatibility check complete: {detectedMods.Count} mods detected, {conflictCount} potential conflicts, {compatibleCount} known compatible");

            if (conflictCount > 0)
            {
                Log.Message("[Eternal] If you experience issues, please report them on the mod page.");
            }
        }
        
        /// <summary>
        /// Checks if a specific mod is loaded.
        /// </summary>
        /// <param name="modIdentifier">The identifier of the mod to check.</param>
        /// <returns>True if the mod is loaded, false otherwise.</returns>
        public static bool IsModLoaded(string modIdentifier)
        {
            return detectedMods.ContainsKey(modIdentifier) && detectedMods[modIdentifier];
        }
        
        /// <summary>
        /// Gets a list of all detected conflicting mods.
        /// </summary>
        /// <returns>List of conflicting mod identifiers.</returns>
        public static List<string> GetConflictingMods()
        {
            return detectedMods.Keys
                .Where(id => IsConflictingMod("", id))
                .ToList();
        }
    }
}