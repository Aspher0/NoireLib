using System;
using System.IO;
using System.Reflection;

namespace NoireLib.Remote.Internal;

// CSS and script inline. The listener closes after one request and every asset would cost a connection.
internal static class HttpConsolePage
{
    private const string ResourceName = "NoireLib.Remote.Server.Console.console.html";
    private const string NoncePlaceholder = "__NONCE__";

    private static string? embedded;

    public static string Render(NoireRemoteServer server, string nonce)
    {
        var source = FromDisk(server.Options.ConsolePagePath) ?? Embedded();

        return source.Replace(NoncePlaceholder, nonce);
    }

    // For editing without a rebuild.
    private static string? FromDisk(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return File.Exists(path) ? File.ReadAllText(path!) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static string Embedded()
    {
        if (embedded != null)
            return embedded;

        var assembly = typeof(HttpConsolePage).Assembly;

        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? FindBySuffix(assembly)
            ?? throw new InvalidOperationException("The console page is not embedded in " + assembly.GetName().Name + ".");

        using var reader = new StreamReader(stream);
        embedded = reader.ReadToEnd();

        return embedded;
    }

    // A folder rename would break the resource name silently.
    private static Stream? FindBySuffix(Assembly assembly)
    {
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (name.EndsWith("console.html", StringComparison.OrdinalIgnoreCase))
                return assembly.GetManifestResourceStream(name);
        }

        return null;
    }
}
