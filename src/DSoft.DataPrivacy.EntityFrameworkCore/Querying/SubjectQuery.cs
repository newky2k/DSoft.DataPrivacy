using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DSoft.DataPrivacy.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DSoft.DataPrivacy.EntityFrameworkCore.Querying;

/// <summary>
/// Builds queries that find the records reached from a data subject along a <see cref="DataSubjectLink"/>.
/// Each step is a correlated <c>EXISTS</c> on the foreign key, so no navigation properties are needed.
/// </summary>
internal static class SubjectQuery
{
    private static readonly MethodInfo EfProperty = typeof(EF).GetMethod(nameof(EF.Property))!;

    private static readonly MethodInfo QueryableAny = typeof(Queryable).GetMethods()
        .Single(m => m.Name == nameof(Queryable.Any) && m.GetParameters().Length == 2);

    private static readonly MethodInfo QueryableWhere = typeof(Queryable).GetMethods()
        .First(m => m.Name == nameof(Queryable.Where) && m.GetParameters()[1].ParameterType.GetGenericArguments()[0].GetGenericArguments().Length == 2);

    private static readonly MethodInfo SetMethod = typeof(DbContext).GetMethods()
        .Single(m => m.Name == nameof(DbContext.Set) && m.IsGenericMethod && m.GetParameters().Length == 0);

    private static readonly MethodInfo NamedSetMethod = typeof(DbContext).GetMethods()
        .Single(m => m.Name == nameof(DbContext.Set) && m.IsGenericMethod && m.GetParameters().Length == 1);

    private static readonly MethodInfo ToListAsyncMethod = typeof(EntityFrameworkQueryableExtensions)
        .GetMethod(nameof(EntityFrameworkQueryableExtensions.ToListAsync))!;

    /// <summary>True when records of the entity type can be queried by this helper.</summary>
    public static bool CanQuery(IEntityType entityType) => !entityType.IsOwned();

    /// <summary>The root query for an entity type: <c>context.Set&lt;T&gt;()</c>, or the named set for a shared-type entity such as a join table.</summary>
    public static IQueryable Set(DbContext context, IEntityType entityType)
        => entityType.HasSharedClrType
            ? (IQueryable)NamedSetMethod.MakeGenericMethod(entityType.ClrType).Invoke(context, new object[] { entityType.Name })!
            : (IQueryable)SetMethod.MakeGenericMethod(entityType.ClrType).Invoke(context, null)!;

    /// <summary>Loads the records of <paramref name="entityType"/> linked to the subject through <paramref name="link"/>.</summary>
    /// <param name="context">The context.</param>
    /// <param name="entityType">The type whose records are wanted; the link's first foreign key must be declared on it or a base type.</param>
    /// <param name="link">The path to the subject.</param>
    /// <param name="subjectKey">The subject's values for the principal key the link ends on.</param>
    /// <param name="tracking">Whether the records are tracked.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    public static Task<List<object>> LoadAsync(DbContext context, IEntityType entityType, DataSubjectLink link, object?[] subjectKey, bool tracking, CancellationToken cancellationToken)
    {
        var parameter = Expression.Parameter(entityType.ClrType, "e");
        var predicate = Build(context, parameter, link.Path, 0, subjectKey);
        return LoadAsync(context, entityType, Expression.Lambda(predicate, parameter), tracking, cancellationToken);
    }

    /// <summary>Loads the records of <paramref name="dependentType"/> whose <paramref name="foreignKey"/> points at the principal key values given.</summary>
    public static Task<List<object>> LoadDependentsAsync(DbContext context, IEntityType dependentType, IForeignKey foreignKey, object?[] principalKey, CancellationToken cancellationToken)
    {
        var parameter = Expression.Parameter(dependentType.ClrType, "e");
        var predicate = KeyEquals(parameter, foreignKey.Properties, principalKey);
        return LoadAsync(context, dependentType, Expression.Lambda(predicate, parameter), tracking: true, cancellationToken);
    }

