using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NoireLib.SourceGenerators;

/// <summary>Reports an annotated property that can neither publish nor consume.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class NoirePropertyMemberAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NoireLib_007";

    private const string Category = "Usage";

    private const string IpcMember = "NoireLib.IPC.NoireIpcAttribute";
    private const string RemoteMember = "NoireLib.Remote.NoireRemoteAttribute";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        title: "An annotated property publishes its value and needs a getter",
        messageFormat: "'{0}' cannot be published: a property whose type is not a delegate or a NoireLib wrapper publishes its value and needs a getter",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A property whose type is a delegate, NoireIpcConsumer, NoireIpcEventConsumer or NoireRemoteConsumer " +
            "consumes someone else's member and is filled by the binder. Any other property publishes what its " +
            "getter reads. A property with no getter says nothing either way.");

    private static readonly string[] ConsumerTypeNames =
    [
        "NoireIpcConsumer", "NoireIpcEventConsumer", "NoireIpcEvent", "NoireRemoteConsumer",
    ];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSymbolAction(Analyze, SymbolKind.Property);
    }

    private static void Analyze(SymbolAnalysisContext context)
    {
        var property = (IPropertySymbol)context.Symbol;

        if (NoireRemoteMemberAnalyzer.FindAttribute(property, IpcMember) == null
            && NoireRemoteMemberAnalyzer.FindAttribute(property, RemoteMember) == null)
        {
            return;
        }

        if (IsConsumerType(property.Type))
            return;

        if (property.GetMethod != null)
            return;

        var location = property.Locations.FirstOrDefault();

        if (location != null)
            context.ReportDiagnostic(Diagnostic.Create(Rule, location, property.Name));
    }

    private static bool IsConsumerType(ITypeSymbol type)
    {
        var bare = type is INamedTypeSymbol named && named.IsGenericType && named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T
            ? named.TypeArguments[0]
            : type;

        if (bare.TypeKind == TypeKind.Delegate)
            return true;

        return bare is INamedTypeSymbol wrapper && ConsumerTypeNames.Contains(wrapper.Name);
    }
}
