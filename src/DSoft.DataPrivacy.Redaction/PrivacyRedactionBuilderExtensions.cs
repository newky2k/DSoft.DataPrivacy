using System;
using Microsoft.Extensions.Compliance.Redaction;

namespace DSoft.DataPrivacy.Redaction;

/// <summary>Registers redactors for personal data classified with <see cref="PersonalDataTaxonomy"/>.</summary>
public static class PrivacyRedactionBuilderExtensions
{
    /// <summary>
    /// Erases high-risk personal data (special categories, criminal offence data, credentials, free text, children's
    /// and financial data) from logs. Other identifying data is replaced with a keyed hash when
    /// <paramref name="configureHmac"/> is given, so log lines about one person can still be correlated, and erased
    /// otherwise.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddRedaction(redaction => redaction.AddPersonalDataRedactors(hmac =>
    /// {
    ///     hmac.KeyId = 1;
    ///     hmac.Key = configuration["Logging:RedactionKey"];
    /// }));
    /// </code>
    /// </example>
    public static IRedactionBuilder AddPersonalDataRedactors(this IRedactionBuilder builder, Action<HmacRedactorOptions>? configureHmac = null)
    {
        if (builder == null)
            throw new ArgumentNullException(nameof(builder));

        builder.SetRedactor<ErasingRedactor>(PersonalDataTaxonomy.Erased);

        if (configureHmac == null)
        {
            builder.SetRedactor<ErasingRedactor>(PersonalDataTaxonomy.Identifying);
        }
        else
        {
#pragma warning disable EXTEXP0002 // HMAC redaction is marked experimental by Microsoft.Extensions.Compliance.Redaction.
            builder.SetHmacRedactor(configureHmac, PersonalDataTaxonomy.Identifying);
#pragma warning restore EXTEXP0002
        }

        return builder;
    }
}
