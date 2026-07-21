using FluentAssertions;
using NoireLib.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the grouped NoireUI path against the two ways it can rot without anything failing to compile: a forward whose
/// signature or optional default has fallen behind the surface it forwards to, and a forward that reaches consumers
/// with no documentation.<br/>
/// Both assertions read the built artifact rather than the generator, because the assembly and the XML documentation
/// file beside it are exactly what a consumer plugin compiles against and reads IntelliSense from. Nothing here knows
/// how the generator produced any of it.
/// </summary>
/// <remarks>
/// Driven by <see cref="NoireFacadeAttribute"/> rather than by a list, so a surface marked later is covered without
/// either test being edited.
/// </remarks>
public class NoireFacadeContractTests
{
    /// <summary>
    /// Every marked surface, paired with the nested type carrying its grouped entry.
    /// </summary>
    public static TheoryData<string> MarkedSurfaces()
    {
        var data = new TheoryData<string>();

        foreach (var surface in Surfaces())
            data.Add(surface.FullName!);

        return data;
    }

    [Fact]
    public void MarkedSurfaces_AreNotEmpty()
        => Surfaces().Should().NotBeEmpty("the contract tests are driven by the marker, so an empty set would pass every assertion vacuously");

    [Theory]
    [MemberData(nameof(MarkedSurfaces))]
    public void GroupedEntry_MatchesItsSurfaceMemberForMember(string surfaceName)
    {
        var surface = Surface(surfaceName);
        var grouped = GroupedEntry(surface);

        grouped.Should().NotBeNull($"{surface.Name} is marked for the grouped path, so NoireUI.{GroupedName(surface)} must exist");

        foreach (var member in PublicStatics(surface))
        {
            var counterparts = PublicStatics(grouped!).Where(candidate => candidate.Name == member.Name).ToList();
            counterparts.Should().NotBeEmpty($"NoireUI.{GroupedName(surface)} must forward {surface.Name}.{member.Name}");

            counterparts.Should().Contain(
                candidate => Signature(candidate) == Signature(member),
                $"NoireUI.{GroupedName(surface)}.{member.Name} must match {surface.Name}.{member.Name} exactly. Expected {Signature(member)}, found {string.Join(" | ", counterparts.Select(Signature))}");
        }
    }

    [Theory]
    [MemberData(nameof(MarkedSurfaces))]
    public void GroupedEntry_ForwardsNothingTheSurfaceDoesNotDeclare(string surfaceName)
    {
        var surface = Surface(surfaceName);
        var grouped = GroupedEntry(surface);
        var declared = PublicStatics(surface).Select(Signature).ToHashSet(StringComparer.Ordinal);

        grouped.Should().NotBeNull($"NoireUI.{GroupedName(surface)} must exist before it can be compared against {surface.Name}");

        foreach (var member in PublicStatics(grouped!))
        {
            declared.Should().Contain(
                Signature(member),
                $"NoireUI.{GroupedName(surface)}.{member.Name} has no counterpart on {surface.Name}, so the grouped path offers something the surface does not");
        }
    }

    [Theory]
    [MemberData(nameof(MarkedSurfaces))]
    public void GroupedEntry_ShipsDocumentationForEveryMember(string surfaceName)
    {
        var surface = Surface(surfaceName);
        var grouped = GroupedEntry(surface);
        var documented = ShippedDocumentation();

        documented.Should().ContainKey(grouped!.FullName!.Replace('+', '.'),
            "the grouped type itself carries the documentation of the surface it groups");

        foreach (var group in PublicStatics(grouped).GroupBy(static member => member.Name))
        {
            var prefix = $"{grouped.FullName!.Replace('+', '.')}.{group.Key}";

            var entries = documented
                .Where(entry => entry.Key == prefix || entry.Key.StartsWith(prefix + "(", StringComparison.Ordinal) || entry.Key.StartsWith(prefix + "`", StringComparison.Ordinal))
                .ToList();

            entries.Should().HaveCount(group.Count(),
                $"the shipped documentation file must carry one entry per overload of NoireUI.{GroupedName(surface)}.{group.Key}");

            foreach (var entry in entries)
                entry.Value.Should().NotBeNullOrWhiteSpace($"{entry.Key} would reach a consumer as a blank tooltip");
        }
    }

