using System;
using System.Collections.Generic;

namespace DSoft.EntityFrameworkCore.GDPR.Rules;

/// <summary>Kinds of processing an application performs, for deciding what is allowed while processing is restricted.</summary>
public enum ProcessingOperation
{
    /// <summary>Keeping the data.</summary>
    Storage = 0,

    /// <summary>Giving the person their own data, in answer to an access or portability request.</summary>
    SubjectAccess = 1,

    /// <summary>Correcting the data, in answer to a rectification request.</summary>
    Rectification = 2,

    /// <summary>Establishing, exercising or defending legal claims.</summary>
    LegalClaims = 3,

    /// <summary>Protecting the rights of another natural or legal person.</summary>
    ProtectionOfAnotherPerson = 4,

    /// <summary>Reasons of important public interest.</summary>
    ImportantPublicInterest = 5,

    /// <summary>Everyday use of the data in the service.</summary>
    ServiceDelivery = 6,

    /// <summary>Sending marketing.</summary>
    Marketing = 7,

    /// <summary>Profiling or automated decision making.</summary>
    Profiling = 8,

    /// <summary>Reports, dashboards and analytics.</summary>
    Reporting = 9,

    /// <summary>Bulk export, data transfer or disclosure to a third party.</summary>
    Disclosure = 10,
}

/// <summary>
/// Decides whether an operation may run on a person's data while their processing is restricted (Article 18).
/// Apart from storage, restricted data may only be processed with the person's consent, for legal claims, to
/// protect another person's rights, or for important public interest (Article 18(2)).
/// </summary>
/// <remarks>
/// Answering the person's own access or rectification request is permitted: it is done at their request and is
/// how a dispute that led to the restriction is resolved. Tell the person before a restriction is lifted (Article 18(3)).
/// </remarks>
public sealed class ProcessingRestrictionPolicy
{
    private readonly HashSet<ProcessingOperation> _permitted;

    /// <summary>A policy permitting only what Article 18(2) permits.</summary>
    public ProcessingRestrictionPolicy()
    {
        _permitted = new HashSet<ProcessingOperation>
        {
            ProcessingOperation.Storage,
            ProcessingOperation.SubjectAccess,
            ProcessingOperation.Rectification,
            ProcessingOperation.LegalClaims,
            ProcessingOperation.ProtectionOfAnotherPerson,
            ProcessingOperation.ImportantPublicInterest,
        };
    }

    /// <summary>The Article 18(2) policy.</summary>
    public static ProcessingRestrictionPolicy Default { get; } = new();

    /// <summary>Operations permitted while restricted, without consent.</summary>
    public IReadOnlyCollection<ProcessingOperation> Permitted => _permitted;

    /// <summary>
    /// Also permits <paramref name="operation"/> while restricted. Only do this where one of the Article 18(2)
    /// grounds applies to it, for example direct care that protects the person's own vital interests.
    /// </summary>
    public ProcessingRestrictionPolicy Permit(ProcessingOperation operation)
    {
        if (ReferenceEquals(this, Default))
            throw new InvalidOperationException("The default policy cannot be changed; create a new ProcessingRestrictionPolicy.");

        _permitted.Add(operation);
        return this;
    }

    /// <summary>True when <paramref name="operation"/> may run.</summary>
    /// <param name="operation">What the application wants to do.</param>
    /// <param name="isRestricted">Whether the person's processing is currently restricted.</param>
    /// <param name="hasConsent">Whether the person has consented to this operation during the restriction.</param>
    public bool IsPermitted(ProcessingOperation operation, bool isRestricted, bool hasConsent = false)
        => !isRestricted || hasConsent || _permitted.Contains(operation);
}
