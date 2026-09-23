using System.Linq;
using System.Threading.Tasks;
using DSoft.DataPrivacy.EntityFrameworkCore.Retention;
using DSoft.DataPrivacy.Rules;
using DSoft.DataPrivacy.Tests.TestModel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DSoft.DataPrivacy.Tests;

public sealed class RetentionAndAuditingTests : System.IDisposable
{
    private readonly ShopDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Records_past_their_period_are_deleted()
    {
        using (var seed = _database.CreateContext())
        {
            Seed.FullCustomer(seed);   // consent given three years ago
            Seed.LightCustomer(seed);  // consent given last month
        }

        var policy = new RetentionPolicy("Marketing", RetentionPeriod.FromYears(2));
        using (var context = _database.CreateContext())
        {
            var result = await context.PersonalData().ApplyRetentionAsync(policy, new RetentionRunOptions { Now = Seed.Now });
            Assert.Equal(1, result.Deleted);
            Assert.False(result.HasMore);
        }

        using var check = _database.CreateContext();
        Assert.Equal("Sms", check.Consents.Single().Channel);
    }

    [Fact]
    public async Task Batches_continue_until_nothing_is_left()
    {
        using (var seed = _database.CreateContext())
        {
            for (var i = 0; i < 5; i++)
                Seed.FullCustomer(seed, $"Customer {i}", $"c{i}@example.com");
        }

        var policy = new RetentionPolicy("Marketing", RetentionPeriod.FromYears(2));
        var runs = 0;
        RetentionRunResult result;
        do
        {
            using var context = _database.CreateContext();
            result = await context.PersonalData().ApplyRetentionAsync(policy, new RetentionRunOptions { Now = Seed.Now, BatchSize = 2 });
            runs++;
        }
        while (result.HasMore && runs < 10);

        using var check = _database.CreateContext();
        Assert.Empty(check.Consents);
        Assert.Equal(3, runs);
    }

    [Fact]
    public async Task Anonymising_retention_marks_records_so_they_are_not_selected_again()
    {
        using (var seed = _database.CreateContext())
            Seed.LightCustomer(seed); // last active five years ago

        var policy = new RetentionPolicy("Customers", RetentionPeriod.FromYears(3), RetentionTrigger.LastActivity, ErasureAction.Anonymise);

        using (var context = _database.CreateContext())
            Assert.Equal(1, (await context.PersonalData().ApplyRetentionAsync(policy, new RetentionRunOptions { Now = Seed.Now })).Anonymised);

        using (var context = _database.CreateContext())
            Assert.Empty((await context.PersonalData().ApplyRetentionAsync(policy, new RetentionRunOptions { Now = Seed.Now })).Entries);

        using var check = _database.CreateContext();
        Assert.Equal("[erased]", check.Customers.Single().Name);
    }

    [Fact]
    public async Task Entities_retained_under_a_duty_are_skipped_unless_the_policy_says_so()
    {
        using (var seed = _database.CreateContext())
            Seed.FullCustomer(seed); // clinical note written ten years ago

        var policy = new RetentionPolicy("Clinical", RetentionPeriod.FromYears(8));

        using (var context = _database.CreateContext())
        {
            var result = await context.PersonalData().ApplyRetentionAsync(policy, new RetentionRunOptions { Now = Seed.Now });
            Assert.Empty(result.Entries);
            Assert.Contains(result.Skipped, s => s.StartsWith("ClinicalNote: retained on erasure"));
        }

        policy.IncludeRetainedRecords = true;
        using (var context = _database.CreateContext())
            Assert.Equal(1, (await context.PersonalData().ApplyRetentionAsync(policy, new RetentionRunOptions { Now = Seed.Now })).Deleted);
    }

    [Fact]
    public async Task Changes_to_personal_data_are_reported_without_values()
    {
        var observer = new RecordingObserver();

        int id;
        using (var seed = _database.CreateContext())
            id = Seed.LightCustomer(seed).Id;

        using (var context = _database.CreateContext(privacy => privacy.ObserveChanges(observer)))
        {
            var customer = context.Customers.Single(c => c.Id == id);
            customer.Email = "new@example.com";
            customer.Tier = "Gold";
            await context.SaveChangesAsync();
        }

        var change = Assert.Single(observer.Changes);
        Assert.Equal("Customer", change.Entity);
        Assert.Equal(EntityState.Modified, change.State);
        Assert.Equal(id, change.Key["Id"]);
        Assert.Equal("Email", Assert.Single(change.Properties).Name);
        Assert.DoesNotContain("new@example.com", change.ToString());
    }
}
