/*
 * Relative Path: Eternal/Source/Eternal/Utils/EternalLogger.cs
 * Creation Date: 29-10-2025
 * Last Edit: 20-02-2026
 *              Task 01-02: CurrentLogLevel simplified to use Eternal_Mod.GetSettings() (SAFE-08).
 *              Task 03-01: HandleException extended with count-based coalescing (threshold: 100)
 *                          and 6 new categories (HediffSwap, Resurrection, Regrowth, Snapshot,
 *                          CorpseTracking, MapProtection). isErrorLevel updated accordingly.
 *                          Warning-level stack trace guard updated to use GetSettings() (SAFE-08).
 * Author: 0Shard
 * Description: Centralized logging utility for Eternal mod with enhanced error handling and context information.
 *              Provides structured logging with different levels and automatic context capture.
 *              HandleException dispatches to Log.Error or Log.Warning based on EternalExceptionCategory,
 *              with count-based coalescing to suppress repeated same-pawn/same-category log spam.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Verse;
using Eternal.Exceptions;

namespace Eternal.Utils
{
    /// <summary>
    /// Centralized logging utility for Eternal mod with enhanced error handling and context information.
    /// Provides structured logging with different levels and automatic context capture.
    /// </summary>
    public static class EternalLogger
    {
        /// <summary>
        /// Enumeration of logging levels for Eternal mod.
        /// </summary>
        public enum LogLevel
        {
            Error = 0,
            Warning = 1,
            Info = 2,
            Debug = 3
        }

        /// <summary>
        /// Coalescing dictionary tracking failure counts per (pawnId, category) pair.
        /// HandleException logs on the 1st occurrence and every 100th thereafter to suppress spam.
        /// </summary>
        private static readonly Dictionary<(string pawnId, EternalExceptionCategory cat), int> _failureCounts
            = new Dictionary<(string, EternalExceptionCategory), int>();

        /// <summary>
        /// Gets the current logging level from settings.
        /// Uses GetSettings() for guaranteed non-null access (SAFE-08).
        /// </summary>
        private static LogLevel CurrentLogLevel
        {
            get
            {
                try
                {
                    return (LogLevel)Eternal_Mod.GetSettings().loggingLevel;
                }
                catch (Exception ex)
                {
                    // Fallback to Warning level if settings are unavailable
                    Log.Error($"[Eternal] Failed to get logging level: {ex.Message}");
                }
                return LogLevel.Warning;
            }
        }

        /// <summary>
        /// Logs an error message with optional context information.
        /// </summary>
        /// <param name="message">The error message to log.</param>
        /// <param name="operation">The operation that was being performed.</param>
        /// <param name="pawn">The pawn associated with the error.</param>
        /// <param name="map">The map associated with the error.</param>
        /// <param name="exception">The exception that caused the error.</param>
        public static void Error(string message, string operation = null, Pawn pawn = null, Verse.Map map = null, Exception exception = null)
        {
            LogMessage(LogLevel.Error, message, operation, pawn, map, exception);
        }

        /// <summary>
        /// Logs a warning message with optional context information.
        /// </summary>
        /// <param name="message">The warning message to log.</param>
        /// <param name="operation">The operation that was being performed.</param>
        /// <param name="pawn">The pawn associated with the warning.</param>
        /// <param name="map">The map associated with the warning.</param>
        public static void Warning(string message, string operation = null, Pawn pawn = null, Verse.Map map = null)
        {
            LogMessage(LogLevel.Warning, message, operation, pawn, map);
        }

        /// <summary>
        /// Logs an info message with optional context information.
        /// </summary>
        /// <param name="message">The info message to log.</param>
        /// <param name="operation">The operation that was being performed.</param>
        /// <param name="pawn">The pawn associated with the info.</param>
        /// <param name="map">The map associated with the info.</param>
        public static void Info(string message, string operation = null, Pawn pawn = null, Verse.Map map = null)
        {
            LogMessage(LogLevel.Info, message, operation, pawn, map);
        }

        /// <summary>
        /// Logs a debug message with optional context information.
        /// </summary>
        /// <param name="message">The debug message to log.</param>
        /// <param name="operation">The operation that was being performed.</param>
        /// <param name="pawn">The pawn associated with the debug info.</param>
        /// <param name="map">The map associated with the debug info.</param>
        public static void Debug(string message, string operation = null, Pawn pawn = null, Verse.Map map = null)
        {
            LogMessage(LogLevel.Debug, message, operation, pawn, map);
        }

        /// <summary>
        /// Logs an EternalException with full context information.
        /// </summary>
        /// <param name="exception">The EternalException to log.</param>
        public static void LogException(EternalException exception)
        {
            if (exception == null)
            {
                Log.Error("[Eternal] Attempted to log null exception");
                return;
            }

            LogMessage(LogLevel.Error, exception.GetFormattedMessage(), exception.Operation, exception.Pawn, exception.Map as Verse.Map, exception);
        }

        /// <summary>
        /// Logs a generic exception with context information.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="operation">The operation that was being performed.</param>
        /// <param name="pawn">The pawn associated with the exception.</param>
        /// <param name="map">The map associated with the exception.</param>
        public static void LogException(Exception exception, string operation = null, Pawn pawn = null, Verse.Map map = null)
        {
            if (exception == null)
            {
                Log.Error("[Eternal] Attempted to log null exception");
                return;
            }

            string message = $"Exception: {exception.Message}";
            if (!string.IsNullOrEmpty(operation))
            {
                message += $" (Operation: {operation})";
            }

            LogMessage(LogLevel.Error, message, operation, pawn, map, exception);
        }

        /// <summary>
        /// Handles an exception using the category taxonomy to determine log severity and prefix.
        /// Coalesces repeated failures: logs on 1st occurrence and every 100th thereafter for the
        /// same (pawn, category) pair to prevent log spam from tight loops.
        /// DataInconsistency, GameStateInvalid, InternalError, HediffSwap, Resurrection, and
        /// CorpseTracking dispatch to <c>Log.Error</c>.
        /// CompatibilityFailure, ConfigurationError, Regrowth, Snapshot, and MapProtection
        /// dispatch to <c>Log.Warning</c>.
        /// Stack traces are included for error-level categories. For warning-level categories,
        /// stack traces are gated behind <c>debugMode</c>.
        /// </summary>
        /// <param name="category">The exception category that determines severity and prefix.</param>
        /// <param name="operation">The operation that was being performed (may be null).</param>
        /// <param name="pawn">The pawn associated with the exception (may be null).</param>
        /// <param name="exception">The exception that was caught (may be null).</param>
        public static void HandleException(EternalExceptionCategory category, string operation, Pawn pawn, Exception exception)
        {
            try
            {
                // --- Coalescing: suppress repeated same-pawn/same-category failures ---
                string pawnId = pawn?.ThingID ?? "unknown";
                var coalesceKey = (pawnId, category);
                _failureCounts.TryGetValue(coalesceKey, out int failureCount);
                failureCount++;
                _failureCounts[coalesceKey] = failureCount;

                bool shouldLog = (failureCount == 1) || (failureCount % 100 == 0);
                if (!shouldLog)
                    return;

                // --- Format message ---
                string prefix = GetCategoryPrefix(category);
                string context = BuildContext(operation, pawn);
                string baseMessage = exception != null
                    ? $"{prefix} {exception.Message}"
                    : $"{prefix} (no exception)";

                if (context.Length > 0)
                    baseMessage += $" ({context})";

                if (failureCount > 1)
                    baseMessage += $" (repeated {failureCount} times)";

                // --- Severity routing ---
                bool isErrorLevel = category == EternalExceptionCategory.DataInconsistency
                    || category == EternalExceptionCategory.GameStateInvalid
                    || category == EternalExceptionCategory.InternalError
                    || category == EternalExceptionCategory.HediffSwap
                    || category == EternalExceptionCategory.Resurrection
                    || category == EternalExceptionCategory.CorpseTracking;

                if (isErrorLevel)
                {
                    string fullMessage = exception != null
                        ? $"{baseMessage}\nStack Trace: {exception.StackTrace}"
                        : baseMessage;
                    Log.Error(fullMessage);
                }
                else
                {
                    // Warning-level: include stack trace only in debug mode
                    if (exception != null && Eternal_Mod.GetSettings().debugMode)
                    {
                        Log.Warning($"{baseMessage}\nStack Trace: {exception.StackTrace}");
                    }
                    else
                    {
                        Log.Warning(baseMessage);
                    }
                }
            }
            catch (Exception logEx)
            {
                Log.Error($"[Eternal] Critical logging failure in HandleException: {logEx.Message}");
                if (exception != null)
                    Log.Error($"[Eternal] Original exception was: {exception.Message}");
            }
        }

        /// <summary>
        /// Returns the abbreviated log prefix for a given exception category.
        /// </summary>
        private static string GetCategoryPrefix(EternalExceptionCategory category)
        {
            switch (category)
            {
                case EternalExceptionCategory.DataInconsistency:   return "[Eternal:DataInconsistency]";
                case EternalExceptionCategory.CompatibilityFailure: return "[Eternal:CompatFailure]";
                case EternalExceptionCategory.GameStateInvalid:    return "[Eternal:GameStateInvalid]";
                case EternalExceptionCategory.InternalError:       return "[Eternal:InternalError]";
                case EternalExceptionCategory.ConfigurationError:  return "[Eternal:ConfigError]";
                case EternalExceptionCategory.HediffSwap:          return "[Eternal:HediffSwap]";
                case EternalExceptionCategory.Resurrection:        return "[Eternal:Resurrection]";
                case EternalExceptionCategory.Regrowth:            return "[Eternal:Regrowth]";
                case EternalExceptionCategory.Snapshot:            return "[Eternal:Snapshot]";
                case EternalExceptionCategory.CorpseTracking:      return "[Eternal:CorpseTracking]";
                case EternalExceptionCategory.MapProtection:       return "[Eternal:MapProtection]";
                default:                                           return "[Eternal:Unknown]";
            }
        }

        /// <summary>
        /// Builds a compact context suffix from operation and pawn, returning empty string when both are null.
        /// </summary>
        private static string BuildContext(string operation, Pawn pawn)
        {
            if (string.IsNullOrEmpty(operation) && pawn == null)
                return string.Empty;

            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            if (!string.IsNullOrEmpty(operation))
                sb.Append($"Op: {operation}");

            if (pawn != null)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append($"Pawn: {pawn.Name?.ToStringFull ?? "Unknown"}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Core logging method that handles all log messages with context information.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="operation">The operation that was being performed.</param>
        /// <param name="pawn">The pawn associated with the log.</param>
        /// <param name="map">The map associated with the log.</param>
        /// <param name="exception">The exception associated with the log.</param>
        private static void LogMessage(LogLevel level, string message, string operation = null, Pawn pawn = null, Verse.Map map = null, Exception exception = null)
        {
            // Check if this log level should be logged
            if (level > CurrentLogLevel)
            {
                return;
            }

            try
            {
                StringBuilder logBuilder = new StringBuilder();
                logBuilder.Append($"[Eternal] [{level}] {message}");

                // Add context information
                StringBuilder contextBuilder = new StringBuilder();

                if (!string.IsNullOrEmpty(operation))
                {
                    contextBuilder.Append($"Op: {operation}");
                }

                if (pawn != null)
                {
                    if (contextBuilder.Length > 0)
                        contextBuilder.Append(", ");
                    contextBuilder.Append($"Pawn: {pawn.Name?.ToStringFull ?? "Unknown"}");
                }

                if (map != null)
                {
                    if (contextBuilder.Length > 0)
                        contextBuilder.Append(", ");
                    contextBuilder.Append($"Map: {map}");
                }

                if (contextBuilder.Length > 0)
                {
                    logBuilder.Append($" (Context: {contextBuilder})");
                }

                string finalMessage = logBuilder.ToString();

                // Log using RimWorld's logging system
                switch (level)
                {
                    case LogLevel.Error:
                        if (exception != null)
                        {
                            Log.Error($"{finalMessage}\nStack Trace: {exception.StackTrace}");
                        }
                        else
                        {
                            Log.Error(finalMessage);
                        }
                        break;
                    case LogLevel.Warning:
                        Log.Warning(finalMessage);
                        break;
                    case LogLevel.Info:
                        Log.Message(finalMessage);
                        break;
                    case LogLevel.Debug:
                        // Only log debug messages if debug mode is enabled
                        if (Eternal_Mod.settings?.debugMode == true)
                        {
                            Log.Message(finalMessage);
                        }
                        break;
                }
            }
            catch (Exception logEx)
            {
                // Fallback logging if the logging system itself fails
                Log.Error($"[Eternal] Critical logging failure: {logEx.Message}");
                Log.Error($"[Eternal] Original message was: {message}");
            }
        }
    }
}
