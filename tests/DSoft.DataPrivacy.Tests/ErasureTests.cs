using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSoft.DataPrivacy.EntityFrameworkCore.Erasure;
using DSoft.DataPrivacy.EntityFrameworkCore.Metadata;
using DSoft.DataPrivacy.Regimes;
using DSoft.DataPrivacy.Rules;
using DSoft.DataPrivacy.Tests.TestModel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DSoft.DataPrivacy.Tests;

public sealed class ErasureTests : System.IDisposable
{
    private readonly ShopDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task A_customer_with_retained_records_is_anonymised_in_place()
    {
        int id;
        using (var seed = _database.CreateContext())
            id = Seed.FullCustomer(seed).Id;

        ErasureResult result;
        using (var context = _database.CreateContext())
            result = await context.PersonalData().EraseAsync<Customer>(id);

        using var check = _database.CreateContext();
        var customer = check.Customers.Single(c => c.Id == id);

        Assert.Equal("[erased]", customer.Name);
        Assert.Equal(new PersonalDataHasher(ShopContext.HashKey).Hash("jo@example.com"), customer.Email);
        Assert.Null(customer.DateOfBirth);
        Assert.Null(customer.Notes);
        Assert.Null(customer.PasswordHash);
        Assert.Equal("[erased]", customer.HomeAddress!.Postcode);
        Assert.Equal("Bronze", customer.Tier);
        Assert.NotNull(customer.AnonymisedAt);

        // Consent belongs to the person and nothing depends on it.
        Assert.Empty(check.Consents.Where(c => c.CustomerId == id));

        // The order is kept for accounting without the name on it.
        var order = check.Orders.Include(o => o.Lines).Single(o => o.CustomerId == id);
        Assert.Equal("[erased]", order.DeliveryName);
        Assert.Equal(42.50m, order.Total);
        Assert.Single(order.Lines);

        // The clinical note is kept whole, under its exemption.
        var note = check.ClinicalNotes.Single(n => n.CustomerId == id);
        Assert.Equal("Allergic to penicillin", note.Text);
        Assert.Contains(result.Entries, e => e.Entity == "ClinicalNote" && e.Action == ErasureAction.Retain && e.RetentionGround == RetentionGround.HealthOrSocialCare);

        // The ticket only mentions the customer, who still exists, so it is left alone.
        Assert.Equal(id, check.Tickets.Single().RaisedById);

        Assert.Contains(result.Entries, e => e.Entity == "Customer" && e.Action == ErasureAction.Anonymise);
    }

    [Fact]
    public async Task A_customer_nothing_depends_on_is_deleted()
    {
        int id;
        using (var seed = _database.CreateContext())
            id = Seed.LightCustomer(seed).Id;

        ErasureResult result;
        using (var context = _database.CreateContext())
            result = await context.PersonalData().EraseAsync<Customer>(id);

        using var check = _database.CreateContext();
        Assert.False(check.Customers.Any(c => c.Id == id));
        Assert.Empty(check.Consents);
        Assert.Null(check.Tickets.Single().RaisedById);
        Assert.Equal(2, result.Deleted);
    }

    [Fact]
    public async Task The_log_holds_no_personal_values()
    {
        int id;
        using (var seed = _database.CreateContext())
            id = Seed.FullCustomer(seed).Id;

        using var context = _database.CreateContext();
        var result = await context.PersonalData().EraseAsync<Customer>(id);
        var log = string.Join("\n", result.Entries.Select(e => e.ToString()));

        foreach (var value in new[] { "Jo Bloggs", "jo@example.com", "AB1 2CD", "penicillin", "Dr Smith", "203.0.113.7" })
            Assert.DoesNotContain(value, log);
    }

    [Fact]
    public async Task A_dry_run_changes_nothing()
    {
        int id;
        using (var seed = _database.CreateContext())
            id = Seed.FullCustomer(seed).Id;

        ErasureResult result;
        using (var context = _database.CreateContext())
            result = await context.PersonalData().EraseAsync<Customer>(id, new ErasureOptions { DryRun = true });

        Assert.True(result.DryRun);
        Assert.Contains(result.Entries, e => e.Entity == "Customer" && e.Anonymised.Contains("Email"));

        using var check = _database.CreateContext();
        Assert.Equal("jo@example.com", check.Customers.Single(c => c.Id == id).Email);
        Assert.Single(check.Consents);
    }

