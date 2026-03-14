// Relative Path: Eternal/Source/Eternal/EternalDefOf.cs
// Creation Date: 28-10-2025
// Last Edit: 12-03-2026
// Author: 0Shard
// Description: DefOf references for Eternal mod definitions.
//              Includes WorldObject definitions for Odyssey DLC compatibility.
//              VerifyBindings() now triggers EternalModState kill switch when critical
//              defs are null, and sends an in-game letter for player notification.
//              09-03: Added Eternal_MetabolicRecovery as critical def binding.
//              12-03: Added Eternal_ElixirSynthesis research project binding (non-critical).

using System.Collections.Generic;
using Verse;
using RimWorld;
using RimWorld.Planet;

namespace Eternal
{
    /// <summary>
    /// DefOf references for Eternal mod definitions.
    /// Provides easy access to mod-specific defs.
    /// </summary>
    [DefOf]
    public static class EternalDefOf
    {
        // Trait definitions
        public static TraitDef Eternal_GeneticMarker;

        // Thought definitions
        public static ThoughtDef Eternal_MoodBuff;

        // Hediff definitions
        public static HediffDef Eternal_Essence;
        public static HediffDef Eternal_Regrowing;  // Unified regrowing hediff (Immortals pattern)
        public static HediffDef Eternal_MetabolicRecovery;  // Food debt visibility hediff (09-03)
        public static HediffDef Eternal_TeleportationRecovery;

        // Research project definitions
        public static ResearchProjectDef Eternal_ElixirSynthesis;

        // Thing definitions
        public static ThingDef EternalAnchor;

        // WorldObject definitions (Odyssey DLC compatibility)
        // MayRequireOdyssey attribute ensures this doesn't cause errors if Odyssey isn't loaded
        [MayRequireOdyssey]
        public static WorldObjectDef Eternal_CrashSite;

        // Static constructor to initialize DefOf references
        static EternalDefOf()
        {
            // Ensure RimWorld's DefOf system populates these fields from XML definitions
            DefOfHelper.EnsureInitializedInCtor(typeof(EternalDefOf));
        }

        /// <summary>
        /// Verifies all DefOf references are properly bound.
        /// Critical defs (GeneticMarker, Essence, Regrowing, MetabolicRecovery) trigger the kill switch when null.
        /// Non-critical defs (MoodBuff, TeleportationRecovery, EternalAnchor) log an error only.
        /// Optional DLC-gated def (CrashSite) is only checked when Odyssey is active.
        /// Resets the kill switch first so fixing mod order between sessions recovers cleanly.
        /// </summary>
        /// <returns>True if all critical bindings are valid.</returns>
        public static bool VerifyBindings()
        {
            // Pitfall 3: reset each session so fixing load order without full restart works.
            EternalModState.Reset();

            // --- Critical defs: null → kill switch + in-game letter ---
            var missingCritical = new List<string>();

            if (Eternal_GeneticMarker == null)
            {
                Log.Error("[Eternal] DefOf binding FAILED: Eternal_GeneticMarker is null — mod cannot identify Eternal pawns. Check Traits_Eternal.xml and load order.");
                missingCritical.Add("Eternal_GeneticMarker");
            }

            if (Eternal_Essence == null)
            {
                Log.Error("[Eternal] DefOf binding FAILED: Eternal_Essence is null — resurrection hediff missing. Check Hediffs_Eternal.xml and load order.");
                missingCritical.Add("Eternal_Essence");
            }

            if (Eternal_Regrowing == null)
            {
                Log.Error("[Eternal] DefOf binding FAILED: Eternal_Regrowing is null — regrowth hediff missing. Check Hediffs_Eternal.xml and load order.");
                missingCritical.Add("Eternal_Regrowing");
            }

            if (Eternal_MetabolicRecovery == null)
            {
                Log.Error("[Eternal] DefOf binding FAILED: Eternal_MetabolicRecovery is null — metabolic recovery hediff missing. Check Hediffs_Eternal.xml and load order.");
                missingCritical.Add("Eternal_MetabolicRecovery");
            }

            if (missingCritical.Count > 0)
            {
                string list = string.Join(", ", missingCritical);
                EternalModState.Disable($"Missing critical defs: {list}.");

                // Pitfall 2: schedule letter after game init so Find.LetterStack is ready.
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    try
                    {
                        if (Find.LetterStack == null)
                            return;

                        Find.LetterStack.ReceiveLetter(
                            "Eternal Mod Disabled",
                            $"The Eternal mod has been automatically disabled because critical XML definitions are missing: {list}.\n\n" +
                            "This is usually caused by incorrect mod load order or a missing dependency.\n\n" +
                            "To fix: check your mod list order and ensure all Eternal XML files are present, then restart the game.",
                            LetterDefOf.NegativeEvent);
                    }
                    catch
                    {
                        // Never throw from inside LongEventHandler — letter is best-effort.
                    }
                });

                return false;
            }

            // --- Non-critical defs: null → log error only, mod continues ---
            if (Eternal_MoodBuff == null)
            {
                Log.Error("[Eternal] DefOf binding FAILED: Eternal_MoodBuff is null — mood thought after resurrection will not fire. Check ThoughtDefs_Eternal.xml.");
            }

            if (Eternal_TeleportationRecovery == null)
            {
                Log.Error("[Eternal] DefOf binding FAILED: Eternal_TeleportationRecovery is null — recovery hediff after corpse teleport will not apply. Check Hediffs_Eternal.xml.");
            }

            if (EternalAnchor == null)
            {
                Log.Error("[Eternal] DefOf binding FAILED: EternalAnchor is null — map retention anchor thing missing. Check ThingDefs_Eternal.xml.");
            }

            if (Eternal_ElixirSynthesis == null)
            {
                Log.Error("[Eternal] DefOf binding FAILED: Eternal_ElixirSynthesis is null — elixir crafting research will not gate correctly. Check Research_ElixirSynthesis.xml.");
            }

            // --- Optional DLC-gated def: only check when Odyssey is active ---
            // Pitfall 5: never trigger kill switch for missing optional DLC defs.
            if (ModsConfig.IsActive("ludeon.rimworld.odyssey") && Eternal_CrashSite == null)
            {
                Log.Warning("[Eternal] DefOf binding WARNING: Eternal_CrashSite is null but Odyssey DLC is active — crash site world object will not work. Check WorldObjects_Eternal.xml.");
            }

            // Success log gated behind debug mode
            if (Eternal_Mod.settings?.debugMode == true)
            {
                Log.Message("[Eternal] DefOf binding verified: All critical references bound successfully");
            }

            return true;
        }
    }
}
