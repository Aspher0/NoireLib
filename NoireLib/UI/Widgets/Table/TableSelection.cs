namespace NoireLib.UI;

/// <summary>
/// Whether a <see cref="NoireTable{T}"/> lets rows be selected, and how many at once.
/// </summary>
public enum TableSelection
{
    /// <summary>Rows cannot be selected. The default.</summary>
    None,

    /// <summary>One row at a time; picking another releases the first.</summary>
    Single,

    /// <summary>Any number of rows.</summary>
    Multiple,
}
