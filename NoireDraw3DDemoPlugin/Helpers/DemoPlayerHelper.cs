using NoireLib;
using System;
using System.Numerics;

namespace NoireDraw3DDemoPlugin.Helpers;

internal static class DemoPlayerHelper
{
    public static Vector3 Position() => NoireService.ObjectTable.LocalPlayer?.Position ?? Vector3.Zero;

    public static Vector3 Forward()
    {
        var rotation = NoireService.ObjectTable.LocalPlayer?.Rotation ?? 0f;
        return new Vector3(MathF.Sin(rotation), 0f, MathF.Cos(rotation));
    }
}
