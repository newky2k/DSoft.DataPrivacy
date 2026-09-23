using System;

namespace DSoft.EntityFrameworkCore.GDPR;

/// <summary>
/// What kind of personal data a value is. Categories are flags: an email address is
/// <see cref="DirectIdentifier"/> | <see cref="Contact"/>, a clinical note is <see cref="Health"/> | <see cref="FreeText"/>.
/// </summary>
/// <remarks>
/// The categories are the ones a controller needs to act on: the Article 9 special categories and Article 10
/// criminal offence data carry extra conditions, identifiers decide what an erasure must remove, and
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

    /// <summary>Data that is, or may be, about a child (Article 8).</summary>
    Child = 1L << 10,

    /// <summary>Data about employment: contract, performance, absence, disciplinary records.</summary>
    Employment = 1L << 11,

    /// <summary>A photograph or video of a person. Only biometric when processed to identify someone (Recital 51).</summary>
    Image = 1L << 12,

    /// <summary>Special category: racial or ethnic origin (Article 9(1)).</summary>
    RacialOrEthnicOrigin = 1L << 20,

    /// <summary>Special category: political opinions (Article 9(1)).</summary>
    PoliticalOpinion = 1L << 21,

    /// <summary>Special category: religious or philosophical beliefs (Article 9(1)).</summary>
    ReligiousOrPhilosophicalBelief = 1L << 22,

    /// <summary>Special category: trade union membership (Article 9(1)).</summary>
    TradeUnionMembership = 1L << 23,

    /// <summary>Special category: genetic data (Article 9(1), Article 4(13)).</summary>
    Genetic = 1L << 24,

    /// <summary>Special category: biometric data processed to uniquely identify a person (Article 9(1), Article 4(14)).</summary>
    Biometric = 1L << 25,

    /// <summary>Special category: data concerning health, physical or mental (Article 9(1), Article 4(15)).</summary>
    Health = 1L << 26,

    /// <summary>Special category: data concerning a person's sex life or sexual orientation (Article 9(1)).</summary>
    SexLifeOrOrientation = 1L << 27,

    /// <summary>Criminal convictions and offences, or related security measures (Article 10).</summary>
    CriminalOffence = 1L << 32,

    /// <summary>Every Article 9 special category.</summary>
    SpecialCategory = RacialOrEthnicOrigin | PoliticalOpinion | ReligiousOrPhilosophicalBelief | TradeUnionMembership
        | Genetic | Biometric | Health | SexLifeOrOrientation,

    /// <summary>Identifiers a person can be recognised by, directly or indirectly.</summary>
    Identifier = DirectIdentifier | IndirectIdentifier | OnlineIdentifier,
}
