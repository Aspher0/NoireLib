using Dalamud.Bindings.ImGui;
using FluentAssertions;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks how an id hashes through the bindings' UTF-8 overloads against how it hashes through their string ones.
/// </summary>
/// <remarks>
/// This is the question ADR 0002 held ids back on, and it is settled here by measurement rather than by reading the
/// bindings. Getting UTF-8 wrong for display text is a visible glitch; getting it wrong for an id is silent, because a
/// widget id that hashes differently orphans every value a user saved under it and nothing about that looks like an
/// encoding fault.<br/>
/// The finding is that the overloads measure their span rather than reading to a terminator: an unterminated span is
/// the same id as the equivalent string, and a trailing null is content that changes it. Both halves are asserted,
/// because the second is what makes the first easy to break by being helpful.<br/>
/// Run inside a real frame, because <c>GetID</c> hashes against the current id stack and there is no stack outside one.
/// </remarks>
[Collection(NoireUiTestCollection.Name)]
public sealed class UiIdEncodingTests : IClassFixture<UiHarness>
{
    private readonly UiHarness harness;

    public UiIdEncodingTests(UiHarness harness) => this.harness = harness;

    /// <summary>
    /// The cases that matter: plain, both id-splitting markers, and content whose UTF-8 and UTF-16 lengths differ.
    /// </summary>
    /// <remarks>
    /// <c>##</c> hides the part after it from the label but keeps it in the id; <c>###</c> replaces the id outright.
    /// Both change how ImGui splits a string, so an encoding difference could land on one and not the others.
    /// </remarks>
    private static readonly string[] Cases =
    [
        "Save",
        string.Empty,
        "Label##hidden",
        "Label###replaced",
        "##leading",
        "###leading",
        "Sauvegarder les reglages",
        "Éléments",
        "日本語のラベル",
        "emoji 🎲 id",
        "mixed Élé##日本語",
    ];

    [Fact]
    public void GetId_OverAnUnterminatedSpan_MatchesTheStringHash()
    {
        var mismatches = new List<string>();

        harness.Draw(() =>
        {
            foreach (var id in Cases)
            {
                var fromString = ImGui.GetID(id);
                var fromBytes = ImGui.GetID(Encoding.UTF8.GetBytes(id));

                if (fromString != fromBytes)
                    mismatches.Add($"{id} (string {fromString:X8}, bytes {fromBytes:X8})");
            }
        });

        // Reported together rather than one assertion per case, so a run says which cases disagree instead of stopping
        // at the first.
        mismatches.Should().BeEmpty(
            "an id encoded to UTF-8 must hash the same as the string it came from, or moving one to bytes would "
            + "silently orphan every value saved under it");
    }

    [Fact]
    public void GetId_OverATerminatedSpan_DoesNotMatchTheStringHash()
    {
        var matches = new List<string>();

        harness.Draw(() =>
        {
            foreach (var id in Cases)
            {
                var fromString = ImGui.GetID(id);

                var bytes = Encoding.UTF8.GetBytes(id);
                var terminated = new byte[bytes.Length + 1];
                bytes.CopyTo(terminated, 0);

                if (fromString == ImGui.GetID(terminated))
                    matches.Add(id);
            }
        });

        // The overloads take a length, so a trailing null is one more byte of content rather than the end of the
        // string. Asserted rather than left implicit: appending a terminator is the obvious thing to do when handing
        // bytes to a C API, and doing it here would change every id it touched.
        matches.Should().BeEmpty(
            "a trailing null is content to these overloads, so anything building an id must not append one");
    }

    [Fact]
    public void GetId_OverAUtf8Literal_MatchesTheStringHash()
    {
        uint fromString = 0;
        uint fromLiteral = 0;
        uint fromSplitString = 0;
        uint fromSplitLiteral = 0;

        harness.Draw(() =>
        {
            fromString = ImGui.GetID("Save");
            fromLiteral = ImGui.GetID("Save"u8);

            fromSplitString = ImGui.GetID("Label###replaced");
            fromSplitLiteral = ImGui.GetID("Label###replaced"u8);
        });

        // The form the library would actually write. A u8 literal's span excludes the null the compiler places after
        // it, which is what puts it on the safe side of the two tests above rather than the unsafe one.
        fromLiteral.Should().Be(fromString);
        fromSplitLiteral.Should().Be(fromSplitString);
    }
}
