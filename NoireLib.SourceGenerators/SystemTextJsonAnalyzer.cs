#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace NoireLib.SourceGenerators;

/// <summary>
/// Reports a System.Text.Json attribute on a type that a NoireLib configuration serializes. Configurations are written
/// by Newtonsoft.Json, which ignores those attributes, so the file silently differs from what the attribute asks for.
/// Types no configuration reaches are left alone.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class SystemTextJsonAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NoireLib_002";

    private const string Category = "Usage";
    private const string ConfigBaseName = "NoireConfigBase";
    private const string ConfigBaseNamespace = "NoireLib.Configuration";
    private const string SystemTextJsonNamespace = "System.Text.Json";

    // Caps the serialization walk so a self-nesting generic cannot expand forever.
    private const int MaxVisitedTypes = 20000;

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        title: "A configuration serializes a System.Text.Json attribute",
        messageFormat: "'{0}' is a System.Text.Json attribute on '{1}', which configuration '{2}' serializes. NoireLib configurations are serialized by Newtonsoft.Json, which ignores it. Use the Newtonsoft.Json equivalent",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Classes deriving from NoireConfigBase are written and read by Newtonsoft.Json. A System.Text.Json attribute " +
            "on the configuration or on any type it serializes, directly or through a collection, has no effect: a " +
            "renamed property keeps its name, an ignored one is still written, a converter never runs.");

    private static readonly string[] NewtonsoftOptInAttributes =
    [
        "Newtonsoft.Json.JsonPropertyAttribute",
        "Newtonsoft.Json.JsonRequiredAttribute",
        "Newtonsoft.Json.JsonExtensionDataAttribute",
        "System.Runtime.Serialization.DataMemberAttribute",
    ];

    private const string NewtonsoftIgnoreAttribute = "Newtonsoft.Json.JsonIgnoreAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            var reached = new Lazy<Dictionary<INamedTypeSymbol, INamedTypeSymbol>>(
                () => FindSerializedTypes(start.Compilation));

            start.RegisterSymbolAction(symbolContext => AnalyzeType(symbolContext, reached.Value), SymbolKind.NamedType);
        });
    }

    private static void AnalyzeType(SymbolAnalysisContext context, Dictionary<INamedTypeSymbol, INamedTypeSymbol> reached)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (!reached.TryGetValue(type, out var config))
            return;

        Report(context, type, type, config);

        foreach (var member in type.GetMembers())
        {
            if (member.IsImplicitlyDeclared)
                continue;

            Report(context, member, type, config);
        }
    }

    private static void Report(SymbolAnalysisContext context, ISymbol symbol, INamedTypeSymbol type, INamedTypeSymbol config)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (!IsSystemTextJson(attribute.AttributeClass))
                continue;

            var syntax = attribute.ApplicationSyntaxReference;

            if (syntax == null)
                continue;

            var location = Location.Create(syntax.SyntaxTree, syntax.Span);

            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                location,
                attribute.AttributeClass!.Name,
                type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                config.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }

    private static bool IsSystemTextJson(INamedTypeSymbol? attributeClass)
    {
        var ns = attributeClass?.ContainingNamespace?.ToDisplayString();

        return ns != null && (ns == SystemTextJsonNamespace || ns.StartsWith(SystemTextJsonNamespace + ".", StringComparison.Ordinal));
    }

    // Maps every source type a configuration serializes, by its original definition, to the first configuration that
    // reaches it.
    internal static Dictionary<INamedTypeSymbol, INamedTypeSymbol> FindSerializedTypes(Compilation compilation)
    {
        var sourceTypes = new List<INamedTypeSymbol>();
        CollectSourceTypes(compilation.Assembly.GlobalNamespace, sourceTypes);

        var reached = new Dictionary<INamedTypeSymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var config in sourceTypes.Where(IsConfiguration))
        {
            var pending = new Stack<ITypeSymbol>();
            pending.Push(config);

            while (pending.Count > 0 && visited.Count < MaxVisitedTypes)
            {
                var current = pending.Pop();

                if (!visited.Add(current))
                    continue;

                Expand(current, config, sourceTypes, reached, pending);
            }
        }

        return reached;
    }

    private static void Expand(
        ITypeSymbol type,
        INamedTypeSymbol config,
        List<INamedTypeSymbol> sourceTypes,
        Dictionary<INamedTypeSymbol, INamedTypeSymbol> reached,
        Stack<ITypeSymbol> pending)
    {
        if (type is IArrayTypeSymbol array)
        {
            pending.Push(array.ElementType);
            return;
        }

        if (type is not INamedTypeSymbol named)
            return;

        // Collections, dictionaries, nullables and user generics all carry their element types as type arguments.
        foreach (var argument in named.TypeArguments)
            pending.Push(argument);

        if (!IsInSource(named))
            return;

        var definition = named.OriginalDefinition;

        if (!reached.ContainsKey(definition))
            reached.Add(definition, config);

        // Newtonsoft writes the runtime type, so a derived class or an implementation stored in this slot is serialized
        // by its own contract.
        foreach (var candidate in sourceTypes)
        {
            if (!SymbolEqualityComparer.Default.Equals(candidate, definition) && DerivesFrom(candidate, definition))
                pending.Push(candidate);
        }

        for (var current = named; current != null && IsInSource(current); current = current.BaseType)
        {
            if (!ReferenceEquals(current, named))
                pending.Push(current);

            foreach (var member in current.GetMembers())
            {
                var memberType = SerializedMemberType(member);

                if (memberType != null)
                    pending.Push(memberType);
            }
        }
    }

    // The type Newtonsoft writes for this member, or null when the member is not serialized.
    private static ITypeSymbol? SerializedMemberType(ISymbol member)
    {
        if (member.IsStatic || member.IsImplicitlyDeclared)
            return null;

        ITypeSymbol? memberType;
        bool isPublic;

        switch (member)
        {
            case IPropertySymbol property when !property.IsIndexer:
                memberType = property.Type;
                isPublic = property.DeclaredAccessibility == Accessibility.Public && property.GetMethod != null;
                break;
            case IFieldSymbol field when field.AssociatedSymbol == null:
                memberType = field.Type;
                isPublic = field.DeclaredAccessibility == Accessibility.Public;
                break;
            default:
                return null;
        }

        var attributes = member.GetAttributes();

        if (attributes.Any(a => a.AttributeClass?.ToDisplayString() == NewtonsoftIgnoreAttribute))
            return null;

        if (isPublic || attributes.Any(a => NewtonsoftOptInAttributes.Contains(a.AttributeClass?.ToDisplayString())))
            return memberType;

        return null;
    }

    private static bool IsConfiguration(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current != null; current = current.BaseType)
        {
            if (current.Name == ConfigBaseName && current.ContainingNamespace?.ToDisplayString() == ConfigBaseNamespace)
                return true;
        }

        return false;
    }

    private static bool DerivesFrom(INamedTypeSymbol candidate, INamedTypeSymbol definition)
    {
        if (definition.TypeKind == TypeKind.Interface)
            return candidate.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, definition));

        for (var current = candidate.BaseType; current != null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, definition))
                return true;
        }

        return false;
    }

    private static bool IsInSource(INamedTypeSymbol type)
        => type.DeclaringSyntaxReferences.Length > 0;

    private static void CollectSourceTypes(INamespaceOrTypeSymbol container, List<INamedTypeSymbol> types)
    {
        foreach (var member in container.GetMembers())
        {
            if (member is INamespaceSymbol ns)
            {
                CollectSourceTypes(ns, types);
            }
            else if (member is INamedTypeSymbol type && IsInSource(type))
            {
                types.Add(type);
                CollectSourceTypes(type, types);
            }
        }
    }
}
