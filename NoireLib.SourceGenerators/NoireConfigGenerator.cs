#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NoireLib.SourceGenerators;

[Generator]
public class NoireConfigGenerator : IIncrementalGenerator
{
    private const string ConfigAttributeFullName = "NoireLib.Configuration.NoireConfigAttribute";
    private const string AutoSaveAttributeFullName = "NoireLib.Configuration.AutoSaveAttribute";

    private static readonly List<string> PropertiesToIgnore = new()
    {
        "LoadFromDiskOnInitialization",
        "Version",
    };

    private static readonly List<string> MethodsToIgnore = new()
    {
        "GetConfigFileName",
    };

    // Member names the settings class declares itself.
    private const string SettingsDefaultsField = "NoireDefaults";
    private const string SettingsAllField = "All";

    private static readonly SymbolDisplayFormat SettingTypeFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (s, _) => IsCandidateClass(s),
                static (ctx, _) => GetSemanticTarget(ctx))
            .Where(static m => m is not null);

        context.RegisterSourceOutput(classDeclarations, Execute!);
    }

    private static bool IsCandidateClass(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax c && c.AttributeLists.Count > 0;
    }

    private static ClassInfo? GetSemanticTarget(GeneratorSyntaxContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration);

        if (classSymbol == null)
            return null;

        var configAttribute = classSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ConfigAttributeFullName);

        if (configAttribute == null)
            return null;

        string staticClassName;

        if (configAttribute.ConstructorArguments.Length > 0)
        {
            var argument = configAttribute.ConstructorArguments[0];
            if (argument.Value is string customName && !string.IsNullOrWhiteSpace(customName))
            {
                staticClassName = customName;
            }
            else
            {
                // Invalid argument, skip this class
                return null;
            }
        }
        else
        {
            staticClassName = classSymbol.Name + "Static";
        }

        string? settingsClassName = null;

        foreach (var named in configAttribute.NamedArguments)
        {
            if (named.Key == "SettingsClassName" && named.Value.Value is string settingsName && !string.IsNullOrWhiteSpace(settingsName))
                settingsClassName = settingsName;
        }

        var classAutoSave = classSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == AutoSaveAttributeFullName);

        var properties = new List<PropertyInfo>();
        var settings = new List<SettingInfo>();
        foreach (var member in classSymbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (member.DeclaredAccessibility != Accessibility.Public)
                continue;

            if (member.IsStatic || member.IsIndexer)
                continue;

            var hasAutoSave = classAutoSave || member.GetAttributes()
                .Any(a => a.AttributeClass?.ToDisplayString() == AutoSaveAttributeFullName);

            var hasSetter = member.SetMethod is { DeclaredAccessibility: Accessibility.Public };

            properties.Add(new PropertyInfo(member.Name, member.Type.ToDisplayString(), hasAutoSave, hasSetter));

            if (settingsClassName != null && IsSetting(member))
                settings.Add(new SettingInfo(member.Name, member.Type.ToDisplayString(SettingTypeFormat)));
        }

        var methods = new List<MethodInfo>();
        foreach (var method in classSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (method.DeclaredAccessibility != Accessibility.Public)
                continue;

            if (method.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.Constructor)
                continue;

            var parameters = new List<(string Type, string Name)>();
            foreach (var param in method.Parameters)
                parameters.Add((param.Type.ToDisplayString(), param.Name));

            var hasAutoSave = classAutoSave || method.GetAttributes()
                .Any(a => a.AttributeClass?.ToDisplayString() == AutoSaveAttributeFullName);

            methods.Add(new MethodInfo(method.Name, method.ReturnType.ToDisplayString(), parameters, method.IsStatic, hasAutoSave));
        }

        return new ClassInfo(classSymbol.ContainingNamespace.ToDisplayString(),
            staticClassName,
            classSymbol.Name,
            properties,
            methods,
            settingsClassName,
            classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            settings);
    }

    // A public read/write instance property of a simple type: a value the settings UI can edit and a share code can carry.
    private static bool IsSetting(IPropertySymbol property)
    {
        if (PropertiesToIgnore.Contains(property.Name) || property.Name is SettingsDefaultsField or SettingsAllField)
            return false;

        if (property.GetMethod is not { DeclaredAccessibility: Accessibility.Public }
            || property.SetMethod is not { DeclaredAccessibility: Accessibility.Public, IsInitOnly: false })
            return false;

        var type = property.Type;

        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            type = nullable.TypeArguments[0];

        if (type.TypeKind == TypeKind.Enum)
            return true;

        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean:
            case SpecialType.System_Char:
            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
            case SpecialType.System_String:
            case SpecialType.System_DateTime:
                return true;
        }

        var name = type.ToDisplayString();
        return name is "System.TimeSpan" or "System.Numerics.Vector2" or "System.Numerics.Vector3" or "System.Numerics.Vector4";
    }

    private void Execute(SourceProductionContext context, ClassInfo? classInfo)
    {
        if (classInfo == null)
            return;

        var source = GenerateSource(classInfo);
        context.AddSource($"{classInfo.StaticClassName}.g.cs", SourceText.From(source, Encoding.UTF8));

        if (classInfo.SettingsClassName != null)
            context.AddSource($"{classInfo.SettingsClassName}.g.cs", SourceText.From(GenerateSettingsSource(classInfo), Encoding.UTF8));
    }

    private static string GenerateSettingsSource(ClassInfo classInfo)
    {
        const string setting = "global::NoireLib.Configuration.NoireSetting";
        const string rules = "global::NoireLib.Configuration.NoireRules";
        var config = classInfo.InstanceFullName;
        var instance = $"global::NoireLib.Configuration.NoireConfigManager.GetConfig<{config}>()!";
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine($"namespace {classInfo.Namespace}");
        sb.AppendLine("{");
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// The settings of the {classInfo.InstanceClassName} configuration, one per simple property.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"public static partial class {classInfo.SettingsClassName}");
        sb.AppendLine("{");
        sb.AppendLine($"private static readonly {config} {SettingsDefaultsField} = new {config}();");
        sb.AppendLine();

        foreach (var info in classInfo.Settings)
        {
            sb.AppendLine("/// <summary>");
            sb.AppendLine($"/// The {info.Name} setting.");
            sb.AppendLine("/// </summary>");
            sb.AppendLine($"public static readonly {setting}<{info.TypeName}> {info.Name} = new {setting}<{info.TypeName}>(");
            sb.AppendLine($"\"{info.Name}\",");
            sb.AppendLine($"{SettingsDefaultsField}.{info.Name},");
            sb.AppendLine($"{rules}<{info.TypeName}>.Of(typeof({config}), \"{info.Name}\"),");
            sb.AppendLine($"static () => {instance}.{info.Name},");
            sb.AppendLine($"static value => {{ var instance = {instance}; instance.{info.Name} = value; instance.RequestSave(); }});");
            sb.AppendLine();
        }

        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Every setting above, in declaration order.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"public static readonly global::NoireLib.Configuration.INoireSetting[] {SettingsAllField} = new global::NoireLib.Configuration.INoireSetting[]");
        sb.AppendLine("{");

        foreach (var info in classInfo.Settings)
            sb.AppendLine($"{info.Name},");

        sb.AppendLine("};");
        sb.AppendLine("}");
        sb.AppendLine("}");

        return FormatCSharpCode(sb.ToString());
    }

    private static string GenerateSource(ClassInfo classInfo)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine($"namespace {classInfo.Namespace}");
        sb.AppendLine("{");
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// Static accessor for the {classInfo.InstanceClassName} configuration.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"public static class {classInfo.StaticClassName}");
        sb.AppendLine("{");
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Gets the singleton instance of this configuration, resolved through the manager cache so every");
        sb.AppendLine("/// access path hands out the same object.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"public static {classInfo.InstanceClassName} Instance => global::NoireLib.Configuration.NoireConfigManager.GetConfig<{classInfo.InstanceClassName}>()!;");
        sb.AppendLine();

        foreach (var property in classInfo.Properties)
        {
            if (PropertiesToIgnore.Contains(property.Name))
                continue;

            sb.AppendLine("/// <summary>");
            sb.AppendLine($"/// Gets or sets the {property.Name} property on the configuration instance.");

            if (property.HasAutoSave && property.HasSetter)
                sb.AppendLine("/// Setting it requests a save immediately.");

            sb.AppendLine("/// </summary>");
            sb.AppendLine($"public static {property.TypeName} {property.Name}");
            sb.AppendLine("{");
            sb.AppendLine("get => Instance." + property.Name + ";");

            if (property.HasSetter)
            {
                sb.AppendLine("set");
                sb.AppendLine("{");

                if (property.HasAutoSave)
                {
                    sb.AppendLine("var instance = Instance;");
                    sb.AppendLine("instance." + property.Name + " = value;");
                    sb.AppendLine("instance.RequestSave();");
                }
                else
                {
                    sb.AppendLine("Instance." + property.Name + " = value;");
                }

                sb.AppendLine("}");
            }

            sb.AppendLine("}");
            sb.AppendLine();
        }

        foreach (var method in classInfo.Methods)
        {
            if (MethodsToIgnore.Contains(method.Name))
                continue;

            var paramDecls = method.Parameters.Count > 0
                ? string.Join(", ", method.Parameters.Select(p => $"{p.Type} {p.Name}").ToArray())
                : string.Empty;

            var argList = method.Parameters.Count > 0
                ? string.Join(", ", method.Parameters.Select(p => p.Name).ToArray())
                : string.Empty;

            sb.AppendLine("/// <summary>");
            sb.AppendLine($"/// Calls the {method.Name} method on the configuration instance.");

            if (method.HasAutoSave)
                sb.AppendLine("/// Requests a save after it runs.");

            sb.AppendLine("/// </summary>");
            sb.AppendLine($"public static {method.ReturnType} {method.Name}({paramDecls})");
            sb.AppendLine("{");

            var hasReturnValue = method.ReturnType != "void";
            var target = method.IsStatic ? classInfo.InstanceClassName : "instance";

            if (method.IsStatic)
            {
                if (hasReturnValue && method.HasAutoSave)
                {
                    sb.AppendLine($"var result = {target}.{method.Name}({argList});");
                    sb.AppendLine("RequestSave();");
                    sb.AppendLine("return result;");
                }
                else if (hasReturnValue)
                {
                    sb.AppendLine($"return {target}.{method.Name}({argList});");
                }
                else
                {
                    sb.AppendLine($"{target}.{method.Name}({argList});");
                    if (method.HasAutoSave)
                        sb.AppendLine("RequestSave();");
                }
            }
            else
            {
                sb.AppendLine("var instance = Instance;");

                if (hasReturnValue && method.HasAutoSave)
                {
                    sb.AppendLine($"var result = instance.{method.Name}({argList});");
                    sb.AppendLine("instance.RequestSave();");
                    sb.AppendLine("return result;");
                }
                else if (hasReturnValue)
                {
                    sb.AppendLine($"return instance.{method.Name}({argList});");
                }
                else
                {
                    sb.AppendLine($"instance.{method.Name}({argList});");
                    if (method.HasAutoSave)
                        sb.AppendLine("instance.RequestSave();");
                }
            }

            sb.AppendLine("}");
            sb.AppendLine();
        }

        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Saves the configuration to disk, blocking until the write has landed.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public static bool Save() => Instance.Save();");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Captures the configuration now and writes it shortly afterwards on a background thread.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public static void RequestSave() => Instance.RequestSave();");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Reloads the configuration from disk.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"public static void Reload() => global::NoireLib.Configuration.NoireConfigManager.ReloadConfig<{classInfo.InstanceClassName}>();");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Clears the cached instance, so the next access reloads from disk.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"public static void ClearCache() => global::NoireLib.Configuration.NoireConfigManager.UnloadConfig<{classInfo.InstanceClassName}>();");
        sb.AppendLine("}");
        sb.AppendLine("}");

        return FormatCSharpCode(sb.ToString());
    }

    private static string FormatCSharpCode(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot().NormalizeWhitespace();
        return root.ToFullString();
    }

    private class ClassInfo
    {
        public string Namespace { get; }
        public string StaticClassName { get; }
        public string InstanceClassName { get; }
        public List<PropertyInfo> Properties { get; }
        public List<MethodInfo> Methods { get; }
        public string? SettingsClassName { get; }
        public string InstanceFullName { get; }
        public List<SettingInfo> Settings { get; }

        public ClassInfo(string @namespace, string staticClassName, string instanceClassName, List<PropertyInfo> properties, List<MethodInfo> methods,
            string? settingsClassName, string instanceFullName, List<SettingInfo> settings)
        {
            Namespace = @namespace;
            StaticClassName = staticClassName;
            InstanceClassName = instanceClassName;
            Properties = properties;
            Methods = methods;
            SettingsClassName = settingsClassName;
            InstanceFullName = instanceFullName;
            Settings = settings;
        }
    }

    private class SettingInfo
    {
        public string Name { get; }
        public string TypeName { get; }

        public SettingInfo(string name, string typeName)
        {
            Name = name;
            TypeName = typeName;
        }
    }

    private class PropertyInfo
    {
        public string Name { get; }
        public string TypeName { get; }
        public bool HasAutoSave { get; }
        public bool HasSetter { get; }

        public PropertyInfo(string name, string typeName, bool hasAutoSave, bool hasSetter)
        {
            Name = name;
            TypeName = typeName;
            HasAutoSave = hasAutoSave;
            HasSetter = hasSetter;
        }
    }

    private class MethodInfo
    {
        public string Name { get; }
        public string ReturnType { get; }
        public List<(string Type, string Name)> Parameters { get; }
        public bool IsStatic { get; }
        public bool HasAutoSave { get; }

        public MethodInfo(string name, string returnType, List<(string Type, string Name)> parameters, bool isStatic, bool hasAutoSave)
        {
            Name = name;
            ReturnType = returnType;
            Parameters = parameters;
            IsStatic = isStatic;
            HasAutoSave = hasAutoSave;
        }
    }
}
