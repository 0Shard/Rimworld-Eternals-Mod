// Relative Path: Eternal/Source/Eternal/Compatibility/CultivatorOfTheRimDetection.cs
// Creation Date: 12-07-2026
// Last Edit: 12-07-2026
// Author: 0Shard
// Description: Detects the Cultivator of the Rim mod (packageId Aranmaho.Xianxia, workshop 3221270722)
// and caches types for reflection-based patching. Grants Eternals guaranteed technique-manual
// learning (1000x learn chance); the 10x cultivation speed is delivered via an XML statFactors
// patch on the Eternal trait (Patches/CultivatorOfTheRim_Patches.xml), not through this class.

using System;
using System.Reflection;
using HarmonyLib;
using Verse;
using Eternal.Exceptions;
using Eternal.Utils;

namespace Eternal.Compatibility
{
    /// <summary>
    /// Detects the Cultivator of the Rim mod and caches types for conditional Harmony patching.
    /// </summary>
    public static class CultivatorOfTheRimDetection
    {
        #region Mod Detection

        /// <summary>
        /// packageId from the mod's About.xml (workshop item 3221270722).
        /// ModsConfig.IsActive is an exact match — do not guess variants.
        /// </summary>
        private const string CTR_PACKAGE_ID = "Aranmaho.Xianxia";

        /// <summary>
        /// Checks if Cultivator of the Rim is active.
        /// </summary>
        public static bool CultivatorOfTheRimActive
        {
            get
            {
                try
                {
                    return ModsConfig.IsActive(CTR_PACKAGE_ID);
                }
                catch (Exception ex)
                {
                    EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                        "CultivatorOfTheRimActive", null, ex);
                    return false;
                }
            }
        }

        #endregion

        #region Type Cache

        private static bool _typesInitialized = false;

        // CultivatorOfTheRim.BookOutcomeDoerTechniqueManual — rolls the technique learn chance
        // every 250 reading ticks (binary hediff grant, no exp system).
        private static Type _techniqueManualDoerType;
        private static MethodInfo _onReadingTickMethod;
        private static FieldInfo _chanceCachedField;
        private static PropertyInfo _finalChanceProperty;

        /// <summary>
        /// Gets the OnReadingTick method from BookOutcomeDoerTechniqueManual.
        /// </summary>
        public static MethodInfo OnReadingTickMethod
        {
            get
            {
                EnsureTypesInitialized();
                return _onReadingTickMethod;
            }
        }

        /// <summary>
        /// Gets the chanceCached field (public float caching the computed learn chance).
        /// </summary>
        public static FieldInfo ChanceCachedField
        {
            get
            {
                EnsureTypesInitialized();
                return _chanceCachedField;
            }
        }

        /// <summary>
        /// Gets the finalChance property (learnChance × book-quality curve, lazily cached).
        /// </summary>
        public static PropertyInfo FinalChanceProperty
        {
            get
            {
                EnsureTypesInitialized();
                return _finalChanceProperty;
            }
        }

        #endregion

        #region Type Initialization

        /// <summary>
        /// Lazily initializes Cultivator of the Rim types via reflection.
        /// </summary>
        private static void EnsureTypesInitialized()
        {
            if (_typesInitialized)
                return;

            _typesInitialized = true;

            if (!CultivatorOfTheRimActive)
                return;

            try
            {
                _techniqueManualDoerType = AccessTools.TypeByName("CultivatorOfTheRim.BookOutcomeDoerTechniqueManual");
                if (_techniqueManualDoerType != null)
                {
                    _onReadingTickMethod = AccessTools.Method(_techniqueManualDoerType, "OnReadingTick");
                    _chanceCachedField = AccessTools.Field(_techniqueManualDoerType, "chanceCached");
                    _finalChanceProperty = AccessTools.Property(_techniqueManualDoerType, "finalChance");

                    if (_onReadingTickMethod == null)
                        Log.Warning("[Eternal] Cultivator of the Rim: OnReadingTick method not found on BookOutcomeDoerTechniqueManual");
                    if (_chanceCachedField == null)
                        Log.Warning("[Eternal] Cultivator of the Rim: chanceCached field not found on BookOutcomeDoerTechniqueManual");
                    if (_finalChanceProperty == null)
                        Log.Warning("[Eternal] Cultivator of the Rim: finalChance property not found on BookOutcomeDoerTechniqueManual");
                }
                else
                {
                    Log.Warning("[Eternal] Cultivator of the Rim detected but BookOutcomeDoerTechniqueManual type not found");
                }

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Cultivator of the Rim types initialized: " +
                        $"TechniqueManualDoer={_techniqueManualDoerType != null}, " +
                        $"OnReadingTick={_onReadingTickMethod != null}, " +
                        $"chanceCached={_chanceCachedField != null}, " +
                        $"finalChance={_finalChanceProperty != null}");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "CultivatorOfTheRimDetection.EnsureTypesInitialized", null, ex);
            }
        }

        /// <summary>
        /// Resets the type cache (for testing or mod reload scenarios).
        /// </summary>
        public static void ResetTypeCache()
        {
            _typesInitialized = false;
            _techniqueManualDoerType = null;
            _onReadingTickMethod = null;
            _chanceCachedField = null;
            _finalChanceProperty = null;
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Gets the cached learn chance from a BookOutcomeDoerTechniqueManual instance.
        /// Reading the finalChance property first ensures the cache is populated.
        /// </summary>
        public static float GetFinalChance(object techniqueManualDoer)
        {
            if (techniqueManualDoer == null || FinalChanceProperty == null)
                return 0f;

            try
            {
                return (float)FinalChanceProperty.GetValue(techniqueManualDoer);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "GetFinalChance", null, ex);
                return 0f;
            }
        }

        /// <summary>
        /// Overwrites the cached learn chance on a BookOutcomeDoerTechniqueManual instance.
        /// </summary>
        public static void SetChanceCached(object techniqueManualDoer, float value)
        {
            if (techniqueManualDoer == null || ChanceCachedField == null)
                return;

            try
            {
                ChanceCachedField.SetValue(techniqueManualDoer, value);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "SetChanceCached", null, ex);
            }
        }

        #endregion
    }
}
