namespace NoireLib.UI;

/// <summary>The control a settings row holds, for the skin to size the row before drawing it.</summary>
public enum SettingControl
{
    /// <summary>A switch or a checkbox.</summary>
    Toggle,

    /// <summary>A segmented choice or a dropdown.</summary>
    Choice,

    /// <summary>A stepper.</summary>
    Number,

    /// <summary>A duration field.</summary>
    Duration,

    /// <summary>Whatever the plugin draws.</summary>
    Custom,
}
