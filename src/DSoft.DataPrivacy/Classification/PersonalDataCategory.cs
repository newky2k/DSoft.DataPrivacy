using System;

namespace DSoft.DataPrivacy;

/// <summary>
/// What kind of personal data a value is. Categories are flags: an email address is
/// <see cref="DirectIdentifier"/> | <see cref="Contact"/>, a clinical note is <see cref="Health"/> | <see cref="FreeText"/>.
/// </summary>
/// <remarks>
/// The categories are the ones an organisation needs to act on: special data (GDPR Article 9, POPIA section 26)
/// and criminal offence data carry extra conditions, identifiers decide what an erasure must remove, and
/// <see cref="FreeText"/> marks values whose content cannot be known in advance and needs review before disclosure.
/// </remarks>
[Flags]
public enum PersonalDataCategory : long
{
    /// <summary>Not classified.</summary>
    None = 0,

    /// <summary>Identifies a person on its own: name, email address, national identifier, date of birth.</summary>
    DirectIdentifier = 1L << 0,

    /// <summary>Identifies a person when combined with other data: job title, postcode, internal reference numbers.</summary>
    IndirectIdentifier = 1L << 1,

    /// <summary>A way to reach a person: postal address, email address, phone number.</summary>
    Contact = 1L << 2,

    /// <summary>An online identifier: IP address, cookie or advertising identifier, device identifier (Recital 30).</summary>
    OnlineIdentifier = 1L << 3,

    /// <summary>Where a person is or was: coordinates, check-ins, travel history.</summary>
    Location = 1L << 4,

    /// <summary>Bank, card, salary, payment or credit data.</summary>
    Financial = 1L << 5,

    /// <summary>Secrets that authenticate a person: password hashes, tokens, recovery codes. Never exported.</summary>
    Credential = 1L << 6,

    /// <summary>Data recorded about behaviour or equipment use: usage events, telemetry, device readings.</summary>
    Telemetry = 1L << 7,

    /// <summary>Free text whose content is not known in advance and may hold anything, including other people's data.</summary>
    FreeText = 1L << 8,

    /// <summary>
    /// A copy of personal data kept to make a record self-describing: "created by" names, snapshots taken
    /// for an audit trail. It usually has to be retained with the record it describes.
    /// </summary>
    AuditCopy = 1L << 9,

    /// <summary>Data that is, or may be, about a child (GDPR Article 8, POPIA section 34).</summary>
    Child = 1L << 10,

    /// <summary>Data about employment: contract, performance, absence, disciplinary records.</summary>
    Employment = 1L << 11,

    /// <summary>A photograph or video of a person. Only biometric when processed to identify someone (Recital 51).</summary>
    Image = 1L << 12,

    /// <summary>Special category: racial or ethnic origin (GDPR Art 9(1), POPIA s26).</summary>
    RacialOrEthnicOrigin = 1L << 20,

    /// <summary>Special category: political opinions (GDPR Art 9(1), POPIA s26).</summary>
    PoliticalOpinion = 1L << 21,

    /// <summary>Special category: religious or philosophical beliefs (GDPR Art 9(1), POPIA s26).</summary>
    ReligiousOrPhilosophicalBelief = 1L << 22,

    /// <summary>Special category: trade union membership (GDPR Art 9(1), POPIA s26).</summary>
    TradeUnionMembership = 1L << 23,

    /// <summary>Special category: genetic data (GDPR Art 9(1); health information under POPIA s26).</summary>
    Genetic = 1L << 24,

    /// <summary>Special category: biometric data processed to uniquely identify a person (GDPR Art 9(1), POPIA s26).</summary>
    Biometric = 1L << 25,

    /// <summary>Special category: data concerning health, physical or mental (GDPR Art 9(1), POPIA s26).</summary>
    Health = 1L << 26,

    /// <summary>Special category: data concerning a person's sex life or sexual orientation (GDPR Art 9(1), POPIA s26).</summary>
    SexLifeOrOrientation = 1L << 27,

    /// <summary>Criminal convictions and offences, or related security measures (GDPR Article 10; special personal information under POPIA s26).</summary>
    CriminalOffence = 1L << 32,

    /// <summary>
    /// Every special category common to the GDPR (Article 9) and POPIA (section 26). POPIA also treats
    /// <see cref="CriminalOffence"/> as special: use <see cref="Regimes.PrivacyRegime.IsSpecial"/> for the law in force.
    /// </summary>
    SpecialCategory = RacialOrEthnicOrigin | PoliticalOpinion | ReligiousOrPhilosophicalBelief | TradeUnionMembership
        | Genetic | Biometric | Health | SexLifeOrOrientation,

    /// <summary>Identifiers a person can be recognised by, directly or indirectly.</summary>
    Identifier = DirectIdentifier | IndirectIdentifier | OnlineIdentifier,
}
