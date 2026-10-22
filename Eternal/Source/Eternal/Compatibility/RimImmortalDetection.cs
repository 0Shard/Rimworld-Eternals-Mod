// Relative Path: Eternal/Source/Eternal/Compatibility/RimImmortalDetection.cs
// Creation Date: 28-12-2025
// Last Edit: 11-07-2026
// Author: 0Shard
// Description: Detects RimImmortal mod and provides type caching for reflection-based patching.
// Grants Eternals instant max cultivation progress and 99.99% breakthrough chance when RimImmortal is active.

using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;
using Eternal.Exceptions;
using Eternal.Utils;

namespace Eternal.Compatibility
{
    /// <summary>
    /// Detects RimImmortal mod and caches types for conditional Harmony patching.
    /// Provides instant max cultivation progress and 99.99% breakthrough chance for Eternal pawns.
    /// </summary>
    public static class RimImmortalDetection
    {
        #region Mod Detection

        /// <summary>
        /// Multiple possible mod IDs for RimImmortal.
        /// </summary>
        private static readonly string[] RIMMORTAL_VARIANTS = new[]
        {
            "RI.RimImmortal.Core",   // 边缘仙路-羽化登仙 RimImmortal-Core[1.6] (workshop 3296476341)
            "Atrox.RimImmortal",
            "RimImmortal",
            "rimImmortal",
            "RI",
            "atrox.rimimmortal"
        };

        /// <summary>
        /// Checks if RimImmortal mod is active.
        /// </summary>
        public static bool RimImmortalActive
        {
            get
            {
                try
                {
                    return RIMMORTAL_VARIANTS.Any(id => ModsConfig.IsActive(id));
                }
                catch (Exception ex)
                {
                    EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                        "RimImmortalActive", null, ex);
                    return false;
                }
            }
        }

        #endregion

        #region Type Cache

        private static bool _typesInitialized = false;
        private static readonly Action InitializeTypes = EnsureTypesInitialized;

        // Core.Pawn_EnergyTracker
        private static Type _pawnEnergyTrackerType;
        private static MethodInfo _setExpMethod;
        private static FieldInfo _energyTrackerPawnField;

        // RIRitualFramework.MessageDialog
        private static Type _messageDialogType;
        private static MethodInfo _getFloatUpgradeMethod;
        private static FieldInfo _sucessRateField;
        private static FieldInfo _messageDialogPawnField;

        /// <summary>
        /// Gets the SetExp method from Pawn_EnergyTracker.
        /// </summary>
        public static MethodInfo SetExpMethod => CompatibilityBinding.Cached(InitializeTypes, ref _setExpMethod);

        /// <summary>
        /// Gets the GetFloatUpgrade method from MessageDialog.
        /// </summary>
        public static MethodInfo GetFloatUpgradeMethod => CompatibilityBinding.Cached(InitializeTypes, ref _getFloatUpgradeMethod);

        /// <summary>
        /// Gets the pawn field from Pawn_EnergyTracker.
        /// </summary>
        public static FieldInfo EnergyTrackerPawnField => CompatibilityBinding.Cached(InitializeTypes, ref _energyTrackerPawnField);

        /// <summary>
        /// Gets the sucessRate field from MessageDialog.
        /// </summary>
        public static FieldInfo SucessRateField => CompatibilityBinding.Cached(InitializeTypes, ref _sucessRateField);

        /// <summary>
        /// Gets the pawn field from MessageDialog.
        /// </summary>
        public static FieldInfo MessageDialogPawnField => CompatibilityBinding.Cached(InitializeTypes, ref _messageDialogPawnField);

        #endregion

        #region Type Initialization

