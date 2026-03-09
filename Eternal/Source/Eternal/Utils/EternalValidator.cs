// file path: Eternal/Source/Eternal/Utils/EternalValidator.cs
// Author Name: 0Shard
// Date Created: 29-10-2025
// Date Last Modified: 22-01-2026
// Description: Validation utility class for Eternal mod with comprehensive input validation and defensive programming.

using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Eternal.Exceptions;
using Eternal.Extensions;

namespace Eternal.Utils
{
    /// <summary>
    /// Validation utility class for Eternal mod with comprehensive input validation and defensive programming.
    /// Provides centralized validation methods for common operations and parameters.
    /// </summary>
    public static class EternalValidator
    {
        /// <summary>
        /// Validates that a pawn is not null and is in a valid state.
        /// </summary>
        /// <param name="pawn">The pawn to validate.</param>
        /// <param name="parameterName">The name of the parameter being validated.</param>
        /// <param name="operation">The operation being performed.</param>
        /// <exception cref="EternalValidationException">Thrown when pawn is invalid.</exception>
        public static void ValidatePawn(Pawn pawn, string parameterName = "pawn", string operation = null)
        {
            if (pawn == null)
            {
                var ex = new EternalValidationException(
                    "Pawn cannot be null",
                    parameterName,
                    operation);
                throw ex;
            }
            
            if (pawn.Destroyed)
            {
                var ex = new EternalValidationException(
                    "Pawn cannot be destroyed",
                    parameterName,
                    pawn,
                    operation);
                throw ex;
            }
            
            if (pawn.health == null)
            {
                var ex = new EternalValidationException(
                    "Pawn health cannot be null",
                    parameterName,
                    pawn,
                    operation);
                throw ex;
            }
            
            if (pawn.health.hediffSet == null)
            {
                var ex = new EternalValidationException(
                    "Pawn hediff set cannot be null",
                    parameterName,
                    pawn,
                    operation);
                throw ex;
            }
        }
        
        /// <summary>
        /// Validates that a pawn has the Eternal trait.
        /// </summary>
        /// <param name="pawn">The pawn to validate.</param>
        /// <param name="parameterName">The name of the parameter being validated.</param>
        /// <param name="operation">The operation being performed.</param>
        /// <exception cref="EternalValidationException">Thrown when pawn doesn't have Eternal trait.</exception>
        public static void ValidateEternalPawn(Pawn pawn, string parameterName = "pawn", string operation = null)
        {
            ValidatePawn(pawn, parameterName, operation);
            
            if (pawn.story?.traits == null)
            {
                var ex = new EternalValidationException(
                    "Pawn story traits cannot be null",
                    parameterName,
                    pawn,
                    operation);
                throw ex;
            }
            
            if (!pawn.HasTraitIgnoringSuppression(EternalDefOf.Eternal_GeneticMarker))
            {
                var ex = new EternalValidationException(
                    "Pawn must have Eternal trait",
                    parameterName,
                    pawn,
                    operation);
                throw ex;
            }
        }
        
        /// <summary>
        /// Validates that a numeric value is within the specified range.
        /// </summary>
        /// <param name="value">The value to validate.</param>
        /// <param name="min">The minimum allowed value.</param>
        /// <param name="max">The maximum allowed value.</param>
        /// <param name="parameterName">The name of the parameter being validated.</param>
        /// <param name="operation">The operation being performed.</param>
        /// <exception cref="EternalValidationException">Thrown when value is outside range.</exception>
        public static void ValidateRange(float value, float min, float max, string parameterName, string operation = null)
        {
            if (value < min || value > max)
            {
                var ex = new EternalValidationException(
                    $"Value must be between {min} and {max}",
                    parameterName,
                    value,
                    $"[{min}, {max}]",
                    operation);
                throw ex;
            }
        }
        
        /// <summary>
        /// Validates that a numeric value is within the specified range.
        /// </summary>
        /// <param name="value">The value to validate.</param>
        /// <param name="min">The minimum allowed value.</param>
        /// <param name="max">The maximum allowed value.</param>
        /// <param name="parameterName">The name of the parameter being validated.</param>
        /// <param name="operation">The operation being performed.</param>
        /// <exception cref="EternalValidationException">Thrown when value is outside range.</exception>
        public static void ValidateRange(int value, int min, int max, string parameterName, string operation = null)
        {
            if (value < min || value > max)
            {
                var ex = new EternalValidationException(
                    $"Value must be between {min} and {max}",
                    parameterName,
                    value,
                    $"[{min}, {max}]",
                    operation);
                throw ex;
            }
        }
        
    }
}