using System.Numerics;

namespace NoireLib.UI;

/// <summary>A point at which a gauge changes colour.</summary>
/// <remarks>A threshold applies at or below its value, and the lowest matching one wins.</remarks>
/// <param name="Value">The fraction at or below which this colour applies, from 0 to 1.</param>
/// <param name="Color">The colour to paint with.</param>
public readonly record struct GaugeThreshold(float Value, Vector4 Color);
