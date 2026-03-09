// Relative Path: Eternal/Source/Eternal/EternalModState.cs
// Creation Date: 21-02-2026
// Last Edit: 21-02-2026
// Author: 0Shard
// Description: Global kill switch for the Eternal mod.
//              When critical defs are missing (bad mod load order, XML errors),
//              IsDisabled is set to true and all entry points (tick, gizmo, patches)
//              early-return to prevent NRE floods. Reset() is called each session
//              so fixing mod order without a full game restart recovers cleanly.

using Verse;

namespace Eternal
{
    /// <summary>
    /// Static kill switch for the Eternal mod.
    /// Set to disabled when critical DefOf bindings fail at startup.
    /// All mod entry points (tick processing, gizmos, Harmony patches) check
    /// <see cref="IsDisabled"/> and return immediately when true.
    /// </summary>
    public static class EternalModState
    {
        /// <summary>
        /// Whether the mod is currently disabled due to missing critical defs.
        /// When true, all entry points early-return to prevent per-tick NRE floods.
        /// </summary>
        public static bool IsDisabled { get; private set; } = false;

        /// <summary>
        /// Disables the mod and logs one actionable error message.
        /// Idempotent: calling Disable() when already disabled is a no-op.
        /// </summary>
        /// <param name="reason">Human-readable reason for disabling (e.g. "Missing critical defs: X, Y").</param>
        public static void Disable(string reason)
        {
            if (IsDisabled)
                return;

            IsDisabled = true;
            Log.Error($"[Eternal] DISABLED: {reason} Check mod load order.");
        }

        /// <summary>
        /// Resets the kill switch to false.
        /// Called at the start of <c>EternalDefOf.VerifyBindings()</c> so that
        /// fixing mod load order between sessions recovers without a full game restart.
        /// </summary>
        public static void Reset()
        {
            IsDisabled = false;
        }
    }
}
