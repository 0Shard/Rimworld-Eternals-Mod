// Relative Path: Eternal/Source/Eternal/Compatibility/ItsSorceryDetection.cs
// Creation Date: 13-07-2026
// Last Edit: 13-07-2026
// Author: 0Shard
// Description: Detects the It's Sorcery framework (packageId zomuro.itssorcery, workshop 3013487115)
// and caches ItsSorceryFramework types for reflection-based patching. Provides MaxOutSchema
// helper (ForceLevelUp to level cap) used to insta-max Eternal pawns' sorcery schemas.

using System;
using System.Reflection;
using HarmonyLib;
using Verse;
using Eternal.Exceptions;
using Eternal.Utils;

namespace Eternal.Compatibility
{
    /// <summary>
    /// Detects the It's Sorcery framework and caches types for conditional Harmony patching.
    /// </summary>
    public static class ItsSorceryDetection
    {
        #region Mod Detection

        /// <summary>
        /// packageId from the mod's About.xml (workshop item 3013487115).
        /// ModsConfig.IsActive is an exact match — do not guess variants.
        /// </summary>
        private const string ISF_PACKAGE_ID = "zomuro.itssorcery";

        /// <summary>
        /// Checks if It's Sorcery is active.
        /// </summary>
        public static bool ItsSorceryActive
        {
            get
            {
                try
                {
                    return ModsConfig.IsActive(ISF_PACKAGE_ID);
                }
                catch (Exception ex)
                {
                    EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                        "ItsSorceryActive", null, ex);
                    return false;
                }
            }
        }

        #endregion

        #region Type Cache

        private static bool _typesInitialized = false;
        private static readonly Action InitializeTypes = EnsureTypesInitialized;

        // ItsSorceryFramework.ProgressTracker — base schema progress class
        private static Type _progressTrackerType;
        private static FieldInfo _pawnField;
        private static PropertyInfo _maxedProperty;
        private static PropertyInfo _currLevelProperty;
        private static FieldInfo _currClassDefField;
        private static FieldInfo _pointsField;
        private static MethodInfo _applyOptionsMethod;
        private static MethodInfo _adjustModifiersOptionMethod;
        private static MethodInfo _adjustAbilitiesOptionMethod;
        private static MethodInfo _adjustHediffsOptionMethod;
        private static MethodInfo _forceLevelUpMethod;

        // ItsSorceryFramework.ProgressTracker_Level — level-specific override (Harmony patch target)
        private static Type _progressTrackerLevelType;
        private static MethodInfo _addExperienceMethod;

        // ItsSorceryFramework.ProgressTrackerClassDef — schema class definition
        private static Type _progressTrackerClassDefType;
        private static FieldInfo _levelRangeField;

        // ItsSorceryFramework.ProgressLevelModifier — modification options on level-up
        private static Type _progressLevelModifierType;
        private static FieldInfo _optionsField;
        private static FieldInfo _optionChoicesField;

        // ItsSorceryFramework.ProgressLevelOption — individual option choice
        private static Type _progressLevelOptionType;
        private static FieldInfo _optionStatOffsetsField;
        private static FieldInfo _optionStatFactorOffsetsField;
        private static FieldInfo _optionLabelField;
        private static FieldInfo _optionPointGainField;

        // ItsSorceryFramework.CompProperties_UseEffectSchema — schema component properties
        private static Type _compPropertiesUseEffectSchemaType;
        private static FieldInfo _schemaDefField;

        // ItsSorceryFramework.CompUseEffect_Schema — schema component instance
        private static Type _compUseEffectSchemaType;

        // ItsSorceryFramework.SorcerySchemaUtility — schema utility methods
        private static Type _sorcerySchemaUtilityType;
        private static MethodInfo _findSorcerySchemaMethod;

        // ItsSorceryFramework.SorcerySchema — schema instance
        private static Type _sorcerySchemaType;
        private static FieldInfo _progressTrackerField;

        // ItsSorceryFramework.SorcerySchemaDef — schema definition
        private static Type _sorcerySchemaDefType;

