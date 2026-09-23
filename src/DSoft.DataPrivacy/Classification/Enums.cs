namespace DSoft.DataPrivacy;

/// <summary>What happens to a record, or a value, when the person it is about is erased.</summary>
public enum ErasureAction
{
    /// <summary>Let the erasure rules decide.</summary>
    Default = 0,

    /// <summary>Delete the record.</summary>
    Delete = 1,

    /// <summary>Keep the record but replace its personal data with anonymised values.</summary>
    Anonymise = 2,

    /// <summary>Keep the record and its personal data, relying on an <see cref="RetentionGround"/>.</summary>
    Retain = 3,
}

/// <summary>How a single value is anonymised.</summary>
public enum AnonymisationMethod
{
    /// <summary>
    /// Choose by type: <see cref="Null"/> for nullable values, <see cref="Redact"/> for required text,
    /// <see cref="Generalise"/> for required dates, and the type's default for anything else.
    /// </summary>
    Default = 0,

    /// <summary>Set to <c>null</c>, or to the type's default when the value is not nullable.</summary>
    Null = 1,

    /// <summary>Replace text with a fixed marker such as <c>[erased]</c>, cut to the column length.</summary>
    Redact = 2,

    /// <summary>
    /// Replace with a keyed hash (HMAC-SHA256) of the value. The same input always gives the same hash, so a
    /// suppression list can still recognise an erased email address without holding it.
    /// </summary>
    Hash = 3,

    /// <summary>Reduce precision: a date becomes the first of January of its year.</summary>
    Generalise = 4,

    /// <summary>Use a named anonymiser registered with the application.</summary>
    Custom = 5,
}

/// <summary>
/// Why data is kept after a request to delete it, or past its retention period. Record the ground whenever data is
/// retained, so the organisation can show why. The grounds are neutral; whether a ground is available, and the
/// provision it rests on, depends on the <see cref="Regimes.PrivacyRegime"/> in force. For example
/// <see cref="Consent"/> is a POPIA ground (s14(1)(d)) but not a GDPR one.
/// </summary>
public enum RetentionGround
{
    /// <summary>No ground.</summary>
    None = 0,

    /// <summary>Freedom of expression and information, or journalistic, literary or artistic purposes.</summary>
    FreedomOfExpression = 1,

    /// <summary>
    /// Keeping the data is required or authorised by law. Covers statutory record keeping such as accounting and
    /// tax records.
    /// </summary>
    LegalObligation = 2,

    /// <summary>
    /// Health or social care records a professional or institution must keep, including under public health law.
    /// </summary>
    HealthOrSocialCare = 3,

    /// <summary>Archiving in the public interest, historical or scientific research, or statistics, with safeguards.</summary>
    ArchivingOrResearch = 4,

    /// <summary>Establishing, exercising or defending legal claims. Covers a legal hold.</summary>
    LegalClaims = 5,

    /// <summary>Performing a task in the public interest or under official authority, or a public body's legal duties.</summary>
    PublicTask = 6,

    /// <summary>The data is needed for a contract between the organisation and the person.</summary>
    Contract = 7,

    /// <summary>The person has consented to the data being kept.</summary>
    Consent = 8,

    /// <summary>The data is reasonably required for a lawful purpose related to the organisation's functions or activities.</summary>
    OperationalNeed = 9,
}

/// <summary>How a record relates to the data subject it points at.</summary>
public enum DataSubjectLinkKind
{
    /// <summary>
    /// Work it out from the relationship: a required relationship that cascades on delete is
    /// <see cref="Owner"/>, anything else is <see cref="Reference"/>.
    /// </summary>
    Default = 0,

    /// <summary>
    /// The record is about the person: their address, their order, their consent. It is exported in full
    /// and erased with them.
    /// </summary>
    Owner = 1,

    /// <summary>
    /// The record mentions the person: "created by", "assigned to", "approved by". It is listed in an export
    /// but not erased; the person it points at is anonymised in place so the reference stays valid.
    /// </summary>
    Reference = 2,

    /// <summary>The relationship does not make the record personal data about the person.</summary>
    None = 3,
}

