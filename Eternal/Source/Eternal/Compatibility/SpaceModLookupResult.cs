// Relative Path: Eternal/Source/Eternal/Compatibility/SpaceModLookupResult.cs
// Creation Date: 21-02-2026
// Last Edit: 21-02-2026
// Author: 0Shard
// Description: Tri-state result type for SOS2 reflection-based lookups.
//              Discriminates between Success (mod present, value read), ModNotPresent (SOS2 not loaded),
//              and ReflectionFailed (mod loaded but types could not be resolved).
//              Used by SOS2ReflectionCache to surface exact failure modes to callers.

namespace Eternal.Compatibility
{
    /// <summary>
    /// Status codes for SOS2/space mod reflection lookups.
    /// </summary>
    public enum SpaceModLookupStatus : byte
    {
        /// <summary>Mod present and value was read successfully.</summary>
        Success,

        /// <summary>The space mod is not loaded — operation is vacuously safe to ignore.</summary>
        ModNotPresent,

        /// <summary>Mod is loaded but type/field resolution failed — caller should degrade gracefully.</summary>
        ReflectionFailed
    }

    /// <summary>
    /// Readonly value type representing a tri-state bool result from a SOS2 reflection lookup.
    /// Stack-allocated, zero GC pressure — consistent with the project's readonly struct convention
    /// established in Phase 1 (ImmutableSettingsSnapshot nested record structs).
    /// </summary>
    public readonly struct BoolLookupResult
    {
        /// <summary>Discriminator indicating why the lookup succeeded or failed.</summary>
        public readonly SpaceModLookupStatus Status;

        /// <summary>The bool value. Only meaningful when <see cref="Status"/> is <see cref="SpaceModLookupStatus.Success"/>.</summary>
        public readonly bool Value;

        private BoolLookupResult(SpaceModLookupStatus status, bool value)
        {
            Status = status;
            Value = value;
        }

        // ----------------------------------------------------------------
        // Factory methods
        // ----------------------------------------------------------------

        /// <summary>Returns a success result with Value = true.</summary>
        public static BoolLookupResult SuccessTrue() =>
            new BoolLookupResult(SpaceModLookupStatus.Success, true);

        /// <summary>Returns a success result with Value = false.</summary>
        public static BoolLookupResult SuccessFalse() =>
            new BoolLookupResult(SpaceModLookupStatus.Success, false);

        /// <summary>Returns a result indicating the space mod is not loaded.</summary>
        public static BoolLookupResult ModNotPresent() =>
            new BoolLookupResult(SpaceModLookupStatus.ModNotPresent, false);

        /// <summary>Returns a result indicating reflection failed (mod loaded but types unresolvable).</summary>
        public static BoolLookupResult ReflectionFailed() =>
            new BoolLookupResult(SpaceModLookupStatus.ReflectionFailed, false);

        // ----------------------------------------------------------------
        // Convenience properties
        // ----------------------------------------------------------------

        /// <summary>True when the mod was present and the value was read successfully.</summary>
        public bool IsSuccess => Status == SpaceModLookupStatus.Success;

        /// <summary>True only when Status is Success AND Value is true. Safe for callers to use as a bool gate.</summary>
        public bool IsTrue => Status == SpaceModLookupStatus.Success && Value;
    }
}
