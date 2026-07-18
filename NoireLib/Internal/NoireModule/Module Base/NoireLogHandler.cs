using System;
using System.Runtime.CompilerServices;

namespace NoireLib.Core.Modules;

/// <summary>
/// Interpolated string handler used by the gated module logging helpers
/// (<c>LogInfo</c>, <c>LogDebug</c>, <c>LogVerbose</c>) on <see cref="NoireModuleBase{TModule}"/>.<br/>
/// When the module it is built for has <see cref="INoireModule.EnableLogging"/> set to <see langword="false"/>, the
/// compiler is told through the constructor's <c>shouldAppend</c> out parameter not to run any append, so an
/// interpolated message such as <c>LogDebug($"Value: {Compute()}")</c> never allocates a string or evaluates its
/// holes while logging is off. This is why the gated helpers take this handler rather than a plain
/// <see cref="string"/>, whose argument would be built at the call site on every pass regardless of the flag.
/// </summary>
[InterpolatedStringHandler]
public ref struct NoireLogHandler
{
    private DefaultInterpolatedStringHandler inner;

    /// <summary>
    /// Creates the handler for a single gated log call.
    /// </summary>
    /// <param name="literalLength">The total length of the literal parts of the interpolated string, supplied by the compiler.</param>
    /// <param name="formattedCount">The number of interpolation holes, supplied by the compiler.</param>
    /// <param name="module">The module the log call belongs to, whose <see cref="INoireModule.EnableLogging"/> gates the build.</param>
    /// <param name="shouldAppend">Set to <see langword="true"/> only when the module is logging, so the compiler skips every append otherwise.</param>
    public NoireLogHandler(int literalLength, int formattedCount, INoireModule module, out bool shouldAppend)
    {
        IsEnabled = module.EnableLogging;
        shouldAppend = IsEnabled;
        inner = IsEnabled ? new DefaultInterpolatedStringHandler(literalLength, formattedCount) : default;
    }

    /// <summary>
    /// Whether the message is being built, mirroring the module's <see cref="INoireModule.EnableLogging"/> at
    /// construction time. When <see langword="false"/>, no append ran and <see cref="ToStringAndClear"/> must not be called.
    /// </summary>
    internal bool IsEnabled { get; }

    /// <summary>
    /// Appends a literal fragment of the interpolated string.
    /// </summary>
    /// <param name="value">The literal text.</param>
    public void AppendLiteral(string value) => inner.AppendLiteral(value);

    /// <summary>
    /// Appends an interpolation hole value.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value to format.</param>
    public void AppendFormatted<T>(T value) => inner.AppendFormatted(value);

    /// <summary>
    /// Appends an interpolation hole value with a format string.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value to format.</param>
    /// <param name="format">The format string.</param>
    public void AppendFormatted<T>(T value, string? format) => inner.AppendFormatted(value, format);

    /// <summary>
    /// Appends an interpolation hole value with an alignment.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value to format.</param>
    /// <param name="alignment">The minimum field width; negative left-aligns.</param>
    public void AppendFormatted<T>(T value, int alignment) => inner.AppendFormatted(value, alignment);

    /// <summary>
    /// Appends an interpolation hole value with an alignment and a format string.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value to format.</param>
    /// <param name="alignment">The minimum field width; negative left-aligns.</param>
    /// <param name="format">The format string.</param>
    public void AppendFormatted<T>(T value, int alignment, string? format) => inner.AppendFormatted(value, alignment, format);

    /// <summary>
    /// Appends a string interpolation hole.
    /// </summary>
    /// <param name="value">The string value.</param>
    public void AppendFormatted(string? value) => inner.AppendFormatted(value);

    /// <summary>
    /// Appends a string interpolation hole with an alignment and a format string.
    /// </summary>
    /// <param name="value">The string value.</param>
    /// <param name="alignment">The minimum field width; negative left-aligns.</param>
    /// <param name="format">The format string.</param>
    public void AppendFormatted(string? value, int alignment = 0, string? format = null) => inner.AppendFormatted(value, alignment, format);

    /// <summary>
    /// Appends a character span interpolation hole.
    /// </summary>
    /// <param name="value">The character span.</param>
    public void AppendFormatted(ReadOnlySpan<char> value) => inner.AppendFormatted(value);

    /// <summary>
    /// Appends a character span interpolation hole with an alignment and a format string.
    /// </summary>
    /// <param name="value">The character span.</param>
    /// <param name="alignment">The minimum field width; negative left-aligns.</param>
    /// <param name="format">The format string.</param>
    public void AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string? format = null) => inner.AppendFormatted(value, alignment, format);

    /// <summary>
    /// Returns the built message and releases the pooled buffer. Only valid when <see cref="IsEnabled"/> is
    /// <see langword="true"/>; the gated helpers check that first.
    /// </summary>
    /// <returns>The formatted log message.</returns>
    internal string ToStringAndClear() => inner.ToStringAndClear();
}
