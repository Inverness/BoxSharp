using System;
using System.Linq.Expressions;
using System.Reflection;

namespace BoxSharp
{
    internal static class ReflectionUtilities
    {
        internal static Action<TTarget, TValue> MakeFieldSetter<TTarget, TValue>(string fieldName)
        {
            ParameterExpression targetParam = Expression.Parameter(typeof(TTarget));
            ParameterExpression valueParam = Expression.Parameter(typeof(TValue));

            MemberExpression field = Expression.Field(targetParam, fieldName);

            BinaryExpression assign = Expression.Assign(field, valueParam);

            return Expression.Lambda<Action<TTarget, TValue>>(assign, targetParam, valueParam).Compile();
        }

        internal static Action<TValue> MakeStaticFieldSetter<TValue>(Type type, string fieldName)
        {
            ParameterExpression valueParam = Expression.Parameter(typeof(TValue));

            MemberExpression field = Expression.Field(null, type, fieldName);

            BinaryExpression assign = Expression.Assign(field, valueParam);

            return Expression.Lambda<Action<TValue>>(assign, valueParam).Compile();
        }

        internal static Action<object?> MakeStaticFieldSetter(Type type, string fieldName)
        {
            ParameterExpression valueParam = Expression.Parameter(typeof(object));

            MemberExpression field = Expression.Field(null, type, fieldName);

            UnaryExpression convertedValue = Expression.Convert(valueParam, ((FieldInfo) field.Member).FieldType);

            BinaryExpression assign = Expression.Assign(field, convertedValue);

            return Expression.Lambda<Action<object?>>(assign, valueParam).Compile();
        }
    }
}
