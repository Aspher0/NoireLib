using NoireLib.Animations.PapFormat;
using System.IO;
using System.Text;
using Xunit;

namespace NoireLib.Tests;

public class PapFaceFieldTests
{
    private const int PapMagic = 0x20706170;

    private static byte[] BuildPap(uint face)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(PapMagic);
        writer.Write(0x00020001);
        writer.Write((short)1);
        writer.Write((short)101);
        writer.Write((byte)0);
        writer.Write((byte)0);

        var offsetPos = stream.Position;
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        var infoPos = stream.Position;

        var name = new byte[32];
        Encoding.ASCII.GetBytes("cbem_test").CopyTo(name, 0);
        writer.Write(name);
        writer.Write((short)0);
        writer.Write((short)0);
        writer.Write(face);

        var havokPos = stream.Position;
        writer.Write(new byte[] { 1, 2, 3, 4, 5, 6 });

        var footerPos = stream.Position;
        writer.Write(BuildMinimalTmb());

        var endPos = stream.Position;
        stream.Position = offsetPos;
        writer.Write((int)infoPos);
        writer.Write((int)havokPos);
        writer.Write((int)footerPos);
        stream.Position = endPos;

        return stream.ToArray();
    }

    private static byte[] BuildMinimalTmb()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(Encoding.ASCII.GetBytes("TMLB"));
        var sizePos = stream.Position;
        writer.Write(0);
        writer.Write(3);

        writer.Write(Encoding.ASCII.GetBytes("TMDH"));
        writer.Write(0x10);
        writer.Write((short)1);
        writer.Write((short)0);
        writer.Write((short)0);
        writer.Write((short)0);

        var tmalStart = stream.Position;
        writer.Write(Encoding.ASCII.GetBytes("TMAL"));
        writer.Write(0x10);
        var tmalOffsetPos = stream.Position;
        writer.Write(0);
        writer.Write(1);

        var tmacStart = stream.Position;
        writer.Write(Encoding.ASCII.GetBytes("TMAC"));
        writer.Write(0x1C);
        writer.Write((short)2);
        writer.Write((short)0);
        writer.Write(0);
        writer.Write(0);
        var tmacOffsetPos = stream.Position;
        writer.Write(0);
        writer.Write(0);

        var idBlock = stream.Position;
        writer.Write((short)2);

        var endPos = stream.Position;

        stream.Position = tmalOffsetPos;
        writer.Write((int)(idBlock - (tmalStart + 8)));

        stream.Position = tmacOffsetPos;
        writer.Write(0);

        stream.Position = sizePos;
        writer.Write((int)endPos);

        stream.Position = endPos;
        return stream.ToArray();
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(3u)]
    [InlineData(0x01000000u)]
    public void RoundTrip_KeepsTheFaceFieldExactly(uint face)
    {
        var source = BuildPap(face);

        var rewritten = new PapFile(new BinaryReader(new MemoryStream(source))).ToBytes();

        Assert.Equal(source, rewritten);
    }
}
