// file path: Eternal/Source/Eternal/Patches/GameComponentRegistration_Patch.cs
// Author Name: 0Shard
// Date Created: 29-10-2025
// Date Last Modified: 20-02-2026
// Description: Harmony patch to programmatically register Eternal_Component in RimWorld 1.6.

using System;
using System.Reflection;
using HarmonyLib;
using Verse;
using Eternal.Exceptions;
using Eternal.Utils;

namespace Eternal.Patches
{
    /// <summary>
    /// Harmony patch class to register Eternal_Component programmatically in RimWorld 1.6.
    /// In RimWorld 1.6, GameComponentDef was removed, so components must be registered via Harmony patches.
    /// </summary>
    [HarmonyPatch(typeof(Game), "FinalizeInit")]
    public static class GameComponentRegistration_Patch
    {
        /// <summary>
        /// Postfix patch to Game.FinalizeInit to register our component after game initialization.
        /// This is the optimal time to register components as all game systems are initialized.
        /// </summary>
        [HarmonyPostfix]
        public static void RegisterEternalComponent()
        {
            try
            {
                // Only register if not already registered to prevent duplicates
                if (Current.Game.GetComponent<Eternal_Component>() == null)
                {
                    // Create and register the Eternal_Component instance
                    Eternal_Component component = new Eternal_Component(Current.Game);
                    Current.Game.components.Add(component);
                    
                    Log.Message("[Eternal] Successfully registered Eternal_Component via Harmony patch");
                }
                else
                {
                    Log.Message("[Eternal] Eternal_Component already registered, skipping duplicate registration");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.InternalError,
                    "RegisterEternalComponent", null, ex);
            }
        }
    }
}