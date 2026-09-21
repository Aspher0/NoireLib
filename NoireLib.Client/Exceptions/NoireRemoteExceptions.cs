using System;
using System.Collections.Generic;

namespace NoireLib.Remote;

/// <summary>
/// The base of every failure a NoireRemote caller raises.
/// </summary>
public class NoireRemoteException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NoireRemoteException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    public NoireRemoteException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NoireRemoteException"/> class with an inner exception.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The failure underneath.</param>
    public NoireRemoteException(string message, Exception? innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets or sets the error code the listener sent, when the failure came from one.
    /// </summary>
    public string? Code { get; set; }
}

/// <summary>
/// No live instance publishes the endpoint asked for.
/// </summary>
public sealed class NoireRemoteNotFoundException : NoireRemoteException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NoireRemoteNotFoundException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The failure underneath.</param>
    public NoireRemoteNotFoundException(string message, Exception? innerException = null) : base(message, innerException)
    {
    }
}

/// <summary>
/// Several live instances publish the endpoint asked for and none was named.
/// </summary>
public sealed class NoireRemoteAmbiguousEndpointException : NoireRemoteException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NoireRemoteAmbiguousEndpointException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="candidates">The instances that matched.</param>
    public NoireRemoteAmbiguousEndpointException(string message, IReadOnlyList<NoireRemoteInstance> candidates) : base(message)
    {
        Candidates = candidates;
    }

    /// <summary>
    /// Gets the instances that matched. A caller picks one.
    /// </summary>
    public IReadOnlyList<NoireRemoteInstance> Candidates { get; }
}

/// <summary>
/// The credential was missing, malformed or wrong.
/// </summary>
public sealed class NoireRemoteUnauthorizedException : NoireRemoteException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NoireRemoteUnauthorizedException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    public NoireRemoteUnauthorizedException(string message) : base(message)
    {
        Code = NoireRemoteErrorCodes.Unauthorized;
    }
}

/// <summary>
/// The listener refused the request before it reached a member.
/// </summary>
public sealed class NoireRemoteForbiddenException : NoireRemoteException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NoireRemoteForbiddenException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    public NoireRemoteForbiddenException(string message) : base(message)
    {
        Code = NoireRemoteErrorCodes.Forbidden;
    }
}

/// <summary>
/// The arguments did not bind to the member's parameters.
/// </summary>
public sealed class NoireRemoteArgumentException : NoireRemoteException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NoireRemoteArgumentException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="parameterName">The parameter the listener named.</param>
    /// <param name="code">The error code the listener sent.</param>
    public NoireRemoteArgumentException(string message, string? parameterName, string code) : base(message)
    {
        ParameterName = parameterName;
        Code = code;
    }

    /// <summary>
    /// Gets the parameter the listener named.
    /// </summary>
    public string? ParameterName { get; }
}

/// <summary>
/// The call passed its deadline, or no answer arrived.
/// </summary>
public sealed class NoireRemoteTimeoutException : NoireRemoteException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NoireRemoteTimeoutException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The failure underneath.</param>
    public NoireRemoteTimeoutException(string message, Exception? innerException = null) : base(message, innerException)
    {
        Code = NoireRemoteErrorCodes.Timeout;
    }
}

/// <summary>
/// The member's readiness gate is not met. The call never ran.
/// </summary>
public sealed class NoireRemoteNotReadyException : NoireRemoteException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NoireRemoteNotReadyException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="retryAfter">How long the listener asked the caller to wait.</param>
    public NoireRemoteNotReadyException(string message, TimeSpan retryAfter) : base(message)
    {
        RetryAfter = retryAfter;
        Code = NoireRemoteErrorCodes.NotReady;
    }

    /// <summary>
    /// Gets how long the listener asked the caller to wait.
    /// </summary>
    public TimeSpan RetryAfter { get; }
}

/// <summary>
/// The listener's call queue is full.
/// </summary>
public sealed class NoireRemoteBusyException : NoireRemoteException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NoireRemoteBusyException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="retryAfter">How long the listener asked the caller to wait.</param>
    public NoireRemoteBusyException(string message, TimeSpan retryAfter) : base(message)
    {
        RetryAfter = retryAfter;
        Code = NoireRemoteErrorCodes.Busy;
    }

    /// <summary>
    /// Gets how long the listener asked the caller to wait.
    /// </summary>
    public TimeSpan RetryAfter { get; }
}

/// <summary>
/// The member ran and threw.
/// </summary>
public sealed class NoireRemoteRemoteException : NoireRemoteException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NoireRemoteRemoteException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="remoteType">The exception type name the member threw, when the listener sends it.</param>
    /// <param name="remoteMessage">The exception message the member threw.</param>
    /// <param name="remoteStackTrace">The stack trace, when the listener was told to send traces.</param>
    public NoireRemoteRemoteException(string message, string? remoteType, string? remoteMessage, string? remoteStackTrace) : base(message)
    {
        RemoteType = remoteType;
        RemoteMessage = remoteMessage;
        RemoteStackTrace = remoteStackTrace;
        Code = NoireRemoteErrorCodes.HandlerFault;
    }

    /// <summary>
    /// Gets the exception type name the member threw.
    /// </summary>
    public string? RemoteType { get; }

    /// <summary>
    /// Gets the exception message the member threw.
    /// </summary>
    public string? RemoteMessage { get; }

    /// <summary>
    /// Gets the stack trace, or null when the listener did not send one.
    /// </summary>
    public string? RemoteStackTrace { get; }
}

/// <summary>
/// The interface a typed client was built from disagrees with the surface the listener publishes.
/// </summary>
public sealed class NoireRemoteContractException : NoireRemoteException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NoireRemoteContractException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    public NoireRemoteContractException(string message) : base(message)
    {
    }
}

/// <summary>
/// The listener answered something this protocol version cannot read.
/// </summary>
public sealed class NoireRemoteProtocolException : NoireRemoteException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NoireRemoteProtocolException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The failure underneath.</param>
    public NoireRemoteProtocolException(string message, Exception? innerException = null) : base(message, innerException)
    {
        Code = NoireRemoteErrorCodes.ProtocolMismatch;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NoireRemoteProtocolException"/> class with an explicit code.
    /// </summary>
    /// <param name="code">The wire's code for what was wrong, from <see cref="NoireRemoteErrorCodes"/>.</param>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The failure underneath.</param>
    public NoireRemoteProtocolException(string code, string message, Exception? innerException = null) : base(message, innerException)
    {
        Code = code;
    }
}
