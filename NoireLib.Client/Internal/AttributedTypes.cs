using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NoireLib.Core.Reflection;

// A missing dependency makes GetTypes throw with the loaded types attached to the exception.
internal static class AttributedTypes
{
    public static Type[] Loadable(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type != null).Cast<Type>().ToArray();
        }
    }

    public static IReadOnlyList<Type> WithAttribute<TAttribute>(Assembly assembly) where TAttribute : Attribute
        => Loadable(assembly)
            .Where(type => type.GetCustomAttribute<TAttribute>() != null)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
}
