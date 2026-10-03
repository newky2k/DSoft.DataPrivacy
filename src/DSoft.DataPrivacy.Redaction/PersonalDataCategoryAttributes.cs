using System;

namespace DSoft.DataPrivacy.Redaction;

// One parameterless attribute per category: the [LoggerMessage] source generator creates a classification
// attribute with no arguments, so each classification needs a type of its own.

/// <summary>Classifies a logged value as data concerning health (<see cref="PersonalDataCategory.Health"/>). Erased from logs.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class HealthDataAttribute : PersonalDataClassificationAttribute
{
    /// <summary>Classifies the value as <see cref="PersonalDataCategory.Health"/>.</summary>
    public HealthDataAttribute()
        : base(PersonalDataCategory.Health)
    {
    }
}

/// <summary>Classifies a logged value as genetic data (<see cref="PersonalDataCategory.Genetic"/>). Erased from logs.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class GeneticDataAttribute : PersonalDataClassificationAttribute
{
    /// <summary>Classifies the value as <see cref="PersonalDataCategory.Genetic"/>.</summary>
    public GeneticDataAttribute()
        : base(PersonalDataCategory.Genetic)
    {
    }
}

/// <summary>Classifies a logged value as biometric data (<see cref="PersonalDataCategory.Biometric"/>). Erased from logs.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class BiometricDataAttribute : PersonalDataClassificationAttribute
{
    /// <summary>Classifies the value as <see cref="PersonalDataCategory.Biometric"/>.</summary>
    public BiometricDataAttribute()
        : base(PersonalDataCategory.Biometric)
    {
    }
}

/// <summary>Classifies a logged value as data concerning a person's sex life or sexual orientation (<see cref="PersonalDataCategory.SexLifeOrOrientation"/>). Erased from logs.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class SexLifeOrOrientationDataAttribute : PersonalDataClassificationAttribute
{
    /// <summary>Classifies the value as <see cref="PersonalDataCategory.SexLifeOrOrientation"/>.</summary>
    public SexLifeOrOrientationDataAttribute()
        : base(PersonalDataCategory.SexLifeOrOrientation)
    {
    }
}

/// <summary>Classifies a logged value as racial or ethnic origin (<see cref="PersonalDataCategory.RacialOrEthnicOrigin"/>). Erased from logs.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class RacialOrEthnicOriginDataAttribute : PersonalDataClassificationAttribute
{
    /// <summary>Classifies the value as <see cref="PersonalDataCategory.RacialOrEthnicOrigin"/>.</summary>
    public RacialOrEthnicOriginDataAttribute()
        : base(PersonalDataCategory.RacialOrEthnicOrigin)
    {
    }
}

/// <summary>Classifies a logged value as religious or philosophical beliefs (<see cref="PersonalDataCategory.ReligiousOrPhilosophicalBelief"/>). Erased from logs.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class ReligiousOrPhilosophicalBeliefDataAttribute : PersonalDataClassificationAttribute
{
    /// <summary>Classifies the value as <see cref="PersonalDataCategory.ReligiousOrPhilosophicalBelief"/>.</summary>
    public ReligiousOrPhilosophicalBeliefDataAttribute()
        : base(PersonalDataCategory.ReligiousOrPhilosophicalBelief)
    {
    }
}

/// <summary>Classifies a logged value as political opinions (<see cref="PersonalDataCategory.PoliticalOpinion"/>). Erased from logs.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class PoliticalOpinionDataAttribute : PersonalDataClassificationAttribute
{
    /// <summary>Classifies the value as <see cref="PersonalDataCategory.PoliticalOpinion"/>.</summary>
    public PoliticalOpinionDataAttribute()
        : base(PersonalDataCategory.PoliticalOpinion)
    {
    }
}

/// <summary>Classifies a logged value as trade union membership (<see cref="PersonalDataCategory.TradeUnionMembership"/>). Erased from logs.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class TradeUnionMembershipDataAttribute : PersonalDataClassificationAttribute
{
    /// <summary>Classifies the value as <see cref="PersonalDataCategory.TradeUnionMembership"/>.</summary>
    public TradeUnionMembershipDataAttribute()
        : base(PersonalDataCategory.TradeUnionMembership)
    {
    }
}

/// <summary>Classifies a logged value as criminal offence data (<see cref="PersonalDataCategory.CriminalOffence"/>). Erased from logs.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class CriminalOffenceDataAttribute : PersonalDataClassificationAttribute
{
    /// <summary>Classifies the value as <see cref="PersonalDataCategory.CriminalOffence"/>.</summary>
    public CriminalOffenceDataAttribute()
        : base(PersonalDataCategory.CriminalOffence)
    {
    }
}

/// <summary>Classifies a logged value as a credential (<see cref="PersonalDataCategory.Credential"/>). Erased from logs.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class CredentialDataAttribute : PersonalDataClassificationAttribute
{
    /// <summary>Classifies the value as <see cref="PersonalDataCategory.Credential"/>.</summary>
    public CredentialDataAttribute()
        : base(PersonalDataCategory.Credential)
    {
    }
}

