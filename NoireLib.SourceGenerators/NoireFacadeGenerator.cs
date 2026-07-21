#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace NoireLib.SourceGenerators;

/// <summary>
/// Emits the grouped NoireUI path: for every type marked with NoireFacadeAttribute, a nested static class on
/// NoireUI carrying a forward, with copied documentation, for each of that type's public static members.
/// </summary>
/// <remarks>
/// Generated rather than hand-written for two reasons, both of which produce failures that still compile. C# bakes an
/// optional parameter's default into the caller, so a forward whose default fell behind its target would silently
/// produce a different result; and the compiler does not expand inherited-documentation tags into the XML file the
/// package ships, which is a consumer's whole IntelliSense channel, so the documentation has to be copied in full
/// rather than referenced. See docs/adr/0003-noireui-facade-is-generated.md.
/// </remarks>
[Generator]
public sealed class NoireFacadeGenerator : IIncrementalGenerator
{
    private const string FacadeAttributeName = "NoireLib.UI.NoireFacadeAttribute";
    private const string FactoryAttributeName = "NoireLib.UI.NoireFacadeFactoryAttribute";
    private const string LibraryPrefix = "Noire";
    private const string RootType = "NoireUI";
    private const string RootNamespace = "NoireLib.UI";

    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var surfaces = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                FacadeAttributeName,
                static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax,
                static (ctx, _) => Render(ctx))
            .Where(static s => s is not null);

        var widgets = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                FactoryAttributeName,
                static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax,
                static (ctx, _) => RenderFactory(ctx))
            .Where(static s => s is not null);

        context.RegisterSourceOutput(surfaces, static (spc, surface) => spc.AddSource(surface!.HintName, SourceText.From(surface.Source, Encoding.UTF8)));
        context.RegisterSourceOutput(widgets, static (spc, widget) => spc.AddSource(widget!.HintName, SourceText.From(widget.Source, Encoding.UTF8)));
    }

    private static RenderedSurface? Render(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not INamedTypeSymbol surface)
            return null;

        var groupedName = GroupedName(surface, context.Attributes);
        var target = surface.ToDisplayString(TypeFormat);
        var members = ForwardableMembers(surface)
            .Select(member => RenderMember(member, target, context.SemanticModel.Compilation))
            .Where(static text => text != null)
            .OrderBy(static text => text, StringComparer.Ordinal)
            .ToList();

        if (members.Count == 0)
            return null;

        var builder = OpenRootFile();
        builder.Append(NoireFacadeDocumentation.Render(
            surface,
            ImmutableArray<IParameterSymbol>.Empty,
            ImmutableArray<ITypeParameterSymbol>.Empty,
            context.SemanticModel.Compilation,
            indent: "    "));
        builder.Append("    public static class ").AppendLine(groupedName);
        builder.AppendLine("    {");

        for (var i = 0; i < members.Count; i++)
        {
            if (i > 0)
                builder.AppendLine();

            builder.Append(members[i]);
        }

        builder.AppendLine("    }");
        builder.AppendLine("}");

        return new RenderedSurface($"{RootType}.{groupedName}.g.cs", builder.ToString());
    }

    /// <summary>
    /// Emits a creation method on the root for each public constructor of a marked widget, so that a widget an author
    /// builds and drives is browsable beside the surfaces they call statically.
    /// </summary>
    private static RenderedSurface? RenderFactory(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not INamedTypeSymbol widget)
            return null;

        var name = GroupedName(widget, context.Attributes);
        var type = widget.ToDisplayString(TypeFormat);

        // The implicit parameterless constructor counts: a widget with no constructor of its own is still built with
        // new, so leaving it out would make the creation method the one door that could not open.
        var constructors = widget.InstanceConstructors
            .Where(static constructor => constructor.DeclaredAccessibility == Accessibility.Public)
            .Select(constructor => RenderCreationMethod(constructor, widget, name, type, context.SemanticModel.Compilation))
            .OrderBy(static text => text, StringComparer.Ordinal)
            .ToList();

        if (constructors.Count == 0)
            return null;

        var builder = OpenRootFile();

        for (var i = 0; i < constructors.Count; i++)
        {
            if (i > 0)
                builder.AppendLine();

            builder.Append(constructors[i]);
        }

        builder.AppendLine("}");

        return new RenderedSurface($"{RootType}.{name}.Create.g.cs", builder.ToString());
    }

    /// <summary>
    /// Opens a generated file on the root partial class, which both emitters extend.
    /// </summary>
    private static StringBuilder OpenRootFile()
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine();
        builder.Append("namespace ").Append(RootNamespace).AppendLine(";");
        builder.AppendLine();
        builder.Append("public static partial class ").AppendLine(RootType);
        builder.AppendLine("{");

        return builder;
    }

    private static string RenderCreationMethod(IMethodSymbol constructor, INamedTypeSymbol widget, string name, string type, Compilation compilation)
    {
        var builder = new StringBuilder();

        // A constructor documents the arguments; the type documents what the thing is. An author browsing the root
        // wants both, and the constructor alone is the poorer of the two when it says nothing.
        builder.Append(NoireFacadeDocumentation.Render(
            NoireFacadeDocumentation.Exists(constructor) ? constructor : (ISymbol)widget,
            constructor.Parameters,
            widget.TypeParameters,
            compilation,
            indent: "    "));

        builder.Append("    public static ").Append(type).Append(' ').Append(Escaped(name));
        builder.Append(TypeParameterList(widget.TypeParameters));
        builder.Append('(').Append(ParameterList(constructor)).Append(')');
        builder.Append(ConstraintClauses(widget.TypeParameters, indent: "        "));
        builder.AppendLine();
        builder.Append("        => new ").Append(type).Append('(').Append(ArgumentList(constructor)).AppendLine(");");

        return builder.ToString();
    }

    /// <summary>
    /// The grouped name of a surface: the explicit name on the marker, or the surface's own with the library prefix
    /// removed.
    /// </summary>
    /// <remarks>
    /// The override exists for the surfaces whose mechanical name would repeat the root and stutter at the call site.
    /// Existing plurality is mirrored as it stands: a plural name marks a family of interchangeable widgets and a
    /// singular one marks a single subsystem, and normalizing that away would give a surface two names.
    /// </remarks>
    private static string GroupedName(INamedTypeSymbol surface, IReadOnlyList<AttributeData> markers)
    {
        foreach (var marker in markers)
        {
            if (marker.ConstructorArguments.Length > 0 && marker.ConstructorArguments[0].Value is string name && name.Length > 0)
                return name;
        }

        return surface.Name.StartsWith(LibraryPrefix, StringComparison.Ordinal) && surface.Name.Length > LibraryPrefix.Length
            ? surface.Name.Substring(LibraryPrefix.Length)
            : surface.Name;
    }

    private static IEnumerable<ISymbol> ForwardableMembers(INamedTypeSymbol surface)
    {
        foreach (var member in surface.GetMembers())
        {
            if (!member.IsStatic || member.DeclaredAccessibility != Accessibility.Public || member.IsImplicitlyDeclared)
                continue;

            switch (member)
            {
                case IMethodSymbol method when method.MethodKind == MethodKind.Ordinary:
                    yield return method;
                    break;

                case IPropertySymbol { IsIndexer: false } property:
                    yield return property;
                    break;

                // Only constants carry forward as constants. A mutable static field cannot be forwarded without
                // changing what a consumer can do with it, and the surfaces declare none.
                case IFieldSymbol { IsConst: true } field:
                    yield return field;
                    break;
            }
        }
    }

    private static string? RenderMember(ISymbol member, string target, Compilation compilation)
        => member switch
        {
            IMethodSymbol method => RenderMethod(method, target, compilation),
            IPropertySymbol property => RenderProperty(property, target, compilation),
            IFieldSymbol field => RenderConstant(field, target, compilation),
            _ => null,
        };

    private static string RenderMethod(IMethodSymbol method, string target, Compilation compilation)
    {
        var builder = new StringBuilder();
        builder.Append(NoireFacadeDocumentation.Render(method, method.Parameters, method.TypeParameters, compilation, indent: "        "));

        builder.Append("        public static ");

        if (RequiresUnsafe(method))
            builder.Append("unsafe ");

        builder.Append(RefPrefix(method.RefKind));
        builder.Append(method.ReturnsVoid ? "void" : method.ReturnType.ToDisplayString(TypeFormat));
        builder.Append(' ').Append(Escaped(method.Name));
        builder.Append(TypeParameterList(method.TypeParameters));
        builder.Append('(').Append(ParameterList(method)).Append(')');
        builder.Append(ConstraintClauses(method.TypeParameters, indent: "            "));
        builder.AppendLine();

        builder.Append("            => ");

        if (method.RefKind != RefKind.None)
            builder.Append("ref ");

        builder.Append(target).Append('.').Append(Escaped(method.Name));
        builder.Append(TypeParameterList(method.TypeParameters));
        builder.Append('(').Append(ArgumentList(method)).AppendLine(");");

        return builder.ToString();
    }

    private static string RenderProperty(IPropertySymbol property, string target, Compilation compilation)
    {
        var builder = new StringBuilder();
        builder.Append(NoireFacadeDocumentation.Render(property, property.Parameters, ImmutableArray<ITypeParameterSymbol>.Empty, compilation, indent: "        "));

        var readable = property.GetMethod is { DeclaredAccessibility: Accessibility.Public };
        var writable = property.SetMethod is { DeclaredAccessibility: Accessibility.Public };
        var type = property.Type.ToDisplayString(TypeFormat);
        var access = $"{target}.{Escaped(property.Name)}";

        builder.Append("        public static ").Append(type).Append(' ').Append(Escaped(property.Name));

        if (readable && !writable)
        {
            builder.Append(" => ").Append(access).AppendLine(";");
            return builder.ToString();
        }

        builder.AppendLine();
        builder.AppendLine("        {");

        if (readable)
            builder.Append("            get => ").Append(access).AppendLine(";");

        if (writable)
            builder.Append("            set => ").Append(access).AppendLine(" = value;");

        builder.AppendLine("        }");
        return builder.ToString();
    }

    private static string RenderConstant(IFieldSymbol field, string target, Compilation compilation)
    {
        var builder = new StringBuilder();
        builder.Append(NoireFacadeDocumentation.Render(field, ImmutableArray<IParameterSymbol>.Empty, ImmutableArray<ITypeParameterSymbol>.Empty, compilation, indent: "        "));
        builder
            .Append("        public const ").Append(field.Type.ToDisplayString(TypeFormat))
            .Append(' ').Append(Escaped(field.Name))
            .Append(" = ").Append(target).Append('.').Append(Escaped(field.Name)).AppendLine(";");

        return builder.ToString();
    }

    private static bool RequiresUnsafe(IMethodSymbol method)
        => ContainsPointer(method.ReturnType) || method.Parameters.Any(static p => ContainsPointer(p.Type));

    private static bool ContainsPointer(ITypeSymbol type)
        => type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer
            || (type is INamedTypeSymbol named && named.TypeArguments.Any(ContainsPointer));

    private static string RefPrefix(RefKind kind) => kind switch
    {
        RefKind.Ref => "ref ",
        RefKind.RefReadOnly => "ref readonly ",
        _ => string.Empty,
    };

    private static string TypeParameterList(ImmutableArray<ITypeParameterSymbol> parameters)
        => parameters.Length == 0
            ? string.Empty
            : "<" + string.Join(", ", parameters.Select(static p => Escaped(p.Name))) + ">";

    private static string ParameterList(IMethodSymbol method)
        => string.Join(", ", method.Parameters.Select(RenderParameter));

    private static string RenderParameter(IParameterSymbol parameter)
    {
        var builder = new StringBuilder();

        // Caller-info attributes resolve against whoever declares them, so a forward that dropped them would hand the
        // target the facade's own call site instead of the consumer's.
        foreach (var attribute in parameter.GetAttributes())
        {
            if (attribute.AttributeClass is { } attributeClass && IsCallerInfo(attributeClass))
                builder.Append('[').Append(attributeClass.ToDisplayString(TypeFormat)).Append("] ");
        }

        if (parameter.IsParams)
            builder.Append("params ");

        builder.Append(ParameterRefPrefix(parameter));
        builder.Append(parameter.Type.ToDisplayString(TypeFormat)).Append(' ').Append(Escaped(parameter.Name));

        var defaultValue = DefaultValue(parameter);
        if (defaultValue != null)
            builder.Append(" = ").Append(defaultValue);

        return builder.ToString();
    }

    private static bool IsCallerInfo(INamedTypeSymbol attributeClass)
        => attributeClass.ContainingNamespace?.ToDisplayString() == "System.Runtime.CompilerServices"
            && attributeClass.Name is "CallerMemberNameAttribute"
                or "CallerFilePathAttribute"
                or "CallerLineNumberAttribute"
                or "CallerArgumentExpressionAttribute";

    private static string ParameterRefPrefix(IParameterSymbol parameter) => parameter.RefKind switch
    {
        RefKind.Ref => "ref ",
        RefKind.Out => "out ",
        RefKind.In => "in ",
        RefKind.RefReadOnlyParameter => "ref readonly ",
        _ => string.Empty,
    };

    private static string ArgumentList(IMethodSymbol method)
        => string.Join(", ", method.Parameters.Select(static p => ArgumentRefPrefix(p.RefKind) + Escaped(p.Name)));

    private static string ArgumentRefPrefix(RefKind kind) => kind switch
    {
        RefKind.Ref => "ref ",
        RefKind.Out => "out ",
        RefKind.In => "in ",
        RefKind.RefReadOnlyParameter => "in ",
        _ => string.Empty,
    };

    private static string ConstraintClauses(ImmutableArray<ITypeParameterSymbol> parameters, string indent)
    {
        if (parameters.Length == 0)
            return string.Empty;

        var builder = new StringBuilder();

        foreach (var parameter in parameters)
        {
            var constraints = new List<string>();

            if (parameter.HasReferenceTypeConstraint)
                constraints.Add(parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
            else if (parameter.HasUnmanagedTypeConstraint)
                constraints.Add("unmanaged");
            else if (parameter.HasValueTypeConstraint)
                constraints.Add("struct");
            else if (parameter.HasNotNullConstraint)
                constraints.Add("notnull");

            constraints.AddRange(parameter.ConstraintTypes.Select(t => t.ToDisplayString(TypeFormat)));

            if (parameter.HasConstructorConstraint)
                constraints.Add("new()");

            if (constraints.Count == 0)
                continue;

            builder
                .AppendLine()
                .Append(indent).Append("where ").Append(Escaped(parameter.Name)).Append(" : ")
                .Append(string.Join(", ", constraints));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Renders a parameter's default as a constant expression that does not depend on what is in scope where it lands.
    /// </summary>
    private static string? DefaultValue(IParameterSymbol parameter)
    {
        if (!parameter.HasExplicitDefaultValue)
            return null;

        var value = parameter.ExplicitDefaultValue;
        var type = parameter.Type;

        if (value == null)
            return type.IsReferenceType || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T ? "null" : "default";

        // An enum default is written as a cast over its numeric value rather than as a member name, so a combination of
        // flags with no name of its own renders as correctly as a single named member does.
        var underlying = type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T && type is INamedTypeSymbol nullable
            ? nullable.TypeArguments[0]
            : type;

        if (underlying.TypeKind == TypeKind.Enum)
            return $"({underlying.ToDisplayString(TypeFormat)}){Literal(value)}";

        return Literal(value);
    }

    private static string Literal(object value) => value switch
    {
        bool b => b ? "true" : "false",
        string s => SymbolDisplay.FormatLiteral(s, quote: true),
        char c => SymbolDisplay.FormatLiteral(c, quote: true),
        float f => f.ToString("R", CultureInfo.InvariantCulture) + "f",
        double d => d.ToString("R", CultureInfo.InvariantCulture) + "d",
        decimal m => m.ToString(CultureInfo.InvariantCulture) + "m",
        long l => l.ToString(CultureInfo.InvariantCulture) + "L",
        ulong u => u.ToString(CultureInfo.InvariantCulture) + "UL",
        uint u => u.ToString(CultureInfo.InvariantCulture) + "U",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private static string Escaped(string name)
        => SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None && SyntaxFacts.GetContextualKeywordKind(name) == SyntaxKind.None
            ? name
            : "@" + name;

    /// <summary>
    /// One surface's finished output. Compared by its text so an edit elsewhere in the library does not re-emit it.
    /// </summary>
    private sealed class RenderedSurface : IEquatable<RenderedSurface>
    {
        public RenderedSurface(string hintName, string source)
        {
            HintName = hintName;
            Source = source;
        }

        public string HintName { get; }

        public string Source { get; }

        public bool Equals(RenderedSurface? other)
            => other != null && HintName == other.HintName && Source == other.Source;

        public override bool Equals(object? obj) => Equals(obj as RenderedSurface);

        public override int GetHashCode() => unchecked((HintName.GetHashCode() * 397) ^ Source.GetHashCode());
    }
}
