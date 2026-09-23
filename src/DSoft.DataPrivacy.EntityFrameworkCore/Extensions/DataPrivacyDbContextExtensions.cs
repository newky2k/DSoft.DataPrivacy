using System;
using DSoft.DataPrivacy;

namespace Microsoft.EntityFrameworkCore;

/// <summary>Entry point to the personal data operations on a context.</summary>
public static class DataPrivacyDbContextExtensions
{
    /// <summary>
    /// The personal data operations for this context: the model, subject access export, erasure, retention and
    /// validation. The context must be configured with <c>UseDataPrivacy()</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// var export = await db.PersonalData().ExportAsync&lt;Customer&gt;(customerId);
    /// var result = await db.PersonalData().EraseAsync&lt;Customer&gt;(customerId);
    /// </code>
    /// </example>
    public static PersonalDataOperations PersonalData(this DbContext context)
        => new(context ?? throw new ArgumentNullException(nameof(context)));
}
