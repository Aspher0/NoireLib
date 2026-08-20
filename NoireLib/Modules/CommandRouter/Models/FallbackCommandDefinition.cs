namespace NoireLib.CommandRouter;

/// <summary>How a root command's fallback is presented in help listings.</summary>
public sealed class FallbackCommandDefinition
{
    /// <summary>The display name of the free-form argument, rendered as &lt;name&gt; in help listings.</summary>
    public string Name { get; }

    /// <summary>Help text describing what the fallback does with its argument.</summary>
    public string? HelpText { get; }

    /// <summary>
    /// The display order among the subcommand lines in help listings, where a tie lists the fallback first and
    /// the default <see cref="int.MaxValue"/> lists it after every subcommand.
    /// </summary>
    public int DisplayOrder { get; }

    /// <summary>Whether the fallback appears in help listings, which does not affect dispatch.</summary>
    public bool ShowInHelp { get; }

    /// <summary>Creates a fallback presentation definition.</summary>
    /// <param name="name">The display name of the free-form argument.</param>
    /// <param name="helpText">Help text describing what the fallback does, or null.</param>
    /// <param name="displayOrder">The display order among the subcommand lines.</param>
    /// <param name="showInHelp">Whether the fallback appears in help listings.</param>
    internal FallbackCommandDefinition(string name, string? helpText, int displayOrder, bool showInHelp)
    {
        Name = name;
        HelpText = helpText;
        DisplayOrder = displayOrder;
        ShowInHelp = showInHelp;
    }
}
