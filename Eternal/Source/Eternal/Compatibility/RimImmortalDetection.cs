// Relative Path: Eternal/Source/Eternal/Compatibility/RimImmortalDetection.cs
// Creation Date: 28-12-2025
// Last Edit: 20-02-2026
// Author: 0Shard
// Description: Detects RimImmortal mod and provides type caching for reflection-based patching.
// Grants Eternals 5x cultivation speed and 5x breakthrough chance when RimImmortal is active.

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
    /// Provides 5x cultivation speed and 5x breakthrough chance for Eternal pawns.
    /// </summary>
    public static class RimImmortalDetection
    {
        #region Mod Detection

        /// <summary>
        /// Multiple possible mod IDs for RimImmortal.
        /// </summary>
        private static readonly string[] RIMMORTAL_VARIANTS = new[]
        {
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

        // Core.Pawn_EnergyTracker
        private static Type _pawnEnergyTrackerType;
        private static MethodInfo _setEnergyMethod;
        private static FieldInfo _energyTrackerPawnField;

        // RIRitualFramework.MessageDialog
        private static Type _messageDialogType;
        private static MethodInfo _getFloatUpgradeMethod;
        private static FieldInfo _sucessRateField;
        private static FieldInfo _messageDialogPawnField;

        /// <summary>
        /// Gets the SetEnergy method from Pawn_EnergyTracker.
        /// </summary>
        public static MethodInfo SetEnergyMethod
        {
            get
            {
                EnsureTypesInitialized();
                return _setEnergyMethod;
            }
        }

        /// <summary>
        /// Gets the GetFloatUpgrade method from MessageDialog.
        /// </summary>
        public static MethodInfo GetFloatUpgradeMethod
        {
            get
            {
                EnsureTypesInitialized();
                return _getFloatUpgradeMethod;
            }
        }

        /// <summary>
        /// Gets the pawn field from Pawn_EnergyTracker.
        /// </summary>
        public static FieldInfo EnergyTrackerPawnField
        {
            get
            {
                EnsureTypesInitialized();
                return _energyTrackerPawnField;
            }
        }

        /// <summary>
        /// Gets the sucessRate field from MessageDialog.
        /// </summary>
        public static FieldInfo SucessRateField
        {
            get
            {
                EnsureTypesInitialized();
                return _sucessRateField;
            }
        }

        /// <summary>
        /// Gets the pawn field from MessageDialog.
        /// </summary>
        public static FieldInfo MessageDialogPawnField
        {
            get
            {
                EnsureTypesInitialized();
                return _messageDialogPawnField;
            }
        }

        #endregion

        #region Type Initialization

        /// <summary>
        /// Lazily initializes RimImmortal types via reflection.
        /// </summary>
        private static void EnsureTypesInitialized()
        {
            if (_typesInitialized)
                return;

            _typesInitialized = true;

            if (!RimImmortalActive)
                return;

            try
            {
                // Core.Pawn_EnergyTracker - handles cultivation energy
                _pawnEnergyTrackerType = AccessTools.TypeByName("Core.Pawn_EnergyTracker");
                if (_pawnEnergyTrackerType != null)
                {
                    _setEnergyMethod = AccessTools.Method(_pawnEnergyTrackerType, "SetEnergy", new[] { typeof(float) });
                    _energyTrackerPawnField = AccessTools.Field(_pawnEnergyTrackerType, "pawn");

                    if (_setEnergyMethod == null)
                    {
                        Log.Warning("[Eternal] RimImmortal: SetEnergy method not found on Pawn_EnergyTracker");
                    }
                }
                else
                {
                    Log.Warning("[Eternal] RimImmortal detected but Pawn_EnergyTracker type not found");
                }

                // RIRitualFramework.MessageDialog - handles breakthrough chance calculation
                _messageDialogType = AccessTools.TypeByName("RIRitualFramework.MessageDialog");
                if (_messageDialogType != null)
                {
                    _getFloatUpgradeMethod = AccessTools.Method(_messageDialogType, "GetFloatUpgrade");
                    _sucessRateField = AccessTools.Field(_messageDialogType, "sucessRate");
                    _messageDialogPawnField = AccessTools.Field(_messageDialogType, "pawn");

                    if (_getFloatUpgradeMethod == null)
                    {
                        Log.Warning("[Eternal] RimImmortal: GetFloatUpgrade method not found on MessageDialog");
                    }
                    if (_sucessRateField == null)
                    {
                        Log.Warning("[Eternal] RimImmortal: sucessRate field not found on MessageDialog");
                    }
                }
                else
                {
                    Log.Warning("[Eternal] RimImmortal detected but MessageDialog type not found");
                }

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] RimImmortal types initialized: " +
                        $"EnergyTracker={_pawnEnergyTrackerType != null}, " +
                        $"SetEnergy={_setEnergyMethod != null}, " +
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

        /// <summary>
        /// Resets the type cache (for testing or mod reload scenarios).
        /// </summary>
        public static void ResetTypeCache()
        {
            _typesInitialized = false;
            _pawnEnergyTrackerType = null;
            _setEnergyMethod = null;
            _energyTrackerPawnField = null;
            _messageDialogType = null;
            _getFloatUpgradeMethod = null;
            _sucessRateField = null;
            _messageDialogPawnField = null;
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Gets the pawn from a Pawn_EnergyTracker instance.
        /// </summary>
        public static Pawn GetPawnFromEnergyTracker(object energyTracker)
        {
            if (energyTracker == null || EnergyTrackerPawnField == null)
                return null;

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
                return null;

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
                return 0f;

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
                return;

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

        /// <summary>
        /// Logs RimImmortal detection status (for debugging).
        /// </summary>
        public static void LogRimImmortalStatus()
        {
            if (Eternal_Mod.settings?.debugMode != true)
                return;

            Log.Message($"[Eternal] RimImmortal Detection Status:");
            Log.Message($"  - RimImmortal Active: {(RimImmortalActive ? "Yes" : "No")}");

            if (RimImmortalActive)
            {
                EnsureTypesInitialized();
                Log.Message($"  - EnergyTracker Type: {(_pawnEnergyTrackerType != null ? "Found" : "Not Found")}");
                Log.Message($"  - SetEnergy Method: {(_setEnergyMethod != null ? "Found" : "Not Found")}");
                Log.Message($"  - MessageDialog Type: {(_messageDialogType != null ? "Found" : "Not Found")}");
                Log.Message($"  - GetFloatUpgrade Method: {(_getFloatUpgradeMethod != null ? "Found" : "Not Found")}");
                Log.Message($"  - SucessRate Field: {(_sucessRateField != null ? "Found" : "Not Found")}");
            }
        }

        #endregion
    }
}
