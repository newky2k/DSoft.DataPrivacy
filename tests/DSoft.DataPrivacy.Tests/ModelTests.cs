using System.Linq;
using DSoft.DataPrivacy.EntityFrameworkCore.Metadata;
using DSoft.DataPrivacy.Tests.TestModel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DSoft.DataPrivacy.Tests;

public sealed class ModelTests : IClassFixture<ModelTests.Database>
{
    private readonly ShopContext _context;

    public ModelTests(Database database)
    {
        _context = database.CreateContext();
    }

    public sealed class Database : System.IDisposable
    {
        private readonly ShopDatabase _database = new();

        public ShopContext CreateContext() => _database.CreateContext();

        public void Dispose() => _database.Dispose();
    }

    private PersonalDataModel Model => _context.PersonalData().Model;

    [Fact]
    public void Attributes_become_the_classification()
    {
        var customer = Model.Find(typeof(Customer))!;

        Assert.True(customer.IsDataSubject);
        Assert.Equal("Customers", customer.DataClass);
        Assert.Equal(PersonalDataCategory.DirectIdentifier | PersonalDataCategory.Contact, customer.Properties.Single(p => p.Name == "Email").Categories);
        Assert.Equal(AnonymisationMethod.Hash, customer.Properties.Single(p => p.Name == "Email").Classification.Anonymisation);
        Assert.Contains(customer.NotPersonalData, p => p.Name == "Tier");
        Assert.Equal("LastActiveAt", customer.RetentionTriggers[RetentionTrigger.LastActivity].Name);
        Assert.Equal("AnonymisedAt", customer.AnonymisedAt!.Name);
    }

    [Fact]
    public void Owned_type_properties_belong_to_their_owner()
    {
        var customer = Model.Find(typeof(Customer))!;

        Assert.Contains(customer.Properties, p => p.Name == "HomeAddress.Postcode"
            && p.Categories == (PersonalDataCategory.Contact | PersonalDataCategory.IndirectIdentifier));
    }

    [Fact]
    public void Fluent_configuration_is_read()
    {
        var consent = Model.Find(typeof(MarketingConsent))!;
        var line = Model.Find(typeof(OrderLine))!;

        Assert.Equal(PersonalDataCategory.OnlineIdentifier, consent.Properties.Single(p => p.Name == "IpAddress").Categories);
        Assert.Equal(ClassificationSource.Explicit, consent.Properties.Single().Classification.Source);
        Assert.Equal(ErasureAction.Anonymise, line.Erasure);
        Assert.Contains(line.NotPersonalData, p => p.Name == "Product");
    }

    [Fact]
    public void Retained_entities_carry_their_exemption()
    {
        var note = Model.Find(typeof(ClinicalNote))!;

        Assert.Equal(ErasureAction.Retain, note.Erasure);
        Assert.Equal(ErasureExemption.PublicHealth, note.Exemption);
        Assert.Equal("Health records must be kept for eight years", note.ExemptionReason);
    }

    [Fact]
    public void Links_are_declared_or_inferred_from_the_relationship()
    {
        Assert.Equal(DataSubjectLinkKind.Owner, Model.Find(typeof(Order))!.Links.Single().Kind);          // declared, despite Restrict
        Assert.Equal(DataSubjectLinkKind.Owner, Model.Find(typeof(MarketingConsent))!.Links.Single().Kind); // required + cascade
        Assert.Equal(DataSubjectLinkKind.Reference, Model.Find(typeof(SupportTicket))!.Links.Single().Kind); // optional
    }

    [Fact]
    public void Links_follow_several_relationships()
    {
        var link = Model.Find(typeof(OrderLine))!.Links.Single();

        Assert.Equal(2, link.Path.Count);
        Assert.Equal(DataSubjectLinkKind.Owner, link.Kind);
        Assert.Equal("OrderLine.OrderId → Order.CustomerId → Customer", link.Describe());
    }

    [Fact]
    public void Personal_data_with_no_route_to_a_person_is_reported()
    {
        Assert.Equal(new[] { "AuditEntry" }, Model.Unlinked.Select(e => e.Name));

        var issues = _context.PersonalData().Validate();
        Assert.Contains(issues, i => i.Entity == "AuditEntry" && i.Severity == PersonalDataIssueSeverity.Warning);
        Assert.DoesNotContain(issues, i => i.Severity == PersonalDataIssueSeverity.Error);
    }

    [Fact]
    public void Unclassified_candidate_properties_are_found()
    {
        var gaps = Model.FindUnclassified();

        // OrderLine.Product is explicitly not personal data; nothing else in the test model is left undecided.
        Assert.Empty(gaps);
    }

    [Fact]
    public void Inventory_lists_every_classified_property()
    {
        var inventory = Model.Inventory();
        var csv = inventory.ToCsv();

        Assert.Contains(inventory.Items, i => i.Entity == "ClinicalNote" && i.Property == "Text" && i.SpecialCategory && i.OnErasure == "Retain (PublicHealth)");
        Assert.Contains(inventory.Items, i => i.Entity == "Customer" && i.Property == "HomeAddress.Line1" && i.Table == "Customers");
        Assert.StartsWith("Entity,Table,Property", csv);
    }

    [Fact]
    public void A_model_without_the_conventions_is_refused()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        using var plain = new PlainContext(new DbContextOptionsBuilder<PlainContext>().UseSqlite(connection).Options);

        var error = Assert.Throws<System.InvalidOperationException>(() => plain.PersonalData());
        Assert.Contains("UseDataPrivacy", error.Message);
    }

    private sealed class PlainContext : DbContext
    {
        public PlainContext(DbContextOptions<PlainContext> options)
            : base(options)
        {
        }

        public DbSet<AuditEntry> Entries => Set<AuditEntry>();
    }
}
