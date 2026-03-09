// file path: Eternal/Source/Eternal/Exceptions/EternalException.cs
// Author Name: 0Shard
// Date Created: 29-10-2025
// Date Last Modified: 29-10-2025
// Description: Base exception class for all Eternal mod specific exceptions.

using System;

namespace Eternal.Exceptions
{
    /// <summary>
    /// Base exception class for all Eternal mod specific exceptions.
    /// Provides common functionality for error logging and context information.
    /// </summary>
    public class EternalException : Exception
    {
        /// <summary>
        /// The pawn associated with this exception, if applicable.
        /// </summary>
        public Verse.Pawn Pawn { get; set; }
        
        /// <summary>
        /// The map associated with this exception, if applicable.
        /// </summary>
        public Verse.Map Map { get; }
        
        /// <summary>
        /// The operation that was being performed when the exception occurred.
        /// </summary>
        public string Operation { get; }
        
        /// <summary>
        /// Initializes a new instance of EternalException class.
        /// </summary>
        public EternalException() : base()
        {
        }
        
        /// <summary>
        /// Initializes a new instance of EternalException class with a specified error message.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public EternalException(string message) : base(message)
        {
        }
        
        /// <summary>
        /// Initializes a new instance of EternalException class with a specified error message and operation.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="operation">The operation that was being performed.</param>
        public EternalException(string message, string operation) : base(message)
        {
            Operation = operation;
        }
        
        /// <summary>
        /// Initializes a new instance of EternalException class with a specified error message, operation, and pawn.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="operation">The operation that was being performed.</param>
        /// <param name="pawn">The pawn associated with this exception.</param>
        public EternalException(string message, string operation, Verse.Pawn pawn) : base(message)
        {
            Operation = operation;
            Pawn = pawn;
        }
        
        /// <summary>
        /// Initializes a new instance of EternalException class with a specified error message, operation, pawn, and map.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="operation">The operation that was being performed.</param>
        /// <param name="pawn">The pawn associated with this exception.</param>
        /// <param name="map">The map associated with this exception.</param>
        public EternalException(string message, string operation, Verse.Pawn pawn, Verse.Map map) : base(message)
        {
            Operation = operation;
            Pawn = pawn;
            Map = map;
        }
        
        /// <summary>
        /// Initializes a new instance of EternalException class with a specified error message and inner exception.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public EternalException(string message, Exception innerException) : base(message, innerException)
        {
        }
        
        /// <summary>
        /// Initializes a new instance of EternalException class with a specified error message, operation, and inner exception.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="operation">The operation that was being performed.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public EternalException(string message, string operation, Exception innerException) : base(message, innerException)
        {
            Operation = operation;
        }
        
        /// <summary>
        /// Gets a formatted message that includes context information.
        /// </summary>
        /// <returns>Formatted message with context information.</returns>
        public virtual string GetFormattedMessage()
        {
            string contextInfo = "";
            
            if (!string.IsNullOrEmpty(Operation))
            {
                contextInfo += $"Operation: {Operation}";
            }
            
            if (Pawn != null)
            {
                if (!string.IsNullOrEmpty(contextInfo))
                    contextInfo += ", ";
                contextInfo += $"Pawn: {Pawn.Name?.ToStringFull ?? "Unknown"}";
            }
            
            if (Map != null)
            {
                if (!string.IsNullOrEmpty(contextInfo))
                    contextInfo += ", ";
                contextInfo += $"Map: {Map}";
            }
            
            if (!string.IsNullOrEmpty(contextInfo))
            {
                return $"{Message} (Context: {contextInfo})";
            }
            
            return Message;
        }
    }
}