using System;
using System.Linq.Expressions;

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
    }
}
