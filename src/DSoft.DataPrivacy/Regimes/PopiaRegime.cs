using System;

namespace DSoft.DataPrivacy.Regimes;

/// <summary>
/// South Africa's Protection of Personal Information Act 4 of 2013 (POPIA). Access requests follow the procedure
/// of the Promotion of Access to Information Act 2 of 2000 (PAIA), as POPIA section 23 requires.
/// </summary>
/// <remarks>
/// POPIA also protects existing juristic persons, such as companies, as data subjects. Mark any entity that
/// represents one with <see cref="DataSubjectAttribute"/>.
/// </remarks>
public sealed class PopiaRegime : PrivacyRegime
{
    internal PopiaRegime()
        : base("popia", "POPIA", "Protection of Personal Information Act 4 of 2013 (South Africa)")
    {
    }

    /// <inheritdoc />
    public override string ControllerTerm => "responsible party";

    /// <inheritdoc />
    public override string ProcessorTerm => "operator";

    /// <inheritdoc />
    public override string SpecialDataTerm => "special personal information";

    /// <summary>
    /// The section 26 categories: religious or philosophical beliefs, race or ethnic origin, trade union
    /// membership, political persuasion, health or sex life, biometric information, and criminal behaviour.
    /// Genetic data is included as health information.
    /// </summary>
    public override PersonalDataCategory SpecialCategories => PersonalDataCategory.SpecialCategory | PersonalDataCategory.CriminalOffence;

    /// <inheritdoc />
    public override TimeSpan? BreachNotificationWindow => null;

    /// <inheritdoc />
    public override string BreachNotificationRule
        => "Notify the Information Regulator and the people affected as soon as reasonably possible after discovering the compromise (s22).";

    /// <inheritdoc />
    public override string? Cite(RetentionGround ground) => ground switch
    {
        RetentionGround.LegalObligation => "s14(1)(a)",
        RetentionGround.HealthOrSocialCare => "s14(1)(a)",
        RetentionGround.OperationalNeed => "s14(1)(b)",
        RetentionGround.LegalClaims => "s14(1)(b)",
        RetentionGround.PublicTask => "s14(1)(b)",
        RetentionGround.Contract => "s14(1)(c)",
        RetentionGround.Consent => "s14(1)(d)",
        RetentionGround.ArchivingOrResearch => "s14(2)",
        RetentionGround.FreedomOfExpression => "s7",
        _ => null,
    };

    /// <inheritdoc />
    public override string? Cite(LawfulBasis basis) => basis switch
    {
        LawfulBasis.Consent => "s11(1)(a)",
        LawfulBasis.Contract => "s11(1)(b)",
        LawfulBasis.LegalObligation => "s11(1)(c)",
        LawfulBasis.SubjectLegitimateInterest => "s11(1)(d)",
        LawfulBasis.PublicTask => "s11(1)(e)",
        LawfulBasis.LegitimateInterests => "s11(1)(f)",
        _ => null,
    };

    /// <inheritdoc />
    public override string? Cite(SpecialDataCondition condition) => condition switch
    {
        SpecialDataCondition.ExplicitConsent => "s27(1)(a)",
        SpecialDataCondition.LegalClaims => "s27(1)(b)",
        SpecialDataCondition.InternationalLawObligation => "s27(1)(c)",
        SpecialDataCondition.ArchivingOrResearch => "s27(1)(d)",
        SpecialDataCondition.MadePublicByTheSubject => "s27(1)(e)",
        SpecialDataCondition.SubstantialPublicInterest => "s27(2)",
        SpecialDataCondition.NotForProfitBody => "ss28-31",
        SpecialDataCondition.HealthOrSocialCare => "s32",
        SpecialDataCondition.EmploymentAndSocialSecurity => "s32",
        _ => null,
    };

    /// <inheritdoc />
    public override string? Cite(DataSubjectRequestType type) => type switch
    {
        DataSubjectRequestType.Access => "s23 (PAIA procedure)",
        DataSubjectRequestType.Rectification => "s24",
        DataSubjectRequestType.Erasure => "s24",
        DataSubjectRequestType.Restriction => "s14(6)",
        DataSubjectRequestType.Objection => "s11(3)",
        DataSubjectRequestType.AutomatedDecisionReview => "s71",
        _ => null,
    };

    /// <summary>
    /// Access requests: 30 days after receipt under PAIA, or 60 when extended once by up to 30 days, moved to the
    /// next working day. POPIA sets no fixed period for other requests, which must be dealt with as soon as
    /// reasonably practicable, so they return <c>null</c>.
    /// </summary>
    public override DateTime? RequestDeadline(DataSubjectRequestType type, DateTime receivedOn, bool extended = false, Func<DateTime, bool>? isNonWorkingDay = null)
    {
        if (type != DataSubjectRequestType.Access)
            return null;

        return NextWorkingDay(receivedOn.Date.AddDays(extended ? 60 : 30), isNonWorkingDay);
    }
}
