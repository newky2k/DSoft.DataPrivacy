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
    public async Task A_record_on_legal_hold_is_not_deleted_and_is_reported()
    {
        int heldCustomer;
        using (var seed = _database.CreateContext())
        {
            heldCustomer = Seed.FullCustomer(seed).Id; // consent given three years ago
            Seed.FullCustomer(seed, "Someone Else", "else@example.com");
        }

        var policy = new RetentionPolicy("Marketing", RetentionPeriod.FromYears(2));
        int heldConsent;
        using (var context = _database.CreateContext())
        {
            heldConsent = context.Consents.Single(c => c.CustomerId == heldCustomer).Id;
            var options = new RetentionRunOptions
            {
                Now = Seed.Now,
                IsOnLegalHold = (entity, row) => entity.Name == "MarketingConsent" && ((MarketingConsent)row).CustomerId == heldCustomer,
            };

            var result = await context.PersonalData().ApplyRetentionAsync(policy, options);

            Assert.Equal(1, result.Deleted);
            Assert.Equal(1, result.Held);
            Assert.Equal($"MarketingConsent {heldConsent}: on legal hold; left untouched.", Assert.Single(result.Skipped));
            Assert.DoesNotContain(result.Entries, e => e.Key == heldConsent.ToString());
            Assert.False(result.HasMore);
        }

        using var check = _database.CreateContext();
        Assert.Equal(heldConsent, check.Consents.Single().Id);
        Assert.Equal("203.0.113.7", check.Consents.Single().IpAddress);
    }

    [Fact]
    public async Task A_record_on_legal_hold_is_not_anonymised()
    {
        int held;
        using (var seed = _database.CreateContext())
        {
            held = Seed.LightCustomer(seed).Id; // last active five years ago
            Seed.LightCustomer(seed, "Sam Roe", "sam@example.com");
        }

        var policy = new RetentionPolicy("Customers", RetentionPeriod.FromYears(3), RetentionTrigger.LastActivity, ErasureAction.Anonymise);
        using (var context = _database.CreateContext())
        {
            var options = new RetentionRunOptions { Now = Seed.Now, IsOnLegalHold = (_, row) => row is Customer customer && customer.Id == held };
            var result = await context.PersonalData().ApplyRetentionAsync(policy, options);

            Assert.Equal(1, result.Anonymised);
            Assert.Equal(1, result.Held);
            Assert.Contains($"Customer {held}: on legal hold; left untouched.", result.Skipped);
        }

        using var check = _database.CreateContext();
        var customer = check.Customers.Single(c => c.Id == held);
        Assert.Equal("Alex Doe", customer.Name);
        Assert.Null(customer.AnonymisedAt);
        Assert.Equal("[erased]", check.Customers.Single(c => c.Id != held).Name);
    }

    [Fact]
    public async Task A_dry_run_reports_legal_holds_the_same_way()
    {
        int heldCustomer;
        using (var seed = _database.CreateContext())
        {
            heldCustomer = Seed.FullCustomer(seed).Id;
            Seed.FullCustomer(seed, "Someone Else", "else@example.com");
        }

        var policy = new RetentionPolicy("Marketing", RetentionPeriod.FromYears(2));
        using (var context = _database.CreateContext())
        {
            var options = new RetentionRunOptions
            {
                Now = Seed.Now,
                DryRun = true,
                IsOnLegalHold = (_, row) => ((MarketingConsent)row).CustomerId == heldCustomer,
            };

            var result = await context.PersonalData().ApplyRetentionAsync(policy, options);

            Assert.Equal(1, result.Deleted);
            Assert.Equal(1, result.Held);
            Assert.EndsWith(": on legal hold; left untouched.", Assert.Single(result.Skipped));
        }

        using var check = _database.CreateContext();
        Assert.Equal(2, check.Consents.Count());
    }

    [Fact]
    public async Task Records_on_legal_hold_do_not_stop_the_records_behind_them()
    {
        using (var seed = _database.CreateContext())
        {
            for (var i = 0; i < 6; i++)
                Seed.FullCustomer(seed, $"Customer {i}", $"c{i}@example.com");
        }

        // The first four due records are held, which is more than a batch.
        int[] heldIds;
        using (var context = _database.CreateContext())
            heldIds = context.Consents.OrderBy(c => c.Id).Take(4).Select(c => c.Id).ToArray();

        var policy = new RetentionPolicy("Marketing", RetentionPeriod.FromYears(2));
        var runs = 0;
        RetentionRunResult result;
        do
        {
            using var context = _database.CreateContext();
            result = await context.PersonalData().ApplyRetentionAsync(policy, new RetentionRunOptions
            {
                Now = Seed.Now,
                BatchSize = 2,
                IsOnLegalHold = (_, row) => heldIds.Contains(((MarketingConsent)row).Id),
            });
            runs++;
        }
        while (result.HasMore && runs < 10);

        using var check = _database.CreateContext();
        Assert.Equal(heldIds, check.Consents.OrderBy(c => c.Id).Select(c => c.Id).ToArray());
        Assert.Equal(4, result.Held);
    }

    [Fact]
    public async Task A_released_hold_lets_the_record_go_on_the_next_run()
    {
        using (var seed = _database.CreateContext())
            Seed.FullCustomer(seed);

        var policy = new RetentionPolicy("Marketing", RetentionPeriod.FromYears(2));
        using (var context = _database.CreateContext())
            Assert.Equal(0, (await context.PersonalData().ApplyRetentionAsync(policy, new RetentionRunOptions { Now = Seed.Now, IsOnLegalHold = (_, _) => true })).Deleted);

        using (var context = _database.CreateContext())
            Assert.Equal(1, (await context.PersonalData().ApplyRetentionAsync(policy, new RetentionRunOptions { Now = Seed.Now, IsOnLegalHold = (_, _) => false })).Deleted);

        using var check = _database.CreateContext();
        Assert.Empty(check.Consents);
    }

    [Fact]
    public async Task A_condition_keeps_records_that_do_not_meet_it_from_being_deleted()
    {
        int current;
        using (var seed = _database.CreateContext())
        {
            current = Seed.FullCustomer(seed).Id; // consent given three years ago
            var anonymised = Seed.FullCustomer(seed, "Someone Else", "else@example.com");
            anonymised.AnonymisedAt = Seed.Now;
            seed.SaveChanges();
        }

        using (var context = _database.CreateContext())
        {
            // A consent is kept while its customer still has a record: a subquery on another set.
            var policy = new RetentionPolicy("Marketing", RetentionPeriod.FromYears(2))
                .Where<MarketingConsent>(c => !context.Customers.Any(k => k.Id == c.CustomerId && k.AnonymisedAt == null));

            var result = await context.PersonalData().ApplyRetentionAsync(policy, new RetentionRunOptions { Now = Seed.Now });

            Assert.Equal(1, result.Deleted);
            Assert.Equal(0, result.Held);
            Assert.Empty(result.Skipped);
            Assert.False(result.HasMore);
        }

        using var check = _database.CreateContext();
        Assert.Equal(current, check.Consents.Single().CustomerId);
    }

    [Fact]
    public async Task A_condition_keeps_records_that_do_not_meet_it_from_being_anonymised()
    {
        int gold;
        using (var seed = _database.CreateContext())
        {
            var customer = Seed.LightCustomer(seed); // last active five years ago
            customer.Tier = "Gold";
            seed.SaveChanges();
            gold = customer.Id;
            Seed.LightCustomer(seed, "Sam Roe", "sam@example.com");
        }

        var policy = new RetentionPolicy("Customers", RetentionPeriod.FromYears(3), RetentionTrigger.LastActivity, ErasureAction.Anonymise)
            .Where<Customer>(c => c.Tier != "Gold");

        using (var context = _database.CreateContext())
        {
            var result = await context.PersonalData().ApplyRetentionAsync(policy, new RetentionRunOptions { Now = Seed.Now });

            Assert.Equal(1, result.Anonymised);
            Assert.Empty(result.Skipped);
        }

        using var check = _database.CreateContext();
        Assert.Equal("Alex Doe", check.Customers.Single(c => c.Id == gold).Name);
        Assert.Null(check.Customers.Single(c => c.Id == gold).AnonymisedAt);
        Assert.Equal("[erased]", check.Customers.Single(c => c.Id != gold).Name);
    }

    [Fact]
    public async Task A_condition_applies_to_a_dry_run()
    {
        using (var seed = _database.CreateContext())
        {
            Seed.FullCustomer(seed);                                        // an Email consent, three years old
            Seed.FullCustomer(seed, "Someone Else", "else@example.com");
            seed.Consents.OrderBy(c => c.Id).Last().Channel = "Post";
            seed.SaveChanges();
        }

        var policy = new RetentionPolicy("Marketing", RetentionPeriod.FromYears(2)).Where<MarketingConsent>(c => c.Channel == "Post");

        using (var context = _database.CreateContext())
        {
            var result = await context.PersonalData().ApplyRetentionAsync(policy, new RetentionRunOptions { Now = Seed.Now, DryRun = true });

            Assert.Equal(1, result.Deleted);
            Assert.Empty(result.Skipped);
        }

        using var check = _database.CreateContext();
        Assert.Equal(2, check.Consents.Count());
    }

    [Fact]
    public async Task Several_conditions_for_one_entity_must_all_be_met()
    {
        using (var seed = _database.CreateContext())
        {
            for (var i = 0; i < 3; i++)
                Seed.FullCustomer(seed, $"Customer {i}", $"c{i}@example.com");
        }

        int[] ids;
        using (var context = _database.CreateContext())
            ids = context.Consents.OrderBy(c => c.Id).Select(c => c.Id).ToArray();
        int first = ids[0], last = ids[2];

        var policy = new RetentionPolicy("Marketing", RetentionPeriod.FromYears(2))
            .Where<MarketingConsent>(c => c.Id != first)
            .Where<MarketingConsent>(c => c.Id != last);

        using (var context = _database.CreateContext())
            Assert.Equal(1, (await context.PersonalData().ApplyRetentionAsync(policy, new RetentionRunOptions { Now = Seed.Now })).Deleted);

        using var check = _database.CreateContext();
        Assert.Equal(new[] { first, last }, check.Consents.OrderBy(c => c.Id).Select(c => c.Id).ToArray());
    }

    [Fact]
    public async Task A_condition_for_an_entity_outside_the_data_class_is_reported_and_changes_nothing_else()
    {
        using (var seed = _database.CreateContext())
            Seed.FullCustomer(seed);

        var policy = new RetentionPolicy("Marketing", RetentionPeriod.FromYears(2)).Where<Order>(o => o.Total > 1000);

        using (var context = _database.CreateContext())
        {
            var result = await context.PersonalData().ApplyRetentionAsync(policy, new RetentionRunOptions { Now = Seed.Now });

            Assert.Equal("Order: has a condition, but is not an entity in the Marketing data class.", Assert.Single(result.Skipped));
            Assert.Equal(1, result.Deleted); // the consent has no condition, so the period alone selects it
        }

        using var check = _database.CreateContext();
        Assert.Empty(check.Consents);
        Assert.Single(check.Orders);
    }

    [Fact]
    public async Task A_condition_can_be_built_from_the_context_of_each_run()
    {
        using (var seed = _database.CreateContext())
        {
            Seed.FullCustomer(seed);
            var anonymised = Seed.FullCustomer(seed, "Someone Else", "else@example.com");
            anonymised.AnonymisedAt = Seed.Now;
            seed.SaveChanges();
        }

        // Built once, with no context in hand.
        var policy = new RetentionPolicy("Marketing", RetentionPeriod.FromYears(2))
            .Where<MarketingConsent>(db => c => !db.Set<Customer>().Any(k => k.Id == c.CustomerId && k.AnonymisedAt == null));

        using (var context = _database.CreateContext())
            Assert.Equal(1, (await context.PersonalData().ApplyRetentionAsync(policy, new RetentionRunOptions { Now = Seed.Now, DryRun = true })).Deleted);

        using (var context = _database.CreateContext())
            Assert.Equal(1, (await context.PersonalData().ApplyRetentionAsync(policy, new RetentionRunOptions { Now = Seed.Now })).Deleted);

        using var check = _database.CreateContext();
        Assert.Single(check.Consents);
    }

    [Fact]
    public async Task A_condition_still_applies_when_the_batch_is_widened_past_legal_holds()
    {
        using (var seed = _database.CreateContext())
        {
            for (var i = 0; i < 7; i++)
                Seed.FullCustomer(seed, $"Customer {i}", $"c{i}@example.com");
        }

        // In key order: three held, two kept by the condition, two free to go. A batch holds two.
        int[] ids;
        using (var context = _database.CreateContext())
            ids = context.Consents.OrderBy(c => c.Id).Select(c => c.Id).ToArray();
        var held = ids.Take(3).ToArray();
        int keptA = ids[3], keptB = ids[4];

        var policy = new RetentionPolicy("Marketing", RetentionPeriod.FromYears(2))
            .Where<MarketingConsent>(c => c.Id != keptA && c.Id != keptB);

        var runs = 0;
        RetentionRunResult result;
        do
        {
            using var context = _database.CreateContext();
            result = await context.PersonalData().ApplyRetentionAsync(policy, new RetentionRunOptions
            {
                Now = Seed.Now,
                BatchSize = 2,
                IsOnLegalHold = (_, row) => held.Contains(((MarketingConsent)row).Id),
            });

            // Records kept by the condition are never loaded, so they are never reported as held.
            Assert.All(result.Skipped, s => Assert.EndsWith(": on legal hold; left untouched.", s));
            Assert.DoesNotContain(result.Skipped, s => s.StartsWith($"MarketingConsent {keptA}:") || s.StartsWith($"MarketingConsent {keptB}:"));
            runs++;
        }
        while (result.HasMore && runs < 10);

        using var check = _database.CreateContext();
        Assert.Equal(ids.Take(5).ToArray(), check.Consents.OrderBy(c => c.Id).Select(c => c.Id).ToArray());
        Assert.Equal(3, result.Held);
    }

    [Fact]
    public async Task A_condition_on_a_base_type_applies_and_one_on_a_derived_type_selected_through_its_base_is_refused()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var context = new TrailContext(connection);
        context.Database.EnsureCreated();
        context.Add(new TrailEntry { Source = "Portal", At = Seed.Now.AddYears(-9) });
        context.Add(new TrailEntry { Source = "Api", At = Seed.Now.AddYears(-9) });
        context.Add(new PrintTrailEntry { Source = "Portal", At = Seed.Now.AddYears(-9) });
        context.SaveChanges();

        var derived = new RetentionPolicy("Trails", RetentionPeriod.FromYears(8)).Where<PrintTrailEntry>(e => e.Source == "Api");
        var error = await Assert.ThrowsAsync<System.InvalidOperationException>(
            () => context.PersonalData().ApplyRetentionAsync(derived, new RetentionRunOptions { Now = Seed.Now }));
        Assert.Contains("Put the condition on 'TrailEntry'", error.Message);
        Assert.Equal(3, context.Set<TrailEntry>().Count());

        var onBase = new RetentionPolicy("Trails", RetentionPeriod.FromYears(8)).Where<TrailEntry>(e => e.Source == "Api");
        Assert.Equal(1, (await context.PersonalData().ApplyRetentionAsync(onBase, new RetentionRunOptions { Now = Seed.Now })).Deleted);
        Assert.Equal(2, context.Set<TrailEntry>().Count(e => e.Source == "Portal"));
    }

    [PersonalDataEntity(DataClass = "Trails")]
    public class TrailEntry
    {
        public int Id { get; set; }

        [NotPersonalData]
        public string Source { get; set; } = string.Empty;

        [RetentionTrigger]
        [NotPersonalData]
        public System.DateTime At { get; set; }
    }

    [PersonalDataEntity(DataClass = "Trails")]
    public class PrintTrailEntry : TrailEntry
    {
    }

    private sealed class TrailContext : DbContext
    {
        public TrailContext(Microsoft.Data.Sqlite.SqliteConnection connection)
            : base(new DbContextOptionsBuilder<TrailContext>().UseSqlite(connection).UseDataPrivacy().Options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TrailEntry>();
            modelBuilder.Entity<PrintTrailEntry>();
        }
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
