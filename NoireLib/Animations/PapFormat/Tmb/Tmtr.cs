using NoireLib.Animations.PapFormat.Tmb.Entries;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.Animations.PapFormat.Tmb;

// Internal wire-format plumbing behind PapFile/TmbFile; not part of the public PapFormat surface.
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

public class Tmtr : TmbItemWithTime
{
    public override string Magic => "TMTR";
    public override int Size => 0x18;
    public override int ExtraSize => 0;

    public readonly List<TmbEntry> Entries = [];
    private List<int> TempIds = [];

    public Tmtr(TmbFile file) : base(file) { }

    public Tmtr(TmbFile file, TmbReader reader) : base(file, reader)
    {
        TempIds = reader.ReadOffsetTimeline();
        reader.ReadInt32(); // Lua condition, always 0 and not supported here
    }

    public void PickEntries(TmbReader reader)
    {
        Entries.AddRange(reader.Pick<TmbEntry>(TempIds));
    }

    public override void Write(TmbWriter writer)
    {
        base.Write(writer);
        writer.WriteOffsetTimeline(Entries);
        writer.Write(0); // Lua condition, always 0 and not supported here
    }

    public List<C009> GetC009Entries()
    {
        return Entries.OfType<C009>().ToList();
    }
}

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
