// file path: Eternal/Source/Eternal/Patches/TraitSet_Patch.cs
// Author Name: 0Shard
// Date Created: 29-10-2025
// Date Last Modified: 21-02-2026
// Description: Harmony patch for TraitSet to automatically add Eternal_Essence hediff when Eternal_GeneticMarker trait is added.
//              Uses Harmony's Traverse for safer field access that adapts to RimWorld version changes.

using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Eternal;
using Eternal.Exceptions;
using Eternal.Utils;

namespace Eternal.Patches
{
    /// <summary>
    /// Harmony patch for TraitSet to automatically add Eternal_Essence hediff when Eternal_GeneticMarker trait is added.
    /// This ensures trait-hediff consistency regardless of how the trait was added.
    /// </summary>
    [HarmonyPatch(typeof(TraitSet))]
    [HarmonyPatch(nameof(TraitSet.GainTrait))]
    public static class TraitSet_GainTrait_Patch
    {
        // Cached accessor for pawn field - initialized once for performance
        // Uses Harmony's AccessTools which is version-resilient
        private static readonly AccessTools.FieldRef<TraitSet, Pawn> pawnFieldRef;
        private static bool pawnFieldInitialized;
        private static bool pawnFieldAccessible;

        /// <summary>
        /// Static constructor to cache field accessor on first use.
        /// </summary>
        static TraitSet_GainTrait_Patch()
        {
            try
            {
                // Use AccessTools.FieldRefAccess for efficient, cached field access
                // This is more robust than raw reflection and handles field renaming better
                pawnFieldRef = AccessTools.FieldRefAccess<TraitSet, Pawn>("pawn");
                pawnFieldAccessible = true;
            }
            catch (Exception ex)
            {
                // Field might not exist or have different name in this RimWorld version
                // Fall back to alternative method
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "TraitSet_GainTrait_Patch static ctor", null, ex);
                pawnFieldAccessible = false;
            }
            pawnFieldInitialized = true;
        }

        /// <summary>
        /// Postfix method that runs after a trait is added to a pawn.
        /// Checks if the added trait is Eternal_GeneticMarker and adds the Eternal_Essence hediff if needed.
        /// </summary>
        /// <param name="__instance">The TraitSet instance.</param>
        /// <param name="t">The trait that was added.</param>
        [HarmonyPostfix]
        public static void Postfix(TraitSet __instance, Trait t)
        {
            try
            {
                // SAFE-09: skip all processing when mod is disabled due to missing critical defs.
                if (EternalModState.IsDisabled)
                    return;

                // Early exit if not the Eternal trait
                if (t?.def != EternalDefOf.Eternal_GeneticMarker)
                {
                    return;
                }

                // Get the pawn that owns this TraitSet
                Pawn pawn = GetPawnFromTraitSet(__instance);
                if (pawn?.health?.hediffSet == null)
                {
                    return;
                }

                // Check if the pawn already has the Eternal_Essence hediff
                if (pawn.health.hediffSet.HasHediff(EternalDefOf.Eternal_Essence))
                {
                    return;
                }

                // Add the Eternal_Essence hediff
                Hediff hediff = HediffMaker.MakeHediff(EternalDefOf.Eternal_Essence, pawn);
                if (hediff != null)
                {
                    // Initialize the hediff with proper severity
                    hediff.Severity = 1.0f;
                    pawn.health.AddHediff(hediff);

                    // Log the addition for debugging purposes
                    if (Eternal_Mod.settings?.debugMode == true)
                    {
                        Log.Message($"[Eternal] Added Eternal_Essence hediff to {pawn.Name?.ToStringShort ?? "Unknown"} after Eternal_GeneticMarker trait was added");
                    }
                }
                else
                {
                    Log.Error($"[Eternal] Failed to create Eternal_Essence hediff for {pawn.Name?.ToStringShort ?? "Unknown"}");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "TraitSet_GainTrait_Patch.Postfix", null, ex);
            }
        }

        /// <summary>
        /// Gets the pawn that owns a TraitSet using Harmony's safer field access.
        /// Falls back to Traverse if cached accessor fails.
        /// </summary>
        /// <param name="traitSet">The TraitSet instance.</param>
        /// <returns>The pawn that owns the TraitSet, or null if not found.</returns>
        private static Pawn GetPawnFromTraitSet(TraitSet traitSet)
        {
            if (traitSet == null)
            {
                return null;
            }

            try
            {
                // Try cached field accessor first (fastest)
                if (pawnFieldInitialized && pawnFieldAccessible)
                {
                    return pawnFieldRef(traitSet);
                }

                // Fallback: Use Traverse which searches for the field by name
                // Traverse is more resilient to obfuscation and version changes
                var traverse = Traverse.Create(traitSet);

                // Try common field names
                var pawn = traverse.Field("pawn").GetValue<Pawn>();
                if (pawn != null)
                {
                    return pawn;
                }

                // Try property access as fallback
                pawn = traverse.Property("Pawn").GetValue<Pawn>();
                if (pawn != null)
                {
                    return pawn;
                }

                // Last resort: iterate through fields to find one of type Pawn
                var fields = AccessTools.GetDeclaredFields(typeof(TraitSet));
                foreach (var field in fields)
                {
                    if (field.FieldType == typeof(Pawn))
                    {
                        var value = field.GetValue(traitSet) as Pawn;
                        if (value != null)
                        {
                            return value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "GetPawnFromTraitSet", null, ex);
            }

            return null;
        }
    }
}
