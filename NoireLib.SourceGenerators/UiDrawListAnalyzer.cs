#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Immutable;

namespace NoireLib.SourceGenerators;

/// <summary>
/// Reports a NoireUI surface that acquires an ImGui draw list directly instead of through the
/// <c>UiDraw</c> gate, which is what makes instrumentation a property of the code rather than a
/// thing to remember.
/// </summary>
/// <remarks>
/// A surface that reaches for its own list opens no profiler scope, so its cost lands in whichever
/// scope encloses it and reads as a caller's expense rather than its own.
/// <para>
/// This rule is an Error where the other analyzers in this project are Warnings, and the departure
/// is deliberate. A warning does not stop a build, so it cannot deliver the guarantee this rule
/// exists for: that an unmeasured surface is not merely discouraged but impossible to ship.
/// </para>
/// <para>
/// Scoped to <c>NoireLib/UI/</c>. Plugin code outside the library is unaffected: consumers may call
/// ImGui however they like, and this constrains the library only.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class UiDrawListAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NoireLib_003";

    private const string Category = "Usage";

    /// <summary>
    /// The gate's own implementation, which has to make these calls so that nothing else does.
    /// </summary>
    /// <remarks>
    /// <c>NoireShapes.DrawList</c> is exempt as the chokepoint the gate resolves the window list through, but only
    /// that one member: the rest of <c>NoireShapes.cs</c> is a drawing surface like any other, and exempting the whole
    /// file would let new ungated drawing compile there in silence.
    /// </remarks>
    private static readonly (string File, string? Member)[] Exemptions =
    [
        ("UiDraw.cs", null),
        ("NoireShapes.cs", "DrawList"),
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

        // Resolved through the model rather than matched on the receiver's spelling, so a fully qualified call or one
        // through a using alias is caught the same as a bare ImGui.
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

    /// <summary>
    /// Whether a file belongs to the library's UI, which is the only code this rule constrains.
    /// </summary>
    /// <remarks>
    /// Matched on the path rather than on the namespace, because a consumer plugin is free to declare
    /// types in <c>NoireLib.UI</c> and must not inherit the library's own constraint.
    /// </remarks>
    private static bool IsInsideNoireUi(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        var normalized = path.Replace('\\', '/');

        return normalized.IndexOf("/NoireLib/UI/", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Whether a call sits in the gate's own implementation. See <see cref="Exemptions"/>.
    /// </summary>
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

    /// <summary>
    /// The name of the member a call sits in, or <see langword="null"/> when it sits outside one.
    /// </summary>
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
