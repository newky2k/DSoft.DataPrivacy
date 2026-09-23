using System;
using System.Linq;
using DSoft.DataPrivacy.Redaction;
using DSoft.DataPrivacy.Rules;
using DSoft.DataPrivacy.Tests.TestModel;
using Xunit;

namespace DSoft.DataPrivacy.Tests;

public sealed class RuleTests
{
    [Fact]
    public void A_legal_hold_beats_everything()
    {
        var outcome = ErasureDecision.DecideRecord(new RecordErasureFacts { EntityErasure = ErasureAction.Delete, LegalHold = true });

        Assert.Equal(ErasureAction.Retain, outcome.Action);
        Assert.Equal(RetentionGround.LegalClaims, outcome.RetentionGround);
    }

    [Fact]
    public void A_retained_entity_is_kept_with_its_exemption()
    {
        var outcome = ErasureDecision.DecideRecord(new RecordErasureFacts { EntityErasure = ErasureAction.Retain, EntityGround = RetentionGround.HealthOrSocialCare });

        Assert.Equal(ErasureAction.Retain, outcome.Action);
        Assert.Equal(RetentionGround.HealthOrSocialCare, outcome.RetentionGround);
    }

    [Fact]
    public void A_retained_entity_without_an_exemption_is_a_configuration_error()
        => Assert.Throws<InvalidOperationException>(() => ErasureDecision.DecideRecord(new RecordErasureFacts { EntityErasure = ErasureAction.Retain }));

    [Fact]
    public void A_policy_category_keeps_the_record()
    {
        var policy = new ErasurePolicy().RetainCategory(PersonalDataCategory.Health, RetentionGround.HealthOrSocialCare, "Clinical records");

        var outcome = ErasureDecision.DecideRecord(new RecordErasureFacts { Categories = PersonalDataCategory.Health | PersonalDataCategory.FreeText }, policy);

        Assert.Equal(ErasureAction.Retain, outcome.Action);
    }

    [Fact]
    public void Anonymise_and_delete_follow_configuration()
    {
        Assert.Equal(ErasureAction.Anonymise, ErasureDecision.DecideRecord(new RecordErasureFacts { EntityErasure = ErasureAction.Anonymise }).Action);
        Assert.Equal(ErasureAction.Delete, ErasureDecision.DecideRecord(new RecordErasureFacts()).Action);
    }

    [Theory]
    [InlineData(typeof(string), true, AnonymisationMethod.Null)]
    [InlineData(typeof(string), false, AnonymisationMethod.Redact)]
    [InlineData(typeof(DateTime), false, AnonymisationMethod.Generalise)]
    [InlineData(typeof(int), false, AnonymisationMethod.Null)]
    public void Default_anonymisation_depends_on_the_type(Type type, bool nullable, AnonymisationMethod expected)
    {
        var outcome = ErasureDecision.DecideField(new FieldErasureFacts { ValueType = type, IsNullable = nullable });

        Assert.Equal(expected, outcome.Method);
    }

    [Fact]
    public void A_retained_value_needs_an_exemption()
    {
        Assert.Equal(ErasureAction.Retain, ErasureDecision.DecideField(new FieldErasureFacts { Erasure = ErasureAction.Retain, RetentionGround = RetentionGround.HealthOrSocialCare }).Action);
        Assert.Throws<InvalidOperationException>(() => ErasureDecision.DecideField(new FieldErasureFacts { Erasure = ErasureAction.Retain }));
    }

    [Theory]
    [InlineData("P6Y", 6, 0, 0)]
    [InlineData("P18M", 0, 18, 0)]
    [InlineData("P1Y6M", 1, 6, 0)]
    [InlineData("P2W", 0, 0, 14)]
    public void Retention_periods_parse_from_iso_8601(string text, int years, int months, int days)
        => Assert.Equal(new RetentionPeriod(years, months, days), RetentionPeriod.Parse(text));

