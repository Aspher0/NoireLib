namespace NoireLib.UI;

/// <summary>The texts an <see cref="IChromeSkin"/> shows in its title bar.</summary>
/// <param name="Title">The title.</param>
/// <param name="Subtitle">A second line, or <see langword="null"/>.</param>
public readonly record struct ChromeTitle(string Title, string? Subtitle);