    /// <summary>Loads records matching a predicate, which must be a lambda over the entity's CLR type.</summary>
    public static async Task<List<object>> LoadAsync(DbContext context, IEntityType entityType, LambdaExpression predicate, bool tracking, CancellationToken cancellationToken, int? take = null)
    {
        var query = Set(context, entityType);
        query = query.Provider.CreateQuery(Expression.Call(QueryableWhere.MakeGenericMethod(entityType.ClrType), query.Expression, Expression.Quote(predicate)));

        if (take.HasValue)
        {
            var takeMethod = typeof(Queryable).GetMethods().First(m => m.Name == nameof(Queryable.Take) && m.GetParameters()[1].ParameterType == typeof(int));
            query = query.Provider.CreateQuery(Expression.Call(takeMethod.MakeGenericMethod(entityType.ClrType), query.Expression, Expression.Constant(take.Value)));
        }

        if (!tracking)
        {
            var noTracking = typeof(EntityFrameworkQueryableExtensions).GetMethod(nameof(EntityFrameworkQueryableExtensions.AsNoTracking))!;
            query = (IQueryable)noTracking.MakeGenericMethod(entityType.ClrType).Invoke(null, new object[] { query })!;
        }

        var task = (Task)ToListAsyncMethod.MakeGenericMethod(entityType.ClrType).Invoke(null, new object[] { query, cancellationToken })!;
        await task.ConfigureAwait(false);

        var list = (System.Collections.IEnumerable)task.GetType().GetProperty("Result")!.GetValue(task)!;
        return list.Cast<object>().ToList();
    }

    /// <summary><c>EF.Property&lt;T&gt;(entity, name)</c>.</summary>
    public static Expression PropertyAccess(Expression entity, IReadOnlyProperty property)
        => Expression.Call(EfProperty.MakeGenericMethod(property.ClrType), entity, Expression.Constant(property.Name));

    /// <summary>A parameterised constant: wrapping the value in a box makes the provider send it as a parameter.</summary>
    public static Expression Parameter(object? value, Type type)
    {
        var boxType = typeof(StrongBox<>).MakeGenericType(type);
        var box = Activator.CreateInstance(boxType, ConvertValue(value, type));
        return Expression.Field(Expression.Constant(box), nameof(StrongBox<object>.Value));
    }

    private static Expression Build(DbContext context, ParameterExpression entity, IReadOnlyList<IForeignKey> path, int index, object?[] subjectKey)
    {
        var foreignKey = path[index];

        if (index == path.Count - 1)
            return KeyEquals(entity, foreignKey.Properties, subjectKey);

        // EXISTS (SELECT 1 FROM principal p WHERE p.key = e.fk AND <rest of path>)
        var principalType = foreignKey.PrincipalEntityType;
        var principal = Expression.Parameter(principalType.ClrType, "p" + index);

        Expression join = Expression.Constant(true);
        for (var i = 0; i < foreignKey.Properties.Count; i++)
        {
            var dependentProperty = foreignKey.Properties[i];
            var principalProperty = foreignKey.PrincipalKey.Properties[i];
            var left = Expression.Convert(PropertyAccess(principal, principalProperty), Nullable(dependentProperty.ClrType));
            var right = Expression.Convert(PropertyAccess(entity, dependentProperty), Nullable(dependentProperty.ClrType));
            join = And(join, Expression.Equal(left, right));
        }

        var rest = Build(context, principal, path, index + 1, subjectKey);
        var lambda = Expression.Lambda(And(join, rest), principal);
        var source = Set(context, principalType).Expression;

        return Expression.Call(QueryableAny.MakeGenericMethod(principalType.ClrType), source, Expression.Quote(lambda));
    }

    private static Expression KeyEquals(Expression entity, IReadOnlyList<IProperty> properties, object?[] values)
    {
        if (properties.Count != values.Length)
            throw new InvalidOperationException("The key has a different number of values to the foreign key it is compared with.");

        Expression result = Expression.Constant(true);
        for (var i = 0; i < properties.Count; i++)
        {
            var type = Nullable(properties[i].ClrType);
            result = And(result, Expression.Equal(Expression.Convert(PropertyAccess(entity, properties[i]), type), Parameter(values[i], type)));
        }

        return result;
    }

    private static Expression And(Expression left, Expression right)
        => left is ConstantExpression { Value: true } ? right : Expression.AndAlso(left, right);

    private static Type Nullable(Type type)
        => type.IsValueType && System.Nullable.GetUnderlyingType(type) == null ? typeof(Nullable<>).MakeGenericType(type) : type;

    private static object? ConvertValue(object? value, Type type)
    {
        if (value == null)
            return null;

        var target = System.Nullable.GetUnderlyingType(type) ?? type;
        return target.IsInstanceOfType(value) ? value : Convert.ChangeType(value, target, System.Globalization.CultureInfo.InvariantCulture);
    }
}
