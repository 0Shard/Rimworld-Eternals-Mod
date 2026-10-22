// Relative Path: Eternal/Source/Eternal/InGameTests/Helpers/SettingsScope.cs
// Creation Date: 13-03-2026
// Last Edit: 13-03-2026
// Author: 0Shard
// Description: IDisposable save/restore wrapper for Eternal v1.0.1 settings fields.
//              Tests use this in a `using` block to mutate settings temporarily and have
//              them automatically restored when the block exits, even on assertion failure.
//              Only covers the six Effects fields added in v1.0.1.

#if DEBUG

namespace Eternal.InGameTests.Helpers
{
    /// <summary>
    /// Captures all v1.0.1 Effects settings on construction and restores them on Dispose().
    /// Usage: using (new SettingsScope()) { /* mutate settings freely */ }
    /// </summary>
    public class SettingsScope : System.IDisposable
    {
        private readonly Eternal_Settings _settings;
        // Snapshot of v1.0.1 effects settings taken at construction time
        private readonly bool _consciousnessBuffEnabled;
        private readonly float _consciousnessMultiplier;
        private readonly bool _moodBuffEnabled;
        private readonly int _moodBuffValue;
        private readonly bool _populationCapEnabled;
        private readonly int _populationCap;

        /// <summary>
        /// Saves current v1.0.1 settings values so they can be restored later.
        /// </summary>
        public SettingsScope()
        {
            var s = Eternal_Mod.settings;
            TestAssert.Available(s, "Eternal effects settings");
            _settings = s;

            _consciousnessBuffEnabled = s.consciousnessBuffEnabled;
            _consciousnessMultiplier = s.consciousnessMultiplier;
            _moodBuffEnabled = s.moodBuffEnabled;
            _moodBuffValue = s.moodBuffValue;
            _populationCapEnabled = s.populationCapEnabled;
            _populationCap = s.populationCap;
        }

        /// <summary>
        /// Restores all captured settings values to their original state.
        /// Called automatically at end of `using` block.
        /// </summary>
        public void Dispose()
        {
            var s = _settings;
            bool sameIdentity = object.ReferenceEquals(s, Eternal_Mod.settings);

            s.consciousnessBuffEnabled = _consciousnessBuffEnabled;
            s.consciousnessMultiplier = _consciousnessMultiplier;
            s.moodBuffEnabled = _moodBuffEnabled;
            s.moodBuffValue = _moodBuffValue;
            s.populationCapEnabled = _populationCapEnabled;
            s.populationCap = _populationCap;
            Eternal_Mod.settings = s;
            TestAssert.IsTrue(sameIdentity,
                "Effects settings identity must stay unchanged inside SettingsScope");
        }
    }
}

#endif
