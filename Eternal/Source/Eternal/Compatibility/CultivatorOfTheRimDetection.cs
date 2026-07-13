// Relative Path: Eternal/Source/Eternal/Compatibility/CultivatorOfTheRimDetection.cs
// Creation Date: 12-07-2026
// Last Edit: 13-07-2026
// Author: 0Shard
// Description: Detects the Cultivator of the Rim mod (packageId Aranmaho.Xianxia, workshop 3221270722)
// and caches types for reflection-based patching. Grants Eternals guaranteed technique-manual
// learning in the No-Sorcery variant (1000x learn chance; with It's Sorcery active manuals are
// schema items handled by ItsSorceryDetection instead); the 25x cultivation speed is delivered
// via an XML statFactors patch on the Eternal trait (Patches/CultivatorOfTheRim_Patches.xml).
// Also caches HediffComp_BodyCultivation members for the passive body-cultivation patch.

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
        // every 250 reading ticks (binary hediff grant, no exp system). Only loaded by CTR's
        // No-Sorcery variant; with zomuro.itssorcery active this type is never instantiated.
        private static Type _techniqueManualDoerType;
        private static MethodInfo _onReadingTickMethod;
        private static FieldInfo _chanceCachedField;
        private static PropertyInfo _finalChanceProperty;

        // CultivatorOfTheRim.HediffComp_BodyCultivation — passive body-realm severity gain
        // for Eternals via periodic CompPostTickInterval callback.
        private static Type _bodyCultivationCompType;
        private static MethodInfo _compPostTickIntervalMethod;
        private static FieldInfo _bodyCultivationSpeedField;

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

        /// <summary>
        /// Gets the CompPostTickInterval method from HediffComp_BodyCultivation.
        /// </summary>
        public static MethodInfo CompPostTickIntervalMethod
        {
            get
            {
                EnsureTypesInitialized();
                return _compPostTickIntervalMethod;
            }
        }

        /// <summary>
        /// Gets the CultivationSpeed field from HediffComp_BodyCultivation.
        /// </summary>
        public static FieldInfo BodyCultivationSpeedField
        {
            get
            {
                EnsureTypesInitialized();
                return _bodyCultivationSpeedField;
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

                _bodyCultivationCompType = AccessTools.TypeByName("CultivatorOfTheRim.HediffComp_BodyCultivation");
                if (_bodyCultivationCompType != null)
                {
                    _compPostTickIntervalMethod = AccessTools.Method(_bodyCultivationCompType, "CompPostTickInterval");
                    _bodyCultivationSpeedField = AccessTools.Field(_bodyCultivationCompType, "CultivationSpeed");

                    if (_compPostTickIntervalMethod == null)
                        Log.Warning("[Eternal] Cultivator of the Rim: CompPostTickInterval method not found on HediffComp_BodyCultivation");
                    if (_bodyCultivationSpeedField == null)
                        Log.Warning("[Eternal] Cultivator of the Rim: CultivationSpeed field not found on HediffComp_BodyCultivation");
                }
                else
                {
                    Log.Warning("[Eternal] Cultivator of the Rim detected but HediffComp_BodyCultivation type not found");
                }

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Cultivator of the Rim types initialized: " +
                        $"TechniqueManualDoer={_techniqueManualDoerType != null}, " +
                        $"OnReadingTick={_onReadingTickMethod != null}, " +
                        $"chanceCached={_chanceCachedField != null}, " +
                        $"finalChance={_finalChanceProperty != null}, " +
                        $"BodyCultivationComp={_bodyCultivationCompType != null}, " +
                        $"CompPostTickInterval={_compPostTickIntervalMethod != null}, " +
                        $"CultivationSpeed={_bodyCultivationSpeedField != null}");
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
            _bodyCultivationCompType = null;
            _compPostTickIntervalMethod = null;
            _bodyCultivationSpeedField = null;
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

        /// <summary>
        /// Reads the comp's stat-refreshed CultivationSpeed field. 0f signals lookup failure (callers skip the gain).
        /// </summary>
        public static float GetBodyCultivationSpeed(object bodyCultivationComp)
        {
            if (bodyCultivationComp == null || BodyCultivationSpeedField == null)
                return 0f;

            try
            {
                return (float)BodyCultivationSpeedField.GetValue(bodyCultivationComp);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "GetBodyCultivationSpeed", null, ex);
                return 0f;
            }
        }

        #endregion
    }
}
