using System;
using System.Linq;
using DSoft.DataPrivacy.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DSoft.DataPrivacy.Tests;

public sealed class CoverageTests : IDisposable
{
    private readonly MailContext _context = new();

    public void Dispose() => _context.Dispose();

    private PersonalDataModel Model => _context.PersonalData().Model;

    [Fact]
    public void Entities_with_no_link_to_a_person_are_missed_by_the_linked_check()
    {
        Assert.Empty(Model.FindUnclassified());
    }

    [Fact]
    public void Personal_looking_properties_are_found_on_entities_with_no_link_to_a_person()
    {
        var gaps = Model.FindUnclassifiedAnywhere().Select(g => g.ToString()).ToList();

        // Once an entity looks personal, every undecided candidate on it is listed.
        Assert.Contains("InboundMail.FromEmail (String)", gaps);
        Assert.Contains("InboundMail.Subject (String)", gaps);
        Assert.Contains("InboundMail.ReceivedAt (DateTime)", gaps);
    }

    [Fact]
    public void Fluent_classification_counts()
    {
        var gaps = Model.FindUnclassifiedAnywhere().Where(g => g.Entity.ClrType == typeof(EventLog)).Select(g => g.Name).ToList();

        // Actor is classified in OnModelCreating, which marks the entity as holding personal data and decides Actor.
        Assert.Equal(new[] { "Payload" }, gaps);
    }

    [Fact]
    public void Owned_types_are_checked_with_their_owner()
    {
        var gaps = Model.FindUnclassifiedAnywhere().Where(g => g.Entity.ClrType == typeof(Venue)).Select(g => g.Name).OrderBy(n => n).ToList();

        Assert.Equal(new[] { "Location.PostCode", "Title" }, gaps);
        Assert.DoesNotContain(Model.FindUnclassifiedAnywhere(), g => g.Entity.ClrType == typeof(GeoPoint));
    }

    [Fact]
    public void Keyless_types_and_entities_that_do_not_look_personal_are_skipped()
    {
        var entities = Model.FindUnclassifiedAnywhere().Select(g => g.Entity.ClrType).Distinct().ToList();

        Assert.DoesNotContain(typeof(MailSummary), entities); // keyless
        Assert.DoesNotContain(typeof(Product), entities);     // nothing looks personal
        Assert.DoesNotContain(typeof(Template), entities);    // the only personal-looking name is decided
        Assert.DoesNotContain(typeof(Person), entities);      // fully classified
    }

    [Fact]
    public void The_test_of_what_looks_personal_can_be_replaced()
    {
        var gaps = Model.FindUnclassifiedAnywhere(property => property.Name == "Sku").Select(g => g.ToString()).OrderBy(n => n).ToList();

        // EventLog is still checked: it holds classified data whatever the test says.
        Assert.Equal(new[] { "EventLog.Payload (String)", "Product.Sku (String)", "Product.Title (String)" }, gaps);
    }

    [Fact]
    public void Candidate_options_apply()
    {
        var options = new PersonalDataCoverageOptions();
        options.IgnoredPropertyNames.Add("Subject");

        var gaps = Model.FindUnclassifiedAnywhere(options: options).Where(g => g.Entity.ClrType == typeof(InboundMail)).Select(g => g.Name).OrderBy(n => n).ToList();

        Assert.Equal(new[] { "FromEmail", "ReceivedAt" }, gaps);
    }

    [Theory]
    [InlineData("Email", true)]
    [InlineData("ContactEmail", true)]
    [InlineData("phone_number", true)]
    [InlineData("FirstName", true)]
    [InlineData("last_name", true)]
    [InlineData("UserName", true)]
    [InlineData("IpAddress", true)]
    [InlineData("post_code", true)]
    [InlineData("Postcode", true)]
    [InlineData("DateOfBirth", true)]
    [InlineData("Title", false)]
    [InlineData("Sku", false)]
    [InlineData("CreatedAt", false)]
    [InlineData("Name", false)]
    public void Names_that_suggest_personal_data_are_recognised(string name, bool expected)
    {
        Assert.Equal(expected, PersonalDataCoverage.LooksPersonal(name));
    }

    [DataSubject]
    public class Person
    {
        public int Id { get; set; }

        [PersonalData(PersonalDataCategory.DirectIdentifier)]
        public string Name { get; set; } = string.Empty;
    }

    // Personal data, no foreign key to a person and no classification.
    public class InboundMail
    {
        public int Id { get; set; }

        public string FromEmail { get; set; } = string.Empty;

        public string Subject { get; set; } = string.Empty;

        public DateTime ReceivedAt { get; set; }

        public int Size { get; set; }
    }

    public class EventLog
    {
        public int Id { get; set; }

        public string Actor { get; set; } = string.Empty;

        public string Payload { get; set; } = string.Empty;
    }

    public class Venue
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public GeoPoint Location { get; set; } = new();
    }

    public class GeoPoint
    {
        public string PostCode { get; set; } = string.Empty;
    }

    public class Product
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Sku { get; set; } = string.Empty;
    }

    public class Template
    {
        public int Id { get; set; }

        public string EmailSubject { get; set; } = string.Empty;
    }

    public class MailSummary
    {
        public string FromEmail { get; set; } = string.Empty;
    }

    private sealed class MailContext : DbContext
    {
        public MailContext()
            : base(new DbContextOptionsBuilder<MailContext>().UseSqlite("DataSource=:memory:").UseDataPrivacy().Options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Person>();
            modelBuilder.Entity<InboundMail>();
            modelBuilder.Entity<EventLog>().Property(e => e.Actor).IsPersonalData(PersonalDataCategory.AuditCopy);
            modelBuilder.Entity<Venue>().OwnsOne(v => v.Location);
            modelBuilder.Entity<Product>();
            modelBuilder.Entity<Template>().Property(t => t.EmailSubject).IsNotPersonalData("Wording of a template");
            modelBuilder.Entity<MailSummary>().HasNoKey();
        }
    }
}
