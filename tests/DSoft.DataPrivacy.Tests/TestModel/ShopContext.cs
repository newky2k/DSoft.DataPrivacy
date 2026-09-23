using System;
using System.Collections.Generic;
using DSoft.DataPrivacy.EntityFrameworkCore.Auditing;
using DSoft.DataPrivacy.EntityFrameworkCore.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DSoft.DataPrivacy.Tests.TestModel;

[DataSubject]
[PersonalDataEntity(DataClass = "Customers", Description = "People who buy from the shop")]
public class Customer
{
    public int Id { get; set; }

    [PersonalData(PersonalDataCategory.DirectIdentifier)]
    public string Name { get; set; } = string.Empty;

    [PersonalData(PersonalDataCategory.DirectIdentifier | PersonalDataCategory.Contact, Anonymisation = AnonymisationMethod.Hash)]
    public string Email { get; set; } = string.Empty;

    [PersonalData(PersonalDataCategory.DirectIdentifier | PersonalDataCategory.Child)]
    public DateTime? DateOfBirth { get; set; }

    [PersonalData(PersonalDataCategory.FreeText)]
    public string? Notes { get; set; }

    [PersonalData(PersonalDataCategory.Credential)]
    public string? PasswordHash { get; set; }

    public Address? HomeAddress { get; set; }

    [NotPersonalData("A loyalty tier code")]
    public string Tier { get; set; } = "Bronze";

    [RetentionTrigger(RetentionTrigger.LastActivity)]
    [NotPersonalData]
    public DateTime LastActiveAt { get; set; }

    [AnonymisedAt]
    [NotPersonalData]
    public DateTime? AnonymisedAt { get; set; }

    public List<Order> Orders { get; set; } = new();
}

public class Address
{
    [PersonalData(PersonalDataCategory.Contact)]
    public string Line1 { get; set; } = string.Empty;

    [PersonalData(PersonalDataCategory.Contact | PersonalDataCategory.IndirectIdentifier)]
    public string Postcode { get; set; } = string.Empty;
}

// Kept for the accounting records, without the customer's details.
[PersonalDataEntity(DataClass = "Sales", Erasure = ErasureAction.Anonymise)]
public class Order
{
    public int Id { get; set; }

    [DataSubjectKey]
    public int CustomerId { get; set; }

    public Customer? Customer { get; set; }

    [PersonalData(PersonalDataCategory.DirectIdentifier)]
    public string DeliveryName { get; set; } = string.Empty;

    public decimal Total { get; set; }

    [RetentionTrigger]
    [NotPersonalData]
    public DateTime PlacedAt { get; set; }

    public List<OrderLine> Lines { get; set; } = new();
}

public class OrderLine
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public string Product { get; set; } = string.Empty;

    public int Quantity { get; set; }
}

[PersonalDataEntity(DataClass = "Marketing")]
public class MarketingConsent
{
    public int Id { get; set; }

    public int CustomerId { get; set; }

    [NotPersonalData]
    public string Channel { get; set; } = string.Empty;

    public string? IpAddress { get; set; }

    [RetentionTrigger]
    [NotPersonalData]
    public DateTime GivenAt { get; set; }
}

[RetainOnErasure(RetentionGround.HealthOrSocialCare, Reason = "Health records must be kept for eight years")]
[PersonalDataEntity(DataClass = "Clinical")]
public class ClinicalNote
{
    public int Id { get; set; }

    [DataSubjectKey]
    public int CustomerId { get; set; }

    [PersonalData(PersonalDataCategory.Health | PersonalDataCategory.FreeText)]
    public string Text { get; set; } = string.Empty;

    [PersonalData(PersonalDataCategory.AuditCopy)]
    public string AuthorName { get; set; } = string.Empty;

    [RetentionTrigger]
    [NotPersonalData]
    public DateTime WrittenAt { get; set; }
}

public class SupportTicket
{
    public int Id { get; set; }

    // Optional and set to null on delete: the ticket only mentions who raised it.
    public int? RaisedById { get; set; }

    [PersonalData(PersonalDataCategory.FreeText)]
    public string Description { get; set; } = string.Empty;
}

// Personal data with no relationship to a customer.
public class AuditEntry
{
    public int Id { get; set; }

    [PersonalData(PersonalDataCategory.Contact)]
    public string ActorEmail { get; set; } = string.Empty;
}

public sealed class ShopContext : DbContext
{
    public static readonly byte[] HashKey = new byte[32] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32 };

    public ShopContext(SqliteConnection connection, Action<DataPrivacyOptionsBuilder>? configure = null)
        : base(new DbContextOptionsBuilder<ShopContext>().UseSqlite(connection).UseDataPrivacy(privacy =>
        {
            privacy.UseHashKey(HashKey);
            configure?.Invoke(privacy);
        }).Options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    public DbSet<MarketingConsent> Consents => Set<MarketingConsent>();

    public DbSet<ClinicalNote> ClinicalNotes => Set<ClinicalNote>();

    public DbSet<SupportTicket> Tickets => Set<SupportTicket>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().OwnsOne(c => c.HomeAddress);

        // Orders are kept for accounting, so a customer with orders cannot be deleted.
        modelBuilder.Entity<Order>()
            .HasOne(o => o.Customer).WithMany(c => c.Orders)
            .HasForeignKey(o => o.CustomerId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<OrderLine>()
            .OnErasure(ErasureAction.Anonymise)
            .HasOne<Order>().WithMany(o => o.Lines).HasForeignKey(l => l.OrderId);

        modelBuilder.Entity<OrderLine>().Property(l => l.Product).IsNotPersonalData();

        modelBuilder.Entity<MarketingConsent>()
            .HasOne<Customer>().WithMany().HasForeignKey(c => c.CustomerId);

        // Configured fluently rather than with an attribute.
        modelBuilder.Entity<MarketingConsent>()
            .Property(c => c.IpAddress).IsPersonalData(PersonalDataCategory.OnlineIdentifier);

        modelBuilder.Entity<ClinicalNote>()
            .HasOne<Customer>().WithMany().HasForeignKey(n => n.CustomerId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<SupportTicket>()
            .HasOne<Customer>().WithMany().HasForeignKey(t => t.RaisedById).OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>An open in-memory SQLite database with the schema created.</summary>
public sealed class ShopDatabase : IDisposable
{
    public ShopDatabase()
    {
        Connection = new SqliteConnection("DataSource=:memory:");
        Connection.Open();
        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public SqliteConnection Connection { get; }

    public ShopContext CreateContext(Action<DataPrivacyOptionsBuilder>? configure = null) => new(Connection, configure);

    public void Dispose() => Connection.Dispose();
}

public sealed class RecordingObserver : IPersonalDataChangeObserver
{
    public List<PersonalDataChange> Changes { get; } = new();

    public System.Threading.Tasks.ValueTask OnChangesSavedAsync(DbContext context, IReadOnlyList<PersonalDataChange> changes, System.Threading.CancellationToken cancellationToken)
    {
        Changes.AddRange(changes);
        return default;
    }
}
