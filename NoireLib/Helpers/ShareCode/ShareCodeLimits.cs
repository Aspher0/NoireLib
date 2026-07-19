namespace NoireLib.Helpers;

/// <summary>
/// The ceilings a share code is decoded under. They are part of the format, not a tuning knob: a code that needs more
/// than these to read is not a valid share code, and every conformant reader refuses it.
/// </summary>
/// <remarks>
/// A share code arrives from a stranger, pasted out of a chat window. Decompressing one with no ceiling is a zip bomb:
/// a few kilobytes of paste expands to gigabytes and the game dies with no useful error. Deep nesting is a separate
/// problem with the same cause, and a worse ending, because it overflows the stack inside the parser where no
/// <c>try/catch</c> can save you. Both are enforced while decoding rather than checked afterwards.
/// </remarks>
public sealed class ShareCodeLimits
{
    /// <summary>
    /// The longest code accepted, in characters. Checked before any decoding work happens.
    /// </summary>
    public int MaxEncodedCharacters { get; set; } = 64 * 1024;

    /// <summary>
    /// The largest payload accepted after decompression, in bytes. Enforced during decompression, so an oversized
    /// payload is abandoned partway rather than expanded first and measured second.
    /// </summary>
    public int MaxDecodedBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// The deepest nesting accepted in the payload. Enforced by the JSON reader as it reads, before the recursion that
    /// would overflow the stack.
    /// </summary>
    public int MaxDepth { get; set; } = 32;

    /// <summary>
    /// The longest kind tag accepted, in UTF-8 bytes. The tag is written as a single length byte, so this cannot exceed 255.
    /// </summary>
    public int MaxKindBytes { get; set; } = 64;

    /// <summary>
    /// Creates an independent copy, so a stricter set can be derived without touching the shared one.
    /// </summary>
    /// <returns>The copy.</returns>
    public ShareCodeLimits Clone() => new()
    {
        MaxEncodedCharacters = MaxEncodedCharacters,
        MaxDecodedBytes = MaxDecodedBytes,
        MaxDepth = MaxDepth,
        MaxKindBytes = MaxKindBytes,
    };
}
