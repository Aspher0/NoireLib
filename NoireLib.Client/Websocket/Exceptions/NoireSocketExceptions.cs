using System;
using System.Collections.Generic;
using System.Net;

namespace NoireLib.Websocket;

/// <summary>
/// The root of every failure this system raises.
/// </summary>
public class NoireSocketException : Exception
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    /// <param name="message">What failed.</param>
    /// <param name="inner">The underlying failure, when there is one.</param>
    public NoireSocketException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// A connection could not be opened.
/// </summary>
public sealed class NoireSocketConnectException : NoireSocketException
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    /// <param name="message">What failed.</param>
    /// <param name="status">The status the server answered the handshake with, when it answered one.</param>
    /// <param name="headers">The response headers, when they were read.</param>
    /// <param name="inner">The underlying failure, when there is one.</param>
    public NoireSocketConnectException(string message, HttpStatusCode? status = null,
        IReadOnlyDictionary<string, string>? headers = null, Exception? inner = null) : base(message, inner)
    {
        Status = status;
        Headers = headers;
    }

    /// <summary>
    /// Gets the status the handshake was refused with, or null when nothing answered.
    /// </summary>
    public HttpStatusCode? Status { get; }

    /// <summary>
    /// Gets the response headers, or null when none were read.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Headers { get; }
}

/// <summary>
/// A send was attempted on a connection that is not open and will not queue.
/// </summary>
public sealed class NoireSocketClosedException : NoireSocketException
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    /// <param name="message">What failed.</param>
    public NoireSocketClosedException(string message) : base(message) { }
}

/// <summary>
/// The send queue was full under <see cref="NoireSocketSendOverflow.Fail"/>.
/// </summary>
public sealed class NoireSocketQueueFullException : NoireSocketException
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    /// <param name="capacity">The queue capacity that was reached.</param>
    public NoireSocketQueueFullException(int capacity)
        : base("The send queue holds as many messages as it accepts (" + capacity + ").")
        => Capacity = capacity;

    /// <summary>
    /// Gets the capacity that was reached.
    /// </summary>
    public int Capacity { get; }
}

/// <summary>
/// A message passed the size cap in force.
/// </summary>
public sealed class NoireSocketMessageTooLargeException : NoireSocketException
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    /// <param name="size">The size reached before the read stopped.</param>
    /// <param name="limit">The cap in force.</param>
    public NoireSocketMessageTooLargeException(long size, long limit)
        : base("A message of " + size + " bytes passed the " + limit + " byte cap.")
    {
        Size = size;
        Limit = limit;
    }

    /// <summary>
    /// Gets the size reached before the read stopped.
    /// </summary>
    public long Size { get; }

    /// <summary>
    /// Gets the cap in force.
    /// </summary>
    public long Limit { get; }
}

/// <summary>
/// An acknowledgement or a poll did not answer in time.
/// </summary>
public sealed class NoireSocketTimeoutException : NoireSocketException
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    /// <param name="message">What timed out.</param>
    /// <param name="timeout">The limit that was passed.</param>
    public NoireSocketTimeoutException(string message, TimeSpan timeout) : base(message)
        => Timeout = timeout;

    /// <summary>
    /// Gets the limit that was passed.
    /// </summary>
    public TimeSpan Timeout { get; }
}

/// <summary>
/// The peer sent something the transport cannot read.
/// </summary>
public sealed class NoireSocketProtocolException : NoireSocketException
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    /// <param name="message">What was wrong with it.</param>
    /// <param name="closeCode">The close code the connection is ending with.</param>
    public NoireSocketProtocolException(string message, NoireWebsocketCloseCode closeCode = NoireWebsocketCloseCode.ProtocolError)
        : base(message)
        => CloseCode = closeCode;

    /// <summary>
    /// Gets the close code the connection is ending with.
    /// </summary>
    public NoireWebsocketCloseCode CloseCode { get; }
}