/// <summary>The event a retention period is counted from.</summary>
public enum RetentionTrigger
{
    /// <summary>When the record was created.</summary>
    CreatedAt = 0,

    /// <summary>The last time the person was active, or the record was used.</summary>
    LastActivity = 1,

    /// <summary>When the relationship with the person ended: an account closed, a contract ended, an employee left.</summary>
    RelationshipEnded = 2,

    /// <summary>When the record itself was closed: a case resolved, an order completed.</summary>
    RecordClosed = 3,
}

/// <summary>
/// The grounds on which personal data may be processed at all: GDPR Article 6(1), POPIA section 11(1). The values
/// are neutral; <see cref="Regimes.PrivacyRegime.Cite(LawfulBasis)"/> gives the provision under each law.
/// </summary>
public enum LawfulBasis
{
    /// <summary>Not yet decided. Every purpose needs one before processing starts.</summary>
    Undecided = 0,

    /// <summary>The person has consented.</summary>
    Consent = 1,

    /// <summary>Necessary for a contract with the person, or for steps before entering one.</summary>
    Contract = 2,

    /// <summary>Necessary to comply with an obligation imposed by law.</summary>
    LegalObligation = 3,

    /// <summary>Necessary to protect someone's life.</summary>
    VitalInterests = 4,

    /// <summary>Necessary for a task in the public interest, official authority or a public body's legal duty.</summary>
    PublicTask = 5,

    /// <summary>Necessary for the legitimate interests of the organisation or a third party.</summary>
    LegitimateInterests = 6,

    /// <summary>Protects a legitimate interest of the person themselves (POPIA s11(1)(d)).</summary>
    SubjectLegitimateInterest = 7,
}

/// <summary>
/// The extra conditions for processing special data: GDPR Article 9(2), POPIA sections 27 to 33. One is needed in
/// addition to a <see cref="LawfulBasis"/>. <see cref="Regimes.PrivacyRegime.Cite(SpecialDataCondition)"/> gives
/// the provision under each law.
/// </summary>
public enum SpecialDataCondition
{
    /// <summary>No special data is processed, or the condition is not yet decided.</summary>
    None = 0,

    /// <summary>Explicit consent.</summary>
    ExplicitConsent = 1,

    /// <summary>Employment, social security, social protection, pensions or insurance.</summary>
    EmploymentAndSocialSecurity = 2,

    /// <summary>Vital interests where the person cannot consent.</summary>
    VitalInterests = 3,

    /// <summary>A not-for-profit, religious, political or trade union body, about its members.</summary>
    NotForProfitBody = 4,

    /// <summary>The person deliberately made the data public.</summary>
    MadePublicByTheSubject = 5,

    /// <summary>Establishing, exercising or defending a right, obligation or claim in law.</summary>
    LegalClaims = 6,

    /// <summary>Substantial public interest.</summary>
    SubstantialPublicInterest = 7,

    /// <summary>Preventive or occupational medicine, diagnosis, or health or social care.</summary>
    HealthOrSocialCare = 8,

    /// <summary>Public health.</summary>
    PublicHealth = 9,

    /// <summary>Archiving, research or statistics.</summary>
    ArchivingOrResearch = 10,

    /// <summary>Complying with an obligation of international public law (POPIA s27(1)(c)).</summary>
    InternationalLawObligation = 11,
}

/// <summary>
/// The rights a data subject can exercise. Not every law has every right: POPIA has no portability right, for
/// example. <see cref="Regimes.PrivacyRegime.RequestTypes"/> lists the ones a law provides.
/// </summary>
public enum DataSubjectRequestType
{
    /// <summary>Access to the person's own data (a subject access request).</summary>
    Access = 1,

    /// <summary>Rectification, or correction, of inaccurate data.</summary>
    Rectification = 2,

    /// <summary>Erasure, or deletion or destruction, of data.</summary>
    Erasure = 3,

    /// <summary>Restriction of processing.</summary>
    Restriction = 4,

    /// <summary>Data portability.</summary>
    Portability = 5,

    /// <summary>Objection to processing.</summary>
    Objection = 6,

    /// <summary>Rights about decisions made only by automated means, including profiling.</summary>
    AutomatedDecisionReview = 7,
}
