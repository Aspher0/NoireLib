using FluentAssertions;
using Newtonsoft.Json.Linq;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the share-code format and the limits it decodes under. The format is permanent from the first code a user
/// pastes anywhere, and the input is authored by strangers, so both the round-trip and every refusal path are contracts
/// rather than implementation details.
/// </summary>
public class ShareCodeHelperTests : IDisposable
{
    private const string Kind = "tests.preset";

    private readonly ShareCodeLimits originalLimits = ShareCodeHelper.Limits;

    public void Dispose()
    {
        ShareCodeHelper.Limits = originalLimits;
        GC.SuppressFinalize(this);
    }

    /// <summary>An inert data type, which is the only thing a share code should ever be decoded into.</summary>
    private sealed class PresetDto
    {
        public string Name { get; set; } = string.Empty;

        public int Interval { get; set; }

        public List<string> Tags { get; set; } = new();

        public bool Enabled { get; set; }
    }

    private static PresetDto SamplePreset() => new()
    {
        Name = "Evening",
        Interval = 250,
        Tags = new List<string> { "combat", "raid", "solo" },
        Enabled = true,
    };

    /// <summary>Rewrites the payload bytes of a code, to forge the damage a chat client or an editor would do.</summary>
    private static string TamperWith(string code, Func<byte[], byte[]> edit)
    {
        var bytes = code[ShareCodeHelper.Prefix.Length..].FromBase64();
        return ShareCodeHelper.Prefix + edit(bytes).ToBase64(urlSafe: true);
    }

    #region Round trip

    [Fact]
    public void Encode_ProducesAPrefixedSingleToken()
    {
        var code = ShareCodeHelper.Encode(Kind, SamplePreset());

        code.Should().StartWith(ShareCodeHelper.Prefix);
        code.Should().NotContainAny(
            new[] { " ", "\n", "\t", "+", "/", "=" },
            "a code has to survive being pasted into a chat box");
    }

