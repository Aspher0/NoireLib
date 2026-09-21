using Lumina.Data.Parsing.Layer;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>
/// One layer of a level (<c>.lgb</c>) or shared group (<c>.sgb</c>) file: its identity, how it is gated, and every
/// entry it places.
/// </summary>
public sealed class LayerGroupLayer
{
    /// <summary>The layer id, unique within its file.</summary>
    public required int Id { get; init; }

    /// <summary>The layer name the level designers gave it.</summary>
    public required string Name { get; init; }

    /// <summary>How <see cref="LayerSetIds"/> gates the layer.</summary>
    public required LayerSetReferencedType Reference { get; init; }

    /// <summary>The layer-set ids the layer is gated on, empty when it is ungated.</summary>
    public required IReadOnlyList<uint> LayerSetIds { get; init; }

    /// <summary>The festival that must be running for the layer to stand, zero when it is not seasonal.</summary>
    public required ushort FestivalId { get; init; }

    /// <summary>The festival phase the layer belongs to, zero for every phase.</summary>
    public required ushort FestivalPhase { get; init; }

    /// <summary>Every entry the layer places, of every type, in file order.</summary>
    public required IReadOnlyList<LayerGroupEntry> Entries { get; init; }
}
