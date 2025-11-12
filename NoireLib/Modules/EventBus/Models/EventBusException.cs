using System;

namespace NoireLib.EventBus;

/// <summary>
/// Exception thrown by the EventBus when a handler throws an exception and the mode is LogAndThrow.
/// </summary>
public class EventBusException : Exception
{
    /// <summary>
    /// Creates a new EventBusException with the specified message.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public EventBusException(string message) : base(message) { }

    /// <summary>
    /// Creates a new EventBusException with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public EventBusException(string message, Exception innerException) : base(message, innerException) { }
}
