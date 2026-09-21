using System;
using System.Linq.Expressions;

namespace NoireLib.Remote.Internal;

// An event declares its own handler type.
internal static class RemoteDelegateFactory
{
    public static Delegate? Create(Type delegateType, Action<object?[]> forward)
    {
        var invoke = delegateType.GetMethod("Invoke");

        if (invoke == null)
            return null;

        var parameters = invoke.GetParameters();
        var declared = new ParameterExpression[parameters.Length];
        var boxed = new Expression[parameters.Length];

        for (var index = 0; index < parameters.Length; index++)
        {
            declared[index] = Expression.Parameter(parameters[index].ParameterType, parameters[index].Name ?? ("arg" + index));
            boxed[index] = Expression.Convert(declared[index], typeof(object));
        }

        var body = Expression.Invoke(
            Expression.Constant(forward),
            Expression.NewArrayInit(typeof(object), boxed));

        try
        {
            return Expression.Lambda(delegateType, body, declared).Compile();
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
