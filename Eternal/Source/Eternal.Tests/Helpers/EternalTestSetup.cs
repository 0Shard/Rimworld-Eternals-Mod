// Relative Path: Eternal/Source/Eternal.Tests/Helpers/EternalTestSetup.cs
// Creation Date: 24-02-2026
// Last Edit: 24-02-2026
// Author: 0Shard
// Description: Static settings initializer for tests that hit Eternal_Mod.GetSettings().
//              UnifiedFoodDebtManager.MaxDebtMultiplier reads Eternal_Mod.GetSettings().maxDebtMultiplier
//              which is a static call. Without initialization, it creates a new Eternal_Settings()
//              internally (GetSettings fallback), which requires RimWorld types at construction time.
//              This helper ensures the static field is initialized before test execution.

using System;
using System.Reflection;

namespace Eternal.Tests.Helpers
{
    /// <summary>
    /// Initializes <c>Eternal_Mod.settings</c> for test contexts where no RimWorld game is loaded.
    /// Uses reflection to set the static field and construct <c>Eternal_Settings</c> to avoid
    /// direct type references that would trigger Assembly-CSharp JIT loading.
    /// </summary>
    public static class EternalTestSetup
    {
        private static bool _initialized;

        /// <summary>
        /// Sets <c>Eternal_Mod.settings</c> to a new <c>Eternal_Settings</c> instance via reflection.
        /// Safe to call multiple times — only runs once per test process.
        /// </summary>
        public static void InitializeSettings()
        {
            if (_initialized)
                return;

            try
            {
                // Use reflection to avoid direct type reference to Eternal_Mod/Eternal_Settings
                // which would trigger Assembly-CSharp loading at the calling method's JIT time.
                var modType = Type.GetType("Eternal.Eternal_Mod, Eternal");
                var settingsType = Type.GetType("Eternal.Eternal_Settings, Eternal");

                if (modType == null || settingsType == null)
                    return;

                var settingsField = modType.GetField("settings", BindingFlags.Public | BindingFlags.Static);
                if (settingsField == null)
                    return;

                // Create Eternal_Settings instance via reflection
                var settingsInstance = Activator.CreateInstance(settingsType);
                settingsField.SetValue(null, settingsInstance);

                _initialized = true;
            }
            catch (Exception)
            {
                // If reflection fails (e.g., Assembly-CSharp not available),
                // tests that need settings will use GetSettings() fallback
            }
        }

        /// <summary>
        /// Resets <c>Eternal_Mod.settings</c> to null (cleanup after tests).
        /// </summary>
        public static void ResetSettings()
        {
            try
            {
                var modType = Type.GetType("Eternal.Eternal_Mod, Eternal");
                var settingsField = modType?.GetField("settings", BindingFlags.Public | BindingFlags.Static);
                settingsField?.SetValue(null, null);
                _initialized = false;
            }
            catch (Exception)
            {
                // Cleanup is best-effort
            }
        }
    }
}
