using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;

namespace NoireLib.UI;

/// <summary>
/// A set of <see cref="NoireFont"/> faces of one typeface, one per weight, picked by weight the way CSS matches
/// <c>font-weight</c>.
/// </summary>
public sealed class NoireFontFamily : IDisposable
{
    /// <summary>The weight from which a browser synthesises a bold.</summary>
    public const int BoldThreshold = 600;

    private readonly SortedDictionary<int, NoireFont> faces = new();
    private readonly Dictionary<int, int> matched = new();

    /// <summary>
    /// Creates an empty family.
    /// </summary>
    /// <param name="name">The typeface name, for logs.</param>
    public NoireFontFamily(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>The typeface name.</summary>
    public string Name { get; }

    /// <summary>The weights added, ascending.</summary>
    public IReadOnlyCollection<int> Weights => faces.Keys;

    /// <summary>
    /// Adds a face at a weight, replacing any face already there.
    /// </summary>
    /// <param name="weight">The CSS weight, 1 to 1000.</param>
    /// <param name="face">The face.</param>
    /// <returns>This family.</returns>
    public NoireFontFamily Add(int weight, NoireFont face)
    {
        ArgumentNullException.ThrowIfNull(face);
        ArgumentOutOfRangeException.ThrowIfLessThan(weight, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(weight, 1000);

        faces[weight] = face;
        matched.Clear();

        return this;
    }

    /// <summary>
    /// Adds a face at a weight from TrueType data. See <see cref="NoireFont.FromMemory"/>.
    /// </summary>
    /// <param name="weight">The CSS weight, 1 to 1000.</param>
    /// <param name="data">The font file's bytes.</param>
    /// <returns>This family.</returns>
    public NoireFontFamily Add(int weight, byte[] data) => Add(weight, NoireFont.FromMemory(data, $"{Name} {weight}"));

    /// <summary>
    /// Adds a face at a weight from a manifest resource. See <see cref="NoireFont.FromManifestResource"/>.
    /// </summary>
    /// <param name="weight">The CSS weight, 1 to 1000.</param>
    /// <param name="assembly">The assembly holding the resource.</param>
    /// <param name="resourceName">The resource's manifest name.</param>
    /// <returns>This family.</returns>
    public NoireFontFamily Add(int weight, Assembly assembly, string resourceName)
        => Add(weight, NoireFont.FromManifestResource(assembly, resourceName));

    /// <summary>
    /// The face that draws a weight, chosen by the CSS font matching rules when that weight was not added.
    /// </summary>
    /// <param name="weight">The CSS weight.</param>
    /// <returns>The face.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the family has no faces.</exception>
    public NoireFont this[int weight] => faces[Matched(weight)];

    /// <summary>The face that draws a weight, and the smear that stands in for a missing bold.</summary>
    /// <param name="weight">The CSS weight.</param>
    /// <param name="sizePx">The em size at 100%.</param>
    /// <returns>The face and its smear.</returns>
    public NoireFontPick Pick(int weight, float sizePx)
    {
        var found = Matched(weight);
        var smear = weight >= BoldThreshold && found < BoldThreshold ? NoireFont.SyntheticBoldPixels(sizePx) : 0f;

        return new NoireFontPick(faces[found], found, smear);
    }

    /// <summary>
    /// Draws text at a weight, synthesising a bold when the family has no face heavy enough. See
    /// <see cref="NoireFont.Draw(ImDrawListPtr, Vector2, uint, string, float, float, float, float)"/>.
    /// </summary>
    /// <param name="drawList">The draw list to draw into.</param>
    /// <param name="position">The top left of the line, in screen pixels.</param>
    /// <param name="color">The packed ABGR color.</param>
    /// <param name="text">The text.</param>
    /// <param name="weight">The CSS weight.</param>
    /// <param name="sizePx">The em size at 100%.</param>
    /// <param name="trackingPx">Extra space between characters at 100%.</param>
    /// <param name="maxWidth">The width past which the text is cut with an ellipsis, in real pixels, or 0 for none.</param>
    /// <returns>The drawn size in real pixels.</returns>
    public Vector2 Draw(ImDrawListPtr drawList, Vector2 position, uint color, string text, int weight, float sizePx, float trackingPx = 0f, float maxWidth = 0f)
    {
        var pick = Pick(weight, sizePx);
        return pick.Face.Draw(drawList, position, color, text, sizePx, trackingPx, maxWidth, pick.SyntheticBoldPx);
    }

    private int Matched(int weight)
    {
        if (matched.TryGetValue(weight, out var found))
            return found;

        if (faces.Count == 0)
            throw new InvalidOperationException($"The {Name} family has no faces.");

        found = MatchWeight(faces.Keys, weight);
        matched[weight] = found;

        return found;
    }

    /// <summary>
    /// Asks every face for a size. See <see cref="NoireFont.Request(float)"/>.
    /// </summary>
    /// <param name="sizePx">The em size at 100%.</param>
    public void Request(float sizePx)
    {
        foreach (var face in faces.Values)
            face.Request(sizePx);
    }

    // CSS Fonts 4 font-weight matching.
    internal static int MatchWeight(IEnumerable<int> available, int desired)
    {
        var present = new List<int>(available);

        if (present.Contains(desired))
            return desired;

        if (desired is >= 400 and <= 500)
        {
            var between = Best(present, w => w > desired && w <= 500, ascending: true);
            if (between.HasValue)
                return between.Value;

            return Best(present, w => w < desired, ascending: false) ?? Best(present, w => w > 500, ascending: true)!.Value;
        }

        return desired < 400
            ? Best(present, w => w < desired, ascending: false) ?? Best(present, w => w > desired, ascending: true)!.Value
            : Best(present, w => w > desired, ascending: true) ?? Best(present, w => w < desired, ascending: false)!.Value;
    }

    private static int? Best(List<int> weights, Func<int, bool> accept, bool ascending)
    {
        int? best = null;

        foreach (var weight in weights)
        {
            if (!accept(weight))
                continue;

            if (best == null || (ascending ? weight < best : weight > best))
                best = weight;
        }

        return best;
    }

    /// <summary>
    /// Disposes every face.
    /// </summary>
    public void Dispose()
    {
        foreach (var face in faces.Values)
            face.Dispose();

        faces.Clear();
        matched.Clear();
    }
}
