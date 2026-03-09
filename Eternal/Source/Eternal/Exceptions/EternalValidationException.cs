// file path: Eternal/Source/Eternal/Exceptions/EternalValidationException.cs
// Author Name: 0Shard
// Date Created: 29-10-2025
// Date Last Modified: 29-10-2025
// Description: Exception class for validation errors in Eternal mod.

using System;
using System.Collections.Generic;
using Verse;

namespace Eternal.Exceptions
{
    /// <summary>
    /// Exception class for validation errors in Eternal mod.
    /// Thrown when input validation fails or invalid states are detected.
    /// </summary>
    public class EternalValidationException : EternalException
    {
        /// <summary>
        /// The name of the parameter that failed validation.
        /// </summary>
        public string ParameterName { get; set; }
        
        /// <summary>
        /// The value that failed validation.
        /// </summary>
        public object InvalidValue { get; set; }
        
        /// <summary>
        /// The expected value or range for the parameter.
        /// </summary>
        public string ExpectedValue { get; set; }
        
        /// <summary>
        /// Collection of validation errors, if multiple validations failed.
        /// </summary>
        public List<string> ValidationErrors { get; }
        
        /// <summary>
        /// Initializes a new instance of EternalValidationException class.
        /// </summary>
        public EternalValidationException() : base()
        {
            ValidationErrors = new List<string>();
        }
        
        /// <summary>
        /// Initializes a new instance of EternalValidationException class with a specified error message.
        /// </summary>
        /// <param name="message">The message that describes error.</param>
        public EternalValidationException(string message) : base(message)
        {
            ValidationErrors = new List<string>();
        }
        
        /// <summary>
        /// Initializes a new instance of EternalValidationException class with a specified error message and operation.
        /// </summary>
        /// <param name="message">The message that describes error.</param>
        /// <param name="operation">The operation that was being performed.</param>
        public EternalValidationException(string message, string operation) : base(message, operation)
        {
            ValidationErrors = new List<string>();
        }
        
        /// <summary>
        /// Initializes a new instance of EternalValidationException class with a specified error message, parameter name, and invalid value.
        /// </summary>
        /// <param name="message">The message that describes error.</param>
        /// <param name="parameterName">The name of the parameter that failed validation.</param>
        /// <param name="invalidValue">The value that failed validation.</param>
        public EternalValidationException(string message, string parameterName, object invalidValue) : base(message)
        {
            ParameterName = parameterName;
            InvalidValue = invalidValue;
            ValidationErrors = new List<string>();
        }
        
        /// <summary>
        /// Initializes a new instance of EternalValidationException class with a specified error message, parameter name, invalid value, and operation.
        /// </summary>
        /// <param name="message">The message that describes error.</param>
        /// <param name="parameterName">The name of the parameter that failed validation.</param>
        /// <param name="invalidValue">The value that failed validation.</param>
        /// <param name="operation">The operation that was being performed.</param>
        public EternalValidationException(string message, string parameterName, object invalidValue, string operation) : base(message, operation)
        {
            ParameterName = parameterName;
            InvalidValue = invalidValue;
            ValidationErrors = new List<string>();
        }
        
        /// <summary>
        /// Initializes a new instance of EternalValidationException class with a specified error message, parameter name, invalid value, expected value, and operation.
        /// </summary>
        /// <param name="message">The message that describes error.</param>
        /// <param name="parameterName">The name of the parameter that failed validation.</param>
        /// <param name="invalidValue">The value that failed validation.</param>
        /// <param name="expectedValue">The expected value or range for the parameter.</param>
        /// <param name="operation">The operation that was being performed.</param>
        public EternalValidationException(string message, string parameterName, object invalidValue, string expectedValue, string operation) : base(message, operation)
        {
            ParameterName = parameterName;
            InvalidValue = invalidValue;
            ExpectedValue = expectedValue;
            ValidationErrors = new List<string>();
        }
        
        /// <summary>
        /// Initializes a new instance of EternalValidationException class with a specified error message and collection of validation errors.
        /// </summary>
        /// <param name="message">The message that describes error.</param>
        /// <param name="validationErrors">Collection of validation errors.</param>
        public EternalValidationException(string message, List<string> validationErrors) : base(message)
        {
            ValidationErrors = validationErrors ?? new List<string>();
        }
        
        /// <summary>
        /// Initializes a new instance of EternalValidationException class with a specified error message, collection of validation errors, and operation.
        /// </summary>
        /// <param name="message">The message that describes error.</param>
        /// <param name="validationErrors">Collection of validation errors.</param>
        /// <param name="operation">The operation that was being performed.</param>
        public EternalValidationException(string message, List<string> validationErrors, string operation) : base(message, operation)
        {
            ValidationErrors = validationErrors ?? new List<string>();
        }
        
        /// <summary>
        /// Initializes a new instance of EternalValidationException class with a specified error message and inner exception.
        /// </summary>
        /// <param name="message">The message that describes error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public EternalValidationException(string message, Exception innerException) : base(message, innerException)
        {
            ValidationErrors = new List<string>();
        }
        
        /// <summary>
        /// Initializes a new instance of EternalValidationException class with a specified error message, inner exception, and operation.
        /// </summary>
        /// <param name="message">The message that describes error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        /// <param name="operation">The operation that was being performed.</param>
        public EternalValidationException(string message, Exception innerException, string operation) : base(message, operation, innerException)
        {
            ValidationErrors = new List<string>();
        }
        
        /// <summary>
        /// Gets a formatted message that includes validation-specific context information.
        /// </summary>
        /// <returns>Formatted message with validation context information.</returns>
        public override string GetFormattedMessage()
        {
            string baseMessage = base.GetFormattedMessage();
            string validationContext = "";
            
            if (!string.IsNullOrEmpty(ParameterName))
            {
                validationContext += $"Parameter: {ParameterName}";
                
                if (InvalidValue != null)
                {
                    validationContext += $", Invalid Value: {InvalidValue}";
                }
                
                if (!string.IsNullOrEmpty(ExpectedValue))
                {
                    validationContext += $", Expected: {ExpectedValue}";
                }
            }
            
            if (ValidationErrors != null && ValidationErrors.Count > 0)
            {
                if (!string.IsNullOrEmpty(validationContext))
                    validationContext += ", ";
                validationContext += $"Errors: [{string.Join(", ", ValidationErrors)}]";
            }
            
            if (!string.IsNullOrEmpty(validationContext))
            {
                return $"{baseMessage} (Validation Context: {validationContext})";
            }
            
            return baseMessage;
        }
    }
}