    // ---------------------------------------------------------------- creation methods

    /// <summary>
    /// Every widget marked for a creation method.
    /// </summary>
    public static TheoryData<string> MarkedWidgets()
    {
        var data = new TheoryData<string>();

        foreach (var widget in Widgets())
            data.Add(widget.FullName!);

        return data;
    }

    [Fact]
    public void MarkedWidgets_AreNotEmpty()
        => Widgets().Should().NotBeEmpty("an empty set would pass every creation-method assertion vacuously");

    [Theory]
    [MemberData(nameof(MarkedWidgets))]
    public void CreationMethod_MatchesEveryPublicConstructor(string widgetName)
    {
        var widget = Surface(widgetName);
        var name = GroupedName(widget);

        var created = typeof(NoireUI)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == name)
            .ToList();

        created.Should().NotBeEmpty($"{widget.Name} is marked for a creation method, so NoireUI.{name} must exist");

        foreach (var constructor in widget.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
        {
            created.Should().Contain(
                method => ConstructorSignature(method) == ConstructorSignature(constructor),
                $"NoireUI.{name} must offer every way {widget.Name} can be constructed. Missing {ConstructorSignature(constructor)}");
        }

        foreach (var method in created)
        {
            (method.IsGenericMethodDefinition ? method.ReturnType.GetGenericTypeDefinition() : method.ReturnType)
                .Should().Be(widget, $"NoireUI.{name} creates a {widget.Name}");
        }
    }

    [Theory]
    [MemberData(nameof(MarkedWidgets))]
    public void CreationMethod_DoesNotReplaceDirectConstruction(string widgetName)
    {
        var widget = Surface(widgetName);

        widget.IsPublic.Should().BeTrue($"{widget.Name} stays constructible directly; the creation method is an additional door");
        widget.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Should().NotBeEmpty();
        widget.GetCustomAttribute<ObsoleteAttribute>().Should().BeNull($"{widget.Name} is not deprecated by gaining a creation method");
    }

    [Theory]
    [MemberData(nameof(MarkedWidgets))]
    public void CreationMethod_ShipsDocumentation(string widgetName)
    {
        var widget = Surface(widgetName);
        var name = GroupedName(widget);
        var documented = ShippedDocumentation();
        var prefix = $"NoireLib.UI.NoireUI.{name}";

        var entries = documented
            .Where(entry => entry.Key == prefix || entry.Key.StartsWith(prefix + "(", StringComparison.Ordinal) || entry.Key.StartsWith(prefix + "`", StringComparison.Ordinal))
            .ToList();

        entries.Should().NotBeEmpty($"NoireUI.{name} must reach the shipped documentation file");

        foreach (var entry in entries)
            entry.Value.Should().NotBeNullOrWhiteSpace($"{entry.Key} would reach a consumer as a blank tooltip");
    }

    [Fact]
    public void Markers_OnOneType_AreMutuallyExclusive()
    {
        var both = typeof(NoireUI).Assembly
            .GetTypes()
            .Where(static type => type.GetCustomAttribute<NoireFacadeAttribute>() != null && type.GetCustomAttribute<NoireFacadeFactoryAttribute>() != null)
            .Select(static type => type.Name)
            .ToList();

        both.Should().BeEmpty(
            "a nested grouped class and a creation method of the same name cannot coexist, and one system gets one entry point");
    }

