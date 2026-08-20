using System.Runtime.InteropServices;

namespace NoireLib.Animations.Timelines;

/// <summary>
/// The parameter block the game's timeline sequencer takes when it is asked to play an animation.
/// FFXIVClientStructs does not declare this type, so the layout is reverse-engineered and most fields are still
/// named by offset. The sequencer's parameter is untyped, so the size and the offsets are load-bearing and nothing
/// catches a mistake here at compile time.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 0x60)]
public struct ActionTimelineAnimParams
{
    /// <summary> Virtual table pointer, filled in by the game; callers leave it zero. </summary>
    [FieldOffset(0x00)] public nint VirtualTable;

    /// <summary> Blend-duration field, honoured only when <see cref="OverridesBlendDuration"/> is set. </summary>
    [FieldOffset(0x10)] public float Unk0;

    /// <inheritdoc cref="Unk0"/>
    [FieldOffset(0x14)] public float Unk4;

    /// <inheritdoc cref="Unk0"/>
    [FieldOffset(0x18)] public float Unk8;

    /// <inheritdoc cref="Unk0"/>
    [FieldOffset(0x1C)] public float UnkC;

    /// <inheritdoc cref="Unk0"/>
    [FieldOffset(0x20)] public float Unk10;

    /// <summary> How strongly the animation applies, 1.0 being full strength. </summary>
    [FieldOffset(0x24)] public float Intensity;

    /// <summary> Where in the animation playback starts, in seconds. </summary>
    [FieldOffset(0x28)] public float StartTimestamp;

    /// <summary> Unknown float; the game passes -1 for "use the animation's own value". </summary>
    [FieldOffset(0x2C)] public float Unk1C;

    /// <summary> Unknown. </summary>
    [FieldOffset(0x30)] public ulong Unk20;

    /// <summary> The object a targeted animation is aimed at. </summary>
    [FieldOffset(0x38)] public ulong TargetObjectId;

    /// <summary> Unknown. </summary>
    [FieldOffset(0x40)] public uint Unk30;

    /// <summary> The animation channel, 0 to 7, or -1 for the animation's own default. </summary>
    [FieldOffset(0x44)] public uint Priority;

    /// <summary> Unknown; the game passes -1. </summary>
    [FieldOffset(0x48)] public int Unk38;

    /// <summary> Unknown flag byte; 0xFF for almost every timeline, 0 for a small number of them. </summary>
    [FieldOffset(0x4C)] public byte Unk3C;

    /// <summary> Set to make the game honour the blend-duration floats at 0x10 through 0x20 rather than the animation's own. </summary>
    [FieldOffset(0x4D)] public byte OverridesBlendDuration;

    /// <summary> Unknown. </summary>
    [FieldOffset(0x4E)] public byte Unk3E;

    /// <summary> Unknown. </summary>
    [FieldOffset(0x4F)] public byte Unk3F;

    /// <summary> Unknown. </summary>
    [FieldOffset(0x50)] public byte Unk40;

    /// <summary> Unknown. </summary>
    [FieldOffset(0x52)] public byte Unk42;
}
