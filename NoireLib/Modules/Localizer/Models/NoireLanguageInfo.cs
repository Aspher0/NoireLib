using System.Collections.Generic;

namespace NoireLib.Localizer;

/// <summary>A language the user can pick for the declared texts.</summary>
/// <param name="Code">The locale code, for example <c>de</c>.</param>
/// <param name="NativeName">The name in that language, for example <c>Deutsch</c>.</param>
/// <param name="Translated">How many declared texts it translates.</param>
/// <param name="Total">How many texts are declared.</param>
/// <param name="UserFile">Whether it comes only from a file in the plugin's configuration folder.</param>
/// <param name="IsActive">Whether it is the active language, or the closest listed parent of it.</param>
public sealed record NoireLanguageInfo(string Code, string NativeName, int Translated, int Total, bool UserFile, bool IsActive)
{
    /// <summary>The people its language file credits, from its <c>@credits = A, B</c> line; empty when it names none.</summary>
    public IReadOnlyList<string> Credits { get; init; } = [];

    /// <summary>How many of its translations were made from a source text that has changed since; they still show.</summary>
    public int Outdated { get; init; }
}
