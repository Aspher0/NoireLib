namespace NoireLib.Websocket;

/// <summary>
/// Which verb a long poll is issued with.
/// </summary>
public enum NoireLongPollMethod
{
    /// <summary>A GET. The cursor travels as a query parameter or a header.</summary>
    Get,

    /// <summary>A POST, the only form that may carry a body.</summary>
    Post,
}
