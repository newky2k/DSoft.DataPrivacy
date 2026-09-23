using System;
using System.Linq;
using System.Threading.Tasks;
using DSoft.DataPrivacy.Regimes;
using DSoft.DataPrivacy.Rules;
using DSoft.DataPrivacy.Tests.TestModel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DSoft.DataPrivacy.Tests;

public sealed class RegimeTests
{
    private static readonly PrivacyRegime Gdpr = PrivacyRegimes.Gdpr;
    private static readonly PrivacyRegime Popia = PrivacyRegimes.Popia;

    [Fact]
    public void Regimes_are_found_by_identifier()
    {
        Assert.Same(Popia, PrivacyRegimes.Find("POPIA"));
        Assert.Same(Gdpr, PrivacyRegimes.Get("gdpr"));
        Assert.Null(PrivacyRegimes.Find("ccpa"));
        Assert.Contains("gdpr, popia", Assert.Throws<ArgumentException>(() => PrivacyRegimes.Get("ccpa")).Message);
    }

    [Fact]
    public void Each_law_uses_its_own_terms()
    {
        Assert.Equal("controller", Gdpr.ControllerTerm);
        Assert.Equal("responsible party", Popia.ControllerTerm);
        Assert.Equal("operator", Popia.ProcessorTerm);
        Assert.Equal("special personal information", Popia.SpecialDataTerm);
    }

    [Theory]
    [InlineData(RetentionGround.HealthOrSocialCare, "Art 17(3)(c)", "s14(1)(a)")]
    [InlineData(RetentionGround.LegalObligation, "Art 17(3)(b)", "s14(1)(a)")]
    [InlineData(RetentionGround.LegalClaims, "Art 17(3)(e)", "s14(1)(b)")]
    [InlineData(RetentionGround.ArchivingOrResearch, "Art 17(3)(d)", "s14(2)")]
    [InlineData(RetentionGround.Consent, null, "s14(1)(d)")]
    [InlineData(RetentionGround.Contract, null, "s14(1)(c)")]
    [InlineData(RetentionGround.OperationalNeed, null, "s14(1)(b)")]
    [InlineData(RetentionGround.None, null, null)]
    public void Retention_grounds_cite_each_law(RetentionGround ground, string? gdpr, string? popia)
    {
        Assert.Equal(gdpr, Gdpr.Cite(ground));
        Assert.Equal(popia, Popia.Cite(ground));
        Assert.Equal(gdpr != null, Gdpr.Recognises(ground));
    }

    [Theory]
    [InlineData(LawfulBasis.Consent, "Art 6(1)(a)", "s11(1)(a)")]
    [InlineData(LawfulBasis.LegitimateInterests, "Art 6(1)(f)", "s11(1)(f)")]
    [InlineData(LawfulBasis.VitalInterests, "Art 6(1)(d)", null)]
    [InlineData(LawfulBasis.SubjectLegitimateInterest, null, "s11(1)(d)")]
    public void Lawful_bases_cite_each_law(LawfulBasis basis, string? gdpr, string? popia)
    {
        Assert.Equal(gdpr, Gdpr.Cite(basis));
        Assert.Equal(popia, Popia.Cite(basis));
    }

    [Fact]
    public void Special_data_conditions_cite_each_law()
    {
        Assert.Equal("Art 9(2)(h)", Gdpr.Cite(SpecialDataCondition.HealthOrSocialCare));
        Assert.Equal("s32", Popia.Cite(SpecialDataCondition.HealthOrSocialCare));
        Assert.Null(Gdpr.Cite(SpecialDataCondition.InternationalLawObligation));
        Assert.Equal("s27(1)(c)", Popia.Cite(SpecialDataCondition.InternationalLawObligation));
    }

    [Fact]
    public void Criminal_data_is_special_under_popia_only()
    {
        Assert.True(Popia.IsSpecial(PersonalDataCategory.CriminalOffence));
        Assert.False(Gdpr.IsSpecial(PersonalDataCategory.CriminalOffence));
        Assert.True(Gdpr.IsSpecial(PersonalDataCategory.Health));
        Assert.True(Popia.IsSpecial(PersonalDataCategory.Health));
        Assert.False(Popia.IsSpecial(PersonalDataCategory.Contact));
    }

    [Fact]
    public void Popia_has_no_portability_right()
    {
        Assert.Contains(DataSubjectRequestType.Portability, Gdpr.RequestTypes);
        Assert.DoesNotContain(DataSubjectRequestType.Portability, Popia.RequestTypes);
        Assert.Equal("s24", Popia.Cite(DataSubjectRequestType.Erasure));
    }

    [Fact]
    public void Gdpr_deadline_uses_the_last_day_when_the_month_is_short()
    {
        // 29 February 2028 is a Tuesday.
        Assert.Equal(new DateTime(2028, 2, 29), Gdpr.RequestDeadline(DataSubjectRequestType.Access, new DateTime(2028, 1, 31)));

        // 28 February 2026 is a Saturday, so the deadline moves to Monday 2 March.
        Assert.Equal(new DateTime(2026, 3, 2), Gdpr.RequestDeadline(DataSubjectRequestType.Erasure, new DateTime(2026, 1, 31)));
    }

