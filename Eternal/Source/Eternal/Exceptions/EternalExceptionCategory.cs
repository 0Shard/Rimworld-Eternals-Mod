/*
 * Relative Path: Eternal/Source/Eternal/Exceptions/EternalExceptionCategory.cs
 * Creation Date: 19-02-2026
 * Last Edit: 20-02-2026
 * Author: 0Shard
 * Description: Defines the exception category taxonomy used throughout Eternal for structured error handling.
 *              Category alone determines log severity — no per-site severity overrides.
 *              DataInconsistency, GameStateInvalid, InternalError, HediffSwap, Resurrection, CorpseTracking map to Log.Error.
 *              CompatibilityFailure, ConfigurationError, Regrowth, Snapshot, MapProtection map to Log.Warning.
 */

namespace Eternal.Exceptions
{
    /// <summary>
    /// Taxonomy of exception categories for Eternal mod. Each category determines the
    /// log severity (Error vs Warning) and prefix used in <see cref="Eternal.Utils.EternalLogger.HandleException"/>.
    /// </summary>
    public enum EternalExceptionCategory
    {
        /// <summary>
        /// Save data or pawn state is corrupt or internally inconsistent.
        /// Severity: <c>Log.Error</c> — prefix <c>[Eternal:DataInconsistency]</c>.
        /// </summary>
        DataInconsistency,

        /// <summary>
        /// Reflection or interop with an optional mod failed (mod not loaded, API surface changed, etc.).
        /// Severity: <c>Log.Warning</c> — prefix <c>[Eternal:CompatFailure]</c>.
        /// </summary>
        CompatibilityFailure,

        /// <summary>
        /// RimWorld returned an unexpected game state (null corpse, missing map, unexpected enum value, etc.).
        /// Severity: <c>Log.Error</c> — prefix <c>[Eternal:GameStateInvalid]</c>.
        /// </summary>
        GameStateInvalid,

        /// <summary>
        /// Programming error that should never occur in correct code (violated invariant, unreachable branch, etc.).
        /// Severity: <c>Log.Error</c> — prefix <c>[Eternal:InternalError]</c>.
        /// </summary>
        InternalError,

        /// <summary>
        /// User-provided XML definition or mod setting value is invalid or out of range.
        /// Severity: <c>Log.Warning</c> — prefix <c>[Eternal:ConfigError]</c>.
        /// </summary>
        ConfigurationError,

        /// <summary>
        /// HediffSet swap operation failed during CompleteResurrection or ResurrectImmediately.
        /// Severity: <c>Log.Error</c> — prefix <c>[Eternal:HediffSwap]</c>.
        /// </summary>
        HediffSwap,

        /// <summary>
        /// General resurrection pipeline failure (outside of HediffSet swap).
        /// Severity: <c>Log.Error</c> — prefix <c>[Eternal:Resurrection]</c>.
        /// </summary>
        Resurrection,

        /// <summary>
        /// 4-phase body part regrowth failure (phase transition or progress calculation error).
        /// Severity: <c>Log.Warning</c> — prefix <c>[Eternal:Regrowth]</c>.
        /// </summary>
        Regrowth,

        /// <summary>
        /// PawnAssignmentSnapshot capture or restore failure.
        /// Severity: <c>Log.Warning</c> — prefix <c>[Eternal:Snapshot]</c>.
        /// </summary>
        Snapshot,

        /// <summary>
        /// EternalCorpseManager or EternalCorpsePreservation tracking failure.
        /// Severity: <c>Log.Error</c> — prefix <c>[Eternal:CorpseTracking]</c>.
        /// </summary>
        CorpseTracking,

        /// <summary>
        /// Map protection or scheduled corpse teleportation failure.
        /// Severity: <c>Log.Warning</c> — prefix <c>[Eternal:MapProtection]</c>.
        /// </summary>
        MapProtection,
    }
}
