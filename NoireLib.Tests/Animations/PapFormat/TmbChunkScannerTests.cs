using System.Linq;
using System.Text;
using NoireLib.Animations.PapFormat.Tmb;
using Xunit;

namespace NoireLib.Tests;

public class TmbChunkScannerTests
{
    private static byte[] AttendWindow()
    {
        var window = new byte[0x40];
        var qwords = new ulong[]
        {
            0x0000000100000046, 0x0000001852544D54, 0x0000002C00000003, 0x0000000000000001,
            0x0000001839303043, 0x000000C800000004, 0x0000001600000000, 0x6263000400030002,
        };

        for (var i = 0; i < qwords.Length; i++)
        {
            var value = qwords[i];
            for (var b = 0; b < 8; b++)
                window[i * 8 + b] = (byte)(value >> (b * 8));
        }

        return window;
    }

    private static byte[] SyntheticStream()
    {
        var stream = new byte[0x60];

        void Tag(int offset, string tag, int size)
        {
            for (var i = 0; i < 4; i++)
                stream[offset + i] = (byte)tag[i];

            Word(offset + 4, size);
        }

        void Word(int offset, int value)
        {
            stream[offset + 0] = (byte)value;
            stream[offset + 1] = (byte)(value >> 8);
            stream[offset + 2] = (byte)(value >> 16);
            stream[offset + 3] = (byte)(value >> 24);
        }

        Tag(0x00, "TMDH", 0x10);
        Tag(0x10, "TMAL", 0x10);
        Tag(0x20, "TMTR", 0x18);
        Tag(0x38, "C010", 0x10);
        Word(0x40, 0x10); // chunk-relative: 0x38 + 0x10 = 0x48, where the name sits
        Word(0x44, 0x00);

        var name = Encoding.ASCII.GetBytes("bp088cf81457_loop");
        name.CopyTo(stream, 0x48);
        return stream;
    }

    public class TagBytes
    {
        [Fact]
        public void AnUppercaseLetter_IsATagByte()
            => Assert.True(TmbChunkScanner.IsTagByte((byte)'T'));

        [Fact]
        public void ADigit_IsATagByte()
            => Assert.True(TmbChunkScanner.IsTagByte((byte)'0'));

        [Fact]
        public void ALowercaseLetter_IsNotATagByte()
            => Assert.False(TmbChunkScanner.IsTagByte((byte)'t'));

        [Fact]
        public void AZeroByte_IsNotATagByte()
            => Assert.False(TmbChunkScanner.IsTagByte(0));

        [Fact]
        public void AFourLetterRun_ReadsAsATag()
        {
            Assert.True(TmbChunkScanner.TryReadTag(AttendWindow(), 0x08, out var tag));
            Assert.Equal("TMTR", tag);
        }

        [Fact]
        public void ARunOfSmallIntegers_IsNotATag()
            => Assert.False(TmbChunkScanner.TryReadTag(AttendWindow(), 0x00, out _));

        [Fact]
        public void ATagRunningPastTheWindow_IsRefused()
            => Assert.False(TmbChunkScanner.TryReadTag(AttendWindow(), 0x3E, out _));
    }

    public class ChunkHeaders
    {
        [Fact]
        public void TheSizeWord_IsLittleEndian()
            => Assert.Equal(0x18, TmbChunkScanner.ReadInt32(AttendWindow(), 0x0C));

        [Fact]
        public void ARealStreamsFirstChunk_IsTmtrOfSize0x18()
        {
            Assert.True(TmbChunkScanner.TryReadChunk(AttendWindow(), 0x08, out var chunk));
            Assert.Equal(new TmbChunk(0x08, "TMTR", 0x18), chunk);
        }

        [Fact]
        public void ARealStreamsSecondChunk_IsC009OfSize0x18()
        {
            Assert.True(TmbChunkScanner.TryReadChunk(AttendWindow(), 0x20, out var chunk));
            Assert.Equal(new TmbChunk(0x20, "C009", 0x18), chunk);
        }

        [Fact]
        public void AnUnalignedSize_IsRefused()
        {
            var data = SyntheticStream();
            data[0x04] = 0x12;
            Assert.False(TmbChunkScanner.TryReadChunk(data, 0x00, out _));
        }

        [Fact]
        public void ASizeSmallerThanAHeader_IsRefused()
        {
            var data = SyntheticStream();
            data[0x04] = 0x04;
            Assert.False(TmbChunkScanner.TryReadChunk(data, 0x00, out _));
        }

        [Fact]
        public void ASizeBeyondTheCeiling_IsRefused()
        {
            var data = SyntheticStream();
            data[0x06] = 0xFF;
            Assert.False(TmbChunkScanner.TryReadChunk(data, 0x00, out _));
        }

        [Fact]
        public void AChunkRunningPastTheWindow_IsRefused()
        {
            var data = SyntheticStream();
            Assert.True(TmbChunkScanner.TryReadChunk(data, 0x38, out _));

            data[0x3C] = 0x40; // C010 claiming 0x40 bytes from 0x38 in a 0x60 window
            Assert.False(TmbChunkScanner.TryReadChunk(data, 0x38, out _));
        }
    }

    public class Walking
    {
        [Fact]
        public void TheSyntheticStream_WalksInOrder()
        {
            var chunks = TmbChunkScanner.Walk(SyntheticStream(), 0, 16, SyntheticStream().Length);
            Assert.Equal(new[] { "TMDH", "TMAL", "TMTR", "C010" }, chunks.Select(c => c.Tag).ToArray());
        }

