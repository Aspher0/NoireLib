#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Immutable;

namespace NoireLib.SourceGenerators;

/// <summary>Reports a module constructor whose body runs after the base constructor has already activated the module.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class NoireModuleConstructorAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NoireLib_008";

    private const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        title: "Module constructor body runs after activation",
        messageFormat: "The body of this '{0}' constructor runs after the base constructor has already run OnActivated. Move the setup to InitializeModule, or pass active: false to the base constructor and activate at the end of this body.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "The module base constructor runs InitializeModule and then OnActivated before a derived constructor " +
            "body runs, so state assigned in that body is not there yet when OnActivated reads it.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeConstructor, OperationKind.ConstructorBody);
    }

    private static void AnalyzeConstructor(OperationAnalysisContext context)
    {
        var body = (IConstructorBodyOperation)context.Operation;

        if (body.Syntax is not ConstructorDeclarationSyntax declaration)
            return;

        if (declaration.Modifiers.Any(SyntaxKind.StaticKeyword))
            return;

        if ((declaration.Body == null || declaration.Body.Statements.Count == 0) && declaration.ExpressionBody == null)
            return;

        if (context.ContainingSymbol is not IMethodSymbol constructor || !InheritsFromModuleBase(constructor.ContainingType))
            return;

        // The initializer, implicit or not, is the base or this call; a this call is judged at its own target.
        if (body.Initializer is not IExpressionStatementOperation { Operation: IInvocationOperation invocation })
            return;

        if (SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingType, constructor.ContainingType))
            return;

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter is not { Name: "active", Type.SpecialType: SpecialType.System_Boolean })
                continue;

            if (argument.Value.ConstantValue is { HasValue: true, Value: false })
                return;

            context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.Identifier.GetLocation(), constructor.ContainingType.Name));
            return;
        }
    }

    private static bool InheritsFromModuleBase(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current != null; current = current.BaseType)
        {
            if (current.OriginalDefinition.Name == "NoireModuleBase" &&
                current.ContainingNamespace?.ToDisplayString() == "NoireLib.Core.Modules")
            {
                return true;
            }
        }

        return false;
    }
}