    [Fact]
    public async Task A_legal_hold_keeps_everything()
    {
        int id;
        using (var seed = _database.CreateContext())
            id = Seed.LightCustomer(seed).Id;

        ErasureResult result;
        using (var context = _database.CreateContext())
            result = await context.PersonalData().EraseAsync<Customer>(id, new ErasureOptions { LegalHold = true });

        Assert.All(result.Entries, e => Assert.Equal(RetentionGround.LegalClaims, e.RetentionGround));

        using var check = _database.CreateContext();
        Assert.Equal("alex@example.com", check.Customers.Single(c => c.Id == id).Email);
    }

    [Fact]
    public async Task A_policy_can_retain_a_category_wherever_it_is_found()
    {
        int id;
        using (var seed = _database.CreateContext())
            id = Seed.LightCustomer(seed).Id;

        var policy = new ErasurePolicy().RetainCategory(PersonalDataCategory.OnlineIdentifier, RetentionGround.LegalObligation, "Fraud prevention records");

        using (var context = _database.CreateContext(privacy => privacy.UseErasurePolicy(policy)))
            await context.PersonalData().EraseAsync<Customer>(id);

        using var check = _database.CreateContext();
        Assert.Single(check.Consents);                           // kept: it holds an IP address
        Assert.True(check.Customers.Any(c => c.Id == id));       // kept, anonymised, because the consent depends on it
        Assert.Equal("[erased]", check.Customers.Single(c => c.Id == id).Name);
    }

    private static readonly RecordRetention PostalConsent = new(RetentionGround.LegalObligation, "Postal consents are kept for seven years");

    private static RecordRetention? RetainPostalConsent(PersonalDataEntity entity, object row)
        => row is MarketingConsent { Channel: "Post" } ? PostalConsent : null;

    /// <summary>A customer with a consent by text message and one by post, and nothing else that keeps them.</summary>
    private int SeedCustomerWithPostalConsent()
    {
        using var seed = _database.CreateContext();
        var id = Seed.LightCustomer(seed).Id;
        seed.Consents.Add(new MarketingConsent { CustomerId = id, Channel = "Post", IpAddress = "192.0.2.9", GivenAt = Seed.Now.AddMonths(-2) });
        seed.SaveChanges();
        return id;
    }

    [Fact]
    public async Task A_record_can_be_retained_on_its_own_ground()
    {
        var id = SeedCustomerWithPostalConsent();

        ErasureResult result;
        using (var context = _database.CreateContext(privacy => privacy.UseRegime(PrivacyRegimes.Gdpr)))
            result = await context.PersonalData().EraseAsync<Customer>(id, new ErasureOptions { RetainRecord = RetainPostalConsent });

        using var check = _database.CreateContext();

        // The postal consent is kept whole; the other consent, of the same type, is deleted.
        var kept = check.Consents.Single();
        Assert.Equal("Post", kept.Channel);
        Assert.Equal("192.0.2.9", kept.IpAddress);

        var entry = result.Entries.Single(e => e.Entity == "MarketingConsent" && e.Action == ErasureAction.Retain);
        Assert.Equal(RetentionGround.LegalObligation, entry.RetentionGround);
        Assert.Equal("Art 17(3)(b)", entry.Citation);
        Assert.Equal(PostalConsent.Reason, entry.Reason);
        Assert.Single(result.Entries, e => e.Entity == "MarketingConsent" && e.Action == ErasureAction.Delete);
        Assert.Equal(1, result.Retained);
    }

    [Fact]
    public async Task What_a_retained_record_depends_on_is_anonymised_not_deleted()
    {
        var id = SeedCustomerWithPostalConsent();

        ErasureResult result;
        using (var context = _database.CreateContext())
            result = await context.PersonalData().EraseAsync<Customer>(id, new ErasureOptions { RetainRecord = RetainPostalConsent });

        using var check = _database.CreateContext();
        var customer = check.Customers.Single(c => c.Id == id);
        Assert.Equal("[erased]", customer.Name);
        Assert.NotNull(customer.AnonymisedAt);
        Assert.Equal(id, check.Consents.Single().CustomerId);
        Assert.Contains(result.Entries, e => e.Entity == "Customer" && e.Action == ErasureAction.Anonymise);
    }

