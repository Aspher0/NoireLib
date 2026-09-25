using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>Where a hotbar slot sits on screen.</summary>
/// <param name="HotbarId">The hotbar, from 0 (hotbar 1) to 9 (hotbar 10).</param>
/// <param name="SlotIndex">The slot, from 0 (leftmost) to 11.</param>
/// <param name="Min">The slot's top left corner, in framebuffer pixels.</param>
/// <param name="Max">The slot's bottom right corner, in framebuffer pixels.</param>
public readonly record struct HotbarSlotBounds(int HotbarId, int SlotIndex, Vector2 Min, Vector2 Max);
