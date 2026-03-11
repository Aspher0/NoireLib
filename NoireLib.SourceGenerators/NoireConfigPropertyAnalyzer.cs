#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

namespace NoireLib.SourceGenerators;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class NoireConfigPropertyAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NoireLib_001";
    private const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        title: "Property must have both getter and setter",
        messageFormat: "Property '{0}' in NoireConfigBase-derived class must have both {{ get; set; }} accessors in order to access the configuration system correctly",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Properties in classes inheriting from NoireConfigBase require both get and set accessors for the configuration system to function correctly.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
        context.RegisterSymbolAction(AnalyzeField, SymbolKind.Field);
    }

    private void AnalyzeProperty(SymbolAnalysisContext context)
    {
        var propertySymbol = (IPropertySymbol)context.Symbol;

        if (propertySymbol.DeclaredAccessibility != Accessibility.Public)
            return;

        if (propertySymbol.IsStatic)
            return;

        if (propertySymbol.IsOverride)
            return;

        var containingClass = propertySymbol.ContainingType;
        if (!InheritsFromNoireConfigBase(containingClass))
            return;

        bool hasGetter = propertySymbol.GetMethod != null && propertySymbol.GetMethod.DeclaredAccessibility == Accessibility.Public;
        bool hasSetter = propertySymbol.SetMethod != null && propertySymbol.SetMethod.DeclaredAccessibility == Accessibility.Public;

        if (!hasGetter || !hasSetter)
        {
            var diagnostic = Diagnostic.Create(Rule, propertySymbol.Locations[0], propertySymbol.Name);
            context.ReportDiagnostic(diagnostic);
        }
    }

    private void AnalyzeField(SymbolAnalysisContext context)
    {
        var fieldSymbol = (IFieldSymbol)context.Symbol;

        if (fieldSymbol.DeclaredAccessibility != Accessibility.Public)
            return;

        if (fieldSymbol.IsStatic)
            return;

        if (fieldSymbol.IsImplicitlyDeclared)
            return;

        var containingClass = fieldSymbol.ContainingType;
        if (!InheritsFromNoireConfigBase(containingClass))
            return;

        var diagnostic = Diagnostic.Create(Rule, fieldSymbol.Locations[0], fieldSymbol.Name);
        context.ReportDiagnostic(diagnostic);
    }

    private bool InheritsFromNoireConfigBase(INamedTypeSymbol classSymbol)
    {
        var currentType = classSymbol.BaseType;

        while (currentType != null)
        {
            if (currentType.Name == "NoireConfigBase" &&
                currentType.ContainingNamespace?.ToString() == "NoireLib.Configuration")
            {
                return true;
            }

            currentType = currentType.BaseType;
        }

        return false;
    }
}
