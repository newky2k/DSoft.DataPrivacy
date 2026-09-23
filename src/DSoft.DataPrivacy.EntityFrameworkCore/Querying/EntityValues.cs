using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DSoft.DataPrivacy.EntityFrameworkCore.Querying;

/// <summary>Reads values from entity instances, tracked or not.</summary>
internal static class EntityValues
{
    /// <summary>Reads a mapped property. Shadow properties can only be read from a tracked entity.</summary>
    public static bool TryGet(DbContext context, object entity, IPropertyBase property, out object? value)
    {
        if (property.IsIndexerProperty())
        {
            // Property bags, such as many-to-many join entities, hold their values in an indexer.
            value = property.PropertyInfo!.GetValue(entity, new object[] { property.Name });
            return true;
        }

        if (property.PropertyInfo != null && property.PropertyInfo.DeclaringType!.IsInstanceOfType(entity))
        {
            value = property.PropertyInfo.GetValue(entity);
            return true;
        }

        if (property.FieldInfo != null && property.FieldInfo.DeclaringType!.IsInstanceOfType(entity))
        {
            value = property.FieldInfo.GetValue(entity);
            return true;
        }

        var entry = context.Entry(entity);
        if (entry.State != EntityState.Detached && property is IProperty scalar)
        {
            value = entry.Property(scalar.Name).CurrentValue;
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>Reads the values of <paramref name="properties"/>, failing when one cannot be read.</summary>
    public static object?[] GetKey(DbContext context, object entity, IReadOnlyList<IProperty> properties)
    {
        var values = new object?[properties.Count];
        for (var i = 0; i < properties.Count; i++)
        {
            if (!TryGet(context, entity, properties[i], out values[i]))
                throw new NotSupportedException($"The key property '{properties[i].DeclaringType.DisplayName()}.{properties[i].Name}' cannot be read.");
        }

        return values;
    }

    /// <summary>The primary key as a name-to-value map.</summary>
    public static IReadOnlyDictionary<string, object?> KeyMap(DbContext context, object entity, IEntityType entityType)
    {
        var key = entityType.FindPrimaryKey();
        if (key == null)
            return new Dictionary<string, object?>();

        var values = GetKey(context, entity, key.Properties);
        return key.Properties.Select((p, i) => (p.Name, values[i])).ToDictionary(x => x.Name, x => x.Item2);
    }

    /// <summary>Loads one entity by primary key.</summary>
    public static async Task<object?> FindAsync(DbContext context, IEntityType entityType, object?[] keyValues, bool tracking, CancellationToken cancellationToken)
    {
        var key = entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException($"'{entityType.DisplayName()}' has no primary key.");
        if (key.Properties.Count != keyValues.Length)
            throw new ArgumentException($"'{entityType.DisplayName()}' has a key of {key.Properties.Count} values, but {keyValues.Length} were given.", nameof(keyValues));

        var parameter = System.Linq.Expressions.Expression.Parameter(entityType.ClrType, "e");
        System.Linq.Expressions.Expression predicate = System.Linq.Expressions.Expression.Constant(true);
        for (var i = 0; i < key.Properties.Count; i++)
        {
            var type = key.Properties[i].ClrType;
            var equal = System.Linq.Expressions.Expression.Equal(SubjectQuery.PropertyAccess(parameter, key.Properties[i]), SubjectQuery.Parameter(keyValues[i], type));
            predicate = i == 0 ? equal : System.Linq.Expressions.Expression.AndAlso(predicate, equal);
        }

        var rows = await SubjectQuery.LoadAsync(context, entityType, System.Linq.Expressions.Expression.Lambda(predicate, parameter), tracking, cancellationToken).ConfigureAwait(false);
        return rows.FirstOrDefault();
    }

    /// <summary>The entity type of a loaded row: its runtime type, or <paramref name="hint"/> for shared-type entities.</summary>
    public static IEntityType ResolveType(DbContext context, object row, IEntityType hint)
        => hint.HasSharedClrType ? hint : context.Model.FindRuntimeEntityType(row.GetType()) ?? hint;

    /// <summary>A short, value-free rendering of a key for logs, such as <c>42</c> or <c>(7, 3)</c>.</summary>
    public static string FormatKey(IReadOnlyDictionary<string, object?> key)
    {
        var values = key.Values.Select(v => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "null").ToList();
        return values.Count == 1 ? values[0] : "(" + string.Join(", ", values) + ")";
    }
}
