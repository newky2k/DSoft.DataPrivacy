using System.Linq;
using System.Threading.Tasks;
using DSoft.EntityFrameworkCore.GDPR.Export;
using DSoft.EntityFrameworkCore.GDPR.Tests.TestModel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DSoft.EntityFrameworkCore.GDPR.Tests;

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
    public async Task An_unknown_subject_gives_no_export()
    {
        using var context = _database.CreateContext();

        Assert.Null(await context.PersonalData().ExportAsync<Customer>(12345));
    }
}