/// <summary>Classifies a logged value as a child's data (<see cref="PersonalDataCategory.Child"/>). Erased from logs.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class ChildDataAttribute : PersonalDataClassificationAttribute
{
    /// <summary>Classifies the value as <see cref="PersonalDataCategory.Child"/>.</summary>
    public ChildDataAttribute()
        : base(PersonalDataCategory.Child)
    {
    }
}

/// <summary>Classifies a logged value as financial data (<see cref="PersonalDataCategory.Financial"/>). Erased from logs.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class FinancialDataAttribute : PersonalDataClassificationAttribute
{
    /// <summary>Classifies the value as <see cref="PersonalDataCategory.Financial"/>.</summary>
    public FinancialDataAttribute()
        : base(PersonalDataCategory.Financial)
    {
    }
}

/// <summary>Classifies a logged value as free text (<see cref="PersonalDataCategory.FreeText"/>). Erased from logs.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class FreeTextDataAttribute : PersonalDataClassificationAttribute
{
    /// <summary>Classifies the value as <see cref="PersonalDataCategory.FreeText"/>.</summary>
    public FreeTextDataAttribute()
        : base(PersonalDataCategory.FreeText)
    {
    }
}

/// <summary>Classifies a logged value as an image of a person (<see cref="PersonalDataCategory.Image"/>). Hashed in logs when a key is configured, and erased otherwise.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class ImageDataAttribute : PersonalDataClassificationAttribute
{
    /// <summary>Classifies the value as <see cref="PersonalDataCategory.Image"/>.</summary>
    public ImageDataAttribute()
        : base(PersonalDataCategory.Image)
    {
    }
}

/// <summary>Classifies a logged value as a direct identifier (<see cref="PersonalDataCategory.DirectIdentifier"/>). Hashed in logs when a key is configured, and erased otherwise.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class DirectIdentifierDataAttribute : PersonalDataClassificationAttribute
{
    /// <summary>Classifies the value as <see cref="PersonalDataCategory.DirectIdentifier"/>.</summary>
    public DirectIdentifierDataAttribute()
        : base(PersonalDataCategory.DirectIdentifier)
    {
    }
}

/// <summary>Classifies a logged value as contact data (<see cref="PersonalDataCategory.Contact"/>). Hashed in logs when a key is configured, and erased otherwise.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class ContactDataAttribute : PersonalDataClassificationAttribute
{
    /// <summary>Classifies the value as <see cref="PersonalDataCategory.Contact"/>.</summary>
    public ContactDataAttribute()
        : base(PersonalDataCategory.Contact)
    {
    }
}

/// <summary>Classifies a logged value as location data (<see cref="PersonalDataCategory.Location"/>). Hashed in logs when a key is configured, and erased otherwise.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class LocationDataAttribute : PersonalDataClassificationAttribute
{
    /// <summary>Classifies the value as <see cref="PersonalDataCategory.Location"/>.</summary>
    public LocationDataAttribute()
        : base(PersonalDataCategory.Location)
    {
    }
}

/// <summary>Classifies a logged value as an online identifier (<see cref="PersonalDataCategory.OnlineIdentifier"/>). Hashed in logs when a key is configured, and erased otherwise.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class OnlineIdentifierDataAttribute : PersonalDataClassificationAttribute
{
    /// <summary>Classifies the value as <see cref="PersonalDataCategory.OnlineIdentifier"/>.</summary>
    public OnlineIdentifierDataAttribute()
        : base(PersonalDataCategory.OnlineIdentifier)
    {
    }
}

/// <summary>Classifies a logged value as employment data (<see cref="PersonalDataCategory.Employment"/>). Hashed in logs when a key is configured, and erased otherwise.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class EmploymentDataAttribute : PersonalDataClassificationAttribute
{
    /// <summary>Classifies the value as <see cref="PersonalDataCategory.Employment"/>.</summary>
    public EmploymentDataAttribute()
        : base(PersonalDataCategory.Employment)
    {
    }
}

/// <summary>Classifies a logged value as an indirect identifier (<see cref="PersonalDataCategory.IndirectIdentifier"/>). Hashed in logs when a key is configured, and erased otherwise.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class IndirectIdentifierDataAttribute : PersonalDataClassificationAttribute
{
    /// <summary>Classifies the value as <see cref="PersonalDataCategory.IndirectIdentifier"/>.</summary>
    public IndirectIdentifierDataAttribute()
        : base(PersonalDataCategory.IndirectIdentifier)
    {
    }
}

/// <summary>Classifies a logged value as telemetry (<see cref="PersonalDataCategory.Telemetry"/>). Hashed in logs when a key is configured, and erased otherwise.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class TelemetryDataAttribute : PersonalDataClassificationAttribute
{
    /// <summary>Classifies the value as <see cref="PersonalDataCategory.Telemetry"/>.</summary>
    public TelemetryDataAttribute()
        : base(PersonalDataCategory.Telemetry)
    {
    }
}

/// <summary>Classifies a logged value as an audit copy of personal data (<see cref="PersonalDataCategory.AuditCopy"/>). Hashed in logs when a key is configured, and erased otherwise.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class AuditCopyDataAttribute : PersonalDataClassificationAttribute
{
    /// <summary>Classifies the value as <see cref="PersonalDataCategory.AuditCopy"/>.</summary>
    public AuditCopyDataAttribute()
        : base(PersonalDataCategory.AuditCopy)
    {
    }
}