    [Fact]
    public void Gdpr_deadline_is_one_month_or_three_when_extended()
    {
        Assert.Equal(new DateTime(2026, 10, 5), Gdpr.RequestDeadline(DataSubjectRequestType.Access, new DateTime(2026, 9, 5)));
        Assert.Equal(new DateTime(2026, 12, 7), Gdpr.RequestDeadline(DataSubjectRequestType.Access, new DateTime(2026, 9, 7), extended: true));
        Assert.True(Gdpr.IsOverdue(DataSubjectRequestType.Access, new DateTime(2026, 9, 5), new DateTime(2026, 10, 6)));
    }

    [Fact]
    public void Popia_access_deadline_is_thirty_days_or_sixty_when_extended()
    {
        // 30 days after Monday 7 September 2026 is Wednesday 7 October.
        Assert.Equal(new DateTime(2026, 10, 7), Popia.RequestDeadline(DataSubjectRequestType.Access, new DateTime(2026, 9, 7)));
        Assert.Equal(new DateTime(2026, 11, 6), Popia.RequestDeadline(DataSubjectRequestType.Access, new DateTime(2026, 9, 7), extended: true));

        // 30 days after 5 September 2026 is Monday 5 October; a public holiday that day moves it to Tuesday.
        Assert.Equal(new DateTime(2026, 10, 6), Popia.RequestDeadline(DataSubjectRequestType.Access, new DateTime(2026, 9, 5),
            isNonWorkingDay: d => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || d == new DateTime(2026, 10, 5)));
    }

    [Fact]
    public void Popia_sets_no_fixed_period_for_other_requests()
    {
        Assert.Null(Popia.RequestDeadline(DataSubjectRequestType.Erasure, new DateTime(2026, 9, 7)));
        Assert.False(Popia.IsOverdue(DataSubjectRequestType.Erasure, new DateTime(2020, 1, 1), new DateTime(2026, 9, 7)));
        Assert.Null(Gdpr.RequestDeadline((DataSubjectRequestType)99, new DateTime(2026, 9, 7)));
    }

    [Fact]
    public void Breach_notification_windows_differ()
    {
        Assert.Equal(TimeSpan.FromHours(72), Gdpr.BreachNotificationWindow);
        Assert.Null(Popia.BreachNotificationWindow);
        Assert.Contains("s22", Popia.BreachNotificationRule);
    }

    [Fact]
    public async Task The_erasure_log_cites_the_regime_in_force()
    {
        using var database = new ShopDatabase();
        int id;
        using (var seed = database.CreateContext())
            id = Seed.FullCustomer(seed).Id;

        using var context = database.CreateContext(privacy => privacy.UseRegime(PrivacyRegimes.Popia));
        var result = await context.PersonalData().EraseAsync<Customer>(id, new DSoft.DataPrivacy.EntityFrameworkCore.Erasure.ErasureOptions { DryRun = true });

        Assert.Equal("POPIA", result.Regime);
        var note = result.Entries.Single(e => e.Entity == "ClinicalNote");
        Assert.Equal(RetentionGround.HealthOrSocialCare, note.RetentionGround);
        Assert.Equal("s14(1)(a)", note.Citation);
    }

    [Fact]
    public void Validation_rejects_grounds_the_law_does_not_recognise()
    {
        using var database = new ShopDatabase();
        var policy = new ErasurePolicy().RetainCategory(PersonalDataCategory.Contact, RetentionGround.Consent, "Kept while the person consents");

        using (var gdpr = database.CreateContext(privacy => privacy.UseRegime(PrivacyRegimes.Gdpr).UseErasurePolicy(policy)))
            Assert.Contains(gdpr.PersonalData().Validate(), i => i.Severity == PersonalDataIssueSeverity.Error && i.Message.Contains("Consent") && i.Message.Contains("GDPR"));

        using (var popia = database.CreateContext(privacy => privacy.UseRegime("popia").UseErasurePolicy(policy)))
            Assert.DoesNotContain(popia.PersonalData().Validate(), i => i.Severity == PersonalDataIssueSeverity.Error);

        using (var none = database.CreateContext())
            Assert.Contains(none.PersonalData().Validate(), i => i.Message.Contains("No privacy regime"));
    }

    [Fact]
    public void The_inventory_follows_the_regime()
    {
        using var database = new ShopDatabase();
        using var context = database.CreateContext(privacy => privacy.UseRegime(PrivacyRegimes.Gdpr));

        var note = context.PersonalData().Inventory().Items.First(i => i.Entity == "ClinicalNote" && i.Property == "Text");

        Assert.True(note.SpecialCategory);
        Assert.Equal("Retain (HealthOrSocialCare, Art 17(3)(c))", note.OnErasure);
    }

