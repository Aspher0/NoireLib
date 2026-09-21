namespace NoireLib.Remote;

/// <summary>
/// The closed set of values the <c>error.code</c> field of a failed envelope takes.
/// </summary>
public static class NoireRemoteErrorCodes
{
    /// <summary>A malformed request line, malformed JSON, a chunked body or an upgrade request.</summary>
    public const string BadRequest = "BadRequest";

    /// <summary>A parameter with no default was not supplied.</summary>
    public const string ArgumentMissing = "ArgumentMissing";

    /// <summary>The body carries a key that matches no parameter.</summary>
    public const string ArgumentUnknown = "ArgumentUnknown";

    /// <summary>A value did not deserialize into the parameter type.</summary>
    public const string ArgumentInvalid = "ArgumentInvalid";

    /// <summary>The caller asked for a protocol version this listener does not serve.</summary>
    public const string ProtocolMismatch = "ProtocolMismatch";

    /// <summary>The credential is missing, malformed or wrong.</summary>
    public const string Unauthorized = "Unauthorized";

    /// <summary>The request looks like a browser sent it, or a local member was reached from another machine.</summary>
    public const string Forbidden = "Forbidden";

    /// <summary>No endpoint of that name is published.</summary>
    public const string UnknownEndpoint = "UnknownEndpoint";

    /// <summary>The endpoint publishes no member of that name.</summary>
    public const string UnknownMember = "UnknownMember";

    /// <summary>The member exists but is not served on the wire the call arrived on.</summary>
    public const string TransportNotServed = "TransportNotServed";

    /// <summary>A socket frame was not one JSON object.</summary>
    public const string FrameMalformed = "FrameMalformed";

    /// <summary>A socket frame named a kind this listener does not serve.</summary>
    public const string UnknownFrameKind = "UnknownFrameKind";

    /// <summary>The job id is unknown or its retention has passed.</summary>
    public const string JobNotFound = "JobNotFound";

    /// <summary>The route does not take that verb.</summary>
    public const string BadMethod = "BadMethod";

    /// <summary>The member passed its deadline.</summary>
    public const string Timeout = "Timeout";

    /// <summary>The pinned instance id names another instance.</summary>
    public const string InstanceMismatch = "InstanceMismatch";

    /// <summary>The body or the header block passed its cap.</summary>
    public const string TooLarge = "TooLarge";

    /// <summary>The content type is not JSON.</summary>
    public const string BadContentType = "BadContentType";

    /// <summary>The member threw.</summary>
    public const string HandlerFault = "HandlerFault";

    /// <summary>The readiness gate is not met.</summary>
    public const string NotReady = "NotReady";

    /// <summary>The call queue is full.</summary>
    public const string Busy = "Busy";

    /// <summary>The plugin is unloading.</summary>
    public const string ShuttingDown = "ShuttingDown";

    /// <summary>A poll's filter names an operator or a path this listener does not read.</summary>
    public const string FilterInvalid = "FilterInvalid";

    /// <summary>The batch carries more calls than the listener accepts, or none at all.</summary>
    public const string BatchInvalid = "BatchInvalid";

    /// <summary>The idempotency key was already used for another route or another body.</summary>
    public const string IdempotencyConflict = "IdempotencyConflict";

    /// <summary>The file transfer is unknown, expired or already consumed.</summary>
    public const string FileNotFound = "FileNotFound";

    /// <summary>The bytes received do not hash to what the transfer declared.</summary>
    public const string ChecksumMismatch = "ChecksumMismatch";

    /// <summary>The transfer is missing a range of the file it declared.</summary>
    public const string FileIncomplete = "FileIncomplete";

    /// <summary>The route is one this listener does not serve, because the feature behind it is off.</summary>
    public const string FeatureDisabled = "FeatureDisabled";

    /// <summary>A fleet call named a member whose contract that listener no longer has: it changed since it was read.</summary>
    public const string ContractChanged = "ContractChanged";
}
