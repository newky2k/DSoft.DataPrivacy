using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSoft.EntityFrameworkCore.GDPR.Erasure;
using DSoft.EntityFrameworkCore.GDPR.Rules;
using DSoft.EntityFrameworkCore.GDPR.Tests.TestModel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DSoft.EntityFrameworkCore.GDPR.Tests;

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
        Assert.Contains(result.Entries, e => e.Entity == "ClinicalNote" && e.Action == ErasureAction.Retain && e.Exemption == ErasureExemption.PublicHealth);

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

        Assert.All(result.Entries, e => Assert.Equal(ErasureExemption.LegalClaims, e.Exemption));

        using var check = _database.CreateContext();
        Assert.Equal("alex@example.com", check.Customers.Single(c => c.Id == id).Email);
    }

    [Fact]
    public async Task A_policy_can_retain_a_category_wherever_it_is_found()
    {
        int id;
        using (var seed = _database.CreateContext())
            id = Seed.LightCustomer(seed).Id;

        var policy = new ErasurePolicy().RetainCategory(PersonalDataCategory.OnlineIdentifier, ErasureExemption.LegalObligation, "Fraud prevention records");

        using (var context = _database.CreateContext(gdpr => gdpr.UseErasurePolicy(policy)))
            await context.PersonalData().EraseAsync<Customer>(id);

        using var check = _database.CreateContext();
        Assert.Single(check.Consents);                           // kept: it holds an IP address
        Assert.True(check.Customers.Any(c => c.Id == id));       // kept, anonymised, because the consent depends on it
        Assert.Equal("[erased]", check.Customers.Single(c => c.Id == id).Name);
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
