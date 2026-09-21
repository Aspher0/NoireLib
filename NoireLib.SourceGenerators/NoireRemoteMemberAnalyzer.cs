#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace NoireLib.SourceGenerators;

/// <summary>Reports a member of a NoireRemote endpoint whose signature cannot cross the wire.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class NoireRemoteMemberAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NoireLib_004";

    private const string Category = "Usage";
    private const string EndpointAttribute = "NoireLib.Remote.NoireRemoteClassAttribute";
    private const string MemberAttribute = "NoireLib.Remote.NoireRemoteAttribute";

    private static readonly string[] GameAssemblyPrefixes =
    [
        "Dalamud", "FFXIVClientStructs", "Lumina", "InteropGenerator", "HexaGen", "TerraFX",
    ];

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        title: "A published member's signature has to survive JSON",
        messageFormat: "'{0}' cannot be published: {1}. Change the signature or drop its [NoireRemote]",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "NoireRemote hands UTF-8 JSON to a caller that may have no CLR at all. Every parameter type and the " +
            "return type has to round-trip through it. A member that fails the check is dropped when the type " +
            "publishes, and the route only shows up missing at call time.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        var endpoint = FindAttribute(type, EndpointAttribute);

        if (endpoint == null)
            return;

        foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
        {
            if (!ShouldCheck(method))
                continue;

            var reason = Refuse(method);

            if (reason == null)
                continue;

            var location = method.Locations.FirstOrDefault();

            if (location != null)
                context.ReportDiagnostic(Diagnostic.Create(Rule, location, method.Name, reason));
        }
    }

    internal static bool ShouldCheck(IMethodSymbol method)
    {
        if (method.MethodKind != MethodKind.Ordinary)
            return false;

        return FindAttribute(method, MemberAttribute) != null;
    }

    internal static string? Refuse(IMethodSymbol method)
    {
        if (method.IsGenericMethod)
            return "a generic method has no fixed signature to bind arguments against";

        foreach (var parameter in method.Parameters)
        {
            if (parameter.RefKind != RefKind.None)
                return "parameter '" + parameter.Name + "' is passed by reference";

            var reason = RefuseType(parameter.Type);

            if (reason != null)
                return "parameter '" + parameter.Name + "' is " + reason;
        }

        var returnReason = RefuseType(UnwrapReturn(method.ReturnType));

        return returnReason == null ? null : "the return type is " + returnReason;
    }

    internal static ITypeSymbol UnwrapReturn(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named && named.TypeArguments.Length == 1)
        {
            var name = named.ConstructedFrom.ToDisplayString();

            if (name == "System.Threading.Tasks.Task<TResult>" || name == "System.Threading.Tasks.ValueTask<TResult>")
                return named.TypeArguments[0];
        }

        return type;
    }

    internal static string? RefuseType(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Pointer || type.TypeKind == TypeKind.FunctionPointer)
            return "a pointer type";

        if (type.TypeKind == TypeKind.Delegate)
            return "a delegate";

        var display = type.ToDisplayString();

        if (display == "nint" || display == "nuint" || display == "System.IntPtr" || display == "System.UIntPtr")
            return "a pointer-sized integer, which carries no shape a caller can read";

        if (display == "object")
            return "object, which carries no shape";

        var assembly = type.ContainingAssembly?.Name;

        if (assembly != null)
        {
            foreach (var prefix in GameAssemblyPrefixes)
            {
                if (assembly.StartsWith(prefix, StringComparison.Ordinal))
                    return "declared in " + assembly + ", which a caller outside the game cannot load";
            }
        }

        return null;
    }

    internal static AttributeData? FindAttribute(ISymbol symbol, string metadataName)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == metadataName)
                return attribute;
        }

        return null;
    }

}