        // ItsSorceryFramework.ProgressDiffClassLedger — ledger for option applications
        private static Type _progressDiffClassLedgerType;

        #region ProgressTracker Properties and Methods

        /// <summary>
        /// Gets the pawn field from ProgressTracker.
        /// </summary>
        public static FieldInfo PawnField => CompatibilityBinding.Cached(InitializeTypes, ref _pawnField);

        /// <summary>
        /// Gets the Maxed property from ProgressTracker.
        /// </summary>
        public static PropertyInfo MaxedProperty => CompatibilityBinding.Cached(InitializeTypes, ref _maxedProperty);

        /// <summary>
        /// Gets the CurrLevel property from ProgressTracker.
        /// </summary>
        public static PropertyInfo CurrLevelProperty => CompatibilityBinding.Cached(InitializeTypes, ref _currLevelProperty);

        /// <summary>
        /// Gets the currClassDef field from ProgressTracker.
        /// </summary>
        public static FieldInfo CurrClassDefField => CompatibilityBinding.Cached(InitializeTypes, ref _currClassDefField);

        /// <summary>
        /// Gets the points field from ProgressTracker.
        /// </summary>
        public static FieldInfo PointsField => CompatibilityBinding.Cached(InitializeTypes, ref _pointsField);

        /// <summary>
        /// Gets the ApplyOptions method from ProgressTracker.
        /// </summary>
        public static MethodInfo ApplyOptionsMethod => CompatibilityBinding.Cached(InitializeTypes, ref _applyOptionsMethod);

        /// <summary>
        /// Gets the AdjustModifiers overload taking ProgressLevelOption from ProgressTracker.
        /// </summary>
        public static MethodInfo AdjustModifiersOptionMethod => CompatibilityBinding.Cached(InitializeTypes, ref _adjustModifiersOptionMethod);

        /// <summary>
        /// Gets the AdjustAbilities overload taking ProgressLevelOption from ProgressTracker.
        /// </summary>
        public static MethodInfo AdjustAbilitiesOptionMethod => CompatibilityBinding.Cached(InitializeTypes, ref _adjustAbilitiesOptionMethod);

        /// <summary>
        /// Gets the AdjustHediffs overload taking ProgressLevelOption from ProgressTracker.
        /// </summary>
        public static MethodInfo AdjustHediffsOptionMethod => CompatibilityBinding.Cached(InitializeTypes, ref _adjustHediffsOptionMethod);

        /// <summary>
        /// Gets the ForceLevelUp method from ProgressTracker (int levels, bool silent_msg).
        /// Base-virtual MethodInfo — Invoke dispatches virtually to the ProgressTracker_Level override.
        /// </summary>
        public static MethodInfo ForceLevelUpMethod => CompatibilityBinding.Cached(InitializeTypes, ref _forceLevelUpMethod);

        #endregion

        #region ProgressTracker_Level Properties and Methods

        /// <summary>
        /// Gets the AddExperience method from ProgressTracker_Level (Harmony patch target).
        /// </summary>
        public static MethodInfo AddExperienceMethod => CompatibilityBinding.Cached(InitializeTypes, ref _addExperienceMethod);

        #endregion

        #region ProgressTrackerClassDef Properties and Methods

        /// <summary>
        /// Gets the levelRange field from ProgressTrackerClassDef.
        /// </summary>
        public static FieldInfo LevelRangeField => CompatibilityBinding.Cached(InitializeTypes, ref _levelRangeField);

        #endregion

        #region ProgressLevelModifier Properties and Methods

        /// <summary>
        /// Gets the options field (List of ProgressLevelOption) from ProgressLevelModifier.
        /// </summary>
        public static FieldInfo OptionsField => CompatibilityBinding.Cached(InitializeTypes, ref _optionsField);

        /// <summary>
        /// Gets the optionChoices field (int) from ProgressLevelModifier.
        /// </summary>
        public static FieldInfo OptionChoicesField => CompatibilityBinding.Cached(InitializeTypes, ref _optionChoicesField);

        #endregion

