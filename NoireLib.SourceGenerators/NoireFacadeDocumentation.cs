#nullable enable
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace NoireLib.SourceGenerators;

/// <summary>
/// Reads a member's real documentation and re-emits it as comment lines for the grouped NoireUI path, resolving
/// inherited-documentation tags against the member they point at.
/// </summary>
/// <remarks>
/// Kept apart from the rendering of signatures because the two change for unrelated reasons: this half moves when a
/// documentation tag needs different treatment, the other when a C# construct does.<br/>
/// It exists at all because the compiler leaves inherited-documentation tags unexpanded when it writes the XML
/// documentation file, and that file is the whole of what a consumer's IntelliSense reads from a package. A forward
/// that carried such a tag through, or that pointed back at its target, would reach consumers as a blank tooltip.
/// </remarks>
internal static class NoireFacadeDocumentation
{
    /// <summary>
    /// How far a chain of inherited-documentation tags is followed before it is treated as a cycle.
    /// </summary>
    private const int MaxInheritDepth = 8;

    /// <summary>
    /// Renders a member's documentation as <c>///</c> comment lines, or nothing when it has none.
    /// </summary>
    /// <param name="symbol">The member whose documentation is read.</param>
    /// <param name="parameters">The parameters of the member the documentation is emitted on.</param>
    /// <param name="typeParameters">The type parameters of the member the documentation is emitted on.</param>
    /// <param name="compilation">The compilation inherited members are resolved against.</param>
    /// <param name="indent">The indentation the comment lines are written at.</param>
    /// <returns>The comment lines, or an empty string.</returns>
    public static string Render(
        ISymbol symbol,
        ImmutableArray<IParameterSymbol> parameters,
        ImmutableArray<ITypeParameterSymbol> typeParameters,
        Compilation compilation,
        string indent)
    {
        var member = Resolve(symbol, compilation, depth: 0);
        if (member == null)
            return string.Empty;

        Sanitize(
            member,
            new HashSet<string>(parameters.Select(static p => p.Name), StringComparer.Ordinal),
            new HashSet<string>(typeParameters.Select(static p => p.Name), StringComparer.Ordinal));

        var body = Body(member);
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;

        var builder = new StringBuilder();

        foreach (var line in body!.Split('\n'))
        {
            var text = line.TrimEnd('\r', ' ');
            builder.Append(indent).Append("///");

            if (text.Length > 0)
                builder.Append(' ').Append(text);

            builder.AppendLine();
        }

        return builder.ToString();
    }

    /// <summary>
    /// Whether a member documents itself at all, which decides whether a creation method borrows its widget's
    /// documentation instead.
    /// </summary>
    /// <param name="symbol">The member to check.</param>
    /// <returns>True when the member carries documentation of its own.</returns>
    public static bool Exists(ISymbol symbol)
        => !string.IsNullOrWhiteSpace(symbol.GetDocumentationCommentXml());