    [Fact]
    public void Retention_periods_count_calendar_years()
    {
        var now = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var period = RetentionPeriod.FromYears(6);

        Assert.Equal(new DateTimeOffset(2020, 3, 1, 0, 0, 0, TimeSpan.Zero), period.CutoffFrom(now));
        Assert.True(period.HasElapsed(new DateTimeOffset(2020, 2, 29, 0, 0, 0, TimeSpan.Zero), now));
        Assert.False(period.HasElapsed(null, now));
        Assert.Equal("P6Y", period.ToString());
    }

    [Fact]
    public void Restricted_processing_allows_only_article_18_grounds()
    {
        var policy = ProcessingRestrictionPolicy.Default;

        Assert.True(policy.IsPermitted(ProcessingOperation.Storage, isRestricted: true));
        Assert.True(policy.IsPermitted(ProcessingOperation.SubjectAccess, isRestricted: true));
        Assert.False(policy.IsPermitted(ProcessingOperation.Marketing, isRestricted: true));
        Assert.True(policy.IsPermitted(ProcessingOperation.Marketing, isRestricted: true, hasConsent: true));
        Assert.True(policy.IsPermitted(ProcessingOperation.Reporting, isRestricted: false));
        Assert.Throws<InvalidOperationException>(() => policy.Permit(ProcessingOperation.Reporting));
    }

    [Fact]
    public void Hashes_are_stable_and_ignore_case_and_spacing()
    {
        var hasher = new PersonalDataHasher(ShopContext.HashKey);

        Assert.Equal(hasher.Hash("jo@example.com"), hasher.Hash("  JO@Example.com "));
        Assert.Equal(64, hasher.Hash("jo@example.com").Length);
        Assert.NotEqual(hasher.Hash("jo@example.com"), new PersonalDataHasher(new byte[32]).Hash("jo@example.com"));
        Assert.Throws<ArgumentException>(() => new PersonalDataHasher(new byte[16]));
    }

    [Fact]
    public void Categories_answer_the_common_questions()
    {
        var health = PersonalDataCategory.Health | PersonalDataCategory.FreeText;

        Assert.True(health.IsSpecialCategory());
        Assert.True(health.IsHighRisk());
        Assert.False(PersonalDataCategory.Contact.IsHighRisk());
        Assert.Equal("FreeText, Health", health.Describe());
    }

    [Fact]
    public void The_registry_reads_attributes_without_entity_framework()
    {
        var registry = PersonalDataRegistry.FromTypes(new[] { typeof(Customer), typeof(Order), typeof(OrderLine), typeof(ClinicalNote) });

        Assert.Equal(new[] { "Customer" }, registry.DataSubjects.Select(t => t.Type.Name));
        Assert.Null(registry.Find(typeof(OrderLine))); // configured only through the fluent API
        Assert.Equal(RetentionGround.HealthOrSocialCare, registry.Find(typeof(ClinicalNote))!.RetentionGround);
        Assert.Contains("Sales", registry.DataClasses());
    }

    [Fact]
    public void Coverage_lists_properties_nobody_has_decided_about()
    {
        var missing = PersonalDataCoverage.FindUnclassified(new[] { typeof(OrderLine), typeof(Customer) });

        Assert.Equal(new[] { "OrderLine.Product (String)" }, missing.Select(m => m.ToString()));
        Assert.Contains("OrderLine.Product", PersonalDataCoverage.Format(missing));
    }

    [Fact]
    public void Log_classification_uses_the_most_sensitive_category()
    {
        var classification = PersonalDataTaxonomy.For(PersonalDataCategory.Contact | PersonalDataCategory.Health);

        Assert.Equal(PersonalDataTaxonomy.TaxonomyName, classification.TaxonomyName);
        Assert.Equal("Health", classification.Value);
        Assert.Equal("Contact", new PersonalDataClassificationAttribute(PersonalDataCategory.Contact).Classification.Value);
    }
}
