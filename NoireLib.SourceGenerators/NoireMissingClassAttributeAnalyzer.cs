using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace NoireLib.SourceGenerators;

/// <summary>Reports a static class with IPC or NoireRemote members but no class attribute. Nothing is registered.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class NoireMissingClassAttributeAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NoireLib_006";

    private const string Category = "Usage";

    private const string IpcMember = "NoireLib.IPC.NoireIpcAttribute";
    private const string IpcClass = "NoireLib.IPC.NoireIpcClassAttribute";
    private const string RemoteMember = "NoireLib.Remote.NoireRemoteAttribute";
    private const string RemoteClass = "NoireLib.Remote.NoireRemoteClassAttribute";

    private static readonly string[] RegistrationMethods = ["RegisterType", "Initialize", "Publish", "PublishType"];

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        title: "An annotated static class needs its class attribute",
        messageFormat: "'{0}' declares [{1}] members but carries no [{2}]. Nothing is registered unless the type is passed to RegisterType or Publish by hand",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "Automatic registration only visits types carrying the class attribute. A static type with annotated " +
            "members and no class attribute registers nothing, without a log or an error.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            var registered = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

            start.RegisterOperationAction(
                operation =>
                {
                    var invocation = (IInvocationOperation)operation.Operation;

                    if (!RegistrationMethods.Contains(invocation.TargetMethod.Name))
                        return;

                    foreach (var typeArgument in invocation.TargetMethod.TypeArguments.OfType<INamedTypeSymbol>())
                        registered.Add(typeArgument);

                    foreach (var argument in invocation.Arguments)
                    {
                        if (argument.Value is ITypeOfOperation typeOf && typeOf.TypeOperand is INamedTypeSymbol named)
                            registered.Add(named);
                    }
                },
                OperationKind.Invocation);

            var reports = new List<(INamedTypeSymbol Type, string Member, string Class, Location Location)>();

            start.RegisterSymbolAction(
                symbolContext =>
                {
                    var type = (INamedTypeSymbol)symbolContext.Symbol;

                    if (!type.IsStatic || type.TypeKind != TypeKind.Class)
                        return;

                    Check(type, IpcMember, IpcClass, "NoireIpc", "NoireIpcClass", reports);
                    Check(type, RemoteMember, RemoteClass, "NoireRemote", "NoireRemoteClass", reports);
                },
                SymbolKind.NamedType);

            start.RegisterCompilationEndAction(end =>
            {
                foreach (var report in reports)
                {
                    if (registered.Contains(report.Type))
                        continue;

                    end.ReportDiagnostic(Diagnostic.Create(Rule, report.Location, report.Type.Name, report.Member, report.Class));
                }
            });
        });
    }

    private static void Check(
        INamedTypeSymbol type,
        string memberAttribute,
        string classAttribute,
        string memberName,
        string className,
        List<(INamedTypeSymbol, string, string, Location)> reports)
    {
        if (NoireRemoteMemberAnalyzer.FindAttribute(type, classAttribute) != null)
            return;

        var annotated = type.GetMembers().Any(member => NoireRemoteMemberAnalyzer.FindAttribute(member, memberAttribute) != null);

        if (!annotated)
            return;

        var location = type.Locations.FirstOrDefault();

        if (location != null)
            reports.Add((type, memberName, className, location));
    }
}