    [Fact]
    public async Task A_record_decision_that_retains_nothing_changes_nothing()
    {
        var id = SeedCustomerWithPostalConsent();

        var offered = new List<string>();
        ErasureResult result;
        using (var context = _database.CreateContext())
        {
            result = await context.PersonalData().EraseAsync<Customer>(id, new ErasureOptions
            {
                RetainRecord = (entity, _) =>
                {
                    offered.Add(entity.Name);
                    return null;
                },
            });
        }

        using var check = _database.CreateContext();
        Assert.False(check.Customers.Any(c => c.Id == id));
        Assert.Empty(check.Consents);
        Assert.Equal(3, result.Deleted);
        Assert.Equal(0, result.Retained);
        Assert.Equal(new[] { "Customer", "MarketingConsent", "MarketingConsent" }, offered.OrderBy(n => n));
    }

    [Fact]
    public async Task A_record_decision_leaves_a_retained_entity_as_it_is()
    {
        int id;
        using (var seed = _database.CreateContext())
            id = Seed.FullCustomer(seed).Id;

        var offered = new List<string>();
        ErasureResult result;
        using (var context = _database.CreateContext())
        {
            result = await context.PersonalData().EraseAsync<Customer>(id, new ErasureOptions
            {
                RetainRecord = (entity, _) =>
                {
                    offered.Add(entity.Name);
                    return entity.Name == "ClinicalNote" ? new RecordRetention(RetentionGround.LegalObligation, "Another duty") : null;
                },
            });
        }

        // The note's entity already keeps it, so it is not offered and keeps its own ground and reason.
        var note = result.Entries.Single(e => e.Entity == "ClinicalNote");
        Assert.Equal(ErasureAction.Retain, note.Action);
        Assert.Equal(RetentionGround.HealthOrSocialCare, note.RetentionGround);
        Assert.Equal("Health records must be kept for eight years", note.Reason);
        Assert.DoesNotContain("ClinicalNote", offered);
        Assert.Contains("Order", offered);
    }

    [Fact]
    public async Task A_legal_hold_is_still_reported_as_a_legal_hold()
    {
        var id = SeedCustomerWithPostalConsent();

        var offered = 0;
        ErasureResult result;
        using (var context = _database.CreateContext())
        {
            result = await context.PersonalData().EraseAsync<Customer>(id, new ErasureOptions
            {
                IsOnLegalHold = (_, row) => row is MarketingConsent,
                RetainRecord = (entity, row) =>
                {
                    offered++;
                    return RetainPostalConsent(entity, row);
                },
            });
        }

        Assert.All(result.Entries.Where(e => e.Entity == "MarketingConsent"), e => Assert.Equal(RetentionGround.LegalClaims, e.RetentionGround));
        Assert.Equal(1, offered);   // only the customer, which no hold covers
    }

    [Fact]
    public async Task A_dry_run_reports_a_retained_record()
    {
        var id = SeedCustomerWithPostalConsent();

        ErasureResult result;
        using (var context = _database.CreateContext(privacy => privacy.UseRegime(PrivacyRegimes.Popia)))
            result = await context.PersonalData().EraseAsync<Customer>(id, new ErasureOptions { DryRun = true, RetainRecord = RetainPostalConsent });

        var entry = result.Entries.Single(e => e.Action == ErasureAction.Retain);
        Assert.Equal("MarketingConsent", entry.Entity);
        Assert.Equal(RetentionGround.LegalObligation, entry.RetentionGround);
        Assert.Equal("s14(1)(a)", entry.Citation);
        Assert.Equal(PostalConsent.Reason, entry.Reason);
        Assert.Contains(result.Entries, e => e.Entity == "Customer" && e.Action == ErasureAction.Anonymise);
        Assert.Equal(1, result.Deleted);

        using var check = _database.CreateContext();
        Assert.Equal("Alex Doe", check.Customers.Single(c => c.Id == id).Name);
        Assert.Equal(2, check.Consents.Count());
    }

    [Fact]
    public async Task Each_record_is_offered_to_the_callback_first()
    {
        int id;
        using (var seed = _database.CreateContext())
            id = Seed.LightCustomer(seed).Id;

        var seen = new List<string>();
        using var context = _database.CreateContext();
        await context.PersonalData().EraseAsync<Customer>(id, new ErasureOptions
        {
            OnRecord = (record, _) =>
            {
                seen.Add($"{record.Entity.Name}:{record.Outcome.Action}");
                return Task.CompletedTask;
            },
        });

        Assert.Equal(new[] { "Customer:Delete", "MarketingConsent:Delete" }, seen.OrderBy(s => s));
    }

    [Fact]
    public async Task An_unknown_subject_is_reported_not_found()
    {
        using var context = _database.CreateContext();

        var result = await context.PersonalData().EraseAsync<Customer>(999);

        Assert.False(result.Found);
        Assert.Empty(result.Entries);
    }
}