    private static readonly PrivacyRegime Both = PrivacyRegimes.Combine(PrivacyRegimes.Gdpr, PrivacyRegimes.Popia);

    [Fact]
    public void Combining_one_regime_returns_it_and_duplicates_are_removed()
    {
        Assert.Same(Gdpr, PrivacyRegimes.Combine(Gdpr));
        Assert.Same(Gdpr, PrivacyRegimes.Combine(Gdpr, Gdpr));

        var nested = Assert.IsType<CombinedPrivacyRegime>(PrivacyRegimes.Combine(Both, Popia));
        Assert.Equal(new[] { Gdpr, Popia }, nested.Regimes);
        Assert.Throws<ArgumentException>(() => PrivacyRegimes.Combine());
    }

    [Fact]
    public void Combinations_are_found_from_configuration()
    {
        var combined = Assert.IsType<CombinedPrivacyRegime>(PrivacyRegimes.Get("gdpr, popia"));

        Assert.Equal("gdpr+popia", combined.Id);
        Assert.Equal("GDPR + POPIA", combined.Name);
        Assert.IsType<CombinedPrivacyRegime>(PrivacyRegimes.Find("POPIA+GDPR"));
        Assert.Null(PrivacyRegimes.Find("gdpr,ccpa"));
    }

    [Fact]
    public void A_combination_only_recognises_what_every_law_recognises()
    {
        Assert.Equal("GDPR Art 17(3)(c); POPIA s14(1)(a)", Both.Cite(RetentionGround.HealthOrSocialCare));
        Assert.False(Both.Recognises(RetentionGround.Consent));          // POPIA only
        Assert.False(Both.Recognises(LawfulBasis.VitalInterests));       // GDPR only
        Assert.True(Both.Recognises(LawfulBasis.LegitimateInterests));
        Assert.Equal("controller / responsible party", Both.ControllerTerm);
    }

    [Fact]
    public void A_combination_treats_data_as_special_when_any_law_does()
    {
        Assert.True(Both.IsSpecial(PersonalDataCategory.CriminalOffence));
        Assert.True(Both.IsSpecial(PersonalDataCategory.Health));
        Assert.False(Both.IsSpecial(PersonalDataCategory.Contact));
    }

    [Fact]
    public void A_combination_gives_every_right_any_law_gives()
    {
        Assert.Contains(DataSubjectRequestType.Portability, Both.RequestTypes);
        Assert.Equal("GDPR Art 20", Both.Cite(DataSubjectRequestType.Portability));
        Assert.Equal("GDPR Art 17; POPIA s24", Both.Cite(DataSubjectRequestType.Erasure));
    }

    [Fact]
    public void A_combination_uses_the_earliest_deadline_and_shortest_breach_window()
    {
        var received = new DateTime(2026, 1, 5);

        // POPIA (PAIA) 30 days is Wednesday 4 February; the GDPR month is Thursday 5 February.
        Assert.Equal(new DateTime(2026, 2, 4), Both.RequestDeadline(DataSubjectRequestType.Access, received));

        // POPIA sets no period for erasure, so the GDPR month applies.
        Assert.Equal(new DateTime(2026, 2, 5), Both.RequestDeadline(DataSubjectRequestType.Erasure, received));

        // Extended: POPIA 60 days is Friday 6 March; the GDPR three months is Monday 6 April.
        Assert.Equal(new DateTime(2026, 3, 6), Both.RequestDeadline(DataSubjectRequestType.Access, received, extended: true));

        Assert.Equal(TimeSpan.FromHours(72), Both.BreachNotificationWindow);
        Assert.Contains("POPIA:", Both.BreachNotificationRule);
    }

    [Fact]
    public async Task Erasure_and_validation_apply_every_law()
    {
        using var database = new ShopDatabase();
        int id;
        using (var seed = database.CreateContext())
            id = Seed.FullCustomer(seed).Id;

        using (var context = database.CreateContext(privacy => privacy.UseRegimes(PrivacyRegimes.Gdpr, PrivacyRegimes.Popia)))
        {
            var result = await context.PersonalData().EraseAsync<Customer>(id, new DSoft.DataPrivacy.EntityFrameworkCore.Erasure.ErasureOptions { DryRun = true });
            Assert.Equal("GDPR + POPIA", result.Regime);
            Assert.Equal("GDPR Art 17(3)(c); POPIA s14(1)(a)", result.Entries.Single(e => e.Entity == "ClinicalNote").Citation);
        }

        var consentPolicy = new ErasurePolicy().RetainCategory(PersonalDataCategory.Contact, RetentionGround.Consent, "Kept while the person consents");
        using (var context = database.CreateContext(privacy => privacy.UseRegime("gdpr,popia").UseErasurePolicy(consentPolicy)))
            Assert.Contains(context.PersonalData().Validate(), i => i.Severity == PersonalDataIssueSeverity.Error && i.Message.Contains("GDPR + POPIA"));
    }
}
