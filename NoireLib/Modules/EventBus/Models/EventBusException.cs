using System;

namespace NoireLib.EventBus;

/// <summary>
/// Exception thrown by the EventBus when a handler throws an exception and the mode is LogAndThrow.
/// </summary>
public class EventBusException : Exception
{
    public EventBusException(string message) : base(message) { }

    public EventBusException(string message, Exception innerException) : base(message, innerException) { }
}
