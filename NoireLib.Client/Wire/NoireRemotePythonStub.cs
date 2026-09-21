using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NoireLib.Remote;

/// <summary>
/// Turns a listener's manifest into a self-contained Python client for it, needing only the standard library.
/// </summary>
public static class NoireRemotePythonStub
{
    // Names Python reserves, plus the few builtins a generated parameter would shadow badly.
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "False", "None", "True", "and", "as", "assert", "async", "await", "break", "class", "continue", "def",
        "del", "elif", "else", "except", "finally", "for", "from", "global", "if", "import", "in", "is", "lambda",
        "nonlocal", "not", "or", "pass", "raise", "return", "try", "while", "with", "yield", "type", "id", "list",
        "dict", "str", "int", "float", "bool", "bytes", "object", "self",
    };

    /// <summary>
    /// Generates the Python client for a manifest.
    /// </summary>
    /// <param name="manifest">The manifest to generate from.</param>
    /// <returns>The module source.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="manifest"/> is null.</exception>
    public static string FromManifest(NoireRemoteManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var sb = new StringBuilder();
        AppendHeader(sb, manifest);
        AppendTransport(sb);

        foreach (var endpoint in manifest.Endpoints)
        {
            sb.AppendLine();
            sb.AppendLine($"class {PyIdentifier(endpoint.Name)}:");
            sb.Append($"    \"\"\"{Escape(endpoint.Summary ?? endpoint.Name)}");
            sb.AppendLine("\"\"\"");

            if (endpoint.Members.Count == 0)
            {
                sb.AppendLine();
                sb.AppendLine("    pass");
                continue;
            }

            foreach (var member in endpoint.Members)
                AppendMember(sb, endpoint.Name, member);
        }

        return sb.ToString();
    }

    private static void AppendHeader(StringBuilder sb, NoireRemoteManifest manifest)
    {
        sb.AppendLine("\"\"\"Generated client for a NoireRemote listener. Do not edit: regenerate from the manifest.");
        sb.AppendLine();
        sb.AppendLine($"Plugin:   {Escape(manifest.Plugin)} {Escape(manifest.PluginVersion)}");
        sb.AppendLine($"Protocol: {manifest.Protocol.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine("\"\"\"");
        sb.AppendLine();
        sb.AppendLine("import glob");
        sb.AppendLine("import json");
        sb.AppendLine("import os");
        sb.AppendLine("import urllib.error");
        sb.AppendLine("import urllib.request");
        sb.AppendLine();
        sb.AppendLine($"PLUGIN = {PyString(manifest.Plugin)}");
        sb.AppendLine($"PROTOCOL = {manifest.Protocol.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine();
    }

    private static void AppendTransport(StringBuilder sb)
    {
        sb.AppendLine(@"class NoireRemoteError(RuntimeError):
    """"""A call the listener refused, carrying its error code and message.""""""

    def __init__(self, status, code, message):
        super().__init__(f""{code}: {message}"")
        self.status = status
        self.code = code
        self.message = message

def listener(plugin=PLUGIN):
    """"""Finds the plugin's live listener through the instance registry.

    A session that crashed leaves its record behind. The newest heartbeat wins over the first match.
    Calling a dead listener with its old credential is how a client locks itself out: the listener refuses an
    address for five minutes after ten rejected credentials.
    """"""
    folder = os.path.join(os.environ[""LOCALAPPDATA""], ""NoireLib"", ""http"", ""instances"")
    best = None
    for path in glob.glob(os.path.join(folder, ""*.json"")):
        try:
            with open(path, encoding=""utf-8"") as handle:
                record = json.load(handle)
        except (OSError, ValueError):
            continue
        if record.get(""plugin"") != plugin:
            continue
        if best is None or record.get(""heartbeatUtc"", """") > best.get(""heartbeatUtc"", """"):
            best = record
    if best is None:
        raise NoireRemoteError(0, ""NoListener"", f""no live listener for {plugin}"")
    return best

def call(endpoint, member, args=None, timeout=30):
    """"""Invokes one published member.

    Arguments travel under an ""args"" object. Sending them at the top level binds nothing. Every parameter
    silently takes its default, and a call can then appear to succeed while changing nothing.
    """"""
    host = listener()
    url = f""http://{host['address']}:{host['port']}/noire/v1/{endpoint}/{member}""
    body = json.dumps({""args"": args or {}}).encode(""utf-8"")
    request = urllib.request.Request(
        url,
        data=body,
        method=""POST"",
        headers={
            ""Authorization"": ""Bearer "" + host[""token""],
            ""Content-Type"": ""application/json"",
        },
    )
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            payload = json.load(response)
    except urllib.error.HTTPError as failure:
        detail = failure.read().decode(""utf-8"", ""replace"")
        try:
            error = json.loads(detail).get(""error"", {})
        except ValueError:
            error = {}
        raise NoireRemoteError(failure.code, error.get(""code"", ""Http""), error.get(""message"", detail)) from failure

    if not payload.get(""ok"", False):
        error = payload.get(""error"", {})
        raise NoireRemoteError(200, error.get(""code"", ""Failed""), error.get(""message"", """"))
    return payload.get(""result"")
");
    }

    private static void AppendMember(StringBuilder sb, string endpoint, NoireRemoteManifestMember member)
    {
        var parameters = new List<string>();
        var required = new List<NoireRemoteManifestParameter>();
        var optional = new List<NoireRemoteManifestParameter>();

        foreach (var p in member.Parameters)
        {
            if (p.Required)
                required.Add(p);
            else
                optional.Add(p);
        }

        foreach (var p in required)
            parameters.Add(PyIdentifier(p.Name));
        foreach (var p in optional)
            parameters.Add($"{PyIdentifier(p.Name)}=None");

        sb.AppendLine();
        sb.AppendLine("    @staticmethod");
        sb.AppendLine($"    def {PyIdentifier(member.Name)}({string.Join(", ", parameters)}):");

        var summary = member.Summary;
        if (!string.IsNullOrWhiteSpace(summary))
        {
            sb.Append("        \"\"\"").Append(Escape(summary!.Trim()));
            sb.AppendLine("\"\"\"");
        }

        if (member.Parameters.Count == 0)
        {
            sb.AppendLine($"        return call({PyString(endpoint)}, {PyString(member.Name)})");
            return;
        }

        sb.AppendLine("        args = {}");
        foreach (var p in required)
            sb.AppendLine($"        args[{PyString(p.Name)}] = {PyIdentifier(p.Name)}");

        foreach (var p in optional)
        {
            // An omitted optional parameter is left out. The listener applies the member's own default.
            sb.AppendLine($"        if {PyIdentifier(p.Name)} is not None:");
            sb.AppendLine($"            args[{PyString(p.Name)}] = {PyIdentifier(p.Name)}");
        }

        sb.AppendLine($"        return call({PyString(endpoint)}, {PyString(member.Name)}, args)");
    }

    private static string PyIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "_";

        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');

        if (char.IsDigit(sb[0]))
            sb.Insert(0, '_');

        var result = sb.ToString();
        return Reserved.Contains(result) ? result + "_" : result;
    }

    private static string PyString(string value) => "\"" + Escape(value) + "\"";

    private static string Escape(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
}