        #region ProgressLevelOption Properties and Methods

        /// <summary>
        /// Gets the statOffsets field (List of StatModifier) from ProgressLevelOption.
        /// </summary>
        public static FieldInfo OptionStatOffsetsField => CompatibilityBinding.Cached(InitializeTypes, ref _optionStatOffsetsField);

        /// <summary>
        /// Gets the statFactorOffsets field (List of StatModifier) from ProgressLevelOption.
        /// </summary>
        public static FieldInfo OptionStatFactorOffsetsField => CompatibilityBinding.Cached(InitializeTypes, ref _optionStatFactorOffsetsField);

        /// <summary>
        /// Gets the label field (string) from ProgressLevelOption.
        /// </summary>
        public static FieldInfo OptionLabelField => CompatibilityBinding.Cached(InitializeTypes, ref _optionLabelField);

        /// <summary>
        /// Gets the pointGain field (int) from ProgressLevelOption.
        /// </summary>
        public static FieldInfo OptionPointGainField => CompatibilityBinding.Cached(InitializeTypes, ref _optionPointGainField);

        #endregion

        #region CompProperties_UseEffectSchema Properties and Methods

        /// <summary>
        /// Gets the schemaDef field from CompProperties_UseEffectSchema.
        /// </summary>
        public static FieldInfo SchemaDefField => CompatibilityBinding.Cached(InitializeTypes, ref _schemaDefField);

        #endregion

        #region SorcerySchemaUtility Properties and Methods

        /// <summary>
        /// Gets the FindSorcerySchema static method taking (Pawn, SorcerySchemaDef).
        /// </summary>
        public static MethodInfo FindSorcerySchemaMethod => CompatibilityBinding.Cached(InitializeTypes, ref _findSorcerySchemaMethod);

        #endregion

        #region SorcerySchema Properties and Methods

        /// <summary>
        /// Gets the progressTracker field from SorcerySchema.
        /// </summary>
        public static FieldInfo ProgressTrackerField => CompatibilityBinding.Cached(InitializeTypes, ref _progressTrackerField);

        #endregion

        #endregion

        #region Type Initialization

