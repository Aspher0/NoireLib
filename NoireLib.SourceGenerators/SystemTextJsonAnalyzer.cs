#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Linq;

namespace NoireLib.SourceGenerators;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class SystemTextJsonAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NoireLib_002";
    private const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        title: "Use Newtonsoft.Json instead of System.Text.Json",
        messageFormat: "Please use Newtonsoft.Json instead of System.Text.Json to avoid issues with serialization",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "System.Text.Json can cause serialization issues in Dalamud plugins. Use Newtonsoft.Json instead for compatibility.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeUsingDirective, SyntaxKind.UsingDirective);
        context.RegisterSyntaxNodeAction(AnalyzeQualifiedName, SyntaxKind.QualifiedName);
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
        context.RegisterSyntaxNodeAction(AnalyzeAttribute, SyntaxKind.Attribute);
    }

    private void AnalyzeUsingDirective(SyntaxNodeAnalysisContext context)
    {
        var usingDirective = (UsingDirectiveSyntax)context.Node;
        var nameText = usingDirective.Name?.ToString();

        if (nameText != null &&
            (nameText == "System.Text.Json" ||
             nameText.StartsWith("System.Text.Json.")))
        {
            var diagnostic = Diagnostic.Create(Rule, usingDirective.GetLocation());
            context.ReportDiagnostic(diagnostic);
        }
    }

    private void AnalyzeQualifiedName(SyntaxNodeAnalysisContext context)
    {
        var qualifiedName = (QualifiedNameSyntax)context.Node;
        var fullName = qualifiedName.ToString();

        if (fullName.StartsWith("System.Text.Json"))
        {
            if (qualifiedName.Ancestors().OfType<UsingDirectiveSyntax>().Any())
                return;

            var diagnostic = Diagnostic.Create(Rule, qualifiedName.GetLocation());
            context.ReportDiagnostic(diagnostic);
        }
    }

    private void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(memberAccess);
        var symbol = symbolInfo.Symbol;

        if (symbol == null)
            return;

        var containingNamespace = symbol.ContainingNamespace?.ToDisplayString();
        if (containingNamespace != null &&
            (containingNamespace == "System.Text.Json" ||
             containingNamespace.StartsWith("System.Text.Json.")))
        {
            var diagnostic = Diagnostic.Create(Rule, memberAccess.GetLocation());
            context.ReportDiagnostic(diagnostic);
        }
    }

    private void AnalyzeAttribute(SyntaxNodeAnalysisContext context)
    {
        var attribute = (AttributeSyntax)context.Node;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(attribute);
        var symbol = symbolInfo.Symbol;

        if (symbol == null)
            return;

        var attributeType = symbol is IMethodSymbol methodSymbol
            ? methodSymbol.ContainingType
            : symbol as INamedTypeSymbol;

        if (attributeType == null)
            return;

        var containingNamespace = attributeType.ContainingNamespace?.ToDisplayString();
        if (containingNamespace != null &&
            (containingNamespace == "System.Text.Json" ||
             containingNamespace.StartsWith("System.Text.Json.")))
        {
            var diagnostic = Diagnostic.Create(Rule, attribute.GetLocation());
            context.ReportDiagnostic(diagnostic);
        }
    }
}