    private static XElement? Resolve(ISymbol symbol, Compilation compilation, int depth)
    {
        var xml = symbol.GetDocumentationCommentXml(expandIncludes: true);
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        XElement member;
        try
        {
            member = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        // Descendants rather than direct children: a tag nested inside a summary or a remarks is unusual but legal,
        // and one reaching the output is the blank tooltip this whole mechanism exists to prevent.
        foreach (var tag in member.Descendants("inheritdoc").ToList())
        {
            var source = depth < MaxInheritDepth ? InheritedFrom(tag, symbol, compilation) : null;
            var inherited = source == null ? null : Resolve(source, compilation, depth + 1);

            var nested = tag.Parent != member;

            // Whether or not there was anything to inherit, the tag itself never survives into the output.
            tag.Remove();

            // A nested tag has no sensible merge target, so it is dropped rather than folded in at the top level,
            // where it would attach a summary to whatever element happened to contain it.
            if (inherited != null && !nested)
                Merge(member, inherited);
        }

        return member;
    }

    /// <summary>
    /// Drops the parts of inherited documentation that describe something the member it landed on does not have.
    /// </summary>
    /// <remarks>
    /// An overload commonly inherits from the fuller overload beside it, which documents parameters the shorter one
    /// never takes. Left in place those become build warnings on the generated file and a tooltip describing an
    /// argument that is not in the signature. Prose referring to such a parameter keeps the word and loses the link,
    /// since the sentence around it usually still reads correctly.
    /// </remarks>
    private static void Sanitize(XElement member, HashSet<string> parameters, HashSet<string> typeParameters)
    {
        Prune(member, "param", parameters);
        Prune(member, "typeparam", typeParameters);
        Demote(member, "paramref", parameters);
        Demote(member, "typeparamref", typeParameters);
    }

    private static void Prune(XElement member, string tag, HashSet<string> valid)
    {
        foreach (var element in member.Elements(tag).ToList())
        {
            if (element.Attribute("name")?.Value is not { } name || !valid.Contains(name))
                element.Remove();
        }
    }

    private static void Demote(XElement member, string tag, HashSet<string> valid)
    {
        foreach (var element in member.Descendants(tag).ToList())
        {
            var name = element.Attribute("name")?.Value;

            if (name != null && valid.Contains(name))
                continue;

            element.ReplaceWith(new XElement("c", name ?? string.Empty));
        }
    }

    /// <summary>
    /// Folds inherited documentation underneath what the member says for itself, so a member that adds a
    /// <c>param</c> of its own keeps it and gains everything it did not restate.
    /// </summary>
    private static void Merge(XElement member, XElement inherited)
    {
        foreach (var element in inherited.Elements())
        {
            if (member.Elements(element.Name).Any(existing => SameSubject(existing, element)))
                continue;

            member.Add(element);
        }
    }

    /// <summary>
    /// Whether two documentation elements describe the same thing, which for the per-name tags means the same name.
    /// </summary>
    private static bool SameSubject(XElement left, XElement right)
        => left.Attribute("name")?.Value == right.Attribute("name")?.Value;

    private static ISymbol? InheritedFrom(XElement tag, ISymbol symbol, Compilation compilation)
    {
        if (tag.Attribute("cref")?.Value is { Length: > 0 } cref)
            return DocumentationCommentId.GetFirstSymbolForDeclarationId(cref, compilation);

        return symbol switch
        {
            IMethodSymbol method => method.OverriddenMethod ?? InterfaceMember(method),
            IPropertySymbol property => property.OverriddenProperty ?? InterfaceMember(property),
            INamedTypeSymbol type => type.BaseType,
            _ => null,
        };
    }

    private static ISymbol? InterfaceMember(ISymbol symbol)
    {
        foreach (var contract in symbol.ContainingType.AllInterfaces)
        {
            foreach (var candidate in contract.GetMembers(symbol.Name))
            {
                if (SymbolEqualityComparer.Default.Equals(symbol.ContainingType.FindImplementationForInterfaceMember(candidate), symbol))
                    return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// The inside of a documentation element, with the common indentation of the source file taken back off.
    /// </summary>
    private static string? Body(XElement member)
    {
        var builder = new StringBuilder();

        foreach (var node in member.Nodes())
            builder.Append(node.ToString());

        var text = builder.ToString();
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var lines = text.Split('\n').Select(static line => line.TrimEnd('\r')).ToList();

        while (lines.Count > 0 && lines[0].Trim().Length == 0)
            lines.RemoveAt(0);

        while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0)
            lines.RemoveAt(lines.Count - 1);

        if (lines.Count == 0)
            return null;

        var margin = lines
            .Where(static line => line.Trim().Length > 0)
            .Select(static line => line.Length - line.TrimStart(' ').Length)
            .DefaultIfEmpty(0)
            .Min();

        return string.Join("\n", lines.Select(line => line.Length >= margin ? line.Substring(margin) : line.TrimStart(' ')));
    }
}