    [Fact]
    public void Decode_RoundTripsTheValue()
    {
        var code = ShareCodeHelper.Encode(Kind, SamplePreset());

        var result = ShareCodeHelper.Decode<PresetDto>(code, Kind);

        result.Success.Should().BeTrue(result.Message);
        result.Kind.Should().Be(Kind);
        result.Value!.Name.Should().Be("Evening");
        result.Value.Interval.Should().Be(250);
        result.Value.Tags.Should().Equal(new[] { "combat", "raid", "solo" });
        result.Value.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Decode_RoundTripsAPayloadTooSmallToBeWorthCompressing()
    {
        // Short payloads come out larger when deflated, so they travel raw. Both branches have to read back.
        var code = ShareCodeHelper.Encode(Kind, 7);

        ShareCodeHelper.Decode<int>(code, Kind).Value.Should().Be(7);
    }

    [Fact]
    public void Decode_RoundTripsALargeRepetitivePayload()
    {
        var big = Enumerable.Repeat("the same line over and over", 4000).ToList();

        var code = ShareCodeHelper.Encode(Kind, big);

        code.Length.Should().BeLessThan(big.Sum(s => s.Length), "a repetitive payload has to compress or the format is not doing its job");
        ShareCodeHelper.Decode<List<string>>(code, Kind).Value.Should().HaveCount(4000);
    }

    [Fact]
    public void Decode_TolerantOfSurroundingWhitespace()
    {
        var code = ShareCodeHelper.Encode(Kind, SamplePreset());

        ShareCodeHelper.Decode<PresetDto>($"  \n {code} \r\n ", Kind).Success.Should().BeTrue("people paste with whitespace attached");
    }

    #endregion

    #region Kind

    [Fact]
    public void Decode_WithADifferentKind_RefusesAndSaysWhich()
    {
        var code = ShareCodeHelper.Encode("tests.theme", SamplePreset());

        var result = ShareCodeHelper.Decode<PresetDto>(code, Kind);

        result.Success.Should().BeFalse();
        result.Error.Should().Be(ShareCodeError.WrongKind);
        result.Kind.Should().Be("tests.theme", "the mismatch cannot be explained without naming both sides");
        result.Message.Should().Contain("tests.theme").And.Contain(Kind);
    }

    [Fact]
    public void Decode_WithNoExpectedKind_AcceptsAnyKind()
    {
        var code = ShareCodeHelper.Encode("tests.theme", SamplePreset());

        ShareCodeHelper.Decode<PresetDto>(code, string.Empty).Success.Should().BeTrue();
    }

    [Fact]
    public void TryReadKind_ReadsTheTagWithoutDecodingThePayload()
    {
        var code = ShareCodeHelper.Encode("tests.layout", SamplePreset());

        ShareCodeHelper.TryReadKind(code, out var kind).Should().BeTrue();
        kind.Should().Be("tests.layout");
    }

    [Fact]
    public void TryReadKind_OnSomethingElse_ReturnsFalse()
    {
        ShareCodeHelper.TryReadKind("hello", out var kind).Should().BeFalse();
        kind.Should().BeEmpty();
    }

    [Fact]
    public void Encode_WithABlankKind_Throws()
    {
        var act = () => ShareCodeHelper.Encode("  ", SamplePreset());

        act.Should().Throw<ArgumentException>("an untagged code could be applied to the wrong thing");
    }

    [Fact]
    public void Encode_WithAnOversizedKind_Throws()
    {
        var act = () => ShareCodeHelper.Encode(new string('k', 300), SamplePreset());

        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region Refusals

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Decode_WithNothing_ReportsEmpty(string? code)
    {
        ShareCodeHelper.Decode<PresetDto>(code, Kind).Error.Should().Be(ShareCodeError.Empty);
    }

    [Fact]
    public void Decode_OfUnrelatedText_ReportsNotAShareCode()
    {
        var result = ShareCodeHelper.Decode<PresetDto>("just some text someone pasted", Kind);

        result.Error.Should().Be(ShareCodeError.NotAShareCode);
        result.Message.Should().Contain(ShareCodeHelper.Prefix, "the message has to tell them what a code looks like");
    }

    [Fact]
    public void Decode_OfAFutureFormat_ReportsWrongVersion()
    {
        var result = ShareCodeHelper.Decode<PresetDto>("NOIRE9-AAAAAAAA", Kind);

        result.Error.Should().Be(ShareCodeError.WrongVersion, "a later format has to read as 'update the plugin', not as 'this is broken'");
    }

    [Fact]
    public void Decode_OfInvalidBase64_ReportsMalformed()
    {
        ShareCodeHelper.Decode<PresetDto>(ShareCodeHelper.Prefix + "!!!not base64!!!", Kind)
            .Error.Should().Be(ShareCodeError.Malformed);
    }

    [Fact]
    public void Decode_OfATruncatedCode_ReportsMalformed()
    {
        var code = ShareCodeHelper.Encode(Kind, SamplePreset());

        var half = code[..(ShareCodeHelper.Prefix.Length + 4)];

        ShareCodeHelper.Decode<PresetDto>(half, Kind).Error.Should().Be(ShareCodeError.Malformed, "half a code is the most common bad paste there is");
    }

    [Fact]
    public void Decode_OfAnEditedPayload_ReportsChecksumMismatch()
    {
        var code = ShareCodeHelper.Encode(Kind, SamplePreset());

        // Flip a bit in the last payload byte, leaving the header and the stored checksum intact.
        var tampered = TamperWith(code, bytes =>
        {
            bytes[^1] ^= 0xFF;
            return bytes;
        });

        var result = ShareCodeHelper.Decode<PresetDto>(tampered, Kind);

        result.Success.Should().BeFalse();
        result.Error.Should().BeOneOf(ShareCodeError.ChecksumMismatch, ShareCodeError.Malformed);
    }

    [Fact]
    public void Decode_WithAForgedChecksum_StillRefusesAnEditedPayload()
    {
        var code = ShareCodeHelper.Encode(Kind, SamplePreset());

        // Leave the payload alone and corrupt only the stored checksum, which isolates the check itself.
        var tampered = TamperWith(code, bytes =>
        {
            bytes[1] ^= 0xFF;
            return bytes;
        });

        ShareCodeHelper.Decode<PresetDto>(tampered, Kind).Error.Should().Be(ShareCodeError.ChecksumMismatch);
    }

    [Fact]
    public void Decode_OfAPayloadOfTheWrongShape_ReportsUnreadable()
    {
        var code = ShareCodeHelper.Encode(Kind, "just a string");

        ShareCodeHelper.Decode<PresetDto>(code, Kind).Error.Should().Be(ShareCodeError.Unreadable);
    }

    #endregion

    #region Limits

    [Fact]
    public void Encode_OfAPayloadOverTheLimit_ThrowsRatherThanProducingAnUnreadableCode()
    {
        ShareCodeHelper.Limits = new ShareCodeLimits { MaxDecodedBytes = 256 };

        var act = () => ShareCodeHelper.Encode(Kind, Enumerable.Repeat("padding", 200).ToList());

        act.Should().Throw<InvalidOperationException>("producing a code no conformant reader accepts only moves the failure to whoever pasted it");
    }

    [Fact]
    public void Decode_OfACodeThatExpandsPastTheLimit_RefusesInsteadOfExpandingIt()
    {
        // A small paste that inflates to a lot is the zip-bomb shape. The ceiling has to bite during decompression.
        ShareCodeHelper.Limits = new ShareCodeLimits { MaxDecodedBytes = 8 * 1024 * 1024 };
        var code = ShareCodeHelper.Encode(Kind, new string('a', 4 * 1024 * 1024));

        code.Length.Should().BeLessThan(64 * 1024, "the point of the test is a small code with a large expansion");

        ShareCodeHelper.Limits = new ShareCodeLimits { MaxDecodedBytes = 64 * 1024 };

        var result = ShareCodeHelper.Decode<string>(code, Kind);

        result.Success.Should().BeFalse();
        result.Error.Should().Be(ShareCodeError.TooLarge);
    }

    [Fact]
    public void Decode_OfAnOverlongCode_RefusesBeforeDoingAnyWork()
    {
        ShareCodeHelper.Limits = new ShareCodeLimits { MaxEncodedCharacters = 64 };

        var result = ShareCodeHelper.Decode<PresetDto>(ShareCodeHelper.Prefix + new string('A', 500), Kind);

        result.Error.Should().Be(ShareCodeError.TooLarge);
    }

    [Fact]
    public void Decode_OfADeeplyNestedPayload_RefusesBeforeTheStackGivesOut()
    {
        ShareCodeHelper.Limits = new ShareCodeLimits { MaxDepth = 256 };

        var nested = new JObject();
        var cursor = nested;
        for (var i = 0; i < 200; i++)
        {
            var child = new JObject();
            cursor["child"] = child;
            cursor = child;
        }

        var code = ShareCodeHelper.Encode(Kind, nested);

        ShareCodeHelper.Limits = new ShareCodeLimits { MaxDepth = 32 };

        var result = ShareCodeHelper.Decode<JObject>(code, Kind);

        result.Success.Should().BeFalse();
        result.Error.Should().Be(ShareCodeError.Unreadable, "depth has to be refused by the reader, because the stack overflow it causes is not catchable");
    }

    #endregion

    #region Security

    [Fact]
    public void Decode_NeverLetsThePayloadNameTheTypeItBecomes()
    {
        // TypeNameHandling stays off for every caller, whatever settings they pass, so a $type hint in a stranger's
        // paste is inert: it neither resolves a type nor reaches the decoded value.
        var payload = new JObject
        {
            ["$type"] = "System.Diagnostics.Process, System.Diagnostics.Process",
            ["Name"] = "Evening",
        };

        var code = ShareCodeHelper.Encode(Kind, payload);

        var asObject = ShareCodeHelper.Decode<object>(code, Kind);
        asObject.Success.Should().BeTrue(asObject.Message);
        asObject.Value.Should().BeOfType<JObject>("decoding into object must produce plain data, never the type the payload asked for");

        var asDictionary = ShareCodeHelper.Decode<Dictionary<string, string>>(code, Kind);
        asDictionary.Success.Should().BeTrue(asDictionary.Message);
        asDictionary.Value!["Name"].Should().Be("Evening");
        asDictionary.Value.Should().NotContainKey("$type", "the hint is consumed as metadata and resolves to nothing");
    }

    [Fact]
    public void Decode_OfTrailingContentAfterTheDocument_IsRefused()
    {
        var code = ShareCodeHelper.Encode(Kind, SamplePreset());
        var payloadStart = ShareCodeHelper.Prefix.Length;
        var bytes = code[payloadStart..].FromBase64();

        // Uncompressed payloads only: append a second document after the first one.
        var uncompressed = ShareCodeHelper.Encode(Kind, 1);
        var uncompressedBytes = uncompressed[payloadStart..].FromBase64();
        (uncompressedBytes[0] & 0x01).Should().Be(0, "a one-byte payload is not worth compressing");

        var extended = uncompressedBytes.Concat("{}"u8.ToArray()).ToArray();

        var result = ShareCodeHelper.Decode<int>(ShareCodeHelper.Prefix + extended.ToBase64(urlSafe: true), Kind);

        result.Success.Should().BeFalse("content after the document means the code was appended to");
        bytes.Should().NotBeEmpty();
    }

    #endregion
}