        /// <summary>
        /// Lazily initializes RimImmortal types via reflection.
        /// </summary>
        private static void EnsureTypesInitialized()
        {
            if (_typesInitialized)
            {
                return;
            }

            _typesInitialized = true;

            if (!RimImmortalActive)
            {
                return;
            }

            try
            {
                // Core.Pawn_EnergyTracker - holds cultivation energy and realm progress (exp)
                _pawnEnergyTrackerType = AccessTools.TypeByName("Core.Pawn_EnergyTracker");
                if (_pawnEnergyTrackerType != null)
                {
                    _setExpMethod = CompatibilityBinding.WarnMissing(AccessTools.Method(_pawnEnergyTrackerType, "SetExp", new[] { typeof(float) }),
                        "[Eternal] RimImmortal: SetExp method not found on Pawn_EnergyTracker");
                    _energyTrackerPawnField = AccessTools.Field(_pawnEnergyTrackerType, "pawn");

                }
                else
                {
                    Log.Warning("[Eternal] RimImmortal detected but Pawn_EnergyTracker type not found");
                }

                // RIRitualFramework.MessageDialog - handles breakthrough chance calculation
                _messageDialogType = AccessTools.TypeByName("RIRitualFramework.MessageDialog");
                if (_messageDialogType != null)
                {
                    _getFloatUpgradeMethod = CompatibilityBinding.WarnMissing(AccessTools.Method(_messageDialogType, "GetFloatUpgrade"),
                        "[Eternal] RimImmortal: GetFloatUpgrade method not found on MessageDialog");
                    _sucessRateField = CompatibilityBinding.WarnMissing(AccessTools.Field(_messageDialogType, "sucessRate"),
                        "[Eternal] RimImmortal: sucessRate field not found on MessageDialog");
                    _messageDialogPawnField = AccessTools.Field(_messageDialogType, "pawn");

                }
                else
                {
                    Log.Warning("[Eternal] RimImmortal detected but MessageDialog type not found");
                }

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] RimImmortal types initialized: " +
                        $"EnergyTracker={_pawnEnergyTrackerType != null}, " +
                        $"SetExp={_setExpMethod != null}, " +
                        $"MessageDialog={_messageDialogType != null}, " +
                        $"GetFloatUpgrade={_getFloatUpgradeMethod != null}, " +
                        $"SucessRate={_sucessRateField != null}");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "EnsureTypesInitialized", null, ex);
            }
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Gets the pawn from a Pawn_EnergyTracker instance.
        /// </summary>
        public static Pawn GetPawnFromEnergyTracker(object energyTracker)
        {
            if (energyTracker == null || EnergyTrackerPawnField == null)
            {
                return null;
            }

            try
            {
                return EnergyTrackerPawnField.GetValue(energyTracker) as Pawn;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "GetPawnFromEnergyTracker", null, ex);
                return null;
            }
        }

        /// <summary>
        /// Gets the pawn from a MessageDialog instance.
        /// </summary>
        public static Pawn GetPawnFromMessageDialog(object messageDialog)
        {
            if (messageDialog == null || MessageDialogPawnField == null)
            {
                return null;
            }

            try
            {
                return MessageDialogPawnField.GetValue(messageDialog) as Pawn;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "GetPawnFromMessageDialog", null, ex);
                return null;
            }
        }

        /// <summary>
        /// Gets the current sucessRate from a MessageDialog instance.
        /// </summary>
        public static float GetSucessRate(object messageDialog)
        {
            if (messageDialog == null || SucessRateField == null)
            {
                return 0f;
            }

            try
            {
                return (float)SucessRateField.GetValue(messageDialog);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "GetSucessRate", null, ex);
                return 0f;
            }
        }

        /// <summary>
        /// Sets the sucessRate on a MessageDialog instance.
        /// </summary>
        public static void SetSucessRate(object messageDialog, float value)
        {
            if (messageDialog == null || SucessRateField == null)
            {
                return;
            }

            try
            {
                SucessRateField.SetValue(messageDialog, value);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.CompatibilityFailure,
                    "SetSucessRate", null, ex);
            }
        }

        #endregion
    }
}
