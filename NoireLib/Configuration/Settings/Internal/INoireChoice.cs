namespace NoireLib.Configuration;

// An enum setting seen without its type: each value, and its label in the active language.
internal interface INoireChoice
{
    string[] Labels { get; }

    int Index { get; }

    object? ValueAt(int index);
}
