using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DSoft.DataPrivacy.EntityFrameworkCore.Metadata;

/// <summary>One classified property in a data inventory.</summary>
public sealed record PersonalDataInventoryItem
{
    /// <summary>The entity.</summary>
    public string Entity { get; init; } = string.Empty;

    /// <summary>The table, when known.</summary>
    public string? Table { get; init; }

    /// <summary>The property, prefixed by owned navigations.</summary>
    public string Property { get; init; } = string.Empty;

    /// <summary>The column, when known.</summary>
    public string? Column { get; init; }

    /// <summary>The CLR type of the value.</summary>
    public string ValueType { get; init; } = string.Empty;

    /// <summary>What kind of personal data it is.</summary>
    public PersonalDataCategory Categories { get; init; }

    /// <summary>True for Article 9 special category data.</summary>
    public bool SpecialCategory => Categories.IsSpecialCategory();

    /// <summary>The property's data class, or its entity's.</summary>
    public string? DataClass { get; init; }

    /// <summary>True when the entity is a data subject.</summary>
    public bool IsDataSubject { get; init; }

    /// <summary>How records reach a data subject, one path per line.</summary>
    public string LinkedBy { get; init; } = string.Empty;

    /// <summary>What an erasure does to the record.</summary>
    public string OnErasure { get; init; } = string.Empty;

    /// <summary>The property's description, or its entity's.</summary>
    public string? Description { get; init; }
}

/// <summary>
/// Every classified property in a model. Feed it to the record of processing activities (Article 30), a DPIA,
/// or a report of where personal data lives.
/// </summary>
public sealed class PersonalDataInventory
{
    private PersonalDataInventory(IReadOnlyList<PersonalDataInventoryItem> items)
    {
        Items = items;
    }

    /// <summary>The rows, ordered by entity then property.</summary>
    public IReadOnlyList<PersonalDataInventoryItem> Items { get; }

    internal static PersonalDataInventory Create(PersonalDataModel model)
    {
        var items = new List<PersonalDataInventoryItem>();

        foreach (var entity in model.Entities.Where(e => e.Properties.Count > 0))
        {
            var linkedBy = entity.IsDataSubject
                ? "(data subject)"
                : entity.Links.Count == 0 ? "(not linked)" : string.Join("; ", entity.Links.Select(l => l.ToString()));

            var onErasure = entity.Erasure switch
            {
                ErasureAction.Retain => $"Retain ({entity.Exemption})",
                ErasureAction.Anonymise => "Anonymise",
                ErasureAction.Delete => "Delete",
                _ => entity.IsDataSubject ? "Delete, or anonymise if records depend on it" : "Delete",
            };

            foreach (var property in entity.Properties.OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                items.Add(new PersonalDataInventoryItem
                {
                    Entity = entity.Name,
                    Table = entity.Table,
                    Property = property.Name,
                    Column = property.Property.FindAnnotation("Relational:ColumnName")?.Value as string ?? property.Property.Name,
                    ValueType = (Nullable.GetUnderlyingType(property.Property.ClrType) ?? property.Property.ClrType).Name,
                    Categories = property.Categories,
                    DataClass = property.Classification.DataClass ?? entity.DataClass,
                    IsDataSubject = entity.IsDataSubject,
                    LinkedBy = linkedBy,
                    OnErasure = onErasure,
                    Description = property.Classification.Description ?? entity.Description,
                });
            }
        }

        return new PersonalDataInventory(items);
    }

    /// <summary>The inventory as CSV with a header row.</summary>
    public string ToCsv()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Entity,Table,Property,Column,Type,Categories,SpecialCategory,DataClass,DataSubject,LinkedBy,OnErasure,Description");

        foreach (var item in Items)
        {
            builder.AppendLine(string.Join(",", new[]
            {
                Csv(item.Entity), Csv(item.Table), Csv(item.Property), Csv(item.Column), Csv(item.ValueType),
                Csv(item.Categories.Describe()), item.SpecialCategory ? "true" : "false", Csv(item.DataClass),
                item.IsDataSubject ? "true" : "false", Csv(item.LinkedBy), Csv(item.OnErasure), Csv(item.Description),
            }));
        }

        return builder.ToString();
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        // Neutralise spreadsheet formulas as well as quoting.
        var text = value!;
        if (text[0] is '=' or '+' or '-' or '@')
            text = "'" + text;

        return text.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }
}
