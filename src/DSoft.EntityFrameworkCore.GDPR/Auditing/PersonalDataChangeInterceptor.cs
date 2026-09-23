using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DSoft.EntityFrameworkCore.GDPR.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DSoft.EntityFrameworkCore.GDPR.Auditing;

/// <summary>A classified property that changed.</summary>
/// <param name="Name">The property name.</param>
/// <param name="Categories">What kind of personal data it holds.</param>
public sealed record PersonalDataChangedProperty(string Name, PersonalDataCategory Categories);

/// <summary>
/// Personal data added, changed or deleted on one record. It says which values changed, never what they were
/// or became, so it can be kept as a rectification log or audit trail without copying the data.
/// </summary>
public sealed record PersonalDataChange
{
    /// <summary>The entity.</summary>
    public string Entity { get; init; } = string.Empty;

    /// <summary>The record's key, read after saving so generated keys are known.</summary>
    public IReadOnlyDictionary<string, object?> Key { get; init; } = new Dictionary<string, object?>();

    /// <summary><see cref="EntityState.Added"/>, <see cref="EntityState.Modified"/> or <see cref="EntityState.Deleted"/>.</summary>
    public EntityState State { get; init; }

    /// <summary>The classified properties that changed.</summary>
    public IReadOnlyList<PersonalDataChangedProperty> Properties { get; init; } = Array.Empty<PersonalDataChangedProperty>();

    /// <summary>When the change was saved.</summary>
    public DateTimeOffset At { get; init; }
}

/// <summary>Receives the personal data changes made by each successful save.</summary>
public interface IPersonalDataChangeObserver
{
    /// <summary>Called after changes are saved. Throwing does not undo the save.</summary>
    ValueTask OnChangesSavedAsync(DbContext context, IReadOnlyList<PersonalDataChange> changes, CancellationToken cancellationToken);
}

/// <summary>Collects personal data changes as they are saved and passes them to the configured observers.</summary>
internal sealed class PersonalDataChangeInterceptor : SaveChangesInterceptor
{
    private readonly IReadOnlyList<IPersonalDataChangeObserver> _observers;
    private readonly ConditionalWeakTable<DbContext, List<Pending>> _pending = new();

    public PersonalDataChangeInterceptor(IReadOnlyList<IPersonalDataChangeObserver> observers)
    {
        _observers = observers.ToList();
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return new ValueTask<InterceptionResult<int>>(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Publish(eventData.Context, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        await Publish(eventData.Context, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Context != null)
            _pending.Remove(eventData.Context);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        SaveChangesFailed(eventData);
        return Task.CompletedTask;
    }

    private void Capture(DbContext? context)
    {
        if (context == null)
            return;

        var pending = new List<Pending>();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            var properties = new List<PersonalDataChangedProperty>();
            foreach (var property in entry.Properties)
            {
                var classification = PersonalDataModel.ReadClassification(property.Metadata);
                if (classification == null)
                    continue;

                var changed = entry.State switch
                {
                    EntityState.Modified => property.IsModified,
                    EntityState.Added => property.CurrentValue != null,
                    _ => property.OriginalValue != null,
                };

                if (changed)
                    properties.Add(new PersonalDataChangedProperty(property.Metadata.Name, classification.Categories));
            }

            if (properties.Count > 0)
                pending.Add(new Pending(entry, entry.State, properties));
        }

        _pending.AddOrUpdate(context, pending);
    }

    private async ValueTask Publish(DbContext? context, CancellationToken cancellationToken)
    {
        if (context == null || !_pending.TryGetValue(context, out var pending))
            return;

        _pending.Remove(context);
        if (pending.Count == 0)
            return;

        var at = DateTimeOffset.UtcNow;
        var changes = pending.Select(p => new PersonalDataChange
        {
            Entity = p.Entry.Metadata.ShortName(),
            Key = p.Entry.Metadata.FindPrimaryKey()?.Properties.ToDictionary(k => k.Name, k => p.Entry.Property(k.Name).CurrentValue)
                ?? new Dictionary<string, object?>(),
            State = p.State,
            Properties = p.Properties,
            At = at,
        }).ToList();

        foreach (var observer in _observers)
            await observer.OnChangesSavedAsync(context, changes, cancellationToken).ConfigureAwait(false);
    }

    private sealed record Pending(EntityEntry Entry, EntityState State, IReadOnlyList<PersonalDataChangedProperty> Properties);
}