        [Fact]
        public void TheWalk_StopsAtTheChunkCap()
            => Assert.Equal(2, TmbChunkScanner.Walk(SyntheticStream(), 0, 2, SyntheticStream().Length).Count);

        [Fact]
        public void TheWalk_StopsWhereTheStreamStopsValidating()
            => Assert.Equal(2, TmbChunkScanner.Walk(AttendWindow(), 0x08, 16, AttendWindow().Length).Count);
    }

    public class NameRuns
    {
        [Fact]
        public void AnEmbeddedName_IsFound()
        {
            var names = TmbChunkScanner.FindNames(SyntheticStream(), 0, SyntheticStream().Length, minLength: 6, maxResults: 8);
            Assert.Contains(names, n => n.Offset == 0x48 && n.Text == "bp088cf81457_loop");
        }

        [Fact]
        public void ShortRuns_AreIgnored()
            => Assert.DoesNotContain(TmbChunkScanner.FindNames(SyntheticStream(), 0, SyntheticStream().Length, minLength: 6, maxResults: 8),
                n => n.Text == "TMDH");

        [Fact]
        public void TheResultCap_IsHonoured()
            => Assert.Single(TmbChunkScanner.FindNames(SyntheticStream(), 0, SyntheticStream().Length, minLength: 4, maxResults: 1));

        [Fact]
        public void AWindowOfIntegers_YieldsNoNames()
            => Assert.Empty(TmbChunkScanner.FindNames(new byte[0x40], 0, 0x40, minLength: 6, maxResults: 8));
    }

    public class StreamHeader
    {
        private static byte[] Headered()
        {
            var stream = SyntheticStream();
            var headed = new byte[TmbChunkScanner.StreamHeaderSize + stream.Length];
            Encoding.ASCII.GetBytes(TmbChunkScanner.StreamTag).CopyTo(headed, 0);
            var size = TmbChunkScanner.StreamHeaderSize + 0x48; // header + four chunks, name data excluded
            headed[4] = (byte)size;
            headed[5] = (byte)(size >> 8);
            headed[8] = 4; // entry count
            stream.CopyTo(headed, TmbChunkScanner.StreamHeaderSize);
            return headed;
        }

        [Fact]
        public void ATmlbHeader_ReadsItsSizeAndCount()
        {
            Assert.True(TmbChunkScanner.TryReadStreamHeader(Headered(), 0, out var size, out var count));
            Assert.Equal(TmbChunkScanner.StreamHeaderSize + 0x48, size);
            Assert.Equal(4, count);
        }

        [Fact]
        public void AChunkTagIsNotAStreamHeader()
            => Assert.False(TmbChunkScanner.TryReadStreamHeader(SyntheticStream(), 0, out _, out _));

        [Fact]
        public void AnImplausibleSize_IsRefused()
        {
            var data = Headered();
            data[4] = 0x02; // smaller than the header itself
            data[5] = 0x00;
            Assert.False(TmbChunkScanner.TryReadStreamHeader(data, 0, out _, out _));
        }

        [Fact]
        public void TheWalkStartsAfterTheHeader()
        {
            var data = Headered();
            var chunks = TmbChunkScanner.Walk(data, TmbChunkScanner.StreamHeaderSize, 16, data.Length);
            Assert.Equal(new[] { "TMDH", "TMAL", "TMTR", "C010" }, chunks.Select(c => c.Tag).ToArray());
        }

        [Fact]
        public void TheWalkStopsAtTheDeclaredStreamEnd()
        {
            var data = Headered();
            var chunks = TmbChunkScanner.Walk(data, TmbChunkScanner.StreamHeaderSize, 16,
                TmbChunkScanner.StreamHeaderSize + 0x38);
            Assert.Equal(new[] { "TMDH", "TMAL", "TMTR" }, chunks.Select(c => c.Tag).ToArray());
        }

        [Fact]
        public void ANameOutsideTheStream_IsNotReported()
            => Assert.Empty(TmbChunkScanner.FindNames(SyntheticStream(), 0, 0x48, minLength: 6, maxResults: 8));

        [Fact]
        public void ANameInsideTheStream_IsStillReported()
            => Assert.Contains(TmbChunkScanner.FindNames(SyntheticStream(), 0x40, 0x60, 6, 8),
                n => n.Text == "bp088cf81457_loop");
    }

    public class Fingerprinting
    {
        private static readonly TmbChunk[] Conduct =
        [
            new(0, "TMDH", 0x10), new(0x10, "TMAL", 0x10), new(0x20, "C010", 0x28),
        ];

        private static readonly TmbChunk[] Attend = [new(0, "TMDH", 0x10), new(0x10, "TMAL", 0x10)];

        private static string ConductPrint()
            => TmbChunkScanner.Fingerprint(0x1CC, 12, Conduct,
                [new TmbNameSighting(0x40, "cfxf_smile_wk")], 16, 3);

        private static string AttendPrint()
            => TmbChunkScanner.Fingerprint(0x93, 5, Attend, [], 16, 3);

        [Fact]
        public void TheFingerprint_CarriesSizeCountAndTags()
            => Assert.Equal("0x1CC/12e/TMDH-TMAL-C010/cfxf_smile_wk", ConductPrint());

        [Fact]
        public void DifferentContents_FingerprintDifferently()
            => Assert.NotEqual(ConductPrint(), AttendPrint());

        [Fact]
        public void TheSameContent_FingerprintsIdentically()
            => Assert.Equal(ConductPrint(), ConductPrint());

        [Fact]
        public void TheTagListIsCapped()
            => Assert.Contains("+1", TmbChunkScanner.Fingerprint(0x10, 3, Conduct, [], 2, 3));
    }
}