        /// <summary>
        /// Lazily initializes It's Sorcery types via reflection.
        /// </summary>
        private static void EnsureTypesInitialized()
        {
            if (_typesInitialized)
            {
                return;
            }

            _typesInitialized = true;

            if (!ItsSorceryActive)
            {
                return;
            }

            try
            {
                // Load all types
                _progressTrackerType = AccessTools.TypeByName("ItsSorceryFramework.ProgressTracker");
                _progressTrackerLevelType = AccessTools.TypeByName("ItsSorceryFramework.ProgressTracker_Level");
                _progressLevelModifierType = AccessTools.TypeByName("ItsSorceryFramework.ProgressLevelModifier");
                _progressLevelOptionType = AccessTools.TypeByName("ItsSorceryFramework.ProgressLevelOption");
                _progressDiffClassLedgerType = AccessTools.TypeByName("ItsSorceryFramework.ProgressDiffClassLedger");
                _compUseEffectSchemaType = AccessTools.TypeByName("ItsSorceryFramework.CompUseEffect_Schema");
                _compPropertiesUseEffectSchemaType = AccessTools.TypeByName("ItsSorceryFramework.CompProperties_UseEffectSchema");
                _sorcerySchemaUtilityType = AccessTools.TypeByName("ItsSorceryFramework.SorcerySchemaUtility");
                _sorcerySchemaType = AccessTools.TypeByName("ItsSorceryFramework.SorcerySchema");
                _sorcerySchemaDefType = AccessTools.TypeByName("ItsSorceryFramework.SorcerySchemaDef");
                _progressTrackerClassDefType = AccessTools.TypeByName("ItsSorceryFramework.ProgressTrackerClassDef");

                // Load ProgressTracker members
                if (_progressTrackerType != null)
                {
                    _pawnField = CompatibilityBinding.WarnMissing(AccessTools.Field(_progressTrackerType, "pawn"),
                        "[Eternal] It's Sorcery: pawn field not found on ProgressTracker");
                    _maxedProperty = CompatibilityBinding.WarnMissing(AccessTools.Property(_progressTrackerType, "Maxed"),
                        "[Eternal] It's Sorcery: Maxed property not found on ProgressTracker");
                    _currLevelProperty = CompatibilityBinding.WarnMissing(AccessTools.Property(_progressTrackerType, "CurrLevel"),
                        "[Eternal] It's Sorcery: CurrLevel property not found on ProgressTracker");
                    _currClassDefField = CompatibilityBinding.WarnMissing(AccessTools.Field(_progressTrackerType, "currClassDef"),
                        "[Eternal] It's Sorcery: currClassDef field not found on ProgressTracker");
                    _pointsField = CompatibilityBinding.WarnMissing(AccessTools.Field(_progressTrackerType, "points"),
                        "[Eternal] It's Sorcery: points field not found on ProgressTracker");
                    _applyOptionsMethod = CompatibilityBinding.WarnMissing(AccessTools.Method(_progressTrackerType, "ApplyOptions"),
                        "[Eternal] It's Sorcery: ApplyOptions method not found on ProgressTracker");

                    // AdjustModifiers overload taking (ProgressLevelOption, ref ProgressDiffClassLedger)
                    if (_progressLevelOptionType != null && _progressDiffClassLedgerType != null)
                    {
                        _adjustModifiersOptionMethod = AccessTools.Method(_progressTrackerType, "AdjustModifiers",
                            new[] { _progressLevelOptionType, _progressDiffClassLedgerType.MakeByRefType() });
                        _adjustAbilitiesOptionMethod = AccessTools.Method(_progressTrackerType, "AdjustAbilities",
                            new[] { _progressLevelOptionType, _progressDiffClassLedgerType.MakeByRefType() });
                        _adjustHediffsOptionMethod = AccessTools.Method(_progressTrackerType, "AdjustHediffs",
                            new[] { _progressLevelOptionType, _progressDiffClassLedgerType.MakeByRefType() });
                    }

                    _forceLevelUpMethod = CompatibilityBinding.WarnMissing(AccessTools.Method(_progressTrackerType, "ForceLevelUp", new[] { typeof(int), typeof(bool) }),
                        "[Eternal] It's Sorcery: ForceLevelUp method not found on ProgressTracker");

                    CompatibilityBinding.WarnMissing(_adjustModifiersOptionMethod,
                        "[Eternal] It's Sorcery: AdjustModifiers(ProgressLevelOption) method not found on ProgressTracker");

                    CompatibilityBinding.WarnMissing(_adjustAbilitiesOptionMethod,
                        "[Eternal] It's Sorcery: AdjustAbilities(ProgressLevelOption) method not found on ProgressTracker");

                    CompatibilityBinding.WarnMissing(_adjustHediffsOptionMethod,
                        "[Eternal] It's Sorcery: AdjustHediffs(ProgressLevelOption) method not found on ProgressTracker");

                }
                else
                {
                    Log.Warning("[Eternal] It's Sorcery detected but ProgressTracker type not found");
                }

                // Load ProgressTracker_Level members
                if (_progressTrackerLevelType != null)
                {
                    _addExperienceMethod = CompatibilityBinding.WarnMissing(AccessTools.Method(_progressTrackerLevelType, "AddExperience", new[] { typeof(float) }),
                        "[Eternal] It's Sorcery: AddExperience method not found on ProgressTracker_Level");

                }
                else
                {
                    Log.Warning("[Eternal] It's Sorcery: ProgressTracker_Level type not found");
                }

                // Load ProgressTrackerClassDef members
                if (_progressTrackerClassDefType != null)
                {
                    _levelRangeField = CompatibilityBinding.WarnMissing(AccessTools.Field(_progressTrackerClassDefType, "levelRange"),
                        "[Eternal] It's Sorcery: levelRange field not found on ProgressTrackerClassDef");

                }
                else
                {
                    Log.Warning("[Eternal] It's Sorcery: ProgressTrackerClassDef type not found");
                }

                // Load ProgressLevelModifier members
                if (_progressLevelModifierType != null)
                {
                    _optionsField = CompatibilityBinding.WarnMissing(AccessTools.Field(_progressLevelModifierType, "options"),
                        "[Eternal] It's Sorcery: options field not found on ProgressLevelModifier");
                    _optionChoicesField = CompatibilityBinding.WarnMissing(AccessTools.Field(_progressLevelModifierType, "optionChoices"),
                        "[Eternal] It's Sorcery: optionChoices field not found on ProgressLevelModifier");

                }
                else
                {
                    Log.Warning("[Eternal] It's Sorcery: ProgressLevelModifier type not found");
                }

                // Load ProgressLevelOption members
                if (_progressLevelOptionType != null)
                {
                    _optionStatOffsetsField = CompatibilityBinding.WarnMissing(AccessTools.Field(_progressLevelOptionType, "statOffsets"),
                        "[Eternal] It's Sorcery: statOffsets field not found on ProgressLevelOption");
                    _optionStatFactorOffsetsField = CompatibilityBinding.WarnMissing(AccessTools.Field(_progressLevelOptionType, "statFactorOffsets"),
                        "[Eternal] It's Sorcery: statFactorOffsets field not found on ProgressLevelOption");
                    _optionLabelField = CompatibilityBinding.WarnMissing(AccessTools.Field(_progressLevelOptionType, "label"),
                        "[Eternal] It's Sorcery: label field not found on ProgressLevelOption");
                    _optionPointGainField = CompatibilityBinding.WarnMissing(AccessTools.Field(_progressLevelOptionType, "pointGain"),
                        "[Eternal] It's Sorcery: pointGain field not found on ProgressLevelOption");

                }
                else
                {
                    Log.Warning("[Eternal] It's Sorcery: ProgressLevelOption type not found");
                }

                // Load CompProperties_UseEffectSchema members
                if (_compPropertiesUseEffectSchemaType != null)
                {
                    _schemaDefField = CompatibilityBinding.WarnMissing(AccessTools.Field(_compPropertiesUseEffectSchemaType, "schemaDef"),
                        "[Eternal] It's Sorcery: schemaDef field not found on CompProperties_UseEffectSchema");

                }
                else
                {
                    Log.Warning("[Eternal] It's Sorcery: CompProperties_UseEffectSchema type not found");
                }

                // Load SorcerySchemaUtility members
                if (_sorcerySchemaUtilityType != null && _sorcerySchemaDefType != null)
                {
                    _findSorcerySchemaMethod = CompatibilityBinding.WarnMissing(AccessTools.Method(_sorcerySchemaUtilityType, "FindSorcerySchema",
                        new[] { typeof(Pawn), _sorcerySchemaDefType }),
                        "[Eternal] It's Sorcery: FindSorcerySchema method not found on SorcerySchemaUtility");

                }
                else
                {
                    if (_sorcerySchemaUtilityType == null)
                    {
                        Log.Warning("[Eternal] It's Sorcery: SorcerySchemaUtility type not found");
                    }

                    if (_sorcerySchemaDefType == null)
                    {
                        Log.Warning("[Eternal] It's Sorcery: SorcerySchemaDef type not found");
                    }
                }

                // Load SorcerySchema members
                if (_sorcerySchemaType != null)
                {
                    _progressTrackerField = CompatibilityBinding.WarnMissing(AccessTools.Field(_sorcerySchemaType, "progressTracker"),
                        "[Eternal] It's Sorcery: progressTracker field not found on SorcerySchema");

                }
                else
                {
                    Log.Warning("[Eternal] It's Sorcery: SorcerySchema type not found");
                }

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] It's Sorcery types initialized: " +
                        $"ProgressTracker={_progressTrackerType != null}, " +
                        $"ProgressTracker_Level={_progressTrackerLevelType != null}, " +
                        $"ProgressLevelModifier={_progressLevelModifierType != null}, " +
                        $"ProgressLevelOption={_progressLevelOptionType != null}, " +
                        $"ProgressDiffClassLedger={_progressDiffClassLedgerType != null}, " +
                        $"CompUseEffect_Schema={_compUseEffectSchemaType != null}, " +
                        $"CompProperties_UseEffectSchema={_compPropertiesUseEffectSchemaType != null}, " +
                        $"SorcerySchemaUtility={_sorcerySchemaUtilityType != null}, " +
                        $"SorcerySchema={_sorcerySchemaType != null}, " +
                        $"SorcerySchemaDef={_sorcerySchemaDefType != null}, " +
                        $"ProgressTrackerClassDef={_progressTrackerClassDefType != null}");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "ItsSorceryDetection.EnsureTypesInitialized", null, ex);
            }
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Checks if a ProgressTracker instance has been maxed out.
        /// </summary>
        public static bool IsMaxed(object progressTracker)
        {
            if (progressTracker == null || MaxedProperty == null)
            {
                return true;  // Safe no-op signal
            }

            try
            {
                return (bool)MaxedProperty.GetValue(progressTracker);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "IsMaxed", null, ex);
                return true;
            }
        }

        /// <summary>
        /// Gets the current level of a ProgressTracker instance.
        /// </summary>
        public static int GetCurrLevel(object progressTracker)
        {
            if (progressTracker == null || CurrLevelProperty == null)
            {
                return 0;
            }

            try
            {
                return (int)CurrLevelProperty.GetValue(progressTracker);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "GetCurrLevel", null, ex);
                return 0;
            }
        }

        /// <summary>
        /// Gets the maximum level of a ProgressTracker instance based on its class definition.
        /// </summary>
        public static int GetMaxLevel(object progressTracker)
        {
            if (progressTracker == null || CurrClassDefField == null || LevelRangeField == null)
            {
                return 0;
            }

            try
            {
                object classDefObj = CurrClassDefField.GetValue(progressTracker);
                if (classDefObj == null)
                {
                    return 0;
                }

                object levelRangeObj = LevelRangeField.GetValue(classDefObj);
                if (levelRangeObj == null)
                {
                    return 0;
                }

                // IntRange has a TrueMax property
                var intRangeType = levelRangeObj.GetType();
                var trueMaxProperty = intRangeType.GetProperty("TrueMax");
                if (trueMaxProperty != null)
                {
                    return (int)trueMaxProperty.GetValue(levelRangeObj);
                }

                return 0;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "GetMaxLevel", null, ex);
                return 0;
            }
        }

        /// <summary>
        /// Force-levels a ProgressTracker to its maximum level.
        /// </summary>
        public static void MaxOutSchema(object progressTracker)
        {
            if (progressTracker == null)
            {
                return;
            }

            if (IsMaxed(progressTracker))
            {
                return;
            }

            int currLevel = GetCurrLevel(progressTracker);
            int maxLevel = GetMaxLevel(progressTracker);
            int levels = maxLevel - currLevel;

            if (levels <= 0)
            {
                return;
            }

            if (ForceLevelUpMethod == null)
            {
                return;
            }

            try
            {
                ForceLevelUpMethod.Invoke(progressTracker, new object[] { levels, false });

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] ISF: force-leveled schema by {levels} levels");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "MaxOutSchema", null, ex);
            }
        }

        /// <summary>
        /// Finds the SorcerySchema progress tracker for a pawn and schema definition.
        /// Returns null if not found or on error.
        /// </summary>
        public static object FindSchemaProgressTracker(Pawn pawn, object schemaDef)
        {
            if (pawn == null || schemaDef == null || FindSorcerySchemaMethod == null)
            {
                return null;
            }

            try
            {
                object schemaObj = FindSorcerySchemaMethod.Invoke(null, new object[] { pawn, schemaDef });
                if (schemaObj == null)
                {
                    return null;
                }

                if (ProgressTrackerField == null)
                {
                    return null;
                }

                return ProgressTrackerField.GetValue(schemaObj);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "FindSchemaProgressTracker", null, ex);
                return null;
            }
        }

        #endregion
    }
}
