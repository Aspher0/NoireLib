#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Immutable;

namespace NoireLib.SourceGenerators;

// Reports a NoireUI surface acquiring a draw list outside the UiDraw gate: its cost would land in its caller's profiler
// scope. An error, not a warning: an unmeasured surface must not ship. Only NoireLib/UI/ is constrained.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class UiDrawListAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NoireLib_003";

    private const string Category = "Usage";

    // The gate itself. Only NoireShapes.DrawList and UiContext.WindowDrawList are exempt, never their whole files.
    private static readonly (string File, string? Member)[] Exemptions =
    [
        ("UiDraw.cs", null),
        ("NoireShapes.cs", "DrawList"),
        ("UiContext.cs", "WindowDrawList"),
    ];

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        title: "Obtain draw lists through UiDraw rather than from ImGui directly",
        messageFormat: "'{0}' bypasses the UiDraw gate, so this drawing opens no profiler scope and its cost is charged to its caller. Use UiDraw.Begin(), BeginWindow(), BeginForeground() or BeginBackground()",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Every drawing surface inside NoireLib/UI must obtain its draw list from the UiDraw gate, which " +
            "opens a profiler scope at the same time and names it after the calling type. A surface that " +
            "acquires a list directly is invisible to the profiler, and its cost reads as its caller's. " +
            "Reported as an error rather than a warning because a warning does not stop a build, and the " +
            "guarantee here is that an unmeasured surface cannot ship rather than that it is discouraged.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;
        var method = memberAccess.Name.Identifier.Text;

        if (!IsDrawListAccessor(method))
            return;

        var path = context.Node.SyntaxTree.FilePath;

        if (!IsInsideNoireUi(path))
            return;

        // Resolved through the model: a fully qualified or aliased call is caught like a bare ImGui one.
        if (context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol is not IMethodSymbol symbol)
            return;

        if (symbol.ContainingType?.Name != "ImGui")
            return;

        if (IsExempt(path, memberAccess))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, memberAccess.GetLocation(), $"ImGui.{method}"));
    }

    private static bool IsDrawListAccessor(string method)
        => method == "GetWindowDrawList"
        || method == "GetForegroundDrawList"
        || method == "GetBackgroundDrawList";

    // Matched on the path: a consumer plugin may declare types in NoireLib.UI and must not inherit this rule.
    private static bool IsInsideNoireUi(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        var normalized = path.Replace('\\', '/');

        return normalized.IndexOf("/NoireLib/UI/", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsExempt(string path, SyntaxNode node)
    {
        var normalized = path.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        var file = slash < 0 ? normalized : normalized.Substring(slash + 1);

        foreach (var (exemptFile, exemptMember) in Exemptions)
        {
            if (!string.Equals(file, exemptFile, StringComparison.OrdinalIgnoreCase))
                continue;

            if (exemptMember == null)
                return true;

            if (string.Equals(EnclosingMemberName(node), exemptMember, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string? EnclosingMemberName(SyntaxNode node)
    {
        for (var current = node.Parent; current != null; current = current.Parent)
        {
            switch (current)
            {
                case PropertyDeclarationSyntax property:
                    return property.Identifier.Text;

                case MethodDeclarationSyntax method:
                    return method.Identifier.Text;

                case TypeDeclarationSyntax:
                    return null;
            }
        }

        return null;
    }
}
