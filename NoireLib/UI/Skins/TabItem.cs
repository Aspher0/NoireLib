namespace NoireLib.UI;

/// <summary>One tab of <see cref="IControlSkin.Tabs"/>.</summary>
/// <param name="Label">The label.</param>
/// <param name="Icon">The icon, shown alone when the tabs shrink.</param>
/// <param name="Count">A count shown after the label, or -1.</param>
public readonly record struct TabItem(string Label, NoireIcon? Icon = null, int Count = -1);
