using System.Numerics;

namespace NoireLib.UI;

/// <summary>
/// A point at which a gauge changes colour, so a number that has become a problem looks like one before it is read.
/// </summary>
/// <remarks>
/// A threshold applies at or below its value, and the lowest matching one wins. That reads the way the things being
/// measured read: under a quarter is critical, under a half is a warning, anything above is fine. A gauge counting the
/// other way (a cast bar filling toward a cost) reaches the same result with the values inverted.
/// </remarks>
/// <param name="Value">The fraction at or below which this colour applies, from 0 to 1.</param>
/// <param name="Color">The colour to paint with.</param>
public readonly record struct GaugeThreshold(float Value, Vector4 Color);
