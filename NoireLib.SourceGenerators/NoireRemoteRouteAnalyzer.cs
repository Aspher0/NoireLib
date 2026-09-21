#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;

namespace NoireLib.SourceGenerators;

/// <summary>Reports two NoireRemote routes in one compilation that resolve to the same endpoint and member.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class NoireRemoteRouteAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NoireLib_005";

    private const string Category = "Usage";
    private const string EndpointAttribute = "NoireLib.Remote.NoireRemoteClassAttribute";
    private const string MemberAttribute = "NoireLib.Remote.NoireRemoteAttribute";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        title: "Two NoireRemote routes resolve to the same name",
        messageFormat: "The route '{0}' is declared twice. The listener refuses the second one and the endpoint is lost",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "An endpoint name is global inside one plugin's listener and a member name is global inside its " +
            "endpoint. Two declarations resolving to one route make the listener refuse the second at startup.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            var seen = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

            start.RegisterSymbolAction(symbol => AnalyzeType(symbol, seen), SymbolKind.NamedType);
        });
    }

    private static void AnalyzeType(SymbolAnalysisContext context, ConcurrentDictionary<string, byte> seen)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        // Interface endpoints describe a client only and never publish.
        if (type.TypeKind == TypeKind.Interface)
            return;

        var endpoint = NoireRemoteMemberAnalyzer.FindAttribute(type, EndpointAttribute);

        if (endpoint == null)
            return;

        var endpointName = ReadName(endpoint) ?? type.Name;

        foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
        {
            if (!NoireRemoteMemberAnalyzer.ShouldCheck(method))
                continue;

            var attribute = NoireRemoteMemberAnalyzer.FindAttribute(method, MemberAttribute);
            var memberName = (attribute == null ? null : ReadName(attribute)) ?? method.Name;
            var route = endpointName + "/" + memberName;

            if (seen.TryAdd(route, 0))
                continue;

            var location = method.Locations.FirstOrDefault();

            if (location != null)
                context.ReportDiagnostic(Diagnostic.Create(Rule, location, route));
        }
    }

    private static string? ReadName(AttributeData attribute)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == "Name" && argument.Value.Value is string named && named.Length > 0)
                return named;
        }

        if (attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is string positional && positional.Length > 0)
            return positional;

        return null;
    }

}