    /// <summary>
    /// An overload commonly borrows its documentation from the fuller overload beside it, which documents parameters
    /// the shorter one never takes. Copying that across unchanged describes an argument the signature does not have.
    /// </summary>
    [Fact]
    public void ShippedDocumentation_DescribesNoParameterTheMemberDoesNotHave()
    {
        var documentation = XDocument.Load(DocumentationPath());
        var overdocumented = new List<string>();

        foreach (var member in documentation.Descendants("member"))
        {
            if (member.Attribute("name")?.Value is not { } name || !name.StartsWith("M:NoireLib.UI.NoireUI.", StringComparison.Ordinal))
                continue;

            var documented = member.Elements("param").Count();
            if (documented > ParameterCount(name))
                overdocumented.Add($"{name} documents {documented} parameters but takes {ParameterCount(name)}");
        }

        overdocumented.Should().BeEmpty("a tooltip that names an argument the signature does not have is worse than no tooltip");
    }

    /// <summary>
    /// How many parameters a documentation id declares, counting only the separators at the top level so that a
    /// generic argument list inside one parameter is not mistaken for several.
    /// </summary>
    private static int ParameterCount(string documentationId)
    {
        var open = documentationId.IndexOf('(');
        if (open < 0)
            return 0;

        var parameters = documentationId.Substring(open + 1, documentationId.LastIndexOf(')') - open - 1);
        if (parameters.Length == 0)
            return 0;

        var count = 1;
        var depth = 0;

        foreach (var character in parameters)
        {
            switch (character)
            {
                case '{' or '[' or '(':
                    depth++;
                    break;

                case '}' or ']' or ')':
                    depth--;
                    break;

                case ',' when depth == 0:
                    count++;
                    break;
            }
        }

        return count;
    }

    /// <summary>
    /// The compiler copies an inherited-documentation tag into the XML file verbatim instead of expanding it, and that
    /// file is the whole of what a consumer's IntelliSense reads from a package. Every tag reaching it is a member
    /// that documents itself in source and reaches consumers blank.
    /// </summary>
    /// <remarks>
    /// Public members only. A member a consumer cannot see costs them nothing, and the tag is genuinely useful in
    /// source, so the gate is drawn at the package boundary rather than at the syntax.
    /// </remarks>
    [Fact]
    public void ShippedDocumentation_CarriesNoInheritedDocumentationTags_OnPublicUiMembers()
    {
        var documentation = XDocument.Load(DocumentationPath());

        var inherited = documentation
            .Descendants("member")
            .Where(static member => member.Descendants("inheritdoc").Any())
            .Select(static member => member.Attribute("name")?.Value)
            .Where(static name => name != null && name.Contains("NoireLib.UI.", StringComparison.Ordinal))
            .Where(name => IsPublic(name!))
            .ToList();

        inherited.Should().BeEmpty(
            "an inherited-documentation tag on a public member reaches every consumer of the package as a blank tooltip");
    }

