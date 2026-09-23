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

    /// <summary>Keep the record and its personal data, relying on an <see cref="ErasureExemption"/>.</summary>
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
/// The Article 17(3) grounds on which data may be kept after a valid erasure request. Record the ground whenever
/// data is retained, so the controller can show why.
/// </summary>
public enum ErasureExemption
{
    /// <summary>No exemption.</summary>
    None = 0,

    /// <summary>Article 17(3)(a): exercising the right of freedom of expression and information.</summary>
    FreedomOfExpression = 1,

    /// <summary>
    /// Article 17(3)(b): compliance with a legal obligation, or a task carried out in the public interest or in
    /// the exercise of official authority. Covers statutory record keeping such as accounting and tax records.
    /// </summary>
    LegalObligation = 2,

    /// <summary>
    /// Article 17(3)(c): reasons of public interest in the area of public health, in line with Article 9(2)(h)
    /// and (i) and Article 9(3). Covers health and social care records a professional is required to keep.
    /// </summary>
    PublicHealth = 3,

    /// <summary>Article 17(3)(d): archiving in the public interest, scientific or historical research, or statistics.</summary>
    ArchivingOrResearch = 4,

    /// <summary>Article 17(3)(e): establishing, exercising or defending legal claims. Covers a legal hold.</summary>
    LegalClaims = 5,
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

/// <summary>The Article 6(1) lawful bases for processing personal data.</summary>
public enum LawfulBasis
{
    /// <summary>Not yet decided. Every purpose needs one before processing starts.</summary>
    Undecided = 0,

    /// <summary>Article 6(1)(a): the person has given consent.</summary>
    Consent = 1,

    /// <summary>Article 6(1)(b): necessary for a contract with the person, or steps before entering one.</summary>
    Contract = 2,

    /// <summary>Article 6(1)(c): necessary to comply with a legal obligation.</summary>
    LegalObligation = 3,

    /// <summary>Article 6(1)(d): necessary to protect someone's life.</summary>
    VitalInterests = 4,

    /// <summary>Article 6(1)(e): necessary for a task in the public interest or official authority.</summary>
    PublicTask = 5,

    /// <summary>Article 6(1)(f): necessary for legitimate interests not overridden by the person's interests.</summary>
    LegitimateInterests = 6,
}

/// <summary>The Article 9(2) conditions for processing special category data. One is needed in addition to a <see cref="LawfulBasis"/>.</summary>
public enum SpecialCategoryCondition
{
    /// <summary>No special category data is processed, or the condition is not yet decided.</summary>
    None = 0,

    /// <summary>Article 9(2)(a): explicit consent.</summary>
    ExplicitConsent = 1,

    /// <summary>Article 9(2)(b): employment, social security and social protection law.</summary>
    EmploymentAndSocialSecurity = 2,

    /// <summary>Article 9(2)(c): vital interests where the person cannot consent.</summary>
    VitalInterests = 3,

    /// <summary>Article 9(2)(d): a not-for-profit body, about its members.</summary>
    NotForProfitBody = 4,

    /// <summary>Article 9(2)(e): data manifestly made public by the person.</summary>
    MadePublicByTheSubject = 5,

    /// <summary>Article 9(2)(f): legal claims or judicial acts.</summary>
    LegalClaims = 6,

    /// <summary>Article 9(2)(g): substantial public interest.</summary>
    SubstantialPublicInterest = 7,

    /// <summary>Article 9(2)(h): preventive or occupational medicine, medical diagnosis, health or social care.</summary>
    HealthOrSocialCare = 8,

    /// <summary>Article 9(2)(i): public health.</summary>
    PublicHealth = 9,

    /// <summary>Article 9(2)(j): archiving, research or statistics.</summary>
    ArchivingOrResearch = 10,
}

/// <summary>The rights a data subject can exercise (Articles 15 to 22).</summary>
public enum DataSubjectRequestType
{
    /// <summary>Article 15: right of access (a subject access request).</summary>
    Access = 1,

    /// <summary>Article 16: right to rectification.</summary>
    Rectification = 2,

    /// <summary>Article 17: right to erasure.</summary>
    Erasure = 3,

    /// <summary>Article 18: right to restriction of processing.</summary>
    Restriction = 4,

    /// <summary>Article 20: right to data portability.</summary>
    Portability = 5,

    /// <summary>Article 21: right to object.</summary>
    Objection = 6,

    /// <summary>Article 22: rights related to automated decision making, including profiling.</summary>
    AutomatedDecisionReview = 7,
}
