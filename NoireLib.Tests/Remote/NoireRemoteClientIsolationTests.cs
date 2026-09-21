using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the one property the caller half has to keep: it loads with no Dalamud on the probing path. A program
/// outside the game references this assembly alone, and a Dalamud type appearing in one of its signatures would break
/// every such program with no compile error and no other test noticing.
/// </summary>
public sealed class NoireRemoteClientIsolationTests
{
    private static readonly string[] AllowedPrefixes = ["System.", "netstandard", "mscorlib"];

    // Socket.IO packages are listed by name. A new one fails this test.
    private static readonly string[] AllowedNames =
    [
        "Newtonsoft.Json", "System", "netstandard", "mscorlib",
        "SocketIOClient", "SocketIO.Core", "SocketIO.Serializer.Core", "SocketIO.Serializer.NewtonsoftJson",
    ];

    private static readonly string[] ForbiddenPrefixes = ["Dalamud", "FFXIVClientStructs", "ImGui", "Lumina", "Serilog"];

    private static string ClientAssemblyPath()
        => Path.Combine(AppContext.BaseDirectory, "NoireLib.Client.dll");

    [Fact]
    public void TheClientAssembly_ShipsBesideTheLibrary()
    {
        File.Exists(ClientAssemblyPath()).Should().BeTrue(
            "a plugin referencing NoireLib carries the caller half with it, and the tests read it from the same folder");
    }

    [Fact]
    public void TheClientAssembly_ReferencesOnlyTheBaseClassLibraryNewtonsoftAndTheSocketIoPackages()
    {
        var offenders = new List<string>();

        foreach (var name in ReferencedAssemblyNames())
        {
            if (!IsAllowed(name))
                offenders.Add(name);
        }

        offenders.Should().BeEmpty(
            "a caller outside the game resolves every one of these assemblies itself, and nothing else is there to resolve");
    }

    [Fact]
    public void TheClientAssembly_ReferencesNothingFromTheGameSide()
    {
        var offenders = new List<string>();

        foreach (var name in ReferencedAssemblyNames())
        {
            foreach (var forbidden in ForbiddenPrefixes)
            {
                if (name.StartsWith(forbidden, StringComparison.Ordinal))
                    offenders.Add(name);
            }
        }

        offenders.Should().BeEmpty(
            "a console program referencing this assembly alone has none of these on its probing path");
    }

    private static List<string> ReferencedAssemblyNames()
    {
        var names = new List<string>();

        using var stream = File.OpenRead(ClientAssemblyPath());
        using var reader = new PEReader(stream);

        var metadata = reader.GetMetadataReader();

        foreach (var handle in metadata.AssemblyReferences)
            names.Add(metadata.GetString(metadata.GetAssemblyReference(handle).Name));

        return names;
    }

    private static bool IsAllowed(string name)
    {
        foreach (var allowed in AllowedNames)
        {
            if (string.Equals(name, allowed, StringComparison.Ordinal))
                return true;
        }

        foreach (var prefix in AllowedPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    [Fact]
    public void TheClientAssembly_DeclaresTheWireContractTypesTheDocumentationNames()
    {
        var assembly = typeof(Remote.NoireRemoteClient).Assembly;

        assembly.GetName().Name.Should().Be("NoireLib.Client");

        foreach (var name in new[]
        {
            nameof(Remote.NoireRemoteInstanceRecord),
            nameof(Remote.NoireRemoteEnvelope),
            nameof(Remote.NoireRemoteError),
            nameof(Remote.NoireRemoteJobStatus),
            nameof(Remote.NoireRemoteManifest),
            nameof(Remote.NoireRemoteEvent),
            nameof(Remote.NoireRemoteHeaders),
            nameof(Remote.NoireRemoteErrorCodes),
            nameof(Remote.NoireRemotePaths),
            nameof(Remote.NoireRemoteSignature),
            nameof(Remote.NoireRemoteDirectory),
            nameof(Remote.NoireRemoteClassAttribute),
        })
        {
            assembly.GetType("NoireLib.Remote." + name).Should().NotBeNull(
                "the caller declares the same contract the listener serves, from the same assembly");
        }
    }

    [Fact]
    public void TheListener_LivesInTheClientAndOnlyTheFacadeStaysBehind()
    {
        typeof(Remote.NoireRemote).Assembly.GetName().Name.Should().Be("NoireLib");
        typeof(Remote.NoireRemoteOptions).Assembly.GetName().Name.Should().Be("NoireLib.Client");
        typeof(Remote.NoireRemoteServer).Assembly.GetName().Name.Should().Be("NoireLib.Client");
    }
}
