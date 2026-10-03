using System.Linq;
using System.Threading.Tasks;
using DSoft.DataPrivacy.EntityFrameworkCore.Export;
using DSoft.DataPrivacy.Tests.TestModel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DSoft.DataPrivacy.Tests;

public sealed class ExportTests : System.IDisposable
{
    private readonly ShopDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Export_holds_the_subject_and_everything_that_belongs_to_them()
    {
        int id;
        using (var seed = _database.CreateContext())
            id = Seed.FullCustomer(seed).Id;

        using var context = _database.CreateContext();
        var export = (await context.PersonalData().ExportAsync<Customer>(id))!;

        Assert.Equal("Customer", export.Subject);
        Assert.Equal("(data subject)", export.Sections[0].Relationship);
        Assert.Contains(export.Sections, s => s.Entity == "Order" && s.Records.Count == 1);
        Assert.Contains(export.Sections, s => s.Entity == "OrderLine" && s.Records.Count == 1);
        Assert.Contains(export.Sections, s => s.Entity == "MarketingConsent");
        Assert.Contains(export.Sections, s => s.Entity == "ClinicalNote");
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Credentials_are_withheld_and_free_text_is_flagged()
    {
        int id;
        using (var seed = _database.CreateContext())
            id = Seed.FullCustomer(seed).Id;

        using var context = _database.CreateContext();
        var export = (await context.PersonalData().ExportAsync<Customer>(id))!;
        var fields = export.Sections[0].Records.Single().Fields;

        var password = fields.Single(f => f.Name == "PasswordHash");
        Assert.True(password.Withheld);
        Assert.Null(password.Value);

        Assert.True(fields.Single(f => f.Name == "Notes").NeedsReview);
        Assert.Equal("AB1 2CD", fields.Single(f => f.Name == "HomeAddress.Postcode").Value);
        Assert.DoesNotContain(fields, f => f.Name == "Tier"); // not personal data
        Assert.True(export.RequiresReview);
    }

    [Fact]
    public async Task Records_that_only_mention_the_person_are_listed_by_key()
    {
        int id;
        using (var seed = _database.CreateContext())
            id = Seed.FullCustomer(seed).Id;

        using var context = _database.CreateContext();
        var keysOnly = (await context.PersonalData().ExportAsync<Customer>(id))!;
        var detailed = (await context.PersonalData().ExportAsync<Customer>(id, new DataSubjectExportOptions { ReferenceDetail = ReferenceDetail.ClassifiedFields }))!;

        var ticket = keysOnly.Sections.Single(s => s.Entity == "SupportTicket");
        Assert.Equal(DataSubjectLinkKind.Reference, ticket.Kind);
        Assert.Empty(ticket.Records.Single().Fields);

        Assert.Equal("Kettle arrived broken", detailed.Sections.Single(s => s.Entity == "SupportTicket").Records.Single().Fields.Single().Value);
    }

    [Fact]
    public async Task Other_peoples_records_are_not_included()
    {
        int id;
        using (var seed = _database.CreateContext())
        {
            id = Seed.FullCustomer(seed).Id;
            Seed.FullCustomer(seed, "Someone Else", "else@example.com");
        }

        using var context = _database.CreateContext();
        var json = (await context.PersonalData().ExportAsync<Customer>(id))!.ToJson();

        Assert.Contains("jo@example.com", json);
        Assert.DoesNotContain("else@example.com", json);
        Assert.DoesNotContain("Someone Else", json);
    }

    [Fact]
    public async Task Excluded_types_are_never_queried()
    {
        int id;
        using (var seed = _database.CreateContext())
        {
            id = Seed.FullCustomer(seed).Id;

            // With no table, any query for the excluded records would fail.
            seed.Database.ExecuteSqlRaw("DROP TABLE ClinicalNotes");
        }

        using var context = _database.CreateContext();
        var options = new DataSubjectExportOptions { ExcludedTypes = { typeof(ClinicalNote) } };
        var export = (await context.PersonalData().ExportAsync<Customer>(id, options))!;

        Assert.DoesNotContain(export.Sections, s => s.Entity == "ClinicalNote");
        Assert.Equal(new[] { "ClinicalNote" }, export.ExcludedEntities);
        Assert.Contains(export.Sections, s => s.Entity == "Order");
        Assert.DoesNotContain("penicillin", export.ToJson());
    }

    [Fact]
    public async Task Entities_can_be_excluded_by_data_class()
    {
        int id;
        using (var seed = _database.CreateContext())
        {
            id = Seed.FullCustomer(seed).Id;
            seed.Database.ExecuteSqlRaw("DROP TABLE ClinicalNotes");
        }

        using var context = _database.CreateContext();
        var options = new DataSubjectExportOptions { Exclude = entity => entity.DataClass == "Clinical" };
        var export = (await context.PersonalData().ExportAsync<Customer>(id, options))!;

        Assert.Equal(new[] { "ClinicalNote" }, export.ExcludedEntities);
        Assert.Contains(export.Sections, s => s.Entity == "MarketingConsent");
    }

    [Fact]
    public async Task Records_reached_through_an_excluded_entity_are_excluded_too()
    {
        int id;
        using (var seed = _database.CreateContext())
        {
            id = Seed.FullCustomer(seed).Id;
            seed.Database.ExecuteSqlRaw("DROP TABLE OrderLines");
            seed.Database.ExecuteSqlRaw("DROP TABLE Orders");
        }

        using var context = _database.CreateContext();
        var options = new DataSubjectExportOptions { ExcludedTypes = { typeof(Order) } };
        var export = (await context.PersonalData().ExportAsync<Customer>(id, options))!;

        Assert.Equal(new[] { "Order", "OrderLine" }, export.ExcludedEntities.OrderBy(e => e));
        Assert.DoesNotContain(export.Sections, s => s.Entity is "Order" or "OrderLine");
        Assert.Contains(export.Sections, s => s.Entity == "ClinicalNote");
    }

    [Fact]
    public async Task The_data_subject_is_exported_even_when_the_exclusion_matches_it()
    {
        int id;
        using (var seed = _database.CreateContext())
            id = Seed.FullCustomer(seed).Id;

        using var context = _database.CreateContext();
        var export = (await context.PersonalData().ExportAsync<Customer>(id, new DataSubjectExportOptions { Exclude = _ => true }))!;

        Assert.Equal("Customer", Assert.Single(export.Sections).Entity);
        Assert.DoesNotContain("Customer", export.ExcludedEntities);
        Assert.Contains("ClinicalNote", export.ExcludedEntities);
    }

    [Fact]
    public async Task A_derived_type_excluded_on_its_own_is_left_out_of_its_base_types_records()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var context = new NotesContext(connection);
        context.Database.EnsureCreated();
        context.Add(new Author { Id = 1, Name = "Jo Bloggs" });
        context.Add(new Note { AuthorId = 1, Text = "Ordinary" });
        context.Add(new SealedNote { AuthorId = 1, Text = "Locked away" });
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var options = new DataSubjectExportOptions { ExcludedTypes = { typeof(SealedNote) } };
        var export = (await context.PersonalData().ExportAsync<Author>(1, options))!;

        var notes = export.Sections.Single(s => s.Entity == "Note");
        Assert.Equal("Ordinary", notes.Records.Single().Fields.Single(f => f.Name == "Text").Value);
        Assert.DoesNotContain("Locked away", export.ToJson());
        Assert.Equal(new[] { "SealedNote" }, export.ExcludedEntities);
    }

    [Fact]
    public async Task An_unknown_subject_gives_no_export()
    {
        using var context = _database.CreateContext();

        Assert.Null(await context.PersonalData().ExportAsync<Customer>(12345));
    }

    [DataSubject]
    public class Author
    {
        public int Id { get; set; }

        [PersonalData(PersonalDataCategory.DirectIdentifier)]
        public string Name { get; set; } = string.Empty;
    }

    public class Note
    {
        public int Id { get; set; }

        [DataSubjectKey]
        public int AuthorId { get; set; }

        [PersonalData(PersonalDataCategory.FreeText)]
        public string Text { get; set; } = string.Empty;
    }

    public class SealedNote : Note
    {
    }

    private sealed class NotesContext : DbContext
    {
        public NotesContext(Microsoft.Data.Sqlite.SqliteConnection connection)
            : base(new DbContextOptionsBuilder<NotesContext>().UseSqlite(connection).UseDataPrivacy().Options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Author>();
            modelBuilder.Entity<Note>().HasOne<Author>().WithMany().HasForeignKey(n => n.AuthorId);
            modelBuilder.Entity<SealedNote>();
        }
    }
}
