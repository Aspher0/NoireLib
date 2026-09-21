using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Remote.Internal;

// A member that fails it is dropped at publication, never at call time.
internal static class RemoteJsonContract
{
    private static readonly HashSet<Type> Scalars =
    [
        typeof(string), typeof(bool), typeof(char),
        typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
        typeof(int), typeof(uint), typeof(long), typeof(ulong),
        typeof(float), typeof(double), typeof(decimal),
        typeof(Guid), typeof(DateTime), typeof(DateTimeOffset), typeof(TimeSpan),
        typeof(Uri), typeof(Version),
    ];

    private static readonly HashSet<Type> Vectors =
    [
        typeof(Vector2), typeof(Vector3), typeof(Vector4), typeof(Quaternion),
    ];

    private static readonly string[] GameAssemblyPrefixes =
    [
        "Dalamud", "FFXIVClientStructs", "Lumina", "InteropGenerator", "HexaGen", "TerraFX",
    ];

    public static bool CanCross(Type type, out string reason)
        => CanCross(type, [], out reason);

    private static bool CanCross(Type type, HashSet<Type> seen, out string reason)
    {
        reason = string.Empty;

        if (type.IsByRef || type.IsPointer || type == typeof(IntPtr) || type == typeof(UIntPtr))
        {
            reason = "a pointer or reference type";
            return false;
        }

        if (type == typeof(void))
            return true;

        if (type.IsGenericParameter || type.ContainsGenericParameters)
        {
            reason = "an open generic type";
            return false;
        }

        if (type == typeof(object))
        {
            reason = "object, which carries no shape";
            return false;
        }

        if (typeof(Delegate).IsAssignableFrom(type))
        {
            reason = "a delegate";
            return false;
        }

        if (IsGameType(type))
        {
            reason = "declared in " + (type.Assembly.GetName().Name ?? "a game assembly");
            return false;
        }

        if (Scalars.Contains(type) || Vectors.Contains(type) || type.IsEnum)
            return true;

        var nullable = Nullable.GetUnderlyingType(type);

        if (nullable != null)
            return CanCross(nullable, seen, out reason);

        if (type == typeof(JObject) || type == typeof(JArray) || type == typeof(JToken) || type == typeof(JValue))
            return true;

        // Its bytes move on the file routes.
        if (type == typeof(NoireRemoteFile))
            return true;

        if (!seen.Add(type))
            return true;

        if (type.IsArray)
            return CanCross(type.GetElementType()!, seen, out reason);

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var arguments = type.GetGenericArguments();

            if (definition == typeof(Dictionary<,>) || definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>))
            {
                if (arguments[0] != typeof(string))
                {
                    reason = "a map keyed by something other than a string";
                    return false;
                }

                return CanCross(arguments[1], seen, out reason);
            }

            if (arguments.Length == 1 && IsSequenceDefinition(definition))
                return CanCross(arguments[0], seen, out reason);
        }

        if (type.IsInterface)
        {
            reason = "an interface with no known shape";
            return false;
        }

        if (type.IsAbstract)
        {
            reason = "abstract; nothing can be built from the wire";
            return false;
        }

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length > 0 || property.GetMethod == null)
                continue;

            if (!CanCross(property.PropertyType, seen, out var inner))
            {
                reason = Nested(property.Name, inner);
                return false;
            }
        }

        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!CanCross(field.FieldType, seen, out var inner))
            {
                reason = Nested(field.Name, inner);
                return false;
            }
        }

        return true;
    }

    // Callers write this after "its value is" or "the return type is". It reads as a type phrase.
    private static string Nested(string member, string inner)
        => "a type whose " + member + " is " + inner;

    public static bool IsGameType(Type type)
    {
        var assembly = type.Assembly.GetName().Name;

        if (string.IsNullOrEmpty(assembly))
            return false;

        foreach (var prefix in GameAssemblyPrefixes)
        {
            if (assembly!.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    public static Type UnwrapReturn(Type returnType)
    {
        if (returnType == typeof(Task) || returnType == typeof(ValueTask))
            return typeof(void);

        if (returnType.IsGenericType)
        {
            var definition = returnType.GetGenericTypeDefinition();

            if (definition == typeof(Task<>) || definition == typeof(ValueTask<>))
                return returnType.GetGenericArguments()[0];
        }

        return returnType;
    }

    public static string JsonTypeOf(Type type, IDictionary<string, JObject>? shapes)
    {
        var nullable = Nullable.GetUnderlyingType(type);

        if (nullable != null)
            type = nullable;

        if (type == typeof(void))
            return NoireRemoteJsonTypes.Null;

        if (type == typeof(string) || type == typeof(char) || type == typeof(Guid)
            || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan)
            || type == typeof(Uri) || type == typeof(Version))
            return NoireRemoteJsonTypes.String;

        if (type == typeof(bool))
            return NoireRemoteJsonTypes.Boolean;

        if (type.IsEnum)
            return NoireRemoteJsonTypes.Enum;

        if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort)
            || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong))
            return NoireRemoteJsonTypes.Integer;

        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            return NoireRemoteJsonTypes.Number;

        if (type == typeof(Vector2))
            return NoireRemoteJsonTypes.Vector2;

        if (type == typeof(Vector3))
            return NoireRemoteJsonTypes.Vector3;

        if (type == typeof(Vector4))
            return NoireRemoteJsonTypes.Vector4;

        if (type == typeof(Quaternion))
            return NoireRemoteJsonTypes.Quaternion;

        if (type.IsArray)
            return NoireRemoteJsonTypes.Array;

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();

            if (definition == typeof(Dictionary<,>) || definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>))
                return NoireRemoteJsonTypes.Map;

            if (type.GetGenericArguments().Length == 1 && IsSequenceDefinition(definition))
                return NoireRemoteJsonTypes.Array;
        }

        if (shapes == null)
            return NoireRemoteJsonTypes.Object;

        return DescribeShape(type, shapes);
    }

    public static IReadOnlyList<string>? EnumValuesOf(Type type)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        return target.IsEnum ? Enum.GetNames(target) : null;
    }

    private static string DescribeShape(Type type, IDictionary<string, JObject> shapes)
    {
        var name = type.Name;

        if (shapes.ContainsKey(name))
            return NoireRemoteJsonTypes.ShapePrefix + name;

        // Registered before the members are walked. A self-referencing type terminates.
        var shape = new JObject();
        shapes[name] = shape;

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length > 0 || property.GetMethod == null)
                continue;

            shape[CamelCase(property.Name)] = JsonTypeOf(property.PropertyType, shapes);
        }

        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            shape[CamelCase(field.Name)] = JsonTypeOf(field.FieldType, shapes);

        return NoireRemoteJsonTypes.ShapePrefix + name;
    }

    private static string CamelCase(string name)
        => name.Length == 0 || char.IsLower(name[0]) ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);

    public static bool IsSequenceDefinition(Type definition)
        => definition == typeof(List<>)
            || definition == typeof(IList<>)
            || definition == typeof(IReadOnlyList<>)
            || definition == typeof(ICollection<>)
            || definition == typeof(IReadOnlyCollection<>)
            || definition == typeof(IEnumerable<>)
            || definition == typeof(HashSet<>)
            || definition == typeof(Queue<>)
            || definition == typeof(Stack<>);

    public static bool IsCancellationToken(Type type)
        => type == typeof(CancellationToken);
}
