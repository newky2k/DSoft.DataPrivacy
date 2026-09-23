using System;

namespace DSoft.DataPrivacy.Regimes;

/// <summary>
/// The General Data Protection Regulation: Regulation (EU) 2016/679, and the UK GDPR as retained in UK law with the
/// Data Protection Act 2018. The two use the same article numbers for everything this library relies on.
/// </summary>
public sealed class GdprRegime : PrivacyRegime
{
    internal GdprRegime()
        : base("gdpr", "GDPR", "General Data Protection Regulation (EU) 2016/679, and the UK GDPR with the Data Protection Act 2018")
    {
    }

    /// <inheritdoc />
    public override string ControllerTerm => "controller";

    /// <inheritdoc />
    public override string ProcessorTerm => "processor";

    /// <inheritdoc />
    public override string SpecialDataTerm => "special category data";

    /// <summary>The Article 9(1) categories. Criminal offence data is governed separately by Article 10.</summary>
    public override PersonalDataCategory SpecialCategories => PersonalDataCategory.SpecialCategory;

    /// <inheritdoc />
    public override TimeSpan? BreachNotificationWindow => TimeSpan.FromHours(72);

    /// <inheritdoc />
    public override string BreachNotificationRule
        => "Notify the supervisory authority without undue delay and, where feasible, within 72 hours of becoming aware of the breach (Art 33); tell the people affected without undue delay when the risk to them is high (Art 34).";

    /// <inheritdoc />
    public override string? Cite(RetentionGround ground) => ground switch
    {
        RetentionGround.FreedomOfExpression => "Art 17(3)(a)",
        RetentionGround.LegalObligation => "Art 17(3)(b)",
        RetentionGround.PublicTask => "Art 17(3)(b)",
        RetentionGround.HealthOrSocialCare => "Art 17(3)(c)",
        RetentionGround.ArchivingOrResearch => "Art 17(3)(d)",
        RetentionGround.LegalClaims => "Art 17(3)(e)",
        _ => null,
    };

    /// <inheritdoc />
    public override string? Cite(LawfulBasis basis) => basis switch
    {
        LawfulBasis.Consent => "Art 6(1)(a)",
        LawfulBasis.Contract => "Art 6(1)(b)",
        LawfulBasis.LegalObligation => "Art 6(1)(c)",
        LawfulBasis.VitalInterests => "Art 6(1)(d)",
        LawfulBasis.PublicTask => "Art 6(1)(e)",
        LawfulBasis.LegitimateInterests => "Art 6(1)(f)",
        _ => null,
    };

    /// <inheritdoc />
    public override string? Cite(SpecialDataCondition condition) => condition switch
    {
        SpecialDataCondition.ExplicitConsent => "Art 9(2)(a)",
        SpecialDataCondition.EmploymentAndSocialSecurity => "Art 9(2)(b)",
        SpecialDataCondition.VitalInterests => "Art 9(2)(c)",
        SpecialDataCondition.NotForProfitBody => "Art 9(2)(d)",
        SpecialDataCondition.MadePublicByTheSubject => "Art 9(2)(e)",
        SpecialDataCondition.LegalClaims => "Art 9(2)(f)",
        SpecialDataCondition.SubstantialPublicInterest => "Art 9(2)(g)",
        SpecialDataCondition.HealthOrSocialCare => "Art 9(2)(h)",
        SpecialDataCondition.PublicHealth => "Art 9(2)(i)",
        SpecialDataCondition.ArchivingOrResearch => "Art 9(2)(j)",
        _ => null,
    };

    /// <inheritdoc />
    public override string? Cite(DataSubjectRequestType type) => type switch
    {
        DataSubjectRequestType.Access => "Art 15",
        DataSubjectRequestType.Rectification => "Art 16",
        DataSubjectRequestType.Erasure => "Art 17",
        DataSubjectRequestType.Restriction => "Art 18",
        DataSubjectRequestType.Portability => "Art 20",
        DataSubjectRequestType.Objection => "Art 21",
        DataSubjectRequestType.AutomatedDecisionReview => "Art 22",
        _ => null,
    };

    /// <summary>
    /// One month from receipt for every right (Art 12(3)), or three months when extended for complex or numerous
    /// requests. Follows the UK regulator's method: the day of receipt is day one, the corresponding date next
    /// month, the last day of the month when there is no such date, then the next working day.
    /// </summary>
    public override DateTime? RequestDeadline(DataSubjectRequestType type, DateTime receivedOn, bool extended = false, Func<DateTime, bool>? isNonWorkingDay = null)
    {
        if (Cite(type) == null)
            return null;

        // DateTime.AddMonths already clamps 31 January to the last day of February.
        return NextWorkingDay(receivedOn.Date.AddMonths(extended ? 3 : 1), isNonWorkingDay);
    }
}
