namespace NoireLib.Helpers;

/// <summary>
/// The outcome of reading a share code: the value when it worked, and a reason plus a message a user can be shown when
/// it did not.
/// </summary>
/// <remarks>
/// Decoding returns a result rather than throwing because the input is authored by a stranger and pasted by hand. A bad
/// paste is an ordinary thing that happens, not an exceptional one, and it deserves a message in the window rather than
/// a stack trace in the log.
/// </remarks>
/// <typeparam name="T">The decoded payload type.</typeparam>
/// <param name="Success">Whether the code was read successfully.</param>
/// <param name="Value">The decoded payload, or <see langword="default"/> when it was not read.</param>
/// <param name="Kind">The kind tag the code carries. Populated whenever it could be read, including on a
/// <see cref="ShareCodeError.WrongKind"/> failure, so the mismatch can be explained.</param>
/// <param name="Error">Why it failed, or <see cref="ShareCodeError.None"/>.</param>
/// <param name="Message">A description of the failure, phrased for a user rather than a log.</param>
public readonly record struct ShareCodeResult<T>(bool Success, T? Value, string Kind, ShareCodeError Error, string Message)
{
    /// <summary>
    /// Builds a successful result.
    /// </summary>
    /// <param name="value">The decoded payload.</param>
    /// <param name="kind">The kind tag the code carried.</param>
    /// <returns>The result.</returns>
    public static ShareCodeResult<T> Ok(T? value, string kind) => new(true, value, kind, ShareCodeError.None, string.Empty);

    /// <summary>
    /// Builds a failed result.
    /// </summary>
    /// <param name="error">Why it failed.</param>
    /// <param name="message">A description of the failure, phrased for a user.</param>
    /// <param name="kind">The kind tag, when it could be read.</param>
    /// <returns>The result.</returns>
    public static ShareCodeResult<T> Fail(ShareCodeError error, string message, string kind = "") => new(false, default, kind, error, message);
}
