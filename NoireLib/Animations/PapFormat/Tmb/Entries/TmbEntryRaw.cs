using NoireLib.Animations.PapFormat.Parsing;
using NoireLib.Animations.PapFormat.Tmb;
using System.Collections.Generic;

namespace NoireLib.Animations.PapFormat.Tmb.Entries;

// Internal wire-format plumbing behind PapFile/TmbFile; not part of the public PapFormat surface.
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

/// <summary>
/// A TMB entry of an unrecognised type, kept as raw bytes so a read/write round trip preserves it.
/// </summary>
public class TmbEntryRaw : TmbEntry
{
    private readonly string _magic;
    private readonly byte[] _data;

    public override string Magic => _magic;
    public override string DisplayName => $"Unknown Entry ({_magic})";
    public override int Size { get; }
    public override int ExtraSize => 0;

    public TmbEntryRaw(TmbFile file, TmbReader reader, string magic, int size) : base(file, reader)
    {
        _magic = magic;
        Size = size;

        // Base constructors read: magic (4) + size (4) + id (2) + time (2) = 12 bytes
        var remainingSize = size - 12;
        _data = reader.Reader.ReadBytes(remainingSize);
    }

    protected override List<ParsedBase> GetParsed() => [];

    public override void Write(TmbWriter writer)
    {
        base.Write(writer);
        writer.Writer.Write(_data);
    }
}

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