    /// <summary>
    /// Whether a documentation id names something a consumer of the package can actually see.
    /// </summary>
    private static bool IsPublic(string documentationId)
    {
        var body = documentationId.Substring(2);

        var signature = body.IndexOf('(');
        if (signature >= 0)
            body = body.Substring(0, signature);

        if (documentationId.StartsWith("T:", StringComparison.Ordinal))
            return typeof(NoireUI).Assembly.GetType(body)?.IsPublic == true;

        var split = body.LastIndexOf('.');
        if (split < 0)
            return false;

        // A generic method carries its arity after the name; the type it lives on does not.
        var memberName = body.Substring(split + 1).Split('`')[0];
        var declaring = typeof(NoireUI).Assembly.GetType(body.Substring(0, split));

        return declaring is { IsPublic: true }
            && declaring.GetMember(memberName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Length > 0;
    }

    [Fact]
    public void DocumentationFile_ReachesTheTestOutput()
        => File.Exists(DocumentationPath()).Should().BeTrue(
            $"the documentation guarantee is asserted against the shipped file, which the build must place at {DocumentationPath()}");

    // ---------------------------------------------------------------- the surfaces and their grouped entries

    private static IReadOnlyList<Type> Surfaces()
        => typeof(NoireUI).Assembly
            .GetTypes()
            .Where(static type => type.GetCustomAttribute<NoireFacadeAttribute>() != null)
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToList();

    private static IReadOnlyList<Type> Widgets()
        => typeof(NoireUI).Assembly
            .GetTypes()
            .Where(static type => type.GetCustomAttribute<NoireFacadeFactoryAttribute>() != null)
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToList();

    private static Type Surface(string fullName)
        => typeof(NoireUI).Assembly.GetType(fullName)
            ?? throw new InvalidOperationException($"{fullName} is no longer in the assembly.");

    private static Type? GroupedEntry(Type surface)
        => typeof(NoireUI).GetNestedType(GroupedName(surface), BindingFlags.Public);

    private static string GroupedName(Type surface)
    {
        if (surface.GetCustomAttribute<NoireFacadeAttribute>()?.Name is { Length: > 0 } explicitName)
            return explicitName;

        // Reflection carries a generic type's arity in its name; the grouped name never does.
        var name = surface.Name.Split('`')[0];

        return name.StartsWith("Noire", StringComparison.Ordinal) && name.Length > "Noire".Length
            ? name.Substring("Noire".Length)
            : name;
    }

    private static IEnumerable<MemberInfo> PublicStatics(Type type)
        => type.GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(static member => member switch
            {
                MethodInfo method => !method.IsSpecialName,
                PropertyInfo => true,
                FieldInfo field => field.IsLiteral,
                _ => false,
            });

    // ---------------------------------------------------------------- what "identical" means

    /// <summary>
    /// A member rendered down to everything a caller can observe: return type, generic arity, and for each parameter
    /// its type, name, by-reference direction and default value.
    /// </summary>
    /// <remarks>
    /// Defaults are part of the signature on purpose. C# bakes an optional parameter's default into the caller, so a
    /// forward whose default has drifted compiles clean and produces a different result from the same library.
    /// </remarks>
    private static string Signature(MemberInfo member) => member switch
    {
        MethodInfo method =>
            $"{method.ReturnType} {method.Name}{Arity(method)}({string.Join(", ", method.GetParameters().Select(Signature))})",
        PropertyInfo property =>
            $"{property.PropertyType} {property.Name} {{ {(property.GetGetMethod() != null ? "get; " : string.Empty)}{(property.GetSetMethod() != null ? "set; " : string.Empty)}}}",
        FieldInfo field =>
            $"const {field.FieldType} {field.Name} = {field.GetRawConstantValue()}",
        _ => member.ToString()!,
    };

    /// <summary>
    /// A constructor and the creation method standing in for it, rendered the same way so drift between them shows.
    /// </summary>
    private static string ConstructorSignature(MethodBase constructor)
        => $"({string.Join(", ", constructor.GetParameters().Select(Signature))})";

    private static string Arity(MethodInfo method)
        => method.IsGenericMethodDefinition ? $"`{method.GetGenericArguments().Length}" : string.Empty;

    private static string Signature(ParameterInfo parameter)
    {
        var direction = parameter.IsOut ? "out " : parameter.IsIn ? "in " : parameter.ParameterType.IsByRef ? "ref " : string.Empty;
        var defaultValue = parameter.HasDefaultValue ? $" = {Render(parameter.RawDefaultValue)}" : string.Empty;
        var variadic = parameter.GetCustomAttribute<ParamArrayAttribute>() != null ? "params " : string.Empty;

        return $"{variadic}{direction}{parameter.ParameterType} {parameter.Name}{defaultValue}";
    }

    private static string Render(object? value) => value switch
    {
        null => "null",
        IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString()!,
    };

    // ---------------------------------------------------------------- the shipped documentation file

    private static string DocumentationPath()
        => Path.ChangeExtension(typeof(NoireUI).Assembly.Location, ".xml");

    /// <summary>
    /// The shipped documentation file as a map of member id (without its kind prefix) to summary text.
    /// </summary>
    private static Dictionary<string, string?> ShippedDocumentation()
    {
        var documentation = XDocument.Load(DocumentationPath());
        var entries = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var member in documentation.Descendants("member"))
        {
            if (member.Attribute("name")?.Value is not { Length: > 2 } name)
                continue;

            entries[name.Substring(2)] = member.Element("summary")?.Value.Trim();
        }

        return entries;
    }
}
